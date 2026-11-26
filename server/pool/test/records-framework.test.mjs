// The record pass and its views (TASK-2026-11-26-pro-collection A0): thresholds and ranking (AC5), withdrawal on
// deletion (AC6), the loader against the contract and the catalogue (AC8), and what a broken view or deriver does.
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { catalogProblems, loadModules, readCatalog } from '../node/records/loader.mjs';
import { THRESHOLDS, createStats } from '../node/records/stats.mjs';
import { DAY, TODAY, install, ranked, recordsKit, traceView, winrateView } from './records-kit.mjs';

/** The Wilson score interval worked out by hand, apart from the code under test (z = 1.96). */
function wilsonByHand(k, n) {
  const z = 1.96;
  const p = k / n;
  const denominator = 1 + (z * z) / n;
  const centre = p + (z * z) / (2 * n);
  const spread = z * Math.sqrt((p * (1 - p)) / n + (z * z) / (4 * n * n));
  return [(centre - spread) / denominator, (centre + spread) / denominator];
}

/** `games` Ranked games of class `cls` spread over installs first..first+installs-1, the first `wins` won. */
function spread(kit, { cls, games, installs, wins = 0, first = 1, ...o }) {
  for (let i = 0; i < games; i++) kit.add(first + (i % installs), ranked({ cls, res: i < wins ? 'W' : 'L', ...o }));
}

test('AC5: a cell of 4 installs is suppressed, 9 games keep counts without a value, 7 of 10 is 0.7 with its Wilson interval and not rankable', () => {
  const kit = recordsKit({ views: [winrateView()] });
  spread(kit, { cls: 'PRIEST', games: 10, installs: 4, wins: 5, first: 1 });
  spread(kit, { cls: 'ROGUE', games: 9, installs: 5, wins: 5, first: 11 });
  spread(kit, { cls: 'MAGE', games: 10, installs: 5, wins: 7, first: 21 });
  assert.equal(kit.refresh().built, true);
  const a = kit.answer('test-winrate');
  assert.equal(a.available, true);
  assert.equal(a.data.games, 29);
  assert.equal(a.data.suppressed, 1, 'the PRIEST row of 4 installs');
  assert.equal(a.data.rows.some(r => r.cls === 'PRIEST'), false);

  const rogue = a.data.rows.find(r => r.cls === 'ROGUE');
  assert.deepEqual(rogue.winrate, { num: 5, den: 9, value: null, low: null, high: null, rankable: false });
  assert.equal(rogue.quality, 'small_sample');

  const mage = a.data.rows.find(r => r.cls === 'MAGE');
  const [low, high] = wilsonByHand(7, 10);
  assert.equal(mage.winrate.value, 0.7);
  assert.ok(Math.abs(mage.winrate.low - low) < 0.001 && Math.abs(mage.winrate.high - high) < 0.001, JSON.stringify(mage.winrate));
  assert.deepEqual([mage.winrate.low, mage.winrate.high], [0.397, 0.892]);
  assert.equal(mage.winrate.rankable, false, `half-width ${((high - low) / 2).toFixed(3)} is over 5 points`);
  assert.equal(mage.quality, 'ok');
  assert.equal(JSON.stringify(a).includes(install(21)), false, 'no install id in the answer');
});

