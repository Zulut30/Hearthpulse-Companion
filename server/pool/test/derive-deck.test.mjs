// TASK-2026-11-26-pro-collection B2 (AC10): producer `deck` - the main list of a record's deck code, its key and the
// canonical code. test/fixtures/deckcodes.json is shared with DeckKeyAgreementTests, which holds the client's
// DeckString and DeckRecords.CardListKey to the same lists, keys and canonical codes.
import assert from 'node:assert/strict';
import test from 'node:test';
import { readFileSync } from 'node:fs';
import * as producer from '../node/records/derive/deck.mjs';
import { CLASS_HEROES, decodeDeck, deckKey, derive, encodeDeck } from '../node/records/derive/deck.mjs';
import { validateEvent } from '../src/events.js';

const fixture = JSON.parse(readFileSync(new URL('./fixtures/deckcodes.json', import.meta.url), 'utf8'));
const valid = fixture.cases.filter(c => c.key !== null);
const broken = fixture.cases.filter(c => c.key === null);
/** "<dbf>x<copies>,..." -> Map, in the order written. */
const listOf = key => new Map(key === '' ? [] : key.split(',').map(entry => entry.split('x').map(Number)));
const sideboardKey = side => (side === null ? null : side.size === 0 ? '' : deckKey(side));
const record = (body, d = {}) => ({ iid: 'i1', day: 20418, mode: body.mode ?? 'ranked', fmt: body.fmt ?? null, body, d });
const byName = name => fixture.cases.find(c => c.name === name);

test('the module keeps contract К2: producer deck, order 10, version deck-1', () => {
  assert.equal(producer.name, 'deck');
  assert.equal(producer.order, 10);
  assert.equal(producer.version, 'deck-1');
  assert.equal(typeof producer.derive, 'function');
});

test('every code of the fixture reads into the list the client reads (AC10)', () => {
  assert.ok(valid.length >= 20 && broken.length >= 5, 'the fixture lost cases');
  for (const c of valid) {
    const d = decodeDeck(c.code);
    assert.ok(d, `${c.name}: does not read`);
    assert.deepEqual([...d.cards], [...listOf(c.key)], c.name);
    assert.equal(deckKey(d.cards), c.key, c.name);
    assert.equal(d.fmt, c.fmt, c.name);
    assert.deepEqual(d.heroes, c.heroes, c.name);
    assert.equal(sideboardKey(d.sideboard), c.sideboard, c.name);
  }
});

test('codes of one list share one key, and different lists have different keys (AC10)', () => {
  const keys = new Map();
  for (const c of valid) {
    const key = deckKey(decodeDeck(c.code).cards);
    assert.equal(keys.get(c.group) ?? key, key, `${c.name}: another key than the rest of ${c.group}`);
    keys.set(c.group, key);
  }
  assert.equal(new Set(keys.values()).size, keys.size, 'two lists share a key');

  // The ways one list is written differently: a hero skin, the format byte, the order of the cards or another encoder.
  const dh = valid.filter(c => c.group === 'dh');
  assert.ok(new Set(dh.map(c => c.heroes.join())).size > 1, 'no hero skin');
  assert.ok(new Set(dh.map(c => c.fmt)).size > 1, 'no other format');
  assert.ok(dh.some(a => dh.some(b => a !== b && a.fmt === b.fmt && a.heroes.join() === b.heroes.join() && a.code.trim() !== b.code.trim())),
    'no other card order');
  // Other lists close to it: one card swapped, the same cards with other copies.
  assert.notEqual(byName('dh-one-card-swapped').key, byName('dh-standard').key);
  assert.notEqual(byName('dh-copies-moved').key, byName('dh-standard').key);
});

test('the sideboard is read but is not part of the list (AC10)', () => {
  const warrior = valid.filter(c => c.group === 'warrior');
  const sideboards = warrior.map(c => sideboardKey(decodeDeck(c.code).sideboard));
  assert.ok(sideboards.includes(''), 'no code without a sideboard');
  assert.ok(sideboards.includes(null), 'no code with a sideboard that does not read');
  assert.ok(new Set(sideboards.filter(s => s)).size >= 2, 'no two different sideboards');
  assert.equal(new Set(warrior.map(c => deckKey(decodeDeck(c.code).cards))).size, 1);
  for (const dbf of [104949, 104951, 110446]) {
    // Zilliax's modules are only in the sideboard of the real code.
    assert.ok(listOf(byName('warrior-wild-etc-zilliax').sideboard).has(dbf), `module ${dbf}`);
    assert.ok(!decodeDeck(byName('warrior-wild-etc-zilliax').code).cards.has(dbf), `module ${dbf} in the list`);
  }
});

test('the canonical code names the class default hero and reads back into the same list (AC10)', () => {
  const canonical = new Map();
  for (const c of valid) {
    const { cls, fmt, code } = c.canonical;
    const d = decodeDeck(c.code);
    assert.equal(encodeDeck(d.cards, cls, fmt), code, c.name);
    const back = decodeDeck(code);
    assert.equal(deckKey(back.cards), c.key, c.name);
    assert.deepEqual(back.heroes, [CLASS_HEROES[cls]], c.name);
    assert.equal(back.fmt, fmt, c.name);
    assert.equal(back.sideboard.size, 0, c.name);
    // A skin, the format byte, card order and sideboard are gone: one list, class and format give one code.
    const group = `${c.group}|${cls}|${fmt}`;
    assert.equal(canonical.get(group) ?? code, code, `${c.name}: another canonical code than the rest of ${c.group}`);
    canonical.set(group, code);
  }
  // A code the game wrote with the default hero and no sideboard is already canonical.
  for (const name of ['dh-standard', 'priest-standard', 'warlock-standard', 'shaman-wild', 'rogue-wild']) {
    assert.equal(byName(name).canonical.code, byName(name).code, name);
  }
  assert.notEqual(byName('rogue-standard-skin').canonical.code, byName('rogue-standard-skin').code);
});

