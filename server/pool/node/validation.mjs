import { createHash } from 'node:crypto';
import { createGunzip } from 'node:zlib';
import { Readable } from 'node:stream';

export const MiB = 1024 * 1024;
export const HEX32 = /^[a-f0-9]{32}$/;
export const HEX64 = /^[a-f0-9]{64}$/;
export const MODES = ['ranked', 'casual', 'arena', 'brawl', 'bg', 'duos'];
const SIDES = ['none', 'local', 'opponent', 'shop', 'partner_public'];
const ZONES = ['HAND', 'PLAY', 'DECK', 'GRAVEYARD', 'SETASIDE', 'SHOP', 'UNKNOWN'];
const TAGS = new Set(('TURN STEP RESOURCES RESOURCES_USED TEMP_RESOURCES OVERLOAD_LOCKED OVERLOAD_OWED DAMAGE HEALTH ATK ARMOR DURABILITY COST TECH_LEVEL PREMIUM ZONE_POSITION TAUNT DIVINE_SHIELD WINDFURY REBORN STEALTH FROZEN POISONOUS VENOMOUS').split(' '));
export class CaptureError extends Error {
  constructor(status, code, details = {}) { super(code); this.status = status; this.code = code; this.details = details; }
}
export const fail = (status, code, details) => { throw new CaptureError(status, code, details); };
export const int = (x, min = 0, max = 1_000_000) => Number.isSafeInteger(x) && x >= min && x <= max;
export const object = x => x !== null && typeof x === 'object' && !Array.isArray(x);
export const exact = (x, keys) => object(x) && Object.keys(x).length === keys.length && keys.every(k => Object.hasOwn(x, k));
const card = x => typeof x === 'string' && /^[A-Za-z0-9_]{1,80}$/.test(x);
const nullable = (x, check) => x === null || check(x);
const cards = (x, max) => Array.isArray(x) && x.length <= max && x.every(card);
const number = x => int(x, -1_000_000, 1_000_000);
const tags = x => object(x) && Object.keys(x).length <= TAGS.size && Object.entries(x).every(([k, v]) => TAGS.has(k) && number(v));
const fields = (d, names) => exact(d, names.split(' '));
export const FORMATS = ['standard', 'wild', 'classic', 'twist'];
export const ARENAS = ['normal', 'underground'];
export const CHOICE_TYPES = ['mulligan', 'general', 'other'];
export const LIFECYCLE_GAPS = ['late_attach', 'rotation', 'shutdown'];
const GAP_REASONS = ['late_attach', 'rotation', 'source_missing', 'unsupported', 'limit', 'invalid', 'shutdown'];
const hidden = (side, zone) => side !== 'local' && ['HAND', 'DECK', 'SETASIDE', 'UNKNOWN'].includes(zone);
function visibleEntity(x) {
  return SIDES.includes(x.side) && ZONES.includes(x.zone) && int(x.entity, 1) && int(x.position, 0, 1000)
    && nullable(x.card, card) && tags(x.tags)
    && (!hidden(x.side, x.zone) || (x.card === null && Object.keys(x.tags).every(t => t === 'ZONE_POSITION')))
    && x.side !== 'none' && (x.side !== 'shop' || x.zone === 'SHOP')
    && (x.side !== 'opponent' || x.zone !== 'SHOP') && (x.side !== 'partner_public' || ['PLAY', 'GRAVEYARD'].includes(x.zone));
}

/**
 * Strict allowlist, never a sanitizer of arbitrary raw log text. Source v2 (TASK-2026-11-09 C1) adds start.format and
 * start.arena, choice.choiceType and the gap reason combat_view; a v1 line carrying any of them is invalid. Source v3
 * (TASK-2026-11-19 U) is v2 plus the bg_combat record; a v1/v2 line of that kind is invalid.
 */
