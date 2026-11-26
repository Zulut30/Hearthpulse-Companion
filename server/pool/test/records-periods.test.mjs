// Windows by balance patch (TASK-2026-11-26-pro-collection AC4, contract K3): which records each window holds, the
// days and builds it reports, and the patches file the service refuses.
import assert from 'node:assert/strict';
import test from 'node:test';
import { checkPatches, patchOf, readPatches, windowsOf } from '../node/records/periods.mjs';
import { coinOf, mmrOf, ranksOf } from '../node/records/buckets.mjs';
import { DAY, NO_PATCHES, TODAY, battlegrounds, ranked, recordsKit, traceView } from './records-kit.mjs';

const T = TODAY;
// P1 from build 100, P2 from build 200 (the AC's two patches), each with the day it came out.
const PATCHES = { v: 1, lines: { constructed: [{ id: 'P1', fromBuild: 100, fromDay: T - 80 }, { id: 'P2', fromBuild: 200, fromDay: T - 35 }], battlegrounds: [], arena: [] } };

/** The records the trace view received in a window, as "build@T-n" ("-" for no build), in day order. */
const seen = (kit, query) => {
  const a = kit.answer('test-trace', query);
  assert.equal(a.available, true, JSON.stringify(query));
  return a.data.records.map(([build, day]) => `${build ?? '-'}@T-${T - day}`).sort();
};

test('each window holds the records of its patch and days, an unknown build only the unknown 30 days, nothing older than 90 days', () => {
  const kit = recordsKit({ views: [traceView()], patches: PATCHES });
  const at = (build, ago) => ranked({ day: T - ago, ...(build === null ? {} : { build }) });
  kit.add(1,
    // P1: builds 100 and 150; the first day read (T-89) counts, T-90 is not read.
    at(100, 50), at(150, 40), at(100, 89), at(100, 90),
    // P2: builds 200 and 210 on both sides of the 7- and 30-day edges.
    at(210, 3), at(210, 6), at(200, 7), at(200, 15), at(210, 29), at(200, 30),
    // Unknown: no build, or a build below the first patch.
    at(null, 10), at(null, 29), at(null, 30), at(50, 5),
  );
  // Dated tomorrow by a clock that runs ahead: stored, not read until that day.
  kit.add(2, ranked({ day: T + 1, build: 210 }));
  // Another line: a Battlegrounds record with a P2 build is not a Constructed record.
  kit.add(3, battlegrounds({ mmr: 6000 }, { build: 210, day: T - 3 }));
  assert.equal(kit.refresh().built, true);

  const all = kit.answer('test-trace');
  assert.deepEqual(all.windows, [
    { patch: 'P2', window: 'patch' }, { patch: 'P2', window: '7d' }, { patch: 'P2', window: '30d' },
    { patch: 'P1', window: 'patch' }, { patch: 'unknown', window: '30d' },
  ]);
  assert.deepEqual(all.period, { line: 'constructed', patch: 'P2', builds: [200, 210], window: 'patch', fromDay: T - 35, toDay: T }, 'no patch given: the newest, whole');

  assert.deepEqual(seen(kit, { patch: 'P2', window: 'patch' }), ['200@T-15', '200@T-30', '200@T-7', '210@T-29', '210@T-3', '210@T-6']);
  assert.deepEqual(seen(kit, { patch: 'P2', window: '7d' }), ['210@T-3', '210@T-6'], 'day >= T-6');
  assert.deepEqual(seen(kit, { patch: 'P2', window: '30d' }), ['200@T-15', '200@T-7', '210@T-29', '210@T-3', '210@T-6'], 'day >= T-29');
  assert.deepEqual(seen(kit, { patch: 'P1' }), ['100@T-50', '100@T-89', '150@T-40'], 'P1 joins 100 and 150; T-90 is not read');
  assert.deepEqual(seen(kit, { patch: 'unknown' }), ['-@T-10', '-@T-29', '50@T-5'], 'unknown: 30 days, no build or below the first patch');

  assert.deepEqual(kit.answer('test-trace', { patch: 'P2', window: '7d' }).period, { line: 'constructed', patch: 'P2', builds: [210, 210], window: '7d', fromDay: T - 6, toDay: T });
  assert.deepEqual(kit.answer('test-trace', { patch: 'P1' }).period, { line: 'constructed', patch: 'P1', builds: [100, 150], window: 'patch', fromDay: T - 80, toDay: T - 36 },
    'the previous patch ends the day before the newest starts');
  assert.deepEqual(kit.answer('test-trace', { patch: 'unknown' }).period, { line: 'constructed', patch: 'unknown', builds: [50, 50], window: '30d', fromDay: T - 29, toDay: T });

  // Windows that do not exist answer no_window with the list of those that do.
  for (const query of [{ patch: 'P1', window: '7d' }, { patch: 'P1', window: '30d' }, { patch: 'unknown', window: 'patch' }, { patch: 'P0' }]) {
    const a = kit.answer('test-trace', query);
    assert.equal(a.available, false, JSON.stringify(query));
    assert.equal(a.reason, 'no_window');
    assert.equal(a.data, null);
    assert.deepEqual(a.windows, all.windows);
  }
});

