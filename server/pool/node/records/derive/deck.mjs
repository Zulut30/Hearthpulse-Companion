// Producer `deck` (TASK-2026-11-26-pro-collection B2, contract К2; AC10): the deck code of a game record as a card
// list, the key of that list and the canonical code that may be published.
//
// - The list is the code's main deck: dbf id -> copies. A sideboard (E.T.C.'s band, Zilliax's modules) is read but is
//   not part of the list.
// - The key is the client's (DeckRecords.CardListKey): "<dbf>x<copies>" in dbf order, joined by commas. So a hero
//   skin, the format byte, the order of the cards and the sideboard never split one list, and a client can find its
//   own deck among published rows by the key it already has.
// - The canonical code (DeckString.Encode) has the class's default hero, the record's format, the cards by copies in
//   dbf order and no sideboard. A rare hero skin in a published code could single a player out (card, decision 17).
//
// On every code the game writes, the reader gives the list Cards/DeckString.TryDecode gives; derive-deck.test.mjs and
// DeckKeyAgreementTests hold both to test/fixtures/deckcodes.json. On codes no game writes it is stricter than the
// client, and such a code is broken here (null): base64 that Convert.FromBase64String refuses (Node's own decoder
// skips bad characters), a varint longer than 5 bytes or above 2^31 - 1 (C# wraps it, a count into a negative one), a
// dbf id or copy count under 1, a main deck without cards.

/** Format byte of a code -> the record's `fmt` (DeckString: 1 Wild, 2 Standard, 3 Classic, 4 Twist). */
const FORMATS = [null, 'wild', 'standard', 'classic', 'twist'];

/**
 * Class -> dbf id of its default hero, the hero DeckString.ClassHero names (HERO_01 ... HERO_11), with the dbf ids of
 * HearthstoneJSON (enUS cards.collectible.json, read 2026-11-26). These ids never change; the fixture repeats them
 * with their card ids, and DeckKeyAgreementTests checks the card ids against ClassHero.
 */
export const CLASS_HEROES = Object.freeze({
  WARRIOR: 7,
  SHAMAN: 1066,
  ROGUE: 930,
  PALADIN: 671,
  HUNTER: 31,
  DRUID: 274,
  WARLOCK: 893,
  MAGE: 637,
  PRIEST: 813,
  DEMONHUNTER: 56550,
  DEATHKNIGHT: 78065,
});

// Whole base64 quads, padding only at the end: what Convert.FromBase64String accepts once the code is trimmed.
const BASE64 = /^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/;
const MAX_INT = 0x7fffffff;

class Broken extends Error {}

/** The varint stream of a code; every read past the end or out of range throws Broken. */
class Reader {
  constructor(bytes) { this.bytes = bytes; this.at = 0; }

  get done() { return this.at >= this.bytes.length; }

  /** An unsigned varint of at most 5 bytes that fits a C# int. */
  varint() {
    let value = 0;
    for (let shift = 0; shift <= 28; shift += 7) {
      if (this.at >= this.bytes.length) throw new Broken();
      const b = this.bytes[this.at++];
      value += (b & 0x7f) * 2 ** shift;
      if ((b & 0x80) === 0) {
        if (value > MAX_INT) throw new Broken();
        return value;
      }
    }
    throw new Broken();
  }

  /** A dbf id or a copy count: 1 or more. */
  positive() {
    const value = this.varint();
    if (value < 1) throw new Broken();
    return value;
  }

  /** The three groups of a list - cards x1, x2 and xN (dbf, copies) - with `owner` read after each card. */
  groups(owner = false) {
    const list = new Map();
    for (let group = 1; group <= 3; group++) {
      for (let n = this.varint(); n > 0; n--) {
        const dbf = this.positive();
        const copies = group === 3 ? this.positive() : group;
        if (owner) this.varint();
        list.set(dbf, (list.get(dbf) ?? 0) + copies);
      }
    }
    return byDbf(list);
  }
}

const byDbf = list => new Map([...list].sort((a, b) => a[0] - b[0]));
const isList = cards => cards instanceof Map && cards.size > 0
  && [...cards].every(([dbf, copies]) => Number.isSafeInteger(dbf) && dbf > 0 && dbf <= MAX_INT && Number.isSafeInteger(copies) && copies > 0 && copies <= MAX_INT);

