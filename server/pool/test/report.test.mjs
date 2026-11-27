// TASK-2026-11-26-pro-collection A4 (AC9): the coverage report on a synthetic pool of 7 installations, by hand.
// buildReport is called with a frozen day; the command runs on the real clock, with every record days away from the
// window's edges, so a run across midnight UTC gives the same counts.
import assert from 'node:assert/strict';
import test from 'node:test';
import * as fs from 'node:fs';
import path from 'node:path';
import { tmpdir } from 'node:os';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { DatabaseSync } from 'node:sqlite';
import { validateEvent } from '../src/events.js';
import { openDatabase } from '../node/sqlite.mjs';
import { buildReport, mmrRange, openPool, parseArgs, publicReport, renderText, shown } from '../node/report.mjs';

const REPORT = fileURLToPath(new URL('../node/report.mjs', import.meta.url));
const DAY = 86_400_000;
const T = 20_783; // 2026-11-26
const INSTALLS = [1, 2, 3, 4, 5, 6, 7].map(n => String(n).repeat(32));
const [I1, I2, I3, I4, I5, I6, I7] = INSTALLS;
const DECKS = { MAGE: 'AAECAf0EMageDeckCode1', WARRIOR: 'AAECAQcWarriorDeckCode2', PRIEST: 'AAECAa0GPriestDeckCode3',
  HUNTER: 'AAECAR8HunterDeckCode4', ROGUE: 'AAECAaIHRogueDeckCode5', DRUID: 'AAECAZICDruidDeckCode6' };
const CARDS = ['CARD_M1', 'CARD_M2', 'CARD_M3', 'CARD_M4', 'CARD_W1', 'CARD_W2', 'CARD_W3', 'CARD_P1', 'CARD_P2', 'CARD_P3',
  'CARD_H1', 'CARD_H2', 'CARD_H3', 'CARD_D1', 'CARD_D2', 'CARD_D3', 'BG_HERO_A', 'BG_HERO_B'];
const M = ['CARD_M1', 'CARD_M2', 'CARD_M3'];

/** Record builders on `today`: each adds an [install id, stored row] pair to `rows`, 3 days old unless told. */
function recorder(today) {
  const rows = [];
  let serial = 0;
  const at = day => (day === undefined ? {} : { day });
  const game = (iid, fields) => {
    const row = validateEvent({ id: (++serial).toString(16).padStart(32, '0'), day: today - 3, res: 'W', ...fields }, today);
    assert.ok(row, 'a fixture record is valid');
    rows.push([iid, row]);
  };
  const ranked = (iid, cls, offered, extra = {}) => game(iid, {
    mode: 'ranked', fmt: 'standard', rank: 'gold', cls, hero: 'HERO_01', deck: DECKS[cls],
    ...(offered ? { first: offered.length === 3, mull: { o: offered, k: offered.slice(0, 1) } } : {}), ...extra,
  });
  const bg = (iid, mode, mmr, placement = 2, day = undefined) => game(iid, {
    mode, ...at(day), bg: { placement, offered: ['BG_HERO_A', 'BG_HERO_B'], picked: 'BG_HERO_A', ...(mmr === undefined ? {} : { mmr }) },
  });
  // Contract K1 stores a finished Arena run as mode arena_run with fmt null and no result.
  const run = (iid, kind, cls, wins, losses) => {
    const id = (++serial).toString(16).padStart(32, '0');
    const body = { id, day: today - 3, mode: 'arena_run', run: { kind, cls, wins, losses } };
    rows.push([iid, { id, day: body.day, mode: 'arena_run', fmt: null, body: JSON.stringify(body) }]);
  };
  return { rows, game, ranked, bg, run };
}

/**
 * The hand-checked pool. Main records are 3 days old; 60-day-old ones never count; `edges` adds a record on the first
 * day of a 30-day window (counted), the day before it and tomorrow (not counted).
 */
