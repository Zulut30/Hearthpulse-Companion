# Arena draft helper

Backlog A2 (data and card scores), A1 (draft helper on the overlay), W2 (community Arena numbers in the app). Both UIs.

## What the player sees

- **Draft screen, overlay panel `arena-picks`**: a badge over each of the three offered cards with its ArenaSmith
  score and tier colour; the highest score gets a gold border, a star and "Best pick". Under the score the badge says
  where the card would sit in the deck: its cost and how many cards of the deck share that curve bucket
  ("3 mana · 4 in deck"), and its type with the deck's count of that type ("Spell · 5 in deck"). A legendary that
  brings a package (Underground) is scored as the average of the whole package and says "+2 cards with it". From
  the sixth pick on, a scored card may get two more lines, our own adjustment for the deck so far and why
  ("Deck fit: −2" / "Many cards at 5 mana · Few minions", see Deck fit); the big number stays the source's score. At the
  hero pick the badges show the class win rate (tier from the win rate), the class's place among all classes and the
  7+ wins share; the best class is marked. At the hero power pick there are no badges (no numbers exist for it).
- **Overlay panel `arena-draft`** (left edge): run class (or "Choose a hero" / "Choose a hero power"), the run's mode
  ("Underground Arena" / "Normal Arena") once the client told it, picks `n/30`, average ArenaSmith score of the deck so
  far with its tier, mana curve 0..7+ with the buckets of the offered cards lit, the deck's card types
  ("12 minions · 5 spells · 1 weapon"), class win rates at the hero pick (the offered classes in gold), after it the
  run class's own line under the title ("Win rate 55.2% · #1 of 11 classes · 7+ wins: 15%"), the cards
  picked by cost with their scores. Attribution line "Scores: Hearthpulse data"; on a normal-Arena run a second line
  says the scores come from Underground Arena games (the only slice the server has, see Data).
- **Arena page** (main window, My Stats Arena): each run row shows "Deck score N" when the run saved a deck code; a
  "Deck score" card for the latest run (average, tier, curve, three strongest and weakest cards); a "Class win rates"
  card with the community numbers.
- Setting: `AppSettings.ShowArenaDraft`, **beta, off by default**; Settings → Tracker in both apps, labelled
  "(beta)". Off, the draft screen is not read from memory at all (`GameSession.SetArenaDraftEnabled`, hosts call it
  next to `SetCardPoolEnabled`); Arena.log is still followed so turning it on mid-draft knows the deck. While it is a
  beta the tracker logs to `hearthpulse.log` every Arena.log line it recognized with the resulting state
  (`Arena draft: recognized "..." -> hero ..., hero power ..., deck ..., N cards, mode ...`), every change of the
  offer read from memory (`Arena draft: offer read from memory: A, B (+P Q), C; Underground Arena` /
  `no offer read from memory`) and the package cards added after a pick: compare them with the screen.
  In the UI the numbers are credited to "Hearthpulse data" only. Both panels hide outside the `DRAFT` scene and take
  no clicks (`IsHitTestVisible="False"`, no `OverlayHit.Interactive`), so the cards stay clickable. Nothing is chosen
  or clicked for the player: the helper only shows numbers.

## Two Arenas (owner, 2026-11-07)

- A game's Arena is its logged game type: `GT_UNDERGROUND_ARENA` is Underground Arena (seen in a real game,
  2026-10-28), anything else with ARENA the regular Arena (`ArenaStats.KindOf`). History's `game_type` column already
  keeps it, so no new column: rows saved before the split keep what the log said. A run never mixes the two; Underground
  ends at 5 losses, regular at 3.
- The Arena page has the tabs "Arena" / "Underground Arena": stats, runs, the deck score and the class win rates per
  Arena. Card scores of the other Arena's slice score no deck (the card says whose scores there are). The community class
  win rates (`v1/arena/classes`) have no Underground split: the Underground tab says "no data", and an Underground draft
  shows no class numbers at the hero pick (`ArenaDraft.Build`).
- History filters "Arena" and "Underground" (Other holds neither); rows read "Underground Arena".

## Data (Hearthpulse data API, no token)