export function validRecord(r) {
  if (!fields(r, 'v seq kind turn side entity card target data') || ![1, 2, 3].includes(r.v) || !int(r.seq, 0, 10_000_000)
    || !int(r.turn, 0, 1000) || !SIDES.includes(r.side) || !int(r.entity) || !int(r.target)
    || !nullable(r.card, card) || !object(r.data) || (r.side === 'none' && r.card !== null)) return false;
  const d = r.data;
  switch (r.kind) {
    case 'start': return fields(d, r.v === 1 ? 'captureKind mode day build' : 'captureKind mode day build format arena')
      && ['game', 'draft'].includes(d.captureKind) && MODES.includes(d.mode)
      && (r.v === 1 || (nullable(d.format, v => FORMATS.includes(v) && ['ranked', 'casual'].includes(d.mode))
        && nullable(d.arena, v => ARENAS.includes(v) && d.mode === 'arena'))) && int(d.day, 0, 100000) && nullable(d.build, v => int(v, 1, 2147483647))
      && r.turn === 0 && r.side === 'none' && r.entity === 0 && r.target === 0;
    case 'end': return fields(d, 'status result placement reason') && ['complete', 'incomplete'].includes(d.status)
      && nullable(d.result, v => ['W', 'L', 'T'].includes(v)) && nullable(d.placement, v => int(v, 1, 8))
      && ['normal', 'abandoned', 'rotation', 'shutdown', 'unsupported', 'unknown'].includes(d.reason)
      && r.side === 'none' && r.entity === 0 && r.target === 0;
    case 'entity': return fields(d, 'zone position tags') && visibleEntity({ ...d, side: r.side, entity: r.entity, card: r.card });
    case 'tag': return fields(d, 'tag value') && TAGS.has(d.tag) && number(d.value)
      && r.side !== 'shop' && (r.side === 'local' || r.card === null);
    case 'block_start': case 'block_end': return fields(d, 'block') && ['PLAY', 'POWER', 'TRIGGER', 'ATTACK', 'DEATHS', 'MOVE_MINION', 'OTHER'].includes(d.block)
      && (r.side === 'local' || r.card === null);
    case 'choice': return fields(d, r.v === 1 ? 'choiceId sourceCard options picked resolved' : 'choiceId sourceCard options picked resolved choiceType')
      && r.side === 'local' && (r.v === 1 || CHOICE_TYPES.includes(d.choiceType))
      && int(d.choiceId) && nullable(d.sourceCard, card) && cards(d.options, 64) && d.options.length > 0 && cards(d.picked, 16)
      && typeof d.resolved === 'boolean' && (d.resolved || d.picked.length === 0) && multisetSubset(d.picked, d.options);
    case 'action': return fields(d, 'action cost') && ['play', 'buy', 'sell', 'refresh', 'freeze', 'upgrade', 'move', 'hero_power', 'receive', 'draw', 'discard', 'unknown'].includes(d.action)
      && nullable(d.cost, v => int(v))
      && (['local', 'opponent', 'partner_public'].includes(r.side) || (r.side === 'none' && d.action === 'unknown' && r.entity === 0 && r.card === null && d.cost === null))
      && (r.side === 'local' || r.card === null || ['play', 'hero_power', 'move'].includes(d.action))
      && (!(r.side === 'opponent' && ['draw', 'receive', 'discard'].includes(d.action)) || (r.card === null && d.cost === null && r.target === 0));
    case 'snapshot': return fields(d, 'gold tier cards') && ['local', 'none'].includes(r.side) && nullable(d.gold, v => int(v))
      && nullable(d.tier, v => int(v, 1, 10)) && Array.isArray(d.cards) && d.cards.length <= 256
      && new Set(d.cards.map(x => x?.entity)).size === d.cards.length
      && d.cards.every(x => fields(x, 'entity card side zone position tags') && visibleEntity(x));
    case 'draft_choice': return fields(d, 'pick options picked underground') && r.side === 'local' && int(d.pick, 0, 1000)
      && Array.isArray(d.options) && d.options.length >= 1 && d.options.length <= 64
      && d.options.every(x => fields(x, 'card package') && card(x.card) && cards(x.package, 64))
      && nullable(d.picked, v => int(v, 0, d.options.length - 1)) && nullable(d.underground, v => typeof v === 'boolean');
    case 'gap': return fields(d, 'reason count') && (GAP_REASONS.includes(d.reason) || (r.v >= 2 && d.reason === 'combat_view')) && int(d.count, 1, 1_000_000)
      && r.side === 'none' && r.entity === 0 && r.target === 0;
    case 'bg_combat': return r.v === 3 && r.side === 'none' && r.entity === 0 && r.target === 0 && validCombat(d);
    default: return false;
  }
}

