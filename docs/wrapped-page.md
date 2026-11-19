# Recap page ("Wrapped")

The player's month or year in the main window of both apps (task `TASK-2026-11-19-collection-achievements-wrapped`,
AC28), from their own history and the achievements they completed: games, hours, modes, favourites, when they play,
records, Battlegrounds and achievements. Russian «Итоги», English "Recap" (the page's code name stays `Wrapped`). Only
local data; no opponent's name, class, deck or BattleTag anywhere on the page.

## Where it is

Sidebar group "Library", under Achievements: "Recap" (`ShowPage("Wrapped")`, no Ctrl digit). The page is
`Main/Pages/WrappedPage.xaml` / `.axaml`; the shared view model is `src/Hearthpulse.Presentation/Main/MainViewModel.Wrapped.cs`
with its records in `WrappedRows.cs`; the numbers are Core's `Stats/Wrapped.cs` (`Wrapped.Build`, `Wrapped.Range`), whose
year agrees with the year in review (`YearInReview`, unchanged; its banner and window stay as they are).

## The period

- Opening: this month; when it has nothing (no game, no completed achievement), the newest month with something.
- "Month | Year" top right (under the title in the compact layout): a year shows the shown month's year; a month goes
  to that year's newest month with something (else this month in this year, December in an earlier one).
- ‹ › step one period within `Wrapped.Range`: from the first game or the first dated completion to the current period.
  A disabled arrow keeps its tip ("Nothing earlier" / "This is the current period").
- The title is "November 2026" (the month with a capital) or "2026".

## What it shows

- **Hero card**: one number in `FontSizeHero` (64) over the theme's glow - games, or completed achievements in a period
  without games - "games in November", the change against the previous period of the same length ("+34 vs last
  month", "Same as last month", "No games last month"; its tip names that period and its games). Under a rule: hours
  played (a game counts at most three hours; "45 min" under an hour), the win rate with "W–L outside Battlegrounds" (or
  "Battlegrounds only"), the best win streak.
- **Favourites**: class, deck (`DeckName.Display`, with its class) and Battlegrounds hero (the base hero), each with a
  portrait and its games; a tile without data is left out, the slide too when all three are.
- **Modes**: the most played one, then each mode with a bar against it, games, time and W–L ("—" for Battlegrounds,
  Duos and Mercenaries).
- **When you play**: the busiest weekday and hour with a bar chart each (Monday first, local time; the busiest bar in the
  accent, each bar's tip its count), and the longest game of at most three hours (mode, class or hero, date).
- **Battlegrounds** (only with solo games): games, first places, top 4 (of games with a place), average place; Duos are
  only under Modes.
- **Achievements**: how many tiers were completed in the period (local date) and their points, the latest ten, "And N
  more on the Achievements page"; without a book or without completions one line says so.

## States

The first result: "Working out your recap…" in the hero card. Later requests keep the shown period on screen while a
lit line over the card says the next one is being worked out. A period with nothing: the hero card's empty state, no
slides. No games and no achievements at all: "No games yet…", both arrows off. A failure: a line and "Try again".

## Work and threads

Each request (opening, an arrow, month/year, a new book, a new game) is worked out off the UI thread with a build number;
an older result never replaces a newer one. The history is read once (`HistoryStore.Since`) and again after the history
changed (`OnRecentChanged`): when the page is on screen at once, else the next time it is shown. A language switch
rebuilds the texts from the worked-out report (`RebuildWrapped`).

## Checks

- `tests/Hearthpulse.Tests/WrappedViewModelTests.cs`: the opening period, month and year, the arrows and their limits, the
  hero numbers and slides, empty states, a new game, a newer request winning, the achievements of the period, no
  opponent anywhere, the language switch.
- `tests/Hearthpulse.Tests/WrappedLayoutTests.cs` (Avalonia Headless): the arrows' names and disabled tips, the hero
  number whole at 420 and 1280, the empty period.
- `tests/Hearthpulse.Presentation.Tests/WrappedContractTests.cs`: both view models show the same recap, and the WPF page
  loads.
- Render group `wrapped` (`DevRender.Wrapped.cs`, `OffscreenRenderer.Wrapped.cs`): this month, this year, English, an
  empty period, without a book, compact (Avalonia). Not in the baseline loop.
