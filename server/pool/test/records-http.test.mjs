// GET /v2/analytics/records/<view> on the Node service (TASK-2026-11-26-pro-collection AC7, contract K4): the answers
// before and after a pass, the refusals, the routes that were there before, the minute's upkeep, /healthz, and the
// nginx include that opens the route.
import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { startPool } from '../node/server.mjs';
import { DAY, ID, setup } from './capture-fixture.mjs';
import { traceView, winrateView } from './records-kit.mjs';

const RECORDS = { modules: { views: [winrateView(), traceView()], derivers: [] }, patches: { v: 1, lines: { constructed: [], battlegrounds: [], arena: [] } } };
const iid = n => (0xabcd00 + n).toString(16).padStart(32, '0');
const game = (n, o = {}) => ({ id: (0x99000 + n).toString(16).padStart(32, '0'), day: DAY, mode: 'ranked', fmt: 'standard', rank: 'gold', cls: 'MAGE', res: n % 2 ? 'W' : 'L', ...o });
const get = (pool, path) => fetch(`${pool.url}${path}`);
const post = (pool, path, body) => fetch(`${pool.url}${path}`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) });

/** Twelve games of six installs, through the public intake. */
async function seed(pool) {
  for (let n = 0; n < 12; n += 2) {
    const answer = await post(pool, '/v1/events', { v: 1, iid: iid(n), games: [game(n), game(n + 1)] });
    assert.equal(answer.status, 200);
  }
}

test('AC7: before any pass a view answers 200 not_published; refusals are 400, 404 and 405; no-store throughout', async t => {
  const f = await setup(t, { records: RECORDS });
  const answer = await get(f.pool, '/v2/analytics/records/test-winrate');
  assert.equal(answer.status, 200);
  assert.equal(answer.headers.get('cache-control'), 'no-store');
  assert.equal(answer.headers.get('content-type'), 'application/json');
  const body = await answer.json();
  assert.deepEqual([body.v, body.source, body.view, body.metric, body.rulesVersion], [2, 'game_records', 'test-winrate', 'test.winrate', 'test-winrate-1']);
  assert.deepEqual([body.available, body.reason, body.data, body.period, body.windows], [false, 'not_published', null, null, []]);

  for (const query of ['?cls=MAGE', '?fmt=classic', '?window=1y', '?rank=d5', '?fmt=standard&fmt=wild', '?patch=..%2F..']) {
    const refused = await get(f.pool, `/v2/analytics/records/test-winrate${query}`);
    assert.equal(refused.status, 400, query);
    assert.equal(refused.headers.get('cache-control'), 'no-store');
    assert.deepEqual(await refused.json(), { ok: false, error: 'invalid_query' }, query);
  }
  for (const path of ['/v2/analytics/records/nope', '/v2/analytics/records/', '/v2/analytics/records/test-winrate/x']) {
    const missing = await get(f.pool, path);
    assert.equal(missing.status, 404, path);
    assert.deepEqual(await missing.json(), { ok: false, error: 'unknown_view' }, path);
  }
  for (const path of ['/v2/analytics/other', '/v2/analytics/records', '/v2/x']) {
    const other = await get(f.pool, path);
    assert.equal(other.status, 404, path);
    assert.deepEqual(await other.json(), { ok: false, error: 'not_found' }, `${path}: answered outside the write queue`);
  }
  for (const method of ['POST', 'PUT', 'DELETE']) {
    const wrong = await fetch(`${f.pool.url}/v2/analytics/records/test-winrate`, { method, body: method === 'DELETE' ? undefined : '{}' });
    assert.equal(wrong.status, 405, method);
    assert.equal((await wrong.json()).error, 'method_not_allowed');
  }
  assert.equal(f.pool.store.sqlite.prepare("SELECT COUNT(*) AS n FROM snap WHERE mode LIKE 'records%'").get().n, 0, 'no request builds anything');
});

test('AC7: after the pass the view is published, and the routes that were there before answer as before', async t => {
  const f = await setup(t, { records: RECORDS });
  await seed(f.pool);
  assert.deepEqual((await f.pool.refreshRecords()).built, true);
  const published = await (await get(f.pool, '/v2/analytics/records/test-winrate?rank=gold')).json();
  assert.equal(published.available, true);
  assert.deepEqual(published.period.patch, 'unknown');
  assert.deepEqual(published.data.rows.map(r => [r.cls, r.games, r.installs, r.winrate.num, r.winrate.den]), [['MAGE', 12, 6, 6, 12]]);
  const text = JSON.stringify(published);
  for (let n = 0; n < 12; n += 2) assert.equal(text.includes(iid(n)), false, 'no install id');

  const heroes = await get(f.pool, '/v2/analytics/bg/heroes?mode=solo');
  assert.equal(heroes.status, 200);
  assert.deepEqual([(await heroes.json()).reason], ['not_published']);
  assert.equal((await get(f.pool, '/v2/analytics/bg/heroes?mode=x')).status, 400);
  assert.equal((await (await get(f.pool, '/v1/events/summary')).json()).games, 12);
  assert.equal((await get(f.pool, '/v1/events/mulligan-pairs?fmt=standard&cls=MAGE')).status, 200);

  const health = await (await get(f.pool, '/healthz')).json();
  assert.deepEqual({ ...health.records, built: typeof health.records.built }, { built: 'number', views: 2, published: 2, withdrawn: 0, failed: 0 });
  assert.equal(JSON.stringify(health).includes(iid(0)), false);
});