test('the default heroes are those of HearthstoneJSON the fixture records', () => {
  assert.deepEqual({ ...CLASS_HEROES }, Object.fromEntries(Object.entries(fixture.heroes).map(([cls, hero]) => [cls, hero.dbf])));
  assert.ok(Object.isFrozen(CLASS_HEROES));
});

test('a broken code gives cards: null, deckKey: null (AC10)', () => {
  for (const c of broken) {
    assert.equal(decodeDeck(c.code), null, c.name);
    const rec = record({ deck: c.code });
    derive(rec, {});
    assert.deepEqual(rec.d, { cards: null, deckKey: null }, c.name);
  }
  for (const value of [undefined, null, 42, {}, [], true]) assert.equal(decodeDeck(value), null, String(value));
});

test('codes no game writes are broken here, though the client still reads them', () => {
  assert.ok(fixture.stricter.length >= 5);
  for (const c of fixture.stricter) {
    assert.equal(typeof c.client, 'string', c.name);
    assert.equal(decodeDeck(c.code), null, c.name);
    const rec = record({ deck: c.code });
    derive(rec, {});
    assert.deepEqual(rec.d, { cards: null, deckKey: null }, c.name);
  }
});

test('derive fills rec.d from the record as validateEvent stores it and keeps what others wrote', () => {
  const today = 20418;
  const stored = validateEvent({ id: 'a'.repeat(32), day: today, mode: 'ranked', res: 'W', fmt: 'standard', rank: 'gold', cls: 'DEMONHUNTER',
    hero: 'HERO_10a', deck: byName('dh-hero-skin').code, first: true }, today);
  const rec = record(JSON.parse(stored.body), { arch: 'kept' });
  derive(rec, { today });
  assert.ok(rec.d.cards instanceof Map);
  assert.equal(rec.d.deckKey, byName('dh-standard').key);
  assert.deepEqual([...rec.d.cards], [...listOf(byName('dh-standard').key)]);
  assert.equal(rec.d.arch, 'kept');
  assert.equal(encodeDeck(rec.d.cards, rec.body.cls, rec.fmt), byName('dh-standard').code);

  // No code, a Battlegrounds record, no `d` yet.
  for (const body of [{ mode: 'ranked', res: 'L' }, { mode: 'bg', res: 'W', bg: { place: 3 } }]) {
    const r = record(body);
    derive(r, {});
    assert.deepEqual(r.d, { cards: null, deckKey: null });
  }
  const fresh = { body: { deck: byName('priest-standard').code } };
  derive(fresh, {});
  assert.equal(fresh.d.deckKey, byName('priest-standard').key);
});

test('derive never throws', () => {
  const code = byName('dh-standard').code;
  for (const rec of [null, undefined, 42, 'x', {}, { body: null }, { body: { deck: 42 } }, { d: null, body: { deck: code } },
    { d: 'x', body: { deck: code } }, Object.freeze({ d: Object.freeze({}), body: { deck: code } }),
    { d: {}, get body() { throw new Error('unreadable'); } }]) {
    assert.doesNotThrow(() => derive(rec, {}));
  }
  const unreadable = { d: { cards: 'old', deckKey: 'old' }, get body() { throw new Error('unreadable'); } };
  derive(unreadable, {});
  assert.deepEqual(unreadable.d, { cards: null, deckKey: null });
  const replaced = { d: 'x', body: { deck: code } };
  derive(replaced, {});
  assert.equal(replaced.d.deckKey, byName('dh-standard').key);
});

test('deckKey and encodeDeck take only a list of positive dbf ids and copies, a known class and format', () => {
  assert.equal(deckKey(new Map([[5, 1], [3, 2]])), '3x2,5x1');
  for (const cards of [null, undefined, new Map(), new Map([[1, 0]]), new Map([[0, 1]]), new Map([[1.5, 1]]), new Map([[1, 2 ** 31]]),
    new Map([['1', 1]]), { 1: 1 }, [[1, 1]]]) {
    assert.equal(deckKey(cards), null, String(cards));
    assert.equal(encodeDeck(cards, 'MAGE', 'standard'), null, String(cards));
  }
  const cards = listOf(byName('priest-standard').key);
  assert.equal(encodeDeck(cards, 'priest', 'Standard'), encodeDeck(cards, 'PRIEST', 'standard'));
  for (const [cls, fmt] of [['NEUTRAL', 'standard'], ['WHIZBANG', 'wild'], ['CONSTRUCTOR', 'wild'], [null, 'wild'], ['PRIEST', 'duels'],
    ['PRIEST', null], ['PRIEST', 'unknown'], ['PRIEST', 3]]) {
    assert.equal(encodeDeck(cards, cls, fmt), null, `${cls} ${fmt}`);
  }
});
