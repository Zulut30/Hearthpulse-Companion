// The record fields of TASK-2026-11-26-pro-collection (AC3, contract K1): build, band, bg.comp and the Arena run
// summary, through POST /v1/events of the shared code on the real schema. A bad new field is dropped and the record
// kept; a bad run summary is refused.
import assert from 'node:assert/strict';
import test from 'node:test';
import worker from '../src/index.js';
import { validateEvent } from '../src/events.js';
import { openDatabase } from '../node/sqlite.mjs';

const TODAY = Math.floor(Date.now() / 86_400_000);
const IID = 'c'.repeat(32);
let sequence = 0;
const id = () => (0x5eed0000 + ++sequence).toString(16).padStart(32, '0');

const ranked = (o = {}) => ({ id: id(), mode: 'ranked', fmt: 'standard', rank: 'diamond', cls: 'MAGE', res: 'W', ...o });
const bg = (b = {}, o = {}) => ({ id: id(), mode: 'bg', res: 'W', bg: { placement: 3, mmr: 6500, ...b }, ...o });
const run = (r = {}, o = {}) => ({ id: id(), mode: 'arena_run', run: { kind: 'arena', cls: 'PRIEST', wins: 7, losses: 3, ...r }, ...o });

function pool() {
  const store = openDatabase(':memory:');
  const post = (path, body) => worker.fetch(new Request(`https://pool.test${path}`, {
    method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body),
  }), { DB: store.DB }, {});
  const stored = recordId => {
    const row = store.sqlite.prepare('SELECT mode, fmt, body FROM e WHERE id = ?').get(recordId);
    return row ? { mode: row.mode, fmt: row.fmt, body: JSON.parse(row.body) } : null;
  };
  return { store, post, stored };
}

test('AC3: valid new fields are stored: build, the d4-1 band of diamond, the main tribe of a Battlegrounds board', async () => {
  const p = pool();
  const games = [ranked({ build: 231577 }), ranked({ band: 'd4-1', build: 1 }), bg({ comp: 'BEAST' }, { build: 99_999_999 }), bg({ comp: 'MIXED' }, { mode: 'duos' })];
  const answer = await p.post('/v1/events', { v: 1, iid: IID, games });
  assert.equal(answer.status, 200);
  assert.deepEqual(await answer.json(), { ok: true, stored: 4 });
  assert.equal(p.stored(games[0].id).body.build, 231577);
  assert.deepEqual([p.stored(games[1].id).body.rank, p.stored(games[1].id).body.band], ['diamond', 'd4-1']);
  assert.equal(p.stored(games[2].id).body.bg.comp, 'BEAST');
  assert.equal(p.stored(games[2].id).body.build, 99_999_999);
  assert.equal(p.stored(games[3].id).body.bg.comp, 'MIXED');
});

test('AC3: a bad new field is dropped and its record is still stored', async () => {
  const p = pool();
  const games = [
    ranked({ build: 0 }), ranked({ build: 'x' }), ranked({ build: 100_000_000 }), ranked({ build: 2.5 }),
    ranked({ rank: 'gold', band: 'd4-1' }), ranked({ band: 'd5' }), ranked({ rank: undefined, band: 'd4-1' }),
    ranked({ mode: 'casual', band: 'd4-1' }), bg({ comp: 'beast' }), bg({ comp: 'B' }), bg({ comp: ['BEAST'] }),
  ];
  const answer = await p.post('/v1/events', { v: 1, iid: IID, games });
  assert.deepEqual(await answer.json(), { ok: true, stored: games.length });
  for (const g of games) {
    const { body } = p.stored(g.id);
    assert.equal(body.build, undefined, JSON.stringify(g));
    assert.equal(body.band, undefined, JSON.stringify(g));
    assert.equal(body.bg?.comp, undefined, JSON.stringify(g));
  }
  assert.equal(p.stored(games[4].id).body.rank, 'gold', 'the rest of the record stays');
  assert.equal(p.stored(games[8].id).body.bg.placement, 3);
});

test('AC3: a finished Arena run is stored as K1 says, without a result or a format', async () => {
  const p = pool();
  const deck = 'AAECAa0GBpYFAA==';
  const games = [
    run({ deck }, { build: 231577, app: '0.31.0', res: 'W', fmt: 'standard', turns: 9, name: 'Player#1234' }),
    run({ kind: 'arena', wins: 12, losses: 2 }),
    run({ kind: 'underground', wins: 3, losses: 5 }),
    run({ kind: 'underground', wins: 12, losses: 4, deck: 'not a deck!' }),
  ];
  const answer = await p.post('/v1/events', { v: 1, iid: IID, games });
  assert.deepEqual(await answer.json(), { ok: true, stored: 4 });
  const first = p.stored(games[0].id);
  assert.deepEqual(first, {
    mode: 'arena_run', fmt: null,
    body: { id: games[0].id, day: TODAY, mode: 'arena_run', app: '0.31.0', build: 231577, run: { kind: 'arena', cls: 'PRIEST', wins: 7, losses: 3, deck } },
  });
  assert.deepEqual(p.stored(games[3].id).body.run, { kind: 'underground', cls: 'PRIEST', wins: 12, losses: 4 }, 'a bad deck code is dropped');
});

test('AC3: a run that is not over, past its loss limit or without its summary is refused; a batch of only those is a 400', async () => {
  const p = pool();
  const refused = [
    run({ wins: 5, losses: 2 }), run({ losses: 4 }), run({ wins: 13, losses: 1 }), run({ kind: 'underground', wins: 4, losses: 6 }),
    run({ kind: 'underground', wins: 2, losses: 3 }), run({ wins: 12, losses: 3 }), run({ kind: 'draft' }), run({ cls: 'priest' }),
    run({ wins: '7' }), { ...run(), run: undefined }, { ...run(), run: [7, 3] },
  ];
  for (const g of refused) assert.equal(validateEvent(g, TODAY), null, JSON.stringify(g));
  const answer = await p.post('/v1/events', { v: 1, iid: IID, games: refused });
  assert.equal(answer.status, 400);
  assert.equal(p.store.sqlite.prepare('SELECT COUNT(*) AS n FROM e').get().n, 0);
  assert.equal(p.store.sqlite.prepare('SELECT COUNT(*) AS n FROM equota').get().n, 0, 'nothing counted against the day');

  const mixed = await p.post('/v1/events', { v: 1, iid: IID, games: [refused[0], ranked(), run()] });
  assert.deepEqual(await mixed.json(), { ok: true, stored: 2 }, 'the good records of a mixed batch are stored');
});

test('the record without the new fields is stored exactly as before', () => {
  const g = { id: 'd'.repeat(32), day: TODAY, mode: 'ranked', fmt: 'wild', rank: 'legend', cls: 'ROGUE', res: 'L', turns: 8, secs: 400, app: '0.30.2', first: false };
  assert.equal(validateEvent(g, TODAY).body,
    `{"id":"${'d'.repeat(32)}","day":${TODAY},"mode":"ranked","res":"L","fmt":"wild","turns":8,"secs":400,"app":"0.30.2","rank":"legend","cls":"ROGUE","first":false}`);
});