| Call | Used for | Cache |
|---|---|---|
| `GET v1/arena/cards?fields=id,dbfId,score,tier,cardClass,win_rate` | The card scores (since 2026-11-15). `data[]`: `id`, `dbfId`, `score` (ArenaSmith, or `null`), `tier`, `cardClass`, `win_rate` 0-100; `meta`: `count`, `limit` (5000 at most), `offset`, `game_type` (`underground` / `normal`), `time_range` (`LAST_4_DAYS`), `fetched_at` | `cache/api/arena-cards-route.json`, 6 h, the answer as it is (about 115 KB); a further page, if the list ever outgrows one, in `arena-cards-route-<n>.json` |
| `GET datasets/hsreplay_arena_cards_advanced` | The same scores, read **only when the route gave no card** (not deployed: its 404 is cached like an answer; offline with no copy of the route). `data.structured.cards[]`; the slice's game type from `structured.source.api_url` (`ArenaGameTypeFilter=BGT_UNDERGROUND_ARENA`) | `cache/api/arena-cards.json`, 6 h; trimmed (the answer is about 1.7 MB, the list is published twice). The file of a version before the route still serves offline |
| `GET v1/arena/classes?limit=20` | `data[]`: `class`, `win_rate`, `pick_rate`, `pct_7_plus`, `num_drafts` | `cache/api/arena-classes.json`, 6 h |
| `GET datasets/hsreplay_arena_legendaries` | Legendary packages of the Underground Arena: `data.structured.groups[]` with `key_card.id` and `cards[]` (`id`, `count`). Used only after a pick (Legendary packages below) | `cache/api/arena-legendaries.json`, 6 h; trimmed to the key card and its cards' ids (the answer is about 580 KB) |

- Checked live 2026-11-15 (read-only GET): the route answers 1218 cards in one page, 146 of them with `score: null`;
  `class=MAGE` filters by card class (83 rows); an unknown field is a 422 naming the allowed ones (`id, dbfId, name,
  cost, type, rarity, cardClass, arena_class, tier, score, win_rate, deck_winrate, winrate_when_drawn,
  winrate_when_played, popularity, in_runs, avg_copies, times_played, pick_rate, offer_rate`); `game_type=normal` is
  ignored (the answer stays `underground`). Every row is the all-classes slice (`arena_class: ALL`): **no score by
  the player's class exists**, a neutral card has one score whatever class drafts it. The legendaries dataset: 133
  groups, one per key card (112 with three cards, 19 with two, 2 with one).
