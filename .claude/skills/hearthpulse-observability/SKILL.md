---
name: hearthpulse-observability
description: Use when a Hearthpulse change must stay diagnosable after it ships, or when you cannot tell from the available evidence what happened - adding or reviewing Diag log lines, crash reporting, upload queues and their retry state, the stats pool's health endpoint, failure lines and synthetic smoke, freshness of collected data shown to the player, a staleness or failure alert for the pool or the collectors. Not for a failure you can reproduce now (diagnosing-bugs) or for timing and memory (hearthpulse-performance). Triggers - "логирование", "что писать в лог", "диагностика", "свежесть данных", "данные устарели", "очередь не уходит", "мониторинг пула", "healthz", "alert", "телеметрия".
---

> Source: [addyosmani/agent-skills](https://github.com/addyosmani/agent-skills)
> `skills/observability-and-instrumentation/SKILL.md` at `39b723c`, MIT, © 2025 Addy Osmani, see
> [THIRD-PARTY-NOTICES](../THIRD-PARTY-NOTICES.md). Hearthpulse changes: kept the method (questions first, a signal
> per question, events not prose, bounded fields, symptom alerts with a runbook, verify the signal itself); replaced
> the web-service stack (JSON logger middleware, Prometheus, OpenTelemetry tracing, pager tiers) with this project's
> three planes and their existing signals; added the consent boundary for the desktop app.

# Seeing what Hearthpulse is doing

A signal exists to answer a question someone will ask later. Write the questions first, then find or add the one
signal that answers each. A line that answers no question is noise in a 4 MB log.

## Three planes, kept apart

| Plane | Who reads the signal | What exists | What must not be added |
|---|---|---|---|
| **The app on the player's PC** | the player, or a developer handed a log or a consented crash report | `Diag.Info/Debug/Error` to `hearthpulse.log` (rotated at 4 MB to `.old.log`); `CrashLog` and `CrashReporter` (off by default, `docs/crash-reports.md`); the account center's journal (`AccountActivity`: sent, waiting, paused with reason and next try, refused) | any background report of usage, features, timings or errors that the player did not switch on. No setting for it exists, so none is sent. A new outbound signal starts with a consent setting and its text: [hearthpulse-privacy](../hearthpulse-privacy/SKILL.md) |
| **The stats pool** (`server/pool`, Node service) | whoever operates the host | `GET /healthz`: capture counts by status, archive bytes and metadata bytes each against its cap, whether a worker is configured; one-line JSON failure records with a fixed code (`reportFailure`); `deploy/synthetic-smoke.mjs`; the runbook `deploy/LINUX-RUNBOOK.md` | access logs, request bodies, install ids, capture keys or source snippets in any log or monitor |
| **Collectors and the site** (data API, hearthpulse.net; other repositories) | their own monitoring | from here only their answers are visible: `fetched_at` / `completed_at` in data API bodies, the site's `/health/ready` | assumptions about their internals; say "seen in the response on <date>" |

A question about one plane is not answered by a signal from another: a fresh app log says nothing about a stalled
collector, and a green `/healthz` says nothing about whether any client is configured to upload.

## Method

1. **Write 2-4 questions** the reader will ask about this feature. For an upload: is it sending, what waits, why
   did it stop, when does it retry. For collected data: how old is this slice, did the last run fail.
2. **Pick the signal that already answers each.** Only add one where the answer is "nobody can tell".

   | Question | Signal here |
   |---|---|
   | What happened in this one case? | a `Diag` line: stable wording, an outcome code, counts |
   | Is it stuck or flowing? | a persisted state the UI shows: queue length, last success, pause reason, next retry (the pattern of `ProfileUploader` and `AccountActivity`) |
   | How old is what I am looking at? | a timestamp carried with the data to the screen (`Updated` on `BattlegroundsMeta` / `ConstructedMeta`, `FetchedAt` on a hero slice, `published` and `stale` on an analytics snapshot) |
   | Is the collector behind this page late? | `DataHealthReport.Section(...)` from the data API's own summary (`Core/Api/DataHealth.cs`): fresh, stale, waiting for the origin, failed, unknown (offline is unknown, never "stale"); Settings, Data shows every section |
   | Is the service alive and within limits? | `/healthz` fields; add a field there before adding a log line |
   | Does the whole path work? | the synthetic smoke: upload, parse, status, forget |

3. **Log events, not prose and not data.** One line per outcome with a fixed phrase, a code and numbers:
   `Game stats: sent 1 game(s)`. Levels: `Error` for a broken invariant, `Info` for a finished unit of work or a
   handled degradation worth a trend, `Debug` for detail. Never per-line or per-snapshot logging on the log thread:
   aggregate over a window, as the WPF watchdog's `Window: showed N snapshots, slowest ... ms` line does
   (`App.Watchdog.cs`).
4. **Name the entry point** when several paths write the same kind of line: live game, log backfill, "Send past
   games", a retry after restart. A line that cannot be attributed is half a signal.
5. **Keep fields bounded and safe.** Codes, counts, durations, modes, versions. No token, e-mail, BattleTag, player
   name, deck name, file path, request body or card-by-card content; no user-supplied text as a key or label. A
   server line carries a code and a cut error message, never source content.
6. **Make failure and absence distinguishable.** "Not set up", "switched off", "offline", "refused", "nothing yet"
   and "stale" are different states for the reader; give each its own code or text instead of one empty result.
7. **An alert is for a symptom someone will act on**, has a threshold with a duration, and points at a runbook line:
   what it means, the first check, who decides. Candidates here are age and flow, not CPU: a collector run older
   than its cadence, capture rows piling up in `failed` or `stored`, the archive near its cap, the smoke failing.
   An alert nobody acts on is deleted. Where an alert runs (a host monitor, a scheduled workflow) is an operations
   decision for the owner; propose it with the exact check, do not deploy it.
8. **Verify the signal itself.** Trigger the path and read the output: force the failure in a test or against a
   local pool, find the line by its code, confirm the state survives a restart, confirm the timestamp shown is the
   data's and not the time of the request. A signal that was never seen firing is unverified, and the report says so.

## Checks

- App: a test that asserts the state or line for success, pause and refusal (the uploader tests are the model), run
  with `HEARTHPULSE_DATA` on a copy of the data folder when the app itself is started.
- Pool: `node --test server/pool/test/*.test.mjs`; against a local service, `GET /healthz` and
  `node server/pool/deploy/synthetic-smoke.mjs <http://127.0.0.1:port>`. Anything against a public host is a
  read-only GET unless the owner asked for more.
- Freshness on screen: both UIs show the same date for the same slice
  ([hearthpulse-visual-check](../hearthpulse-visual-check/SKILL.md)).

## Red flags

- An upload, retry loop or background loader with no persisted "last result".
- A catch block that only swallows: no line, no state.
- A number on screen with no date while its source refreshes on a schedule.
- A new log line built from interpolated user or game text.
- A "temporary" debug line inside the parser loop or the snapshot publisher.
- A health check that reports "ok" without reading the thing that can break.
