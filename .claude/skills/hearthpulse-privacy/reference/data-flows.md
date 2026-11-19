# What leaves the machine

Snapshot of the documented flows on 2026-11-16. The linked doc and the code are the source of truth: when a row
disagrees with them, fix the row in the same change. Paths are from the repository root.

| Flow | Default and switch | Identifier | Kept | Deletion | Spec |
|---|---|---|---|---|---|
| **Shared Battlegrounds and Constructed games** to the stats pool (`/v1/games`, `/v1/constructed`) | on by default with an opt-out, no consent question (owner, 2026-11-15): sent while `PoolClient.Sharing` holds, i.e. a pool is set up and `AppSettings.PoolConsent` is not `false` (the kept refusal: the Settings → Data switch, or "Turn off" on the notice earlier versions showed); no in-app notice gates it since 2026-11-17; `PoolHistoryShared` records that earlier games were sent once, and switching off stops the remaining batches | game id: HMAC of the game with a key that never leaves the PC | 60 days, totals over the last 30 | rows expire after 60 days; this route has no forget call (verify in `server/pool/src/index.js` before promising one) | `server/pool/README.md`, `docs/bg-meta-sources.md` |
| **Game records** of every tracked mode (`/v1/events`) | on by default once the pool is set up, no in-app notice (`GameStatsClient.Sharing`); off = `AppSettings.GameStatsOff` | install id: keyed hash of the pool secret, made on first use while sharing is on (the first record or shared game, or the start of source capture), never by a refusal | 180 days; local queue `game-stats/<address hash>/`, one per pool address, at most 300 records, 4 MB, 14 days | switching off empties the queues of every address and sends `POST /v1/events/forget` to each pool a batch was ever handed to (the current one now, another when the settings name it again), retried until that pool answers; a refusal before anything was sent makes no request | `docs/game-stats.md` |
| **Source captures** (`/v1/captures/*`), including the Battlegrounds fights of source v3 (`bg_combat`: both boards and the simulator's other inputs as it saw them, the fight step by step, the odds shown and the result; never a hand; used only to measure the simulator: never joined with hearthpulse.net profiles, with other installations' captures of the same lobby, or with any outside replay or video) | follows the game-records switch (`SourceCaptureClient.Sharing`) | install id + generation; fact rows keep `owner`, a hash of the install id; fight rows (`bg_combats`) keep the generation, and their owner-only export names a game by a hash of the capture key; an export file is a working copy on the pool host or the owner's PC only (over SSH, never committed or shared), deleted after the measurement and within 180 days of its rows' day, and `simcover`/`simreplay --pool` ignore older rows | target 180 days, bounded by the archive budget; fights go with their capture; local spool `source-capture/<address hash>/`, one per pool address, 14 days | off = stop capture, clear the spools of every address, persist a forget request per address whose generation may have reached its pool (sent only to that address; none when nothing was sent); the server revokes the generation, removes archive, facts, fights and snapshot contribution; `202` is not final | `docs/source-capture.md`, `docs/analytics-api.md` |
| **Games to the hearthpulse.net profile** (`tracker/events/batch`) | on by default while signed in; `AppSettings.UploadGamesToProfile`; nothing without an account | the account's token; event id UUID v5 per game, without the BattleTag | the site's policy | switch off drops the queue; export and deletion on the site's account page | `docs/profile-upload.md` |
| **Replay by link** (`tracker/replays`) | never automatic: one game, by the button and its confirmation | account token; a delete token per upload in `replays/uploads.json` | 180 days, 200 per account | the card's delete button, the account page, or with the account | `docs/web-replays.md` |
| **Crash reports** (`/api/v1/crash-reports`) | off; "Send report" for one report or `AppSettings.SendCrashReports` | random report id; no `Authorization` header | 90 days on the site without IP or account; locally at most 5 waiting, 30 days | switching off deletes what waits (`CrashReporter.ClearPending`) | `docs/crash-reports.md` |
| **Twitch extension state** (`/api/v1/twitch/state`) | off; `AppSettings.TwitchExtension`, only while signed in; the card itself is hidden until the wide release | account token | the site's side, not verified here | switch off stops sending | `docs/streaming.md` (the player's own deck rows and turn, never the hand, deck order or the opponent) |
| **Requests for data** (data API, HearthstoneJSON, hsbg.cards, GitHub releases, Brawl articles, stats pool) | also after opting out of uploads; no game records are sent by these GETs. Pool mulligan-pair queries include format and class; HTTP exposes the network address and `User-Agent: Hearthpulse/<version>` | none | - | - | [hearthpulse-data-sources](../../hearthpulse-data-sources/SKILL.md) |

