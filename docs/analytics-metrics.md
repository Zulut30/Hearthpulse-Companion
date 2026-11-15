# Analytics metrics catalog

Every number our statistics will show has one definition here before it is computed: unit, numerator, denominator,
the fields it needs, how missing data is treated, grouping, the small-sample rule and where it is shown. The machine
copy is [analytics-metrics.json](analytics-metrics.json) (catalog `version: 1`); `AnalyticsCatalogTests` keeps the two
in step and checks every field against the field matrix. A changed definition is a new metric `version`; a changed
computation of an implemented metric is a new `rulesVersion`.

## Rules for every metric

- **Unit.** One observation by one participating installation (a game capture, a draft pick, a card instance). Several
  clients' observations of the same lobby are not called unique matches: there is no safe way to join them.
- **Fitness.** A metric names the per-metric eligibility it needs ([ADR 0013](adr/0013-per-metric-eligibility.md));
  `completeForAggregation` is not loosened for it. An observation that is not fit is left out of both numerator and
  denominator.
- **Patch.** The group is `start.build`; `null` is its own "unknown" group. Builds are never mixed to reach a sample.
- **Small samples.** Shown honestly as missing (`null` with `small_sample`), never padded. Every published number
  covers games of at least 5 installations (`owner`, the hash of the install id), counted among the observations that
  number is made of; below that the number is `null`. Above it, a value with fewer than 10 in its denominator keeps
  its counts but its value, interval and distribution are `null`. Server rules: [analytics-api.md](analytics-api.md).
- **Uncertainty.** Shares carry a Wilson interval (z = 1.96); means carry mean ± 1.96·s/√n and are `null` below n = 2.
- **Hidden information.** Only what the local player saw: their own offers, picks, results and places, the public
  format, Arena kind and build. No names, BattleTag, account, game or deck ids.

## Areas and metrics

| Area | Metric | Status |
|---|---|---|
| Game results | `results.winrate`, `results.ties`, `results.duration` | defined |
| Game results | `results.by_class` | needs fields |
| Battlegrounds places | `bg.placement`, `bg.duos.team_placement` | defined |
| Heroes and trinkets | `bg.hero.offer_pick_placement` (`bg-hero-1`) | **implemented** |
| Heroes and trinkets | `bg.trinket.offer_pick` | needs fields |
| Shop and discovers | `bg.shop.actions`, `bg.discover.offer_pick` | defined |
| Shop and discovers | `bg.shop.offer_pick` | needs fields |
| Economy | `bg.economy.actions` | defined |
| Economy | `bg.economy.gold_left` | needs fields |
| Combats | `bg.combat.round_result`, `bg.combat.sim_vs_actual` | needs fields |
| Builds | `bg.build.final_board` | needs fields |
| Arena | `arena.draft.offer_pick` | defined |
| Arena | `arena.run.wins` | needs fields |
| Mulligan | `mulligan.keep` | needs fields |
| Mulligan | `mulligan.pair_winrate` (`mull-pairs-1`) | **implemented** |

Statuses: `implemented` — computed end to end; `defined` — every field exists (`have`/`derived`), no computation yet;
`needs_fields` — a field is `add` (a later F1 round) or `unavailable`, so the number does not exist yet.

## The implemented metric: `bg.hero.offer_pick_placement`, rules `bg-hero-1`

Unit: one Battlegrounds solo game capture. Window: `start.day` in [today − 30, today]. Group: `start.build`.

- `offered(h)` — captures fit for `offers` whose hero offer contains `h` (once per capture).
- `picked(h)` — of those, captures whose pick is `h`; `pickRate = picked / offered`.
- `placement.n(h)` — captures fit for `offers` and `outcome` whose pick is `h`; `mean` place; `dist[0..7]` counts places
  1..8; `top4 = #(place ≤ 4) / n`; `first = #(place = 1) / n`.
- Every number needs 5 distinct installations (`owner`) among its own captures: the hero row — installations it was
  offered to (else it is not listed, only counted in `suppressed`); `picked`/`pickRate` — installations that picked it;
  `placement`/`top4`/`first` — installations that picked it and have a place. Below that the number is `null`.
- Above that, a value with fewer than 10 games in its denominator keeps `num`/`den` (`n`), but `value/low/high`
  (`mean`) and `dist` are `null`; a row is `small_sample` when any of its values is `null`.
- Limit: the offer is the one at the moment of the pick. Heroes rerolled away are not in `offered(h)`, so their offer
  frequency is understated; that is a separate metric later.

