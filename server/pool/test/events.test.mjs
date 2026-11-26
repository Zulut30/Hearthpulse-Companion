import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { DatabaseSync } from "node:sqlite";
import test from "node:test";
import worker from "../src/index.js";
import { pruneEvents, rebuildEvents, validateEvent } from "../src/events.js";

const TODAY = Math.floor(Date.now() / 86_400_000);
const IID = "1".repeat(32);

const ranked = (overrides = {}) => ({
  id: "a".repeat(32), day: TODAY, mode: "ranked", fmt: "standard", rank: "diamond", cls: "MAGE", hero: "HERO_08",
  deck: "AAECAf0EBuwH", ocls: "WARRIOR", oarch: "Control Warrior", res: "W", turns: 9, secs: 540, first: true,
  mull: { o: ["CARD_A", "CARD_B", "CARD_C"], k: ["CARD_A"], r: ["CARD_B", "CARD_C"], n: ["CARD_D", "CARD_E"] },
  plays: [[1, "CARD_A", 1, 0], [3, "CARD_X", 2, 1]],
  oplays: [[2, "CARD_O"]],
  choices: [[3, "discover", "CARD_S", ["CARD_X", "CARD_Y", "CARD_Z"], ["CARD_X"]]],
  draws: [[1, "CARD_F", 0], [3, "CARD_X", 1]],
  exc: [[4, "WW_001t"]],
  ...overrides,
});

const bg = (overrides = {}) => ({
  id: "b".repeat(32), day: TODAY, mode: "bg", res: "W", turns: 12, secs: 1300,
  bg: {
    offered: ["BG_HERO_1", "BG_HERO_2"], picked: "BG_HERO_1", placement: 2, mmr: 6500, anomaly: "BG_ANOMALY_1",
    tribes: ["BEAST", "DRAGON"], tierups: [[3, 2], [5, 3]], trinkets: { o: ["T_A", "T_B"], p: ["T_A"] },
    board: [{ c: "MINION_A", g: 1, a: 10, h: 12 }], buys: [[1, "MINION_A"]], sells: [[2, "MINION_B"]],
    choices: [[4, "discover", "SRC", ["A_1", "A_2"], ["A_1"]]], quests: ["QUEST_A"],
  },
  ...overrides,
});

function sqlite() {
  const db = new DatabaseSync(":memory:");
  db.exec(readFileSync(new URL("../schema.sql", import.meta.url), "utf8"));
  const statement = (sql, args = []) => ({
    bind: (...values) => statement(sql, values),
    first: async () => db.prepare(sql).get(...args) ?? null,
    all: async () => ({ results: db.prepare(sql).all(...args) }),
    run: async () => db.prepare(sql).run(...args),
  });
  return { db, DB: { prepare: (sql) => statement(sql), batch: async (list) => Promise.all(list.map((s) => s.run())) } };
}

const post = (path, body, env) => worker.fetch(new Request(`https://pool.test${path}`, {
  method: "POST", headers: { "content-type": "application/json" },
  body: typeof body === "string" ? body : JSON.stringify(body),
}), env, {});

test("a Constructed record keeps every known field and nothing else", () => {
  const row = validateEvent({ ...ranked(), name: "Player#1234", battletag: "x", accountId: 5 }, TODAY);
  assert.equal(row.id, "a".repeat(32));
  assert.equal(row.mode, "ranked");
  assert.equal(row.fmt, "standard");
  const body = JSON.parse(row.body);
  assert.equal(body.name, undefined);
  assert.equal(body.battletag, undefined);
  assert.equal(body.accountId, undefined);
  assert.deepEqual(body.mull, ranked().mull);
  assert.deepEqual(body.plays, ranked().plays);
  assert.deepEqual(body.choices, ranked().choices);
  assert.deepEqual(body.exc, ranked().exc);
  assert.equal(body.oarch, "Control Warrior");
});

