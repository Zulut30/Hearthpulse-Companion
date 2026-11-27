# Isolated Linux staging (P4 operates; P2 does not deploy)

This is a separate single-writer Node API, not an upgrade of the site's runtime, database, nginx server or Docker
volumes. Default bind is `127.0.0.1:3181`. P4 selected `https://api.kolodahearthstone.com/stats` on the existing
TLS/nginx server on 151, stripping `/stats` before forwarding. No new DNS or other edge configuration is needed.
The latest P4 report is 44 GiB free; retain the low pilot cap and do not clean up existing host data.
Start with synthetic data in a separate data directory; no personal Power.log/Net.log is used by this service.

## Prepare a dedicated runtime and account

Example for Linux x64. Choose a reviewed patched Node **24** build, pinned below to the tested runtime `24.19.0`.
Do not change `/usr/bin/node`, apt alternatives, npm global packages or any existing site service. Download/extract
into a new staging directory first, verify the official checksum, then copy only that version into `/opt`:

```sh
node_version=24.19.0
runtime_stage=$(mktemp -d)
cd "$runtime_stage"
curl --fail --location --output node.tar.xz "https://nodejs.org/dist/v${node_version}/node-v${node_version}-linux-x64.tar.xz"
curl --fail --location --output SHASUMS256.txt "https://nodejs.org/dist/v${node_version}/SHASUMS256.txt"
expected=$(awk -v n="node-v${node_version}-linux-x64.tar.xz" '$2 == n {print $1}' SHASUMS256.txt)
test -n "$expected"
printf '%s  node.tar.xz\n' "$expected" | sha256sum --check -
tar -xf node.tar.xz
sudo install -d -m 0755 /opt/hearthpulse-pool/runtime
sudo cp -a "node-v${node_version}-linux-x64" /opt/hearthpulse-pool/runtime/
/opt/hearthpulse-pool/runtime/node-v24.19.0-linux-x64/bin/node --version
```

Prepare the dedicated `hearthpulse-pool` system user/group through the host's normal account-management process.
Copy the reviewed checkout under `/opt/hearthpulse-pool/app` (read-only for that account). There are no npm
dependencies or install step. Set `/var/lib/hearthpulse-pool` to owner `hearthpulse-pool`, mode `0700`; it must be
outside every web root, object-public mount and website backup. Place `pool.env.example` as
`/etc/hearthpulse-pool/pool.env`, mode `0600`, root-owned. Generate the operator token locally, never in a repo,
shell history or a chat. Leave worker variables unset until P3 is published. A missing worker leaves `stored`.

Apply the supplied unit only as a new service after checking its paths. It runs the pinned Node executable with
UMask 0077 and a private data root. `systemctl daemon-reload` / starting it / enabling it are P4 deployment actions.
No command in this document authorizes changing other services. A second process using the same data root fails
closed on an OS SQLite lock held in `instance.lock.sqlite`. Process exit releases it automatically, including
abrupt termination. Keep the data root on a local filesystem with reliable SQLite locking; never delete or
replace the lock database while any service instance runs.

## Real .NET worker

P3 owns `tools/Hearthpulse.SourceWorker/Hearthpulse.SourceWorker.csproj`. Publish that real CLI from the integrated
checkout, using the configured SDK, with warnings treated as errors and build parallelism two:

```sh
dotnet restore tools/Hearthpulse.SourceWorker/Hearthpulse.SourceWorker.csproj --source https://api.nuget.org/v3/index.json --artifacts-path /tmp/hp-worker-build -r linux-x64 -p:SelfContained=true -m:2
dotnet publish tools/Hearthpulse.SourceWorker/Hearthpulse.SourceWorker.csproj --no-restore --artifacts-path /tmp/hp-worker-build -c Release -r linux-x64 --self-contained true -o /tmp/hp-worker-publish -m:2 -p:HEARTHPULSE_STRICT=true -warnaserror
```

Install the whole self-contained publish directory read-only under `/opt/hearthpulse-pool/worker`. P4 fixes the
executable path in the environment; no runtime upgrade or shell command is needed. The API accepts the validated
reported parser version (the integrated decoder is `source-v1.2`), never a hardcoded `source-v1.0`:

