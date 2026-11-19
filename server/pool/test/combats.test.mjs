import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { ID, DAY, record, jsonl, chunk, manifest, setup } from './capture-fixture.mjs';
import { COMBAT, addRecords, records, streamState, validCombat, validRecord, sha } from '../node/validation.mjs';
import { exportCombats, parseArgs } from '../node/export-combats.mjs';

// Source v3 bg_combat (TASK-2026-11-19 U): the fixture is what the app writes; SourceCaptureCombatTests checks the same
// file against the app's own rules, so the two allowlists cannot drift apart unnoticed.
const FIGHT = JSON.parse(fs.readFileSync(new URL('./bg-combat.fixture.json', import.meta.url), 'utf8'));
const clone = x => JSON.parse(JSON.stringify(x));
const fight = (n, change = f => f) => change({ ...clone(FIGHT), fight: n });
const TOKEN = 'operator-token-for-tests';
const v3 = r => ({ ...r, v: 3 });
const bgStart = (mode = 'bg') => v3(record(0, 'start', { captureKind: 'game', mode, day: DAY, build: 4321, format: null, arena: null }));
const combatView = seq => v3(record(seq, 'gap', { reason: 'combat_view', count: 1 }));
const combat = (seq, data) => v3(record(seq, 'bg_combat', data, { turn: 9 }));
const bgEnd = seq => v3(record(seq, 'end', { status: 'incomplete', result: 'L', placement: 4, reason: 'normal' }, { turn: 9 }));
/** A v3 Battlegrounds game whose journal ends with its fights, as the app's recorder writes it. */
const game = (fights, mode = 'bg') => [bgStart(mode), combatView(1), ...fights.map((f, i) => combat(2 + i, f)), bgEnd(2 + fights.length)];
const bgManifest = (c, rows, id = ID) => manifest([c], { mode: rows[0].data.mode, sourceVersion: 3, captureStatus: 'incomplete', gaps: 1 }, id);
const count = (pool, sql = 'SELECT COUNT(*) AS n FROM bg_combats') => pool.store.sqlite.prepare(sql).get().n;
async function commit(f, rows, id = ID) {
  const c = chunk(jsonl(rows), 0, id);
  const upload = await f.post('chunks', c);
  if (upload.status !== 201) return upload;
  return f.post('complete', bgManifest(c, rows, id));
}
/** Every slot filled with long card ids: each part is allowed, the whole is over the data cap. */
function oversized(f) {
  const minion = (entity, remembered) => ({ ...clone(FIGHT.ours.minions[0]), entity, enchantments: Array(COMBAT.maxEnchantments).fill('E'.repeat(80)), remembered });
  const board = base => Array.from({ length: COMBAT.maxMinions }, (_, i) => minion(base + i, Array.from({ length: COMBAT.maxRemembered }, () => minion(0, []))));
  Object.assign(f, { firstAttacker: 0, facts: [], transcript: null });
  f.ours.minions = board(1);
  f.theirs.minions = board(100);
}
const exportPage = (f, body, token = TOKEN) => f.post('combats/export', body, token ? { authorization: `Bearer ${token}` } : {});

