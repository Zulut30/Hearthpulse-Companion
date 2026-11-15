# Local data: broken and busy files

What both apps do when a file in the data folder (`AppPaths`, `HEARTHPULSE_DATA`) cannot be read, and what they do not
do yet.

| File | Cannot be read | Code |
|---|---|---|
| `settings.json` | Held by another program (an antivirus scan, a cloud sync): read again for up to a second. Broken JSON: every readable setting is kept, the rest is the default, the original is copied to `settings.json.invalid` first (saving is paused while that copy fails). | `AppSettings.Load`, `ReadWithRetry`, `Salvage`, `PreserveUnreadable` |
| `history.db` | Not a database (SQLite 26, or 11 "corrupt" on open): put aside as `history.db.broken-<UTC time>` with its journals, and a new history starts. | `HistoryStore` constructor |
| `combat-captures/*.hpc` | Damaged, cut short, too large (16 MB unpacked) or of another version: passed over, never deleted or repaired; the measurement says which file it did not read. A save that fails (folder not writable, disk full) is logged and the game is recorded as usual. | `CombatCaptureStore.Read`, `Save` |
| `cache/api/*.json` | Younger than 6 hours but not JSON: deleted and asked for again once; when offline, an older readable answer stands in. | `HearthpulseData.FetchAsync` |

## Known limits

- **Settings held longer.** The read is retried for one second and only for `IOException`. A file held longer (a cloud
  sync of a large folder) or a lock Windows reports as `UnauthorizedAccessException` still gives defaults that a later
  save writes over the file (the original is kept in `settings.json.invalid`). The full fix: after a read error that is
  not broken JSON, do not save over the file in that session. The retry waits on the UI thread, twice in WPF at start.
- **History put aside without a word.** The player sees an empty history and nothing says why (only a `Diag` line).
  A database SQLite calls corrupt on open is put aside whole even when part of it could still be read. Showing it needs a
  property on `HistoryStore`, a text in both languages and a notice raised after the hosts' start, in both apps.
- **Replaced log watcher.** A watcher replaced by the watchdog checks that it was stopped before it calls
  `OnSessionStarted` or `OnResync`. That narrows the race with the new watcher but does not close it (check, then act);
  closing it needs a generation shared with the consumer or one lock with `Stop`.

## Files that never leave the machine

Besides settings, history, replays, the log and the cache, the data folder holds:

- `combat-captures/` - one gzip JSON file per finished Battlegrounds game (`<game start>-<hash>.hpc`,
  `CombatCaptureStore`): for every fight, exactly what the combat simulator was given at the first attack (both
  boards with stats, keywords, enchantments, script values, the player-level cards, our own hand's minions, the
  damage cap), what the log said about each minion's effect tags, the fight's result and damage, and the odds the
  app showed. Cards are card ids; there is no name, BattleTag, account id or rating in it, and nothing the player
  did not see (the opponent's hand never reaches the simulator). It exists so the simulator can be measured on the
  player's own games after the game client deleted its logs (`Hearthpulse.Bench simcover --captures`,
  [bg-combat-sim.md](bg-combat-sim.md)). No upload, share, profile sync, source capture or crash report reads the
  folder; there is no switch for it because nothing is sent. Limits: 500 games and 32 MB, the oldest games go
  first. The most recently saved game is kept even when it alone exceeds the byte limit; a temporary file left after a failed cleanup is not counted. A file is written whole under a temporary name and renamed, on a pool thread after the game is recorded.
  Deleting the folder loses nothing the app shows.