test('the numbers of a pass: thresholds per number, mean and placement intervals, and what may order rows', () => {
  const s = createStats({ minInstalls: 5, minGames: 10 });
  assert.equal(s.rate(9, 10, 4), null, 'four installs: no number at all');
  assert.equal(s.rate(9, 10, new Set(['a', 'b', 'c', 'd'])), null);
  // 1000 games at 0.5: Wilson half-width 0.031, within 5 points.
  assert.equal(s.rate(500, 1000, 5).rankable, true);
  assert.equal(s.rate(50, 100, 5).rankable, false, 'half-width 0.096');
  // Mean ± 1.96·s/√n by hand: places 1..8 twice over (n 16, mean 4.5, s = 2.366).
  const places = [1, 2, 3, 4, 5, 6, 7, 8, 1, 2, 3, 4, 5, 6, 7, 8];
  const sd = Math.sqrt(places.reduce((acc, p) => acc + (p - 4.5) ** 2, 0) / 15);
  const half = (1.96 * sd) / Math.sqrt(16);
  const placed = s.placement(places, 6);
  assert.deepEqual(placed, { n: 16, mean: 4.5, low: Math.round((4.5 - half) * 1000) / 1000, high: Math.round((4.5 + half) * 1000) / 1000,
    dist: [2, 2, 2, 2, 2, 2, 2, 2], rankable: false });
  assert.deepEqual(s.placement([1, 2, 3, 9, 0], 6), { n: 3, mean: null, low: null, high: null, dist: null, rankable: false }, 'places out of range are not counted; under 10 nothing shows');
  assert.equal(s.placement(Array(400).fill(4).concat(Array(400).fill(5)), 9).rankable, true, '800 places: half-width 0.035');
  assert.equal(s.mean(Array(50).fill(7), 5, 'wins').rankable, true, 'no spread, within 0.3 wins');
  assert.equal(s.mean(Array(50).fill(7), 5).rankable, false, 'a mean without a limit (turns) never ranks');
  assert.deepEqual(s.mean([3, 4], 5), { n: 2, mean: null, low: null, high: null, rankable: false });
  assert.throws(() => s.mean([1], 5, 'speed'), /unknown_rank_limit/);
  const ranked = s.rankFirst([{ v: 9, r: false }, { v: 1, r: true }, { v: 5, r: true }], x => x.r, (a, b) => b.v - a.v);
  assert.deepEqual(ranked.map(x => x.v), [5, 1, 9], 'an unrankable row never sorts above a rankable one');
});

test('Q1: a mean and a placement need 5 installations behind them, and 10 observations for a value', () => {
  const s = createStats({ minInstalls: 5, minGames: 10 });
  assert.equal(s.mean(Array(10).fill(3), 4), null, 'four installs: no mean, not even its count');
  assert.equal(s.mean(Array(10).fill(3), new Set(['a', 'b', 'c', 'd'])), null);
  assert.equal(s.placement(Array(10).fill(4), 4), null, 'four installs: no placement, not even its count');
  assert.deepEqual(s.mean(Array(9).fill(3), 5), { n: 9, mean: null, low: null, high: null, rankable: false }, 'nine values: the count only');
  assert.deepEqual(s.mean(Array(10).fill(3), 5), { n: 10, mean: 3, low: 3, high: 3, rankable: false }, 'ten values: the mean');
  assert.deepEqual(s.placement(Array(9).fill(4), 5), { n: 9, mean: null, low: null, high: null, dist: null, rankable: false }, 'nine places: no distribution');
  assert.deepEqual(s.placement(Array(10).fill(4), 5), { n: 10, mean: 4, low: 4, high: 4, dist: [0, 0, 0, 10, 0, 0, 0, 0], rankable: true });
});

test('Q5: the K3 thresholds are the foundation\'s: 5 installations, 10 games, 50 games behind a deck row', () => {
  assert.deepEqual(THRESHOLDS, { minInstalls: 5, minGames: 10, minDeckGames: 50 });
  const s = createStats();
  assert.deepEqual([s.minInstalls, s.minGames, s.minDeckGames], [5, 10, 50]);
  // AC14's three decks: 50 games of 5 installations, 49 of 5, 60 of 4.
  const decks = [{ key: 'a', games: 50, installs: 5 }, { key: 'b', games: 49, installs: 5 }, { key: 'c', games: 60, installs: 4 }];
  assert.deepEqual(s.deckRows(decks, d => ({ key: d.key })), { rows: [{ key: 'a' }], suppressed: 2 });
  assert.deepEqual(s.rows(decks, d => ({ key: d.key })), { rows: [{ key: 'a' }, { key: 'b' }], suppressed: 1 }, 'other rows need no 50 games');
  assert.deepEqual(s.deckRows([{ key: 'x', installs: 9 }], d => d), { rows: [], suppressed: 1 }, 'a deck cell without its games is no row');

  // A view reads them from its ctx, as the pass sets them.
  const kit = recordsKit({ views: [traceView({ finish: (acc, ctx) => ({ ...ctx.thresholds, minDeckGames: ctx.stats.minDeckGames }) })] });
  kit.refresh();
  assert.deepEqual(kit.answer('test-trace').data, { minInstalls: 5, minGames: 10, minDeckGames: 50 });
});

