# History export ("экспорт истории")

**Export to CSV** on the History page saves the player's own games as a table for Excel, Google Sheets or a script.
Code: `Core/Data/HistoryExport.cs` (the file), `Presentation/Main/MainViewModel.HistoryExport.cs` and
`Presentation/Main/Match/ReplayLibraryViewModel.cs` (`Export`, `ExportFileRequested`, `ExportTo`), the button and the
save dialog in `Main/Match/ReplayLibraryView` of both UIs. Tests: `HistoryExportTests`, `LocBindingTests.HistoryExport`,
`HistoryExportContractTests` (Windows suite).

## What is exported

- Every game in `history.db` that matches the page's **mode filter** (All, Battlegrounds, Duos, Ranked, Arena,
  Underground, Other: the same rule as the cards, `MainViewModel.FilterMatch`), newest first. The page lists at most
  300 of the 400 newest games; the file has them all. The page has no period filter, so there is none here.
- The search box, the Wins / Losses switch and the tabs do not narrow the file: Saved and Imported are replay files,
  not History rows, and an imported game is not the player's own.
- Games brought over from HDT or Firestone are History rows and are exported (`source` says where a row came from).
- Nothing leaves the PC: the file goes where the player's save dialog points.

## Columns

Names are English and stay the same in every interface language. An unknown value is an empty field.

| Column | Value |
|---|---|
| `started_at`, `ended_at` | ISO 8601 to the second, with the zone the row was saved with (`2026-11-03T18:05:09Z`, an offset, or none) |
| `duration_seconds` | whole seconds |
| `mode` | `Battlegrounds`, `Duos`, `Ranked`, `Arena`, ... as History stores it |
| `game_type`, `format` | the game's `GT_*` and `FT_*` |
| `result` | `Win`, `Loss`, `Tie`, `Unknown`, `Abandoned` |
| `placement` | Battlegrounds place 1-8 |
| `player_class` | `MAGE`, ... |
| `hero_card_id`, `hero` | the player's hero card and its name in the interface language (empty when the card is not known) |
| `deck_name`, `deck_code` | the deck as Hearthstone named it, and its code |
| `opponent_class`, `opponent_archetype` | the opponent's class; its archetype when its played cards placed it clearly |
| `went_first` | `true` / `false` |
| `turns` | turns played |
| `rating_before`, `rating_after`, `rating_change` | Battlegrounds rating around the game |
| `rank`, `rank_legend` | ranked rank at the game's start, and the Legend place |
| `tavern_tier` | Battlegrounds tavern tier at the end |
| `source` | `live`, a log import, or another tracker's import |

**Never written:** the player's and the opponent's names and BattleTags, the game key (it can hold account ids), the
opponent's hero skin, card lists (mulligan, drawn, played), boards, combat lists, the replay's file name. The History
row shows none of the names, so the file has none.

## Format

- RFC 4180: lines end with CRLF (the last one too); a field with a comma, a quote, a line break or a space at either
  end is in quotes, a quote inside is doubled.
- UTF-8 with a byte order mark, so Excel opens Russian deck names as they are.
- Numbers and dates are written with the invariant culture: no thousands separators, no local date order.
- **Formulas are defused.** A text field that starts with `=`, `+`, `-`, `@`, a tab or a carriage return gets an
  apostrophe in front (a deck name is the player's own text and a spreadsheet would run it). Numbers are written by the
  exporter itself and are not touched, so a rating change stays `-25`.

## Writing the file

- The history is read, encoded and written on the thread pool; the button is off until the export ends.
- A file on this PC is written whole or not at all: to a temporary file beside it, then moved over it. A failed write
  leaves the old file as it was. A storage that only gives a stream (no local path, Avalonia) is opened once the table
  is ready and written in one piece; it cannot be made atomic.
- The page's status line ends the export: "Saved N games.", "No games of this mode to export." (no file is written),
  or "Couldn't save the file. Pick another folder and try again." (the reason goes to `hearthpulse.log`).
