# Card page

One Constructed card in the main window of both apps (roadmap `2026-11-15-hsreplay-remediation.md`, 2.10): the card
itself, its numbers in ranked games, the archetypes that run it, its mulligan by archetype, and the player's own games
with it. Until this page a card's numbers showed only in an archetype's mulligan guide and on the page of an own deck.

## Where it opens from

A card row opens the page by a click, or by Enter or Space while it has the focus; the row keeps its hover preview and
takes a hand cursor:

- the deck page: the deck list, the version differences, the "Your mulligan" and "Card results" tables;
- the meta tier list: the rows of the chosen archetype's mulligan guide.

The "Top decks" lists of the meta show decks, not cards, so they have no card row to click. A row is a button (WPF
style `CardRowButton`, Avalonia classes `bare`) around what the row draws, with `CardLink.CardId` and
`CardLink.CardName` (`Controls/CardLink.cs` in each UI): the button raises a bubbling `CardLink.Open` event and the main
window calls `MainViewModel.OpenCard`. A new card row elsewhere needs only that button.

Focus: opening the page moves the keyboard focus to its Back button; closing it (Back, Esc, Alt+Left, the mouse's back
button) returns the focus to the row the page was opened from. When that row is gone (the list was built again while
the card was open) the focus goes to the same card's row of that list, else the list's first row, else the page's
sidebar button; a focus the player moved elsewhere meanwhile is left alone (`MainWindow.CardFocus.cs`,
`MainView.CardFocus.cs`).

Keyboard and screen reader: each list of card rows is one Tab stop (in WPF the list itself is `Focusable="False"`), Up and Down move between its rows (stopping at
the ends), the focused row shows the theme's row focus ring, and the button's name is "Open the card page of <card>"
(`Card.OpenName`). `AccessibleNameTests` checks that every linked row in both UIs' markup is such a button.

The page stands in for the Standard or Wild page (or the deck page open on it), in that page's format. Back, Esc,
Alt+Left and the mouse's back button return to the page under it at the place the player was reading; the Standard
and Wild sidebar items switch the open card to the other format; any other page closes it.

## What it shows

| Section | Numbers | Source |
|---|---|---|
| Header | name, cost, class, set, text, art | card data (`CardDb`, HearthstoneJSON), `CardArt` |
| In ranked games | share of decks (copies per deck), deck win rate, win rate when drawn, win rate when played (times played), kept in the mulligan (win rate in the opening hand), turn played | the format's card list from the data API |
| Archetypes that run it | lists with the card of the archetype's lists, copies, the archetype's own win rate and games | the loaded meta of the format (`ConstructedMeta.Decks`, `Archetypes`) |
| Mulligan by archetype | kept, win rate in hand, difference (in hand minus the archetype's win rate over the same games), opening hands | the loaded meta (`MetaArchetype.Mulligan`, `MulliganBaseline`) |
| Your games | games drawn in and played in with their record and win rate, kept of offered | the local history only (`HistoryStore.WithCards`) |

Rules the model keeps (`Core/Constructed/CardPage.cs`, `CardPages.Build`, pure and tested by hand-made data):

- A number the source does not give has no tile: a card played 0 times has no "when played" tile and no turn; a row
  without `keep_percentage` has no mulligan tile. Nothing is filled in or estimated.
- Every block names its sample and date. The card list gives one sample size, **times played**; it gives no number of
  games or decks behind the deck win rate, so none is shown. The source line is
  "Hearthpulse data · updated 16 Nov 06:04 · Gold · last 14 days": the date is the server's collection time, the rank
  and period are read from the answer (`rank_range`, `time_range`), never from the source's name. Archetype and
  mulligan rows carry the meta's own source, rank, period and date; each mulligan row has its opening hands.
- The other format's list or meta is never shown on a page (`Build` checks `Format`).
- The mulligan column is a **difference** of two win rates, not the card's effect: a card is kept in hands that are
  already good, so the column and its tip claim no cause. Under `CardPages.MulliganVerdictHands` (400) opening hands
  the difference has no green or red: a win rate near 50 % over n hands has a 95 % interval of about ±98/√n points
  (±17 at 33 hands, ±4.9 at 400), wider than the few points shown. The number and its hands are always shown.
- In the compact Avalonia layout the Games and Opening hands columns are hidden and their values stand under the win
  rate and the difference ("132 games", "1,182 hands"): no number is left without its sample.
- Archetypes that share one set of mulligan numbers (`MulliganArchetypeId`) give one mulligan row, the most played.
- Own games are Ranked and Casual games of the page's format, abandoned ones left out, counted per game (two copies
  drawn count once). A win rate from fewer than five own games stays neutral (`DeckDetails.SmallSample`).
