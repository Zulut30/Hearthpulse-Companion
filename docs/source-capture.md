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

Source v2 (current recorder) adds only public context and labels: the game's format for Ranked/Casual
(`standard|wild|classic|twist`), the Arena kind (`normal|underground`, also on draft captures), the client's choice
type (`mulligan|general|other`) and a named `combat_view` gap that marks every Battlegrounds game source as not a
replay of the displayed combat. All records of a capture share one version, recorded as `manifest.sourceVersion`; a
capture spooled by an older app is finished and sent as v1 unchanged. Contract details: the
[CLI contract](../tools/Hearthpulse.SourceWorker/README.md#source-v2-and-result-v2).

There is no player name, BattleTag, account id, deck name, chat, authentication/network log or machine path in the
wire format. Other players' hidden cards, shop or future post-game reveals are not a source. References across
opponent conceal/reveal boundaries must be renewed so an entity cannot track a hidden shuffled card.

Capture is a typed safety boundary. Unknown or unavailable observations produce a gap/partial state rather than
an arbitrary string payload or an invented zero. A newly supported semantic interpretation can reuse saved source;
a new capture field or source sometimes needs a client update. The v1 allowlist does not imply coverage of every
Hearthstone mechanic or a guarantee of complete data from every installation.

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