test('m2: the totals of a slice are numbers too: null under 5 installations, small_sample under 10 games', () => {
  const s = createStats();
  assert.deepEqual(s.totals(40, 4), { games: null, installs: null, quality: 'small_sample' });
  assert.deepEqual(s.totals(2, new Set(['a'])), { games: null, installs: null, quality: 'small_sample' });
  assert.deepEqual(s.totals(9, 5), { games: 9, installs: 5, quality: 'small_sample' });
  assert.deepEqual(s.totals(10, new Set(['a', 'b', 'c', 'd', 'e'])), { games: 10, installs: 5, quality: 'ok' });

  // The example view: a slice of one installation answers as a slice nobody played, with no number at all.
  const kit = recordsKit({ views: [winrateView()] });
  kit.add(1, ranked({ cls: 'DEMONHUNTER', rank: 'legend' }), ranked({ cls: 'DEMONHUNTER', rank: 'legend' }));
  spread(kit, { cls: 'MAGE', games: 10, installs: 5, wins: 6, first: 11 });
  kit.refresh();
  const hidden = { games: null, installs: null, quality: 'small_sample', suppressed: null, rows: [] };
  assert.deepEqual(kit.answer('test-winrate', { rank: 'legend' }).data, hidden, 'two games of one installation at legend');
  assert.deepEqual(kit.answer('test-winrate', { rank: 'bronze' }).data, hidden, 'nobody at bronze');
  const gold = kit.answer('test-winrate', { rank: 'gold' }).data;
  assert.deepEqual([gold.games, gold.installs, gold.quality, gold.suppressed], [10, 5, 'ok', 0]);
  const all = kit.answer('test-winrate').data;
  assert.deepEqual([all.games, all.installs, all.quality, all.suppressed], [12, 6, 'ok', 1], 'the legend player\'s class is a hidden row of a slice of six');
  assert.equal(kit.db.prepare("SELECT body FROM snap WHERE mode = 'records:test-winrate'").get().body.includes('legend'), false,
    'a hidden slice is not in the snapshot');
});

test('AC6: a forget withdraws every view in its own batch; the next pass has lost that install; a clean-up that deletes nothing withdraws nothing', async () => {
  const kit = recordsKit({ views: [winrateView(), traceView()] });
  spread(kit, { cls: 'MAGE', games: 12, installs: 6, wins: 8, first: 1 });
  kit.refresh();
  assert.equal(kit.answer('test-winrate').data.rows[0].games, 12);

  // The minute's clean-up: nothing older than 180 days, nothing deleted, nothing withdrawn.
  await kit.prune();
  for (const view of ['test-winrate', 'test-trace']) assert.equal(kit.answer(view).available, true, `${view} after an empty clean-up`);
  assert.equal(kit.refresh().built, false, 'and no rebuild is due');

  // A forget that fails inside its batch withdraws nothing: the withdrawal is in the same transaction as the delete.
  kit.db.exec("CREATE TRIGGER forget_fails BEFORE DELETE ON e BEGIN SELECT RAISE(ABORT, 'synthetic'); END");
  await assert.rejects(kit.forget(6));
  assert.equal(kit.answer('test-winrate').available, true, 'rolled back with the delete');
  kit.db.exec('DROP TRIGGER forget_fails');

  const forgot = await kit.forget(6);
  assert.deepEqual(forgot.body, { ok: true, deleted: 2 });
  for (const view of ['test-winrate', 'test-trace']) {
    const a = kit.answer(view);
    assert.deepEqual([a.available, a.reason, a.data, a.published, a.period], [false, 'rebuilding', null, null, null], view);
  }
  // A forget of an install with no records deletes nothing and leaves the (rebuilt) views alone.
  assert.equal(kit.refresh().built, true, 'a withdrawn view is due at once');
  const after = kit.answer('test-winrate').data.rows[0];
  assert.deepEqual([after.games, after.installs], [10, 5], 'the pass no longer counts install 6');
  assert.deepEqual((await kit.forget(99)).body, { ok: true, deleted: 0 });
  assert.equal(kit.answer('test-winrate').available, true);
});