- **A card without a score** (`score: null`: too few games) is shown without one: "—" and "No score yet" on the
  badge, "—" in the deck list, not counted in the deck's average, never the best pick. It is not a 0. The `tier`
  the route sends is kept (`ArenaCardScore.SourceTier`) but not shown: it follows the win rate, also for a card with
  nine games and no score (TLC_231: `tier` S, `score` null), while the badge's letter is the score's
  (`ArenaMeta.TierOf`, the site's thresholds).
- `dbfId` is asked beyond the fields the roadmap names: a deck code's card the card data does not know yet is found
  by it (`ArenaMeta.Card(dbfId)`). The route does not carry pick rate, games and drawn / played win rates; nothing in
  the app showed them (`ArenaCardScore.PickRate`, `Games`, `DrawnWinRate`, `PlayedWinRate` stay filled on the dataset
  path only).
- `ArenaMeta` keeps what the answer says about itself: `Underground` (the slice), `TimeRange` and `FetchedAt` (the
  date of the data). The date line in the draft panel belongs to the source-freshness package, not to this one.
  When the source's Arena differs from the run, the slice note is visible under the mode in the draft panel,
  as well as in the header tooltip. `HEARTHPULSE_RENDER_ONLY=arenadraft` renders the draft overlay scenes alone
  (WPF also writes the Arena page scenes), on a copied data folder with the game closed.
- Earlier checks (2026-10-25, 2026-10-26, the dataset): 1219 cards (147 without a score), 11 classes; the server
  builds the slice from `ArenaGameTypeFilter=BGT_UNDERGROUND_ARENA`, `LAST_4_DAYS`. `structured.by_class` holds only
  card counts per class (DEATHKNIGHT 355, DRUID 336...), no scores.
- **Legal status**: the ArenaSmith scores are HSReplay numbers scraped by the server; the legal report
  `LEGAL-HeartLens-card-choices.md` (L-B1, L-M2) puts them under the same HearthSim ToS risk as the other HSReplay
  data. **Owner's decision 2026-10-27**: "for Arena use the ArenaSmith scores from our API and the class win rates";
  the owner accepted the legal risk of the HSReplay data in the API for both endpoints. No further HSReplay source is
  added for Arena; HearthArena stays unused.
- Tier letters follow the site (`server/hsreplayArenasmith.ts` of hearthpulse.net): S 53+, A 45+, B 35+, C 24+,
  D 10+, else E. Class win-rate tiers: S 54%+, A 50%+, B 46%+, C 42%+, D 38%+, else E (`ArenaDraft.TierOfWinRate`).
- Code: `HearthpulseData.LoadArenaMetaAsync` (`Api/HearthpulseData.Arena.cs`: `LoadArenaRouteAsync`, then the
  dataset; `ParseArenaRoute`, `ParseArenaLegendaries`) -> `Arena/ArenaMeta` (`Underground` = the slice's mode,
  `TimeRange`, `FetchedAt`, `PackageOf`). Network failures and timeouts leave empty parts; explicit caller cancellation
  propagates. Hosts load it with the community stats (`App.Arena.cs`, `AppHost.Arena.cs`) and pass it to
  `GameSession.SetArenaMeta` and `MainViewModel.SetArenaMeta`. Offline the stale cache is used; with nothing at all the
  panel says so and the deck and curve still work.

### What the server has (`GET v1/sources?category=arena`, 2026-10-26)

| Source | What it holds | Used |
|---|---|---|
| `hsreplay_arena`, `hsreplay_arena_class_pages_firecrawl` | class win rate, pick rate, 7+ share, drafts (what `v1/arena/classes` serves; `source_id` picks one of them) | yes, via `v1/arena/classes` |
| `hsreplay_arena_cards_advanced` | ArenaSmith score per card, Underground, all classes | yes |
| `hsreplay_arena_legendaries` | 133 legendary packages (`groups[]`: `key_card`, `cards[]`), Underground | after a pick only, as a stand-in when the offer was not read (Legendary packages below); never on a badge |
| `hsreplay_arena_winning_decks` | winning decks (12 MB) | no |
| `heartharena_tierlist` | **per-class card scores**: 11 classes + `any`, about 355 cards each, `score` 1..145, `tier_id` terrible..great; 178 of 316 neutral cards score differently by class | no, waiting for the owner's decision: a scrape of a commercial site whose terms of use were not checked; the same legal question as the HSReplay data (`reference/reviews/LEGAL-HeartLens-card-choices.md`, L-B1/L-M1). Check HearthArena's ToS before wiring it |
| `firestone_arena_cards_normal`, `firestone_arena_cards_underground`, `firestone_arena_legendaries_*` | the only **normal-Arena** card slice, and Underground | never: Firestone data has no permission (hearthpulse-data-sources) |

### Endpoints the server does not have yet

- **Per-class card scores from a source we may show**: a neutral card has one score for every class today
  (HearthArena has them, see above).
- **Normal Arena vs Underground** from a permitted source: the published HSReplay slice is Underground only; a
  normal-Arena slice (or a `mode` parameter) would let the badges match the run. The panel says which slice it shows.
- A small route for the legendary packages (key card and card ids): the app trims the 580 KB dataset itself.

The small cards route asked for here exists since 2026-11-15 (`v1/arena/cards`, above).

## Deck fit (our own adjustment, 2026-11-15)

A source score says how good a card is on average, not what this deck still needs. `Arena/ArenaDeckFit.cs` adds a
small adjustment from the card data alone (cost, type, name). It is our own rule: no synergy table and no number of
HDT, Firestone or HearthArena was read for it, and it is no statistic of the data API.

- **Shown apart.** The badge's number, its tier letter and "Best pick" are the source's and do not move. The
  adjustment is a line of its own under them ("Deck fit: −2", `Arena.DeckFit`), rounded to half-point steps with no
  signed zero, with the two strongest reasons on
  the next line. `ArenaPickOption.Score` stays the source's score, `ArenaPickOption.Fit` (`ArenaFit`: `Points`,
  `Parts`) carries ours.