/**
 * One Battlegrounds fight as the app's combat simulator got it at the first attack, with the real result, the odds
 * shown and the fight step by step (docs/source-capture.md, "Battlegrounds fights"). The same allowlist as the app's
 * SourceCaptureCombat.Validate: card ids, fight-scoped references, stats, keyword/tribe/mechanic flags, the numeric tags
 * and the other inputs effects read (enchantment values, deaths before the capture, the lobby's tribes) and the
 * transcript's steps. Never a hand, the opponent's card values or player enchantments, a name, account, rating, path or
 * text.
 */
export const COMBAT = Object.freeze({
  maxFights: 64, maxMinions: 7, maxRemembered: 7, maxEnchantments: 32, maxDeaths: 32, maxExtras: 16, maxFacts: 14, maxRef: 1000,
  maxStat: 1_000_000, maxDataBytes: 56 * 1024,
  // The transcript (the fight step by step): null, or version 1 within these bounds (compact JSON bytes).
  transcriptVersion: 1, maxTranscriptSteps: 512, maxTranscriptBytes: 16 * 1024,
});
const PLAYER_TAGS = new Set(['BACON_BLOODGEMBUFFATKVALUE', 'BACON_BLOODGEMBUFFHEALTHVALUE', 'TAVERN_SPELL_ATTACK_INCREASE',
  'TAVERN_SPELL_HEALTH_INCREASE', 'BACON_OLD_GOD_ATTACK', 'BACON_OLD_GOD_HEALTH', '4914', '4915']);
// Script numbers of the local player's hero powers, trinkets and other player-level cards; Avenge (X) and its progress.
const CARD_TAGS = new Set(['TAG_SCRIPT_DATA_NUM_1', 'TAG_SCRIPT_DATA_NUM_2', 'SCORE_VALUE_1', 'SCORE_VALUE_2']);
const MARKERS = new Set(['DEATHRATTLE', 'TRIGGER_VISUAL', 'AURA', 'AVENGE', 'OVERKILL', 'BACON_RALLY']);
// Taunt, Divine Shield, Poisonous, Venomous, Reborn, Stealth; Windfury travels as `attacks`.
const KEYWORDS = 1 | 2 | 4 | 8 | 16 | 128;
const bool = x => typeof x === 'boolean';
const share = x => typeof x === 'number' && Number.isFinite(x) && x >= 0 && x <= 1;
const value = x => int(x, -COMBAT.maxStat, COMBAT.maxStat);
const MINION = 'entity card golden attack health maxHealth baseHealth rebornAttack keywords attacks baseKeywords tribes mechanics tier script1 script2 creator enchantments enchantmentValues friendlyDeaths remembered unsimulated';
// An enchantment with the numbers it carries; creatorEntity is a reference of the record, or 0.
const enchantment = e => fields(e, 'card script1 script2 attack health creatorEntity') && card(e.card)
  && ['script1', 'script2', 'attack', 'health'].every(k => value(e[k])) && int(e.creatorEntity, 0, COMBAT.maxRef);