test('AC6: the clean-up that deletes records older than 180 days withdraws the views', async () => {
  const kit = recordsKit({ views: [traceView()] });
  kit.add(1, ranked({ day: TODAY - 100 }));
  kit.add(2, ranked({ day: TODAY - 181 }));
  kit.refresh();
  assert.equal(kit.answer('test-trace').available, true);
  await kit.prune();
  assert.equal(kit.answer('test-trace').reason, 'rebuilding');
  assert.equal(kit.db.prepare('SELECT COUNT(*) AS n FROM e').get().n, 1);
  assert.equal(kit.refresh().built, true);
  assert.equal(kit.answer('test-trace').available, true);
});

test('Q4: a record exactly 180 days old is kept, and the clean-up that keeps it withdraws nothing', async () => {
  const kit = recordsKit({ views: [traceView()] });
  kit.add(1, ranked({ day: TODAY - 10 }));
  kit.add(2, ranked({ day: TODAY - 180 }));
  assert.equal(kit.refresh().built, true);
  await kit.prune();
  assert.equal(kit.db.prepare('SELECT COUNT(*) AS n FROM e').get().n, 2, 'T-180 is within the 180 days');
  assert.equal(kit.answer('test-trace').available, true, 'nothing deleted, nothing withdrawn');
  assert.equal(kit.refresh().built, false, 'and no pass is due');
});

test('a pass is due only when something changed: new records wait 30 minutes, a version or the patches rebuild at once', () => {
  const kit = recordsKit({ views: [winrateView()] });
  spread(kit, { cls: 'MAGE', games: 10, installs: 5, wins: 5 });
  assert.equal(kit.refresh().built, true, 'nothing built yet');
  assert.equal(kit.refresh().built, false);
  spread(kit, { cls: 'MAGE', games: 1, installs: 1, first: 7 });
  kit.advance(29 * 60_000);
  assert.equal(kit.refresh().built, false, 'new records, 29 minutes after the build');
  assert.equal(kit.answer('test-winrate').stale, true, 'they wait for the next build');
  kit.advance(60_000);
  assert.equal(kit.refresh().built, true, '30 minutes');
  assert.equal(kit.answer('test-winrate').stale, false);
  assert.equal(kit.answer('test-winrate').data.games, 11);

  // A new rules version: the old snapshot is not served under it, and the pass is due at once.
  const next = recordsKit({ views: [winrateView({ rules: 'test-winrate-2' })] });
  const copy = kit.db.prepare("SELECT mode, last_row, built, body FROM snap WHERE mode LIKE 'records%'").all();
  for (const r of copy) next.db.prepare('INSERT INTO snap (mode, last_row, built, body) VALUES (?, ?, ?, ?)').run(r.mode, r.last_row, r.built, r.body);
  assert.equal(next.answer('test-winrate').reason, 'rebuilding', 'a snapshot of rules test-winrate-1');
  assert.equal(next.refresh().built, true, 'another signature');
  assert.equal(next.answer('test-winrate').rulesVersion, 'test-winrate-2');

  const patched = recordsKit({ views: [winrateView()], patches: { v: 1, lines: { constructed: [{ id: 'b1', fromBuild: 1, fromDay: 0 }] } } });
  for (const r of copy) patched.db.prepare('INSERT INTO snap (mode, last_row, built, body) VALUES (?, ?, ?, ?)').run(r.mode, r.last_row, r.built, r.body);
  assert.equal(patched.refresh().built, true, 'a new patches file');
});

