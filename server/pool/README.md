# Hearthpulse stats pool

The separate Node 24 / private source-capture service lives in [`node/`](node/) and preserves the legacy routes
below with a real SQLite/D1 adapter. See [`handover-P2.md`](handover-P2.md) for integration contracts and
[`deploy/LINUX-RUNBOOK.md`](deploy/LINUX-RUNBOOK.md) for isolated Linux staging. Source capture is not hosted by
the Cloudflare entrypoint. Public `/v1/events/summary` now uses `Cache-Control: no-store` so forget cannot leave
stale deletion-sensitive totals in a public cache. Apply migration `0008_captures.sql` to existing databases for
transactional snapshot invalidation; the Node service applies it automatically. Migration `0009_analytics.sql`
adds the source v2 analytics facts and the published snapshot behind `GET /v2/analytics/bg/heroes`
([docs/analytics-api.md](../../docs/analytics-api.md)); the Node service applies it too, the Worker never serves it.
Migration `0010_bg_combats.sql` adds the Battlegrounds fights of source v3 journals (`bg_combat`, table `bg_combats`,
[docs/source-capture.md](../../docs/source-capture.md) "Battlegrounds fights"): stored with the manifest commit, gone
with the capture (expiry, forget, deletion), never published, read only by the owner's export: on the host,
`set -a; . /etc/hearthpulse-pool/pool.env; set +a` (or `read -rs POOL_OPERATOR_TOKEN; export POOL_OPERATOR_TOKEN`, never
the token on a command line), then `node server/pool/node/export-combats.mjs --out fights.jsonl` (loopback operator
route `/v1/captures/combats/export`, refused by nginx). The export file is a working copy only on this host or the
owner's PC, moved over SSH, never committed, put on a cloud drive or given to anyone, and deleted after the measurement
and no later than 180 days from the `day` of its rows (`simcover --pool` ignores older rows). Deploy this before an app
that writes source v3 is released, and update the site's privacy policy first (docs/source-capture.md). Source v4
([docs/source-capture.md](../../docs/source-capture.md) "Source v4": a gap is a real loss) needs no migration: the
intake takes v4 lines with no `combat_view` gap and does not count an `OTHER` block as unsupported. Deploy it, with the
worker `source-v2.1`, before an app that writes v4 is released; an app meeting an older pool sends its journals as v3
until it restarts. Tests:
`npm test` here (the same `node --test server/pool/test/*.test.mjs` as CI).