test("a Battlegrounds record keeps its lobby, buys, sells, picks and placement", () => {
  const row = validateEvent(bg(), TODAY);
  const body = JSON.parse(row.body);
  assert.equal(row.mode, "bg");
  assert.equal(body.bg.placement, 2);
  assert.equal(body.bg.mmr, 6500);
  assert.deepEqual(body.bg.buys, [[1, "MINION_A"]]);
  assert.deepEqual(body.bg.sells, [[2, "MINION_B"]]);
  assert.deepEqual(body.bg.trinkets, { o: ["T_A", "T_B"], p: ["T_A"] });
  assert.deepEqual(body.bg.quests, ["QUEST_A"]);
  assert.equal(JSON.parse(validateEvent(bg({ bg: { placement: 1, mmr: 14500 } }), TODAY).body).bg.mmr, 10_000, "10000+");
});

test("a record with a bad core field is rejected", () => {
  for (const overrides of [
    { id: "short" }, { mode: "mercenaries" }, { mode: "friendly" }, { res: "maybe" }, { day: TODAY - 500 }, { day: TODAY + 3 },
    { fmt: "classicx" }, { turns: -1 }, { secs: 10 ** 8 },
  ]) assert.equal(validateEvent(ranked(overrides), TODAY), null, JSON.stringify(overrides));
  assert.equal(validateEvent(bg({ bg: { placement: 9 } }), TODAY), null);
  assert.equal(validateEvent("junk", TODAY), null);
  assert.equal(validateEvent([], TODAY), null);
});

test("bad optional parts are dropped, not the record", () => {
  const body = JSON.parse(validateEvent(ranked({
    rank: "legend-7", cls: "mage<script>", deck: "not a deck code!", oarch: "<b>x</b>", first: "yes",
    mull: { o: ["CARD_A"], k: ["NOT A CARD"] },
    plays: [[1, "CARD_A", 1, 0], [2, "bad card"], "junk", [500, "CARD_B", 1, 0]],
    choices: [[3, "discover", "CARD_S", ["CARD_X"], ["CARD_Q"]], [3, "spy", "CARD_S", ["A_1", "A_2"], ["A_1"]]],
  }), TODAY).body);
  assert.equal(body.rank, undefined);
  assert.equal(body.cls, undefined);
  assert.equal(body.deck, undefined);
  assert.equal(body.oarch, undefined);
  assert.equal(body.first, undefined);
  assert.equal(body.mull, undefined);
  assert.deepEqual(body.plays, [[1, "CARD_A", 1, 0]]);
  assert.deepEqual(body.choices, [[3, "choice", "CARD_S", ["A_1", "A_2"], ["A_1"]]]);
});

test("lists are capped", () => {
  const plays = Array.from({ length: 900 }, (_, i) => [1 + (i % 40), "CARD_A", 1, 0]);
  const body = JSON.parse(validateEvent(ranked({ plays }), TODAY).body);
  assert.equal(body.plays.length, 400);
});

test("POST /v1/events stores a batch once, and a second send is not counted twice", async () => {
  const env = sqlite();
  const first = await post("/v1/events", { v: 1, iid: IID, games: [ranked(), bg()] }, env);
  assert.equal(first.status, 200);
  assert.deepEqual(await first.json(), { ok: true, stored: 2 });
  await post("/v1/events", { v: 1, iid: IID, games: [ranked()] }, env);
  assert.equal(env.db.prepare("SELECT COUNT(*) AS n FROM e").get().n, 2);
  assert.equal(env.db.prepare("SELECT iid FROM e LIMIT 1").get().iid, IID);
});

test("POST /v1/events refuses a bad envelope", async () => {
  const env = sqlite();
  for (const body of [
    "not json", { v: 2, iid: IID, games: [ranked()] }, { v: 1, iid: "short", games: [ranked()] },
    { v: 1, iid: IID, games: [] }, { v: 1, iid: IID, games: Array.from({ length: 21 }, () => ranked()) },
    { v: 1, iid: IID, games: [{ id: "junk" }] },
  ]) assert.equal((await post("/v1/events", body, env)).status, 400, JSON.stringify(body).slice(0, 60));
});

test("POST /v1/events refuses a body over the size limit", async () => {
  const env = sqlite();
  const big = JSON.stringify({ v: 1, iid: IID, games: [ranked({ pad: "x".repeat(600 * 1024) })] });
  assert.equal((await post("/v1/events", big, env)).status, 413);
});