test('AC6 on the service: the minute upkeep that deletes nothing keeps the views; a forget withdraws them until the next pass', async t => {
  const f = await setup(t, { records: RECORDS });
  await seed(f.pool);
  await f.pool.refreshRecords();
  const lines = [];
  await f.pool.maintenance(line => lines.push(line));
  assert.deepEqual(lines, []);
  assert.equal((await (await get(f.pool, '/v2/analytics/records/test-trace')).json()).available, true, 'nothing old was deleted');
  assert.equal((await f.pool.refreshRecords()).built, false);

  assert.deepEqual(await (await post(f.pool, '/v1/events/forget', { iid: iid(0) })).json(), { ok: true, deleted: 2 });
  for (const view of ['test-winrate', 'test-trace']) {
    const a = await (await get(f.pool, `/v2/analytics/records/${view}`)).json();
    assert.deepEqual([a.available, a.reason, a.data], [false, 'rebuilding', null], view);
  }
  assert.equal((await f.pool.refreshRecords()).built, true, 'a withdrawn view rebuilds on the next pass');
  const rebuilt = await (await get(f.pool, '/v2/analytics/records/test-winrate')).json();
  assert.deepEqual([rebuilt.data.rows[0].games, rebuilt.data.rows[0].installs], [10, 5]);
});

test('N5: a malformed balance-patches.json switches the views off with its own line; the service starts and takes records', async t => {
  const f = await setup(t, { records: RECORDS });
  await seed(f.pool);
  await f.pool.refreshRecords();
  assert.equal((await (await get(f.pool, '/v2/analytics/records/test-winrate')).json()).available, true);
  await f.pool.close();

  // The next deploy ships a file cut short: the service starts, says why in one line, and keeps its other work.
  const dir = mkdtempSync(path.join(tmpdir(), 'hp-records-patches-'));
  t.after(() => rmSync(dir, { recursive: true, force: true }));
  const patchesFile = path.join(dir, 'balance-patches.json');
  writeFileSync(patchesFile, '{ "v": 1, "lines": { "constructed": [ { "id": "b1", "fromBuild": ');
  const lines = [];
  const pool = await startPool({ dataDir: f.dataDir, port: 0, schedule: false, archive: { minFreeBytes: 0 },
    records: { modules: RECORDS.modules, patchesFile, log: line => lines.push(JSON.parse(line)) } });
  // Closed here, before the fixture removes the data folder it holds.
  try {
    assert.deepEqual(lines, [{ ok: false, error: 'records_patches_invalid', cause: 'invalid_balance_patches' }], 'a code, nothing of the file');
    assert.deepEqual(pool.recordModules.views, [], 'no view loads');

    const stored = await post(pool, '/v1/events', { v: 1, iid: iid(20), games: [game(20), game(21)] });
    assert.deepEqual([stored.status, await stored.json()], [200, { ok: true, stored: 2 }], 'the intake works');
    assert.equal((await (await get(pool, '/v1/events/summary')).json()).games, 14);
    assert.deepEqual(await pool.refreshRecords(), { built: true, views: 0, failed: [], records: 0 }, 'the pass reads nothing');
    for (const view of ['test-winrate', 'test-trace']) {
      const off = await get(pool, `/v2/analytics/records/${view}`);
      assert.deepEqual([off.status, await off.json()], [404, { ok: false, error: 'unknown_view' }], view);
    }
    assert.equal(pool.store.sqlite.prepare("SELECT COUNT(*) AS n FROM snap WHERE mode LIKE 'records:%'").get().n, 0,
      'the rows published under the old file are gone');
    const health = await (await get(pool, '/healthz')).json();
    assert.deepEqual({ ...health.records, built: typeof health.records.built }, { built: 'number', views: 0, published: 0, withdrawn: 0, failed: 0 });
  } finally {
    await pool.close();
  }
});

test('the shipped service loads the views of this checkout and answers an unknown view with the service own 404', async t => {
  const f = await setup(t);
  assert.deepEqual(f.pool.recordModules.views.map(v => v.key).filter(key => key.startsWith('test-')), []);
  const missing = await get(f.pool, '/v2/analytics/records/constructed-mulligan-x');
  assert.equal(missing.status, 404);
  assert.deepEqual(await missing.json(), { ok: false, error: 'unknown_view' });
  const health = await (await get(f.pool, '/healthz')).json();
  assert.equal(health.records.built, null, 'no pass yet');
  assert.equal(JSON.stringify(health).includes(ID.iid), false);
  // The pass runs on its own timer with its own failure code, as the other minute jobs do.
  const source = readFileSync(new URL('../node/server.mjs', import.meta.url), 'utf8');
  assert.match(source, /refreshRecords\(\)\.catch\(reportFailure\('records_refresh_failed'/);
});

test('the nginx include opens every analytics view read-only with the read zone, and no longer only the hero route', () => {
  const conf = readFileSync(new URL('../deploy/nginx-source-capture.conf', import.meta.url), 'utf8');
  assert.equal(conf.includes('location = /stats/v2/analytics/bg/heroes'), false);
  const block = /location \^~ \/stats\/v2\/analytics\/ \{([^}]*\{[^}]*\}[^}]*)\}/.exec(conf)?.[1];
  assert.ok(block, 'one prefix location for /stats/v2/analytics/');
  for (const directive of ['limit_except GET { deny all; }', 'limit_req zone=hearthpulse_stats_read burst=20 nodelay;', 'proxy_cache off;',
    'access_log off;', 'proxy_set_header Authorization "";', 'proxy_set_header X-Forwarded-For "";', 'rewrite ^/stats(/.*)$ $1 break;',
    'proxy_pass http://127.0.0.1:3181;']) assert.ok(block.includes(directive), directive);
  assert.equal(block.includes('add_header'), false, 'an add_header here would drop the server block\'s headers');
});