test('the snapshot is parsed once per build, not on every request', () => {
  const kit = recordsKit({ views: [traceView()] });
  kit.add(1, ranked());
  kit.refresh();
  const first = kit.answer('test-trace').data;
  assert.equal(kit.answer('test-trace').data, first, 'the same parsed object');
  kit.add(1, ranked());
  kit.refresh({ force: true });
  const rebuilt = kit.answer('test-trace').data;
  assert.notEqual(rebuilt, first);
  assert.equal(rebuilt.records.length, 2);
});

test('the envelope of K4: fields, an unknown or repeated parameter, a bad value, an unknown view, an unsupported mode', () => {
  const kit = recordsKit({ views: [winrateView(), traceView({ key: 'test-modes', query: { kind: v => v ?? 'a' }, unsupported: p => p.kind === 'b' })] });
  const before = kit.answer('test-winrate');
  assert.deepEqual(before, {
    v: 2, source: 'game_records', view: 'test-winrate', metric: 'test.winrate', rulesVersion: 'test-winrate-1', derivers: {},
    available: false, reason: 'not_published', published: null, stale: false, thresholds: { minInstalls: 5, minGames: 10 },
    period: null, windows: [], data: null,
  });
  kit.add(1, ranked());
  kit.refresh();
  const a = kit.answer('test-winrate', { fmt: 'standard', rank: 'gold', window: '30d', patch: 'unknown' });
  assert.deepEqual(Object.keys(a), ['v', 'source', 'view', 'metric', 'rulesVersion', 'derivers', 'available', 'reason', 'published', 'stale',
    'thresholds', 'period', 'windows', 'data']);
  assert.equal(a.published, kit.now);
  assert.deepEqual(a.data, { games: null, installs: null, quality: 'small_sample', suppressed: null, rows: [] }, 'one installation: no totals (m2)');
  // m4: an answer with nothing to serve has published null; no_window serves the snapshot's time and window list.
  const unsupported = kit.answer('test-modes', { kind: 'b' });
  assert.deepEqual([unsupported.reason, unsupported.published, unsupported.period, unsupported.windows, unsupported.data],
    ['unsupported_mode', null, null, [], null]);
  const none = kit.answer('test-winrate', { patch: 'P0' });
  assert.deepEqual([none.reason, none.published, none.period, none.windows, none.data], ['no_window', kit.now, null, [{ patch: 'unknown', window: '30d' }], null]);
  for (const query of [{ cls: 'MAGE' }, { fmt: 'classic' }, { rank: 'd5' }, { window: '14d' }, { patch: 'p 1' }, { fmt: 'STANDARD' }]) {
    assert.deepEqual(kit.request('test-winrate', query), { status: 400, body: { ok: false, error: 'invalid_query' } }, JSON.stringify(query));
  }
  assert.equal(kit.request('test-winrate', new URLSearchParams('fmt=standard&fmt=wild')).status, 400, 'a repeated parameter');
  assert.deepEqual(kit.request('nope'), { status: 404, body: { ok: false, error: 'unknown_view' } });
  assert.equal(kit.answer('test-modes', { kind: 'b' }).reason, 'unsupported_mode');
  assert.equal(kit.answer('test-modes', { kind: 'a' }).available, true);
});

