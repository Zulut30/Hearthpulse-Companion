# Source captures for Hearthpulse statistics

Source capture preserves a safe, ordered projection of the observed game protocol so the server can decode it
again when its parser improves. It supplements the compact records in [game-stats.md](game-stats.md); it does not
upload arbitrary personal log files. Source format, decoder version and derived-statistics version are separate.

The Linux deployment targets the existing Hearthpulse server, with its own process, private archive and database.
It does not use the site's account database. The API and the .NET decoder are independent processes; a successfully
stored source remains available when decoding fails. Deployment settings and limits live with `server/pool`.

## What is captured

The v1 JSONL protocol retains ordered visible entity observations, selected numeric tags, blocks, local choices
with ordered alternatives, actions, visible board/shop snapshots, draft choices and explicit gaps. Each record has
a contiguous sequence number, turn, scoped references and versioned payload. Repeated alternatives remain repeated;
a card instance is distinct from its card definition. Draft captures are independent of completed matches.

Source v2 adds only public context and labels: the game's format for Ranked/Casual
(`standard|wild|classic|twist`), the Arena kind (`normal|underground`, also on draft captures), the client's choice
type (`mulligan|general|other`) and a named `combat_view` gap that marks every Battlegrounds game source as not a
replay of the displayed combat. All records of a capture share one version, recorded as `manifest.sourceVersion`; a
capture spooled by an older app is finished and sent as v1 unchanged. Contract details: the
[CLI contract](../tools/Hearthpulse.SourceWorker/README.md#source-v2-and-result-v2).

Source v3 (current recorder, 2026-11-19) is v2 plus one record kind, `bg_combat`: the fights of a Battlegrounds game
(below). A v1/v2 line of that kind is invalid; everything else reads as v2.

There is no player name, BattleTag, account id, deck name, chat, authentication/network log or machine path in the
wire format. Other players' hidden cards, shop or future post-game reveals are not a source. References across
opponent conceal/reveal boundaries must be renewed so an entity cannot track a hidden shuffled card.

Capture is a typed safety boundary. Unknown or unavailable observations produce a gap/partial state rather than
an arbitrary string payload or an invented zero. A newly supported semantic interpretation can reuse saved source;
a new capture field or source sometimes needs a client update. The v1 allowlist does not imply coverage of every
Hearthstone mechanic or a guarantee of complete data from every installation.

## Battlegrounds fights (`bg_combat`, source v3)

Purpose: measure the combat simulator on at least 500 fights it was not tuned on (owner, 2026-11-19; the task card
TASK-2026-11-19-sim-all-mechanics, package U). Same switch as the rest of the journal: on by default, off with
"Send my games to Hearthpulse statistics"; no consent question and no notice.

- **When.** At the end of a Battlegrounds or Duos game the recorder asks the tracker for the game's finished fights
  and writes one `bg_combat` record per fight just before `end` (`SourceCaptureRecorder.Combats`,
  `GameSession.SourceCombats`). The journal is sent only after `end` is spooled, so no fight leaves during the game. A
  fight whose combat never ended is not one. A historical catch-up replay records nothing, as before.
- **From where.** An allowlisted copy of the fights the tracker holds in memory (`SourceCaptureCombat.FromGame`), the
  same ones the local `combat-captures/` file keeps; that file itself is not read or sent.
- **What (the allowlist).** Envelope `side:"none"`, `entity:0`, `card:null`, `target:0`; data, every field always
  present: `fight` (the combat's number in the game, 0–63, growing), `turn`, `oursFirst` (true/false/null),
  `firstAttacker`, `damageCap`, `countDamage` (false in Duos), `ours`/`theirs`, `facts`, `result` (`W|T|L`),
  `damageTaken`, `damageDealt`, `odds` (null or `win, tie, loss, lethalTaken, lethalDealt` rounded to 4 decimals,
  `unsimulated`, `runs`: what the app showed when the game ended) and `transcript`.
  A side: `hero`, `heroPower`, `extras` (player-level cards: hero powers, trinkets, quest rewards, anomaly, Deity), `tier`,
  `health` (hero health with armor), `turn`, `tags` (only the player counters effects read: `BACON_BLOODGEMBUFFATKVALUE`,
  `BACON_BLOODGEMBUFFHEALTHVALUE`, `TAVERN_SPELL_ATTACK_INCREASE`, `TAVERN_SPELL_HEALTH_INCREASE`,
  `BACON_OLD_GOD_ATTACK`, `BACON_OLD_GOD_HEALTH`, `4914`, `4915`), `cardTags` (the local player's player-level card
  values `TAG_SCRIPT_DATA_NUM_1/2`; always `{}` for the opponent), `minions`. A minion: `entity`, `card`, `golden`,
  `attack`, `health`, `maxHealth`, `baseHealth`, `rebornAttack`, `keywords` (Taunt, Divine Shield, Poisonous, Venomous,
  Reborn, Stealth bits), `attacks` (Windfury), `baseKeywords`, `tribes`, `mechanics`, `tier`, `script1`, `script2`,
  `creator` (card id), `enchantments` (card ids), `remembered` (minions a start-of-combat trigger removed, one level),
  `unsimulated`. A fact: `entity`, `card` (as logged), `markers` (`DEATHRATTLE TRIGGER_VISUAL AURA AVENGE OVERKILL
  BACON_RALLY`).
- **Never.** Either hand (the player's own included), the opponent's card values, other player tags (ratings, ids),
  names, BattleTags, account ids, paths or text. Entity ids are fight-scoped references 1.. in Power.log order (the
  simulator only compares them), not the game's ids. Both boards are visible during the combat; the opponent's player
  counters and minion script values are the same inputs the in-game odds already use.
- **Limits** (client, decoder and pool alike): 64 fights a game, 7 minions a side, 7 remembered per minion (none
  nested), 32 enchantments a minion, 16 player-level cards and values a side, 14 facts, references up to 1000, stats
  0–1 000 000, data at most 56 KiB (inside the 64 KiB line limit). A fight outside them is left out whole and counted
  in the log (`Source capture: N fight(s) left out of the journal`), never cut, so the journal is never refused for it.
- **Transcript.** Reserved for the fight step by step: `null` in this version; anything else is refused until its
  allowlist is written. Bounds fixed now: format version 1, at most 512 steps and 16 KiB.
- **Rules in code.** `SourceCaptureCombat` (app and decoder) and `validCombat` in `server/pool/node/validation.mjs`;
  `server/pool/test/bg-combat.fixture.json` is checked by both test suites (`SourceCaptureCombatTests`,
  `combats.test.mjs`). A new tribe, keyword or mechanic bit, marker, card value or player counter changes all three
  (`The_wire_lists_are_the_ones_the_simulator_reads` fails first).
- **Decoder.** Reads v3; a `bg_combat` adds no fact and no gap, its event keeps no data. Parser version unchanged
  (`source-v2.0`): v1/v2 sources decode as before.
- **Pool.** On the manifest commit the fights go to table `bg_combats` (migration `0010_bg_combats.sql`) in the same
  transaction, or the commit answers 503 to retry under storage pressure. They leave with their capture: expiry (the
  archive retention, 180 days), forget of the generation, deletion. Nothing is published from them; `/healthz` shows a
  count (`bgCombats`).
- **Export (owner only).** `POOL_OPERATOR_TOKEN=… node server/pool/node/export-combats.mjs --out fights.jsonl` on the
  pool host (loopback route `POST /v1/captures/combats/export`, operator token like reprocess; nginx refuses it). One
  line per fight: `{"v":1,"game":"<16 hex of a hash of the capture key>","day","build","mode","fight"}`, no
  installation, generation or capture key. Read with `Hearthpulse.Bench simcover --pool fights.jsonl`.
- **Order of deployment.** A pool without v3 refuses a v3 journal (`400`), and the app then retries it and holds the
  journals behind it until the pool is updated or 14 days pass: deploy `server/pool` before releasing an app that
  writes v3.

## Delivery and lifecycle

Sources are written to a separate durable spool, compressed and sent in chunks with SHA-256 over the exact
uncompressed bytes. Receipts must match the submitted index and hash before a chunk is acknowledged locally.
The manifest is committed after all chunks; `stored`, `processing`, `parsed`, `partial` and `failed` are distinct
states. Missing parts, conflicting hashes, rejected data and revoked generations are not successful delivery.

Source sending follows the existing game-statistics switch (the table in [game-stats.md](game-stats.md), "Sending
rule"); since 2026-11-17 no in-app notice gates it. The Settings → Data and installer texts describe the expanded
fields, the Hearthpulse server and retention. The production endpoint is configured by default since 2026-11-16, after publication of the policy and the owner pilot
(see source-capture-verification.md). Public release remains gated on
server disk capacity and intake quotas; the pilot does not establish published analytics.

Turning sending off stops capture, clears pending source and persists a forget request. The server revokes the
installation generation, removes its archive/derived contributions and prevents late uploads or jobs from restoring
them. An HTTP 202 deleting response is not final confirmation. A new generation is created only after an explicit
subsequent enabling action. Source identities are unrelated to site accounts and are never put in request URLs.

### Queue destination

A spool belongs to one pool address, the endpoint its generation was made for (`state.json`), because the install key
is the same for every pool: sources and deletion requests of a test pool must not reach another host when
`PoolEndpoint` changes or is removed. The address is `PoolDestination.Normalize` (`https://host[:port]/path`, lower
case host, no trailing slash; credentials, a query or a fragment make it no address). It is local metadata and never
part of a payload or a log line.

- **Layout.** `source-capture/<16 hex of the address hash>/` is the spool of one address (`SourceCaptureQueue.Folder`):
  its `state.json` (install key, generation, endpoint, deletion requests) and its sources. `SourceCaptureHost` opens
  the spool of the address the settings name now; a new address gets a spool and a generation of its own. When the
  address changes, also without a settings event, the old spool is flushed and closed untouched.
- **Sending.** Before every chunk, manifest and deletion request the sender compares the pool the settings name at
  that moment with the endpoint in the spool's state, and sends nothing while they differ or no pool is set up. An
  answer that arrives after the address changed settles nothing locally: the chunk or manifest is sent again when its
  pool is back (the server stores a repeated chunk once).
- **Other addresses.** Their spools keep sources and deletion requests and are sent when the settings name that
  address again, also after a restart. The 14 days are applied to their sources on every delivery pass (every 30 s
  while the app runs) and whenever settings are applied; an open spool that turns out to be another pool's applies
  them too. The 2 GB budget is per spool. A deletion request waits for its own address without a time limit.
- **"Something was sent" mark.** `state.json` carries `sent` for the current generation: `false` from its creation,
  `true` from just before its first chunk is handed to the pool (before, because an answer that never arrives may
  still have stored it). A state written before the mark existed has none and counts as sent. Local metadata only.
- **Switching off** revokes the current spool's generation as before, and opens every other address's spool to delete
  its sources and revoke its generation. A deletion request is persisted only for a generation that may have reached
  its pool (`sent` not `false`), and waits for that address; a refusal before the first chunk left makes no request.
  The host makes no install secret for a refusal (`SourceCaptureHost.ApplySettingsAsync`). A
  spool set aside without an address is deleted, and so are the sources of a spool whose state cannot be read and
  whatever an earlier version left in the root that could not be moved; no deletion request can be made for those.
- **Returning** to an address that was left by an opt-out does not resume capture by itself (a revoked generation is
  never resumed): it captures again after the next explicit enabling, as on one address.
- **Spool of an earlier version** (`state.json` and sources directly in `source-capture/`), moved before a spool is
  opened; a move that fails is tried again whenever settings are applied, and nothing in the root is sent meanwhile. Its state names its pool, so the sources go to that address's folder whatever the settings say
  now, and each deletion request goes to the folder of the address it carries (a request of another address keeps
  the install key there, so that address needs an explicit enable). A request without a valid address is dropped
  with a count in the log. Unreadable state means an unknown pool: the spool is renamed to
  `source-capture/unknown-<8 hex>/`, never opened or sent, dropped after 14 days or on switching off.
- **Unreadable or contradicting state of an address's spool.** Unreadable `state.json`, also one that parses but
  lacks its list of deletion requests (`{}`): the host reports `unavailable`, logs `Source capture: unavailable`,
  sends nothing and does not throw; the delivery loop keeps running. A state that names another
  pool than its folder: status `other_pool`, sources kept, nothing sent.
- **Seen from outside.** `SourceCaptureState.Status == "other_pool"` and the counts `HeldCaptures` / `HeldDeletions`
  are apart from `disabled`, `unavailable` and `offline`; Settings → Data shows them in both apps
  (`MainViewModel.SourceCaptureStatusText`, strings `Settings.Data.HeldData` / `HeldDeletions`). The host publishes
  its state under one lock with a growing `Revision`; the view model drops a report older than the one it shows. Log lines, one per
  event, with counts and no address: `Source capture: queues for other pools kept, N capture(s), M deletion
  request(s)` (when the counts change), `Source capture: queue for another pool kept, …` (an open spool stopped for
  the address), `Source capture: earlier queue kept for its own pool, …`, `Source capture: earlier queue has
  unreadable state, set aside, …`, `Source capture: dropped N expired capture(s) kept for another pool`,
  `Source capture: sharing turned off, cleared N queue(s) kept for other pools`,
  `Source capture: nothing was sent for this generation, no deletion request`.
- Tests: `SourceCaptureDestinationTests` (chunk, manifest and forget across an address change, removed override,
  held answers, restart, both earlier-version cases, unreadable and incomplete state, opt-out with and without a
  send, first refusal, a failed move, report order, expiry on a delivery pass).

The target retention is 180 days, subject to the configured service/archive budget and a verified capacity plan.
Disk pressure rejects new intake for retry; it must not acknowledge data it discarded. Pilot deployment has an
explicit storage ceiling. Server backups and restore procedures must preserve revocation/deletion semantics.

## Decoder and aggregate eligibility

`tools/Hearthpulse.SourceWorker` is a standalone .NET 10 command with a self-contained Linux build. It validates
the JSONL envelope, numeric/card/tag restrictions, visibility, boundaries, sequence, source/line/record budgets,
choices and state, then writes an atomic versioned derived result. Error output contains fixed technical codes,
never source snippets or private paths. See [the CLI contract](../tools/Hearthpulse.SourceWorker/README.md).

The decoder hash identifies exact source bytes; `parserVersion` identifies its interpretation. Reprocessing
replaces the contribution for a capture rather than adding a second game. Publication must recheck that the source
generation has not been revoked, even when the worker has already finished.

Structural completeness and completed-game eligibility are separate. `completeForAggregation` requires a normal
complete game without unresolved observations and the appropriate outcome: W/L/T for Ranked/Casual/Arena/Brawl,
placement 1–8 for solo Battlegrounds, or team placement 1–4 for Duos. A complete source without that outcome can
still preserve actions but cannot enter completed-match result aggregates. A draft is never a completed match.

Result v2 (`source-v2.0`) keeps that flag unchanged and adds per-metric eligibility ([ADR 0013](adr/0013-per-metric-eligibility.md)):
`eligibility.outcome` and `eligibility.offers` with fixed reasons, plus the `facts` they admit (the visible result or
place; the Battlegrounds hero offer at the moment of the pick). A v2 Battlegrounds game can be fit for the place and
the hero offer while remaining `partial`; v1 Battlegrounds sources are never fit (`source_v1`). Metric definitions:
[analytics-metrics.md](analytics-metrics.md).

## Verification boundaries

Tests cover synthetic sources, malformed/bounded input, hidden-reference rejection, receipts/retry/deletion and
deterministic reprocessing. Separate acceptance requires real recorder input, both UI hosts and Linux/HTTPS
deployment. A cross-publish is not proof of Linux execution, and synthetic fixtures are not live-game coverage.