const enchantments = (x, max) => Array.isArray(x) && x.length <= max && x.every(enchantment);
function combatMinion(m, onBoard) {
  return fields(m, MINION) && int(m.entity, onBoard ? 1 : 0, COMBAT.maxRef) && nullable(m.card, card) && bool(m.golden)
    && ['attack', 'health', 'maxHealth', 'baseHealth', 'rebornAttack'].every(k => int(m[k], 0, COMBAT.maxStat))
    && int(m.keywords, 0, KEYWORDS) && (m.keywords & ~KEYWORDS) === 0 && int(m.attacks, 0, 8) && int(m.baseKeywords, 0, 255)
    && int(m.tribes, 0, 2047) && int(m.mechanics, 0, 63) && int(m.tier, 0, 10) && value(m.script1) && value(m.script2)
    && nullable(m.creator, card) && cards(m.enchantments, COMBAT.maxEnchantments)
    && enchantments(m.enchantmentValues, COMBAT.maxEnchantments) && int(m.friendlyDeaths, 0, 1000)
    && Array.isArray(m.remembered) && m.remembered.length <= (onBoard ? COMBAT.maxRemembered : 0) && m.remembered.every(x => combatMinion(x, false))
    && bool(m.unsimulated);
}
function combatSide(s, opponent, board) {
  if (!fields(s, 'hero heroPower extras tier health turn tags cardTags playerEnchantments lobbyTribes deaths minions')
    || !nullable(s.hero, card) || !nullable(s.heroPower, card)
    || !cards(s.extras, COMBAT.maxExtras) || new Set(s.extras).size !== s.extras.length
    || !int(s.tier, 0, 10) || !int(s.health, 0, 10_000) || !int(s.turn, 0, 1000)
    || !object(s.tags) || !Object.entries(s.tags).every(([k, v]) => PLAYER_TAGS.has(k) && value(v))
    // The opponent's card values are not shown by the client, so a record never holds them.
    || !object(s.cardTags) || Object.keys(s.cardTags).length > (opponent ? 0 : COMBAT.maxExtras)
    || !Object.entries(s.cardTags).every(([k, v]) => card(k) && object(v) && Object.entries(v).every(([t, n]) => CARD_TAGS.has(t) && value(n)))
    // Nor his player enchantments (whether the client shows them is not established).
    || !enchantments(s.playerEnchantments, opponent ? 0 : COMBAT.maxEnchantments) || !int(s.lobbyTribes, 0, 2047)
    || !Array.isArray(s.deaths) || s.deaths.length > COMBAT.maxDeaths || !s.deaths.every(m => combatMinion(m, false))
    || !Array.isArray(s.minions) || s.minions.length > COMBAT.maxMinions || !s.minions.every(m => combatMinion(m, true))) return false;
  for (const m of s.minions) {
    if (board.has(m.entity)) return false;
    board.add(m.entity);
  }
  return true;
}
// A tag step changes what a replay compares: stats and keywords of a board minion.
const TRANSCRIPT_TAGS = new Set(['ATK', 'HEALTH', 'TAUNT', 'DIVINE_SHIELD', 'POISONOUS', 'VENOMOUS', 'REBORN', 'WINDFURY', 'MEGA_WINDFURY', 'STEALTH']);
const ENDINGS = ['hero', 'restore', 'cleanup', 'end', 'cut'];
const playerCards = s => new Set([...s.extras, ...(s.heroPower === null ? [] : [s.heroPower])]);
/**
 * The fight step by step (SourceCaptureCombat.Transcript.cs is the same): every step names a board minion (one of the
 * capture, or one an earlier step summoned) except a summon (the minion it puts on a board) and a trigger, whose source
 * is a board minion, an enchantment on one (host) or a player-level card of its side; each end closes a trigger; boards
 * name board minions only. No step for a card into the hand or a hand card exists.
 */