test('bg_combat: the app\'s fight is valid; every field outside the allowlist, out of range or of the wrong side is refused', () => {
  assert.equal(validCombat(clone(FIGHT)), true);
  assert.equal(validRecord(combat(2, clone(FIGHT))), true);
  const bad = [
    f => { f.extra = 1; },
    f => { delete f.transcript; },
    f => { f.transcript = { v: COMBAT.transcriptVersion, steps: [] }; },
    f => { f.fight = COMBAT.maxFights; },
    f => { f.result = 'Win'; },
    f => { f.firstAttacker = 99; },
    f => { f.ours.name = 'Player#1234'; },
    f => { f.ours.hand = []; },
    f => { f.ours.tags.PLAYER_ID = 3; },
    f => { f.ours.tags.BACON_RATING = 7000; },
    f => { f.theirs.cardTags = { BG30_MagicItem_301: { TAG_SCRIPT_DATA_NUM_1: 1 } }; },
    f => { f.ours.cardTags.BG30_MagicItem_301.COST = 1; },
    f => { f.ours.extras.push(f.ours.extras[0]); },
    f => { f.ours.hero = 'Hero with spaces'; },
    f => { f.ours.minions[0].card = '../path'; },
    f => { f.ours.minions[0].keywords = 32; },
    f => { f.ours.minions[0].tribes = 4096; },
    f => { f.ours.minions[0].mechanics = 64; },
    f => { f.ours.minions[0].attack = -1; },
    f => { f.ours.minions[0].enchantments = Array(COMBAT.maxEnchantments + 1).fill('BG28_503e'); },
    f => { f.ours.minions[1].remembered[0].remembered = [clone(f.ours.minions[0])]; },
    f => { f.theirs.minions[0].entity = 1; },
    f => { f.ours.minions = Array(COMBAT.maxMinions + 1).fill(0).map((_, i) => ({ ...clone(FIGHT.ours.minions[0]), entity: 10 + i })); },
    f => { f.facts[0].entity = 2; },
    f => { f.facts[1].markers = ['DEATHRATTLE', 'DEATHRATTLE']; },
    f => { f.facts[1].markers = ['TAUNT']; },
    f => { f.facts.push({ ...f.facts[0] }); },
    f => { f.odds.win = 1.5; },
    f => { f.odds.text = 'note'; },
    oversized,
    // The inputs besides the boards: the opponent's player enchantments never, every field known and in range.
    f => { f.theirs.playerEnchantments = [clone(f.ours.playerEnchantments[0])]; },
    f => { f.ours.playerEnchantments[0].name = 'x'; },
    f => { f.ours.minions[0].enchantmentValues[0].creatorEntity = COMBAT.maxRef + 1; },
    f => { f.ours.minions[0].enchantmentValues = Array(COMBAT.maxEnchantments + 1).fill(f.ours.minions[0].enchantmentValues[0]); },
    f => { f.ours.lobbyTribes = 4096; },
    // The order the sides entered play, never a lobby number, an account id or the player's secrets.
    f => { f.oursEnteredFirst = 1; },
    f => { delete f.oursEnteredFirst; },
    f => { f.playerId = 3; },
    f => { f.ours.playerId = 3; },
    f => { f.theirs.gameAccountId = { hi: 144115198130930503, lo: 12345678 }; },
    f => { f.theirs.hi = 1; },
    f => { f.ours.lo = 12345678; },
    f => { f.ours.secrets = ['LOCAL_SECRET_CARD']; },
    f => { f.ours.minions[0].friendlyDeaths = -1; },
    f => { f.ours.minions[0].bloodGemAttack = -1; },
    f => { delete f.ours.minions[0].bloodGemHealth; },
    f => { f.ours.deaths[0].remembered = [clone(f.ours.deaths[0])]; },
    f => { f.ours.deaths = Array(COMBAT.maxDeaths + 1).fill(f.ours.deaths[0]); },
    f => { f.ours.cardTags.BG30_MagicItem_301.SCORE_VALUE_3 = 1; },
    // The transcript: no hand step, only board minions, triggers of what the record names, ends that close one.
    f => { f.transcript.events.push(['hand', 1, 'BG_CFM_315']); },
    f => { f.transcript.events.push(['tag', 99, 'ATK', 1]); },
    f => { f.transcript.events.push(['tag', 3, 'COST', 1]); },
    f => { f.transcript.events.push(['damage', 2, 1]); },
    f => { f.transcript.events.push(['trigger', 99, 0, 'LOCAL_HAND_CARD', false]); },
    f => { f.transcript.events.push(['trigger', 99, 0, 'TB_BaconShop_HP_001', true]); },
    f => { f.transcript.events.push(['end']); },
    f => { f.transcript.events.push(['attack', 3, 4, 0, 'extra']); },
    f => { f.transcript.events[0][2] = 99; },
    f => { f.transcript.boards[2][0] = 1; },
    f => { f.transcript.boards[0][1].push([99, null, 1, 1, 0]); },
    f => { f.transcript.boards[0][2].push([1, null, 1, 1, 0]); },
    f => { f.transcript.version = 2; },
    f => { f.transcript.ending = 'other'; },
    f => { f.transcript.hand = []; },
    f => { f.transcript.events = Array(COMBAT.maxTranscriptSteps + 1).fill(['damage', 1, 1]); f.transcript.boards = []; },
    f => { f.transcript.events = Array.from({ length: 200 }, (_, i) => ['summon', 20 + i, `BG_${'X'.repeat(77)}`, 1, false, false]); f.transcript.boards = []; },
  ];
  for (const change of bad) {
    const f = clone(FIGHT);
    change(f);
    assert.equal(validCombat(f), false, change.toString());
  }
  // Without a transcript, and with one exactly at the step limit, a fight is valid.
  assert.equal(validCombat({ ...clone(FIGHT), transcript: null }), true);
  assert.equal(validCombat({ ...clone(FIGHT), oursEnteredFirst: null }), true);
  assert.equal(validCombat({ ...clone(FIGHT), oursEnteredFirst: false }), true);
  // A minion that died before the capture (a death, a remembered one) can be below 0, a hero beaten in a lethal fight
  // too; a board minion cannot.
  const lethal = clone(FIGHT);
  lethal.ours.deaths[0].health = -340;
  lethal.ours.minions[1].remembered[0].health = -51;
  lethal.theirs.health = -7;
  assert.equal(validCombat(lethal), true);
  assert.equal(validCombat({ ...clone(FIGHT), ours: { ...clone(FIGHT.ours), minions: [{ ...clone(FIGHT.ours.minions[0]), health: -1 }] } }), false);
  assert.equal(validCombat({ ...clone(FIGHT), theirs: { ...clone(FIGHT.theirs), health: -10_001 } }), false);
  const full = clone(FIGHT);
  full.transcript.events = Array(COMBAT.maxTranscriptSteps).fill(['damage', 1, 1]);
  full.transcript.boards = [];
  assert.equal(validCombat(full), true);
  // The envelope: v3 only, no side, entity, card or target.
  for (const r of [{ ...combat(2, clone(FIGHT)), v: 2 }, { ...combat(2, clone(FIGHT)), side: 'local' }, { ...combat(2, clone(FIGHT)), entity: 4 },
    { ...combat(2, clone(FIGHT)), card: 'BG_CFM_315' }, { ...combat(2, clone(FIGHT)), target: 1 }]) assert.equal(validRecord(r), false, JSON.stringify(r).slice(0, 80));
});