test('a view that throws logs its key and nothing else; a view that would publish an install id is held back; the others publish', () => {
  const broken = traceView({ key: 'test-broken', add: rec => { throw new Error(`boom ${rec.iid} ${JSON.stringify(rec.body)}`); } });
  const leaky = traceView({ key: 'test-leaky', add: (acc, rec) => { acc.push([rec.iid, rec.day]); } });
  const kit = recordsKit({ views: [winrateView(), broken, leaky] });
  kit.add(1, ranked());
  const result = kit.refresh();
  assert.deepEqual(result.failed, ['test-broken', 'test-leaky']);
  assert.deepEqual(kit.logs, [
    { ok: false, error: 'records_view_failed', view: 'test-broken', stage: 'add' },
    { ok: false, error: 'records_view_failed', view: 'test-leaky', stage: 'install_id' },
  ]);
  assert.equal(kit.answer('test-winrate').available, true);
  assert.equal(kit.answer('test-broken').reason, 'not_published');
  assert.equal(kit.answer('test-leaky').reason, 'not_published');
  assert.equal(kit.refresh().built, false, 'a view that failed is not retried until something changes');
  assert.equal(kit.status().failed, 2);
});

test('Q2: a view that would publish a record id is held back as one with an install id is', () => {
  const leaky = traceView({ key: 'test-record-id', add: (acc, rec) => { acc.push([`id:${rec.body.id}`, rec.day]); } });
  const kit = recordsKit({ views: [winrateView(), leaky] });
  const game = ranked();
  kit.add(1, game);
  assert.deepEqual(kit.refresh().failed, ['test-record-id']);
  assert.deepEqual(kit.logs, [{ ok: false, error: 'records_view_failed', view: 'test-record-id', stage: 'record_id' }]);
  assert.equal(kit.answer('test-record-id').reason, 'not_published');
  assert.equal(kit.answer('test-winrate').available, true, 'the other view publishes');
  const rows = JSON.stringify(kit.db.prepare('SELECT mode, body FROM snap').all());
  assert.equal(rows.includes(game.id), false, 'the record id is in no published row');
});

test('derivers run in order and fill rec.d once for every view; one that throws is counted, and the pass goes on', () => {
  const order = [];
  const deck = { name: 'deck', order: 10, version: 'deck-t1', derive: rec => { order.push('deck'); rec.d.deckKey = `k-${rec.body.cls}`; } };
  const arch = { name: 'archetypes', order: 20, version: 'arch-t1', derive: rec => { order.push('arch'); rec.d.arch = rec.d.deckKey === 'k-MAGE' ? 'Spell Mage' : null; } };
  const bad = { name: 'bad', order: 30, version: 'bad-t1', derive: () => { throw new Error('bad'); } };
  const archView = traceView({ key: 'test-arch', start: () => ({}), add: (acc, rec) => { acc[rec.d.arch ?? 'none'] = (acc[rec.d.arch ?? 'none'] ?? 0) + 1; }, finish: acc => acc });
  const kit = recordsKit({ views: [archView, traceView()], derivers: [bad, arch, deck] });
  kit.add(1, ranked({ cls: 'MAGE' }), ranked({ cls: 'MAGE' }), ranked({ cls: 'WARRIOR' }));
  kit.refresh();
  assert.deepEqual(order, ['deck', 'arch', 'deck', 'arch', 'deck', 'arch'], 'once a record, by order, not once a view');
  const a = kit.answer('test-arch');
  assert.deepEqual(a.data, { 'Spell Mage': 2, none: 1 });
  assert.deepEqual(a.derivers, { deck: 'deck-t1', archetypes: 'arch-t1', bad: 'bad-t1' });
  assert.deepEqual(kit.logs, [{ ok: false, error: 'records_derive_failed', deriver: 'bad', records: 3 }]);
});

test('a pass that fails leaves every published row as it was', () => {
  const kit = recordsKit({ views: [traceView()] });
  kit.add(1, ranked());
  kit.refresh();
  const published = kit.answer('test-trace').published;
  kit.add(1, ranked());
  kit.db.exec("CREATE TRIGGER publish_fails BEFORE UPDATE ON snap BEGIN SELECT RAISE(ABORT, 'synthetic'); END");
  assert.throws(() => kit.refresh({ force: true }), /synthetic/);
  kit.db.exec('DROP TRIGGER publish_fails');
  const a = kit.answer('test-trace');
  assert.equal(a.published, published);
  assert.equal(a.data.records.length, 1);
});

