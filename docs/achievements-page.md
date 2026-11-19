# Achievements page

The player's Hearthstone achievements in the main window of both apps (task
`TASK-2026-11-19-collection-achievements-wrapped`, AC23-AC27): the points and completed tiers, the chains the player
tracks with a star, the chains close to done, the recent completions, and the whole journal one subcategory at a time.
Only the game's own achievements, read from the client (`docs/collection-cosmetics.md`, "Achievements"); nothing here
is worked out from the history, and nothing leaves the machine.

## Where it is

Sidebar group "Library", under History: "Achievements" (`ShowPage("Achievements")`, no Ctrl digit). The page is
`Main/Pages/AchievementsPage.xaml` / `.axaml`; the shared view model is
`src/Hearthpulse.Presentation/Main/MainViewModel.Achievements.cs` with its rows in `AchievementRows.cs`. The hosts hand
the session's book over in `App.Achievements.cs` / `AppHost.Achievements.cs` (`GameSession.AchievementsChanged`,
dispatched to the UI thread, `MainViewModel.SetAchievements`).

## What it shows

- **Status line** (`LibraryStatus`, `Main/Pages/LibraryLook`): read from the game today at 14:05, or saved data of a
  date with the next step (start Hearthstone; not read yet - after a game or leaving the collection; Windows only).
  Saved data carries the "from saved data" mark; its bars and glyphs are dimmed (`DeckBuildView.SavedOpacity`), text is
  not. Without a book there is no line: the empty state says why (none yet, not read yet, another system), and while the
  first book is being worked out it says so.
- **Summary**: points earned of all (`AchievementBook.Summary`), the share as a bar, completed tiers of all; then each
  category (glyph by the client's icon name, the client's name, completed tiers, a bar, points) as a button that opens
  that category in the journal and brings the journal into view.
- **Tracked**: the chains starred in the order they were starred (`AppSettings.TrackedAchievements`, the first tier's
  id, at most `AppSettings.MaxTrackedAchievements` = 10). A row: the current tier's name, its description, "Level 2 of 3
  · Collection › Outland", a bar, "34 of 50" and the latest gain since the previous read ("+7 · today at 14:05", "+1 level
  · yesterday", or "no change yet"). A tracked chain missing from the book is not shown and stays in the settings; a line
  under the block counts them. "3 of 10" counts the settings' entries.
- **Nearly done**: up to 12 chains not done, not secret, with a quota of at least 2 and some progress, by share
  (`AchievementBook.NearlyDone`). **Recently completed**: up to 12 completed tiers by date (`RecentlyCompleted`); a
  secret one without a name is "Hidden achievement".
- **All achievements**: the categories as tabs (client's names), the chosen category's subcategories as a table of
  contents (completed tiers of each), and the chosen subcategory's rows on `PageRows` (one height, 56): a heading row
  for each named section, then its chains - star, name, description, progress or "Completed" / "All levels completed" /
  "Not completed", level, points, completion date. A secret chain not done shows "Hidden achievement" and "Revealed once
  you complete it", without star, progress or points. Only one subcategory's rows are built at a time.

## The star

A 32 px round icon button on tracked, nearly done and journal rows (not on recent completions, not on secret chains not
done). Its accessible name is its tip: "Track “Name”" / "Stop tracking “Name”". A press saves the settings at once
(`Settings.Save`). At 10 tracked chains the other stars are disabled with "You can track up to 10 chains…" in their tip
(shown on the disabled button) and a visible line under the block's heading. Removing a star in the tracked block moves
the focus to the next row's star, else the previous one, else the block's heading. Every row that shows the same chain
changes in place: rows are kept, so the focus stays where it was.

## Work and threads

The book's chains and sums are worked out off the UI thread (`AchievementBook.Chains` is lazy and thread-safe), only
once the page has been shown, with a build number: a newer book replaces an older one still being worked out, and the
page keeps its rows until the new ones are in. A language switch rebuilds the texts from the worked-out data
(`RebuildAchievementsPage`); the client's names stay in the client's language. Hearthstone starting or closing changes
the status line and the empty state at once (`HearthstoneRunningChanged`).

## Checks

- `tests/Hearthpulse.Tests/AchievementsViewModelTests.cs`: the summary (AC23), the star, limit, missing chain and gains
  (AC24), the two lists (AC25), the journal (AC26), every state (AC27), the language switch.
- `tests/Hearthpulse.Tests/AchievementsLayoutTests.cs` (Avalonia Headless): the star's name and Tab stop, one subcategory
  built, compact chips, the status line read out.
- `tests/Hearthpulse.Presentation.Tests/AchievementsContractTests.cs`: both view models show the same blocks and states,
  and the WPF page loads with its names.
- Render group `achievements` (`DevRender.Achievements.cs`, `OffscreenRenderer.Achievements.cs`): the page on the data
  copy's `achievements.json` with three chains tracked, the journal, English, compact (Avalonia), and the states on the
  made-up book of `AchievementsScene`. Not in the baseline loop.