export function validTranscript(t, captured, ours, theirs) {
  if (!fields(t, 'version complete ending events boards') || Buffer.byteLength(JSON.stringify(t)) > COMBAT.maxTranscriptBytes
    || t.version !== COMBAT.transcriptVersion || !bool(t.complete) || !ENDINGS.includes(t.ending)
    || !Array.isArray(t.events) || t.events.length > COMBAT.maxTranscriptSteps
    || !Array.isArray(t.boards) || t.boards.length > COMBAT.maxTranscriptSteps + 1) return false;
  const board = new Set(captured);
  const onBoard = x => int(x, 1, COMBAT.maxRef) && board.has(x);
  let open = 0;
  for (const s of t.events) {
    if (!Array.isArray(s)) return false;
    const [kind, entity, a, b, c, d] = s;
    const ok = kind === 'attack' ? s.length === 4 && onBoard(entity) && (a === 0 || onBoard(a)) && int(b, 0, 1)
      : kind === 'damage' ? s.length === 3 && onBoard(entity) && value(a)
      : kind === 'death' ? s.length === 2 && onBoard(entity)
      : kind === 'summon' ? s.length === 6 && int(entity, 1, COMBAT.maxRef) && nullable(a, card) && int(b, 0, 10) && bool(c) && bool(d)
      : kind === 'tag' ? s.length === 4 && onBoard(entity) && TRANSCRIPT_TAGS.has(a) && int(b, 0, COMBAT.maxStat)
      : kind === 'trigger' ? s.length === 5 && int(entity, 1, COMBAT.maxRef) && (a === 0 || onBoard(a)) && nullable(b, card) && bool(c)
        && (board.has(entity) || a !== 0 || (b !== null && (c ? theirs : ours).has(b)))
      : kind === 'end' && s.length === 1 && open > 0;
    if (!ok) return false;
    if (kind === 'summon') board.add(entity);
    if (kind === 'trigger') open++;
    if (kind === 'end') open--;
  }
  let last = 0;
  for (const s of t.boards) {
    if (!Array.isArray(s) || s.length !== 3 || !int(s[0], last, t.events.length)) return false;
    last = s[0];
    const seen = new Set();
    for (const side of [s[1], s[2]]) {
      if (!Array.isArray(side) || side.length > COMBAT.maxMinions) return false;
      for (const m of side) {
        if (!Array.isArray(m) || m.length !== 5 || !onBoard(m[0]) || seen.has(m[0]) || !nullable(m[1], card)
          || !int(m[2], 0, COMBAT.maxStat) || !value(m[3]) || !int(m[4], 0, 255)) return false;
        seen.add(m[0]);
      }
    }
  }
  return true;
}
export function validCombat(d) {
  if (!fields(d, 'fight turn oursFirst firstAttacker damageCap countDamage ours theirs facts result damageTaken damageDealt odds transcript')
    || Buffer.byteLength(JSON.stringify(d)) > COMBAT.maxDataBytes
    || !int(d.fight, 0, COMBAT.maxFights - 1) || !int(d.turn, 0, 1000) || !nullable(d.oursFirst, bool)
    || !int(d.firstAttacker, 0, COMBAT.maxRef) || !int(d.damageCap, 0, 1000) || !bool(d.countDamage)) return false;
  const board = new Set();
  if (!combatSide(d.ours, false, board) || !combatSide(d.theirs, true, board) || (d.firstAttacker !== 0 && !board.has(d.firstAttacker))
    || !Array.isArray(d.facts) || d.facts.length > COMBAT.maxFacts) return false;
  const known = new Set();
  for (const f of d.facts) {
    if (!fields(f, 'entity card markers') || !int(f.entity, 1, COMBAT.maxRef) || !board.has(f.entity) || known.has(f.entity) || !card(f.card)
      || !Array.isArray(f.markers) || f.markers.length > MARKERS.size || !f.markers.every(x => MARKERS.has(x))
      || new Set(f.markers).size !== f.markers.length) return false;
    known.add(f.entity);
  }
  return ['W', 'T', 'L'].includes(d.result) && int(d.damageTaken, 0, 1000) && int(d.damageDealt, 0, 1000)
    && nullable(d.odds, o => fields(o, 'win tie loss lethalTaken lethalDealt unsimulated runs') && share(o.win) && share(o.tie) && share(o.loss)
      && nullable(o.lethalTaken, share) && nullable(o.lethalDealt, share) && int(o.unsimulated, 0, 1000) && int(o.runs, 0, 10_000_000))
    && nullable(d.transcript, t => validTranscript(t, board, playerCards(d.ours), playerCards(d.theirs)));
}
function multisetSubset(picked, options) {
  const remaining = [...options];
  return picked.every(c => { const i = remaining.indexOf(c); if (i < 0) return false; remaining.splice(i, 1); return true; });
}
export const sha = bytes => createHash('sha256').update(bytes).digest('hex');
export function parseJson(text) {
  // JSON.parse silently accepts duplicate object keys. Keeping the original bytes would then archive the
  // shadowed, unvalidated value. Reject duplicates at every depth before accepting any source or envelope.
  const stack = [];
  const tokens = /"(?:[^"\\]|\\.)*"|[{}\[\]:,]|-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?|true|false|null/g;
  for (const token of text.matchAll(tokens)) {
    const t = token[0];
    if (t === '{' || t === '[') {
      if (stack.length >= 16) fail(400, 'json_depth');
      stack.push({ object: t === '{', keys: new Set(), key: t === '{' });
    } else if (t === '}' || t === ']') stack.pop();
    else if (t === ',') { if (stack.at(-1)?.object) stack.at(-1).key = true; }
    else if (t === ':') { if (stack.at(-1)) stack.at(-1).key = false; }
    else if (t[0] === '"' && stack.at(-1)?.object && stack.at(-1).key) {
      const key = JSON.parse(t);
      if (stack.at(-1).keys.has(key)) fail(400, 'duplicate_json_key');
      stack.at(-1).keys.add(key);
    }
  }
  try { return JSON.parse(text); } catch { fail(400, 'invalid_json'); }
}
export function identity(body, capture = true) {
  const hex = x => typeof x === 'string' && HEX32.test(x);
  if (body?.v !== 1 || !hex(body.iid) || !hex(body.generation) || (capture && !hex(body.captureId)))
    fail(400, 'invalid_identity');
  const generation = sha(`${body.iid}:${body.generation}`);
  return { generation, owner: sha(body.iid), key: capture ? sha(`${generation}:${body.captureId}`) : null };
}