- The set is the card data's code shown as words ("Emerald Dream" for `EMERALD_DREAM`): card data has no set names.

A load never leaves the page "loading": the data API's request ends after 20 seconds at the latest
(`HearthpulseApi.Timeout`; the Wild list is 4 MB), and then the page shows the older copy with its date, or, without
one, "Couldn't load Hearthpulse data. Check your connection and try again." with a "Try again" button that asks again
(`CardPageViewModel.StatsFailed`, `Retry`). The card, the meta rows and the own games are shown either way.

Empty states: the list has no such card - "No data for this card..." (no retry: nothing failed); no list of the meta runs the
card; no mulligan numbers; the meta is not loaded; no own games of the format / none with the card. Offline the cached
list is shown with its own date, and a late source adds the "update is delayed" line of the data summary
(`DataSection.CardStats`, listed in Settings, Data, "Data status" as "Card statistics").

## Data API

One request per format, read only (`HearthpulseData.LoadCardStatsAsync`, `Core/Api/HearthpulseData.Cards.cs`). Callers
that ask while a format's list loads share that load (one request, one answer for all); a caller's cancellation ends
only its own wait. The page never cancels a load: opening another card, a rebuild (a meta, a language, a data summary)
or Back only stop waiting, the list the open page has is reused, and a result for a card or format the page has left
is not shown (`MainViewModel.Cards.cs`).

| Format | Request | What the answer is (checked live 2026-11-16) |
|---|---|---|
| Standard | `GET datasets/hsreplay_cards_legend_included_popularity` | 1 324 cards, `RANKED_STANDARD`, **`rank_range: GOLD`**, `LAST_14_DAYS`, collected once a day (06:04 UTC), 1.3 MB |
| Wild | `GET datasets/hsreplay_cards_wild_legend_14d` | 4 393 cards, `RANKED_WILD`, `LEGEND`, `LAST_14_DAYS`, collected every 4 hours, 4 MB |

Each row: `id`, `dbfId`, `deck_popularity`, `deck_winrate`, `avg_copies`, `times_played`, `winrate_when_played`,
`winrate_when_drawn`, `keep_percentage`, `opening_hand_winrate`, `avg_turn_played_on`, `avg_turns_in_hand`
(percentages as "55.13%"), plus the card's name, cost, type and Battlegrounds fields the page does not read. The list
is in the answer twice (`data.structured.cards` and `data.hsreplay_extracted.cards`); the collection time is
`fetched_at` beside `data`.

Cache: `cache/api/card-stats-{format}.json`, six hours, written atomically, trimmed by `ShrinkCardStats` to the slice
(`game_type`, `rank_range`, `time_range`) and one array per card (id, dbf id, nine numbers) with the collection time in
`meta.fetched_at` - about 95 KB for Standard and 300 KB for Wild. A 404 (the source is gone) is remembered as
`{"data":null}` for the cache's age; a failed request serves the old file. The loader never throws, a cancelled load
included (the page that asked is closed: it gets null).