test('bg_combat: only in a Battlegrounds or Duos game, in order, at most one per combat and 64 a game', () => {
  addRecords(streamState(), game([fight(0), fight(1), fight(5)]));
  addRecords(streamState(), game([fight(0)], 'duos'));
  const ranked = v3(record(0, 'start', { captureKind: 'game', mode: 'ranked', day: DAY, build: 1, format: 'standard', arena: null }));
  assert.throws(() => addRecords(streamState(), [ranked, combat(1, fight(0))]), e => e.code === 'invalid_combat');
  assert.throws(() => addRecords(streamState(), game([fight(3), fight(3)])), e => e.code === 'invalid_combat');
  assert.throws(() => addRecords(streamState(), game([fight(3), fight(1)])), e => e.code === 'invalid_combat');
  assert.throws(() => addRecords(streamState(), [...game([fight(0)])].slice(0, -1).concat([bgEnd(3), combat(4, fight(1))])), e => e.code === 'invalid_sequence');
  // 64 is the most a game holds; a 65th record cannot have a valid fight number.
  addRecords(streamState(), game(Array.from({ length: COMBAT.maxFights }, (_, i) => fight(i))));
  // The data cap leaves room for the record around it inside the 64 KiB line limit.
  const envelope = Buffer.byteLength(JSON.stringify(combat(1_000_000, {}))) + 1;
  assert.ok(COMBAT.maxDataBytes + envelope < 64 * 1024);
  const f = clone(FIGHT);
  oversized(f);
  assert.ok(Buffer.byteLength(JSON.stringify(f)) > COMBAT.maxDataBytes);
  assert.equal(validCombat(f), false);
});

