// TASK-2026-11-26-pro-collection B1, AC11: the pool names a record's archetypes by the app's rules over its dictionary.
// Hand-checked rules on a small dictionary, the shipped dictionary's integrity, and the agreement cases: synthetic
// records made from the shipped dictionary (fixtures/archetype-cases.json) and the answers the deriver gives them
// (golden/archetypes-agreement.json), which the .NET test ArchetypeAgreementTests checks against what the app's
// GameRecords.Build and ArchetypeGuess.FromSeenCards answer. After a new dictionary, remake both files:
//   HP_REGEN_ARCHETYPES=1 node --test server/pool/test/derive-archetypes.test.mjs
import assert from 'node:assert/strict';
import test from 'node:test';
import { createHash } from 'node:crypto';
import { copyFileSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import { cardIdOf, compile, dbfOf, deckFits, derive, name, opponentArchetype, order, ownArchetype, version, MIN_SHARE } from '../node/records/derive/archetypes.mjs';

const read = url => JSON.parse(readFileSync(url, 'utf8'));
const DERIVER = new URL('../node/records/derive/archetypes.mjs', import.meta.url);
const DICTIONARY = new URL('../data/archetypes.json', import.meta.url);
const CARD_IDS = new URL('../data/card-ids.json', import.meta.url);
const CASES = new URL('./fixtures/archetype-cases.json', import.meta.url);
const GOLDEN = new URL('./golden/archetypes-agreement.json', import.meta.url);
const CLASSES = ['DEATHKNIGHT', 'DEMONHUNTER', 'DRUID', 'HUNTER', 'MAGE', 'PALADIN', 'PRIEST', 'ROGUE', 'SHAMAN', 'WARLOCK', 'WARRIOR'];
const SEED = 20261126;
const PER_FORMAT = 130;

/** A record as the scan hands it to the derivers (contract K2), the deck already decoded into rec.d.cards. */
function rec({ mode = 'ranked', fmt = 'standard', cls, cards, deck, ocls, oplays }) {
  const body = {};
  if (cls != null) body.cls = cls;
  if (deck != null) body.deck = deck;
  if (ocls != null) body.ocls = ocls;
  if (oplays != null) body.oplays = oplays;
  return { iid: 'a'.repeat(32), day: 20418, mode, fmt, build: null, body, d: { cards: cards == null ? cards : new Map(cards) } };
}

function answer(record) {
  const r = rec(record);
  derive(r);
  return [r.d.arch, r.d.oarch];
}

/** A deck code laid out as DeckString.Encode lays one out, with any heroes: version 1, the format, the heroes, the cards by copies in dbf order. */
function deckCode(fmt, heroes, cards) {
  const list = [...cards].sort((a, b) => a[0] - b[0]);
  const values = [0, 1, { wild: 1, standard: 2, classic: 3, twist: 4 }[fmt] ?? 2, heroes.length, ...heroes];
  for (const group of [1, 2, 3]) {
    const of = list.filter(([, copies]) => (group === 3 ? copies >= 3 : copies === group));
    values.push(of.length);
    for (const [dbf, copies] of of) values.push(...(group === 3 ? [dbf, copies] : [dbf]));
  }
  values.push(0);
  const bytes = [];
  for (let v of values) {
    for (; v >= 0x80; v = Math.floor(v / 0x80)) bytes.push((v & 0x7f) | 0x80);
    bytes.push(v);
  }
  return Buffer.from(bytes).toString('base64');
}

// --- The rules, on a dictionary small enough to check by hand ------------------------------------------------------

const core = (prefix, dbf) => ({ coreIds: [1, 2, 3, 4, 5].map(i => `${prefix}${i}`), coreDbf: [1, 2, 3, 4, 5].map(i => dbf + i) });
// In the file's order, the most played first: Big, then Small and Twin, as many games each.
const mini = compile({
  v: 1, rules: 'arch-test', built: '2026-11-26', source: 'test',
  formats: {
    standard: {
      MAGE: [
        { key: 'Empty Mage', coreIds: [], coreDbf: [] },
        { key: 'Big Mage', ...core('A', 0) },
        { key: 'Small Mage', ...core('B', 10) },
        { key: 'Twin Mage', ...core('C', 20) },
      ],
      ROGUE: [{ key: 'Rogue', ...core('R', 30) }],
    },
    wild: { MAGE: [{ key: 'Wild Mage', ...core('W', 40) }] },
  },
  heroes: { MAGE: [637, 801], NEUTRAL: [802], ROGUE: [930] },
});
const deckOf = (...dbf) => new Map(dbf.map(d => [d, 1]));

test('own: a deck holding 60% of the core is that archetype, one card less is not', () => {
  assert.equal(MIN_SHARE, 0.6);
  assert.equal(ownArchetype(mini, 'standard', 'MAGE', deckOf(1, 2, 3, 99)), 'Big Mage');
  assert.equal(ownArchetype(mini, 'standard', 'MAGE', deckOf(1, 2, 99, 98)), null);
});

test('own: the largest share wins, an equal share goes to the earlier, more played archetype', () => {
  assert.equal(ownArchetype(mini, 'standard', 'MAGE', deckOf(1, 2, 3, 4, 11, 12, 13, 14, 15)), 'Small Mage', '100% of Small beats 80% of Big');
  assert.equal(ownArchetype(mini, 'standard', 'MAGE', deckOf(1, 2, 3, 11, 12, 13)), 'Big Mage', '60% each: Big comes first');
  assert.equal(ownArchetype(mini, 'standard', 'MAGE', deckOf(11, 12, 13, 21, 22, 23)), 'Small Mage', '60% each: Small comes before Twin');
});

test('deck fit: a code whose hero is of another class than the record names no own archetype (GameRecords.DeckFits)', () => {
  const code = (...heroes) => deckCode('standard', heroes, [[1, 2], [2, 2], [3, 1]]);
  assert.equal(deckFits(mini, code(637), 'MAGE'), true, 'the class\'s hero');
  assert.equal(deckFits(mini, code(801), 'mage'), true, 'another hero of the class, the class in any case');
  assert.equal(deckFits(mini, code(930), 'MAGE'), false, 'a Rogue deck still held from an earlier game');
  assert.equal(deckFits(mini, code(802), 'MAGE'), false, 'a hero card of no playable class');
  assert.equal(deckFits(mini, code(999), 'MAGE'), true, 'an unknown hero passes');
  assert.equal(deckFits(mini, code(), 'MAGE'), true, 'no hero passes');
  assert.equal(deckFits(mini, code(999, 930, 637), 'MAGE'), false, 'the first hero the dictionary knows decides');
  for (const junk of [undefined, null, 7, '', 'not a deck code', 'AAEB']) assert.equal(deckFits(mini, junk, 'MAGE'), true, `code ${junk}`);
  for (const cls of [null, undefined, '']) assert.equal(deckFits(mini, code(930), cls), true, `class ${cls}`);
});

test('own: another class, format or an unknown deck is null; an archetype without core cards never matches', () => {
  assert.equal(ownArchetype(mini, 'standard', 'ROGUE', deckOf(1, 2, 3, 4, 5)), null);
  assert.equal(ownArchetype(mini, 'standard', 'mage', deckOf(1, 2, 3)), 'Big Mage', 'the class is read in any case, as ForClass does');
  assert.equal(ownArchetype(mini, 'wild', 'MAGE', deckOf(1, 2, 3, 4, 5)), null);
  for (const fmt of ['classic', 'twist', null, undefined]) assert.equal(ownArchetype(mini, fmt, 'MAGE', deckOf(1, 2, 3, 4, 5)), null);
  for (const cls of [null, undefined, '', 'NEUTRAL']) assert.equal(ownArchetype(mini, 'standard', cls, deckOf(1, 2, 3, 4, 5)), null);
  assert.equal(ownArchetype(mini, 'standard', 'MAGE', new Map()), null);
  assert.equal(ownArchetype(mini, 'standard', 'MAGE', null), null);
  assert.equal(ownArchetype(mini, 'standard', 'MAGE', { 1: 1, 2: 1, 3: 1 }), null, 'the deck is a Map (contract K2)');
  assert.equal(ownArchetype(mini, 'standard', 'MAGE', new Map([['1', 1], ['2', 1], ['3', 1]])), null, 'keyed by dbf numbers');
  assert.equal(mini.formats.get('standard').get('MAGE').some(a => a.key === 'Empty Mage'), false);
});

test('opponent: three hits with a lead of two name the archetype, anything less clear is null', () => {
  const guess = seen => opponentArchetype(mini, 'standard', 'MAGE', seen);
  assert.equal(guess(['A1', 'A2', 'A3']), 'Big Mage');
  assert.equal(guess(['A1', 'A2', 'A3', 'B1']), 'Big Mage', 'lead 2');
  assert.equal(guess(['A1', 'A2', 'A3', 'B1', 'B2']), null, 'lead 1');
  assert.equal(guess(['A1', 'A2', 'A3', 'A4', 'B1', 'B2']), 'Big Mage', '4 against 2');
  assert.equal(guess(['B1', 'B2', 'B3', 'C1', 'C2', 'C3']), null, 'a tie at the top');
  assert.equal(guess(['A1', 'A2', 'X1']), null, 'two hits');
  assert.equal(guess(['A1', 'A1', 'A2', 'A2']), null, 'repeats count once: two cards seen');
  assert.equal(guess(['A1', 'A2', 'A3', 'A3', 'X1', 'X2']), 'Big Mage', 'other cards seen are not hits');
});

test('opponent: no class, another class or format is null', () => {
  const seen = ['A1', 'A2', 'A3', 'A4'];
  assert.equal(opponentArchetype(mini, 'standard', 'ROGUE', seen), null);
  assert.equal(opponentArchetype(mini, 'wild', 'MAGE', seen), null);
  for (const ocls of [null, undefined, '']) assert.equal(opponentArchetype(mini, 'standard', ocls, seen), null);
  for (const fmt of ['classic', 'twist', null]) assert.equal(opponentArchetype(mini, fmt, 'MAGE', seen), null);
});

test('compile: an unknown shape throws, parallel core lists must agree, heroes are dbf ids', () => {
  assert.throws(() => compile({ v: 2, rules: 'x', formats: {} }));
  assert.throws(() => compile({ v: 1, rules: '', formats: {} }));
  assert.throws(() => compile({ v: 1, rules: 'x', formats: { standard: { MAGE: {} } } }));
  assert.throws(() => compile({ v: 1, rules: 'x', formats: { standard: { MAGE: [{ key: 'M', coreIds: ['A'], coreDbf: [] }] } } }));
  assert.throws(() => compile({ v: 1, rules: 'x', formats: { standard: { MAGE: [{ coreIds: [], coreDbf: [] }] } } }));
  for (const heroes of [{ MAGE: 637 }, { MAGE: [637.5] }, { MAGE: ['HERO_08'] }, { MAGE: [0] }]) {
    assert.throws(() => compile({ v: 1, rules: 'x', formats: {}, heroes }), JSON.stringify(heroes));
  }
  assert.deepEqual([...compile({ v: 1, rules: 'x', formats: {}, heroes: { mage: [637] } }).heroes], [[637, 'MAGE']]);
  assert.equal(compile({ v: 1, rules: 'x', formats: {} }).heroes.size, 0, 'no heroes: every deck fits');
});

// --- The deriver --------------------------------------------------------------------------------------------------

test('the deriver: contract K2, version = the shipped dictionary\'s rules', () => {
  assert.equal(name, 'archetypes');
  assert.equal(order, 20);
  assert.equal(version, read(DICTIONARY).rules);
});

test('the deriver fills both fields in ranked and casual, null elsewhere, and never throws', () => {
  const { formats } = read(DICTIONARY);
  const [cls] = Object.keys(formats.standard);
  const a = formats.standard[cls][0];
  const record = { fmt: 'standard', cls, cards: a.coreDbf.map(d => [d, 2]), ocls: cls, oplays: a.coreIds.map((id, i) => [i + 1, id]) };
  const r = rec(record);
  r.d.deckKey = 'kept';
  derive(r);
  assert.equal(r.d.arch, a.key, 'the whole core of the most played archetype of the class');
  assert.equal(typeof r.d.oarch === 'string' || r.d.oarch === null, true);
  assert.equal(r.d.deckKey, 'kept');
  assert.deepEqual(answer({ ...record, mode: 'casual' }), [r.d.arch, r.d.oarch]);
  for (const mode of ['arena', 'brawl', 'bg', 'duos', 'arena_run']) assert.deepEqual(answer({ ...record, mode }), [null, null], mode);

  for (const broken of [null, undefined, 7, {}, { mode: 'ranked' }, { mode: 'ranked', body: null, d: { cards: {} } },
    { mode: 'ranked', fmt: 'standard', body: { cls, ocls: cls, oplays: [null, 3, ['x'], [1, 2]] }, d: { cards: 'x' } }]) {
    assert.doesNotThrow(() => derive(broken));
  }
  const odd = { mode: 'ranked', fmt: 'standard', body: { cls: 5, ocls: {}, oplays: 'x' } };
  derive(odd);
  assert.deepEqual([odd.d.arch, odd.d.oarch], [null, null]);
});

/** An archetype of the shipped Standard dictionary and three of its core cards no other archetype of its class has. */
function clearCall() {
  for (const [cls, list] of Object.entries(read(DICTIONARY).formats.standard)) {
    for (const a of list) {
      const others = new Set(list.filter(x => x !== a).flatMap(x => x.coreIds));
      const own = a.coreIds.filter(id => !others.has(id));
      if (own.length >= 3) return { cls, a, other: list.find(x => x !== a), own: own.slice(0, 3) };
    }
  }
  throw new Error('no archetype with three core cards of its own');
}

test('the deriver never reads the client\'s guess of the opponent (body.oarch)', () => {
  const { cls, a, other, own } = clearCall();
  const guess = (oarch, seen) => {
    const r = rec({ cls, cards: [], ocls: cls, oplays: seen.map((id, i) => [i + 1, id]) });
    r.body.oarch = oarch;
    derive(r);
    return r.d.oarch;
  };
  // Three core cards only this archetype has: the pool names it, whatever the client guessed.
  for (const oarch of ['Пират-воин', other?.key ?? 'Another Archetype']) assert.equal(guess(oarch, own), a.key, oarch);
  // Two of them name nothing, even when the client sent this very key.
  for (const oarch of [a.key, 'Пират-воин']) assert.equal(guess(oarch, own.slice(0, 2)), null, oarch);
});

test('the deriver names no own archetype for a deck whose hero is of another class, as the app names none', () => {
  const { formats, heroes } = read(DICTIONARY);
  const [cls] = Object.keys(formats.standard);
  const a = formats.standard[cls][0];
  const other = Object.keys(heroes).find(c => c !== cls && CLASSES.includes(c));
  const played = hero => {
    const cards = a.coreDbf.map(dbf => [dbf, 1]);
    return answer({ cls, cards, deck: deckCode('standard', [hero], cards), ocls: cls, oplays: a.coreIds.map((id, i) => [i + 1, id]) });
  };
  const [own, opponent] = played(heroes[cls][0]);
  assert.equal(own, a.key);
  assert.deepEqual(played(heroes[other][0]), [null, opponent], 'a deck still held from a game of another class; the opponent does not depend on it');
  assert.deepEqual(played(heroes.NEUTRAL[0]), [null, opponent]);
  assert.deepEqual(played(999999999), [own, opponent], 'an unknown hero passes');
});

const shipped = read(DICTIONARY);
const [firstClass] = Object.keys(shipped.formats.standard);
const mostPlayed = shipped.formats.standard[firstClass][0];
/** A ranked record holding the whole core of the most played archetype of the first class, and showing it. */
const named = () => ({
  mode: 'ranked', fmt: 'standard',
  body: { cls: firstClass, ocls: firstClass, oplays: mostPlayed.coreIds.map((id, i) => [i + 1, id]) },
  d: { cards: new Map(mostPlayed.coreDbf.map(dbf => [dbf, 2])) },
});

/**
 * The deriver imported afresh from a copy laid out as in the repository (node/records/derive/ beside data/), with
 * `files` written into data/ (null: the file is missing); `run` gets the module and the lines it logged.
 */
async function isolated(files, run) {
  const root = mkdtempSync(join(tmpdir(), 'hp-archetypes-'));
  const logged = [];
  const error = console.error;
  try {
    const folder = join(root, 'node', 'records', 'derive');
    mkdirSync(folder, { recursive: true });
    mkdirSync(join(root, 'data'));
    copyFileSync(DERIVER, join(folder, 'archetypes.mjs'));
    for (const [file, text] of Object.entries(files)) if (text !== null) writeFileSync(join(root, 'data', file), text);
    console.error = (...args) => logged.push(args.join(' '));
    return await run(await import(pathToFileURL(join(folder, 'archetypes.mjs')).href), logged);
  } finally {
    console.error = error;
    rmSync(root, { recursive: true, force: true });
  }
}

test('a missing or broken dictionary is one log line, version arch-none and no archetypes, never an error', async () => {
  const dictionary = readFileSync(DICTIONARY, 'utf8');
  const cards = readFileSync(CARD_IDS, 'utf8');
  await isolated({ 'archetypes.json': dictionary, 'card-ids.json': cards }, (m, logged) => {
    assert.equal(m.version, shipped.rules, 'the copy reads the data folder beside it');
    const r = named();
    m.derive(r);
    assert.equal(r.d.arch, mostPlayed.key);
    assert.equal(m.cardIdOf(mostPlayed.coreDbf[0]), mostPlayed.coreIds[0]);
    assert.deepEqual(logged, []);
  });
  for (const [what, broken] of Object.entries({ missing: null, 'not JSON': dictionary.slice(0, 200), 'another version': JSON.stringify({ ...shipped, v: 2 }) })) {
    await isolated({ 'archetypes.json': broken, 'card-ids.json': cards }, (m, logged) => {
      assert.deepEqual([m.name, m.order, m.version], ['archetypes', 20, 'arch-none'], what);
      const r = named();
      assert.doesNotThrow(() => m.derive(r), what);
      assert.deepEqual([r.d.arch, r.d.oarch], [null, null], what);
      assert.deepEqual(logged.map(line => JSON.parse(line).error), ['archetypes_dictionary_failed'], what);
    });
  }
});

test('without card ids, cardIdOf and dbfOf answer null and say so once', async () => {
  const dictionary = readFileSync(DICTIONARY, 'utf8');
  for (const [what, cards] of Object.entries({ missing: null, 'not JSON': '{"v": 1, "cards": {"7": "HERO_01",' })) {
    await isolated({ 'archetypes.json': dictionary, 'card-ids.json': cards }, (m, logged) => {
      for (let i = 0; i < 2; i++) {
        assert.equal(m.cardIdOf(mostPlayed.coreDbf[0]), null, what);
        assert.equal(m.dbfOf(mostPlayed.coreIds[0]), null, what);
      }
      assert.deepEqual(logged.map(line => JSON.parse(line).error), ['card_ids_failed'], what);
      const r = named();
      m.derive(r);
      assert.equal(r.d.arch, mostPlayed.key, 'classifying does not read them');
    });
  }
});

// --- The shipped dictionary ----------------------------------------------------------------------------------------

test('the shipped dictionary: shape, no HSGuru game counts, heroes, and a version that changes with the content', () => {
  const dict = read(DICTIONARY);
  assert.equal(dict.v, 1);
  assert.match(dict.built, /^\d{4}-\d{2}-\d{2}$/);
  assert.equal(dict.source, 'HSGuru via api.kolodahearthstone.com');
  assert.equal(dict.heroesSource, 'HearthstoneJSON v1/latest/enUS/cards.json');
  const digest = createHash('sha256').update(JSON.stringify({ formats: dict.formats, heroes: dict.heroes }), 'utf8').digest('hex').slice(0, 8);
  assert.equal(dict.rules, `arch-${dict.built.replaceAll('-', '')}-${digest}`, 'rules = arch-<built>-<sha-256 of formats and heroes>: edit the content, get a new version');
  assert.deepEqual(Object.keys(dict.formats), ['standard', 'wild']);
  for (const [fmt, classes] of Object.entries(dict.formats)) {
    const keys = new Set();
    for (const [cls, list] of Object.entries(classes)) {
      assert.ok(CLASSES.includes(cls), cls);
      assert.ok(list.length > 0);
      for (const a of list) {
        assert.deepEqual(Object.keys(a), ['key', 'coreIds', 'coreDbf'], `${fmt}/${a.key}: the order stands for HSGuru's game counts, which stay out`);
        assert.ok(typeof a.key === 'string' && a.key.length > 0);
        assert.ok(!keys.has(a.key.toUpperCase()), `${fmt}: ${a.key} twice`);
        keys.add(a.key.toUpperCase());
        assert.ok(a.coreIds.length > 0 && a.coreIds.length === a.coreDbf.length, `${a.key}: parallel core lists`);
        assert.equal(new Set(a.coreDbf).size, a.coreDbf.length);
      }
    }
    assert.ok(keys.size >= 30, `${fmt}: ${keys.size} archetypes`);
  }
  const listed = new Set();
  for (const [cls, dbfs] of Object.entries(dict.heroes)) {
    assert.ok(dbfs.length > 0 && dbfs.every((dbf, i) => Number.isInteger(dbf) && dbf > 0 && (i === 0 || dbfs[i - 1] < dbf)), cls);
    for (const dbf of dbfs) {
      assert.ok(!listed.has(dbf), `${dbf} in two classes`);
      listed.add(dbf);
    }
  }
  // Each class's own hero, the one DeckString.ClassHero names, has that class.
  const classHeroes = ['WARRIOR', 'SHAMAN', 'ROGUE', 'PALADIN', 'HUNTER', 'DRUID', 'WARLOCK', 'MAGE', 'PRIEST', 'DEMONHUNTER', 'DEATHKNIGHT'];
  classHeroes.forEach((cls, i) => assert.ok(dict.heroes[cls]?.includes(dbfOf(`HERO_${String(i + 1).padStart(2, '0')}`)), cls));
});

test('card ids: every core card resolves both ways, printing links stay inside the list', () => {
  const dict = read(DICTIONARY);
  const ids = read(CARD_IDS);
  assert.equal(ids.v, 1);
  assert.equal(ids.built, dict.built);
  assert.ok(Object.keys(ids.cards).length >= 5000);
  for (const classes of Object.values(dict.formats)) {
    for (const list of Object.values(classes)) {
      for (const a of list) {
        a.coreDbf.forEach((dbf, i) => {
          assert.equal(cardIdOf(dbf), a.coreIds[i], `${a.key}: ${dbf}`);
          assert.equal(dbfOf(a.coreIds[i]), dbf);
        });
      }
    }
  }
  for (const [dbf, original] of Object.entries(ids.copyOf)) {
    assert.ok(ids.cards[dbf] && ids.cards[String(original)] && Number(dbf) !== original, dbf);
  }
  assert.equal(cardIdOf(Number(Object.keys(ids.cards)[0])), Object.values(ids.cards)[0]);
  assert.equal(cardIdOf('not a dbf'), null);
  assert.equal(dbfOf('NOT_A_CARD_ID'), null);
});

// --- Agreement cases (AC11) ----------------------------------------------------------------------------------------

/** mulberry32: the same seed gives the same cases. */
function prng(seed) {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

/**
 * Synthetic records around the dictionary's own thresholds: decks with the whole core, exactly the share, one card
 * less, two archetypes, the class's cards mixed, the wrong class or format, noise, each in a deck code whose hero is
 * mostly of the record's class, else of another class, of no playable class, unknown or missing; opponents with an
 * archetype's own cards, 3, 4, 5 or more hits, two archetypes close together, two hits, the wrong class or format,
 * noise, repeats and a generated card. Then edges (modes, formats, classes, empty, unknown and broken decks, heroes).
 */
function makeCases(dict, cardIds) {
  const next = prng(SEED);
  const int = (min, max) => min + Math.floor(next() * (max - min + 1));
  const pick = list => list[Math.floor(next() * list.length)];
  const sample = (list, k) => {
    const copy = [...list];
    for (let i = copy.length - 1; i > 0; i--) {
      const j = Math.floor(next() * (i + 1));
      [copy[i], copy[j]] = [copy[j], copy[i]];
    }
    return copy.slice(0, Math.max(0, k));
  };
  const cards = Object.entries(cardIds.cards).map(([dbf, id]) => ({ dbf: Number(dbf), id }));
  const classes = fmt => Object.keys(dict.formats[fmt]);
  const of = (fmt, cls) => dict.formats[fmt][cls];
  const otherThan = (list, not) => pick(list.filter(x => x !== not));
  const fillers = new Map();
  const filler = (fmt, cls) => {
    const key = `${fmt}/${cls}`;
    if (!fillers.has(key)) {
      const classCore = new Set(of(fmt, cls).flatMap(a => a.coreDbf));
      fillers.set(key, cards.filter(c => !classCore.has(c.dbf)));
    }
    return fillers.get(key);
  };
  const fill = (fmt, cls, min, max) => sample(filler(fmt, cls), int(min, max)).map(c => c.dbf);
  const noise = (fmt, cls, min, max) => sample(filler(fmt, cls), int(min, max)).map(c => c.id).concat(next() < 0.2 ? ['GAME_005'] : []);
  const atShare = n => {
    let k = 0;
    while (k / n < MIN_SHARE) k++;
    return k;
  };
  const some = (a, min, max) => sample(a.coreDbf, int(min, max));
  const deck = dbfs => [...new Set(dbfs)].sort((x, y) => x - y).map(dbf => [dbf, int(1, 2)]);
  const plays = ids => {
    const shown = [...ids, ...sample(ids, int(0, 2))];
    return shown.map((id, i) => [1 + Math.floor(i / 2), id]);
  };
  const otherFormat = fmt => (fmt === 'standard' ? 'wild' : 'standard');

  const decks = {
    'core-all': (fmt, cls, a) => ({ cls, cards: [...a.coreDbf, ...fill(fmt, cls, 3, 10)] }),
    'share-at': (fmt, cls, a) => ({ cls, cards: [...some(a, atShare(a.coreDbf.length), atShare(a.coreDbf.length)), ...fill(fmt, cls, 3, 12)] }),
    'share-below': (fmt, cls, a) => ({ cls, cards: [...some(a, atShare(a.coreDbf.length) - 1, atShare(a.coreDbf.length) - 1), ...fill(fmt, cls, 3, 12)] }),
    'share-any': (fmt, cls, a) => ({ cls, cards: [...some(a, 0, a.coreDbf.length), ...fill(fmt, cls, 3, 12)] }),
    'two': (fmt, cls, a) => {
      const b = otherThan(of(fmt, cls), a);
      return { cls, cards: [...some(a, atShare(a.coreDbf.length) - 1, a.coreDbf.length), ...some(b, atShare(b.coreDbf.length) - 1, b.coreDbf.length), ...fill(fmt, cls, 0, 4)] };
    },
    'two-full': (fmt, cls, a) => ({ cls, cards: [...a.coreDbf, ...otherThan(of(fmt, cls), a).coreDbf] }),
    'class-mix': (fmt, cls) => ({ cls, cards: sample([...new Set(of(fmt, cls).flatMap(x => x.coreDbf))], int(8, 20)) }),
    'wrong-class': (fmt, cls, a) => ({ cls: otherThan(classes(fmt), cls), cards: [...a.coreDbf, ...fill(fmt, cls, 3, 10)] }),
    'other-format': fmt => {
      const cls = pick(classes(otherFormat(fmt)));
      return { cls, cards: [...pick(of(otherFormat(fmt), cls)).coreDbf, ...fill(fmt, cls, 3, 10)] };
    },
    'noise': (fmt, cls) => ({ cls, cards: fill(fmt, cls, 12, 25) }),
  };
  // Core cards of an archetype no other archetype of its class has: a clear call when enough of them are seen.
  const distinct = (fmt, cls, a) => {
    const others = new Set(of(fmt, cls).filter(x => x !== a).flatMap(x => x.coreIds));
    return a.coreIds.filter(id => !others.has(id));
  };
  const opponents = {
    'distinct': (fmt, cls, a) => ({ ocls: cls, seen: [...sample(distinct(fmt, cls, a), int(3, 5)), ...sample(a.coreIds, int(0, 2)), ...noise(fmt, cls, 0, 2)] }),
    'hits-3': (fmt, cls, a) => ({ ocls: cls, seen: [...sample(a.coreIds, 3), ...noise(fmt, cls, 0, 3)] }),
    'hits-4': (fmt, cls, a) => ({ ocls: cls, seen: [...sample(a.coreIds, 4), ...noise(fmt, cls, 0, 3)] }),
    'hits-5': (fmt, cls, a) => ({ ocls: cls, seen: [...sample(a.coreIds, 5), ...noise(fmt, cls, 0, 3)] }),
    'hits-many': (fmt, cls, a) => ({ ocls: cls, seen: [...sample(a.coreIds, int(6, 10)), ...noise(fmt, cls, 0, 4)] }),
    'two': (fmt, cls, a) => ({ ocls: cls, seen: [...sample(a.coreIds, int(3, 5)), ...sample(otherThan(of(fmt, cls), a).coreIds, int(1, 4))] }),
    'two-close': (fmt, cls, a) => {
      const k = int(3, 5);
      return { ocls: cls, seen: [...sample(a.coreIds, k), ...sample(otherThan(of(fmt, cls), a).coreIds, k - int(0, 2))] };
    },
    'few': (fmt, cls, a) => ({ ocls: cls, seen: [...sample(a.coreIds, 2), ...noise(fmt, cls, 0, 2)] }),
    'wrong-class': (fmt, cls, a) => ({ ocls: otherThan(classes(fmt), cls), seen: sample(a.coreIds, int(4, 6)) }),
    'other-format': fmt => {
      const cls = pick(classes(otherFormat(fmt)));
      return { ocls: cls, seen: sample(pick(of(otherFormat(fmt), cls)).coreIds, int(4, 6)) };
    },
    'noise': (fmt, cls) => ({ ocls: cls, seen: noise(fmt, cls, 3, 8) }),
  };
  const { heroes } = dict;
  const playable = Object.keys(heroes).filter(c => CLASSES.includes(c));
  const heroOf = cls => {
    const roll = next();
    if (roll < 0.74) return { kind: 'hero', heroes: [pick(heroes[cls])] };
    if (roll < 0.86) return { kind: 'other-hero', heroes: [pick(heroes[otherThan(playable, cls)])] };
    if (roll < 0.92) return { kind: 'neutral-hero', heroes: [pick(heroes.NEUTRAL)] };
    if (roll < 0.97) return { kind: 'unknown-hero', heroes: [UNKNOWN_HERO] };
    return { kind: 'no-hero', heroes: [] };
  };

  const records = [];
  const deckKinds = Object.keys(decks);
  const opponentKinds = Object.keys(opponents);
  for (const fmt of ['standard', 'wild']) {
    for (let i = 0; i < PER_FORMAT; i++) {
      const ownClass = pick(classes(fmt));
      const opponentClass = pick(classes(fmt));
      const deckKind = deckKinds[i % deckKinds.length];
      const opponentKind = opponentKinds[Math.floor(i / deckKinds.length) % opponentKinds.length];
      const own = decks[deckKind](fmt, ownClass, pick(of(fmt, ownClass)));
      const opponent = opponents[opponentKind](fmt, opponentClass, pick(of(fmt, opponentClass)));
      const hero = heroOf(own.cls);
      const list = deck(own.cards);
      records.push({ case: `${fmt}/${deckKind}/${opponentKind}/${hero.kind}`, mode: i % 7 === 6 ? 'casual' : 'ranked', fmt, cls: own.cls, cards: list,
        deck: deckCode(fmt, hero.heroes, list), ocls: opponent.ocls, oplays: plays(opponent.seen) });
    }
  }

  const [cls] = classes('standard');
  const a = of('standard', cls)[0];
  const [hero, skin] = heroes[cls];
  const other = heroes[playable.find(c => c !== cls)][0];
  const coreDeck = deck(a.coreDbf);
  const code = (...ids) => deckCode('standard', ids, coreDeck);
  const unknown = [[999999001, 2], [999999002, 1], [999999003, 2]];
  const base = { mode: 'ranked', fmt: 'standard', cls, cards: coreDeck, deck: code(hero), ocls: cls, oplays: plays(a.coreIds.slice(0, 5)) };
  const edges = [
    { case: 'edge/ranked' },
    { case: 'edge/casual', mode: 'casual' },
    { case: 'edge/arena', mode: 'arena' },
    { case: 'edge/brawl', mode: 'brawl' },
    { case: 'edge/bg', mode: 'bg', fmt: null },
    { case: 'edge/classic', fmt: 'classic' },
    { case: 'edge/twist', fmt: 'twist' },
    { case: 'edge/no-format', fmt: null },
    { case: 'edge/no-class', cls: null, ocls: null },
    { case: 'edge/neutral', cls: 'NEUTRAL', ocls: 'NEUTRAL' },
    { case: 'edge/empty', cards: [], deck: null, oplays: [] },
    { case: 'edge/undecoded', cards: null, deck: 'not-a-deck-code!', oplays: null },
    { case: 'edge/unknown-cards', cards: unknown, deck: deckCode('standard', [hero], unknown), oplays: [[1, 'ZZZ_001'], [1, 'ZZZ_002'], [2, 'ZZZ_003'], [2, 'ZZZ_004']] },
    { case: 'edge/two-seen', oplays: [[1, a.coreIds[0]], [1, a.coreIds[1]], [2, a.coreIds[0]], [3, a.coreIds[1]]] },
    // The code's hero (GameRecords.DeckFits): another of the class, one of another class, of no playable class, an
    // unknown one, none, and an unknown one before one of another class.
    { case: 'edge/skin', deck: code(skin ?? hero) },
    { case: 'edge/other-hero', deck: code(other) },
    { case: 'edge/neutral-hero', deck: code(heroes.NEUTRAL[0]) },
    { case: 'edge/unknown-hero', deck: code(UNKNOWN_HERO) },
    { case: 'edge/no-hero', deck: code() },
    { case: 'edge/second-hero', deck: code(UNKNOWN_HERO, other) },
  ];
  // Two archetypes of a class, a deck holding both cores whole: the earlier of them (the app's tie rule).
  const pair = equalShare(dict);
  if (pair) {
    const both = deck([...pair.first.coreDbf, ...pair.second.coreDbf]);
    edges.push({ case: 'edge/equal-share', fmt: pair.fmt, cls: pair.cls, cards: both, deck: deckCode(pair.fmt, [heroes[pair.cls][0]], both), ocls: pair.cls,
      oplays: plays([...pair.first.coreIds.slice(0, 4), ...pair.second.coreIds.slice(0, 4)]) });
  }
  for (const edge of edges) {
    const record = { ...base, ...edge };
    if (record.deck === null) delete record.deck;
    records.push(record);
  }
  return { v: 1, rules: dict.rules, seed: SEED, records };
}

/** A hero dbf id no card has. */
const UNKNOWN_HERO = 999999999;

/** The second and third archetypes of the first class that has three, or null: a tie the class's first does not take. */
function equalShare(dict) {
  for (const [fmt, classes] of Object.entries(dict.formats)) {
    for (const [cls, list] of Object.entries(classes)) if (list.length >= 3) return { fmt, cls, first: list[1], second: list[2] };
  }
  return null;
}

const lines = (head, rows, tail) => `${head}\n${rows.map(row => `    ${JSON.stringify(row)}`).join(',\n')}\n${tail}\n`;

if (process.env.HP_REGEN_ARCHETYPES === '1') {
  const cases = makeCases(read(DICTIONARY), read(CARD_IDS));
  const answers = cases.records.map(answer);
  const counts = {
    records: cases.records.length,
    decks: cases.records.filter(r => Array.isArray(r.cards)).length,
    codes: cases.records.filter(r => typeof r.deck === 'string').length,
    opponents: cases.records.filter(r => Array.isArray(r.oplays)).length,
    arch: answers.filter(x => x[0] !== null).length,
    oarch: answers.filter(x => x[1] !== null).length,
  };
  writeFileSync(CASES, lines(`{\n  "v": 1,\n  "rules": ${JSON.stringify(cases.rules)},\n  "seed": ${cases.seed},\n  "records": [`, cases.records, '  ]\n}'));
  writeFileSync(GOLDEN, lines(`{\n  "v": 1,\n  "rules": ${JSON.stringify(cases.rules)},\n  "counts": ${JSON.stringify(counts)},\n  "answers": [`, answers, '  ]\n}'));
}

test('agreement cases: made for the shipped dictionary, at least 200 decks and 200 opponents', () => {
  const cases = read(CASES);
  const golden = read(GOLDEN);
  assert.equal(cases.rules, version, 'the cases are for another dictionary: remake them (HP_REGEN_ARCHETYPES=1)');
  assert.equal(golden.rules, version);
  assert.equal(golden.answers.length, cases.records.length);
  assert.ok(cases.records.filter(r => Array.isArray(r.cards) && r.cards.length > 0 && typeof r.deck === 'string').length >= 200);
  assert.ok(cases.records.filter(r => Array.isArray(r.oplays) && r.oplays.length > 0).length >= 200);
  assert.deepEqual(cases, makeCases(read(DICTIONARY), read(CARD_IDS)), 'the cases are the generator\'s for this dictionary and seed');
  // Both answers and nulls in good number on both sides, or the agreement says little.
  for (const side of [0, 1]) {
    const named = golden.answers.filter(x => x[side] !== null).length;
    assert.ok(named >= 0.2 * golden.answers.length && named <= 0.8 * golden.answers.length, `side ${side}: ${named} named`);
  }
});

test('agreement cases: the deriver gives the golden answers', () => {
  const cases = read(CASES);
  const golden = read(GOLDEN);
  const wrong = cases.records.map((record, i) => ({ i, record, got: answer(record), want: golden.answers[i] }))
    .filter(x => x.got[0] !== x.want[0] || x.got[1] !== x.want[1]);
  assert.deepEqual(wrong.slice(0, 5).map(x => `${x.i} ${x.record.case}: ${JSON.stringify(x.got)} != ${JSON.stringify(x.want)}`), []);
  assert.equal(wrong.length, 0);
});

test('agreement cases: the edges answer as the rules say', () => {
  const cases = read(CASES);
  const golden = read(GOLDEN);
  const by = Object.fromEntries(cases.records.map((r, i) => [r.case, golden.answers[i]]));
  const { formats } = read(DICTIONARY);
  const [cls] = Object.keys(formats.standard);
  assert.equal(by['edge/ranked'][0], formats.standard[cls][0].key);
  assert.deepEqual(by['edge/casual'], by['edge/ranked']);
  for (const edge of ['edge/arena', 'edge/brawl', 'edge/bg', 'edge/classic', 'edge/twist', 'edge/no-format', 'edge/no-class', 'edge/neutral', 'edge/empty', 'edge/undecoded', 'edge/unknown-cards']) {
    assert.deepEqual(by[edge], [null, null], edge);
  }
  assert.equal(by['edge/two-seen'][1], null);
  for (const edge of ['edge/skin', 'edge/unknown-hero', 'edge/no-hero']) assert.deepEqual(by[edge], by['edge/ranked'], edge);
  for (const edge of ['edge/other-hero', 'edge/neutral-hero', 'edge/second-hero']) assert.deepEqual(by[edge], [null, by['edge/ranked'][1]], edge);
  const pair = equalShare(read(DICTIONARY));
  if (pair) {
    const both = compile({ v: 1, rules: 'pair', formats: { [pair.fmt]: { [pair.cls]: [pair.first, pair.second] } } });
    const record = cases.records.find(r => r.case === 'edge/equal-share');
    assert.equal(ownArchetype(both, pair.fmt, pair.cls, new Map(record.cards)), pair.first.key, 'both cores whole: the earlier');
    assert.equal(typeof by['edge/equal-share'][0], 'string');
  }
});