test("a record over the per-game size limit is dropped", () => {
  const plays = Array.from({ length: 400 }, (_, i) => [1 + (i % 40), "CARD_" + "A".repeat(55) + i, 10, 1]);
  const choices = Array.from({ length: 150 }, () => [3, "discover", "C".repeat(60), ["A".repeat(60), "B".repeat(60), "D".repeat(60)], ["A".repeat(60)]]);
  assert.equal(validateEvent(ranked({ plays, choices, draws: plays.map((p) => [p[0], p[1], 1]) }), TODAY), null);
});

test("the daily cap answers 503 past it", async () => {
  const env = { ...sqlite(), EVENTS_DAILY: "1" };
  assert.equal((await post("/v1/events", { v: 1, iid: IID, games: [ranked()] }, env)).status, 200);
  assert.equal((await post("/v1/events", { v: 1, iid: IID, games: [bg()] }, env)).status, 503);
});

test("POST /v1/events/forget deletes every record of the install and nobody else's", async () => {
  const env = sqlite();
  await post("/v1/events", { v: 1, iid: IID, games: [ranked()] }, env);
  await post("/v1/events", { v: 1, iid: "2".repeat(32), games: [bg()] }, env);
  const response = await post("/v1/events/forget", { iid: IID }, env);
  assert.equal(response.status, 200);
  assert.deepEqual(await response.json(), { ok: true, deleted: 1 });
  assert.equal(env.db.prepare("SELECT COUNT(*) AS n FROM e").get().n, 1);
  assert.equal((await post("/v1/events/forget", { iid: "nope" }, env)).status, 400);
});

test("the summary counts games per day and builds Discover and mulligan totals from 10 games", async () => {
  const env = sqlite();
  const games = Array.from({ length: 12 }, (_, i) => ranked({
    id: i.toString(16).padStart(32, "0"),
    mull: { o: ["CARD_A", "CARD_B"], k: i % 4 === 0 ? ["CARD_B"] : ["CARD_A", "CARD_B"], r: [], n: [] },
    choices: [[3, "discover", "CARD_S", ["CARD_X", "CARD_Y", "CARD_Z"], [i < 9 ? "CARD_X" : "CARD_Y"]]],
  }));
  // Twelve games from six installs, two each; the Battlegrounds game is one install's alone.
  for (let i = 0; i < 12; i += 2) await post("/v1/events", { v: 1, iid: (i + 1).toString(16).padStart(32, "0"), games: games.slice(i, i + 2) }, env);
  await post("/v1/events", { v: 1, iid: IID, games: [bg()] }, env);
  const summary = JSON.parse(await rebuildEvents(env, true));
  assert.deepEqual(summary.days, [{ day: TODAY, mode: "ranked", fmt: "standard", games: 12 }], "a day cell needs five installs");
  const x = summary.discover.find((r) => r.card === "CARD_X");
  assert.deepEqual(x, { kind: "discover", card: "CARD_X", offered: 12, picked: 9, rate: 0.75 });
  assert.equal(summary.discover[0].card, "CARD_X");
  const a = summary.mulligan.find((r) => r.card === "CARD_A");
  assert.deepEqual(a, { card: "CARD_A", offered: 12, kept: 9, rate: 0.75 });
  assert.ok(summary.mulligan.find((r) => r.card === "CARD_B").rate === 1);

  const read = await worker.fetch(new Request("https://pool.test/v1/events/summary"), env, { waitUntil() {} });
  assert.equal(read.status, 200);
  assert.equal((await read.json()).games, 13);
});

test("the summary leaves out cards seen in fewer than 10 games, or from fewer than 5 installs", async () => {
  const env = sqlite();
  await post("/v1/events", { v: 1, iid: IID, games: [ranked()] }, env);
  const one = Array.from({ length: 12 }, (_, i) => ranked({ id: (100 + i).toString(16).padStart(32, "0") }));
  await post("/v1/events", { v: 1, iid: IID, games: one.slice(0, 12) }, env);
  const summary = JSON.parse(await rebuildEvents(env, true));
  assert.deepEqual(summary.discover, []);
  assert.deepEqual(summary.mulligan, []);
});