test('the view of a removed module is no longer kept', () => {
  const kit = recordsKit({ views: [traceView(), traceView({ key: 'test-gone' })] });
  kit.add(1, ranked());
  kit.refresh();
  const rows = () => kit.db.prepare("SELECT mode FROM snap WHERE mode LIKE 'records:%' ORDER BY mode").all().map(r => r.mode);
  assert.deepEqual(rows(), ['records:test-gone', 'records:test-trace']);
  kit.modules.views.splice(kit.modules.views.findIndex(v => v.key === 'test-gone'), 1);
  assert.equal(kit.refresh().built, true, 'another set of views');
  assert.deepEqual(rows(), ['records:test-trace']);
});

// ---- The loader (AC8, loader part) ---------------------------------------------------------------------------------

const VIEW = (key, extra = '') => `export const key = '${key}'; export const metric = 'test.metric'; export const rules = 'test-1';
export const line = 'constructed'; export const modes = ['ranked']; export const query = {};
export const start = () => []; export const add = (acc) => {}; export const finish = () => ({}); export const slice = b => b; ${extra}`;

function folder(t, files) {
  const dir = mkdtempSync(path.join(tmpdir(), 'hp-records-loader-'));
  t.after(() => rmSync(dir, { recursive: true, force: true }));
  for (const [name, text] of Object.entries(files)) {
    mkdirSync(path.join(dir, path.dirname(name)), { recursive: true });
    writeFileSync(path.join(dir, name), text);
  }
  return dir;
}

test('AC8: the loader finds derivers and views by folder, orders them and leaves out each module that breaks the contract', async t => {
  const dir = folder(t, {
    'derive/b-second.mjs': "export const name = 'second'; export const order = 20; export const version = 'v2'; export const derive = () => {};",
    'derive/a-first.mjs': "export const name = 'first'; export const order = 10; export const version = 'v1'; export const derive = () => {};",
    'derive/no-version.mjs': "export const name = 'nover'; export const order = 5; export const derive = () => {};",
    'derive/again.mjs': "export const name = 'first'; export const order = 30; export const version = 'v3'; export const derive = () => {};",
    'views/good.mjs': VIEW('good'),
    'views/mismatch.mjs': VIEW('other'),
    'views/no-slice.mjs': VIEW('no-slice').replace('export const slice = b => b;', ''),
    'views/bad-mode.mjs': VIEW('bad-mode').replace("['ranked']", "['casual']"),
    'views/bad-query.mjs': VIEW('bad-query').replace('export const query = {};', 'export const query = { window: v => v };'),
    'views/throws.mjs': "throw new Error('at import');",
    'views/notes.txt': 'not a module',
  });
  const lines = [];
  const loaded = await loadModules({ dir, catalog: null, log: line => lines.push(JSON.parse(line)) });
  assert.deepEqual(loaded.derivers.map(d => [d.name, d.order, d.version]), [['first', 10, 'v1'], ['second', 20, 'v2']]);
  assert.deepEqual(loaded.views.map(v => v.key), ['good']);
  assert.deepEqual(loaded.rejected, [
    { module: 'derive/again.mjs', reason: 'duplicate' },
    { module: 'derive/no-version.mjs', reason: 'version' },
    { module: 'views/bad-mode.mjs', reason: 'modes' },
    { module: 'views/bad-query.mjs', reason: 'query' },
    { module: 'views/mismatch.mjs', reason: 'key' },
    { module: 'views/no-slice.mjs', reason: 'functions' },
    { module: 'views/throws.mjs', reason: 'import' },
  ]);
  assert.deepEqual(lines.map(l => l.error), Array(7).fill('records_module_rejected'));
  assert.equal(lines.every(l => Object.keys(l).join() === 'ok,error,module,reason'), true, 'a code, the file and the reason: nothing else');
});

