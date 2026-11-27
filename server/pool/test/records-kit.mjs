// Test kit for the views of game records (TASK-2026-11-26-pro-collection A0, docs/analytics-records.md). A pool
// database with the real schema in memory, a frozen clock, records stored the way POST /v1/events stores them
// (validateEvent), and the real pass, route, forget and clean-up, with the views, derivers and balance patches a test
// gives. A view or deriver is checked against the loader's contract when the kit is made.
import { openDatabase } from '../node/sqlite.mjs';
import { forgetEvents, pruneEvents, validateEvent } from '../src/events.js';
import { answer, refresh, status, withdraw } from '../node/records/index.mjs';
import { checkDeriver, checkView } from '../node/records/loader.mjs';
import { checkPatches } from '../node/records/periods.mjs';
import { oneOf, param } from '../node/records/buckets.mjs';

export const DAY = 86_400_000;
/** The frozen clock: 2026-11-26 12:00 UTC. Days in a test are TODAY - n. */
export const NOW = Date.UTC(2026, 10, 26, 12);
export const TODAY = Math.floor(NOW / DAY);
/** No patch in any line: every build is unknown. */
export const NO_PATCHES = Object.freeze({ v: 1, lines: { constructed: [], battlegrounds: [], arena: [] } });
export const THRESHOLDS = Object.freeze({ minInstalls: 5, minGames: 10 });

let sequence = 0;
/** A record id no other record of the run has. */
export const rid = () => (0x7e570000 + ++sequence).toString(16).padStart(32, '0');
/** The install id of install n. */
export const install = n => (0xfeed0000 + n).toString(16).padStart(32, '0');

/** A Ranked Standard game record; fields of `o` replace the defaults. */
export const ranked = (o = {}) => ({ id: rid(), day: TODAY, mode: 'ranked', fmt: 'standard', rank: 'gold', cls: 'MAGE', res: 'W', first: true, ...o });
/** A Battlegrounds solo record: `bg` fields over {placement: 4}, then the record's own `o`. */
export const battlegrounds = (bg = {}, o = {}) => ({ id: rid(), day: TODAY, mode: 'bg', res: 'W', bg: { placement: 4, ...bg }, ...o });
/** A finished Arena run (7 wins, 3 losses): `run` fields over the defaults, then the record's own `o`. */
export const arenaRun = (run = {}, o = {}) => ({ id: rid(), day: TODAY, mode: 'arena_run', run: { kind: 'arena', cls: 'MAGE', wins: 7, losses: 3, ...run }, ...o });

/**
 * A pool with records, the pass and the route. Options: views, derivers (plain module objects), patches (the file's
 * content), now (frozen), thresholds.
 */
export function recordsKit({ views = [], derivers = [], patches = NO_PATCHES, now = NOW, thresholds = THRESHOLDS } = {}) {
  const store = openDatabase(':memory:');
  const checked = fn => (m, i) => {
    const out = fn(m);
    if (out.reason) throw new Error(`kit module ${m?.key ?? m?.name ?? i}: ${out.reason}`);
    return out.view ?? out.deriver;
  };
  const modules = {
    views: views.map(checked(checkView)),
    derivers: derivers.map(checked(checkDeriver)).sort((a, b) => a.order - b.order),
  };
  const lines = [];
  const log = line => lines.push(JSON.parse(line));
  const env = { DB: store.DB };
  const reply = (body, code = 200) => ({ status: code, body });
  const insert = store.sqlite.prepare('INSERT INTO e (id, iid, day, mode, fmt, v, body) VALUES (?, ?, ?, ?, ?, 1, ?)');
  let clock = now;
  const kit = {
    db: store.sqlite, env, modules, patches: checkPatches(patches), logs: lines,
    get now() { return clock; },
    get today() { return Math.floor(clock / DAY); },
    /** Moves the frozen clock by `ms`. */
    advance(ms) { clock += ms; },
    /** Stores records of install n as POST /v1/events does (validated on the kit's day); a refused record throws. */
    add(n, ...games) {
      for (const g of games) {
        const row = validateEvent(g, kit.today);
        if (!row) throw new Error(`kit record refused: ${JSON.stringify(g).slice(0, 120)}`);
        insert.run(row.id, install(n), row.day, row.mode, row.fmt, row.body);
      }
      return games.length;
    },
    /** The pass at the kit's clock; options as refresh() takes them (force, ...). */
    refresh(options = {}) {
      return refresh(store.sqlite, { modules, patches: kit.patches, now: clock, thresholds, log, ...options });
    },
    /** The route's answer {status, body} for a query object. */
    request(view, query = {}) {
      return answer(store.sqlite, view, new URLSearchParams(query), { modules, log });
    },
    /** The K4 envelope of a 200 answer (anything else throws). */
    answer(view, query = {}) {
      const a = kit.request(view, query);
      if (a.status !== 200) throw new Error(`kit answer ${a.status}: ${JSON.stringify(a.body)}`);
      return a.body;
    },
    /** POST /v1/events/forget for install n, through the shared code. */
    forget(n) { return forgetEvents({ iid: install(n) }, env, reply); },
    /** The records step of the minute's clean-up on `today` (default: the kit's day). */
    prune(today = kit.today) { return pruneEvents(env, today); },
    withdraw() { return withdraw(store.sqlite); },
    status() { return status(store.sqlite); },
    close() { store.close(); },
  };
  return kit;
}

