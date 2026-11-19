---
name: hearthpulse-privacy
description: Use when a Hearthpulse change adds, widens or reviews anything that leaves the player's machine or is kept about them - a new field in a game record, source capture, profile upload, shared replay or crash report, a new identifier, a consent switch or notice text, retention or deletion of server rows, a log line that could carry a name, token or path, test fixtures made from real games. Engineering rules for minimisation, identifiers, TTL, deletion and consent as they stand in this project; legal risk assessment is the hearthpulse-legal agent. Triggers - "приватность", "персональные данные", "согласие", "что отправляется", "удаление данных", "срок хранения", "BattleTag в логах", "privacy", "GDPR", "retention", "opt-out".
---

> Source: [github/awesome-copilot](https://github.com/github/awesome-copilot) `skills/gdpr-compliant/SKILL.md` and
> `references/data-rights.md` at `8ecd5b1`, MIT, © GitHub, Inc., see [THIRD-PARTY-NOTICES](../THIRD-PARTY-NOTICES.md).
> Hearthpulse changes: kept the engineering principles (minimise, bound retention, delete everywhere, log events
> not data, synthetic test data); replaced the generic web-service rules with this project's data flows, consents
> and recorded owner decisions; dropped RoPA/DPIA/DPA paperwork, password hashing, KMS, cloud and CORS sections.

# Player data in Hearthpulse

> Collect less. Store less. Expose less. Retain less.

Every flow that leaves the machine, with its switch, identifier, retention and deletion path, is in
[reference/data-flows.md](reference/data-flows.md). Read the row of the flow you touch, then its doc.

## What is already decided

These are the owner's recorded decisions. Apply them; report a drift between code, in-app text and docs, not the
decision itself.

- **HSReplay and HSGuru data may be used**: [data-permissions.md](../../../docs/data-permissions.md), 2026-11-15.
  That is permission for third-party statistics, not player data, and needs no second confirmation.
- **Game records are on by default with one opt-out switch**, no consent prompt (`docs/game-stats.md`,
  2026-11-06). Since 2026-11-17 there is no in-app notice either: the Settings → Data switches are the only control,
  and nothing waits for a notice.
- **Shared games of the stats pool are on by default with an opt-out, and the app asks no consent question**
  (`docs/game-stats.md`, 2026-11-15): `AppSettings.PoolConsent == false` is a refusal that no update undoes. A yes/no
  sharing banner, or a notice that gates sending, is a drift from these decisions.
- **Games go to the signed-in profile by default** (`AppSettings.UploadGamesToProfile`, `docs/profile-upload.md`);
  the legal review asked for opt-in and the owner kept opt-out.
- **Crash reports are off by default** and sent only by the button or the switch (`docs/crash-reports.md`).
- **The app has no usage telemetry.** Adding any needs a consent setting first, see
  [hearthpulse-observability](../hearthpulse-observability/SKILL.md).
- **Only our own analytics may be paid** (`docs/adr/0009-paid-only-own-analytics.md`); pseudonymous records feed it
  (`docs/adr/0011-game-records-in-the-stats-pool.md`).

A general "this may have legal implications" is not a blocker and not a finding. A finding names the field, the
flow, the rule below it breaks, and the file. Ban risk, licences and a release-level legal read are the
`hearthpulse-legal` agent's report.

## Rules for a change

1. **A purpose per field.** A new field in anything uploaded names the number or feature that needs it today. No
   field "in case it is useful". Coarsen before sending: league instead of legend number, rating rounded down to
   500, day instead of timestamp, as the existing records do.
2. **Only what the player saw** (`docs/adr/0005-hidden-information.md`). An uploaded Constructed game is read
   without its post-game reveals (`MatchReplay.ParseSeen`); a shared replay goes through `ReplayImport.Sanitize`.
   Opponent cards that were never shown have no name anywhere outside the player's own match page. One recorded
   exception (owner, 2026-11-19): the Battlegrounds fights of the journal (`bg_combat`, `docs/source-capture.md`) carry
   simulator inputs the client may not show (the opponent's player counters, his minions' script and enchantment values
   and `creator`), sent after the game, only to measure the simulator, never published. It does not widen to anything else.
3. **Anonymous pool payloads exclude direct identifiers**: player or opponent name, BattleTag, account id, e-mail,
   deck name, exact rating, machine path or IP. The typed source format has no free-text field: an unknown
   observation becomes a gap. Authenticated profile uploads and replay sharing have separate documented
   contracts; preserve their account authorization and allowed fields instead of applying the anonymous pool
   schema to them. A transport peer can observe an IP; do not claim network anonymity.
4. **Keep identifiers separate by purpose.** Shared-game ids and installation identifiers follow the pool's
   keyed-hash contracts. Profile event ids are deterministic UUID v5 (`ProfileEvents.EventId`), not keyed hashes;
   the profile intentionally belongs to the signed-in account. Never link the anonymous install id to that account
   or put it in a URL. A new upload documents its identifier and intended linkability explicitly.
5. **Retention is set when the table is created** and enforced by the service's own job (pool cron or
   `maintain()`), not by hand. A new server table states its TTL in its doc and migration.
