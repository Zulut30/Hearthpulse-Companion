// Coverage report for the owner (docs/analytics-coverage.md): how far the game records in the pool are from the
// thresholds of a published number, family by family, so nobody reads the database by hand. Runs on the pool host
// beside the live service:
//
//   node server/pool/node/report.mjs --data <POOL_DATA_DIR> [--days 30] [--format text|json]
//
// pool.sqlite is opened read-only: SQLite refuses any write through this connection, and a missing file is an error,
// never created. The service's lock (instance.lock.sqlite) is not touched: in WAL mode this reader and the service's
// writer work at the same time. The output is counts only - no install id, record id, deck code, card or class - and
// every count under 5 prints as "<5".
import * as fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { DatabaseSync } from 'node:sqlite';

const DAY = 86_400_000;
// Thresholds and buckets of contract К3 (TASK-2026-11-26-pro-collection): a value is published from 5 installations
// and 10 games in its cell. Wave 3 (F1) moves these to records/buckets.mjs, so the views and this report read one copy.
export const MIN_INSTALLS = 5;
export const MIN_GAMES = 10;
export const MMR_RANGES = [['0-3999', 0, 3999], ['4000-5999', 4000, 5999], ['6000-7999', 6000, 7999], ['8000+', 8000, Infinity]];
// A count under this is printed as "<5", whatever it counts: a small group is never shown exactly.
const EXACT_FROM = 5;
const SMALL = `<${EXACT_FROM}`;
// Records are kept 180 days (KEEP_DAYS in src/events.js): a longer window would read nothing more.
const MAX_DAYS = 180;
const ARENA_KINDS = new Set(['arena', 'underground']);
// A duos lobby has four teams: a duos record placed below 4th is not counted (К5, Battlegrounds views).
const DUOS_LAST_PLACE = 4;
const USAGE = 'node server/pool/node/report.mjs --data <POOL_DATA_DIR> [--days 30] [--format text|json]';

// One query per mode group, each reading only the fields its families need. Constructed views count Ranked Standard and
// Wild only; a record without a class falls into no cell.
const RANKED = `SELECT iid, fmt, json_extract(body, '$.cls') AS cls, json_extract(body, '$.mull.o') AS offered FROM e
  WHERE day BETWEEN :from AND :to AND mode = 'ranked' AND fmt IN ('standard', 'wild') AND json_extract(body, '$.cls') IS NOT NULL`;
const BATTLEGROUNDS = `SELECT iid, mode, json_extract(body, '$.bg.mmr') AS mmr, json_extract(body, '$.bg.placement') AS place FROM e
  WHERE day BETWEEN :from AND :to AND mode IN ('bg', 'duos')`;
const ARENA_RUNS = `SELECT iid, json_extract(body, '$.run.kind') AS kind, json_extract(body, '$.run.cls') AS cls FROM e
  WHERE day BETWEEN :from AND :to AND mode = 'arena_run'`;
const ALL = 'SELECT COUNT(*) AS records, COUNT(DISTINCT iid) AS installs FROM e WHERE day BETWEEN :from AND :to';

/** The command line, or throws 'usage'. */
export function parseArgs(argv) {
  const args = { data: '', days: 30, format: 'text' };
  for (let i = 0; i < argv.length; i += 2) {
    const [name, value] = [argv[i], argv[i + 1]];
    if (value === undefined) throw new Error('usage');
    if (name === '--data') args.data = value;
    else if (name === '--days') args.days = Number(value);
    else if (name === '--format') args.format = value;
    else throw new Error('usage');
  }
  if (!args.data || !Number.isInteger(args.days) || args.days < 1 || args.days > MAX_DAYS || !['text', 'json'].includes(args.format)) {
    throw new Error('usage');
  }
  return { ...args, data: path.resolve(args.data) };
}

/** pool.sqlite of a data folder on a read-only connection. Throws 'pool_missing' rather than create the file. */
export function openPool(dataDir) {
  const file = path.join(dataDir, 'pool.sqlite');
  let isFile = false;
  try { isFile = fs.statSync(file).isFile(); } catch { /* no folder or no file: the same answer */ }
  if (!isFile) throw new Error('pool_missing');
  const db = new DatabaseSync(file, { readOnly: true });
  // A reader waits out the moment the service needs the database to itself instead of failing.
  db.exec('PRAGMA busy_timeout=5000');
  return db;
}

/** One family of cells: its records and installations, and per cell its games and installations. */
class Family {
  #installs = new Set();
  #cells = new Map();
  #games = 0;
  constructor(name, cell, unit) {
    this.name = name;
    this.cell = cell;
    this.unit = unit;
  }

  /** One record that falls into `keys`: counted once for the family and once in each of its cells. */
  add(iid, keys) {
    if (keys.length === 0) return;
    this.#games++;
    this.#installs.add(iid);
    for (const key of keys) {
      let cell = this.#cells.get(key);
      if (!cell) this.#cells.set(key, cell = { games: 0, installs: new Set() });
      cell.games++;
      cell.installs.add(iid);
    }
  }

