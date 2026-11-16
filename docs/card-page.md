# Card page

One Constructed card in the main window of both apps (roadmap `2026-11-15-hsreplay-remediation.md`, 2.10): the card
itself, its numbers in ranked games, the archetypes that run it, its mulligan by archetype, and the player's own games
with it. Until this page a card's numbers showed only in an archetype's mulligan guide and on the page of an own deck.

## Where it opens from

A click on a card row opens the page; the row keeps its hover preview and takes a hand cursor:

- the deck page: the deck list, the version differences, the "Your mulligan" and "Card results" tables;
- the meta tier list: the rows of the chosen archetype's mulligan guide.

The "Top decks" lists of the meta show decks, not cards, so they have no card row to click. A row sets
`CardLink.CardId` (`Controls/CardLink.cs` in each UI); the click raises a bubbling `CardLink.Open` event and the main
window calls `MainViewModel.OpenCard`. A new card row elsewhere needs only that attribute.

The page stands in for the Standard or Wild page (or the deck page open on it), in that page's format. Back, Esc,
Alt+Left and the mouse's back button return to the page under it at the place the player was reading; the Standard
and Wild sidebar items switch the open card to the other format; any other page closes it. Mouse only: the rows are
not keyboard stops.

## What it shows

| Section | Numbers | Source |
|---|---|---|
| Header | name, cost, class, set, text, art | card data (`CardDb`, HearthstoneJSON), `CardArt` |
| In ranked games | share of decks (copies per deck), deck win rate, win rate when drawn, win rate when played (times played), kept in the mulligan (win rate in the opening hand), turn played | the format's card list from the data API |
| Archetypes that run it | lists with the card of the archetype's lists, copies, the archetype's own win rate and games | the loaded meta of the format (`ConstructedMeta.Decks`, `Archetypes`) |
| Mulligan by archetype | kept, win rate in hand, impact (in hand minus the archetype's win rate over the same games), opening hands | the loaded meta (`MetaArchetype.Mulligan`, `MulliganBaseline`) |
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
- Archetypes that share one set of mulligan numbers (`MulliganArchetypeId`) give one mulligan row, the most played.
- Own games are Ranked and Casual games of the page's format, abandoned ones left out, counted per game (two copies
  drawn count once). A win rate from fewer than five own games stays neutral (`DeckDetails.SmallSample`).
- The set is the card data's code shown as words ("Emerald Dream" for `EMERALD_DREAM`): card data has no set names.

Empty states: the list has no such card - "No data for this card..."; the list could not be loaded and there is no
copy - "Couldn't load Hearthpulse data. Check your connection and open the card again."; no list of the meta runs the
card; no mulligan numbers; the meta is not loaded; no own games of the format / none with the card. Offline the cached
list is shown with its own date, and a late source adds the "update is delayed" line of the data summary
(`DataSection.CardStats`, listed in Settings, Data, "Data status" as "Card statistics").

## Data API

One request per format, read only (`HearthpulseData.LoadCardStatsAsync`, `Core/Api/HearthpulseData.Cards.cs`):

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
| Render scenes | `HEARTHPULSE_RENDER_ONLY=card` (`DevRender.Card.cs`, `OffscreenRenderer.Card.cs`): `main-card`, `main-card-full`, Avalonia also `main-card-compact`; `HEARTHPULSE_RENDER_CARD=<card id>` picks the card |
| Tests | `CardStatsLoadTests`, `CardPageTests`, `CardPageViewModelTests`, `CardPageLayoutTests` (Hearthpulse.Tests), `CardPageContractTests` (Hearthpulse.Presentation.Tests) |

Nothing leaves the machine: the page sends two read-only requests with no player data in them, and the own-games block
is read from `history.db`.