/**
 * The optional tail of a code: a flag, then the sideboard cards in the same three groups, each card followed by the
 * dbf id of its owner. An empty Map without a tail or with the flag off, null when the flag is set and the tail does
 * not read (DeckDefinition.SideboardUnread).
 */
function sideboardOf(reader) {
  try {
    if (reader.done) return new Map();
    const flag = reader.varint();
    if (flag === 0) return new Map();
    return flag === 1 ? reader.groups(true) : null;
  } catch {
    return null;
  }
}

/**
 * A deck code as `{fmt, heroes, cards, sideboard}`: `fmt` the record's name of the format byte (null when unknown),
 * `heroes` the hero dbf ids as written, `cards` the main deck (dbf id -> copies, in dbf order), `sideboard` the same
 * for the sideboards (empty when the code has none, null when the code has one that does not read). Null when the code
 * is broken. Never throws.
 */
export function decodeDeck(code) {
  if (typeof code !== 'string') return null;
  const text = code.trim();
  if (text.length === 0 || !BASE64.test(text)) return null;
  const reader = new Reader(Buffer.from(text, 'base64'));
  try {
    if (reader.varint() !== 0 || reader.varint() !== 1) return null;
    const fmt = FORMATS[reader.varint()] ?? null;
    const heroes = [];
    for (let n = reader.varint(); n > 0; n--) heroes.push(reader.positive());
    const cards = reader.groups();
    if (cards.size === 0) return null;
    return { fmt, heroes, cards, sideboard: sideboardOf(reader) };
  } catch {
    return null;
  }
}

/**
 * The key of a main deck (dbf id -> copies): "<dbf>x<copies>" in dbf order, joined by commas - DeckRecords.CardListKey
 * of the client. Null for anything that is not a non-empty list of positive dbf ids and copy counts.
 */
export function deckKey(cards) {
  if (!isList(cards)) return null;
  return [...byDbf(cards)].map(([dbf, copies]) => `${dbf}x${copies}`).join(',');
}

const varints = values => {
  const bytes = [];
  for (let v of values) {
    while (v >= 0x80) { bytes.push((v & 0x7f) | 0x80); v = Math.floor(v / 0x80); }
    bytes.push(v);
  }
  return Buffer.from(bytes);
};

/**
 * The canonical code of a main deck (dbf id -> copies) for a class (`MAGE`) and a record format (`standard`), as
 * DeckString.Encode writes it: version 1, the format, one hero - the class's default -, the cards x1, x2 and xN in dbf
 * order, the sideboard flag off. Null for an unknown class or format, or a list `deckKey` refuses.
 */
export function encodeDeck(cards, cls, fmt) {
  const heroKey = typeof cls === 'string' ? cls.toUpperCase() : null;
  const hero = heroKey !== null && Object.hasOwn(CLASS_HEROES, heroKey) ? CLASS_HEROES[heroKey] : null;
  const format = typeof fmt === 'string' ? FORMATS.indexOf(fmt.toLowerCase()) : -1;
  if (hero === null || format < 1 || !isList(cards)) return null;
  const list = [...byDbf(cards)];
  const values = [0, 1, format, 1, hero];
  for (const group of [1, 2, 3]) {
    const cardsOf = list.filter(([, copies]) => (group === 3 ? copies >= 3 : copies === group));
    values.push(cardsOf.length);
    for (const [dbf, copies] of cardsOf) values.push(...(group === 3 ? [dbf, copies] : [dbf]));
  }
  values.push(0);
  return varints(values).toString('base64');
}

export const name = 'deck';
export const order = 10;
export const version = 'deck-1';

/**
 * Fills `rec.d.cards` (the main deck of `rec.body.deck`, dbf id -> copies) and `rec.d.deckKey`; both null when the
 * record has no code or a broken one. The list does not depend on the window, so `ctx` is not read. Never throws.
 */
export function derive(rec, ctx) {
  if (rec === null || typeof rec !== 'object') return;
  try {
    if (rec.d === null || typeof rec.d !== 'object') rec.d = {};
    const deck = decodeDeck(rec.body?.deck);
    rec.d.cards = deck?.cards ?? null;
    rec.d.deckKey = deck ? deckKey(deck.cards) : null;
  } catch {
    try { rec.d.cards = null; rec.d.deckKey = null; } catch { /* a frozen record keeps what it has */ }
  }
}
