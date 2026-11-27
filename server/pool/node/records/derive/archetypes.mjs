// Archetypes of a game record (contract K2, TASK-2026-11-26-pro-collection B1): the player's own archetype from the
// deck (rec.d.cards, filled by the deck deriver before this one) and the opponent's from the cards it showed
// (body.oplays, body.ocls), by the app's own rules - GameRecords.DeckFits, ConstructedMeta.Match and
// ArchetypeGuess.FromSeenCards - over one dictionary built from HSGuru's catalogue and HearthstoneJSON's heroes
// (server/pool/data/archetypes.json, made by tools/archetypes/server_dictionary.py). The client's guess (body.oarch, a
// localised name) is never read. The version is the dictionary's rules: the views' envelopes name it and a new
// dictionary rebuilds them. Unknown or unclear is null, and derive never throws.
import { readFileSync } from 'node:fs';

export const name = 'archetypes';
export const order = 20;

/** ConstructedMeta.Match: a deck is the archetype of its class whose core cards it holds the largest share of, from 60%. */
export const MIN_SHARE = 0.6;
/** ArchetypeGuess.MinHits: the opponent's archetype holds at least this many of the seen cards among its core cards ... */
export const MIN_HITS = 3;
/** ... ArchetypeGuess.MinLead: and this many more than any other archetype of the class. */
export const MIN_LEAD = 2;
/** The modes the app names a deck's archetype in (GameRecords.Build); any other record gets null for both. */
const MODES = new Set(['ranked', 'casual']);

const DATA = new URL('../../../data/', import.meta.url);

/**
 * The dictionary as the classifiers read it: format -> class -> archetypes in the file's order, the most played first as
 * in the app's meta (the file holds no game counts: its order is all the ties need), each with its key and distinct core
 * cards by card id and by dbf id; and hero dbf id -> class. Archetypes without core cards are left out: the app skips
 * them. Throws on a shape it does not know.
 */
export function compile(json) {
  if (json?.v !== 1 || typeof json.rules !== 'string' || json.rules.length === 0 || !json.formats || typeof json.formats !== 'object') {
    throw new Error('unknown archetype dictionary');
  }
  const formats = new Map();
  for (const [fmt, classes] of Object.entries(json.formats)) {
    const byClass = new Map();
    for (const [cls, list] of Object.entries(classes ?? {})) {
      if (!Array.isArray(list)) throw new Error(`archetypes of ${fmt}/${cls} are not a list`);
      const archetypes = [];
      for (const [index, a] of list.entries()) {
        const ids = [...new Set(a?.coreIds ?? [])];
        const dbf = [...new Set(a?.coreDbf ?? [])];
        if (typeof a?.key !== 'string' || ids.length !== dbf.length || !ids.every(id => typeof id === 'string') || !dbf.every(Number.isInteger)) {
          throw new Error(`archetype ${fmt}/${cls}/${index} has another shape`);
        }
        if (ids.length > 0) archetypes.push({ key: a.key, ids, dbf });
      }
      byClass.set(cls.toUpperCase(), archetypes);
    }
    formats.set(fmt, byClass);
  }
  const heroes = new Map();
  for (const [cls, list] of Object.entries(json.heroes ?? {})) {
    if (!Array.isArray(list) || !list.every(dbf => Number.isInteger(dbf) && dbf > 0)) throw new Error(`heroes of ${cls} are not dbf ids`);
    for (const dbf of list) heroes.set(dbf, cls.toUpperCase());
  }
  return { rules: json.rules, formats, heroes };
}

/** The deployed dictionary; a missing or broken file is one log line and a dictionary that names no archetype. */
function load() {
  try {
    return compile(JSON.parse(readFileSync(new URL('archetypes.json', DATA), 'utf8')));
  } catch (error) {
    console.error(JSON.stringify({ ok: false, error: 'archetypes_dictionary_failed', cause: String(error?.message ?? error).slice(0, 200) }));
    return { rules: 'arch-none', formats: new Map(), heroes: new Map() };
  }
}

const dictionary = load();

/** The dictionary's rules ("arch-<date>-<hash>"), "arch-none" without one. */
export const version = dictionary.rules;

/** The hero dbf ids a deck code names after its version and format; none when the code does not read. */
function heroesOf(code) {
  if (typeof code !== 'string') return [];
  const bytes = Buffer.from(code.trim(), 'base64');
  let at = 0;
  const varint = () => {
    let value = 0;
    for (let shift = 0; shift <= 28; shift += 7) {
      if (at >= bytes.length) return null;
      const b = bytes[at++];
      value += (b & 0x7f) * 2 ** shift;
      if ((b & 0x80) === 0) return value;
    }
    return null;
  };
  if (varint() !== 0 || varint() !== 1 || varint() === null) return [];
  const heroes = [];
  for (let n = varint() ?? 0; n > 0; n--) {
    const dbf = varint();
    if (dbf === null) return [];
    heroes.push(dbf);
  }
  return heroes;
}

