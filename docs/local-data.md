# Local data: broken and busy files

What both apps do when a file in the data folder (`AppPaths`, `HEARTHPULSE_DATA`) cannot be read, and what they do not
do yet.

| File | Cannot be read | Code |
|---|---|---|
| `settings.json` | Held by another program (an antivirus scan, a cloud sync): read again for up to a second. Broken JSON: every readable setting is kept, the rest is the default, the original is copied to `settings.json.invalid` first (saving is paused while that copy fails). | `AppSettings.Load`, `ReadWithRetry`, `Salvage`, `PreserveUnreadable` |
| `history.db` | Not a database (SQLite 26, or 11 "corrupt" on open): put aside as `history.db.broken-<UTC time>` with its journals, and a new history starts. | `HistoryStore` constructor |
| `combat-captures/*.hpc` | Damaged, cut short, too large (16 MB unpacked) or of another version: passed over, never deleted or repaired; the measurement says which file it did not read. A save that fails (folder not writable, disk full) is logged and the game is recorded as usual. | `CombatCaptureStore.Read`, `Save` |
| `collection.json` | Missing, cut short, not JSON, of another format version, out of bounds, or with a key that is not a card id or a card that is not three counts: "no collection yet"; the file is left as it is and replaced by the next read of the game client. A save that fails is logged and tried again a minute later; its temp file `collection.json.tmp` is removed after every save and at start-up. | `CollectionStore.Load`, `Save`, `CollectionWatch` |
| `cosmetics.json` | Missing, cut short, not JSON, of another format version, out of bounds (an id below 0, a repeated catalog id, a text over 2 000 characters, an unknown group name): "no card backs or coins yet"; left as it is and replaced by the next read of the game client. Saves as `collection.json` (temp file `cosmetics.json.tmp`, retried a minute later). | `CosmeticsStore.Load`, `Save`, `CosmeticsWatch` |
| `achievements.json` | The same, with texts up to 4 000 characters, and inconsistent (a record pointing at a category, subcategory, section or achievement the file does not hold, a status by another name, a date outside 2000..2100): "no achievements yet"; left as it is and replaced by the next read. Saves as above (`achievements.json.tmp`). | `AchievementsStore.Load`, `Save`, `AchievementsWatch` |
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
  folder; there is no switch for it because nothing is sent. Since source v3 the game's journal carries its own
  allowlisted copy of the same fights, made in memory without the hand, under the game-records switch
  ([source-capture.md](source-capture.md), "Battlegrounds fights"); the file is still not read. Limits: 500 games and 32 MB, the oldest games go
  first. The most recently saved game is kept even when it alone exceeds the byte limit; a temporary file left after a failed cleanup is not counted. A file is written whole under a temporary name and renamed, on a pool thread after the game is recorded.
  Deleting the folder loses nothing the app shows.
- `collection.json` - the player's card collection as last read from the game client on Windows (`CollectionStore`,
  [collection.md](collection.md)): copies by card id (plain, golden, other finishes), the arcane dust balance and the
  time of the read. Local, it does not leave the machine: no code sends it (no upload, share, profile sync, game
  record, source capture or crash report reads it), and there is no switch for it. That rests on reading the sources,
  on a static check of names (`CollectionStoreTests`: a source file with a sending call must not name the
  collection), and since 2026-11-16 on a check of what goes over the wire (`CollectionPrivacyTests`: the app's
  services on a recording HTTP handler, a collection with a marker card id and a marker dust balance shown by the
  main view model; the meta downloads, a finished game's record, shared games, a source capture, a profile upload
  with a made-up sign-in and a crash report are sent, and no request carries a marker in its address, headers or
  body). That is proof for those six flows only: the Twitch publisher and the replay upload and share
  are not run by it and rest on the sources and the static check, not on a network check; the full list is
  in [collection.md](collection.md). The log names a read by its number of cards and nothing else; a crash report
  reads no log, so that line does not travel with one. There is no name, BattleTag, account id
  or region in it, so a loaded file is shown as unconfirmed until the running client was read
  ([collection.md](collection.md)). Deleting the file loses nothing: the next start of the game client writes it again.
- `cosmetics.json` - the client's card backs and coins and the ones the player owns and marked as favourite, as last
  read on Windows (`CosmeticsStore`, [collection-cosmetics.md](collection-cosmetics.md)): ids, the names and
  descriptions in the client's language, the read time. Local in the same way as `collection.json` and checked the
  same way (the static check names its types and file; `CollectionPrivacyTests` holds a card back with a marker name
  in the session); the log names a read by its counts only. No name, BattleTag, account id or region.
- `achievements.json` - the client's achievements (categories, sections, names, descriptions, quotas, points) with
  the player's progress and completion dates and each chain's last gain, as last read on Windows (`AchievementsStore`,
  [collection-cosmetics.md](collection-cosmetics.md)). The catalog exists nowhere but in the client, so without this
  file the achievements page is empty while the game is closed. Local and checked as `cosmetics.json` (an achievement
  with a marker name); deleting it loses the saved gains and the book they count from: the next read shows the
  achievements again but gains nothing until the read after it.
