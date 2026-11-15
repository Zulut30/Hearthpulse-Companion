# Battlegrounds stats: where each number comes from

Battlegrounds community stats no longer come from Firestone (`static.zerotoheroes.com`): there is no permission to use
its data. The owner's decision: **heroes and minions from our API (`api.kolodahearthstone.com`, HSReplay data), the
rest from the Hearthpulse stats pool once it runs**. The app makes no request to `zerotoheroes.com` any more.

**Since 2026-11-15 the in-game advisor also reads the permitted external data for what the pool does not have yet**
(roadmap `2026-11-15-hsreplay-remediation`, 2.4.3; permission: [data-permissions.md](data-permissions.md)): comps and
trinkets from the site service's lists, and the lobby adjustment of the hero pick from each hero's compositions in
the API. Section 4 has the definitions and the small-sample rules. The pool's own rows, when it has them, stand in
front; the daily free limit (ADR 0009) applies to the pool's rows only.

**The main window's Battlegrounds tabs (Heroes, Trinkets, Cards, Comps) do not read this meta any more.** By the
owner's decision of 2026-11-09 ("as on hearthpulse.net") they show the lists of the site's open service
`https://bg.hs-manacost.ru`: [bg-site-stats.md](bg-site-stats.md). Of this meta the tabs keep one thing: the pool's
tribes, shown on the Comps tab when the service has no comps. That decision replaces the earlier "trinkets from the API are not used" for the tabs: the
service's trinket tier list is shown there. Firestone data stays out: the service's
lists are read only when their source is on the allow list (HSReplay), so its Firestone lists are refused. This document describes what the in-game advisor and the overlay
read.

Checked against the live API on 2026-10-23 (`openapi.json`, "Hearthstone Data API" 0.1.0). Field-level notes on the
API: `.claude/skills/hearthstone-context/reference/kolodahearthstone-api.md`.

## 1. What Firestone gave, and where it comes from now

| Firestone file (used by the advisor / pages) | Now | Gaps |
|---|---|---|
| Hero stats per MMR bracket 100/50/25/10/1: average place | **API** `GET /api/bg/heroes?mmr=TOP_50_PERCENT\|TOP_20_PERCENT\|TOP_5_PERCENT\|TOP_1_PERCENT` (current BG patch). Brackets in the app: top 50%, 20%, 5%, 1% | No "all ranks" slice for heroes (`mmr` rejects `ALL`): the widest hero bracket is top 50%. |
| Hero pick rate | API `pick_rate_value` (percent) | — |
| Hero place distribution, top 4, 1st | API `placement_distribution` (8 strings "22.00%") | — |
| Hero sample size (`dataPoints`) | — | **Not published.** Shown as "—"; the app treats 0 as "unknown, already filtered by the source". |
| Hero tribe impact (`tribeStats.impactAveragePosition`) | pool (`/v1/stats` `heroTribes`, since 0.17): placement with the tribe in the lobby minus overall, from 100 games of the hero with the tribe, shrunk toward zero (`delta · n / (n + 150)`). A hero without a pool row for the lobby: **the external lobby adjustment** from its compositions (section 4.3) | The external adjustment is top 50% only and solo only; it needs the lobby's tribes (memory, Windows). |
| Hero combat win rate per turn (`combatWinrate`) | not loaded | API has it only per hero (`/api/bg/heroes/{dbf}` → `combat_winrate[]`, top 50% only): 116 requests of ~150 KB each per refresh. My Stats shows no "global" combat line until there is a bulk endpoint. |
| MMR floor of each percentile (`mmrPercentiles`) | pool: `/v1/meta` `percentiles`, else `/v1/stats` `percentiles` (since 0.17: floors over the pool's own rated games, likely above the ladder's average; used only from 1,000 rated games and 50 at or above each floor, else the widest bracket as before). The pool's own hero brackets merge into the matching API brackets | **Missing** in the API. Without floors the advisor cannot pick a bracket from the rating and uses the widest one (top 50%). The page lets the player pick a bracket by hand. |
| Card stats per bracket: average place of players who played the card | **API** `GET /v1/battlegrounds/minions/stats?mmr=ALL\|TOP_50_PERCENT\|TOP_20_PERCENT&period=CURRENT_BATTLEGROUNDS_PATCH&limit=500` → `normal.avg_placement_with`, `normal.games_with_minion`. Brackets: all ranks, top 50%, top 20% | Minions only (no tavern spells). Golden numbers are not used. |
| Card stats per turn played (`turnStats`) | pool (cards with turn results) | **Missing** in the API in bulk. `/v1/battlegrounds/minions/{id}/stats` has `rounds[]`/`purchases[]` per minion, mostly null per round, 270+ requests. Tavern and Discover picks get no community placement until the pool has it. |
| Comp stats (`comp-stats`): placement, per MMR, per hero | pool (`/v1/meta` comps), beside **the site service's HSReplay comps** (section 4.1) | The external comps have **no placement and no sample** (the service sends `avgPlacement: ""`): they are ordered by the hero's own tribe-family numbers (section 4.3, top 50%) and the source's tier letter, and no comp placement is shown. |
| Comp guides (`comps-strategies`): cards core/addon, tips, when to commit, tribes, power level, difficulty | pool (`/v1/meta` guides), beside the external comps' core cards, tier letter, difficulty and advice (section 4.1) | The external comps have core cards only (no add-ons, no "when to commit") and name no tribes. Titles and advice are in Russian only. |
| Trinket stats: placement, pick rate, per MMR | pool (`/v1/stats` trinkets and, since 0.17, `trinketsByBracket`; `/v1/meta` trinkets), behind them **the site service's trinket tier list** per MMR slice (section 4.2) | The external `games` is "the minimum sample compatible with the rounded distribution", not a count: used as a lower bound and shown as one. |
| Tribe results of final boards | pool (`/v1/stats` tribes) | Shown instead of comps when the pool is on and there are no comps. |

