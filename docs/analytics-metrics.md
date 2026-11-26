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
- **Patch.** A metric over source captures groups by `start.build`; `null` is its own "unknown" group. Builds are never
  mixed to reach a sample. A view over game records groups by balance patch instead: builds that change no card are
  one patch, and two balance patches are never merged ([ADR 0014](adr/0014-record-analytics-and-balance-patches.md),
  [windows](#rules-for-every-record-view)).
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
| Constructed meta, and the record views of the other areas | `mulligan.card`, `constructed.archetype`, `constructed.class`, `constructed.matchup`, `constructed.deck`, `constructed.card`, `constructed.choice`, `bg.hero.records`, `bg.trinket.records`, `bg.comp.records`, `bg.tierup.records`, `arena.class.runs` | in each [section below](#metrics-over-game-records) |

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
  30-day window (the rules do not read the game build, which older records lack); the selection by the keep decision (who keeps both, and against
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

## Metrics over game records

Game records are the main path of our own numbers ([ADR 0014](adr/0014-record-analytics-and-balance-patches.md)):
most of their fields are in the record already (this task adds the build, the Diamond 4–1 band, the final comp and
the Arena run summary), each record carries its installation for the five-installation floor, and a forget deletes
them. The Node pool computes one view per metric in `server/pool/node/records/` and answers
`GET /stats/v2/analytics/records/<view>` from the published snapshot, without recounting (route, envelope and answer
states: `docs/analytics-records.md`). Source captures stay the audited path: `bg.hero.offer_pick_placement` stays as
the capture-side check of `bg.hero.records`. `mulligan.pair_winrate` (`mull-pairs-1`, `events.js`) keeps its 30-day
window over all builds until a new rules version moves it into these windows.

Where a metric over source captures and a metric over game records describe one product, the record metric is the
main path and the capture metric names it in `mainPath`:

| Over source captures | Main path, over game records | The capture metric |
|---|---|---|
| `results.by_class` | `constructed.class` (the deck and opponent-class groups: `constructed.deck`, `constructed.matchup`) | needs fields, no screen |
| `bg.hero.offer_pick_placement` | `bg.hero.records` | implemented, the server's cross-check |
| `bg.trinket.offer_pick` | `bg.trinket.records` | needs fields, no screen |
| `bg.build.final_board` | `bg.comp.records` | needs fields, no screen |
| `arena.run.wins` | `arena.class.runs` (the draft helper stays `arena.draft.offer_pick`, over captures) | needs fields, no screen |
| `mulligan.keep` | `mulligan.card` | needs fields, no screen |

### Rules for every record view

The machine copy is `records` in the JSON (task contract K3).

- **Unit and modes.** One record of one installation: a game, or one finished Arena run (an `arena_run` record). The
  Constructed views read Ranked Standard and Wild only: Casual, Brawl, Classic and Twist are not counted (players
  behave differently there). Solo and Duos are never mixed, nor Arena and Underground.
- **Balance patches.** `server/pool/data/balance-patches.json` lists the balance patches of each line by first build:
  `constructed` (`ranked`), `battlegrounds` (`bg`, `duos`), `arena` (`arena`, `arena_run`). A build belongs to the
  entry with the greatest `fromBuild` not above it; a record without `build`, or with a build below the first entry,
  is `unknown`. The builds of one balance patch (hotfixes that change no card) are one group; two balance patches are
  never merged.
- **Windows.** T is the server's day (UTC); a record dated after T waits for its day. Newest patch of the line:
  `patch`, `7d` (day in [T − 6, T]) and `30d` (day in [T − 29, T]), both inside the patch. Previous patch: `patch`.
  Unknown build: `30d`. Only the last 90 days are read ([T − 89, T]): older patches are not published, and a long
  patch is cut at 90 days.
- **Slices.** Rank bucket (Ranked): `all`, `bronze`, `silver`, `gold`, `platinum`, `diamond`, `d4-1` (Diamond 4–1,
  from `band`), `legend`, `d4-legend` (`d4-1` and `legend` together). MMR range (Battlegrounds): `all`, `0-3999`,
  `4000-5999`, `6000-7999`, `8000+`. Coin: `any`, `first`, `coin`. A record without the field is in `all` or `any`
  only. A record counts in every slice it belongs to, so the slices of one dimension overlap and are never added up.
  A Diamond 4–1 game without its star level (game memory reading off, or an app before the `band` field) is in
  `diamond` only.
- **Thresholds.** A slice (one patch, window and query of a view) whose records come from fewer than 5 installations
  (distinct `iid`) publishes nothing: its totals (`games` or `runs`, `installs`) and `suppressed`, where the view's
  data has them, are `null`, it has no rows and its `quality` is `small_sample`, the same answer as a slice nobody
  played. In a published slice a row needs 5 installations among the records it is made of; otherwise it is not
  listed and counts in `suppressed` where the view's data has one (`constructed-classes`, `constructed-cards`,
  `constructed-choices` and `bg-tierups` have none: such a row is just absent). Each number needs 5 installations
  among the records of its own denominator, else it is `null`; with fewer than 10 in its denominator it keeps its
  counts (`num`, `den`, `n`) but its value, interval and distribution are `null`. A deck list needs 50 games
  (`minDeckGames`). A row with a `null` value is `small_sample`, otherwise `ok`. Nothing is padded or borrowed from
  another slice, window or patch.
- **Uncertainty.** A share carries a Wilson interval (z = 1.96); a mean, mean ± 1.96·s/√n, `null` below n = 2; a
  place, also the count of each place (1–8, Duos 1–4).
- **Order (`rankable`).** A share is rankable when the half-width of its interval is at most 5 points, a mean place at
  most 0.3 places, mean Arena wins at most 0.3 wins; means of turns and seconds never are. Each metric names the
  number its rows are ordered by; rows whose number is not rankable are listed below the ordered ones, by games. At 10
  games a win rate is ±26 points: enough to publish, not to rank. An order is not a test that one row beats the next.
- **Deletion.** A forget (`POST /v1/events/forget`) or the 180-day clean-up that deleted records withdraws every
  record view in the same transaction: `available: false`, `reason: "rebuilding"` until the next build, which no
  longer holds them. A maintenance pass that deleted nothing withdraws nothing. The app keeps an answer at most 6
  hours, shows no older copy while the pool is down, and deletes a copy dated in the future.
- **Display.** "Hearthpulse statistics", beside the external "Hearthpulse data", never mixed with them or filled from
  them, always with games, installations, interval, window and patch. Main window: always, with honest empty states.
  Overlay: only where a value exists, and only for the player's own deck, offers and picks. Until about 300
  installations they complement the external numbers, not replace them.
- **Hidden information.** Only record fields, which hold only what the player saw. The opponent's archetype is
  published only as an aggregate on the meta page, never during a game.

Each metric below is **defined** until the package of its view implements it. That package sets `status:
implemented`, `rulesVersion` and `api` in its own JSON block and the status line of its own section, and nothing else.

| Metric | View | Query (besides `window`, `patch`) | Package · planned rules |
|---|---|---|---|
| `mulligan.card` | `constructed-mulligan` | `fmt`, `rank`, `cls` or `arch`, `coin` | C1 · `mull-card-1` |
| `constructed.archetype` | `constructed-archetypes` | `fmt`, `rank` | C2 · `cons-arch-1` |
| `constructed.class` | `constructed-classes` | `fmt`, `rank` | C2 · `cons-class-1` |
| `constructed.matchup` | `constructed-matchups` | `fmt`, `rank`, `by` (`archetype` or `class`) | C2 · `cons-matchup-1` |
| `constructed.deck` | `constructed-decks` | `fmt`, `rank`, `cls` (optional) | C3 · `cons-deck-1` |
| `constructed.card` | `constructed-cards` | `fmt`, `rank`, `cls` | C3 · `cons-card-1` |
| `constructed.choice` | `constructed-choices` | `fmt`, `rank` | C4 · `choice-1` |
| `bg.hero.records` | `bg-heroes` | `mode` (`solo` or `duos`), `mmr` | D1 · `bg-rec-hero-1` |
| `bg.trinket.records` | `bg-trinkets` | `mode`, `mmr` | D1 · `bg-rec-trinket-1` |
| `bg.comp.records` | `bg-comps` | `mode`, `mmr` | D1 · `bg-rec-comp-1` |
| `bg.tierup.records` | `bg-tierups` | `mode`, `mmr` | D1 · `bg-rec-tierup-1` |
| `arena.class.runs` | `arena-classes` | `kind` (`arena` or `underground`) | E1 · `arena-run-1` |

### `mulligan.card`: the mulligan guide per card

Status: defined.

Ranked game records with a class and a mulligan; slices: format, rank bucket, class or the server's archetype, coin.
`kept` and `turnsHeld` count the copies of a card one by one; the win rates and `turnPlayed` count games.

| Number | Counts | Out of |
|---|---|---|
| `kept` | offered copies that were kept | copies offered in opening hands; a game keeps at most what it offered |
| `opening` | games won with the card in the hand after the mulligan (kept or received) | those games, W or L |
| `drawn` | games won in which a deck copy came into the hand at any time, the opening hand included | those games, W or L |
| `played` | games won in which a deck copy was played | those games, W or L |
| `turnsHeld` | play turn minus arrival turn over played deck copies (the opening hand is turn 0) | played deck copies with a known arrival |
| `turnPlayed` | the turn of the card's first play | games in which a deck copy was played |
| `base` | games won | the slice's games, W or L |

`delta` is `opening`, `drawn` or `played` minus `base.value` (`null` when either is), with no interval of its own: an
observed difference, not the effect of keeping, drawing or playing the card. Created cards are never deck copies;
`drawn`, `played` and the turns need a readable deck code; copies match first in, first out; turns are the player's
own. A deck copy is an approximation: a card whose id is in the list, not a tracked card. A copy of a listed card
that an effect generated counts as one, a listed card whose id changed in the hand (Corrupt or Infuse, for example)
is played as created, and a card returned to the hand, or traded into the deck and drawn again, arrives again. So
`drawn`, `played`, `turnsHeld` and `turnPlayed` are approximate; an exact count needs a new record field and a new
rules version. No slice by opponent class (eleven times the volume). Order: `opening`. Display: U1 (the archetype
card of the meta page) and U2 (the overlay's tooltip and summary, only cards with a value); the keep/replace call
and the hand estimate do not use it.

### `constructed.archetype`: the archetype tier list

Status: defined.

Ranked game records with a class; slices: format, rank bucket. `arch` is the server's archetype of the player's
deck; a deck it does not recognise, or a game without a readable code, is its class's row with `arch: null`.

- `winrate`: games of the archetype won / its games with W or L; `first`, `coin`: the same going first / with the Coin.
- `shareClass`, `shareAll`: games of the archetype / games of its class, all games of the slice.

A tie counts in games and shares, not in win rates. Order: `winrate`. Display: U1, the meta page's tier list.

### `constructed.class`: classes

Status: defined.

Ranked game records with a class; slices: format, rank bucket. `winrate`: games of the class won / its games with W
or L; `first`, `coin`: the same going first / with the Coin; `share`: games of the class / all games of the slice.
Order: `winrate`. Display: U1, the meta page.

### `constructed.matchup`: matchups

Status: defined.

Ranked game records with both classes, from the recording player's side; slices: format, rank bucket, `by`.
`winrate`: games of `a` against `b` won / those games with W or L. By class, `a` and `b` are the two classes; by
archetype, the server's archetypes of the player's deck and of the opponent (from the cards the opponent showed and
its class), and a game in which either is unknown is in the class matrix only. `a` against `b` and `b` against `a` are
different cells from different players' records and need not add up to 100 %. A matrix, not a ranking: `rankable`
marks the cells precise enough to compare. Display: U1, the meta page; the opponent's archetype appears nowhere else.

### `constructed.deck`: popular deck lists

Status: defined.

Ranked game records with a readable deck code; slices: format, rank bucket, class. A list is its key (cards and
copies; card order, hero skin and format ignored, the sideboard left out). It is published only from 50 games
(`minDeckGames`) of 5 installations (the owner's Q5), with its canonical code: re-encoded with the class's default
hero, never a code as a player sent it, never a deck name. `winrate`: games of the list won / its games with W or L;
`vsClass`: the same against each opponent class, each cell with its own thresholds; `secs`, `turns`: mean length in
seconds and in player turns (never rankable). Order: `winrate`. Display: U1, the meta page.

### `constructed.card`: cards of a class

Status: defined.

Ranked game records with a class and a readable deck code (without the list a played card cannot be told from a
created one); slices: format, rank bucket, class. `decks`: games whose list holds the card / the class's games;
`deckWinrate`: those games won / those with W or L; `played`: games in which a deck copy was played / games whose list
holds the card; `playedWinrate`: those games won / those with W or L. Shares are of games, not of distinct lists.
A deck copy is a play whose card id is in the list, so a generated copy of a listed card counts as played and a
listed card whose id changed in the hand does not: `played` and `playedWinrate` are approximate. Order: `decks`.
Display: U5, the card page.

### `constructed.choice`: choices from a source card

Status: defined.

Resolved choices (Discover and the like) in Ranked game records, by source card; slices: format, rank bucket.
`offered`: choices of the source whose options hold the card (once per choice); `picked`: those in which it was picked
/ `offered`; `winrate`: games won in which it was picked from the source / those games with W or L (a game counts
once). A choice without a source card is not counted, whatever its kind. Order: `picked`. Display: U2, the overlay's
choice hint, only options with a value.

### `bg.hero.records`: Battlegrounds heroes from records

Status: defined.

Battlegrounds game records; slices: mode (solo, Duos), MMR range. `pick`: games that picked the hero / `offered`, the
games whose offer holds it; `placement` (mean, interval and the count of each place), `top` (top 4, Duos top 2) and
`first` among the games that picked it. The offer is the record's: up to four heroes seen at the pick, rerolled ones
appended and cut at four, always with the pick; a record without one counts only in the place numbers. Unlike
`bg.hero.offer_pick_placement`, a hero rerolled away early is in `offered`. The floor of `pick` differs too: here 5
installations that were offered the hero, in `bg-hero-1` 5 installations that picked it; a cross-check of the two
paths compares only heroes with a value on both, since a value on one side and `null` on the other is a difference
of floors, not of numbers. A Duos record with a place above 4 is not counted. Order: `placement`. Display: U3, the
Heroes tab (the own block moves here from the capture snapshot).

### `bg.trinket.records`: Battlegrounds trinkets from records

Status: defined.

Battlegrounds game records with trinkets; slices: mode, MMR range. `pick`: games that picked the trinket / games whose
offers held it; `placement` and `top` among the games that picked it. A record keeps every trinket offered in the game
as one list, so the lesser and the greater offer cannot be told apart, and a trinket offered twice counts once.
Order: `placement`. Display: U3, the Trinkets tab.

### `bg.comp.records`: Battlegrounds final comps from records

Status: defined.

Battlegrounds game records with `bg.comp`, the main tribe of the final board (a tribe of at least 4 minions, else
`MIXED`); slices: mode, MMR range. `share`: games with the comp / the slice's games with a comp; `placement`, `top` and
`first` among the games with the comp. An association between ending with a comp and the place, not the comp's
effect. Order: `placement`. Display: U3, the Comps tab.

### `bg.tierup.records`: tavern tier-up timing from records

Status: defined.

Battlegrounds game records that reached tavern tier t (2–7); slices: mode, MMR range. `turn`: the mean first turn on
which the tavern reached t (never rankable); `placement`: the mean place of those games. A game that never reached t
is not in its row, so the rows are different sets of games. Rows are the tiers in order. Display: U3, the
Battlegrounds page.

### `arena.class.runs`: Arena classes by finished runs

Status: defined.

Finished Arena runs (`arena_run` records); slices: Arena kind. `share`: runs of the class / runs of the kind; `seven`:
its runs with 7 wins or more / its runs; `winrate`: wins / wins and losses of its runs (the games of one run are not
independent, so the interval is too narrow); `wins`: the mean wins of its runs. A run is sent once, when its last game
ends it (12 wins or the loss limit), with a deck id and no imported game. Known bias: an app that starts in the middle
of a run has only its later games, so that run's wins are understated, and a run retired before its end is never
sent. Order: `wins`. Display: U4, My Stats → Arena.

## Field matrix

`fieldMatrix` in the JSON lists every field a metric names, with its status and source:
`have` (written by the current recorder or compact record), `derived` (computed reliably from `have` fields), `add`
(planned for a later F1 round, absent today), `unavailable` (not visible to the player or never uploaded — the metric
stays unavailable). The `record.*` rows are the compact game record's fields (`mulligan.pair_winrate` and the record
views); `build`, `band` and `bg.comp` come from the app version with TASK-2026-11-26-pro-collection A1, and the
`run.*` fields of an `arena_run` record from E1, so older records lack them. The `derived.*` rows are what the pool
derives from a record: the balance patch, the rank, MMR and coin slices, and the deck list, its key and the two
archetypes (the `deck` and `archetypes` derivers). Source v2 added `start.format`, `start.arena` and `choice.choiceType`; the remaining `add` fields
(game and run keys, class, deck key, coin, lobby tribes, mulligan phases, combat boundary and results) are the next
round's F1 work.
