// Game records (docs/game-stats.md, ADR 0011): every finished game a player agreed to send, all modes, as one
// compact versioned record. What the record may hold is decided here: every field is checked and rebuilt, anything
// unknown is dropped, so a name or an account id the app never sends cannot be stored either.
//
// A record is one row of table `e` (WITHOUT ROWID: the row and the day index are the only writes), with the random id
// of the install that sent it so the player can have all of their records deleted (POST /v1/events/forget).
// Records are kept for KEEP_DAYS; the cron job builds the only published view (/v1/events/summary): games per day,
// the most picked Discover options and the mulligan keep rate per card, each from MIN_CELL games of MIN_PLAYERS
// installs or more. The same build makes the opening-hand pair table (/v1/events/mulligan-pairs, metric
// mulligan.pair_winrate of docs/analytics-metrics.md). Records are pseudonymous personal data (ADR 0011): one install's
// records are never published, and a forget or a clean-up withdraws both published views with the records.

const CARD = /^[A-Za-z0-9_]{3,64}$/;
const TRIBE = /^[A-Z0-9_]{2,24}$/;
const HEX32 = /^[0-9a-f]{32}$/;
const CLASS = /^[A-Z]{4,14}$/;
const DECK = /^[A-Za-z0-9+/=]{8,200}$/;
const ARCHETYPE = /^[\p{L}\p{N}' .&-]{2,48}$/u;
const MODES = new Set(["ranked", "casual", "arena", "brawl", "bg", "duos"]);
const FORMATS = new Set(["standard", "wild", "classic", "twist"]);
const RANKS = new Set(["bronze", "silver", "gold", "platinum", "diamond", "legend"]);
const RESULTS = new Set(["W", "L", "T"]);
const KINDS = new Set(["discover", "adapt", "chooseone", "dredge", "excavate", "trinket", "quest", "reward", "hero", "choice"]);

const DAY = 86_400_000;
const KEEP_DAYS = 180;
const WINDOW_DAYS = 30;
const MAX_BATCH = 20;
// One record as stored, after validation. A long Constructed game is about 4 KB.
const MAX_RECORD = 24 * 1024;
// Records stored per day: a record writes its row, its day index and its install index, and the clean-up deletes as
// many, so 8,000 records stay near 48,000 rows written a day (D1's free plan allows 100,000 for the whole database).
const EVENTS_DAILY = 8_000;
// A published cell needs this many games, and MIN_PLAYERS installs among them (one player's picks are not published).
const MIN_CELL = 10;
const MIN_PLAYERS = 5;
// Ratings at or above this are stored as this ("10000+"): the few players up there are on Blizzard's public leaderboard.
const MAX_MMR = 10_000;
const TOP = 200;
// The opening-hand pair table (rules mull-pairs-1): a pair is published from this many games with both cards kept,
// of MIN_PLAYERS installs or more; at most MAX_PAIRS pairs a format and class, most played first.
const PAIR_RULES = "mull-pairs-1";
const MIN_PAIR_GAMES = 200;
const MAX_PAIRS = 200;
const PAIR_FORMATS = new Set(["standard", "wild"]);
// Every published view built from the records: withdrawn together whenever records are deleted.
const WITHDRAW = "DELETE FROM snap WHERE mode IN ('events', 'mull-pairs')";
// The deletion fence. D1 gives a build no transaction across its reads, so a forget can land between the moment a
// build read the records and the moment it publishes. Row 'events-rev' of `snap` counts deletions (in last_row): every
// batch that deletes records raises it, a build notes it before its first read, and PUBLISH writes only while it is
// still the same. A build that lost the race publishes nothing and counts again from the records that are left.
const REVISION = "SELECT last_row FROM snap WHERE mode = 'events-rev'";
const DELETED = `INSERT INTO snap (mode, last_row, built, body) VALUES ('events-rev', 1, ?1, '')
  ON CONFLICT(mode) DO UPDATE SET last_row = last_row + 1, built = ?1`;
const PUBLISH = `INSERT INTO snap (mode, last_row, built, body)
  SELECT ?4, ?1, ?2, ?3 WHERE COALESCE((${REVISION}), 0) = ?5
  ON CONFLICT(mode) DO UPDATE SET last_row = excluded.last_row, built = excluded.built, body = excluded.body`;
const BUILD_ATTEMPTS = 3;
const changes = (result) => result?.meta?.changes ?? result?.changes ?? 0;

const MAX = { plays: 400, oplays: 200, choices: 120, draws: 300, exc: 40, mull: 6, turns: 200, buys: 300, sells: 300 };

const int = (x, min, max) => Number.isInteger(x) && x >= min && x <= max;
const card = (x) => typeof x === "string" && CARD.test(x);
const cards = (list, max) => Array.isArray(list) && list.length <= max && list.every(card);
const turn = (x) => int(x, 0, MAX.turns);

/** Keeps only the rows <paramref name="ok"/> accepts, at most <paramref name="max"/>, each rebuilt by <paramref name="map"/>. */
function rows(list, max, ok, map = (r) => r) {
  if (!Array.isArray(list)) return undefined;
  const kept = [];
  for (const r of list) {
    if (kept.length >= max) break;
    if (Array.isArray(r) && ok(r)) kept.push(map(r));
  }
  return kept;
}

const choiceRow = (r) => turn(r[0]) && typeof r[1] === "string" && (r[2] === null || card(r[2]))
  && cards(r[3], 8) && r[3].length >= 2 && cards(r[4], 4) && r[4].length >= 1 && r[4].every((c) => r[3].includes(c));
const choiceOf = (r) => [r[0], KINDS.has(r[1]) ? r[1] : "choice", r[2], [...new Set(r[3])], [...new Set(r[4])]];
const turnCard = (list, max) => rows(list, max, (r) => turn(r[0]) && card(r[1]), (r) => [r[0], r[1]]);

function mulligan(m) {
  if (!m || typeof m !== "object" || Array.isArray(m)) return undefined;
  const parts = ["o", "k", "r", "n"].map((key) => m[key] ?? []);
  if (!parts.every((list) => cards(list, MAX.mull)) || parts[0].length === 0) return undefined;
  const [o, k, r, n] = parts;
  return { o, k, r, n };
}

function battlegrounds(b) {
  if (!b || typeof b !== "object" || Array.isArray(b)) return null;
  if (!int(b.placement, 1, 8)) return null;
  const out = { placement: b.placement };
  if (card(b.picked)) out.picked = b.picked;
  if (cards(b.offered, 4)) out.offered = [...new Set(b.offered)];
  if (int(b.mmr, 0, 30_000) && b.mmr % 500 === 0) out.mmr = Math.min(b.mmr, MAX_MMR);
  if (card(b.anomaly)) out.anomaly = b.anomaly;
  if (Array.isArray(b.tribes) && b.tribes.length <= 8 && b.tribes.every((t) => typeof t === "string" && TRIBE.test(t))) out.tribes = [...new Set(b.tribes)];
  if (int(b.tier, 1, 7)) out.tier = b.tier;
  if (int(b.triples, 0, 60)) out.triples = b.triples;
  const tierups = rows(b.tierups, 8, (r) => turn(r[0]) && int(r[1], 2, 7), (r) => [r[0], r[1]]);
  if (tierups) out.tierups = tierups;
  if (b.trinkets && typeof b.trinkets === "object" && cards(b.trinkets.o ?? [], 12) && cards(b.trinkets.p ?? [], 4)) {
    out.trinkets = { o: [...new Set(b.trinkets.o ?? [])], p: [...new Set(b.trinkets.p ?? [])] };
  }
  if (cards(b.quests, 8)) out.quests = b.quests;
  if (Array.isArray(b.board) && b.board.length <= 7) {
    const board = b.board.filter((m) => m && card(m.c) && int(m.a, 0, 100_000) && int(m.h, 0, 100_000))
      .map((m) => ({ c: m.c, g: m.g ? 1 : 0, a: m.a, h: m.h }));
    if (board.length === b.board.length) out.board = board;
  }
  for (const key of ["buys", "sells"]) {
    const list = turnCard(b[key], MAX[key]);
    if (list) out[key] = list;
  }
  const choices = rows(b.choices, MAX.choices, choiceRow, choiceOf);
  if (choices) out.choices = choices;
  const plays = turnCard(b.plays, MAX.plays);
  if (plays) out.plays = plays;
  const combats = rows(b.combats, 60, (r) => turn(r[0]) && ["W", "T", "L"].includes(r[1]) && int(r[2], 0, 100) && int(r[3], 0, 100),
    (r) => [r[0], r[1], r[2], r[3]]);
  if (combats) out.combats = combats;
  return out;
}

/**
 * The record as stored ({id, day, mode, fmt, body}), or null when a core field is missing or out of range, or the
 * record is too big. Bad optional parts are dropped; unknown fields never reach the database.
 */
export function validateEvent(g, today) {
  if (!g || typeof g !== "object" || Array.isArray(g)) return null;
  if (typeof g.id !== "string" || !HEX32.test(g.id) || !MODES.has(g.mode) || !RESULTS.has(g.res)) return null;
  const day = g.day ?? today;
  if (!int(day, today - 400, today + 1)) return null;
  if (g.fmt !== undefined && g.fmt !== null && !FORMATS.has(g.fmt)) return null;
  if (g.turns !== undefined && !int(g.turns, 0, MAX.turns)) return null;
  if (g.secs !== undefined && !int(g.secs, 0, 6 * 3600)) return null;

  const bgMode = g.mode === "bg" || g.mode === "duos";
  const out = { id: g.id, day, mode: g.mode, res: g.res };
  if (g.fmt) out.fmt = g.fmt;
  if (g.turns !== undefined) out.turns = g.turns;
  if (g.secs !== undefined) out.secs = g.secs;
  if (typeof g.app === "string" && /^\d{1,3}\.\d{1,3}\.\d{1,4}$/.test(g.app)) out.app = g.app;

  if (bgMode) {
    const b = battlegrounds(g.bg);
    if (!b) return null;
    out.bg = b;
  } else {
    if (RANKS.has(g.rank)) out.rank = g.rank;
    for (const key of ["cls", "ocls"]) if (typeof g[key] === "string" && CLASS.test(g[key])) out[key] = g[key];
    if (card(g.hero)) out.hero = g.hero;
    if (typeof g.deck === "string" && DECK.test(g.deck)) out.deck = g.deck;
    if (typeof g.oarch === "string" && ARCHETYPE.test(g.oarch)) out.oarch = g.oarch;
    if (typeof g.first === "boolean") out.first = g.first;
    const mull = mulligan(g.mull);
    if (mull) out.mull = mull;
    const plays = rows(g.plays, MAX.plays, (r) => turn(r[0]) && card(r[1]) && int(r[2], 0, 30) && int(r[3], 0, 1), (r) => [r[0], r[1], r[2], r[3]]);
    if (plays) out.plays = plays;
    const oplays = turnCard(g.oplays, MAX.oplays);
    if (oplays) out.oplays = oplays;
    const choices = rows(g.choices, MAX.choices, choiceRow, choiceOf);
    if (choices) out.choices = choices;
    const draws = rows(g.draws, MAX.draws, (r) => turn(r[0]) && card(r[1]) && int(r[2], 0, 1), (r) => [r[0], r[1], r[2]]);
    if (draws) out.draws = draws;
    const exc = turnCard(g.exc, MAX.exc);
    if (exc) out.exc = exc;
  }

  const body = JSON.stringify(out);
  if (body.length > MAX_RECORD) return null;
  return { id: g.id, day, mode: g.mode, fmt: out.fmt ?? null, body };
}

const INSERT = `INSERT OR IGNORE INTO e (id, iid, day, mode, fmt, v, body)
  SELECT json_extract(value, '$.id'), ?2, json_extract(value, '$.day'), json_extract(value, '$.mode'), json_extract(value, '$.fmt'), 1,
         json_extract(value, '$.body')
  FROM json_each(?1)`;

/** POST /v1/events: {v:1, iid, games:[...]}, 1 to 20 records. */
export async function submitEvents(body, env, reply) {
  const today = Math.floor(Date.now() / DAY);
  if (body?.v !== 1 || typeof body.iid !== "string" || !HEX32.test(body.iid)) return reply({ error: "send {v:1, iid, games}" }, 400);
  const batch = Array.isArray(body.games) ? body.games : [];
  if (batch.length === 0 || batch.length > MAX_BATCH) return reply({ error: "send 1 to 20 games" }, 400);
  const records = batch.map((g) => validateEvent(g, today)).filter(Boolean);
  if (records.length === 0) return reply({ error: "invalid game" }, 400);

  const limit = Number(env.EVENTS_DAILY ?? EVENTS_DAILY);
  const quota = await env.DB.prepare(`INSERT INTO equota (day, n) SELECT ?1, ?2 WHERE ?2 <= ?3
    ON CONFLICT(day) DO UPDATE SET n = n + ?2 WHERE n + ?2 <= ?3`).bind(today, records.length, limit).run();
  if ((quota?.meta?.changes ?? quota?.changes ?? 0) !== 1) return reply({ error: "full for today, try tomorrow" }, 503, { "retry-after": "3600" });
  await env.DB.prepare(INSERT).bind(JSON.stringify(records), body.iid).run();
  return reply({ ok: true, stored: records.length });
}

/** POST /v1/events/forget: {iid}. Deletes every record that install sent. Only the app knows its install id. */
export async function forgetEvents(body, env, reply) {
  if (typeof body?.iid !== "string" || !HEX32.test(body.iid)) return reply({ error: "send {iid}" }, 400);
  // One batch (a transaction in D1 and in the Node service): the records and every view built from them go together,
  // and the fence rises with them, so a build that read these records before the delete cannot publish them after it.
  const [result] = await env.DB.batch([
    env.DB.prepare("DELETE FROM e WHERE iid = ?1").bind(body.iid),
    env.DB.prepare(WITHDRAW),
    env.DB.prepare(DELETED).bind(Date.now()),
  ]);
  return reply({ ok: true, deleted: changes(result) });
}

/** Deletes records older than KEEP_DAYS and the events' old daily counters. */
export async function pruneEvents(env, today = Math.floor(Date.now() / DAY)) {
  await env.DB.batch([
    env.DB.prepare("DELETE FROM e WHERE day < ?1").bind(today - KEEP_DAYS),
    env.DB.prepare("DELETE FROM equota WHERE day < ?1").bind(today - 7),
    env.DB.prepare(WITHDRAW),
    env.DB.prepare(DELETED).bind(Date.now()),
  ]);
}

const round = (x) => Math.round(x * 1000) / 1000;

// Opening-hand pairs. The sample: Ranked Standard/Wild records of the last WINDOW_DAYS up to today (a record may be
// dated tomorrow by a clock that runs ahead, validateEvent allows one day: it waits until that day comes) with a class, a win or a loss and a mulligan. Every
// pair of different cards offered together in one opening hand falls into one of four cells by what was kept (both,
// only a, only b, neither). The cells are disjoint and every game of a pair comes from a deck holding both cards, so
// the comparison cells are the pair's own base numbers, not the class's: two cards of one strong archetype do not
// look like a synergy (or its opposite) merely because their deck wins more than the class does.
const PAIR_CELLS = [["both", 3], ["onlyA", 2], ["onlyB", 1], ["none", 0]];
const PAIRS_SQL = `
  WITH r AS (
    SELECT id, iid, fmt, json_extract(body, '$.cls') AS cls, json_extract(body, '$.res') = 'W' AS won, body
    FROM e
    WHERE day >= ?1 AND day <= ?4 AND mode = 'ranked' AND fmt IN ('standard', 'wild')
      AND json_extract(body, '$.res') IN ('W', 'L') AND json_extract(body, '$.cls') IS NOT NULL
  ), o AS (
    SELECT DISTINCT r.id, r.iid, r.fmt, r.cls, r.won, x.value AS card,
           EXISTS (SELECT 1 FROM json_each(r.body, '$.mull.k') k WHERE k.value = x.value) AS kept
    FROM r, json_each(r.body, '$.mull.o') x
  ), p AS (
    SELECT a.iid, a.fmt, a.cls, a.won, a.card AS a, b.card AS b, a.kept * 2 + b.kept AS cell
    FROM o a JOIN o b ON b.id = a.id AND a.card < b.card
  )
  SELECT fmt, cls, a, b,
    ${PAIR_CELLS.map(([, n]) => `SUM(cell = ${n}) AS g${n}, SUM(cell = ${n} AND won) AS w${n}, COUNT(DISTINCT CASE WHEN cell = ${n} THEN iid END) AS i${n}`).join(", ")}
  FROM p GROUP BY fmt, cls, a, b
  HAVING g3 >= ?2 AND i3 >= ?3
  ORDER BY fmt, cls, g3 DESC, a, b`;

/**
 * The pair table as stored: {window, updated, groups: {"<fmt>|<CLASS>": [pair, ...]}}. A pair is
 * {a, b, installs, both: [games, wins], onlyA, onlyB, none}; a comparison cell (only a kept, only b kept, neither
 * kept) is null under MIN_PLAYERS installs or MIN_CELL games. No install id, record id or deck.
 */
async function buildPairs(env, since, today) {
  const rows = (await env.DB.prepare(PAIRS_SQL).bind(since, MIN_PAIR_GAMES, MIN_PLAYERS, today).all()).results;
  const groups = {};
  for (const r of rows) {
    const list = groups[`${r.fmt}|${r.cls}`] ??= [];
    if (list.length >= MAX_PAIRS) continue;
    const pair = { a: r.a, b: r.b, installs: r.i3 };
    for (const [name, n] of PAIR_CELLS) {
      pair[name] = r[`i${n}`] >= MIN_PLAYERS && r[`g${n}`] >= MIN_CELL ? [r[`g${n}`], r[`w${n}`]] : null;
    }
    list.push(pair);
  }
  return { window: WINDOW_DAYS, updated: Date.now(), groups };
}

/**
 * Builds the summary and the opening-hand pair table (when records came in since the last build, or with force) and
 * keeps both in `snap`, written together and only behind the deletion fence. Returns the summary's body; throws when
 * deletions kept overtaking the build (the caller answers 503 and nothing stale is published).
 */
export async function rebuildEvents(env, force) {
  for (let attempt = 0; attempt < BUILD_ATTEMPTS; attempt++) {
    const built = await buildOnce(env, force);
    if (built !== undefined) return built;
  }
  throw new Error("events_build_overtaken");
}

/** One build: the summary's body, null when nothing needed building, undefined when a deletion overtook it. */
async function buildOnce(env, force) {
  // Before any read of the records: whatever is deleted from here on raises the fence above this number.
  const revision = (await env.DB.prepare(REVISION).first())?.last_row ?? 0;
  const today = Math.floor(Date.now() / DAY);
  const since = today - WINDOW_DAYS;
  const last = (await env.DB.prepare("SELECT COUNT(*) AS n FROM e").first())?.n ?? 0;
  if (!force) {
    const snap = await env.DB.prepare("SELECT last_row, built FROM snap WHERE mode = 'events'").first();
    if (snap && snap.last_row === last && Math.floor(snap.built / DAY) === Math.floor(Date.now() / DAY)) return null;
  }

  const days = (await env.DB.prepare(
    `SELECT day, mode, fmt, COUNT(*) AS games FROM e WHERE day >= ?1 GROUP BY day, mode, fmt
     HAVING COUNT(DISTINCT iid) >= ?2 AND COUNT(*) >= ?3 ORDER BY day, mode, fmt`).bind(since, MIN_PLAYERS, MIN_CELL).all()).results;
  // An option offered by a Discover-style card, and whether it was the one taken.
  const discover = (await env.DB.prepare(
    `SELECT json_extract(c.value, '$[1]') AS kind, o.value AS card, COUNT(*) AS offered,
            SUM(EXISTS (SELECT 1 FROM json_each(c.value, '$[4]') p WHERE p.value = o.value)) AS picked
     FROM e, json_each(e.body, '$.choices') c, json_each(c.value, '$[3]') o
     WHERE e.day >= ?1 AND json_extract(c.value, '$[1]') IN ('discover', 'adapt', 'chooseone', 'dredge')
     GROUP BY kind, card HAVING COUNT(DISTINCT e.id) >= ?2 AND COUNT(DISTINCT e.iid) >= ?4 ORDER BY picked DESC, offered DESC LIMIT ?3`)
    .bind(since, MIN_CELL, TOP, MIN_PLAYERS).all()).results;
  const mulligan = (await env.DB.prepare(
    `SELECT o.value AS card, COUNT(*) AS offered,
            SUM(EXISTS (SELECT 1 FROM json_each(e.body, '$.mull.k') k WHERE k.value = o.value)) AS kept
     FROM e, json_each(e.body, '$.mull.o') o
     WHERE e.day >= ?1 GROUP BY card HAVING COUNT(DISTINCT e.id) >= ?2 AND COUNT(DISTINCT e.iid) >= ?4 ORDER BY offered DESC LIMIT ?3`)
    .bind(since, MIN_CELL, TOP, MIN_PLAYERS).all()).results;

  const body = JSON.stringify({
    window: WINDOW_DAYS,
    updated: Date.now(),
    games: (await env.DB.prepare("SELECT COUNT(*) AS n FROM e WHERE day >= ?1").bind(since).first())?.n ?? 0,
    days: days.map((r) => ({ day: r.day, mode: r.mode, fmt: r.fmt ?? null, games: r.games })),
    discover: discover.map((r) => ({ kind: r.kind, card: r.card, offered: r.offered, picked: r.picked, rate: round(r.picked / r.offered) })),
    mulligan: mulligan.map((r) => ({ card: r.card, offered: r.offered, kept: r.kept, rate: round(r.kept / r.offered) })),
  });
  const pairs = JSON.stringify(await buildPairs(env, since, today));
  const built = Date.now();
  // One batch: both views are written, or (a deletion since `revision`) neither.
  const written = await env.DB.batch([
    env.DB.prepare(PUBLISH).bind(last, built, body, "events", revision),
    env.DB.prepare(PUBLISH).bind(last, built, pairs, "mull-pairs", revision),
  ]);
  return written.every((result) => changes(result) === 1) ? body : undefined;
}

/** GET /v1/events/summary: a deletion-sensitive published view, never served from an edge cache. */
export async function eventsSummary(request, env, ctx, reply) {
  // Deletion-sensitive aggregates must never survive a forget in an unpurgeable public cache.
  const row = await env.DB.prepare("SELECT body, built FROM snap WHERE mode = 'events'").first();
  if (row && Math.floor(row.built / DAY) === Math.floor(Date.now() / DAY)) return reply(row.body, 200, { "cache-control": "no-store" });
  try {
    return reply(await rebuildEvents(env, true), 200, { "cache-control": "no-store" });
  } catch {
    return reply({ error: "rebuilding, try again" }, 503, { "cache-control": "no-store", "retry-after": "30" });
  }
}

/**
 * GET /v1/events/mulligan-pairs?fmt=standard|wild&cls=<CLASS>: the opening-hand pairs of one format and class (metric
 * mulligan.pair_winrate). Deletion-sensitive like the summary: read from `snap`, rebuilt when missing, never cached.
 */
export async function mulliganPairs(url, env, reply) {
  const fmt = url.searchParams.get("fmt");
  const cls = url.searchParams.get("cls");
  if (!PAIR_FORMATS.has(fmt) || typeof cls !== "string" || !CLASS.test(cls)) {
    return reply({ error: "send fmt=standard|wild and cls=<CLASS>" }, 400, { "cache-control": "no-store" });
  }
  const read = () => env.DB.prepare("SELECT body, built FROM snap WHERE mode = 'mull-pairs'").first();
  const unavailable = () => reply({ error: "rebuilding, try again" }, 503, { "cache-control": "no-store", "retry-after": "30" });
  let row = await read();
  if (!row || Math.floor(row.built / DAY) !== Math.floor(Date.now() / DAY)) {
    try {
      await rebuildEvents(env, true);
    } catch {
      return unavailable();
    }
    row = await read();
    // A forget between the build and this read took the table away again: nothing published, nothing answered.
    if (!row) return unavailable();
  }
  const table = JSON.parse(row.body);
  return reply({
    v: 1, metric: "mulligan.pair_winrate", rules: PAIR_RULES, fmt, cls,
    window: table.window, updated: table.updated, minGames: MIN_PAIR_GAMES, minInstalls: MIN_PLAYERS,
    pairs: table.groups[`${fmt}|${cls}`] ?? [],
  }, 200, { "cache-control": "no-store" });
}