Duos: the API has `/api/bg/heroes/duos` (top 50%), but the app's meta has no Duos dimension (Firestone's was solo too).

## 2. How the app loads it

- `BattlegroundsMeta.LoadAsync(HearthpulseData)` reads the four hero slices and the three minion slices through
  `HearthpulseData` (cache `cache/api/bg-heroes-<mmr>.json`, `bg-minions-<mmr>.json`, 6 h, stale cache when offline,
  404 cached as "no data"). `Source` = "Hearthpulse data" (`HearthpulseData.Attribution`). Percentiles and the pool's
  tribe impact stay empty.
- `BattlegroundsMeta.LoadCommunityAsync` is the order both hosts use: **API → external comps and trinkets → pool**.
  The external lists are those `BgSiteClient` loads for the tabs (`BattlegroundsMeta.LoadExternalAsync`: the comps of
  the refresh and the trinket tier list of the four MMR slices, three more cached requests) and become rows marked
  `External` (`BattlegroundsMeta.WithExternal`). The source line stays "Hearthpulse data".
- The heroes' compositions (section 4.3) are part of the API step: one more request beside the hero and minion
  slices, `GET /v1/battlegrounds/heroes/compositions?mmr=TOP_50_PERCENT`, cached like them.
- The pool's address: `PoolEndpoint` in `settings.json` when set, else `PoolClient.DefaultEndpoint` (empty until the
  owner deploys the server, `server/pool/README.md`); only `https://` addresses count. There is no field for it in the
  interface.
- When the stats pool is set up (`PoolClient.Available`), `PoolClient.OverlayAsync` loads the pool's own meta
  (`/v1/meta` + `/v1/stats`) and lays it over the API meta: hero and card numbers blend by sample size **only when the
  API row has one** (API heroes have none, so their numbers stay HSReplay's; heroes the API lacks come from the pool
  once they reach `PoolClient.MinGames`); card turn results, tribes and percentiles come from the pool when the API
  has none. The pool's comps join the external ones; a pool trinket with 50 picks or more goes in front of the
  external row of the same card (never blended with it: one is a count, the other a lower bound), and the external
  row stays behind it for a game the daily limit closes. `Source` then reads "… + Hearthpulse stats pool" - **only
  when the pool's answer holds at least one row or game**: an empty pool changes nothing and is not named.
- Without the API (offline, no cache) the pool's meta is used alone; with neither, the pages show their empty states.

## 3. What the server could add (not done; the server is not changed from here)

All read-only, `{"data": ..., "meta": {source_id, fetched_at, stale}}` like `/v1/*`:

