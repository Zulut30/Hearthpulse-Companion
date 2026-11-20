# Collection page

The player's own collection as a page of the main window in both apps (task `TASK-2026-11-19-collection-achievements-wrapped`,
package P2): how full each expansion is and the dust to complete it, the hero skins by class, the coins and the card
backs. It sits in the sidebar's "Library" group under History, with Achievements and Recap after it; the three library
pages have no Ctrl+digit (`MainViewModel.NavDigitPages`: Ctrl+1…8 stay as they were, Ctrl+8 is Settings).

Data: the collection read from the game client (`docs/collection.md`, `GameSession.Collection`) and the card backs and
coins (`GameSession.Cosmetics`, `docs/collection-cosmetics.md`), both kept only on this machine. The page sends nothing
anywhere.

## Tabs

| Tab | Source | What it shows |
|---|---|---|
| Expansions (`Sets`) | collection + card data | The filter's sum (playable %, unique, dust to complete; golden once verified), a wide bar, then one tile per set, newest first: name (`CardSets.DisplayName`), percent of the playable copies (rounded down: 100 % only for a complete set), its bar, "Playable x/y", "Unique x/y", the dust line ("N dust to complete" with the "estimate" mark when the game hands out some missing cards; "Set complete"; "Only cards you can't craft are left"), "N copies can't be crafted", and Common/Rare/Epic/Legendary with name, colour, bar and "x/y" (FREE cards count in the sums, without a cell). Filter: Standard (default), Wild (Standard + Wild), All (Classic too); the group's name shows on a tile only under Wild and All. |
| Hero skins (`Skins`) | collection + card data | Chips "Class · owned/all" in the game's order (Death Knight first), "Owned: x of y" over all classes, then one class at a time: its hero's portrait, name, "x of y", bar, and its skins (owned first, then by name), each with its art by the card data's id (never an id from the collection; the full card on hover) and "Not owned" under a missing one. |
| Coins (`Coins`) | cosmetics + card data | "Owned: x of y" with a bar and every coin: favourites, the other owned, the rest. Art comes from the coin's card (`CardDb.Get(CardDbfId)`); its name only where it tells the coin apart (most coin cards are named just "The Coin"), else the client's name of the cosmetic; without that card the client's name and a placeholder circle. |
| Card backs (`Backs`) | cosmetics | "Owned: x of y" with a bar, chips by `CardBackCategory` in its order (None and unknown numbers together as "Other", last; empty groups left out), one group at a time. A back has no art in any open source: the tile is a double frame over the theme's motif with a medallion (64 px, a gold edge of its own shape) whose lobes (9, 4, 12, 6, 5, 8 by id mod 6) and turn (id mod 8 × 11.25°) come from its id, the motif behind it turned four times as far, and the group's glyph; the lock or heart badge stays at full strength over a dimmed frame; name and description in the tip and the accessible name. |

A missing tile shows its art at 40 % with a lock badge and says "Not owned" in words; a favourite has a heart badge and
"Favourite" in the tip and the accessible name. Names from the client (card backs, coins without a card) show in the
client's language as read.

Golden copies: shown only once the split by finish is checked against the game (`CollectionFinish.GoldenVerified`,
`docs/collection.md`). The render scene turns them on for its `-golden` frame through the internal
`MainViewModel.CollectionGoldenShown`, never a setting.

## States

Each tab has the status line of its source under the tabs (`LibraryStatus`, template `LibraryStatusLine` in
`Main/Pages/LibraryLook`): "Read from the game today at 14:05." for data from the running client; a saved copy names
its full date, carries the mark "from saved data" (its tip says why) and dims the pictures (bars, art, medallions) to
`DeckBuildView.SavedOpacity`, never the words. With no data there is no line, only the tab's empty state; no tab ever
shows "0 of 0".

| State | Expansions / Hero skins | Coins / Card backs |
|---|---|---|
| No data, Hearthstone not running | `Empty.None` | `Empty.NoCosmetics` |
| No data, Hearthstone running | `Empty.NotRead` | `Empty.CosmeticsNotRead` |
| No data, a system without the read | `Empty.Unsupported` | `Empty.CosmeticsUnsupported` |
| Saved (not running / running / no read) | `Status.Saved` / `Status.SavedNotRead` / `Status.SavedUnsupported` + mark | `Status.Saved` / `Status.CosmeticsSavedNotRead` / `Status.SavedUnsupported` + mark |
| Confirmed | `Status.Confirmed` | `Status.Confirmed` |
| Card data not loaded (or without hero skins) | `Empty.NoCards` | coins: client names, no art |
| First count running | `Loading` | - |
| Count failed (logged) | `Failed`; the next change counts again | - |
| Filter without sets | `Empty.Filter` (the filter stays to pick All) | - |

Hearthstone starting or closing changes the texts at once (`RaiseCollectionStatus`).

## Behaviour and load

- The sets and skins are counted on a pool thread (`ExpansionCompletion.Build`, `HeroSkins.Build`) once per collection
  and card data, when the page is shown; a newer count replaces an older one by its number, and the old numbers stay
  on screen until then. A change while the page is hidden waits for its next showing.
- Only the shown tab's rows are built; skins and card backs one class or group at a time.
- A language switch rebuilds the set names, groups, captions and states (hook `RebuildCollectionPage`); card names
  follow after a restart, as everywhere.
- The tab views reveal themselves on show (`Motion.RevealOnShow`); nothing else moves.

## Code

| Part | Where |
|---|---|
| View model | `src/Hearthpulse.Presentation/Main/MainViewModel.CollectionPage.cs` (frame, states, sets, skins), `MainViewModel.CollectionCosmetics.cs` (coins, card backs), rows `CollectionPageRows.cs`; hooks `MainViewModel.LibraryPages.cs` |
| Views | `Main/Pages/CollectionPage.xaml(.cs)` (WPF), `Main/Pages/CollectionPage.axaml(.cs)` (Avalonia); common look `Main/Pages/LibraryLook.xaml` / `.axaml` |
| Hosts | `App.Cosmetics.cs`, `AppHost.Cosmetics.cs` (`GameSession.CosmeticsChanged` → `MainViewModel.SetCosmetics`) |
| Texts | `Localization/Strings/{en,ru}/collectionpage.json` (`CollectionPage.*`), set names `cardsets.json` |
| Tests | `CollectionPageViewModelTests`, `CollectionPageLayoutTests` (Headless), `CollectionPageContractTests` (both VMs, the WPF page) |
| Render | `HEARTHPULSE_RENDER_ONLY=collectionpage` (`DevRender.CollectionPage.cs`, `OffscreenRenderer.CollectionPage.cs`): the data copy's own files for the tabs, made-up `CosmeticsScene` for the states; not in the baseline loop |
