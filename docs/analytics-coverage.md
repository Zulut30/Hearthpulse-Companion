# Coverage report: how far our own numbers are from publication

Our own statistics are computed from the game records in the stats pool (table `e`, [game-stats.md](game-stats.md)),
and a number is published only from enough installations and games. The coverage report answers the owner's
question "how much is still missing" family by family, so nobody reads the database by hand. It prints counts only.

Code: `server/pool/node/report.mjs`; tests: `server/pool/test/report.test.mjs`. Origin: TASK-2026-11-26-pro-collection,
package A4 (AC9); the thresholds and the MMR ranges are those of the record views (contract К3 of that task).

## Run it

On the pool host, as the service account, beside the running service:

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
publishable = a cell with >=5 installs and >=10 games; arena counts finished runs; "<5" is a count from 0 to 4.
All records in the window: installs 7, records 75.

family           cell                   installs  games  cells  >=5 installs  publishable
ranked           format x class                7     43      6            <5           <5
ranked-mulligan  format x class x card         6     42     16            10            6
bg               mode x MMR range              7     21      6            <5           <5
arena            kind x class (runs)           6      6     <5            <5           <5
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
- **publishable**: cells with 5 installations and 10 games or more: a value is shown with its interval.
- **All records**: every record in the window, any mode (Casual, Brawl, Classic too), for scale.

## Reading it

- A family with `<5` installs publishes nothing yet: it needs installations before games.
- Cells with 5 installs that are not publishable wait for games, not for new players.
- A cell that is not publishable here publishes nothing finer for the same days: rank buckets, the coin, archetypes
  and decks (50 games) split it further. The views use balance-patch windows, so a `patch` window can hold more or
  fewer days than the report.
- The report counts records. A view counts what its metric needs (a record without the metric's field is left out),
  so its counts can be lower.
- Whether a view is published is the route's answer (`available`, `published`), not the report's.
- An installation is not a player: one player on two PCs is two installations.

## Privacy

- Counts only: no install id, record id or deck code, and no cell key (class, card, hero), so no line points at a
  player or a deck.
- Every count from 0 to 4 prints as `<5`, in both formats; 5 and above exactly. The window, its dates and the
  thresholds are settings, not counts.
- A failure prints one JSON line on stderr with a code and exits with 1: `usage` (with the usage line),
  `pool_missing`, or `report_failed` with the error message cut to 120 characters. Never record content.

## Read-only, beside the service

- `pool.sqlite` is opened with SQLite's read-only flag: a write through this connection fails (`SQLITE_READONLY`),
  and a missing file is `pool_missing`, never created.
- The service's single-writer lock (`instance.lock.sqlite`) is not opened. In WAL mode the report and the service
  work at the same time: the report reads the last committed state and never blocks a write. All families come from
  one read transaction, so they agree with each other.
- Cost: one pass over the window's records (index on `day`), a few `json_extract` fields per record; the install ids of
  each cell are held in memory to count them and never printed. It is a command for a person, not a timer.

When to run it: after each pool deploy of the program (its report goes into the task's journal) and whenever the
owner asks. Wave 3 (F1) moves the thresholds and the MMR ranges, copied here from the record views' rules, to the
views' own module so both read one copy.