1. **MMR floors per percentile** - `GET /v1/battlegrounds/mmr-percentiles?mode=solo|duos`
   → `data: [{"mmr_percentile": "TOP_50_PERCENT", "min_rating": 6400}, ...]`. Lets the advisor pick the player's
   bracket from the rating again.
2. **Hero tribe impact** - add to each hero of `/api/bg/heroes` (or a new `/v1/battlegrounds/heroes/tribes?mmr=`):
   `tribe_impact: [{"race": "MURLOC", "impact_avg_placement": -0.12, "games": 1234}]` (negative = better with the tribe
   in the lobby).
3. **Hero sample size** - `num_games` on each hero row. The tier list could then hide thin samples and the pool could
   blend with the API by weight.
4. **Bulk per-turn card results** - `GET /v1/battlegrounds/minions/stats/turns?mmr=&period=` →
   `data: [{"card_id": "BG25_354", "turns": [{"turn": 5, "games": 4100, "avg_placement": 3.92}]}]`, tavern spells
   included. This is what ranks shop and Discover offers.
5. **Bulk hero combat win rate** - `GET /v1/battlegrounds/heroes/combat-winrate?mmr=` →
   `data: [{"id": "TB_BaconShop_HERO_37", "rounds": [{"combat_round": 3, "data_points": 6815, "combat_winrate": 52.47}]}]`.
6. **Compositions** - `GET /v1/battlegrounds/compositions?mmr=` → `data: [{"composition_id": 9, "name": "Demons",
   "name_ru": "Демоны", "num_games", "avg_placement", "placement_distribution", "tribes": ["DEMON"], "core_cards":
   [{"card_id", "at_least_one": 0.74}], "heroes": [{"id", "num_games", "avg_placement"}]}]`. The server already
   stores HSReplay comp datasets (`hsreplay_battlegrounds_comps`, `hsreplay_battlegrounds_compositions`) without a
   route; its `firestone_battlegrounds_comps` dataset must not feed the app. Guides (tips, when to commit) stay with the
   pool or editorial content.
7. **Trinkets with real sample sizes** - `games` as counted, not estimated; the trinket pick then shows a count
   instead of a lower bound.
8. ~~Hero compositions in bulk~~ - **done on the server, 2026-11-15**: `GET
   /v1/battlegrounds/heroes/compositions?mmr=TOP_50_PERCENT`, 116 heroes in one answer (~240 KB), each composition
   with its `tribes`. The app reads it (section 4.3) and makes no request per hero. Other slices answer 422.

## 4. External data in the advisor: definitions and small-sample rules

These numbers are **external data** (HSReplay through our services), not Hearthpulse's own metrics: they are not in
`analytics-metrics.json`, which lists what the stats pool computes from players' games. In the interface their source
is "Hearthpulse data" / «Данные Hearthpulse», with the date the source gives; a number of the pool keeps the pool's
name. In Duos none of this section applies (the lists are solo): Duos keeps to the pool's rows as before.

