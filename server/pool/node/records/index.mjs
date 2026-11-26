// Hearthpulse's own numbers from game records, Node service only (TASK-2026-11-26-pro-collection A0, contracts K0-K4,
// docs/analytics-records.md). refresh() is the pass: it reads the records of the last 90 days once, runs the derivers,
// hands every record to each view in every window it belongs to and publishes all views in one transaction, outside
// HTTP. answer() serves a view from its published snapshot, parsed once per build. A deletion of records withdraws
// every view in its own batch (src/events.js), so the route answers "rebuilding" until the next pass.
//
// Storage: `snap` rows "records:<view>" (last_row = records counted, built = publication time, body = the snapshot or
// '' once withdrawn) and one state row "records" (the last pass: its count, time, signature and failed views). No
// migration: the rows appear with the first pass.
import { createHash } from 'node:crypto';
import { RECORD_VIEWS } from '../../src/events.js';
import { thresholds as analyticsThresholds } from '../analytics.mjs';
import { PATCH_ID, UNKNOWN, WINDOWS, latestPatch, windowsOf } from './periods.mjs';
import { scan } from './scan.mjs';
import { createStats } from './stats.mjs';

const DAY = 86_400_000;
/** After a build, new records wait this long for the next one; a deletion, a new day or a new version do not wait. */
export const REBUILD_AFTER = 30 * 60_000;
const STATE = 'records';
const rowOf = key => RECORD_VIEWS + key;
const LIKE = RECORD_VIEWS + '%';
// 32 lowercase hex characters or more: where an install id or a record id would show if a view published one.
const HEX_RUN = /[0-9a-f]{32,}/g;

const memos = new WeakMap();
/** What the process keeps per database: the record count the last check saw, and the parsed snapshots by view. */
function memo(db) {
  let m = memos.get(db);
  if (!m) memos.set(db, m = { seen: undefined, parsed: new Map() });
  return m;
}

/** The pass's thresholds: those of the analytics snapshot (ANALYTICS_MIN_SOURCES / _CELL, by default 5 and 10). */
export function recordThresholds() {
  const t = analyticsThresholds();
  return { minInstalls: t.minSources, minGames: t.minCell };
}

const versions = derivers => Object.fromEntries(derivers.map(d => [d.name, d.version]));

/** Changes with any view or deriver version, the patches file or the thresholds: a new signature rebuilds at once. */
function signature(modules, patches, thresholds) {
  const text = JSON.stringify({
    views: modules.views.map(v => [v.key, v.metric, v.rules, v.line, v.modes]),
    derivers: modules.derivers.map(d => [d.name, d.order, d.version]),
    patches, thresholds,
  });
  return createHash('sha256').update(text).digest('hex').slice(0, 16);
}

function readState(db) {
  const r = db.prepare('SELECT last_row, built, body FROM snap WHERE mode = ?').get(STATE);
  if (!r) return null;
  let meta = {};
  try { meta = JSON.parse(r.body); } catch { /* an unreadable state rebuilds: its signature no longer matches */ }
  return { count: r.last_row, built: r.built, sig: meta.sig ?? null, day: meta.day ?? null, failed: Array.isArray(meta.failed) ? meta.failed : [] };
}

/** The view rows by key, without their bodies: {last_row, built, size}; size 0 is a withdrawn view. */
function viewRows(db) {
  const rows = db.prepare('SELECT mode, last_row, built, octet_length(body) AS size FROM snap WHERE mode LIKE ?').all(LIKE);
  return new Map(rows.map(r => [r.mode.slice(RECORD_VIEWS.length), r]));
}

/**
 * Whether a pass is due: nothing built yet, a new signature or day, a withdrawn or missing view, or new records
 * REBUILD_AFTER after the last pass. A view that failed in the last pass is no reason by itself: it waits for a pass due
 * for one of the others, so a broken view costs one pass in REBUILD_AFTER, not one a minute.
 */
function due(state, rows, { sig, today, now, count, views }) {
  if (!state || state.sig !== sig || state.day !== today) return true;
  if (count !== state.count && now - state.built >= REBUILD_AFTER) return true;
  const failed = new Set(state.failed);
  return views.some(v => { const r = rows.get(v.key); return (!r || r.size === 0) && !failed.has(v.key); });
}

/** Which id of the pass a serialized snapshot holds: 'install_id', 'record_id' or null. Such a view is not published. */
function leak(text, iids, recordIds) {
  for (const [run] of text.matchAll(HEX_RUN)) {
    for (let i = 0; i + 32 <= run.length; i++) {
      const part = run.slice(i, i + 32);
      if (iids.has(part)) return 'install_id';
      if (recordIds.has(part)) return 'record_id';
    }
  }
  return null;
}