Source: result v2 facts `offers[kind = bg_hero]` and `outcome.placement` (see the
[CLI contract](../tools/Hearthpulse.SourceWorker/README.md#source-v2-and-result-v2)); the server re-checks both
against the source rows. API: `GET /stats/v2/analytics/bg/heroes?mode=solo`.

## The implemented metric: `mulligan.pair_winrate`, rules `mull-pairs-1`

Opening-hand card pairs (plan 2.4.2). Unlike the metrics above it is computed from the **compact game records**
(`/v1/events`, table `e`, [game-stats.md](game-stats.md)), not from source captures, so its fields are the
`record.*` rows of the field matrix and it needs no eligibility.

Unit: one game record of one installation. Sample: Ranked, Standard or Wild, with a class, a result `W` or `L` and a
mulligan; `day` in [today − 30, today], both ends included (a record dated tomorrow by a clock that runs ahead is
stored and waits a day). Group: format × class.

**What it is.** An observed association: how the games went in which players kept both cards, against the games in
which they kept one or neither. It is not a causal effect of keeping them and not a proven gain, and the texts say
"in games where both were kept the win rate was …", never "keeping both wins …".

- For every pair of **different** cards `a < b` offered together in one opening hand, the record falls into one of
  four disjoint cells by what was kept: `both`, `onlyA`, `onlyB`, `none`. Each cell is `[games, wins]`.
- The pair's own number is `both`: numerator wins, denominator **games where both cards were kept**.
- The comparison cells are the pair's own base numbers: every game in them had both cards on offer, so it comes from
  a deck that holds both. The class's totals are deliberately not the base: two cards of one strong archetype both
  win more than the class does, and "pair win rate − (class + card a + card b)" would call that an anti-synergy.
- Pair difference = `p(both) − p(onlyA) − p(onlyB) + p(none)`: how much better the games with both kept went than
  the games with each kept alone add up to.
- **Thresholds.** A pair is listed from 200 games in `both` of at least 5 installations (distinct `iid`), else it is
  absent. A comparison cell under 5 installations or under 10 games is `null`. At most 200 pairs a group, most
  played first. Nothing is padded or borrowed from another group.
- **Uncertainty.** The cells are disjoint, so the nominal 95 % interval of the *difference* is
  `± 1.96 · √(Σ p(1 − p) / n)` over the four cells (each share smoothed by one win and one loss). The app shows a
  pair only with all four cells, 30 games or more in each comparison cell, a nominal interval that excludes zero and
  a difference of at least 1.5 points (`MulliganGuide.PairEffect`). Two separate intervals of two win rates are not
  this test. This is a filter against noise, not a proof.
- **The interval is nominal and too narrow.** It leaves out: repeated games of one installation (counted as
  independent, because only counts are published; `installs` is published beside them); the patches mixed in the
  30-day window (records carry no game build); the selection by the keep decision (who keeps both, and against
  what, is the player's choice); and the many pairs looked at with no correction. The app marks it "approximate".
  A pair everybody keeps has no comparison cells and therefore no value.
- **Deletion and withdrawal.** `POST /v1/events/forget` and the clean-up delete the published table in the same batch
  as the records and raise the deletion fence: row `events-rev` of `snap` counts deletions, a build notes it before
  it reads the records and publishes (one batch, summary and pair table together) only if it has not moved;
  otherwise it publishes nothing and counts again from the records that are left, three times at most, then the
  read answers `503`. So a forget that lands between a build's reads and its write is never published by that build,
  on D1 as in the Node service. The app keeps an answer for 6 hours and keeps **no older copy** for the time the
  pool is down (a known withdrawal cannot be promised and an older answer shown); an expired copy, or one dated in
  the future after a clock change, is deleted before the pool is asked. Pairs in memory are looked up again every
  5 minutes of a mulligan, and dropped when the pool no longer publishes them or its address changes.
- **Display.** The mulligan panel of both apps: the flag on both cards, and in the card tooltip and the summary
  details the observed difference, its approximate interval, the games with both kept and the label "Hearthpulse
  statistics" (our own numbers; never "Hearthpulse data", which names the external ones). The difference is
  **shown only**: it does not enter the keep/replace call or the estimate of the hand, which are what they were
  before this metric existed (docs/mulligan-v2.md).

API: `GET /v1/events/mulligan-pairs?fmt=standard|wild&cls=<CLASS>` (both pool entrypoints; `server/pool/README.md`).
Status on 2026-11-15: computed end to end in code and tests; the production pool holds no records, so the route
answers an empty list and no pair line is on any screen.

## Field matrix

`fieldMatrix` in the JSON lists every field a metric names, with its status and source:
`have` (written by the current recorder or compact record), `derived` (computed reliably from `have` fields), `add`
(planned for a later F1 round, absent today), `unavailable` (not visible to the player or never uploaded — the metric
stays unavailable). The `record.*` rows are the compact game record's fields (`mulligan.pair_winrate`). Source v2 added `start.format`, `start.arena` and `choice.choiceType`; the remaining `add` fields
(game and run keys, class, deck key, coin, lobby tribes, mulligan phases, combat boundary and results) are the next
round's F1 work.
