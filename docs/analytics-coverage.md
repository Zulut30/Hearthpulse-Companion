# Coverage report: how far our own numbers are from publication

Our own statistics are computed from the game records in the stats pool (table `e`, [game-stats.md](game-stats.md)),
and a number is published only from enough installations and games. The coverage report answers the owner's
question "how much is still missing" family by family, so nobody reads the database by hand. It prints counts only.

Code: `server/pool/node/report.mjs`; tests: `server/pool/test/report.test.mjs`. Origin: TASK-2026-11-26-pro-collection,
package A4 (AC9); the thresholds and the MMR ranges are those of the record views (contract K3 of that task).

## Run it

On the pool host, as the service account, beside the running service, at a quiet hour (why: "Read-only, beside the
service" below):

```sh
cd /opt/hearthpulse-pool/app
sudo -u hearthpulse-pool /opt/hearthpulse-pool/runtime/node-v24.19.0-linux-x64/bin/node \
  server/pool/node/report.mjs --data /var/lib/hearthpulse-pool
```

- `--days N` (1-180, default 30): the last N days up to today, by the record's `day` (UTC days). A record dated
  tomorrow by a fast clock waits for its day, as in the views.
- `--format json`: the same counts as one JSON document (`"<5"` stays a string).
- On a copy of the data folder (a backup), point `--data` at the folder that holds `pool.sqlite`.

Run it as `hearthpulse-pool`: the data folder is that account's only (mode 0700), and when the service is stopped
SQLite creates empty `pool.sqlite-wal` / `-shm` files even to read a WAL database; they must keep the service's owner.

## What it prints

The output of the test set (7 installations):

```
Hearthpulse pool coverage, read-only: game records of 2026-10-28..2026-11-26 (30 days), built 2026-11-26T12:00:00.000Z.
publishable = a cell with >=5 installs and >=10 games; arena counts finished runs; "<5" is 1 to 4 installs or games, cell counts are exact.
All records in the window: installs 7, records 77.

family           cell                   installs  games  cells  >=5 installs  publishable
ranked           format x class                7     43      6             3            2
ranked-mulligan  format x class x card         6     42     16            10            6
bg               mode x MMR range              6     22      6             4            2
arena            kind x class (runs)           6      6      2             1            0
```

| Family | Cell | A record counts when | Views it feeds |
|---|---|---|---|
| `ranked` | format × class | `mode: ranked`, format Standard or Wild, a class | `constructed-classes`, `-archetypes`, `-matchups`, `-decks`, `-cards` |
| `ranked-mulligan` | format × class × card | the same, with an offered opening hand; a card's cell counts the games it was offered in (two copies in one hand: one game) | `constructed-mulligan` |
| `bg` | mode (solo, duos) × MMR range | `mode: bg` (solo) or `duos`: the mode's `all` cell and, with a rating, `0-3999`, `4000-5999`, `6000-7999` or `8000+`; a duos record placed below 4th is not counted | `bg-heroes`, `-trinkets`, `-comps`, `-tierups` |
| `arena` | kind (arena, underground) × class | one `arena_run` record: a finished run, not a game | `arena-classes` |

- **installs**: distinct installations among the family's records in the window.
- **games**: the family's records (for `arena`, runs).
- **cells**: cells with at least one record.
- **>=5 installs**: cells with 5 installations or more: a number in them can exist.
- **publishable**: cells with 5 installations and 10 games or more: a number over the cell's own games is shown with
  its interval.
- **All records**: every record in the window, any mode (Casual, Brawl, Classic too), for scale.

## Reading it

- A family with `<5` installs publishes nothing yet: it needs installations before games.
- Cells with 5 installs that are not publishable wait for games, not for new players.
- `publishable` is the threshold of a number over the cell's own games, such as a class's win rate. A number over a
  part of the cell needs more: rank buckets, the coin, archetypes, heroes and decks (50 games) split it further.
- A number over more than the cell's games can have a value in a cell that is not publishable here. For these the
  report can show less than a view publishes:
  - a class's share of its format, an archetype's share of its format (`shareAll`) and a class's share of its Arena
    kind divide by the games or runs of the whole group;
  - the Arena win rate divides the wins by all games of the runs, not by the runs;
  - the mulligan cell counts the games a card was offered in, while the view's `kept` counts offered copies (two in
    one hand are two), and `opening`, `drawn` and `played` count the games the card was in the hand after the
    mulligan, came into the hand or was played, offered or not. `drawn` and `played` can be published for a card
    whose cell here is not publishable, or that has no cell because it was never offered.
- The views use balance-patch windows, so a `patch` window can hold more or fewer days than the report.
- The report counts records. A view counts what its metric needs (a record without the metric's field is left out, a
  copy can count twice), so its counts can be lower or higher.
- Whether a view is published is the route's answer (`available`, `published`), not the report's.
- An installation is not a player: one player on two PCs is two installations.

## Privacy

- Counts only: no install id, record id or deck code, and no cell key (class, card, hero), so no line points at a
  player or a deck.
- Installs and games (records, runs) from 1 to 4 print as `<5`, in both formats: a group that small is never shown
  exactly. 0 and 5 or more print as they are.
- Cell counts (`cells`, `>=5 installs`, `publishable`) print exactly: a cell is a class, a card or a rating range,
  not a group of people, and the last two count only cells of 5 installations or more.
- The window, its dates and the thresholds are settings, not counts.
- A failure prints one JSON line on stderr with a code and exits with 1: `usage` (with the usage line),
  `pool_missing`, or `report_failed` with the error message cut to 120 characters. Never record content.

## Read-only, beside the service

- `pool.sqlite` is opened with SQLite's read-only flag: a write through this connection fails (`SQLITE_READONLY`),
  and a missing file is `pool_missing`, never created.
- The service's single-writer lock (`instance.lock.sqlite`) is not opened, and the report holds no lock a write waits
  for: in WAL mode the service keeps writing while the report reads the last committed state.
- One query reads the window: each record once, whatever its mode (index on `day`), with a few `json_extract` fields
  of the records a family needs. The families and the all-records line come from it, so they agree with each other.
  The install ids of each cell are held in memory to count them and never printed.
- **The service waits for the report.** At the end of its minute's upkeep, and after each source-capture worker job,
  the service truncates its WAL (`PRAGMA wal_checkpoint(TRUNCATE)`). The truncation waits while a read that began
  before it is still open, the report's too, for up to the service's `busy_timeout` of 5 s, and the service runs on
  one thread: during that wait it takes no upload and answers no request. A read that outlasts the 5 s makes the
  truncation give up for that minute, with no log line, and the WAL keeps its frames until the next one, the frames
  of a forget too.
- Cost: about 1 s per 10,000 records in the window. On a desktop PC (Windows 11, Node 24), a pool of 120,000
  synthetic records (430 MB, bodies of 1 KB on average) took 9 s with 90,000 of them in the window and 13 s with
  `--days 180`: a run on a pool that size outlasts the service's 5 s.
- So run it off-peak, at an hour when few apps upload, one run at a time, by hand rather than on a timer. Time the
  first run on the host (`time sudo -u hearthpulse-pool …`). If a run takes longer than a couple of seconds, or for a
  long window, run it on a backup of the data folder rather than beside the service.

When to run it: after each pool deploy of the program (its report goes into the task's journal) and whenever the
owner asks. Wave 3 (F1) moves the thresholds and the MMR ranges, copied here from the record views' rules, to the
views' own module so both read one copy.