/** Runs every view over the records of the pass; returns the snapshots to publish and the views that failed. */
function build(db, { modules, patches, today, now, thresholds, log }) {
  const stats = createStats(thresholds);
  const derivers = versions(modules.derivers);
  const fail = (run, stage) => {
    if (run.failed) return;
    run.failed = true;
    log(JSON.stringify({ ok: false, error: 'records_view_failed', view: run.view.key, stage }));
  };
  const runs = modules.views.map(view => {
    const run = { view, modes: new Set(view.modes), failed: false, windows: [] };
    for (const w of windowsOf(patches, view.line, today)) {
      const window = Object.freeze({ line: view.line, patch: w.patch, window: w.window, fromDay: w.fromDay, toDay: w.toDay });
      run.windows.push({ w, window, ctx: Object.freeze({ today, now, window, thresholds, stats }), acc: undefined, low: null, high: null });
    }
    try { for (const x of run.windows) x.acc = view.start(x.ctx); } catch { fail(run, 'start'); }
    return run;
  });

  const modes = [...new Set(modules.views.flatMap(v => v.modes))];
  const counts = { read: 0, skipped: 0 };
  const iids = new Set();
  const recordIds = new Set();
  const deriveFailures = new Map();
  const deriveCtx = Object.freeze({ today, now });
  for (const rec of scan(db, { today, patches, modes, counts })) {
    const takers = runs.filter(r => !r.failed && r.view.line === rec.line && r.modes.has(rec.mode));
    if (takers.length === 0) continue;
    iids.add(rec.iid);
    recordIds.add(rec.body.id);
    for (const d of modules.derivers) {
      try { d.derive(rec, deriveCtx); } catch { deriveFailures.set(d.name, (deriveFailures.get(d.name) ?? 0) + 1); }
    }
    for (const run of takers) {
      try {
        for (const x of run.windows) {
          if (!x.w.has(rec)) continue;
          run.view.add(x.acc, rec, x.ctx);
          if (rec.build !== null) {
            x.low = x.low === null ? rec.build : Math.min(x.low, rec.build);
            x.high = x.high === null ? rec.build : Math.max(x.high, rec.build);
          }
        }
      } catch { fail(run, 'add'); }
    }
  }
  // Derivers must not throw (unknown is null); one that does is a bug, reported once a pass with its count.
  for (const [deriver, records] of deriveFailures) log(JSON.stringify({ ok: false, error: 'records_derive_failed', deriver, records }));

  const published = [];
  for (const run of runs) {
    if (run.failed) continue;
    try {
      const windows = run.windows.map(x => ({
        patch: x.w.patch, window: x.w.window,
        period: { line: run.view.line, patch: x.w.patch, builds: x.low === null ? null : [x.low, x.high], window: x.w.window, fromDay: x.w.fromDay, toDay: x.w.toDay },
        body: run.view.finish(x.acc, x.ctx) ?? null,
      }));
      const text = JSON.stringify({ v: 1, view: run.view.key, metric: run.view.metric, rules: run.view.rules, derivers, thresholds,
        day: today, latest: latestPatch(patches, run.view.line), windows });
      const found = leak(text, iids, recordIds);
      if (found) fail(run, found);
      else published.push({ key: run.view.key, text });
    } catch { fail(run, 'finish'); }
  }
  return { published, failed: runs.filter(r => r.failed).map(r => r.view.key), counts };
}

/**
 * The pass (K0). Runs when due (see `due`) or with `force`, in one transaction: a failure leaves every published row
 * as it was and throws (the timer reports records_refresh_failed). A view that throws, or whose snapshot would hold an
 * install id or a record id, logs records_view_failed with its key and keeps its previous row; the others publish.
 * Returns {built: false} when nothing was due, else {built: true, views, failed, records}.
 */
export function refresh(db, { modules, patches, now = Date.now(), force = false, thresholds = recordThresholds(), log = console.error } = {}) {
  const today = Math.floor(now / DAY);
  const sig = signature(modules, patches, thresholds);
  const m = memo(db);
  db.exec('SAVEPOINT records_refresh');
  try {
    const count = db.prepare('SELECT COUNT(*) AS n FROM e').get().n;
    m.seen = count;
    if (!force && !due(readState(db), viewRows(db), { sig, today, now, count, views: modules.views })) {
      db.exec('RELEASE records_refresh');
      return { built: false };
    }
    const result = build(db, { modules, patches, today, now, thresholds, log });
    const upsert = db.prepare(`INSERT INTO snap (mode, last_row, built, body) VALUES (?, ?, ?, ?)
      ON CONFLICT(mode) DO UPDATE SET last_row = excluded.last_row, built = excluded.built, body = excluded.body`);
    for (const p of result.published) upsert.run(rowOf(p.key), count, now, p.text);
    // A view that no longer loads is not served: its row goes.
    const loaded = new Set(modules.views.map(v => v.key));
    for (const key of viewRows(db).keys()) if (!loaded.has(key)) db.prepare('DELETE FROM snap WHERE mode = ?').run(rowOf(key));
    upsert.run(STATE, count, now, JSON.stringify({ v: 1, sig, day: today, failed: result.failed }));
    db.exec('RELEASE records_refresh');
    m.parsed.clear();
    return { built: true, views: result.published.length, failed: result.failed, records: result.counts.read };
  } catch (e) {
    db.exec('ROLLBACK TO records_refresh; RELEASE records_refresh');
    throw e;
  }
}