  /** Counts only: nothing that leaves here holds an install id or a cell key. */
  counts() {
    const cells = [...this.#cells.values()];
    const enough = cells.filter(c => c.installs.size >= MIN_INSTALLS);
    return {
      family: this.name, cell: this.cell, unit: this.unit, installs: this.#installs.size, games: this.#games,
      cells: { total: cells.length, enoughInstalls: enough.length, publishable: enough.filter(c => c.games >= MIN_GAMES).length },
    };
  }
}

/** The distinct cards of an offered opening hand (a copy offered twice is one game of that card). */
function offeredCards(text) {
  if (typeof text !== 'string') return [];
  try {
    const list = JSON.parse(text);
    return Array.isArray(list) ? [...new Set(list.filter(card => typeof card === 'string'))] : [];
  } catch {
    return [];
  }
}

/** The MMR range of a rating (a record keeps it rounded down to 500), null without one. */
export function mmrRange(mmr) {
  if (!Number.isInteger(mmr)) return null;
  return MMR_RANGES.find(([, low, high]) => mmr >= low && mmr <= high)?.[0] ?? null;
}

/** A Battlegrounds record falls into its mode's "all" cell and, with a known rating, its MMR range. */
function battlegroundsCells({ mode, mmr, place }) {
  if (mode === 'duos' && place > DUOS_LAST_PLACE) return [];
  const name = mode === 'bg' ? 'solo' : 'duos';
  const range = mmrRange(mmr);
  return range ? [`${name}|all`, `${name}|${range}`] : [`${name}|all`];
}

const date = day => new Date(day * DAY).toISOString().slice(0, 10);

/**
 * The exact counts of the last `days` days up to `today` (days since 1970-01-01, UTC; a record dated later waits for
 * its day). All reads share one snapshot of the database, so the families agree with each other.
 */
export function buildReport(db, { today, days = 30, now = Date.now() }) {
  const window = { from: today - days + 1, to: today };
  const ranked = new Family('ranked', 'format x class', 'game');
  const mulligan = new Family('ranked-mulligan', 'format x class x card', 'game');
  const battlegrounds = new Family('bg', 'mode x MMR range', 'game');
  const arena = new Family('arena', 'kind x class', 'run');
  let all;
  db.exec('SAVEPOINT coverage_report');
  try {
    for (const r of db.prepare(RANKED).iterate(window)) {
      ranked.add(r.iid, [`${r.fmt}|${r.cls}`]);
      mulligan.add(r.iid, offeredCards(r.offered).map(card => `${r.fmt}|${r.cls}|${card}`));
    }
    for (const r of db.prepare(BATTLEGROUNDS).iterate(window)) battlegrounds.add(r.iid, battlegroundsCells(r));
    for (const r of db.prepare(ARENA_RUNS).iterate(window)) {
      if (ARENA_KINDS.has(r.kind) && typeof r.cls === 'string') arena.add(r.iid, [`${r.kind}|${r.cls}`]);
    }
    all = db.prepare(ALL).get(window);
  } finally {
    db.exec('RELEASE coverage_report');
  }
  return {
    v: 1, built: new Date(now).toISOString(),
    window: { days, fromDay: window.from, toDay: window.to, from: date(window.from), to: date(window.to) },
    thresholds: { minInstalls: MIN_INSTALLS, minGames: MIN_GAMES },
    all: { installs: all.installs, records: all.records },
    families: [ranked, mulligan, battlegrounds, arena].map(family => family.counts()),
  };
}

/** A count as printed: under 5 it is "<5", otherwise the number. */
export const shown = n => (n < EXACT_FROM ? SMALL : n);

/** The report as printed by --format json: every count under 5 replaced by "<5". */
export function publicReport(report) {
  return {
    ...report,
    thresholds: { ...report.thresholds, exactFrom: EXACT_FROM },
    all: { installs: shown(report.all.installs), records: shown(report.all.records) },
    families: report.families.map(f => ({
      ...f, installs: shown(f.installs), games: shown(f.games),
      cells: { total: shown(f.cells.total), enoughInstalls: shown(f.cells.enoughInstalls), publishable: shown(f.cells.publishable) },
    })),
  };
}

/** The report as printed by --format text: a few lines and one table, counts under 5 as "<5". */
export function renderText(report) {
  const p = publicReport(report);
  const rows = [
    ['family', 'cell', 'installs', 'games', 'cells', `>=${MIN_INSTALLS} installs`, 'publishable'],
    ...p.families.map(f => [f.family, f.unit === 'run' ? `${f.cell} (runs)` : f.cell, f.installs, f.games,
      f.cells.total, f.cells.enoughInstalls, f.cells.publishable]),
  ];
  const widths = rows[0].map((_, i) => Math.max(...rows.map(row => String(row[i]).length)));
  const line = row => row.map((value, i) => (i < 2 ? String(value).padEnd(widths[i]) : String(value).padStart(widths[i]))).join('  ').trimEnd();
  return [
    `Hearthpulse pool coverage, read-only: game records of ${p.window.from}..${p.window.to} (${p.window.days} days), built ${p.built}.`,
    `publishable = a cell with >=${MIN_INSTALLS} installs and >=${MIN_GAMES} games; arena counts finished runs; "${SMALL}" is a count from 0 to ${EXACT_FROM - 1}.`,
    `All records in the window: installs ${p.all.installs}, records ${p.all.records}.`,
    '',
    ...rows.map(line),
    '',
  ].join('\n');
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  let db;
  try {
    const args = parseArgs(process.argv.slice(2));
    db = openPool(args.data);
    const report = buildReport(db, { today: Math.floor(Date.now() / DAY), days: args.days });
    process.stdout.write(args.format === 'json' ? `${JSON.stringify(publicReport(report), null, 2)}\n` : renderText(report));
  } catch (e) {
    const known = ['usage', 'pool_missing'].includes(e?.message);
    console.error(JSON.stringify(known
      ? { ok: false, error: e.message, ...(e.message === 'usage' ? { usage: USAGE } : {}) }
      : { ok: false, error: 'report_failed', cause: String(e?.message ?? e).slice(0, 120) }));
    process.exitCode = 1;
  } finally {
    db?.close();
  }
}