function records(today, { edges }) {
  const { rows, game, ranked, bg, run } = recorder(today);
  // Ranked Standard Mage: 10 games, 2 of each of I1..I5; each install's coin hand adds a fourth card, CARD_M4.
  for (const iid of [I1, I2, I3, I4, I5]) {
    ranked(iid, 'MAGE', M);
    ranked(iid, 'MAGE', [...M, 'CARD_M4']);
  }
  // Ranked Standard Warrior: 9 games from 5 installs; the hand that offered two copies of CARD_W1 is one game of it.
  [I1, I1, I2, I2, I3, I3, I4, I4, I5].forEach((iid, i) =>
    ranked(iid, 'WARRIOR', i === 0 ? ['CARD_W1', 'CARD_W1', 'CARD_W2', 'CARD_W3'] : ['CARD_W1', 'CARD_W2', 'CARD_W3']));
  // Ranked Wild Priest: 12 games from 4 installs.
  for (const iid of [I1, I2, I3, I4]) for (let i = 0; i < 3; i++) ranked(iid, 'PRIEST', ['CARD_P1', 'CARD_P2', 'CARD_P3'], { fmt: 'wild' });
  // Ranked Standard Hunter: 10 games, 2 of each of I1..I5.
  for (const iid of [I1, I2, I3, I4, I5]) for (let i = 0; i < 2; i++) ranked(iid, 'HUNTER', ['CARD_H1', 'CARD_H2', 'CARD_H3']);
  // Ranked Standard Rogue: one game of I6, no mulligan.
  ranked(I6, 'ROGUE', null);
  // In the window, in no family: Ranked without a class, Ranked Classic, Casual, an Arena game (a game, not a run).
  game(I7, { mode: 'ranked', fmt: 'standard', rank: 'gold' });
  ranked(I7, 'MAGE', M, { fmt: 'classic' });
  ranked(I6, 'MAGE', M, { mode: 'casual' });
  game(I7, { mode: 'arena', cls: 'MAGE' });
  // Battlegrounds solo: I1..I5 twice at 6500, I6 at 8000 and once without a rating, I1 once at 3500.
  for (const iid of [I1, I2, I3, I4, I5]) for (let i = 0; i < 2; i++) bg(iid, 'bg', 6500);
  bg(I6, 'bg', 8000);
  bg(I6, 'bg', undefined);
  bg(I1, 'bg', 3500);
  // Duos: I1..I4 twice at 4000; of I5's duos records the one placed 4th counts, 5th and 6th do not (four teams).
  for (const iid of [I1, I2, I3, I4]) for (let i = 0; i < 2; i++) bg(iid, 'duos', 4000);
  bg(I5, 'duos', 4000, 4);
  bg(I5, 'duos', 4000, 5);
  bg(I5, 'duos', 4000, 6);
  // Arena runs: one Mage run of each of I1..I5, an Underground Paladin run of I6.
  for (const iid of [I1, I2, I3, I4, I5]) run(iid, 'arena', 'MAGE', 7, 3);
  run(I6, 'underground', 'PALADIN', 12, 2);
  // Too old for any window.
  ranked(I6, 'MAGE', M, { day: today - 60 });
  bg(I6, 'bg', 6500, 1, today - 60);
  if (edges) {
    ranked(I7, 'DRUID', ['CARD_D1', 'CARD_D2', 'CARD_D3'], { day: today - 29 });
    ranked(I6, 'DRUID', ['CARD_D1', 'CARD_D2', 'CARD_D3'], { day: today - 30 });
    ranked(I6, 'DRUID', ['CARD_D1', 'CARD_D2', 'CARD_D3'], { day: today + 1 });
    bg(I7, 'bg', 6500, 1, today - 30);
  }
  return rows;
}

/** Two installations, four records: each family holds one or two installations and games. */
function fewRecords(today) {
  const { rows, ranked, bg, run } = recorder(today);
  ranked(I1, 'MAGE', M);
  ranked(I2, 'MAGE', [...M, 'CARD_M4']);
  bg(I1, 'bg', 6500);
  run(I2, 'arena', 'MAGE', 3, 3);
  return rows;
}

/** A data folder whose pool.sqlite (the service's own schema, WAL) holds the rows; the writer is left open. */
function pool(t, rows) {
  const dir = fs.mkdtempSync(path.join(tmpdir(), 'hp-report-'));
  t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
  const store = openDatabase(path.join(dir, 'pool.sqlite'));
  const insert = store.sqlite.prepare('INSERT INTO e (id, iid, day, mode, fmt, v, body) VALUES (?, ?, ?, ?, ?, 1, ?)');
  for (const [iid, r] of rows) insert.run(r.id, iid, r.day, r.mode, r.fmt, r.body);
  return { dir, store, rows };
}