test('the next day moves the 7- and 30-day windows: the pass is due again and rebuilds them', () => {
  const kit = recordsKit({ views: [traceView()], patches: PATCHES });
  kit.add(1, ranked({ day: T - 6, build: 210 }), ranked({ day: T - 29, build: 210 }), ranked({ day: T - 29 }));
  kit.refresh();
  assert.equal(kit.refresh().built, false, 'nothing changed');
  kit.advance(DAY);
  assert.equal(kit.refresh().built, true, 'a new day');
  assert.deepEqual(seen(kit, { patch: 'P2', window: '7d' }), [], 'T-6 is now seven days back');
  assert.deepEqual(seen(kit, { patch: 'P2', window: '30d' }), ['210@T-6'], 'T-29 left the 30 days');
  assert.deepEqual(seen(kit, { patch: 'unknown' }), []);
  assert.deepEqual(seen(kit, { patch: 'P2' }), ['210@T-29', '210@T-6'], 'the whole patch keeps both');
});

test('with no patches every record is unknown, and with one patch there is no previous window', () => {
  const none = recordsKit({ views: [traceView()], patches: NO_PATCHES });
  none.add(1, ranked({ build: 300 }), ranked());
  none.refresh();
  const a = none.answer('test-trace');
  assert.deepEqual(a.windows, [{ patch: 'unknown', window: '30d' }]);
  assert.deepEqual(a.period.patch, 'unknown', 'no patch given and none in the line: the unknown window');
  assert.equal(a.data.records.length, 2);

  const one = { v: 1, lines: { constructed: [{ id: 'b300', fromBuild: 300, fromDay: T - 3 }] } };
  const w = windowsOf(checkPatches(one), 'constructed', T);
  assert.deepEqual(w.map(x => [x.patch, x.window, x.fromDay, x.toDay]), [
    ['b300', 'patch', T - 3, T], ['b300', '7d', T - 3, T], ['b300', '30d', T - 3, T], ['unknown', '30d', T - 29, T],
  ], 'windows of a 3-day-old patch start on its day');
  assert.deepEqual(windowsOf(checkPatches(one), 'arena', T).map(x => x.patch), ['unknown'], 'a line missing from the file has no patch');
});

test('the patch of a build is the entry with the highest fromBuild not above it', () => {
  const p = checkPatches(PATCHES);
  assert.equal(patchOf(p, 'constructed', 99), null);
  assert.equal(patchOf(p, 'constructed', 100), 'P1');
  assert.equal(patchOf(p, 'constructed', 199), 'P1');
  assert.equal(patchOf(p, 'constructed', 200), 'P2');
  assert.equal(patchOf(p, 'constructed', 99_999_999), 'P2');
  assert.equal(patchOf(p, 'constructed', null), null);
  assert.equal(patchOf(p, 'battlegrounds', 250), null, 'lines do not share patches');
  // The file's order does not matter; the list is ordered by build.
  const shuffled = checkPatches({ v: 1, lines: { constructed: [PATCHES.lines.constructed[1], PATCHES.lines.constructed[0]] } });
  assert.deepEqual(shuffled.lines.constructed.map(e => e.id), ['P1', 'P2']);
});