**A 200 answer is not always a list.** The answer describes itself (checked live 2026-11-16): `structured.type`
(`card_stats`), `structured.blocked` (false; true when the origin refused the collector), `schema_validation.ok` (true),
`structured.row_retrieval` (`raw_rows` = `normalized_rows` = 1 324, `unexplained_drops` 0), `publication.mode`
(`stable`). `UsableCardStats` refuses an answer with another type, `blocked: true`, a failed validation, no
`structured.cards`, or fewer than `MinCardRows` = 300 rows with a card id. The floor: the smallest Standard pool of a
rotation is over 700 collectible cards and nearly all are played by someone in two weeks (1 324 and 4 393 rows seen),
so a list of a few dozen or a couple of hundred cards is a collection cut short, not a quiet fortnight. A refused
answer is a failed load: the cache file is not written and keeps its age (so the next open asks again), the older copy
is served with its own date, and without one the page shows the error with "Try again". It is not remembered the way
a 404 is.

Checked and not used:

- `datasets/hsreplay_cards_legend_included_winrate`: the same 1 324 rows as `…_included_popularity` in another order
  (0 rows differ), so one request is enough.
- `GET /api/db/cards/trends?card_name=…`: rows per source and day (`popularity` only, no win rate), keyed by the card's
  name in whatever language the collector stored (English and Russian names are different series). The newest row
  seen was 2026-08-07: three months old, so the page shows no trend. Unknown card: `{"card_name": …, "trends": []}`.
- `GET /api/v1/constructed-cards`: the site's catalog (names, text, images); the page takes these from card data the
  app already has.
- `/api/db/archetypes/{id}/decks?include_cards=…`, `v1/hsguru/archetypes`, `v1/hsguru/mulligan`: the archetype lists and
  mulligan numbers are in the meta the app has already loaded for the format, so the page asks nothing more.
- `/v1/meta-intelligence/*`: needs a token (401), not used.

Fixtures: `tests/Hearthpulse.Tests/Fixtures/Cards/` (how they were cut is in its `README.md`).

## Code

| Part | Where |
|---|---|
| Loader, cache trimming | `Core/Api/HearthpulseData.Cards.cs` |
| Model and its rules | `Core/Constructed/CardPage.cs` (`CardStat`, `CardStatsSlice`, `CardPage`, `CardPages`) |
| Data summary section | `Core/Api/DataHealth.cs` (`DataSection.CardStats`) |
| View model | `Presentation/Main/Cards/CardPageViewModel.cs`, `Presentation/Main/MainViewModel.Cards.cs` (`OpenCard`, `CardOpen`, `LoadCardStats` set by the host) |
| Views | `App/Main/Cards/CardPageView.xaml`, `UI/Main/Cards/CardPageView.axaml`; `PageCard` in `MainWindow.xaml` / `MainView.axaml` |
| Card rows | `App/Controls/CardLink.cs`, `UI/Controls/CardLink.cs` |
| Strings | `Localization/Strings/{en,ru}/card.json` |
| Render scenes | `HEARTHPULSE_RENDER_ONLY=card` (`DevRender.Card.cs`, `OffscreenRenderer.Card.cs`): `main-card`, `main-card-full` (live list and meta, "Your games" on the made-up games of `CardPageScene`), `main-card-loading`, `main-card-error` ("Try again"), `main-card-stale` (an older copy under a late source), Avalonia also `main-card-compact` and `main-card-error-compact`; `main-card-theme.txt` names the theme and its accent and background colours; `HEARTHPULSE_RENDER_CARD=<card id>` picks the card, `HEARTHPULSE_THEME=<preset>` the theme |
| Tests | `CardStatsLoadTests`, `CardPageTests`, `CardPageViewModelTests`, `CardPageLayoutTests` (Hearthpulse.Tests), `CardPageContractTests` (Hearthpulse.Presentation.Tests) |

"Your games" reads the history once for the open page (all Ranked and Casual games of the format with their card
columns) and again after `HistoryStore.Changed`; a load that another card overtook does not read it. Measured on the
developer's data copy (84 games, 60 of them Standard, 50 KB of card columns): 1.7 ms for the query and the JSON of
every row, so the read is kept as one query per page rather than narrowed per card.

Nothing leaves the machine: the page sends two read-only requests with no player data in them, and the own-games block
is read from `history.db`.