test('AC8: a view loads only when its metric is implemented in the catalogue with the same rules version', async t => {
  const dir = folder(t, {
    'views/ok.mjs': VIEW('ok'),
    'views/old-rules.mjs': VIEW('old-rules').replace("'test-1'", "'test-0'"),
    'views/defined.mjs': VIEW('defined').replace("'test.metric'", "'test.defined'"),
    'views/absent.mjs': VIEW('absent').replace("'test.metric'", "'test.absent'"),
  });
  const catalog = new Map([
    ['test.metric', { id: 'test.metric', status: 'implemented', rulesVersion: 'test-1', api: 'GET /stats/v2/analytics/records/ok' }],
    ['test.defined', { id: 'test.defined', status: 'defined' }],
  ]);
  const loaded = await loadModules({ dir, catalog, log: () => {} });
  assert.deepEqual(loaded.views.map(v => v.key), ['ok']);
  assert.deepEqual(loaded.rejected.map(r => [r.module, r.reason]), [
    ['views/absent.mjs', 'catalog'], ['views/defined.mjs', 'catalog'], ['views/old-rules.mjs', 'catalog'],
  ]);
  assert.deepEqual(catalogProblems(catalog, loaded.views), []);
  catalog.set('test.waiting', { id: 'test.waiting', status: 'implemented', rulesVersion: 'w-1', api: 'GET /stats/v2/analytics/records/waiting?fmt=standard' });
  assert.deepEqual(catalogProblems(catalog, loaded.views), [{ metric: 'test.waiting', problem: 'no_view', view: 'waiting' }]);
});

test('an unreadable catalogue is logged and the views load without its check; a missing one is no catalogue', async t => {
  const dir = folder(t, { 'views/ok.mjs': VIEW('ok'), 'catalog.json': '{ not json' });
  const lines = [];
  const broken = await loadModules({ dir, catalogFile: path.join(dir, 'catalog.json'), log: line => lines.push(JSON.parse(line)) });
  assert.deepEqual(broken.views.map(v => v.key), ['ok']);
  assert.deepEqual(lines, [{ ok: false, error: 'records_catalog_unreadable' }]);
  const missing = await loadModules({ dir, catalogFile: path.join(dir, 'absent.json'), log: line => lines.push(line) });
  assert.deepEqual(missing.views.map(v => v.key), ['ok']);
  assert.equal(lines.length, 1, 'nothing logged for a deployed pool without docs/');
  assert.throws(() => readCatalog(path.join(dir, 'catalog.json')), SyntaxError, 'the strict read, as CI uses it');
});

test('AC8: the views in this checkout agree with docs/analytics-metrics.json', async () => {
  const catalog = readCatalog();
  assert.ok(catalog instanceof Map && catalog.size > 0, 'the catalogue is in the checkout');
  const loaded = await loadModules({ catalog, log: () => {} });
  assert.deepEqual(loaded.rejected, [], 'every module of node/records loads');
  for (const view of loaded.views) {
    const metric = catalog.get(view.metric);
    assert.equal(metric.status, 'implemented', view.key);
    assert.equal(metric.rulesVersion, view.rules, view.key);
  }
  assert.deepEqual(catalogProblems(catalog, loaded.views), [], 'a metric naming a records route has its view');
});

test('withdraw() empties every view row and leaves the state row', () => {
  const kit = recordsKit({ views: [traceView()] });
  kit.add(1, ranked());
  kit.refresh();
  assert.equal(kit.withdraw(), 1);
  assert.equal(kit.withdraw(), 0, 'already withdrawn');
  assert.equal(kit.answer('test-trace').reason, 'rebuilding');
  assert.deepEqual(kit.status(), { built: kit.now, views: 1, published: 0, withdrawn: 1, failed: 0 });
  kit.advance(DAY);
  assert.equal(kit.refresh().built, true);
  assert.equal(kit.status().published, 1);
});