Hidden information: everything here is lobby-wide public statistics joined with what the player sees (the offered
heroes and trinkets, the lobby's tribes); nothing about other players is read or shown.

### 4.1. Comps

- **Source.** `GET https://bg.hs-manacost.ru/api/bg-comps`, rows whose `source` is HSReplay ([bg-site-stats.md](bg-site-stats.md),
  section 4; the Firestone rows are cut before the answer is cached). Read: `key`, `title`, `tier` (S..D),
  `difficulty`, `description` (the advice), `coreCards`.
- **No placement, no sample.** The service publishes neither, so `MetaComp.AveragePlacement` is 0 = "not published"
  and no comp placement is shown. Nothing is derived from the tier letter for display.
- **Is the comp playable in this lobby?** Every core card must be able to appear: a minion whose tribes are all out
  of the lobby cannot. Tribeless cards, cards of every tribe and cards the card database does not know do not decide.
  With the lobby's tribes unknown nothing is ruled out.
- **Tribe family** (for ordering and "one comp per tribe"): the family the title leads with ("Нежить - …"); without
  one, the tribe most core cards belong to when at least two do.
- **Order on a hero option.** By the hero's average place in the comp's tribe family (4.3's `compositions[]`), when
  the hero has 40 games or more there; else by a stand-in from the source's tier letter (S = 3.6, 0.3 a tier). The
  stand-in only orders comps and is never shown. One external comp per tribe, two comps in all.
- **Shown.** The names on the hero badge; in its tooltip the tier letter, the difficulty and the advice.

### 4.2. Trinkets

- **Source.** `GET https://bg.hs-manacost.ru/api/bg/tier-lists` (`lists.trinkets`, top 1%) and
  `…/tier-lists?list=trinkets&mmr=TOP_50_PERCENT|TOP_20_PERCENT|TOP_5_PERCENT&timeRange=LAST_7_DAYS`; `source` must
  be on the allow list. Read per row: `id`, `avgPlacement`, `pickRate`, `games`, `race`; the list's `fetchedAt`.
- **Unit.** Average final place of players who took the trinket, last 7 days, per MMR slice. Brackets 50/20/5/1; the
  advisor uses the player's bracket (the widest, top 50%, while there are no MMR floors).
- **Sample.** `games` is the smallest sample that fits the source's rounded placement distribution: a **lower
  bound**. It is used as one (a true sample is at least that large) and shown as "at least N games (a lower bound)",
  never as a count.
- **Small-sample rule.** A row with no games, or without a placement, is not read. A trinket needs a lower bound of
  50 overall and 50 at the bracket; under 50 at the bracket the widest bracket's numbers are used; under 50 overall
  the trinket has no number and cannot be recommended.
- **One trinket, several rows.** The service lists a tribe-bound trinket once per tribe under one card id. The offer
  does not say which tribe it is for: the row without a tribe is used; when every row has one, their average
  weighted by the rows' lower bounds, with the smallest of them as the sample.
- **Shown.** Tier letter by our bounds (`BattlegroundsAdvisor.TierOf`), average place, pick rate, "data from
  <date>"; the source, the time and the lower bound in the tooltip. The best placement of the offer is recommended.

### 4.3. Lobby adjustment of the hero pick

- **Source.** `GET https://api.kolodahearthstone.com/v1/battlegrounds/heroes/compositions?mmr=TOP_50_PERCENT` →
  `data[]` of heroes (`id`, `compositions[]`: `composition_id`, `name`, `tribes`, `num_games`, `avg_placement`), solo,
  top 50%, current patch; `meta.source_id` is `hsreplay_battlegrounds_hero_details`. One request for every hero
  (checked 2026-11-15: 116 heroes, ten families). A composition is a tribe family of final boards ("Mechs",
  "Demons"); its tribe is the answer's `tribes` (HearthstoneJSON race keys), and for a family sent without them
  ("Aberrations" on that date) the one its id or name is known by. An answer of another slice, or one whose source
  names Firestone, is not read.
- **Definition.** For a hero `h` and a lobby `L` (the tribes the player sees):

  `delta(h, L) = (P_in − P_all) · n / (n + 150)`

  where `P_all` is the games-weighted average place over all of the hero's compositions with a known tribe, `P_in`
  the same over those whose tribe is in the lobby, and `n` the games in those. Negative = the lobby suits the hero.
  The hero's shown place is its API average plus `delta`, labelled "avg place, this lobby".
- **Missing data.** A composition whose tribe is not known leaves both averages. Rows without games or with a place
  outside 1..8 are dropped.
- **Small-sample rule.** No adjustment (0) when the hero has fewer than two known compositions or `n < 100`; the
  rest is shrunk toward zero by `n / (n + 150)`, the same prior as the pool's tribe effect. No padding, no borrowing
  from another hero.
- **Precedence.** A hero that has a pool row (`heroTribes`, from 100 games with the tribe) for a tribe of this lobby
  uses the pool's rows as before; the external adjustment is for heroes without one.
- **Limits of the claim.** A composition is the family of the *final* board, so the averages are of players who
  ended there: the adjustment says how the hero fares in the families this lobby allows, not what the tribe's
  presence causes. It is top 50% whatever the player's bracket, and it is added to the bracket's average.
- **Loading.** With the hero and minion slices (`HearthpulseData.LoadHeroCompositionsAsync`), on the hosts' meta
  load, never on the log thread. Cache `cache/api/bg-hero-comps-top_50_percent.json`, 6 h, written through a
  temporary file, without the placement distributions; a 404 or 422 is cached as "no data" (the heroes then have no
  adjustment). Offline the old file answers.