6. **Deletion reaches everything derived.** Source-capture forget revokes the generation and withdraws its facts
   and published contribution atomically. Archive-file cleanup may finish asynchronously: track it and do not
   report a `202` as completed deletion. Late uploads and worker results must not restore revoked contributions.
   Legacy shared-game routes expire by TTL and have no forget call; do not promise one without implementing it. Local
   queues are emptied when the switch goes off. A change that adds a store adds it to the forget path and to its
   test. Backups the service does not control are documented as such, never promised as erased.
7. **A wider upload updates every text that describes it.** Fields beyond what the texts name do not ride on them:
   update the Settings → Data switch's tooltip and line in both languages and the installer notices
   (`installer/windows/notice.txt`, `installer/*/README.txt`) in the same change, and tell the owner the site's
   privacy policy needs the same.
8. **The server trusts nothing.** It rebuilds each record from known fields and drops the rest; the site re-checks a
   replay token by token. Client-side filtering is the first line, never the only one.
9. **Published numbers hide small groups**: the thresholds are
   [hearthpulse-analytics](../hearthpulse-analytics/SKILL.md)'s. A read route returns no capture key, generation
   or install id.
10. **Logs carry events, not data.** `Diag` lines and server log lines name what happened and a code, never a token,
    e-mail, BattleTag, request body or source snippet. The pool runs without access logs. Crash text goes through
    `CrashReport.Scrub` before it is written.
11. **Secrets**: tokens only in `TokenStore` (`account.dat`), never in settings, logs or a scratch copy of the data
    folder; nothing secret in the repository.
12. **Fixtures are synthetic or scrubbed.** A fixture cut from a real log or database has names, BattleTags and
    account ids replaced before it is committed. Experiments run on a copy of the data folder without `account.dat`.

## Evidence

A privacy claim is proved by a test that fails when the rule breaks, not by reading the mapper:

- what a record or report may contain: `GameStatsRealLogTests`, `CrashReportTests`, `ReplayShareTests`,
  `ReplayImportTests`, the profile contract tests;
- nothing sent while a switch is off, and the queue emptied when it goes off: the uploader and reporter tests;
- deletion and withdrawal on the server: `server/pool/test/*.test.mjs` (`node --test`).

The scrubber, the share copy and the import filter are mutation-tested (`docs/mutation-testing.md`). Finding what
the tests would miss: [hearthpulse-test-gaps](../hearthpulse-test-gaps/SKILL.md).

## Outside this repository

The site (profiles, accounts, public replay pages, the privacy policy, data export) lives in the HeartPulse
repository. From here you can verify what the app sends and what the pool stores; state the site side as "per the
site's spec, not verified here" unless you read that repository at a known revision.