/** The data of a slice under the installation threshold, or of one nobody played: the two answer the same. */
const HIDDEN_SLICE = Object.freeze({ games: null, installs: null, quality: 'small_sample', suppressed: null, rows: Object.freeze([]) });

/**
 * A small view for tests and the pattern for real ones: per format and rank bucket, games, wins and installs by
 * class. A slice with hidden totals is left out of the snapshot and answered as a slice nobody played.
 * data: {games, installs, quality, suppressed, rows: [{cls, games, installs, winrate: RATE, quality}]}.
 */
export function winrateView(o = {}) {
  return {
    key: 'test-winrate', metric: 'test.winrate', rules: 'test-winrate-1', line: 'constructed', modes: ['ranked'],
    query: { fmt: oneOf(['standard', 'wild'], 'standard'), rank: param.rank },
    start: () => new Map(),
    add(acc, rec) {
      for (const rank of rec.ranks) {
        const slice = acc.get(`${rec.fmt}|${rank}`) ?? acc.set(`${rec.fmt}|${rank}`, { games: 0, installs: new Set(), classes: new Map() }).get(`${rec.fmt}|${rank}`);
        slice.games++;
        slice.installs.add(rec.iid);
        const cls = rec.body.cls ?? null;
        const cell = slice.classes.get(cls) ?? slice.classes.set(cls, { cls, games: 0, wins: 0, installs: new Set() }).get(cls);
        cell.games++;
        if (rec.body.res === 'W') cell.wins++;
        cell.installs.add(rec.iid);
      }
    },
    finish(acc, ctx) {
      const body = {};
      for (const [key, slice] of acc) {
        const totals = ctx.stats.totals(slice.games, slice.installs);
        if (totals.games === null) continue;
        const { rows, suppressed } = ctx.stats.rows(slice.classes.values(), cell => {
          const winrate = ctx.stats.rate(cell.wins, cell.games, cell.installs);
          return { cls: cell.cls, games: cell.games, installs: cell.installs.size, winrate, quality: ctx.stats.quality(winrate) };
        });
        body[key] = { ...totals, suppressed,
          rows: ctx.stats.rankFirst(rows, r => ctx.stats.rankable(r.winrate), (a, b) => b.games - a.games || (a.cls < b.cls ? -1 : 1)) };
      }
      return body;
    },
    slice: (body, p) => body[`${p.fmt}|${p.rank}`] ?? HIDDEN_SLICE,
    ...o,
  };
}

/** A view that lists what each window received: data {records: [[build, day], ...]} in (day, build) order. */
export function traceView(o = {}) {
  return {
    key: 'test-trace', metric: 'test.trace', rules: 'test-trace-1', line: 'constructed', modes: ['ranked'], query: {},
    start: () => [],
    add: (acc, rec) => { acc.push([rec.build, rec.day]); },
    finish: acc => ({ records: acc.sort((a, b) => a[1] - b[1] || (a[0] ?? 0) - (b[0] ?? 0)) }),
    slice: body => body,
    ...o,
  };
}