test("summary cells require ten distinct games even when five installs repeat the same choice", async () => {
  for (const count of [5, 9, 10]) {
    const env = sqlite();
    for (let i = 0; i < count; i++) {
      const game = ranked({
        id: (1000 + i).toString(16).padStart(32, "0"),
        choices: [[3, "discover", "SRC", ["CARD_X", "CARD_Y"], ["CARD_X"]],
                  [4, "discover", "SRC", ["CARD_X", "CARD_Y"], ["CARD_X"]]],
      });
      assert.equal((await post("/v1/events", { v: 1, iid: ((i % 5) + 1).toString(16).padStart(32, "0"), games: [game] }, env)).status, 200);
    }
    const summary = JSON.parse(await rebuildEvents(env, true));
    if (count < 10) {
      assert.deepEqual(summary.days, [], `${count} games cannot publish a day`);
      assert.deepEqual(summary.discover, [], `${count * 2} offers are only ${count} games`);
      assert.deepEqual(summary.mulligan, []);
    } else {
      assert.equal(summary.days[0].games, 10);
      assert.deepEqual(summary.discover.find(r => r.card === "CARD_X"), { kind: "discover", card: "CARD_X", offered: 20, picked: 20, rate: 1 });
      assert.equal(summary.mulligan.find(r => r.card === "CARD_A").offered, 10);
    }
  }
});

test("the mulligan summary counts copies: a game keeps at most as many copies as it was offered", async () => {
  // Hand calculation. Every hand offers CARD_A twice and CARD_B once; copies kept per game, A of 2 and B of 1:
  //   [A, B] x4 -> A 1, B 1;  [A, A] x4 -> A 2, B 0;  [] x1 -> 0, 0;  forged [A, A, A, B] x1 -> A 2 (not 3), B 1.
  //   A: 4 + 8 + 0 + 2 = 14 of 20 = 0.7 (counting a copy as kept whenever its card was kept gives 18); B: 5 of 10.
  const kept = [...Array(4).fill(["CARD_A", "CARD_B"]), ...Array(4).fill(["CARD_A", "CARD_A"]), [], ["CARD_A", "CARD_A", "CARD_A", "CARD_B"]];
  const games = kept.map((k, i) => ranked({ id: (0x2000 + i).toString(16).padStart(32, "0"), mull: { o: ["CARD_A", "CARD_A", "CARD_B"], k, r: [], n: [] } }));
  const mulligan = async (count) => {
    const env = sqlite();
    // Two games an install: ten games of five installs, the ninth game alone on the fifth.
    for (let i = 0; i < count; i += 2) {
      const response = await post("/v1/events", { v: 1, iid: (1 + i / 2).toString(16).padStart(32, "0"), games: games.slice(i, Math.min(i + 2, count)) }, env);
      assert.equal(response.status, 200);
    }
    return JSON.parse(await rebuildEvents(env, true)).mulligan;
  };
  assert.deepEqual(await mulligan(10), [
    { card: "CARD_A", offered: 20, kept: 14, rate: 0.7 },
    { card: "CARD_B", offered: 10, kept: 5, rate: 0.5 },
  ]);
  assert.deepEqual(await mulligan(9), [], "18 copies of CARD_A are still 9 games: the floor counts games");
});

// ---- Opening-hand pairs (metric mulligan.pair_winrate, rules mull-pairs-1) ---------------------------------------

let sequence = 0x1000;
const install = (n) => (0xabc000 + n).toString(16).padStart(32, "0");
const hand = (offered, kept) => ({ o: offered, k: kept, r: offered.filter((c) => !kept.includes(c)), n: [] });

/** Stores `count` Ranked games of one install, the first `wins` of them won, in batches of 20. */
async function send(env, iid, count, wins, overrides) {
  const games = Array.from({ length: count }, (_, i) => ranked({ id: (sequence++).toString(16).padStart(32, "0"), res: i < wins ? "W" : "L", ...overrides }));
  for (let i = 0; i < games.length; i += 20) {
    const response = await post("/v1/events", { v: 1, iid, games: games.slice(i, i + 20) }, env);
    assert.equal(response.status, 200);
  }
}

/** The same cell spread over installs: `perInstall` games each, `winsEach` of them won. */
async function spread(env, installs, perInstall, winsEach, overrides) {
  for (const n of installs) await send(env, install(n), perInstall, winsEach, overrides);
}

const pairs = async (env, query = "fmt=standard&cls=MAGE") =>
  worker.fetch(new Request(`https://pool.test/v1/events/mulligan-pairs?${query}`), env, { waitUntil() {} });

