# What leaves the machine

Snapshot of the documented flows on 2026-11-15. The linked doc and the code are the source of truth: when a row
disagrees with them, fix the row in the same change. Paths are from the repository root.

| Flow | Default and switch | Identifier | Kept | Deletion | Spec |
|---|---|---|---|---|---|
| **Shared Battlegrounds and Constructed games** to the stats pool (`/v1/games`, `/v1/constructed`) | on by default with an opt-out, no consent question (owner, 2026-11-15): sent while `PoolClient.Sharing` holds, i.e. `AppSettings.PoolConsent` is not `false` (the kept refusal: the Settings → Data switch or "Turn off" on the notice) and the one-time notice of `PoolClient.NoticeVersion` was shown (`AppSettings.PoolNoticeVersion`, `PoolConsentVersion` in settings.json); `PoolHistoryShared` records that earlier games were sent once, and switching off stops the remaining batches | game id: HMAC of the game with a key that never leaves the PC | 60 days, totals over the last 30 | rows expire after 60 days; this route has no forget call (verify in `server/pool/src/index.js` before promising one) | `server/pool/README.md`, `docs/bg-meta-sources.md` |
| **Game records** of every tracked mode (`/v1/events`) | on by default once the pool is set up and the one-time notice was shown; off = `AppSettings.GameStatsOff`; notice flag `GameStatsNoticeShown` | install id: keyed hash of the pool secret, made only when the notice is answered or the switch goes on | 180 days; local queue `game-stats/` at most 300 records, 4 MB, 14 days | switching off empties the queue and sends `POST /v1/events/forget`, retried until the pool answers | `docs/game-stats.md` |
| **Source captures** (`/v1/captures/*`) | follows the game-records switch **and** needs the current source notice (`SourceCaptureNoticeVersion`) | install id + generation; fact rows keep `owner`, a hash of the install id | target 180 days, bounded by the archive budget | off = stop capture, clear the spool, persist a forget request; the server revokes the generation, removes archive, facts and snapshot contribution; `202` is not final | `docs/source-capture.md`, `docs/analytics-api.md` |
| **Games to the hearthpulse.net profile** (`tracker/events/batch`) | on by default while signed in; `AppSettings.UploadGamesToProfile`; nothing without an account | the account's token; event id UUID v5 per game, without the BattleTag | the site's policy | switch off drops the queue; export and deletion on the site's account page | `docs/profile-upload.md` |
| **Replay by link** (`tracker/replays`) | never automatic: one game, by the button and its confirmation | account token; a delete token per upload in `replays/uploads.json` | 180 days, 200 per account | the card's delete button, the account page, or with the account | `docs/web-replays.md` |
| **Crash reports** (`/api/v1/crash-reports`) | off; "Send report" for one report or `AppSettings.SendCrashReports` | random report id; no `Authorization` header | 90 days on the site without IP or account; locally at most 5 waiting, 30 days | switching off deletes what waits (`CrashReporter.ClearPending`) | `docs/crash-reports.md` |
| **Twitch extension state** (`/api/v1/twitch/state`) | off; `AppSettings.TwitchExtension`, only while signed in; the card itself is hidden until the wide release | account token | the site's side, not verified here | switch off stops sending | `docs/streaming.md` (the player's own deck rows and turn, never the hand, deck order or the opponent) |
| **Requests for data** (data API, HearthstoneJSON, hsbg.cards, GitHub releases, Brawl articles) | always; they send no player data, only `User-Agent: Hearthpulse/<version>` | none | - | - | [hearthpulse-data-sources](../../hearthpulse-data-sources/SKILL.md) |

`PoolClient.DefaultEndpoint` is empty in the shipped configuration: the three pool flows send nothing unless
`PoolEndpoint` is set in `settings.json`. Treat that as the present state, not as a reason to skip their rules.

## What stays on the machine

`history.db`, `replays/` (the full replay with post-game reveals, capped at 300 MB and 3000 files),
`hearthpulse.log` (rotated at 4 MB), `settings.json`, `account.dat` (the signed-in session, protected by
`TokenStore`), `cache/`, `combat-captures/` (the fights of the player's Battlegrounds games as the combat simulator got
them, for measuring it with Bench `simcover --captures`: 500 games or 32 MB, oldest first; no sender reads it, and
`CombatCaptureStoreTests` fails when another source file starts to). The local match page may show everything the log revealed by the end of the game; nothing
derived from the post-game part leaves the machine. Reading Hearthstone's memory is read-only and Windows-only;
the opponents' MMR lookup is off by default under the conditions in `docs/bg-opponent-mmr.md`.

## Anonymous pool payload exclusions

Player and opponent names, BattleTag, account id, e-mail, deck name, legend number, exact rating, chat, the game's
network or authentication logs, machine paths, hidden cards of other players. These anonymous-pool exclusions
are not the authenticated profile's schema: profile payloads have documented rating and time fields and are
associated with the signed-in account. Check `docs/profile-upload.md` and `ProfileEvents` for that separate contract.