- **When.** For a card offer, from five known cards in the deck on (`MinPicks`: the first picks take the strongest
  card). Not for a card without a score (nothing to adjust), not for a legendary shown with its package (its number
  is the package's average), not for heroes and hero powers, not for a card the card data does not know.
- **Limit.** Each term is at most 2 points, the sum is kept within ±4 (`ArenaDeckFit.Limit`) on the 0-60 score scale.
- **Target curve** of a 30-card deck by cost slot, set by us (a plain tempo curve: the bulk at 2-4 mana):

  | Cost | 0-1 | 2 | 3 | 4 | 5 | 6 | 7+ |
  |---|---|---|---|---|---|---|---|
  | Cards of 30 | 3 | 7 | 6 | 5 | 4 | 3 | 2 |

- **Rule**, with `n` known cards in the deck (hero powers are no cards; unknown cards contribute neither to `n`
  nor to the type and cost counts) and `share = (n + 1) / 30`:

  | Term | Condition | Points | Reason on the badge |
  |---|---|---|---|
  | Curve, crowded | at least 2 cards already in the slot; `d = (cards in the card's slot + 1) − target × share`, `d ≥ 1` | `−min(d, 2)` | "Many cards at N mana" |
  | Curve, thin | `d ≤ −1` | `+min(−d, 2)` | "Few cards at N mana" |
  | Minions | the card is a minion and `minions × 2 < n` | +1 | "Few minions" |
  | Early minions | the card is a minion costing 3 or less and `(minions costing ≤ 3 + 1) − 8 × share ≤ −1` | +1 | "Few early minions" |
  | Spells | the card is a spell and `spells + 1 > 0.4 × (n + 1)` | −1 | "Many spells" |
  | Weapons | the card is a weapon and the deck has 2 or more | −1 | "Enough weapons" |
  | Copies | `k ≥ 2` cards of the same name in the deck (a CORE reprint is the same card) | `−min(k − 1, 2)` | "k copies already" |

  Between −1 and +1 the curve term is nothing, so a card on the curve gets no line. Locations and hero cards have
  the curve and copies terms only.
- **Example.** 14 cards taken: ten 5-cost spells and four 6-cost minions; a 2-cost minion with a source score of 38
  is offered. `share = 15/30 = 0.5`. Curve: slot "2" has 0, so `d = 1 − 7 × 0.5 = −2.5`, +2. Minions: `4 × 2 < 14`,
  +1. Early minions: `1 − 8 × 0.5 = −3`, +1. Sum +4. The badge reads "38" and under it "Deck fit: +4" /
  "Few cards at 2 mana · Few minions". The same spell offered an eleventh time: slot "5" `11 − 4 × 0.5 = 9`, −2;
  spells `11 > 6`, −1; ten copies, −2; sum −5, shown as "Deck fit: −4".
- Tests with the sums worked out by hand: `ArenaDeckFitTests`; both hosts and both languages:
  `ViewModelContractTests.Overlay_arena_badges_show_the_deck_fit_apart_from_the_score_in_both_hosts`.
- Not done on purpose: the best pick is still the highest source score (the adjustment only informs); tribes,
  card text and class are not read; the target curve is the same for every class.

## Legendary packages and L-M1 (decision, 2026-11-15)

The roadmap asks to read `hsreplay_arena_legendaries` "to show the package's average when the client did not reveal
the package". L-M1 (legal report `LEGAL-HeartLens-claude-arena-draft-v1.md`) forbids exactly that: before the pick a
badge may name only what the client itself put on screen. So:

- **Before the pick nothing changes and the server's list is never used.** The badge's package ("+N cards with
  it") and the package's average come only from the offer read from the client (`m_packageCardIds`), as before; that
  rule and its open check (item 2 of the list below) stand. With no package in the offer, the badge shows the
  legendary's own score and no package, although `ArenaMeta.PackageOf` knows one. Pinned by
  `ArenaDataRouteTests.The_servers_package_list_is_never_shown_on_an_offer`.
- **After the pick** the server's list stands in for the client's only where the client's is missing and the list
  cannot be wrong about what was on screen (`GameSession.AddListedPackage`): the pick was logged live (not while
  catching up), its offer was **not** read from memory, the run is known to be Underground, it is the first draft
  (revision 0) and the pick is the deck's **first card** - the legendary with its package, as in the real log of
  2026-10-28. An offer that was read is the truth also when it brought no package (a legendary offered later in the
  draft comes alone). The cards are added to the deck once, `hearthpulse.log` says where they came from
  (`... (the server's package list: the offer was not read from memory)`), and the client's deck list and the game's
  deck still correct the result.
- The dataset's own `score`, `winrate`, `pick_rate` of a group are not read or shown; no package average is
  computed from the server's list.

## Draft state

- **Arena.log** (`[Arena]` is in `log.config`; `LogWatcher` tails it, also when the file appears mid-session):
  `ArenaDraftTracker` reads the client's DraftManager lines: `DraftManager.OnChosen(): hero=` (new run), `DraftManager.OnChosen(): heroPower=` (the hero power pick, new in the
  current client), `DraftManager.OnChoicesAndContents - Draft Deck ID: {0}, Hero Card = {1}` (the deck is listed again
  after it), `DraftManager.OnChoicesAndContents - Draft deck contains card {0}`, `Client chooses: {0} ({1})` (a pick)
  and `SetDraftMode - ` with the modes `NO_ACTIVE_DRAFT` (clears), `DRAFTING`, `ACTIVE_DRAFT_DECK`, `IN_REWARDS`,
  `REDRAFTING`, and `DraftManager.OnBegin - Got new draft deck with ID: {0}` (a new run and its deck id, before the
  hero; the same id again, back on the screen, keeps the run). **Seen in a real Arena.log** (owner, 2026-10-28, Underground, fixture
  `tests/Hearthpulse.Tests/Fixtures/Arena/arena-2026-10-28.log`, `ArenaDraftLiveLogTests`; the truth is the deck code
  of Decks.log's "Starting Arena Game With Deck"):
  - a legendary taken with its package is logged twice in a row (`Client chooses` of the same card): one pick, so the
    card and its package are not counted twice. Told by the card's rarity, by the package the first line brought, or,
    without the card data, by the repeat alone (then a common taken twice in a row counts once: one card short beats a
    package counted twice); with the card data a common taken twice in a row is two picks;
  - the client's deck list (`Draft deck contains card`) is not always whole: 27 of 30 cards, copies missing. It adds to
    the picks of the same revision card by card (the larger count) instead of replacing them; without picks in this
    session (the app opened after the draft) it is the deck as listed, marked `Incomplete` when short of 30;
  - no `OnChosen(): heroPower=` line in that run.
- Heroes, hero powers and cards are told apart by the card data (`HERO` not collectible, `HERO_POWER`, anything else;
  a collectible hero card is a card), else by the id (`HERO_08`, `HERO_08bp`): `ArenaDraftTracker.KindOf`. A hero
  power is never counted as a deck card. A hero chosen after cards were taken, or another hero than the current one,
  starts a new run even when its `OnChosen` line is missing (review M1: the old deck carried over).
- **The offer is not in the logs**: `HearthstoneMemory.GetArenaDraftChoices` reads `DraftDisplay.s_instance.m_choices`,
  each choice's `m_cardID` and `m_packageCardIds`, and `m_choicesAreUnderground` (read only, only while the scene is
  `DRAFT` and the helper is on, every second; a choice without a card id means the offer is being dealt and nothing
  is shown). The values are not yet seen on a live draft. The offer is face up on the player's screen: no hidden
  information - to be confirmed for a legendary's package (below).
- After a pick the offer stays in memory for a moment: `GameSession.Arena.cs` hides the offer that **holds the chosen
  card** until a different one arrives. When the memory read already has the next offer as the log line arrives, that
  offer stays (no blank pick); when no offer was known yet, the first one read that holds the chosen card is the
  stale one. The cards of a picked package are added to the deck right away (the log names only the clicked card).
- **The run's Arena** (`ArenaDraftState.Variant`: `normal` / `underground` / null) is learned from the offer on screen
  (`m_choicesAreUnderground`) and from the game type of a game of the run (`GT_UNDERGROUND_ARENA` / `GT_ARENA`, only
  when the game's Decks.log deck id is the run's). A new run (a new `OnBegin` deck id, a new hero, `NO_ACTIVE_DRAFT`)
  forgets it, so one run's offer or game never sets another's. `ArenaDraft.Build` scores with the slice of the run's
  Arena; the server has one slice (Underground), so a normal run keeps the slice note and an unknown run gets none.
- **Revisions and the redraft** (A1, 2026-11-09; rule, not yet checked on a live log: no Arena.log with `REDRAFTING`
  exists; edge cases covered 2026-11-15, see the end of this item). `SetDraftMode - REDRAFTING` (a change into it; the line repeated is the same redraft) starts revision r+1
  (`ArenaDraftState.Revision`): the deck so far is dropped, the client's next list (`Draft Deck ID` + `Draft deck
  contains card`) is the revision's base, and only the picks of this revision are added to it (the larger count per
  card). Picks of earlier revisions never come back: no "max" merge with them, which would return the removed cards.
  Until that list arrives the deck is the redraft's picks only and `Incomplete` is true; a list short of 30 after
  the client says the deck is ready (`ACTIVE_DRAFT_DECK` / `IN_REWARDS`) stays short (n/30) with `Incomplete` true.
  Fixture `Fixtures/Arena/redraft-synthetic.log` (synthetic, real line formats), `ArenaDraftRedraftTests`.
  Edge cases read from the code and pinned by tests (2026-11-15, behaviour unchanged): the mode line repeated is the
  same revision; leaving the screen in the middle of a redraft and coming back (`OnBegin` with the same deck id,
  `REDRAFTING` again, the client's list) keeps the run and the revision, and the list is the base to which only later
  picks are added, each pick once; a log cut in the middle of a redraft read twice gives the same unfinished state;
  `NO_ACTIVE_DRAFT` in the middle of a redraft leaves nothing of the run; a new run after a redraft (a new `OnBegin`
  deck id, or a new hero without it) starts at revision 0 with its own cards. Seen while reading the code, left as it
  is: a new hero whose run logged no mode line yet keeps the old run's mode (`ACTIVE_DRAFT_DECK`), so the deck reads
  as short until the client's `SetDraftMode - DRAFTING` line; and a mode that goes `REDRAFTING` -> another mode ->
  `REDRAFTING` counts as a second redraft. Whether the client ever writes either is for the live redraft to show.
- **The game's deck is the truth.** When a game of the run starts (`GT_ARENA` / `GT_UNDERGROUND_ARENA`, game under
  way), the deck Decks.log announced for it (`Starting Arena Game With Deck`) is compared with the tracked one, once
  per game and per announcement; a difference replaces the tracked deck (pick order kept for the cards both have),
  `Incomplete` turns false and `hearthpulse.log` gets one line with the two counts and no deck id
  (`Arena draft: the game's deck differs from the tracked draft (27 cards tracked, 30 in the game); using the game's
  deck`). A deck of another deck id is another run's and changes nothing; a finished game read again after a restart
  is not applied, so it cannot undo a later redraft.
- **Restart**: the draft state is not saved; the app rebuilds it by reading Arena.log again (the same log read twice
  gives the same deck), and after a game restart from the client's list and the next game's Decks.log deck.
- Out of scope here: Discover scoring in Arena games (A5), card synergy by text or tribe (A3), the pick advice (A2).

## Before the beta label goes

Done without live drafts (package R1 of `docs/roadmaps/2026-11-15-hsreplay-remediation.md`, 2026-11-15): the small
cards route with the dataset as a fallback, unscored cards shown as unscored, the slice and the date of the data kept
in `ArenaMeta`, the deck-fit adjustment with its reasons in both UIs, the decision on the server's legendary
packages, the redraft's edge cases under test. **Still open, all of it waiting for the owner's two live drafts**: items
1-4 below. Until then `AppSettings.ShowArenaDraft` stays off by default and its switch stays labelled "(beta)".

1. One real draft of each mode with the helper on: `hearthpulse.log` lines match the screen (hero, hero power,
   each offer, the package of a legendary, the mode), picks reach 30, the deck matches the client's list.
2. **The package cards are on screen before the pick** (legal report `LEGAL-HeartLens-claude-arena-draft-v1.md`,
   L-M1): the badge scores the whole package and says "+N cards with it". If the client shows the package only after
   the pick (or only part of it), use `m_packageCardIds` only once the pick is made, never on the badge.
3. Done for Underground (2026-10-28, above); still to do for a normal Arena run (owner's checklist,
   `reference/tasks/TASK-2026-11-02-arena-live/checklist-owner.md`). Keep that Arena.log as a test fixture in place of the synthetic lines; name the log, not anything else, as the
   source of the line formats.
4. The owner decides on turning the helper on by default (the HSReplay data is decided: Legal status above; HearthArena
   stays unused).

### The owner's live check (two sittings)

Before both: Settings → Tracker → "Arena draft helper (beta)" on; the app running before Hearthstone opens the
Arena screen; screen recording on (the package question, item 2, is answered by the recording).

**A. Normal Arena, one draft.**

1. Open the Arena, start a new run. At the hero pick: three badges with class win rates over the heroes, the panel
   on the left says "Choose a hero".
2. Pick the hero, then the hero power if the client offers one (no badges there, the panel says so).
3. Draft all 30 cards at an ordinary pace. On every offer look at the three badges: a number or "—", the mode line
   "Normal Arena" in the panel, and from the sixth pick on, on some cards, "Deck fit" with a reason. Take at least
   one card twice in a row if it is offered twice.
4. At 30/30 stay on the deck screen for a few seconds, then start one game of the run and play at least to the
   mulligan.
5. Send `hearthpulse.log`, the session's `Arena.log` and `Decks.log`
   (`<Hearthstone>/Logs/Hearthstone_<date>/`), and the recording.

What is compared: each `Arena draft: offer read from memory: ...` line with the three cards on screen at that
moment; `normal Arena` in those lines; the pick count reaching 30; the tracked deck against the deck code of
Decks.log's "Starting Arena Game With Deck"; no `the game's deck differs from the tracked draft` line.

**B. Underground Arena, one redraft.** In a run that already has a deck (a new Underground run drafted with the
helper on, or the current one):

1. On a new run's first card pick, before clicking: does the client show the cards that come with each legendary
   (hover it too)? This is item 2; the recording answers it. Note what the badge said ("+N cards with it").
2. Play until the client offers the redraft after a loss. Start it, take the offered picks one by one. Once, in the
   middle (after two picks), leave to the main menu and come back to the Arena screen, then finish the redraft.
3. Stay on the deck screen for a few seconds, start one game, play to the mulligan.
4. Send the same three logs and the recording.

What is compared: a `SetDraftMode - REDRAFTING` line exists and how often it repeats; which lines follow the
return to the screen; whether the client lists the deck (`Draft deck contains card`) after the redraft and how
whole the list is; the deck after the redraft against the game's deck code (the removed cards gone, the new ones
in once); whether a package legendary is logged twice again.