test("the pair table counts the four keep cells of a pair by hand, and nothing from outside the sample", async () => {
  const env = sqlite();
  const AB = ["CARD_A", "CARD_B", "CARD_Z"];
  // Both kept: 5 installs x 40 games, 24 won each = 200 games, 120 wins.
  await spread(env, [1, 2, 3, 4, 5], 40, 24, { mull: hand(AB, ["CARD_A", "CARD_B"]) });
  // Only A kept: 5 installs x 10 games, 4 won each = 50 games, 20 wins.
  await spread(env, [1, 2, 3, 4, 5], 10, 4, { mull: hand(AB, ["CARD_A"]) });
  // Only B kept: 6 installs x 2 games, 1 won each = 12 games, 6 wins.
  await spread(env, [1, 2, 3, 4, 5, 6], 2, 1, { mull: hand(AB, ["CARD_B", "CARD_Z"]) });
  // Neither kept: 9 games of 5 installs, under the 10 games of a cell: not published.
  await spread(env, [1, 2, 3, 4], 2, 1, { mull: hand(AB, []) });
  await send(env, install(5), 1, 1, { mull: hand(AB, ["CARD_Z"]) });
  // Outside the sample, each of which would change a count above if it leaked in:
  await send(env, install(1), 3, 3, { mode: "casual", mull: hand(AB, ["CARD_A", "CARD_B"]) });
  await send(env, install(2), 3, 3, { res: "T", mull: hand(AB, ["CARD_A", "CARD_B"]) });
  await send(env, install(3), 3, 3, { fmt: "wild", mull: hand(AB, ["CARD_A", "CARD_B"]) });
  await send(env, install(4), 3, 3, { cls: "PRIEST", mull: hand(AB, ["CARD_A", "CARD_B"]) });
  await send(env, install(5), 3, 3, { cls: undefined, mull: hand(AB, ["CARD_A", "CARD_B"]) });
  await send(env, install(6), 3, 3, { day: TODAY - 31, mull: hand(AB, ["CARD_A", "CARD_B"]) });
  // The window ends today: a record dated tomorrow (a clock running ahead is accepted) waits until tomorrow.
  await send(env, install(6), 3, 3, { day: TODAY + 1, mull: hand(AB, ["CARD_A", "CARD_B"]) });
  assert.equal(env.db.prepare("SELECT COUNT(*) AS n FROM e WHERE day = ?").get(TODAY + 1).n, 3, "stored, not counted yet");
  // The first day of the window does count: two more only-B games bring that cell from 12 to 14 below.
  await send(env, install(6), 2, 1, { day: TODAY - 30, mull: hand(AB, ["CARD_B"]) });
  // A kept without B on offer says nothing about the pair; both copies of one card are not a pair.
  await send(env, install(1), 5, 5, { mull: hand(["CARD_A", "CARD_A", "CARD_Q"], ["CARD_A", "CARD_A"]) });

  const response = await pairs(env);
  assert.equal(response.status, 200);
  assert.equal(response.headers.get("cache-control"), "no-store");
  const text = await response.text();
  const body = JSON.parse(text);
  assert.deepEqual({ ...body, updated: 0 }, {
    v: 1, metric: "mulligan.pair_winrate", rules: "mull-pairs-1", fmt: "standard", cls: "MAGE", window: 30, updated: 0,
    minGames: 200, minInstalls: 5,
    pairs: [{ a: "CARD_A", b: "CARD_B", installs: 5, both: [200, 120], onlyA: [50, 20], onlyB: [14, 7], none: null }],
  });
  // Nothing that identifies an install, a record or a deck leaves with the totals.
  for (const secret of [install(1).slice(-6), "AAECAf0EBuwH", "iid", "deck"]) assert.equal(text.includes(secret), false, secret);
  // Other groups hold nothing of their own: three games are far below the threshold.
  assert.deepEqual((await (await pairs(env, "fmt=wild&cls=MAGE")).json()).pairs, []);
  assert.deepEqual((await (await pairs(env, "fmt=standard&cls=PRIEST")).json()).pairs, []);
});

