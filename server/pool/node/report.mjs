// Coverage report for the owner (docs/analytics-coverage.md): how far the game records in the pool are from the
// thresholds of a published number, family by family, so nobody reads the database by hand. Runs on the pool host
// beside the live service:
//
//   node server/pool/node/report.mjs --data <POOL_DATA_DIR> [--days 30] [--format text|json]
//
// pool.sqlite is opened read-only: SQLite refuses any write through this connection, and a missing file is an error,
// never created. The service's lock (instance.lock.sqlite) is not touched: in WAL mode this reader and the service's
// writer work at the same time. The output is counts only - no install id, record id, deck code, card or class - and
// installs and games from 1 to 4 print as "<5".
import * as fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { DatabaseSync } from 'node:sqlite';

const DAY = 86_400_000;
// Thresholds and buckets of contract K3 (TASK-2026-11-26-pro-collection): a value is published from 5 installations
// and 10 games in its cell. Wave 3 (F1) moves these to records/buckets.mjs, so the views and this report read one copy.
export const MIN_INSTALLS = 5;
export const MIN_GAMES = 10;
export const MMR_RANGES = [['0-3999', 0, 3999], ['4000-5999', 4000, 5999], ['6000-7999', 6000, 7999], ['8000+', 8000, Infinity]];
// Installs and games from 1 to 4 print as "<5": so few people are never shown exactly. Zero is nobody and prints as
// is, and so do cell counts: a cell is a class, a card or a rating range, not a group of people.
const EXACT_FROM = 5;
const SMALL = `<${EXACT_FROM}`;
// Records are kept 180 days (KEEP_DAYS in src/events.js): a longer window would read nothing more.
const MAX_DAYS = 180;
// Constructed views count Ranked Standard and Wild only; a record without a class falls into no cell.
const RANKED_FORMATS = new Set(['standard', 'wild']);
const ARENA_KINDS = new Set(['arena', 'underground']);
// A duos lobby has four teams: a duos record placed below 4th is not counted (K5, Battlegrounds views).
const DUOS_LAST_PLACE = 4;
const USAGE = 'node server/pool/node/report.mjs --data <POOL_DATA_DIR> [--days 30] [--format text|json]';

// One query reads the window: each record once (index on day), with only the fields its family needs. The service's
// WAL checkpoint waits for an open read (docs/analytics-coverage.md), so the read is one pass rather than one per
// family, and the families and the all-records line come from one snapshot.
const WINDOW = `SELECT iid, mode, fmt,
    CASE WHEN mode = 'ranked' THEN json_extract(body, '$.cls') END AS cls,
    CASE WHEN mode = 'ranked' THEN json_extract(body, '$.mull.o') END AS offered,
    CASE WHEN mode IN ('bg', 'duos') THEN json_extract(body, '$.bg.mmr') END AS mmr,
    CASE WHEN mode IN ('bg', 'duos') THEN json_extract(body, '$.bg.placement') END AS place,
    CASE WHEN mode = 'arena_run' THEN json_extract(body, '$.run.kind') END AS kind,
    CASE WHEN mode = 'arena_run' THEN json_extract(body, '$.run.cls') END AS runClass
  FROM e WHERE day BETWEEN :from AND :to`;

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
 * its day), all from one query, so the families agree with each other.
 */
export function buildReport(db, { today, days = 30, now = Date.now() }) {
  const window = { from: today - days + 1, to: today };
  const ranked = new Family('ranked', 'format x class', 'game');
  const mulligan = new Family('ranked-mulligan', 'format x class x card', 'game');
  const battlegrounds = new Family('bg', 'mode x MMR range', 'game');
  const arena = new Family('arena', 'kind x class', 'run');
  const installs = new Set();
  let records = 0;
  for (const r of db.prepare(WINDOW).iterate(window)) {
    records++;
    installs.add(r.iid);
    if (r.mode === 'ranked' && RANKED_FORMATS.has(r.fmt) && typeof r.cls === 'string') {
      ranked.add(r.iid, [`${r.fmt}|${r.cls}`]);
      mulligan.add(r.iid, offeredCards(r.offered).map(card => `${r.fmt}|${r.cls}|${card}`));
    } else if (r.mode === 'bg' || r.mode === 'duos') {
      battlegrounds.add(r.iid, battlegroundsCells(r));
    } else if (r.mode === 'arena_run' && ARENA_KINDS.has(r.kind) && typeof r.runClass === 'string') {
      arena.add(r.iid, [`${r.kind}|${r.runClass}`]);
    }
  }
  return {
    v: 1, built: new Date(now).toISOString(),
    window: { days, fromDay: window.from, toDay: window.to, from: date(window.from), to: date(window.to) },
    thresholds: { minInstalls: MIN_INSTALLS, minGames: MIN_GAMES },
    all: { installs: installs.size, records },
    families: [ranked, mulligan, battlegrounds, arena].map(family => family.counts()),
  };
}

/** Installs or games as printed: from 1 to 4 "<5", otherwise the number. */
export const shown = n => (n > 0 && n < EXACT_FROM ? SMALL : n);

/** The report as printed by --format json: installs and games from 1 to 4 replaced by "<5", cell counts exact. */
export function publicReport(report) {
  return {
    ...report,
    thresholds: { ...report.thresholds, exactFrom: EXACT_FROM },
    all: { installs: shown(report.all.installs), records: shown(report.all.records) },
    families: report.families.map(f => ({ ...f, installs: shown(f.installs), games: shown(f.games) })),
  };
}

/** The report as printed by --format text: a few lines and one table, installs and games from 1 to 4 as "<5". */
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
    `publishable = a cell with >=${MIN_INSTALLS} installs and >=${MIN_GAMES} games; arena counts finished runs; "${SMALL}" is 1 to ${EXACT_FROM - 1} installs or games, cell counts are exact.`,
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