/** The query of a request: {window, patch, params} with the view's values, or null for anything outside the contract. */
function readQuery(view, params) {
  const entries = params instanceof URLSearchParams ? [...params] : Object.entries(params ?? {});
  const raw = new Map();
  for (const [name, value] of entries) {
    if (raw.has(name) || typeof value !== 'string') return null;
    if (name !== 'window' && name !== 'patch' && !Object.hasOwn(view.query, name)) return null;
    raw.set(name, value);
  }
  const window = raw.get('window');
  const patch = raw.get('patch');
  if (window !== undefined && !WINDOWS.includes(window)) return null;
  if (patch !== undefined && patch !== UNKNOWN && !PATCH_ID.test(patch)) return null;
  const out = {};
  for (const [name, check] of Object.entries(view.query)) {
    let value;
    try { value = check(raw.get(name)); } catch { return null; }
    if (value === undefined) return null;
    out[name] = value;
  }
  return { window: window ?? null, patch: patch ?? null, params: Object.freeze(out) };
}

/** The published snapshot of a view: null (never published), {withdrawn: true}, or the parsed one (cached per build). */
function snapshot(db, key) {
  const m = memo(db);
  const head = db.prepare('SELECT last_row, built, octet_length(body) AS size FROM snap WHERE mode = ?').get(rowOf(key));
  if (!head || head.size === 0) {
    m.parsed.delete(key);
    return head ? { withdrawn: true } : null;
  }
  const hit = m.parsed.get(key);
  if (hit && hit.built === head.built && hit.count === head.last_row) return hit;
  const value = JSON.parse(db.prepare('SELECT body FROM snap WHERE mode = ?').get(rowOf(key)).body);
  const entry = { withdrawn: false, built: head.built, count: head.last_row, value };
  m.parsed.set(key, entry);
  return entry;
}

/**
 * GET /v2/analytics/records/<view> (K4): {status, body}. 404 unknown_view, 400 invalid_query, else 200 with the
 * envelope: available, or not_published / rebuilding / no_window / unsupported_mode with `data: null`. Reads only the
 * published row; never builds.
 */
export function answer(db, key, params, { modules, log = console.error } = {}) {
  const view = modules.views.find(v => v.key === key);
  if (!view) return { status: 404, body: { ok: false, error: 'unknown_view' } };
  const query = readQuery(view, params);
  if (!query) return { status: 400, body: { ok: false, error: 'invalid_query' } };
  const envelope = f => ({ status: 200, body: {
    v: 2, source: 'game_records', view: view.key, metric: view.metric, rulesVersion: view.rules,
    derivers: f.derivers ?? versions(modules.derivers), available: f.available ?? false, reason: f.reason ?? null,
    published: f.published ?? null, stale: f.stale ?? false, thresholds: f.thresholds ?? recordThresholds(),
    period: f.period ?? null, windows: f.windows ?? [], data: f.data ?? null,
  } });
  if (view.unsupported?.(query.params)) return envelope({ reason: 'unsupported_mode' });
  const snap = snapshot(db, view.key);
  if (!snap) return envelope({ reason: 'not_published' });
  // A snapshot of other rules may have another shape: it waits for the pass that the new version starts.
  if (snap.withdrawn || snap.value.rules !== view.rules) return envelope({ reason: 'rebuilding' });

  const state = readState(db);
  const seen = memo(db).seen;
  const known = {
    derivers: snap.value.derivers, published: snap.built, thresholds: snap.value.thresholds,
    stale: state !== null && (snap.count !== state.count || (seen !== undefined && seen !== state.count)),
    windows: snap.value.windows.map(w => ({ patch: w.patch, window: w.window })),
  };
  const patch = query.patch ?? snap.value.latest ?? UNKNOWN;
  const name = query.window ?? (patch === UNKNOWN ? '30d' : 'patch');
  const found = snap.value.windows.find(w => w.patch === patch && w.window === name);
  if (!found) return envelope({ ...known, reason: 'no_window' });
  let data;
  try { data = view.slice(found.body, query.params); } catch (e) {
    log(JSON.stringify({ ok: false, error: 'records_view_failed', view: view.key, stage: 'slice' }));
    throw e;
  }
  return envelope({ ...known, available: true, period: found.period, data });
}

/** Withdraws every published view in the caller's transaction (the deletions in src/events.js do it in their batch). */
export function withdraw(db) {
  return Number(db.prepare("UPDATE snap SET body = '' WHERE mode LIKE ? AND octet_length(body) > 0").run(LIKE).changes);
}

/** For /healthz: the last pass and how many view rows are published or withdrawn. Counts and a time, no content. */
export function status(db) {
  const state = readState(db);
  const rows = [...viewRows(db).values()];
  const published = rows.filter(r => r.size > 0).length;
  return { built: state?.built ?? null, views: rows.length, published, withdrawn: rows.length - published, failed: state?.failed.length ?? 0 };
}