test('bg_combat: a committed v3 game keeps its fights; v2 lines and a bad fight reject the upload; the operator exports them', async t => {
  const f = await setup(t, { operatorToken: TOKEN });
  const rows = game([fight(0), fight(2)]);
  assert.equal((await commit(f, rows)).status, 202);
  assert.equal(count(f.pool), 2);
  const stored = f.pool.store.sqlite.prepare('SELECT capture,fight,day,build,mode FROM bg_combats ORDER BY fight').all();
  assert.deepEqual(stored.map(r => ({ ...r })), [0, 2].map(n => ({ capture: stored[0].capture, fight: n, day: DAY, build: 4321, mode: 'bg' })));
  // The same commit again stores nothing twice.
  const c = chunk(jsonl(rows));
  assert.equal((await f.post('complete', bgManifest(c, rows))).status, 202);
  assert.equal(count(f.pool), 2);
  const health = await (await fetch(`${f.pool.url}/healthz`)).json();
  assert.equal(health.bgCombats, 2);

  // A v2 journal cannot carry one; neither can a v3 journal with a fight outside the rules.
  const other = { ...ID, captureId: '4'.repeat(32) };
  const v2 = game([fight(0)]).map(r => ({ ...r, v: 2 }));
  assert.equal((await f.post('chunks', chunk(jsonl(v2), 0, other))).status, 400);
  const wrong = game([fight(0, x => { x.ours.hand = []; return x; })]);
  assert.equal((await f.post('chunks', chunk(jsonl(wrong), 0, other))).status, 400);

  // Export: operator only, oldest first, paged, a game named by a hash of its key and nothing else.
  assert.equal((await exportPage(f, { v: 1, after: 0, limit: 10 }, '')).status, 403);
  assert.equal((await exportPage(f, { v: 1, after: 0, limit: 10 }, 'wrong')).status, 403);
  assert.equal((await exportPage(f, { v: 1, after: 0, limit: 201 })).status, 400);
  const first = await (await exportPage(f, { v: 1, after: 0, limit: 1 })).json();
  assert.equal(first.fights.length, 1);
  assert.notEqual(first.next, null);
  const second = await (await exportPage(f, { v: 1, after: first.next, limit: 1 })).json();
  assert.equal(second.fights.length, 1);
  const third = await (await exportPage(f, { v: 1, after: second.next ?? 0, limit: 1 })).json();
  assert.deepEqual([third.fights.length, third.next], [0, null]);
  const lines = [...first.fights, ...second.fights];
  assert.deepEqual(lines.map(l => Object.keys(l)), lines.map(() => ['v', 'game', 'day', 'build', 'mode', 'fight']));
  assert.deepEqual(lines.map(l => l.fight), [fight(0), fight(2)]);
  assert.equal(lines[0].game, sha(stored[0].capture).slice(0, 16));
  const text = JSON.stringify(lines);
  for (const secret of [ID.iid, ID.generation, ID.captureId, stored[0].capture]) assert.equal(text.includes(secret), false);
});

test('bg_combat: forget and expiry take the fights with the capture; a full metadata budget asks to retry', async t => {
  const f = await setup(t, { operatorToken: TOKEN });
  assert.equal((await commit(f, game([fight(0), fight(1)]))).status, 202);
  const second = { ...ID, generation: '5'.repeat(32) };
  assert.equal((await commit(f, game([fight(0)]), second)).status, 202);
  assert.equal(count(f.pool), 3);
  assert.equal((await f.post('forget', { v: 1, iid: ID.iid, generation: ID.generation })).status, 200);
  assert.equal(count(f.pool), 1);
  await f.pool.archive.maintain(Date.now() + 181 * 86_400_000);
  assert.equal(count(f.pool), 0);
  assert.deepEqual((await (await exportPage(f, { v: 1, after: 0, limit: 10 })).json()).fights, []);

  const tight = await setup(t);
  const rows = game([fight(0)]);
  const c = chunk(jsonl(rows));
  assert.equal((await tight.post('chunks', c)).status, 201);
  // The budget fills between the upload and the commit: the commit with fights is refused for a retry, not half kept.
  tight.pool.archive.maxMetadataBytes = 1;
  assert.equal((await tight.post('complete', bgManifest(c, rows))).status, 503);
  assert.equal(count(tight.pool), 0);
  assert.equal(tight.pool.store.sqlite.prepare('SELECT manifest FROM source_captures').get().manifest, null);
});

test('bg_combat: the export command pages into a JSONL file on loopback only and never takes the token from argv', async t => {
  const f = await setup(t, { operatorToken: TOKEN });
  for (let i = 0; i < 3; i++) assert.equal((await commit(f, game([fight(0), fight(1)]), { ...ID, captureId: String(i + 6).repeat(32) })).status, 202);
  const dir = mkdtempSync(path.join(tmpdir(), 'hp-combats-'));
  t.after(() => rmSync(dir, { recursive: true, force: true }));
  const out = path.join(dir, 'fights.jsonl');
  assert.equal(await exportCombats({ url: f.pool.url, out, page: 4, token: TOKEN }), 6);
  const lines = fs.readFileSync(out, 'utf8').trimEnd().split('\n').map(l => JSON.parse(l));
  assert.equal(lines.length, 6);
  assert.equal(new Set(lines.map(l => l.game)).size, 3);
  assert.ok(lines.every(l => validCombat(l.fight) && l.mode === 'bg' && l.day === DAY && l.build === 4321));
  await assert.rejects(exportCombats({ url: f.pool.url, out, token: 'wrong' }), /export_failed_403/);
  await assert.rejects(exportCombats({ url: f.pool.url, out, token: '' }), /operator_token_missing/);
  assert.equal(fs.readdirSync(dir).length, 1);
  assert.throws(() => parseArgs(['--url', 'https://pool.example.com', '--out', out]), /usage/);
  assert.throws(() => parseArgs(['--out', out, '--token', TOKEN]), /usage/);
  assert.deepEqual(parseArgs(['--out', out]), { url: 'http://127.0.0.1:3181', out, page: 200 });
});