test("a pair needs 200 games with both kept and 5 installs: 199 games or 4 installs publish nothing", async () => {
  const short = sqlite();
  await spread(short, [1, 2, 3, 4], 40, 20, { mull: hand(["CARD_A", "CARD_B"], ["CARD_A", "CARD_B"]) });
  await send(short, install(5), 39, 20, { mull: hand(["CARD_A", "CARD_B"], ["CARD_A", "CARD_B"]) });
  assert.deepEqual((await (await pairs(short)).json()).pairs, [], "199 games of 5 installs");

  const few = sqlite();
  await spread(few, [1, 2, 3, 4], 50, 25, { mull: hand(["CARD_A", "CARD_B"], ["CARD_A", "CARD_B"]) });
  assert.deepEqual((await (await pairs(few)).json()).pairs, [], "200 games of 4 installs");

  // The 200th game publishes it; a comparison cell of four installs stays hidden.
  await send(short, install(5), 1, 0, { mull: hand(["CARD_A", "CARD_B"], ["CARD_A", "CARD_B"]) });
  await spread(short, [1, 2, 3, 4], 5, 2, { mull: hand(["CARD_A", "CARD_B"], ["CARD_A"]) });
  short.db.exec("DELETE FROM snap");
  assert.deepEqual((await (await pairs(short)).json()).pairs,
    [{ a: "CARD_A", b: "CARD_B", installs: 5, both: [200, 100], onlyA: null, onlyB: null, none: null }]);
});

test("forget withdraws the pair table with the summary, and the rebuilt table has lost the games of that install", async () => {
  const env = sqlite();
  await spread(env, [1, 2, 3, 4, 5], 40, 24, { mull: hand(["CARD_A", "CARD_B"], ["CARD_A", "CARD_B"]) });
  assert.equal((await (await pairs(env)).json()).pairs.length, 1);
  await worker.fetch(new Request("https://pool.test/v1/events/summary"), env, { waitUntil() {} });
  assert.equal(env.db.prepare("SELECT COUNT(*) AS n FROM snap WHERE mode IN ('events', 'mull-pairs')").get().n, 2);

  const response = await post("/v1/events/forget", { iid: install(3) }, env);
  assert.deepEqual(await response.json(), { ok: true, deleted: 40 });
  assert.equal(env.db.prepare("SELECT COUNT(*) AS n FROM snap WHERE mode IN ('events', 'mull-pairs')").get().n, 0, "both views withdrawn");
  assert.deepEqual((await (await pairs(env)).json()).pairs, [], "160 games of 4 installs are under both thresholds");

  // Six installs: forgetting one leaves a published pair without its games.
  const wide = sqlite();
  await spread(wide, [1, 2, 3, 4, 5, 6], 40, 24, { mull: hand(["CARD_A", "CARD_B"], ["CARD_A", "CARD_B"]) });
  assert.deepEqual((await (await pairs(wide)).json()).pairs[0].both, [240, 144]);
  await post("/v1/events/forget", { iid: install(6) }, wide);
  assert.deepEqual((await (await pairs(wide)).json()).pairs, [{ a: "CARD_A", b: "CARD_B", installs: 5, both: [200, 120], onlyA: null, onlyB: null, none: null }]);
});

test("the clean-up withdraws the pair table too, and an empty pool answers an empty list", async () => {
  const env = sqlite();
  const empty = await pairs(env);
  assert.equal(empty.status, 200);
  assert.deepEqual((await empty.json()).pairs, []);
  assert.equal(env.db.prepare("SELECT COUNT(*) AS n FROM snap WHERE mode = 'mull-pairs'").get().n, 1);
  await pruneEvents(env);
  assert.equal(env.db.prepare("SELECT COUNT(*) AS n FROM snap WHERE mode = 'mull-pairs'").get().n, 0);
});

test("GET /v1/events/mulligan-pairs answers 400 to a missing or malformed format or class", async () => {
  const env = sqlite();
  for (const query of ["", "fmt=standard", "cls=MAGE", "fmt=classic&cls=MAGE", "fmt=STANDARD&cls=MAGE", "fmt=standard&cls=mage",
    "fmt=standard&cls=MA", "fmt=standard&cls=MAGE%27--", "fmt=standard&cls=", "fmt=standard%7Cwild&cls=MAGE"]) {
    const response = await pairs(env, query);
    assert.equal(response.status, 400, query);
    assert.equal(response.headers.get("cache-control"), "no-store");
  }
  assert.equal(env.db.prepare("SELECT COUNT(*) AS n FROM snap").get().n, 0, "a refused request builds nothing");
  assert.equal((await post("/v1/events/mulligan-pairs?fmt=standard&cls=MAGE", {}, env)).status, 404, "read-only");
});