```ini
POOL_WORKER_EXECUTABLE=/opt/hearthpulse-pool/worker/Hearthpulse.SourceWorker
POOL_WORKER_ARGS='[]'
```

The Node service appends **only** `--input <generated private path> --output <generated private path>` and uses
`spawn` with `shell:false`. Paths and commands are never accepted in an HTTP body. One worker at a time; 120 s
timeout; stdout/stderr capped at 16 KiB each and never forwarded/logged. Output JSON is capped at 64 MiB, validated
against its source and source hash, then replaces the active SQL result in a transaction. A crash/bad output
becomes `failed`; the archive remains for explicit reprocess. Restart requeues interrupted `processing` jobs.
The test subprocess fixture is not a production parser and must never be configured in deployment.

## Analytics snapshot

Migration `0009_analytics.sql` (facts and `analytics_snap`) is applied automatically at start, additively and
idempotently. The service rebuilds the `bg-heroes-solo` snapshot in its own queue at most once a minute when needed;
`GET /v2/analytics/bg/heroes?mode=solo` only reads the published row (`docs/analytics-api.md`). Forget and retention
withdraw the snapshot in the same transaction, so the route answers `rebuilding` until the next build. Do not set
`ANALYTICS_MIN_CELL` / `ANALYTICS_MIN_SOURCES` in deployment.

## Proxy and synthetic verification

P4 adds the scoped locations from `nginx-source-capture.conf` to the existing `api.kolodahearthstone.com` TLS server,
without replacing other API routes, DNS or TLS settings. They strip `/stats`, turn proxy caching off, and expose
no archive alias/static location. The proxy rejects reprocess, strips Authorization, does not buffer request bodies to its
disk, and disables access logging. Do not configure request-body logging at a WAF/monitoring layer. Decide the
public rate limit before opening it. Install `nginx-source-capture-limits.conf` in nginx's `http{}` context;
the pilot uses 2 requests/second per restored client address with a burst of 12. The smoke retries rate limits and
brief gateway unavailability during startup. Check `nginx -t` before any P4-authorized reload.

```sh
curl --fail http://127.0.0.1:3181/healthz
/opt/hearthpulse-pool/runtime/node-v24.19.0-linux-x64/bin/node server/pool/deploy/synthetic-smoke.mjs http://127.0.0.1:3181
/opt/hearthpulse-pool/runtime/node-v24.19.0-linux-x64/bin/node server/pool/deploy/synthetic-smoke.mjs https://api.kolodahearthstone.com/stats
```

Set the operator token in the command environment through the host's secret mechanism to include loopback
reprocess. The same script over HTTPS tests upload/manifest/worker/status/forget but must run without that token
because the public proxy blocks reprocess. It prints technical counts only. HTTP 202 `deleting` is a pending
deletion, never success. Check status after service restart, repeat smoke, confirm the synthetic archive directory
and SQL facts are gone. Exercise abrupt restart, interrupted processing, quota 503/Retry-After and delayed upload
410 before opening the pilot. The default 256 MiB archive/128 MiB per day are deliberately small staging caps.

## Layout, cleanup, restoration and deletion

- `pool.sqlite` (+ WAL/SHM): legacy tables plus `source_*`; FULL synchronous WAL, foreign keys, secure_delete.
- `private/captures/<SHA256 generation>/<SHA256 capture>/<index>.jsonl.gz`: verified typed source only, fresh
  gzip without client filenames/comments. Input/output work files live inside that private generation.
- `private/revocations.jsonl`: fsynced append-only **independent revocation ledger** with generation hash and time.
  Keep it outside rollback of older SQL/archive backups; retain it indefinitely for the initial pilot. Restore
  the current ledger first, then start the API: it revokes/removes restored old captures before listening.
  Never restore an older ledger over the current one. Copying a stale SQL backup alone is not a correct restore.
- Permanent SQL capture tombstones stop expired capture IDs returning. Finished source TTL is 180 days maximum;
  unfinished uploads default 24 hours. Maintenance runs every minute and at startup, retries pending deletion,
  expires source/derived together, cleans old quotas and checkpoints WAL. No original logs are retained.