`PoolClient.DefaultEndpoint` is the production pool (`https://api.kolodahearthstone.com/stats`, since 2026-11-16): the
three pool flows send unless the player refused; an isolated run has no default
pool, and a `PoolEndpoint` that is not https switches the pool off. Before that date the default was empty and the flows sent nothing unless
`PoolEndpoint` was set in `settings.json`; this describes the historical state only.

**A queue belongs to the pool address it was filled for** (game records and source captures; "Queue destination" in
both docs). The install id is the same for every address, so nothing queued for one pool, records, chunks, manifests
or a forget, is sent to another: after `PoolEndpoint` changes or is removed it waits in its own folder and goes when
that address is back. A queue with no readable address is set aside and never sent; so is every game-record queue
of an earlier version, whatever `PoolEndpoint` says now, and its forget is undeliverable (the records expire on
their pool within 180 days). Which notice each flow needs: the table "Notice contract" in `docs/game-stats.md`. The address is local metadata: not in a payload, not in the log. A new upload
queue follows the same rule and gets the same tests (`GameStatsTests.Destination.cs`, `SourceCaptureDestinationTests`).

## What stays on the machine

`history.db`, `replays/` (the full replay with post-game reveals, capped at 300 MB and 3000 files),
`hearthpulse.log` (rotated at 4 MB), `settings.json`, `account.dat` (the signed-in session, protected by
`TokenStore`), `cache/`, `combat-captures/` (the fights of the player's Battlegrounds games as the combat simulator got
them, for measuring it with Bench `simcover --captures`: 500 games or 32 MB, oldest first; no sender reads it, and
`CombatCaptureStoreTests` fails when another source file starts to), `collection.json` (the player's card collection
and dust balance as read from the game client on Windows, `docs/collection.md`: local, it does not leave the machine;
no code sends it, which rests on the sources, on a static check of names in `CollectionStoreTests`, and on
`CollectionPrivacyTests`, a check of what goes over the wire: marker card id and dust in the session's collection, the
usual senders run on a recording handler, no marker in any address, header or body. That is network proof for six
flows (downloads, game record, shared games, source capture, profile upload, crash report); the Twitch publisher,
the replay upload and share are not run by it and rest on the static check alone. A new sender
is added to that test; what it does not cover is listed in `docs/collection.md`. The file carries no account, so it is unconfirmed
until the running client was read, and by an unconfirmed collection the views mark every label and keep the "can
build" filter off). The local match page may show everything the log revealed by the end of the game; nothing
derived from the post-game part leaves the machine. Reading Hearthstone's memory is read-only and Windows-only;
the opponents' MMR lookup is off by default under the conditions in `docs/bg-opponent-mmr.md`.

## Anonymous pool payload exclusions

Player and opponent names, BattleTag, account id, e-mail, deck name, legend number, exact rating, chat, the game's
network or authentication logs, machine paths, hidden cards of other players. These anonymous-pool exclusions
are not the authenticated profile's schema: profile payloads have documented rating and time fields and are
associated with the signed-in account. Check `docs/profile-upload.md` and `ProfileEvents` for that separate contract.
