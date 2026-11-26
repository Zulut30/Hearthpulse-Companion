# Game records for Hearthpulse's own statistics

The compact-record contract below remains supported by the isolated Linux service on server 151. The expanded
ordered journal, durable generation deletion and synthetic-pilot activation limits are documented
in [source-capture.md](source-capture.md). The default endpoint is the production pool since 2026-11-16
(`PoolClient.DefaultEndpoint`): the public policy matches the host and the journal, the pool's data is outside the host
backups, and sending is on by default with the Settings → Data opt-out. Older Cloudflare deployment instructions are historical.

Every finished game goes to the stats pool as one compact, versioned **game record**: all
modes we track (Ranked, Casual, Arena, Brawl, Battlegrounds solo and duos), not only Battlegrounds. The records are
the raw material for Hearthpulse's own statistics later (ADR 0009: only our own analytics may be paid). Where they are
stored and why: [ADR 0011](adr/0011-game-records-in-the-stats-pool.md).

Code: `src/Hearthpulse.Core/GameStats/` (record, client, queue, sender), the server in
`server/pool/src/events.js`, the switch in Settings → Data of both apps (`MainViewModel.GameStats.cs`).

## What a record holds (version 1)

Built from the game's history row and its saved replay, after the game, off the log and UI threads.

| Key | What | Source |
|---|---|---|
| `id` | keyed hash of the game's history key (32 hex): a record sent twice is stored once | `GameStatsClient.RecordId` |
| `day`, `secs`, `turns` | day played (days since 1970, UTC), length, turns | history row |
| `mode`, `fmt` | `ranked` `casual` `arena` `brawl` `bg` `duos`; `standard` `wild` `classic` `twist` | history row |
| `rank` | league only (`bronze` … `diamond`, `legend`), Ranked only; never the legend number | history row |
| `cls`, `hero`, `deck` | your class, base hero, deck code (no deck name) | history row |
| `ocls`, `oarch` | opponent's class and archetype (the archetype the app named from cards it saw) | history row |
| `res`, `first` | `W` `L` `T`; whether you went first | history row |
| `mull` | `{o, k, r, n}`: offered, kept, replaced, received (the Coin left out) | replay |
| `plays` | `[turn, card, mana spent, created]`: every card you played; created = not in the deck list (0 without a deck) | replay frames |
| `oplays` | `[turn, card]`: cards the opponent played face up | replay frames |
| `choices` | `[turn, kind, source, [offered], [picked]]`, kind from the source's mechanics: `discover` `adapt` `chooseone` `dredge` `excavate` `choice` | replay |
| `draws` | `[turn, card, created]`: every card that came into your hand after the mulligan | replay |
| `exc` | `[turn, treasure]`: Excavate treasures that came into your hand | replay + `Excavate.Treasures` |
| `bg` | Battlegrounds: `placement`, `mmr` (rounded down to 500, at most 10,000), `offered`/`picked` heroes, `anomaly`, `tribes`, `tier`, `triples`, `tierups`, `trinkets` `{o, p}`, `quests`, `board`, `combats`, `choices`, `plays`, `buys`, `sells` | the pool view of the game (`PoolGame`) + the BG replay's buy/sell steps |
| `app` | app version | |

Not sent: Friendly, Practice, Mercenaries and other modes; abandoned games and games without a result; games brought
over from another tracker. Battlegrounds games outside public matchmaking (a friendly lobby or a game against the AI:
every type with `BATTLEGROUNDS` but `GT_BATTLEGROUNDS`, `_DUO` and `_DUOS`) stay in History and My Stats but are sent
neither as records nor as shared games nor to the profile (`GameRecords.IsPrivateBattlegrounds`, owner 2026-11-26).
Lists are cut at the server's limits (400 plays, 120 choices, 300 draws, 6 mulligan cards).

**Only what the player saw** (ADR 0005). A Constructed replay is read with `MatchReplay.ParseSeen`, which leaves out the
post-game part (`N`/`K` lines: what the log revealed at the end about card backs), so an opponent card that was never
shown (a secret that did not trigger, a card left in hand) has no name in the record. No player name, BattleTag,
account id, deck name, legend number or exact rating. `GameStatsRealLogTests` checks this on the kept logs; the
server rebuilds every record from known fields only, so anything else is dropped.

## On by default, one opt-out switch