async function command(...args) {
  const child = spawn(process.execPath, [REPORT, ...args], { windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
  let stdout = '';
  let stderr = '';
  child.stdout.on('data', data => { stdout += data; });
  child.stderr.on('data', data => { stderr += data; });
  const [code] = await once(child, 'close');
  return { code, stdout, stderr };
}

const family = (report, name) => report.families.find(f => f.family === name);
const counts = (installs, games, total, enoughInstalls, publishable) => ({ installs, games, cells: { total, enoughInstalls, publishable } });
const numbers = f => ({ installs: f.installs, games: f.games, cells: f.cells });

// The hand count of the 30 days up to T with the edge records (comments in records()).
const BY_HAND_30 = {
  all: { installs: 7, records: 77 },
  // Mage 10 + Warrior 9 + Priest 12 + Hunter 10 + Rogue 1 + Druid on T-29 1; cells: >=5 installs Mage, Warrior,
  // Hunter; also >=10 games Mage, Hunter.
  ranked: counts(7, 43, 6, 3, 2),
  // Every class but Rogue; cells M1-M3 (10 games, 5 installs), M4 (5, 5), W1-W3 (9, 5), P1-P3 (12, 4), H1-H3 (10, 5),
  // D1-D3 (1, 1).
  'ranked-mulligan': counts(6, 42, 16, 10, 6),
  // Solo 13 + duos 9 (I5's 4th place in, its 5th and 6th out); cells solo|all (13, 6), solo|6000-7999 (10, 5),
  // solo|8000+, solo|0-3999, duos|all (9, 5), duos|4000-5999 (9, 5).
  bg: counts(6, 22, 6, 4, 2),
  // arena|MAGE (5 runs, 5 installs), underground|PALADIN (1, 1).
  arena: counts(6, 6, 2, 1, 0),
};
// The 7 days up to T: the Druid game of T-29 (I7's only Ranked one) drops out, and I7 counts in the all-records line
// only. The same counts as the 30 days of the set without edge records, which the command reads.
const BY_HAND_7 = { ...BY_HAND_30, all: { installs: 7, records: 76 }, ranked: counts(6, 42, 5, 3, 2), 'ranked-mulligan': counts(5, 41, 13, 10, 6) };

test('AC9 each family counts installs, games and cells as the hand count does', t => {
  const { dir, store } = pool(t, records(T, { edges: true }));
  store.close();
  const db = openPool(dir);
  try {
    const report = buildReport(db, { today: T, days: 30, now: Date.UTC(2026, 10, 26, 12) });
    assert.deepEqual(report.window, { days: 30, fromDay: T - 29, toDay: T, from: '2026-10-28', to: '2026-11-26' });
    assert.deepEqual(report.thresholds, { minInstalls: 5, minGames: 10 });
    assert.equal(report.built, '2026-11-26T12:00:00.000Z');
    assert.deepEqual(report.all, BY_HAND_30.all);
    assert.deepEqual(report.families.map(f => [f.family, f.unit]),
      [['ranked', 'game'], ['ranked-mulligan', 'game'], ['bg', 'game'], ['arena', 'run']]);
    for (const name of ['ranked', 'ranked-mulligan', 'bg', 'arena']) assert.deepEqual(numbers(family(report, name)), BY_HAND_30[name], name);

    const week = buildReport(db, { today: T, days: 7 });
    assert.deepEqual(week.window, { days: 7, fromDay: T - 6, toDay: T, from: '2026-11-20', to: '2026-11-26' });
    assert.deepEqual(week.all, BY_HAND_7.all);
    for (const name of ['ranked', 'ranked-mulligan', 'bg', 'arena']) assert.deepEqual(numbers(family(week, name)), BY_HAND_7[name], name);
  } finally {
    db.close();
  }
});

test('AC9 Battlegrounds ratings fall into the MMR ranges of K3', () => {
  assert.deepEqual([0, 3500, 4000, 5500, 6000, 7500, 8000, 10_000, null, undefined, '6500'].map(mmrRange),
    ['0-3999', '0-3999', '4000-5999', '4000-5999', '6000-7999', '6000-7999', '8000+', '8000+', null, null, null]);
});

test('AC9 installs and games from 1 to 4 print as "<5"; zero and every cell count print exactly', t => {
  assert.deepEqual([0, 1, 4, 5, 6, 43].map(shown), [0, '<5', '<5', 5, 6, 43]);
  const { dir, store } = pool(t, records(T, { edges: true }));
  store.close();
  const db = openPool(dir);
  let report;
  try { report = buildReport(db, { today: T, days: 30 }); } finally { db.close(); }

  const json = publicReport(report);
  assert.deepEqual(json.all, { installs: 7, records: 77 });
  assert.deepEqual(json.thresholds, { minInstalls: 5, minGames: 10, exactFrom: 5 });
  assert.deepEqual(json.families.map(numbers), [
    counts(7, 43, 6, 3, 2),
    counts(6, 42, 16, 10, 6),
    counts(6, 22, 6, 4, 2),
    counts(6, 6, 2, 1, 0),
  ]);

  const text = renderText(report);
  const table = text.split('\n').filter(line => /^(family|ranked|bg|arena)\b/.test(line)).map(line => line.split(/ {2,}/));
  assert.deepEqual(table, [
    ['family', 'cell', 'installs', 'games', 'cells', '>=5 installs', 'publishable'],
    ['ranked', 'format x class', '7', '43', '6', '3', '2'],
    ['ranked-mulligan', 'format x class x card', '6', '42', '16', '10', '6'],
    ['bg', 'mode x MMR range', '6', '22', '6', '4', '2'],
    ['arena', 'kind x class (runs)', '6', '6', '2', '1', '0'],
  ]);
  assert.match(text, /game records of 2026-10-28\.\.2026-11-26 \(30 days\)/);
  assert.match(text, /All records in the window: installs 7, records 77\./);
});

test('AC9 a pool of two installations and an empty pool: "<5" and 0 in every family and the all-records line', { timeout: 60_000 }, async t => {
  const today = Math.floor(Date.now() / DAY);
  const few = pool(t, fewRecords(today));
  few.store.close();
  const none = pool(t, []);
  none.store.close();
  const cases = [
    // Ranked: standard|MAGE of I1 and I2, whose hands offer M1-M3 and M4; one solo game at 6500 (all and 6000-7999);
    // one Arena run. Every family and the window hold 1 to 4 installs and games; no cell reaches 5 installs.
    [few.dir, { installs: '<5', records: '<5' },
      [counts('<5', '<5', 1, 0, 0), counts('<5', '<5', 4, 0, 0), counts('<5', '<5', 2, 0, 0), counts('<5', '<5', 1, 0, 0)]],
    [none.dir, { installs: 0, records: 0 }, Array(4).fill(counts(0, 0, 0, 0, 0))],
  ];
  for (const [dir, all, families] of cases) {
    const json = await command('--data', dir, '--format', 'json');
    const text = await command('--data', dir);
    for (const output of [json, text]) {
      assert.equal(output.code, 0, output.stderr);
      assert.equal(output.stderr, '');
    }
    const report = JSON.parse(json.stdout);
    assert.deepEqual(report.all, all);
    assert.deepEqual(report.families.map(f => f.family), ['ranked', 'ranked-mulligan', 'bg', 'arena']);
    assert.deepEqual(report.families.map(numbers), families);

    assert.ok(text.stdout.includes(`All records in the window: installs ${all.installs}, records ${all.records}.`), text.stdout);
    const table = text.stdout.split('\n').filter(line => /^(ranked|bg|arena)\b/.test(line)).map(line => line.split(/ {2,}/).slice(2));
    assert.deepEqual(table, families.map(f => [f.installs, f.games, f.cells.total, f.cells.enoughInstalls, f.cells.publishable].map(String)));
    assert.doesNotMatch(text.stdout, /NaN|undefined|null/);
  }
});

test('AC9 the command beside a running service: counts only, no install id, record id or deck code', { timeout: 60_000 }, async t => {
  const today = Math.floor(Date.now() / DAY);
  const { dir, store, rows } = pool(t, records(today, { edges: false }));
  // The service as it runs: its single-writer lock held, and a write it has not committed yet (5 more installs).
  const lock = new DatabaseSync(path.join(dir, 'instance.lock.sqlite'));
  lock.exec('CREATE TABLE IF NOT EXISTS service(pid INTEGER); BEGIN EXCLUSIVE;');
  store.sqlite.exec('BEGIN IMMEDIATE');
  const pending = ['8', '9', 'a', 'b', 'c'].map(c => c.repeat(32));
  pending.forEach((iid, i) => store.sqlite.prepare("INSERT INTO e (id, iid, day, mode, fmt, v, body) VALUES (?, ?, ?, 'bg', NULL, 1, ?)")
    .run(`f${i}`.padEnd(32, 'e'), iid, today - 3, JSON.stringify({ res: 'W', bg: { placement: 1, mmr: 6500 } })));
  let json;
  let text;
  try {
    json = await command('--data', dir, '--format', 'json');
    text = await command('--data', dir);
  } finally {
    store.sqlite.exec('ROLLBACK');
    store.close();
    lock.exec('ROLLBACK');
    lock.close();
  }
  assert.equal(json.code, 0, json.stderr);
  assert.equal(text.code, 0, text.stderr);
  assert.equal(json.stderr + text.stderr, '');

  const report = JSON.parse(json.stdout);
  assert.deepEqual(report.window, { days: 30, fromDay: today - 29, toDay: today,
    from: new Date((today - 29) * DAY).toISOString().slice(0, 10), to: new Date(today * DAY).toISOString().slice(0, 10) });
  assert.deepEqual(report.all, { installs: 7, records: 76 }, 'the uncommitted records are not read');
  assert.deepEqual(report.families.map(numbers), [
    counts(6, 42, 5, 3, 2),
    counts(5, 41, 13, 10, 6),
    counts(6, 22, 6, 4, 2),
    counts(6, 6, 2, 1, 0),
  ]);
  assert.match(text.stdout, /^ranked {2,}format x class {2,}6 {2,}42 {2,}5 {2,}3 {2,}2$/m);

  const secrets = [...INSTALLS, ...pending, ...rows.map(([, r]) => r.id), ...Object.values(DECKS), ...CARDS, ...Object.keys(DECKS), 'PALADIN'];
  for (const output of [json.stdout, text.stdout]) {
    for (const secret of secrets) assert.equal(output.includes(secret), false, `the output holds ${secret}`);
  }
});

test('AC9 the report only reads: writes refused, the file unchanged, no lock and no database created', { timeout: 60_000 }, async t => {
  const { dir, store } = pool(t, records(T, { edges: true }));
  store.close();
  const file = path.join(dir, 'pool.sqlite');
  const digest = () => createHash('sha256').update(fs.readFileSync(file)).digest('hex');
  const before = digest();
  const result = await command('--data', dir);
  assert.equal(result.code, 0, result.stderr);
  assert.equal(digest(), before);
  assert.equal(fs.existsSync(path.join(dir, 'instance.lock.sqlite')), false, 'the service lock is not taken');

  const db = openPool(dir);
  try {
    assert.throws(() => db.exec('DELETE FROM e'), e => e.errcode === 8, 'SQLITE_READONLY');
    assert.throws(() => db.exec('CREATE TABLE x (y)'), e => e.errcode === 8, 'SQLITE_READONLY');
    assert.equal(db.prepare('SELECT COUNT(*) AS n FROM e').get().n, records(T, { edges: true }).length);
  } finally {
    db.close();
  }
  assert.equal(digest(), before);

  const empty = fs.mkdtempSync(path.join(tmpdir(), 'hp-report-empty-'));
  t.after(() => fs.rmSync(empty, { recursive: true, force: true }));
  const missing = await command('--data', empty);
  assert.equal(missing.code, 1);
  assert.deepEqual(JSON.parse(missing.stderr), { ok: false, error: 'pool_missing' });
  assert.equal(missing.stdout, '');
  assert.deepEqual(fs.readdirSync(empty), [], 'no pool.sqlite is created');
  assert.throws(() => openPool(path.join(empty, 'no-such-folder')), /pool_missing/);
});

test('AC9 the command line: --data required, --days 1..180, --format text or json, nothing else', async () => {
  assert.deepEqual(parseArgs(['--data', 'pool-data']), { data: path.resolve('pool-data'), days: 30, format: 'text' });
  assert.deepEqual(parseArgs(['--format', 'json', '--days', '180', '--data', 'd']), { data: path.resolve('d'), days: 180, format: 'json' });
  for (const argv of [[], ['--data'], ['--days', '7'], ['--data', 'd', '--days', '0'], ['--data', 'd', '--days', '181'],
    ['--data', 'd', '--days', '1.5'], ['--data', 'd', '--days', 'x'], ['--data', 'd', '--format', 'xml'],
    ['--data', 'd', '--iid', '1'], ['--data', 'd', '--days']]) {
    assert.throws(() => parseArgs(argv), /usage/, argv.join(' '));
  }
  const bad = await command('--data', 'd', '--format', 'csv');
  assert.equal(bad.code, 1);
  assert.equal(JSON.parse(bad.stderr).error, 'usage');
  assert.match(JSON.parse(bad.stderr).usage, /--data <POOL_DATA_DIR> \[--days 30\] \[--format text\|json\]/);
});