// ---- The deletion fence: a forget between a build's reads and its publication (D1 has no transaction across them) --

/** Runs `between` once, at the moment the build has read the records and is about to publish. */
function interleave(env, between, every = false) {
  const batch = env.DB.batch;
  let busy = false;
  let done = false;
  env.DB.batch = async (list) => {
    if (!busy && (every || !done)) {
      busy = true;
      done = true;
      await between();
      busy = false;
    }
    return batch(list);
  };
}

test("a forget that lands between the build's reads and its publication is not published by that build", async () => {
  const env = sqlite();
  await spread(env, [1, 2, 3, 4, 5], 40, 24, { mull: hand(["CARD_A", "CARD_B"], ["CARD_A", "CARD_B"]) });
  // Without the fence this build would publish the pair (200 games of 5 installs) it read before the forget.
  interleave(env, () => post("/v1/events/forget", { iid: install(5) }, env));

  const response = await pairs(env);

  assert.equal(response.status, 200);
  assert.deepEqual((await response.json()).pairs, [], "the build counted again: 160 games of 4 installs");
  assert.deepEqual(JSON.parse(env.db.prepare("SELECT body FROM snap WHERE mode = 'mull-pairs'").get().body).groups, {});
  const summary = JSON.parse(env.db.prepare("SELECT body FROM snap WHERE mode = 'events'").get().body);
  assert.equal(summary.games, 160, "the summary went through the same fence");
  assert.deepEqual(summary.days, [], "four installs are no day cell");
  assert.deepEqual(summary.mulligan, []);
});

test("the summary read is fenced the same way", async () => {
  const env = sqlite();
  await spread(env, [1, 2, 3, 4, 5], 4, 2, { mull: hand(["CARD_A", "CARD_B"], ["CARD_A"]) });
  interleave(env, () => post("/v1/events/forget", { iid: install(1) }, env));

  const response = await worker.fetch(new Request("https://pool.test/v1/events/summary"), env, { waitUntil() {} });

  const summary = await response.json();
  assert.equal(summary.games, 16);
  assert.deepEqual(summary.days, []);
  assert.deepEqual(summary.mulligan, [], "20 offers of 5 installs before the forget, 16 of 4 after");
});

test("a build that deletions keep overtaking publishes nothing and answers 503", async () => {
  const env = sqlite();
  await spread(env, [1, 2, 3, 4, 5], 40, 24, { mull: hand(["CARD_A", "CARD_B"], ["CARD_A", "CARD_B"]) });
  interleave(env, () => pruneEvents(env), true);

  for (const response of [await pairs(env), await worker.fetch(new Request("https://pool.test/v1/events/summary"), env, { waitUntil() {} })]) {
    assert.equal(response.status, 503);
    assert.equal(response.headers.get("retry-after"), "30");
    assert.equal(response.headers.get("cache-control"), "no-store");
  }
  assert.equal(env.db.prepare("SELECT COUNT(*) AS n FROM snap WHERE mode IN ('events', 'mull-pairs')").get().n, 0);
  await assert.rejects(rebuildEvents(env, true), /events_build_overtaken/);
});

test("the fence is a row of snap that deletions raise and nothing else touches", async () => {
  const env = sqlite();
  const revision = () => env.db.prepare("SELECT last_row FROM snap WHERE mode = 'events-rev'").get()?.last_row ?? 0;
  await send(env, install(1), 2, 1, {});
  await rebuildEvents(env, true);
  assert.equal(revision(), 0, "storing and building leave it alone");
  await post("/v1/events/forget", { iid: install(1) }, env);
  assert.equal(revision(), 1);
  await pruneEvents(env);
  assert.equal(revision(), 2);
  assert.equal(JSON.parse(await rebuildEvents(env, true)).games, 0, "a build after the deletions publishes again");
  assert.equal(revision(), 2);
});