- **On by default** (the owner's decision, 2026-11-06: no consent prompt). The legal minimum kept for pseudonymous
  data: one opt-out switch, the notice in the installer (`installer/*/notice.txt`, README: GAME STATISTICS) and section
  9 of the site's privacy policy, and deletion of an install's records (switching off).
- The switch, Settings → Data: «Отправлять мои игры для статистики Hearthpulse» / "Send my games to Hearthpulse
  statistics"; its tooltip lists every field, the line under it says it is on by default, what is never sent, the
  180 days, that the statistics may be used in paid features, how to turn it off and that this deletes the sent games.
  Shown only once the pool is set up (`PoolClient.Available`); nothing is sent before. Stored as
  `AppSettings.GameStatsOff` (false = sending).
- **No in-app notice** (the owner's decision, 2026-11-17). Earlier versions showed a one-time notice above the page
  in the main window («Hearthpulse отправляет ваши игры для своей статистики», «Понятно» / «Выключить») and sent
  nothing before it was answered. It is gone from both UIs, and no flow waits for it: the Settings → Data switches are
  the only control. The installer notice and the privacy policy still describe the flows.
- **Shared games of the stats pool** (`/v1/games`, `/v1/constructed`: public Battlegrounds totals and Discover-style
  picks) are a separate purpose with a switch of their own, and since the owner's decision of 2026-11-15 they are
  **on by default with an opt-out too, with no consent question** ("we do not ask whether to share").
  - `AppSettings.PoolConsent`: `false` is the player's refusal and is always kept (the switch, «Выключить» on the
    notice earlier versions showed, or a "no" to the old question); `true` and `null` share.
  - The switch, Settings → Data: «Делиться моими играми с пулом статистики» / "Share my games with the stats pool".
    Off stops every later send, including the remaining batches of the one-time 30 days of earlier games
    (`Core/Hosting/PoolSharingHost.cs` asks before each batch). These routes have **no deletion on request**: what
    was shared expires after 60 days.
- Records are pseudonymous personal data (ADR 0011): only totals over 10 games and 5 installs are ever shown.
- Switching off: the queue is emptied, and when a batch was ever handed to that pool `POST /v1/events/forget`
  deletes every record of this install there; the request waits in the queue (`game-stats/<address>/forget`) until
  the pool it is for answers. A refusal before anything was sent makes no request. Queues kept for other pool
  addresses are emptied too (see "Queue destination").

## Sending rule

On by default, no consent question, no notice, and an explicit refusal is kept for ever. Each flow needs a pool
address (`PoolClient.Available`) and the absence of its refusal:

| Flow | Sent while | Refusal that is always kept |
|---|---|---|
| Shared games (`/v1/games`, `/v1/constructed`) `PoolClient.Sharing` | `PoolConsent != false`; a pool address | `PoolConsent == false` |
| Game records (`/v1/events`) `GameStatsClient.Sharing` | `GameStatsOff == false`; a pool address | `GameStatsOff == true` |
| Source journals (`/v1/captures/*`) `SourceCaptureClient.Sharing` | `GameStatsOff == false`; a pool address | `GameStatsOff == true` |

- The fields the notice of earlier versions wrote to settings.json (`GameStatsNoticeShown`, `GameStatsNoticeVersion`,
  `SourceCaptureNoticeVersion`, `PoolConsentVersion`) are ignored when it is read and dropped on the next save. A
  player who answered «Понятно», or never saw the notice, shares; one who pressed «Выключить» keeps both refusals.
- The install secret (`AppSettings.PoolSecret`) is made on first use while sharing is on: the first game record or
  shared game, or the start of source capture; never by a refusal (a refusal reuses a saved one or goes without).
- The game records switch speaks for game records and journals only; it does not touch `PoolConsent`.
- Tests: `GameStatsTests.Sharing_is_on_by_default…`, `…Settings_left_by_the_old_notice_send_to_the_default_pool…`,
  `PoolTests.OldSettingsWithTheNoticeFieldsStillLoadAndKeepTheirRefusals`, `PoolSharingHostTests.Settings_left_by_the_old_notice…`,
  `SourceCaptureRecorderTests.Excluded_modes_and_disabled_capture_emit_nothing`,
  `SourceCaptureHostTests.Capture_starts_by_default…`, `GameStatsSharingTests` (both apps).

## Queue and sending

- `game-stats/<address>/` in the data folder (one folder per pool address, see "Queue destination"), one JSON file
  per record; at most 300 records, 4 MB and 14 days (oldest dropped).
- Batches of up to 20, 12 s apart (the pool takes 6 requests a minute from one address). `2xx` removes the batch;
  `400`/`413`/`422` drop it for good; anything else (offline, `429`, `503` daily budget, `404` before the deploy) waits
  and retries after 30 s, doubling up to 30 min. Never on the UI or log thread; nothing waits for it.
- The install id is a keyed hash of the pool secret (`GameStatsClient.InstallId`), never shown and never linked to an
  account. The secret is made by the first record queued while sharing is on and saved before the first batch.

## Queue destination

The install id and the record ids are keyed hashes of the pool secret and are the same for every pool address, so a
queue must not follow the address in the settings: a record or a forget queued for a test pool would otherwise go
to production when `PoolEndpoint` is removed. Each queue therefore belongs to one address
(`Core/GameStats/GameStatsQueues.cs`, `Core/Pool/PoolDestination.cs`).

- **Address.** `PoolDestination.Normalize`: `https://host[:port]/path`, host in lower case, no trailing slash. An
  address that is not https or carries credentials, a query or a fragment is no destination: nothing is queued or
  sent for it. The address is local metadata only; it is never part of a request body, and it is not written to the
  log.
- **Layout.** `game-stats/<16 hex of the address hash>/` holds that pool's records, its `forget` mark and the file
  `destination` (`{"v":1,"endpoint":"https://…"}`), written before the first record. The folder name must be the
  hash of the address in the file.
- **Queueing.** A finished game is queued for the address read when the game ended, also when the address changes
  while its record is built. It also carries the withdrawal count of that moment (`GameStatsQueues.Generation`):
  once sharing was turned off in between, the record is not queued, also when sharing is back on before the build
  ends. What was withdrawn does not come back.
- **Sending.** A pass serves the pool the settings name now and sends to the address stored with the queue. Before
  the forget and before every batch the current address is read again; when it is no longer the queue's, the pass
  stops and the rest waits in its folder. An answer removes records only from the queue the batch was taken from.
- **"Something was sent" mark.** The file `sent` in a queue folder is written before the first batch is handed to
  that pool (before, because an answer that never arrives may still have stored it) and removed when the pool
  confirmed a forget. Only a queue with the mark gets a forget: a first refusal before anything was sent makes no
  request. A forget whose install secret is gone is dropped with a log line instead of being
  sent under a new id. The mark is local metadata, like the address; no payload changes.
  One-time step for the upgrade (`game-stats/layout`): when this layout first runs in a data folder whose settings
  show that an earlier version could already send (a saved secret), the pool named
  then is conservatively marked even if sharing is now off: a previous process may have failed to write its mark.
  The uploader captures that evidence when constructed, before this run changes the settings, and persists the used
  mark before committing the layout. Failed writes are retried, including after restart; refusal before Start does
  not lose the deletion request. A fresh refusal with no secret still makes no request.
- **Durable identity.** A batch is sent only after the install secret was saved successfully with `SaveRequired`.
  A save failure keeps the batch local and retries later, so a restart cannot lose the id needed for deletion.
  Likewise a source generation's `sent` state is published in memory only after its atomic disk write succeeds.
- **Other addresses.** Their queues are kept, not sent, and sent again in the usual way when the settings name that
  address again (also after a restart). They keep the 300 records / 4 MB they were filled under, and the 14 days are
  applied to them on every pass; a folder left with no record, no forget and no `sent` mark is removed. A forget
  waits for its own address with no time limit and is never sent elsewhere.
- **Switching off** empties every queue: the current pool's, the other addresses', what was set aside and anything
  an earlier version left in the root, moved or not. Each queue with the `sent` mark gets a forget, which waits until
  the settings name that address; queues without an address are deleted. While sharing is off this is repeated on
  every pass, so it does not depend on the click or on the move of the earlier queue having worked.
- **Unknown destination.** A queue folder whose `destination` is missing next to data, unreadable, or names another
  address than the folder is renamed to `game-stats/unknown-<8 hex>/` (when the settings name its address, or on the
  next pass otherwise): never sent, never adopted, dropped after 14 days or on switching off; its forget mark cannot
  be delivered, is dropped and does not count as waiting. The pool gets a fresh queue. One log line, nothing throws.
- **Queue of an earlier version** (record files and `forget` directly in `game-stats/`). It names no pool and may
  have been filled for any, so it is **never** given to the pool the settings name now, whether that is the default
  or an explicit `PoolEndpoint`: "filled for A, settings say B by the time of the update" would send A's games to B.
  The records are set aside as an unknown destination (deleted at once when sharing is off). The move is tried on
  every pass until it works; what stays in the root meanwhile is never sent.
  Its `forget` does not know the pool it was meant for either. It is **not delivered**: sending it to whichever pool
  is current would not reach the pool that holds the records. It is dropped with the log line
  `Game stats: earlier deletion request names no pool, cannot be delivered, dropped`. The records it asked to delete
  stay on that pool until they expire there (180 days at most, `e` table retention); the player can still have a pool
  delete them by naming it in `PoolEndpoint`, sending once and turning sharing off.
- **Seen from outside.** Log lines, one per event, with counts and no address:
  `Game stats: queue for another pool kept, N record(s), M deletion request(s)` (when the counts change),
  `Game stats: earlier queue names no pool, set aside, N record(s) not sent`,
  `Game stats: earlier deletion request names no pool, cannot be delivered, dropped`,
  `Game stats: queue with an unreadable destination set aside, …`,
  `Game stats: a record built across a withdrawal was not queued`,
  `Game stats: deletion request without an install id dropped`,
  `Game stats: dropped N expired record(s) kept for another pool`,
  `Game stats: sharing is off, deleted N record(s) kept for other pools, …`.
  `GameStatsUploader.State` (`Pending`, `HeldRecords`, `HeldDeletions`) feeds the status line under the switch in
  Settings → Data of both apps (`MainViewModel.SourceCaptureStatusText`): «Сохранено для другого адреса пула
  статистики…» / "Kept for another stats pool address…" and «Запросы на удаление ждут другого адреса…» are states of
  their own, apart from off, unavailable and an empty queue. Reports are counted and raised under one lock and
  carry a growing `Revision`; the view model drops a report older than the one it shows.
- Tests: `GameStatsTests.Destination.cs` (address change, removed override, held answers both ways, restart, the
  earlier queue never adopted, unreadable destination, forget per address and only after a send, first refusal,
  a record built across a withdrawal, sharing off against leftovers, report order, expiry).

## Server (`server/pool`)

- `POST /v1/events` `{"v":1,"iid":"<32 hex>","games":[…]}`, 1-20 records, body up to 512 KB, one record up to 24 KB
  after validation; 8,000 records a day in all (`EVENTS_DAILY`, then `503`), plus the Worker's rate limiter.
- `POST /v1/events/forget` `{"iid":"…"}` → `{"ok":true,"deleted":n}`.
- `GET /v1/events/summary` → `{window, updated, games, days:[{day, mode, fmt, games}], discover:[{kind, card, offered,
  picked, rate}], mulligan:[{card, offered, kept, rate}]}` over the last 30 days, each cell from 10 games and 5
  installs or more;
  rebuilt by the 6-hourly cron job when new records came in (the Node service builds it on a read), never cached
  (`no-store`): a forget must not leave totals behind.
- `GET /v1/events/mulligan-pairs?fmt=standard|wild&cls=<CLASS>` → `{v, metric, rules, fmt, cls, window, updated,
  minGames, minInstalls, pairs:[{a, b, installs, both:[games, wins], onlyA, onlyB, none}]}`: opening-hand pairs of
  Ranked games over the last 30 days (metric `mulligan.pair_winrate`, [analytics-metrics.md](analytics-metrics.md)).
  The window is `day` in [today − 30, today]: a record dated tomorrow (a clock running ahead) waits a day.
  A pair needs 200 games with both cards kept, of 5 installs or more; a comparison cell under 5 installs or 10 games
  is `null`. Built with the summary, withdrawn with it by a forget or the clean-up, answered `no-store`; a missing or
  malformed `fmt` / `cls` is a `400`. It reads the fields a record already holds: nothing new is sent, and the
  notice does not change. The app reads it in `Core/Pool/MulliganPairs.cs` for the mulligan panel's pair lines
  (an observed association, shown with an approximate interval; the advice does not count it).
- **Deletion fence.** A forget and the clean-up delete the records, both published views (`snap` rows `events` and
  `mull-pairs`) and raise row `events-rev` of `snap` in one batch. A build reads that row before the records and
  writes both views in one batch, each on the condition that the row has not moved; if it has, nothing is written
  and the build counts again (three times, then the read answers `503` with `Retry-After`). A forget between a
  build's reads and its write is therefore never published by that build; D1 has no transaction across the reads,
  the Node service's request queue alone would not cover the Worker.
- The app keeps a pair answer 6 hours on disk and keeps no older copy for the time the pool is down: a withdrawn
  pair is not shown from an old file.
- Table `e` (`WITHOUT ROWID`, indexes by day and install), migration `migrations/0007_events.sql`; kept 180 days.
- Tests: `node --test server/pool/test/events.test.mjs`.

## Checking a build

1. A copy of the data folder (`HEARTHPULSE_DATA`), `"PoolEndpoint": "https://<pool>"` in its `settings.json`
   (game records are on by default).
2. Play or finish a game; the log has `Game stats: sent 1 game(s)`.
3. `GET <pool>/v1/events/summary` (after the next cron run, or `npx wrangler dev --test-scheduled`) counts it.