**What becomes a fixture.** From A: `Arena.log` whole as `tests/Hearthpulse.Tests/Fixtures/Arena/arena-normal-<date>.log`
with the deck code of Decks.log as the truth (a second `ArenaDraftLiveLogTests` case beside the Underground one),
and the offer lines of `hearthpulse.log` as the expected offers. From B: `Arena.log` whole as
`Fixtures/Arena/arena-redraft-<date>.log`, replacing `redraft-synthetic.log` in `ArenaDraftRedraftTests` (the
synthetic file stays only for the cases the live log does not hold), again with the game's deck code as the truth.
Card names in `Client chooses:` lines stay; a BattleTag or a user path in any of the files is replaced first
(rule 12 of `hearthpulse-privacy`). Name the log, and nothing else, as the source of the line formats.

## Layout and licences

- The badge row is centred on the play area left of the client's deck list with the anchors of HdtArenaHelper's
  `ArenaOverlayWindow` (MIT, (c) 2026 Alessandro Colace): 4:3 safe area, play area its left 3.25/4.25, cards 0.26 of
  that width apart, heroes 0.29 and 0.05 of the height lower. Adapted in `OverlayViewModel.Arena.cs` of both UIs
  (licence in the file header and in `THIRD-PARTY-NOTICES.txt`); the panel's default margin `0,566,339,0` comes from
  the same numbers. Everything else is our own code; HDT and Firestone were not used.
- Render scenes: `overlay-arena-hero`, `overlay-arena-heropower`, `overlay-arena-pick` (a normal-Arena run, so the
  slice note shows), `overlay-arena-pick-scale160`, `overlay-arena-package` (an Underground legendary with its package)
  from a made-up Warlock draft with the real scores (`Arena/ArenaDraftScene.cs`), and `main-arena-deck`
  (`DevRender.Arena.cs`, `OffscreenRenderer.Arena.cs`).
- Tests: `tests/Hearthpulse.Tests/ArenaDraftTests.cs` (fixtures `Fixtures/Arena`, trimmed live answers; its fake
  server has no route, so it runs the dataset path), `ArenaDataRouteTests.cs` (the route, its fallback and cache,
  pages, the packages and their rule; `arena-cards-route.json` and `arena-legendaries.json` are live answers of
  2026-11-15 cut to a few rows), `ArenaDeckFitTests.cs`, `ArenaDraftRedraftTests.cs` and the
  two-host contract in `tests/Hearthpulse.Presentation.Tests/ViewModelContractTests.cs`.