/** Output is bounded while inflating, including concatenated gzip members. */
export async function inflate(bytes, max = 4 * MiB) {
  const gunzip = createGunzip();
  const parts = [];
  let length = 0;
  try {
    for await (const part of Readable.from([bytes]).pipe(gunzip)) {
      length += part.length;
      if (length > max) { gunzip.destroy(); fail(413, 'decompressed_limit'); }
      parts.push(part);
    }
  } catch (error) {
    if (error instanceof CaptureError) throw error;
    fail(400, 'invalid_gzip');
  }
  return Buffer.concat(parts, length);
}

/** Each transport chunk contains whole JSONL records, with the original bytes hashed unchanged. */
export function records(bytes) {
  if (!bytes.length || bytes[bytes.length - 1] !== 10 || bytes.includes(13) || (bytes[0] === 239 && bytes[1] === 187 && bytes[2] === 191)) fail(400, 'invalid_jsonl');
  let decoded;
  try { decoded = new TextDecoder('utf-8', { fatal: true }).decode(bytes); } catch { fail(400, 'invalid_utf8'); }
  const result = [];
  for (const line of decoded.slice(0, -1).split('\n')) {
    if (Buffer.byteLength(line) + 1 > 64 * 1024) fail(413, 'line_limit');
    let r;
    r = parseJson(line);
    if (!validRecord(r)) fail(400, 'invalid_record');
    // One source has one wire version; the manifest later pins it (finishStream).
    if (result.length && r.v !== result[0].v) fail(400, 'mixed_source_version');
    result.push(r);
  }
  return result;
}

export function validateManifest(b) {
  identity(b);
  if (Array.isArray(b.chunks) && b.chunks.length > 64) fail(413, 'capture_limit');
  if (!fields(b, 'v iid generation captureId captureKind mode day sourceVersion captureStatus chunks gaps')
    || !['game', 'draft'].includes(b.captureKind) || !MODES.includes(b.mode) || !int(b.day, 0, 100000)
    || ![1, 2, 3].includes(b.sourceVersion) || !['complete', 'incomplete'].includes(b.captureStatus) || !int(b.gaps)
    || !Array.isArray(b.chunks) || !int(b.chunks.length, 1, 64)) fail(400, 'invalid_manifest');
  for (const [i, c] of b.chunks.entries()) {
    if (!fields(c, 'index sha256 uncompressedBytes') || c.index !== i || !HEX64.test(c.sha256 ?? '') || !int(c.uncompressedBytes, 1, 4 * MiB)) fail(400, 'invalid_manifest');
  }
}

