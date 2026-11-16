# Game records for Hearthpulse's own statistics

The compact-record contract below remains supported by the isolated Linux service on server 151. The expanded
ordered journal, updated notice, durable generation deletion and synthetic-pilot activation limits are documented
in [source-capture.md](source-capture.md). The default endpoint is the production pool since 2026-11-16
(`PoolClient.DefaultEndpoint`): the public policy matches the host and the journal, the pool's data is outside the host
backups, and nothing is sent before the one-time notice. Older Cloudflare deployment instructions are historical.

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
over from another tracker. Lists are cut at the server's limits (400 plays, 120 choices, 300 draws, 6 mulligan cards).

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
- **One-time notice** in the main window, both UIs («Hearthpulse отправляет ваши игры для своей статистики»: what is
  sent, what never, 180 days, how to turn off; buttons «Понятно» / «Выключить» / «Подробнее» → the policy). Not a
  consent question: it tells players who updated silently and never saw the installer. Nothing is sent before it was
  shown (`AppSettings.GameStatsNoticeShown`). The install secret is made when the notice is answered or the switch
  goes on, not before.
- **Shared games of the stats pool** (`/v1/games`, `/v1/constructed`: public Battlegrounds totals and Discover-style
  picks) are a separate purpose with a switch of their own, and since the owner's decision of 2026-11-15 they are
  **on by default with an opt-out too, with no consent question** ("we do not ask whether to share"). The question
  banner is gone from both apps; the same one-time notice tells of them.
  - `AppSettings.PoolConsent`: `false` is the player's refusal and is always kept (the switch, «Выключить» on the
    notice, or a "no" to the old question); `true` and `null` share.
  - Nothing goes to the pool before the notice of `PoolClient.NoticeVersion` (5) was shown; the version seen is
    stored in settings.json as `PoolConsentVersion` (1-4 were answers to the old question, so everyone who answered
    it sees the notice once). The notice shows for whichever of its texts the player has not seen and has not turned
    off; «Понятно» never turns back on what was turned off, «Выключить» turns off game records and shared games.
  - After the notice the whole list it names is sent; there are no partial lists by version any more.
  - The switch, Settings → Data: «Делиться моими играми с пулом статистики» / "Share my games with the stats pool".
    Off stops every later send, including the remaining batches of the one-time 30 days of earlier games
    (`Core/Hosting/PoolSharingHost.cs` asks before each batch). These routes have **no deletion on request**: what
    was shared expires after 60 days.
- Records are pseudonymous personal data (ADR 0011): only totals over 10 games and 5 installs are ever shown.
- Switching off: the queue is emptied and `POST /v1/events/forget` deletes every record of this install; the request
  waits in the queue (`game-stats/forget`) until the pool answers.

## Queue and sending

- `game-stats/` in the data folder, one JSON file per record; at most 300 records, 4 MB and 14 days (oldest dropped).
- Batches of up to 20, 12 s apart (the pool takes 6 requests a minute from one address). `2xx` removes the batch;
  `400`/`413`/`422` drop it for good; anything else (offline, `429`, `503` daily budget, `404` before the deploy) waits
  and retries after 30 s, doubling up to 30 min. Never on the UI or log thread; nothing waits for it.
- The install id is a keyed hash of the pool secret (`GameStatsClient.InstallId`), never shown and never linked to an
  account. Turning the switch on makes the secret at once, so the settings saved with the change keep it.

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