- The minute's pass (`maintenanceTick`, `node/server.mjs`) first deletes the expired legacy rows, each delete on its
  own, then maintains the archive last, so its WAL checkpoint frees the pages they released. A failed step does not
  stop the others; it writes one JSON line to the journal (`journalctl -u hearthpulse-pool`): its code and the error
  message cut to 200 characters (`reportFailure`). The pass repeats every minute, so a code that repeats is a stuck
  step:

  | Code | Failed step | What waits |
  |---|---|---|
  | `shared_prune_failed` | `g`, `c` and `quota` older than 60 days, one batch | all three keep their old rows (the batch is atomic) |
  | `records_prune_failed` | `e` older than 180 days, `equota` older than 7, withdrawal of the records' views | the old records and views |
  | `archive_maintain_failed` | capture expiry, pending deletion, old source quotas, WAL checkpoint | expired captures and the WAL; the deletes above already ran |
  | `maintenance_failed` | none of the above: the pass itself broke | everything after the break; a service bug, not data |

  `analytics_refresh_failed` is the snapshot build's own line ("Analytics snapshot"). Before 2026-11-26 every failure
  here was silent and a failed archive step also skipped both deletes, so a first `archive_maintain_failed` after an
  upgrade can be an old fault becoming visible.

Forget fsyncs the independent tombstone first, stops the matching worker, deletes source receipts/manifests/facts,
removes the generation directory and checkpoints WAL. Physical cleanup failure answers 202 and keeps the generation
revoked; retry or maintenance finishes it. Late uploads/worker commits always check the tombstone. Backups need the
same deletion policy: this service cannot erase disconnected backups, filesystem snapshots or journals owned by a
different system. Do not promise backup erasure until P4 has verified the actual host's backup process.

What each budget counts, the `/healthz` fields for it and the capacity figures are in "Budgets and capacity" below.
`/healthz` tells "alive" from "producing": `intake` (rows
taken today: `shared` = `/v1/games` + `/v1/constructed`, `records` = `/v1/events`), `stored` (rows kept per table)
and `analytics` (`built`, `stale`, `published` of the `bg-heroes-solo` snapshot). Intake counts quota-accounted writes,
including repeated submissions, not unique games. Zero intake with a green `ok` means none were counted today;
check client configuration and expected activity before calling the flow healthy or broken. Monitor health counts,
disk/WAL growth, worker failures and
503 rates without IDs or source snippets. Directory/SQL orphan cleanup runs before listening after a crash.
Provision larger capacity only after measured synthetic/pilot mode-specific size, parser time and retention
estimates; the 180-day TTL is an upper retention limit, not a capacity guarantee.

## Budgets and capacity

Intake is refused with 503 and `Retry-After: 30` by whichever budget is exceeded first. A 503 does not keep the
data: the client's queue is bounded, and a capture that waits there too long expires before it is ever stored.