export function streamState() { return { version: null, combatView: 0, header: true, seq: 0, turn: 0, first: null, last: null, gaps: 0, ended: false, unsupported: false, blocks: [], choices: new Map(), concealed: new Set(), publicRefs: new Set(), combats: [] }; }
export function addRecords(state, list) {
  for (const r of list) {
    if (r.seq !== state.seq++ || state.ended || (r.kind === 'start' && r.seq !== 0)) fail(400, 'invalid_sequence');
    state.version ??= r.v;
    if (r.v !== state.version) fail(400, 'mixed_source_version');
    // C1, same rule as the decoder: combat_view belongs to the capture header (only start/gap records before it),
    // once, count 1, only in a Battlegrounds/Duos game; finishStream requires it in every v2 BG/Duos game.
    if (r.kind !== 'start' && r.kind !== 'gap') state.header = false;
    if (r.kind === 'gap' && r.data.reason === 'combat_view') {
      const s = state.first?.data;
      if (!state.header || r.data.count !== 1 || ++state.combatView > 1 || s?.captureKind !== 'game' || !['bg', 'duos'].includes(s?.mode)) fail(400, 'invalid_combat_view');
    }
    if (r.turn < state.turn || (r.seq === 0 && r.kind !== 'start')) fail(400, 'invalid_sequence');
    // v3: the fights of a Battlegrounds game, in order, at most one per combat (the pool stores them on commit).
    if (r.kind === 'bg_combat') {
      const s = state.first?.data;
      if (s?.captureKind !== 'game' || !['bg', 'duos'].includes(s?.mode) || state.combats.length >= COMBAT.maxFights
        || (state.combats.length && r.data.fight <= state.combats.at(-1).fight)) fail(400, 'invalid_combat');
      state.combats.push(r.data);
    }
    state.turn = r.turn;
    state.first ??= r;
    state.last = r;
    if (r.kind === 'end') state.ended = true;
    if (r.kind === 'gap') state.gaps += r.data.count;
    if ((r.kind === 'action' && r.data.action === 'unknown') || r.data.block === 'OTHER') state.unsupported = true;
    if (r.kind === 'block_start') {
      if (state.blocks.length >= 64) fail(413, 'block_limit');
      state.blocks.push(r.data.block);
    }
    if (r.kind === 'block_end') {
      if (state.blocks.at(-1) !== r.data.block) {
        if (!state.gaps) fail(400, 'invalid_blocks');
        state.blocks.length = 0; state.unsupported = true;
      } else state.blocks.pop();
    }
    if (r.kind === 'choice') {
      const old = state.choices.get(r.data.choiceId);
      if (old && old.choiceType !== r.data.choiceType) fail(400, 'invalid_choice_progression');
      if (old?.resolved && (JSON.stringify(old.options) !== JSON.stringify(r.data.options) || old.sourceCard !== r.data.sourceCard
        || !r.data.resolved || old.picked.some((x, i) => r.data.picked[i] !== x))) fail(400, 'invalid_choice_progression');
      state.choices.set(r.data.choiceId, r.data);
    }
    const entities = r.kind === 'snapshot' ? r.data.cards : r.kind === 'entity' ? [{ ...r.data, entity: r.entity, side: r.side, card: r.card }] : [];
    for (const e of entities) {
      if (!e.entity) continue;
      if (hidden(e.side, e.zone)) {
        if (state.publicRefs.has(e.entity)) fail(400, 'concealed_reference_reused');
        state.concealed.add(e.entity);
      } else {
        if (state.concealed.has(e.entity)) fail(400, 'concealed_reference_reused');
        state.publicRefs.add(e.entity);
      }
    }
    if (r.target && state.concealed.has(r.target)) fail(400, 'concealed_reference_reused');
    if (r.card !== null && r.entity && state.concealed.has(r.entity)) fail(400, 'concealed_reference_reused');
    if (r.kind === 'tag' && r.side === 'opponent' && r.entity && r.data.tag !== 'ZONE_POSITION' && !state.publicRefs.has(r.entity)) fail(400, 'unobserved_opponent_tag');
  }
}
export function finishStream(state, b) {
  const start = state.first;
  if (start?.kind !== 'start' || ['captureKind', 'mode', 'day'].some(k => start.data[k] !== b[k]) || state.gaps !== b.gaps
    || state.version !== b.sourceVersion) fail(400, 'manifest_source_mismatch');
  if (state.version >= 2 && start.data.captureKind === 'game' && ['bg', 'duos'].includes(start.data.mode) && state.combatView !== 1) fail(400, 'invalid_combat_view');
  const end = state.last;
  if (b.captureStatus === 'complete' && (end?.kind !== 'end' || end.data.status !== 'complete' || state.gaps !== 0 || state.unsupported
    || state.blocks.length || [...state.choices.values()].some(x => !x.resolved))) fail(400, 'incomplete_stream');
  if (b.captureStatus === 'incomplete' && !(end?.kind === 'gap' || (end?.kind === 'end' && end.data.status === 'incomplete'))) fail(400, 'incomplete_stream');
  if (b.captureKind === 'draft' && end?.kind === 'end' && (end.data.result !== null || end.data.placement !== null)) fail(400, 'draft_result');
}