test('a patches file the pass could misread is refused, and the shipped file is valid', () => {
  const entry = (o = {}) => ({ id: 'P1', fromBuild: 100, fromDay: T, ...o });
  for (const [why, raw] of [
    ['not an object', null], ['version', { v: 2, lines: {} }], ['lines missing', { v: 1 }], ['lines an array', { v: 1, lines: [] }],
    ['unknown line', { v: 1, lines: { wild: [] } }], ['line not a list', { v: 1, lines: { constructed: {} } }],
    ['id unknown', { v: 1, lines: { constructed: [entry({ id: 'unknown' })] } }], ['id shape', { v: 1, lines: { constructed: [entry({ id: 'p 1' })] } }],
    ['build not an integer', { v: 1, lines: { constructed: [entry({ fromBuild: '100' })] } }], ['build too big', { v: 1, lines: { constructed: [entry({ fromBuild: 10 ** 8 })] } }],
    ['day missing', { v: 1, lines: { constructed: [{ id: 'P1', fromBuild: 100 }] } }],
    ['repeated id', { v: 1, lines: { constructed: [entry(), entry({ fromBuild: 200 })] } }],
    ['repeated build', { v: 1, lines: { constructed: [entry(), entry({ id: 'P2' })] } }],
    ['later build, earlier day', { v: 1, lines: { constructed: [entry(), entry({ id: 'P2', fromBuild: 200, fromDay: T - 1 })] } }],
  ]) assert.throws(() => checkPatches(raw), /invalid_balance_patches/, why);
  const shipped = readPatches();
  for (const line of ['constructed', 'battlegrounds', 'arena']) assert.ok(shipped.lines[line].length >= 1, `${line} has its first patch`);
  assert.throws(() => readPatches(new URL('./missing-balance-patches.json', import.meta.url)), /invalid_balance_patches/);
});

test('rank buckets, MMR ranges and the coin of a record', () => {
  assert.deepEqual(ranksOf('ranked', { rank: 'gold' }), ['all', 'gold']);
  assert.deepEqual(ranksOf('ranked', { rank: 'diamond' }), ['all', 'diamond']);
  assert.deepEqual(ranksOf('ranked', { rank: 'diamond', band: 'd4-1' }), ['all', 'diamond', 'd4-1', 'd4-legend']);
  assert.deepEqual(ranksOf('ranked', { rank: 'legend' }), ['all', 'legend', 'd4-legend']);
  assert.deepEqual(ranksOf('ranked', {}), ['all'], 'no rank: all only');
  assert.deepEqual(ranksOf('casual', { rank: 'gold' }), ['all'], 'leagues are Ranked only');
  assert.deepEqual(mmrOf('bg', { bg: { mmr: 3500 } }), ['all', '0-3999']);
  assert.deepEqual(mmrOf('bg', { bg: { mmr: 4000 } }), ['all', '4000-5999']);
  assert.deepEqual(mmrOf('duos', { bg: { mmr: 6000 } }), ['all', '6000-7999']);
  assert.deepEqual(mmrOf('bg', { bg: { mmr: 7999 } }), ['all', '6000-7999']);
  assert.deepEqual(mmrOf('bg', { bg: { mmr: 9500 } }), ['all', '8000+']);
  assert.deepEqual(mmrOf('bg', { bg: {} }), ['all'], 'no MMR: all only');
  assert.deepEqual(mmrOf('ranked', { bg: { mmr: 9500 } }), ['all']);
  assert.deepEqual(coinOf({ first: true }), ['any', 'first']);
  assert.deepEqual(coinOf({ first: false }), ['any', 'coin']);
  assert.deepEqual(coinOf({}), ['any']);
});
