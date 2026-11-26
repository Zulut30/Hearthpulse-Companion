# Own numbers from game records: the record pass and its views

Hearthpulse's own Constructed, Battlegrounds and Arena numbers are computed by the pool's Node service from the game
records of `/v1/events` ([game-stats.md](game-stats.md)), by HSReplay's rules: a balance patch and the last 7 and 30
days inside it, rank buckets and MMR ranges, five installations behind every number and an interval with every
value. This page is the framework (TASK-2026-11-26-pro-collection, package A0, contracts K0–K4): what a view or a
deriver must provide, what the pass does, what the route answers, and how deletion withdraws the numbers. The metric
definitions themselves live in the catalogue ([analytics-metrics.md](analytics-metrics.md), `analytics-metrics.json`);
the source-capture snapshot (`/v2/analytics/bg/heroes`) is a different pipeline ([analytics-api.md](analytics-api.md)).

Only the Node service computes these views; the Cloudflare Worker serves none of them.

## Layout

```
server/pool/
  src/events.js              intake of records (K1 fields), forget and clean-up: they withdraw the views
  node/server.mjs            route GET /v2/analytics/records/<view>, the pass timer, /healthz `records`
  node/records/
    index.mjs                refresh(db, opts) · answer(db, view, params, opts) · withdraw(db) · status(db)
    scan.mjs                 one pass over `e`: the last 90 days → Rec
    periods.mjs              balance patches, lines, windows patch / 7d / 30d
    buckets.mjs              rank buckets, MMR ranges, coin; query validators
    stats.mjs                RATE, MEAN, PLACEMENT, thresholds, rankable, rows / suppressed
    loader.mjs               finds derive/*.mjs and views/*.mjs, checks the contract and the catalogue
    derive/<name>.mjs        derivers (deck: B2, archetypes: B1)
    views/<key>.mjs          one view per file (wave 2)
  data/balance-patches.json  kept by hand (data/README.md)
  test/records-kit.mjs       the test kit for views and derivers
```

## The pass

`refresh()` runs in the service's queue (nothing else writes meanwhile), in one transaction, every 60 seconds when it
is due, never in a request:

- due when nothing was built yet; a view row is missing or withdrawn (not the row of a view that failed in the last
  pass); the day changed; the signature changed (a view's
  key, metric, rules, line or modes, a deriver's name, order or version, the patches file, the thresholds); or the
  number of records changed and the last build is 30 minutes old or more;
- reads the records of the last 90 days up to today (`day` in [T − 89, T]; a record dated tomorrow waits), of the
  modes the loaded views count, once, in (day, mode, id) order;
- runs every deriver on each record in `order`, then hands the record to every view of its line and mode, in every
  window it belongs to (`add`), and finally `finish`es each view in each window;
- publishes every view in the same transaction: `snap` row `records:<view>` (`last_row` = records counted,
  `built` = publication time, `body` = the snapshot) and the state row `records` (count, time, signature, failed
  views). Rows of views that no longer load are deleted.

A view that throws in `start`, `add` or `finish`, or whose snapshot would hold an install id or a record id of the
pass, is not published: one line `records_view_failed` with its key and the stage, its previous row stays (served with
`stale: true` when records changed since), the other views publish. A failed view never makes a pass due by being
missing: it is retried by the next pass due for another reason (new records 30 minutes after the last pass, a new day,
a new version), so a broken view costs one pass in 30 minutes, not one a minute. A deriver that throws leaves its fields of that record unset and is reported once a pass
(`records_derive_failed` with the count). A failure of the pass itself rolls everything back (`records_refresh_failed`).

## A record as views see it (K2)

```js
/** @typedef {{ iid: string,          // only to count installations; never published
 *   day: number, mode: string, fmt: string|null, build: number|null,
 *   line: 'constructed'|'battlegrounds'|'arena', patch: string|null,   // null: build unknown or below the file
 *   ranks: string[], mmr: string[], coin: string[],                    // the buckets the record falls into
 *   body: object,                       // the record as validateEvent stored it
 *   d: { cards?, deckKey?, arch?, oarch? } }} Rec                      // filled by the derivers
 */
```

Lines: `constructed` counts `ranked`; `battlegrounds` counts `bg` and `duos`; `arena` counts `arena` and
`arena_run`. Casual and Brawl are never read. Views treat a Rec as read-only: the same object goes to every view.

| Slice | Values | A record without the field |
|---|---|---|
| rank bucket (Ranked only) | `all` `bronze` `silver` `gold` `platinum` `diamond` `d4-1` `legend` `d4-legend` | `all` only |
| MMR range (`bg`, `duos`) | `all` `0-3999` `4000-5999` `6000-7999` `8000+` | `all` only |
| coin | `any` `first` (`first: true`) `coin` (`first: false`) | `any` only |

`d4-1` is diamond with the record's `band: "d4-1"` (star level 47–50); `d4-legend` joins `d4-1` and `legend`.

## Windows and patches (K3)

`server/pool/data/balance-patches.json` holds the balance patches of each line ([data/README.md](../server/pool/data/README.md)).
The patch of a build is the entry with the highest `fromBuild` not above it; a build below the first entry, or no
build, is unknown. Windows of a line on day T:

| Window | Records | Days of `period` |
|---|---|---|
| newest patch, `patch` | the patch's builds, all 90 days read | `max(fromDay, T−89)` … T |
| newest patch, `7d` | the patch's builds, day ≥ T − 6 | `max(that, T−6)` … T |
| newest patch, `30d` | the patch's builds, day ≥ T − 29 | `max(that, T−29)` … T |
| previous patch, `patch` | the previous patch's builds | `max(fromDay, T−89)` … the day before the newest starts |
| `unknown`, `30d` | unknown build, day ≥ T − 29 | T − 29 … T |

Patches older than the previous one are not shown. `period.builds` is `[lowest, highest]` build among the records the
view received in that window, or `null`.

## Numbers (`ctx.stats`)

The thresholds of K3 are the foundation's (`stats.mjs`, `THRESHOLDS`): 5 installations behind every number
(`minInstalls`), 10 games behind a value (`minGames`) and 50 games behind a deck row (`minDeckGames`). The first two
come from the analytics settings (`ANALYTICS_MIN_SOURCES`, `ANALYTICS_MIN_CELL`, same defaults; production keeps them)
and travel in every envelope as `thresholds: {minInstalls, minGames}`. A view writes no threshold of its own: it uses
the helpers below, and where its `data` names a threshold it reads `ctx.stats.minInstalls`, `minGames` or
`minDeckGames` (`constructed-decks` has `minGames: ctx.stats.minDeckGames`).

- `rate(num, den, installs)` → `RATE {num, den, value, low, high, rankable}`: Wilson interval, z = 1.96.
- `mean(values | Moments, installs, kind)` → `MEAN {n, mean, low, high, rankable}`: mean ± 1.96·s/√n; `kind`
  `'place'` or `'wins'` sets the ranking limit; a mean without a kind (turns, seconds) is never rankable.
- `placement(places, installs, max = 8)` → `PLACEMENT {n, mean, low, high, dist, rankable}`; places outside 1..max
  are not counted (a duos record placed 5 or worse is not a duos place).
- Each returns `null` under `minInstalls` distinct installations among its own observations (`installs` is a Set of
  `rec.iid` or a count); with the installations but a denominator under `minGames` it keeps its counts and the
  value, interval and distribution are `null`. Values are rounded to 3 decimals.
- `totals(games, installs)` → `{games, installs, quality}`: the totals of a slice are numbers too. Under
  `minInstalls` both are `null` and `quality` is `'small_sample'`; under `minGames` games `quality` is
  `'small_sample'`; else `'ok'`. A slice whose totals are hidden publishes no other number either (`suppressed: null`,
  no rows): the view leaves it out of the snapshot and its `slice` answers it as a slice nobody played, so the two
  cannot be told apart (`winrateView` in the kit is the pattern).
- `rankable`: the interval's half-width is at most 5 points for a share, 0.3 for a place, 0.3 for wins.
- `rows(cells, makeRow)` → `{rows, suppressed}`: cells under `minInstalls` are not rows, only counted.
- `deckRows(cells, makeRow)`: as `rows`, and each cell also needs `cell.games` ≥ `minDeckGames`. A deck code is
  published only from 50 games of 5 installations (AC14); a cell without `games` is no row.
- `quality(...numbers)` → `'ok'` when every number has a value, else `'small_sample'`.
- `rankFirst(rows, isRankable, compare)`: rankable rows first, so an unrankable row never sorts above a rankable one.

## A deriver (`derive/<name>.mjs`)

```js
export const name = 'deck';        // [a-z][a-z0-9-]*, unique
export const order = 10;           // derivers run in this order (deck 10, archetypes 20)
export const version = 'deck-1';   // part of the signature and of every envelope's `derivers`
export function derive(rec, ctx) {} // fills rec.d.*; must not throw: unknown is null. ctx = {today, now}
```

## A view (`views/<key>.mjs`)

```js
export const key = 'constructed-mulligan';  // the file name and the route segment
export const metric = 'mulligan.card';      // a catalogue id, status implemented, rulesVersion = rules
export const rules = 'mull-card-1';
export const line = 'constructed';
export const modes = ['ranked'];            // modes of that line
export const query = { fmt: v => …, rank: param.rank };   // value | default when absent | undefined → 400
export function start(ctx) { return acc; }                // once per window
export function add(acc, rec, ctx) {}
export function finish(acc, ctx) { return body; }         // the whole window, every slice; thresholds applied
export function slice(body, params) { return data; }      // one slice for the envelope, from the parsed body
export function unsupported(params) { return false; }     // optional: true answers unsupported_mode
```

`ctx = {today, now, window: {line, patch, window, fromDay, toDay}, thresholds, stats}`. `window` and `patch` are the
route's own parameters; a view's `query` may not use them. Query validators for the shared slices are in
`buckets.mjs` (`param.rank`, `param.mmr`, `param.coin`, `oneOf(values, fallback)`).

The loader rejects (log line `records_module_rejected` with the file and the reason, the others load) a module that
does not import, a key that is not the file name, a line or modes outside the contract, a reserved or malformed query
parameter, a missing function, a repeated deriver name, and, when the catalogue is in the checkout, a metric that is
not `implemented` with the same `rulesVersion`. A wave 2 package switches its catalogue block to `implemented` with
its `rulesVersion` and an `api` that names `/v2/analytics/records/<key>`: `catalogProblems` then requires the view.

## Route and envelope (K4)

`GET /v2/analytics/records/<key>?window=patch|7d|30d&patch=<id>|unknown&<view parameters>`. Without `patch` the newest
patch of the line (none in the file: `unknown`); without `window`, `patch` (`30d` for `unknown`). The answer is read
from the published row, parsed once per build and kept in memory; `cache-control: no-store`.

```json
{ "v": 2, "source": "game_records", "view": "constructed-mulligan", "metric": "mulligan.card", "rulesVersion": "mull-card-1",
  "derivers": { "deck": "deck-1", "archetypes": "arch-20261126" },
  "available": true, "reason": null, "published": 1764150000000, "stale": false,
  "thresholds": { "minInstalls": 5, "minGames": 10 },
  "period": { "line": "constructed", "patch": "b256589", "builds": [256589, 256601], "window": "patch", "fromDay": 20760, "toDay": 20783 },
  "windows": [ { "patch": "b256589", "window": "patch" }, { "patch": "b256589", "window": "7d" }, { "patch": "b256589", "window": "30d" }, { "patch": "unknown", "window": "30d" } ],
  "data": { } }
```

| Answer | When |
|---|---|
| `available: true` | published; `stale: true` when records arrived since the build or this view failed in the last pass |
| `not_published` | the view was never published |
| `rebuilding` | withdrawn after a deletion, or published under other rules (the next pass rebuilds it) |
| `no_window` | the requested window is not in the snapshot; `windows` lists those that are |
| `unsupported_mode` | the view's `unsupported(params)` said so |
| 400 `invalid_query` | an unknown or repeated parameter, a value a validator refuses, a malformed `window` or `patch` |
| 404 `unknown_view` | no loaded view has that key |
| 404 `not_found` | any other path under `/v2` (nginx passes the whole `/stats/v2/analytics/` prefix) |
| 405 | not GET (behind nginx: 403) |

`published` is the build time (ms since 1970, UTC) of the snapshot an answer serves. An answer that serves none
(`not_published`, `rebuilding`, `unsupported_mode`) has `published: null`, `data: null`, `period: null` and
`windows: []`; `no_window` serves the snapshot's `published`, `stale` and `windows` with `data: null` and
`period: null`. A reader (A5, K6) takes `published: null` as "not published": not an error, not a time 0.

No answer holds an install id or a record id: the pass refuses to publish a snapshot in which an install id or a
record id of the pass appears (`records_view_failed`, `stage: install_id` or `record_id`). A deck code of a single
game is the view's rule, not the pass's: deck rows come from `ctx.stats.deckRows` (50 games, 5 installations).

## Deletion

`POST /v1/events/forget` and the clean-up of records older than 180 days withdraw every view (`body = ''`) in the
batch that deletes the records, and only when the batch deletes one: the minute's clean-up that finds nothing old
leaves them published. The condition is read in the same batch before the delete. A withdrawn view answers
`rebuilding`; the next pass (within a minute) is due and counts without the deleted records. `withdraw(db)` does the
same for a caller in the service.

## Fields the pool accepts for these views (K1)

| Key | Where | Rule |
|---|---|---|
| `build` | every record, also `arena_run` | integer 1 … 99,999,999, else the field is dropped and the record kept |
| `band` | `mode: ranked` with `rank: diamond` | only `"d4-1"`; anything else, or another rank, drops the field |
| `bg.comp` | `bg`, `duos` | a tribe (`[A-Z0-9_]{2,24}`) or `MIXED`, else dropped |
| `arena_run` | a record of its own | `{id, day, mode: "arena_run", app?, build?, run: {kind, cls, wins, losses, deck?}}`: `kind` `arena` (loss limit 3) or `underground` (5); `cls` a class; `wins` 0–12; the run is over (12 wins or the loss limit, not both), else the record is refused; `deck` optional; no `res`, stored with `fmt = null` |

The batch envelope stays `{v: 1, iid, games}`; old apps keep working, an old pool drops the unknown fields (and
refuses an `arena_run`, so the pool ships before an app that sends one).

## Deployment

- The deploy ships `server/pool` alone, so the catalogue (`docs/`) is not on the host: the service loads views without
  the catalogue check there. The check runs in CI (`records-framework.test.mjs`, "the views in this checkout agree").
  In a full checkout an unreadable catalogue is one line `records_catalog_unreadable` and the views load unchecked:
  a docs file never stops the service.
- A missing or malformed `data/balance-patches.json` switches every view off (one line `records_patches_invalid` at
  start; the route answers 404 `unknown_view` and the pass removes the published rows); the service and the intake
  of records run on.
- nginx: one prefix `location ^~ /stats/v2/analytics/` (GET only, zone `hearthpulse_stats_read`) serves every view
  ([LINUX-RUNBOOK](../server/pool/deploy/LINUX-RUNBOOK.md), "Record views"). Until the first view is merged the route
  has no view: `GET /stats/v2/analytics/records/<anything>` answers the service's 404 `{"ok":false,"error":"unknown_view"}`.
- `/healthz` → `records: {built, views, published, withdrawn, failed}`.

## Tests

`server/pool/test/records-kit.mjs` gives a view test a pool in memory with the real schema, a frozen clock
(2026-11-26 12:00 UTC), `add(install, ...records)` through `validateEvent`, `refresh()`, `answer(view, query)` (the K4
envelope), `request()` (status and body), `forget(install)`, `prune(today)` and the views, derivers and patches the
test gives (each checked against the loader's contract). `records-periods.test.mjs` (windows, AC4),
`records-framework.test.mjs` (thresholds AC5, deletion AC6, loader and catalogue AC8, failures), `records-http.test.mjs`
(route AC7, nginx include), `record-fields.test.mjs` (fields AC3). Run with `node --test server/pool/test/*.test.mjs`.