A small Cloudflare Worker that collects the Battlegrounds games players choose to share and serves the totals:
heroes, final boards, trinkets, tribes, tavern tier timing, combat odds, picks (Discover, triple rewards, trinkets)
and the cards played. It also collects the Discover-style picks of shared Ranked and Casual Constructed games and
serves what players take from each card (the pick hint on the app's card pool panel). It is built to stay inside
Cloudflare's free plan.

It can also serve **base Battlegrounds statistics** (heroes, cards, trinkets and comp guides) from D1 at `/v1/meta`.
Hearthpulse ships that table empty: upstream filled it once from a third-party stats service, and that data is not
part of this repository. Fill it only from a source you are allowed to redistribute.

What a shared game contains: a game id (an HMAC of the game made with a key that never leaves the player's PC), the day it was played, solo or duos, the rating rounded down to 500,
the heroes offered and picked, the placement, and the game details listed in the app.
A shared Constructed game holds only its id, day, format (standard or wild), whether it was won,
and its picks: the source card, the cards it offered and the one taken. No deck, class, rank or opponent.
No player name, BattleTag, account or IP address is stored, and nothing about other players. Shared games are kept
for 60 days, then the cron job deletes them (the totals use the last 30). The totals are public: anyone can read
them, and the app's notice says so. No published row or cell (a hero, comp, card, trinket, tier, pick or play, in
all or in a rating bracket, a hero with a lobby tribe) holds fewer than 10 distinct games; a picked-outcome average
is null below 10 distinct games that picked that option, and combat odds need predictions from 10 distinct games.
Repeated observations within a game remain frequencies, but do not help it cross the publication floor. The pool keeps no installation id for these games, so the floor is
in games, not in players. Workers logs are off (`wrangler.toml`), so no IP address or request body
lands in Cloudflare's logs either.

## Free plan budget

The free plan allows 100,000 requests a day, 100,000 rows written and 5 million rows read per day.

- Writes are capped: at most 15,000 shared games a day in all (then `POST` answers 503 until the next day; table
  `quota`), and 6 requests a minute from one address (Cloudflare's rate limiter; the app waits 12 s between batches
  of earlier games). A game writes its row and an index entry, the nightly clean-up deletes as many: about 4 rows per
  game, so the cap keeps writes near 60,000 rows a day.
- A game is one row (plus one index entry). The app sends a game when it ends, and earlier games in batches of 20,
  so a batch of 20 games is one request and one `INSERT` statement.
- The totals are not worked out on each read. A cron job (every 6 hours) rebuilds them only when new games came in,
  and stores them as one JSON row in `snap`. Own totals (`/v1/stats`, `/v1/heroes`, `/v1/choices`) read that row
  with `Cache-Control: no-store` and bypass the HTTP response cache. Base `/v1/meta` retains its 30-minute cache.
  On upgrade from older publication rules, clear and rebuild `solo`, `duos`, `choices-standard`, `choices-wild`
  and `events` snapshot rows before serving traffic. Already downloaded responses under the previous 30-minute
  HTTP policy cannot be recalled by this change.
- Each rebuild reads the last 30 days of games about 16 times (the per-bracket totals take one pass each, a column
  set per bracket). At 10,000 games in 30 days that is about 1.3 million rows read a day for both modes, well under
  the limit.

## Deploy

You need Node.js and a Cloudflare account.

```
cd server/pool
npx wrangler login
npx wrangler d1 create hearthpulse-pool
```

Copy the `database_id` it prints into `wrangler.toml`, then:

```
npx wrangler d1 execute hearthpulse-pool --remote --file=schema.sql
npx wrangler deploy
```

`deploy` prints the address, for example `https://hearthpulse-pool.<you>.workers.dev`. Put it in
`PoolClient.DefaultEndpoint` (src/Hearthpulse.Core/Pool/PoolClient.cs) and release the app.

Checklist (the owner deploys; nothing here is deployed from a development session):

1. `node --test server/pool/test/validation.test.mjs server/pool/test/events.test.mjs` passes.
2. `wrangler d1 create`, `database_id` in `wrangler.toml`, `schema.sql` on the new database (an existing pool: the
   files in `migrations/` it lacks, in order; plan 2.1 adds `0006_quota.sql`, the daily write counter; game records add `0007_events.sql`).
3. `npx wrangler deploy`; then `GET <address>/v1/stats?mode=solo` answers `{"games":0,…}` (empty until the first cron
   run after games come in) and `POST /v1/games` with a bad body answers 400.
4. Before a release, try the app against it without rebuilding: `"PoolEndpoint": "https://…"` in `settings.json` of a
   copy of the data folder (`HEARTHPULSE_DATA`). The app uses `PoolEndpoint` when set, else `DefaultEndpoint`; only
   `https://` addresses are used.
5. Set `DefaultEndpoint`, release. Sharing is on by default with an opt-out (the owner's decision, 2026-11-15; no
   in-app notice since 2026-11-17): the Settings → Data switch stops it. The routes have no deletion on request: shared games expire after 60 days.

To move an existing pool to a new version, run the new file in `migrations/` with
`npx wrangler d1 execute hearthpulse-pool --remote --file=migrations/<file>.sql`, then `npx wrangler deploy`.

## Base stats

`migrations/0004_meta.sql` creates the `meta` table, empty. The body is one compact JSON document (heroes per MMR
bracket, cards per bracket with their turn results, trinkets per MMR, comps with their MMR and hero placement, comp
guides) split into numbered rows small enough for a D1 statement. Until rows are added, `/v1/meta` answers 503 and
the app keeps its own base stats, with the shared games blended in. Create the table with:

```
npx wrangler d1 execute hearthpulse-pool --remote --file=migrations/0004_meta.sql --yes
```

## API

- `POST /v1/games` with `{"v":3,"games":[...]}`, 1 to 20 games. Each game:
  `{"id":"<32 hex>","day":20749,"mode":"solo","bracket":6500,"offered":["BG_HERO_1",...],"picked":"BG_HERO_1",
  "placement":3,"src":"live"}`, plus the optional details `turns`, `tier`, `triples`, `anomaly`, `tribes`, `comp`,
  `board` (`[{"c","g","a","h"}]`), `trinkets` (`{"o":[...],"p":[...]}`), `tierups` (`[[turn,tier]]`),
  `combats` (`[[turn,"W|T|L",taken,dealt,winChance|null]]`), `choices` (`[[turn,kind,source,[offered],[picked]]]`, kind `discover`, `trinket`, `choice`, `quest` or `reward`)
  and `plays` (`[[turn,card]]`). A game sent twice is stored once. Hearthpulse 0.8 and 0.9 still send one game
  (`"v":1` or `"v":2`) and are accepted.
- `GET /v1/stats?mode=solo` returns everything: `games`, `detailedGames`, `heroes`, `cards`, `trinkets`, `comps`,
  `tierUps`, `combatOdds`, `choices` and `plays`, over the last 30 days. Since app 0.17 (plan 2.1) also:
  - `ratedGames` and `percentiles`: `[{"top":50,"bracket":7000,"games":1200}, …]` for the top 50/20/5/1 %: the lowest
    rating bracket that reaches the share, walking down from the highest, over the pool's own rated games, with the games
    at or above it. None under 1,000 rated games; a floor with under 50 games at or above it is left out, and so is a
    narrower one on the same bracket as a wider one. These are players who share, likely above the ladder's average:
    not Blizzard's percentiles. The app uses them under the same conditions only.
  - `heroesByBracket`: `{"50":[{hero, games, avgPlacement, top, first}], …}` and `trinketsByBracket`:
    `{"50":[{card, picked, avgPlacement}], …}`: the same totals over the games at or above each published floor, 10 games
    or more a row.
  - `heroTribes`: `[{hero, tribe, games, avgPlacement}]` (10 games or more): how a hero places with each tribe in the
    lobby. The app uses a row from 100 games and shrinks it toward zero (`delta · n / (n + 150)`): the hero pick adds up
    five lobby tribes, and noise must not reorder the heroes.
  - Quests: `choices` rows of kind `quest` (the quests offered and taken) and `reward` (quest rewards).
- `GET /v1/heroes?mode=solo` returns only the hero totals (used by Hearthpulse 0.8).
- `POST /v1/constructed` with `{"v":1,"games":[...]}`, 1 to 20 games. Each game:
  `{"id":"<32 hex>","day":20752,"format":"standard","won":true,"src":"live","picks":[[turn,"SOURCE",["A","B","C"],["B"]],...]}`
  (up to 60 picks, 2 to 8 offered, 1 to 4 picked among the offered). A bad pick is dropped; a game without a valid
  pick is rejected. Needs `migrations/0005_constructed.sql` on an existing pool.
- `GET /v1/choices?format=standard` (or `wild`) returns `{format, days, updated, games, sources:[{source, choices,
  options:[[card, offered, picked, wonWhenPicked], ...]}]}` over the last 30 days; options offered fewer than 10
  times are left out, and the wins of an option picked fewer than 10 times are null. Rebuilt by the same cron job into `snap` rows `choices-standard` / `choices-wild`.
- `GET /v1/meta` returns the base stats (see above) when the table has rows, cached at the edge for 30 minutes.
- Game records of every mode (`src/events.js`, [docs/game-stats.md](../../docs/game-stats.md), ADR 0011; needs
  `migrations/0007_events.sql` on an existing pool): `POST /v1/events` with `{"v":1,"iid":"<32 hex>","games":[...]}`
  (1 to 20 records, each rebuilt from known fields only, at most 24 KB; 8,000 a day), `POST /v1/events/forget` with
  `{"iid":...}` deletes every record of that install, `GET /v1/events/summary` returns games per day, the most picked
  Discover options and the mulligan keep rate per card (10 games or more a card). The keep rate's `offered` and `kept`
  count copies, a game keeping at most the copies it was offered; the floor counts games. Records are kept 180 days.
- `GET /v1/events/mulligan-pairs?fmt=standard|wild&cls=<CLASS>` (metric `mulligan.pair_winrate`, rules
  `mull-pairs-1`, [docs/analytics-metrics.md](../../docs/analytics-metrics.md)): pairs of different cards offered
  together in the opening hand of Ranked games of that format and class over the last 30 days, each as
  `{a, b, installs, both, onlyA, onlyB, none}` with `[games, wins]` per keep cell. A pair needs 200 games with both
  kept, of 5 installs or more; a comparison cell under 5 installs or 10 games is `null`; at most 200 pairs. No
  install id, record id or deck in the answer. The window ends today (a record dated tomorrow waits a day). Built
  in the same rebuild as the summary into `snap` row `mull-pairs` (no migration), deleted with it by
  `/v1/events/forget` and the clean-up in one batch, answered `Cache-Control: no-store`; `400` for a missing or
  malformed parameter, `503` with `Retry-After` when deletions kept overtaking the build. The numbers are an
  observed association with a nominal interval, not a causal effect (see the catalogue).
- Deletion fence for both views of the records: `snap` row `events-rev` counts deletions (`last_row`). A forget and
  the clean-up raise it in the batch that deletes the records and the views; a build reads it first and publishes
  both views in one batch only while it is unchanged, else it counts again. No migration: the row appears on the
  first deletion. Behind nginx the route must be in the
  location's list (`deploy/nginx-source-capture.conf`).

For a local test, run `npx wrangler dev --test-scheduled` and open `/__scheduled` to run the cron job.

## Validation tests

From the repository root, with Node.js 24 or later:

```
node --test server/pool/test/validation.test.mjs server/pool/test/events.test.mjs
```

The built-in runner needs no npm packages. Tests exercise the validators and `POST /v1/games` with an in-memory
database stub, and `POST /v1/constructed` with the pick totals on Node's built-in SQLite (`node:sqlite`, the real
`schema.sql`), without contacting Cloudflare. Invalid required game fields reject that game; invalid optional
details are omitted while the valid game summary is stored. Card IDs must be strings, and trinket lists are checked
before they are merged (up to four picks and twelve offers, with at most twelve distinct cards in their union).