/**
 * GameRecords.DeckFits: false when the deck code's hero is of another class than the record's, a deck the client still
 * held from an earlier game. The hero's class is that of the first of the code's heroes the dictionary knows; an unknown
 * hero or no class passes.
 */
export function deckFits(dict, code, cls) {
  if (typeof cls !== 'string' || cls.length === 0) return true;
  for (const dbf of heroesOf(code)) {
    const heroClass = dict.heroes.get(dbf);
    if (heroClass !== undefined) return heroClass === cls.toUpperCase();
  }
  return true;
}

/**
 * ConstructedMeta.Match over the deck's dbf ids (a Map dbf -> copies): the key of the archetype of the class whose core
 * the deck holds the largest share of, at least MIN_SHARE; on an equal share the earlier one, the more played. Null when
 * none reaches it, the format or the class has no archetypes, or the deck is unknown or empty.
 */
export function ownArchetype(dict, fmt, cls, cards) {
  if (!(cards instanceof Map) || cards.size === 0 || typeof cls !== 'string') return null;
  const archetypes = dict.formats.get(fmt)?.get(cls.toUpperCase());
  if (!archetypes) return null;
  let best = null;
  let bestScore = 0;
  for (const archetype of archetypes) {
    let held = 0;
    for (const dbf of archetype.dbf) if (cards.has(dbf)) held++;
    const score = held / archetype.dbf.length;
    if (score >= MIN_SHARE && score > bestScore) {
      best = archetype;
      bestScore = score;
    }
  }
  return best?.key ?? null;
}

/**
 * ArchetypeGuess.FromSeenCards over the card ids the opponent showed (repeats count once): the key of the archetype of
 * its class holding the most of them among its core cards, when that is at least MIN_HITS and MIN_LEAD more than the
 * next one. Null for anything less clear: a guess would mix matchups.
 */
export function opponentArchetype(dict, fmt, ocls, seenIds) {
  if (typeof ocls !== 'string' || ocls.length === 0) return null;
  const seen = new Set(seenIds);
  if (seen.size < MIN_HITS) return null;
  const archetypes = dict.formats.get(fmt)?.get(ocls.toUpperCase());
  if (!archetypes) return null;
  const scored = archetypes.map(archetype => {
    let hits = 0;
    for (const id of archetype.ids) if (seen.has(id)) hits++;
    return { archetype, hits };
  });
  // The app's order: most hits, then the more played - the dictionary's order, which Array.prototype.sort keeps.
  scored.sort((a, b) => b.hits - a.hits);
  if (scored.length === 0 || scored[0].hits < MIN_HITS) return null;
  const runnerUp = scored.length > 1 ? scored[1].hits : 0;
  return scored[0].hits - runnerUp >= MIN_LEAD ? scored[0].archetype.key : null;
}

/** The card ids of body.oplays ([[turn, card id], ...]); anything else shows no cards. */
function seenOf(oplays) {
  const ids = [];
  if (Array.isArray(oplays)) for (const row of oplays) if (Array.isArray(row) && typeof row[1] === 'string') ids.push(row[1]);
  return ids;
}

const quietly = classify => {
  try {
    return classify();
  } catch {
    return null;
  }
};

/** Fills rec.d.arch and rec.d.oarch: an archetype key, or null when unknown or unclear. Never throws. */
export function derive(rec) {
  try {
    const constructed = MODES.has(rec.mode);
    const body = rec.body ?? {};
    const d = (rec.d ??= {});
    d.arch = constructed ? quietly(() => (deckFits(dictionary, body.deck, body.cls) ? ownArchetype(dictionary, rec.fmt, body.cls, d.cards) : null)) : null;
    d.oarch = constructed ? quietly(() => opponentArchetype(dictionary, rec.fmt, body.ocls, seenOf(body.oplays))) : null;
  } catch {
    // Not a record: there is nothing to fill.
  }
}

let cardIds = null;

/**
 * dbf id <-> card id of every collectible card and every core card (server/pool/data/card-ids.json), read once; a
 * missing or broken file is one log line and no cards, so a view asking for a card gets null instead of an error.
 */
function cardsFile() {
  if (cardIds) return cardIds;
  const byDbf = new Map();
  const byId = new Map();
  try {
    const json = JSON.parse(readFileSync(new URL('card-ids.json', DATA), 'utf8'));
    if (json?.v !== 1 || !json.cards || typeof json.cards !== 'object') throw new Error('unknown card ids file');
    for (const [dbf, id] of Object.entries(json.cards)) {
      byDbf.set(Number(dbf), id);
      byId.set(id, Number(dbf));
    }
  } catch (error) {
    console.error(JSON.stringify({ ok: false, error: 'card_ids_failed', cause: String(error?.message ?? error).slice(0, 200) }));
  }
  cardIds = { byDbf, byId };
  return cardIds;
}

/** The card id of a dbf id, or null. */
export function cardIdOf(dbf) {
  return cardsFile().byDbf.get(Number(dbf)) ?? null;
}

/** The dbf id of a card id, or null. */
export function dbfOf(cardId) {
  return cardsFile().byId.get(cardId) ?? null;
}