| Error | Budget (default) | Counts |
|---|---|---|
| `archive_pressure` | `POOL_ARCHIVE_BYTES`, 256 MiB | per chunk with a receipt: uncompressed bytes + compressed bytes, also for unfinished and `failed` captures until forget or expiry removes them; per `parsed`/`partial` capture: its derived JSON, once; per running job: a fixed 64 MiB reservation in place of the result; the revocation ledger file |
| `metadata_pressure` | 64 MiB, not configurable from the environment | live pages of `pool.sqlite` (rows, indexes, receipts, manifests, tombstones, analytics facts, the legacy `/v1/*` tables and SQLite's own overhead) without the derived JSON the archive budget already counts, plus the whole `-wal` and `-shm` files |
| `daily_pressure` | `POOL_DAILY_BYTES` 128 MiB, `POOL_DAILY_CAPTURES` 1000 | uncompressed bytes and new captures accepted in the UTC day |
| `disk_pressure` | `POOL_MIN_FREE_BYTES`, 64 MiB | free space of the filesystem after the request |

The uncompressed half of the chunk count is deliberate: it pays for the work file a job writes next to the chunks
(`worker-*-input.jsonl`, removed when the job ends). A job starts only when the archive budget has room for its
64 MiB reservation and metadata is under its cap; otherwise the capture waits as `stored`. A job that was let in
commits its result even if intake crossed the metadata cap meanwhile, so that cap can be exceeded by one admitted
request or one job's facts and WAL frames. Completing already uploaded captures remains allowed under pressure; each completion can add metadata and WAL. This tail is bounded by the configured pending-capture limit, not by one request alone. The WAL is truncated after every job, every
forget and every minute by maintenance. Free pages inside `pool.sqlite` are not metadata: SQLite reuses them and
never returns them to the filesystem, so the file keeps its largest size while both budgets fall after forget or
expiry. Before 2026-11-16 the metadata measure was the size of the three files: every derived result was counted
in both budgets, and after a deletion the unshrunk file kept `metadata_pressure` on.

`/healthz` (numbers only, no ids or content): `archiveBytes` / `archiveCapBytes` are the archive budget;
`metadataBytes` / `metadataCapBytes` are the metadata budget; `sqliteFileBytes` is `pool.sqlite` + WAL + SHM on
disk and `sqliteFreeBytes` its reusable free pages. `sqliteWalBytes` and `sqliteShmBytes` expose journals separately: a busy external reader can prevent checkpoint truncation and retain pressure. `sqliteFileBytes` far above `metadataBytes` is expected: the
difference is derived results and free pages. `statuses` growing in `stored` with a configured worker means jobs
are waiting for one of the budgets.

Capacity is a calculation from the formulas above and local measurements, not a guarantee and not a tested load.
Measured by the local suite's fixtures on Windows, Node 24.19.0 (the real worker and Linux were not measured):
an empty service holds 143,360 metadata bytes; a parsed one-chunk capture adds about 3.5 KB of metadata and a
64-chunk one about 16 to 20 KiB (fixture and page-layout dependent); SQLite stores a 179,146-byte result in 180,224 bytes. With S = archive bytes per parsed
capture (uncompressed + compressed + derived):

- captures the archive budget holds: 256 MiB / S; captures that can still be parsed: (256 - 64) MiB / S, because a
  job needs its reservation. For a hypothetical S of 275 KB (not a verified pilot average) that is about 976 and about 732. Past the second
  figure uploads are accepted and stay `stored` until space is freed.
- captures the metadata budget holds: (64 MiB - 143,360 - legacy tables and facts) / 3.5 KB, about 18,900 one-chunk
  captures with empty legacy tables, roughly 3,270 to 4,100 if every capture had 64 chunks. Those metadata-only capacities are unreachable with the smaller archive cap. It is no longer the first limit.
- a day: 128 MiB of uncompressed source or 1000 new captures, whichever comes first.

At low pilot traffic retention dominates; a burst can exhaust the same cap in one day: at the 180-day TTL the default cap holds about five
parsed captures a day. Remeasure S and the split between source and derived on the pilot before changing a cap.

## Local checks

From the checkout root with the dedicated Node 24 binary:

```sh
node --test server/pool/test/*.test.mjs
```

These tests use generated safe records, temp directories, real local HTTP/SQLite/files and test worker processes.
P4 must separately validate the **real P3 .NET CLI**, Linux filesystem permissions, systemd, nginx/TLS and remote
staging restart/deletion. Local Windows tests do not establish production or Linux runtime evidence.

Accounting performance limit: both usage() and metadata() scan capture records. Accessing derived_bytes after a large derived field can walk SQLite overflow pages even though octet_length itself avoids reading the value. A warm Windows review measured about 70 ms over 69 MiB; cold-cache/Linux behavior is unmeasured. Do not extrapolate this as a latency guarantee. Reprocess can retain result-sized WAL until maintenance; external readers can delay checkpoint and admission.

## Record views (TASK-2026-11-26-pro-collection, A0)

The service computes Hearthpulse's own numbers from game records and serves them at
`GET /v2/analytics/records/<view>` ([docs/analytics-records.md](../../../docs/analytics-records.md)). The pass runs in
the service queue at most once a minute when due (new records wait 30 minutes; a deletion, a new day or a new
version rebuild at once), in one transaction; no migration (`snap` rows `records` and `records:<view>`). `/healthz`
shows `records: {built, views, published, withdrawn, failed}`. Until the first view is merged (wave 2) no view is
loaded and every `/v2/analytics/records/<view>` answers the service's 404 `{"ok":false,"error":"unknown_view"}`.

**nginx, once, in the first deploy that carries A0.** The include's `location = /stats/v2/analytics/bg/heroes` becomes
`location ^~ /stats/v2/analytics/` with the same directives (GET only, zone `hearthpulse_stats_read`), so every later
view needs no proxy change. The same TLS server serves the data API:

1. Copy the live include aside (`cp -a <live include> <live include>.before-a0`).
2. Compare it with this file as it was before A0 (`git show <A0 merge>^:server/pool/deploy/nginx-source-capture.conf`).
   If they differ in anything but whitespace, stop and ask the owner: the host carries edits this repository does not
   know.
3. Install the new file in its place, `nginx -t`, and only on success `systemctl reload nginx`.
4. Check, each answering as listed: `https://api.kolodahearthstone.com/v1/system/sources` 200 (the data API),
   `/stats/v1/events/summary` 200, `/stats/v2/analytics/bg/heroes?mode=solo` 200,
   `/stats/v2/analytics/records/<view>` 200 with a JSON envelope once a view exists, before that 404 with
   `"error":"unknown_view"` (the service's own body, not nginx's), and `curl -X POST` on it 403.
5. Rollback: put `<live include>.before-a0` back, `nginx -t`, reload.

**After every deploy of the pool** (not only this one), once a minute has passed since the start, read every failure
line of the new run, not just the `_failed` codes: a malformed `balance-patches.json` no longer stops the service, so
only its line tells.

```sh
sudo journalctl -u hearthpulse-pool --since "$(systemctl show hearthpulse-pool -p ActiveEnterTimestamp --value)" \
  --no-pager -o cat | grep '"ok":false'
```

Empty is the pass. `records_patches_invalid` means the views are off: fix the file and deploy again.
`archive_maintain_failed` may be an old fault that was silent before 2026-11-26: tell the owner, it is not a reason to
roll back.

**Balance patches.** `server/pool/data/balance-patches.json` is kept by hand ([data/README.md](../data/README.md)): after
a Hearthstone patch that changes cards, append an entry with the patch's first build and day to each line it changed,
run `node --test server/pool/test/records-periods.test.mjs`, commit and deploy the pool. The build comes from the live
client (the captures' builds or a fresh `Power.log`), not from HearthstoneJSON's newest folder, which can run ahead
of it (data/README.md, "Find the build"). A forgotten entry merges two
patches in the numbers; it is part of the deploy checklist after every patch. A malformed file switches the views off
and nothing else: one journal line `records_patches_invalid` at start, every `/v2/analytics/records/<view>` answers
503 `records_off` (`retry-after: 60`), the next pass removes the published view rows, and the service runs on, the
intake of records included. Fix the file and deploy again; the views rebuild within a minute of the restart.

**Log codes** (one JSON line each, in the journal; codes, keys and counts only, never a record or an id):

| Code | Meaning | What waits |
|---|---|---|
| `records_refresh_failed` | the pass itself failed (`cause` is the error message, cut to 200 characters); rolled back | every view keeps its previous row; the next minute tries again |
| `records_view_failed` | one view threw (`view`, `stage`: start, add, finish, slice) or would have published an install id or a record id (`stage: install_id`, `record_id`) | that view keeps its previous row (or none); the others publish; retried by the next pass due anyway (new records 30 minutes after the last pass, a new day or version), so a broken view shows once in 30 minutes, not every minute |
| `records_derive_failed` | a deriver threw on `records` records of the pass (`deriver`) | those records lack its fields in this pass; a code bug |
| `records_module_rejected` | at start, a module under `node/records/derive` or `views` did not load or broke the contract (`module`, `reason`) | that module is left out until a fixed deploy |
| `records_patches_invalid` | at start, `server/pool/data/balance-patches.json` is missing or malformed (`cause: invalid_balance_patches`) | every view is off until a deploy with a fixed file; the rest of the service runs |

A code that repeats every minute is a stuck step, as for the upkeep codes above.

**How far from publication** ([docs/analytics-coverage.md](../../../docs/analytics-coverage.md), A4). A read-only report
of installs, games and cells per metric family against the K3 thresholds, `<5` for 1 to 4; run it as the service
account (a stopped service's `-wal`/`-shm` files must stay its own), off peak: while it reads, the minute's WAL
checkpoint waits for it.

```sh
cd /opt/hearthpulse-pool/app
sudo -u hearthpulse-pool /opt/hearthpulse-pool/runtime/node-v24.19.0-linux-x64/bin/node \
  server/pool/node/report.mjs --data /var/lib/hearthpulse-pool
```

Add `--format json` for the machine form.
