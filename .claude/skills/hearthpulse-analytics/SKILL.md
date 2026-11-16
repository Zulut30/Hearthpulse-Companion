---
name: hearthpulse-analytics
description: Use when defining, computing, publishing or interpreting a Hearthpulse statistic - a metric in docs/analytics-metrics.json, the stats pool (server/pool, SQLite/Node), source capture and Hearthpulse.SourceWorker facts, the snapshot route /stats/v2/analytics, publication thresholds, sample sizes, Wilson intervals, shrinkage, patch/rank/MMR grouping, or deciding whether a number may be shown, compared or called ready. Also when the pool is empty or stale and the screen has to fall back to permitted external data. Not for adding an API call or cache (hearthpulse-data-sources). Triggers - "метрика", "выборка", "доверительный интервал", "порог публикации", "пул пуст", "статистика героев", "винрейт значим ли", "sample size", "small_sample", "not_published".
---

> Source: [anthropics/knowledge-work-plugins](https://github.com/anthropics/knowledge-work-plugins)
> `data/skills/statistical-analysis/SKILL.md` at `e91a134`, Apache-2.0, see
> [THIRD-PARTY-NOTICES](../THIRD-PARTY-NOTICES.md). Hearthpulse changes: the caution rules (small samples,
> confounding, Simpson's paradox, survivorship, multiple comparisons, false precision) restated for game statistics
> in [reference/statistics.md](reference/statistics.md); business metrics, pandas snippets, forecasting and
> seasonality removed; everything in this file is the project's own contract.

# Hearthpulse statistics: from a game to a number on screen

One question decides most tasks here: **which layer does this number come from, and is that layer producing data
right now?** Answer it before touching code or wording.

## The four layers

| Layer | Raw or derived | Where | Publication rule |
|---|---|---|---|
| **External data** (HSReplay, HSGuru through the Hearthpulse data API) | derived by the source; we hold no raw games | `Core/Api/HearthpulseData*.cs`, [hearthpulse-data-sources](../hearthpulse-data-sources/SKILL.md) | the source's own; we show its sample size and date, under the name "Hearthpulse data" |
| **Shared BG and Constructed games** (`/v1/games`, `/v1/constructed`, totals `/v1/stats`, `/v1/choices`) | raw rows 60 days, totals over the last 30 | `server/pool/src/index.js`, `Core/Pool/PoolClient*.cs` | a published cell holds 10 games or more; details in `server/pool/README.md` |
| **Game records** (`/v1/events`, `/v1/events/summary`, `/v1/events/mulligan-pairs`) | raw compact records 180 days; summary and pair table derived | `server/pool/src/events.js`, `Core/GameStats/`, `Core/Pool/MulliganPairs.cs`, `docs/game-stats.md` | each summary cell from 10 games and 5 installations; a pair (`mulligan.pair_winrate`) from 200 games with both kept and 5 installations |
| **Source capture → facts → snapshot** (`/v1/captures/*`, `/stats/v2/analytics/bg/heroes`) | raw typed source; facts and snapshot derived, replaceable by reprocessing | `server/pool/node/` (`analytics.mjs`, `archive.mjs`), `tools/Hearthpulse.SourceWorker`, `Core/SourceCapture/`, `Core/Pool/AnalyticsClient.cs` | 5 installations behind every number, value hidden under 10 in its denominator |

The pool has two entrypoints over one schema: the Cloudflare Worker (`server/pool/src`, D1) and the Node 24 service
(`server/pool/node`, SQLite) that also hosts source capture and analytics. The v2 route exists only in the Node
service.

Three versions move independently; bump each version whose contract changes: the **source** format
(`manifest.sourceVersion`, what the recorder wrote), the **parser** (`parserVersion`, how the worker reads it) and
the **rules** of a metric (`rulesVersion`, how facts become a number). A better parser reuses stored source; a new
field needs a client update and has no history.

## Contracts to read, by task

- A metric's meaning, fields, grouping, small-sample rule: [analytics-metrics.md](../../../docs/analytics-metrics.md)
  and its machine copy `docs/analytics-metrics.json`. `AnalyticsCatalogTests` keeps the two in step.
- What the server stores, counts, withdraws and answers: [analytics-api.md](../../../docs/analytics-api.md).
- Which fact a capture is fit for (`eligibility.outcome`, `eligibility.offers`): `docs/adr/0013-per-metric-eligibility.md`
  and the worker's [CLI contract](../../../tools/Hearthpulse.SourceWorker/README.md).
- What a capture may contain: `docs/source-capture.md`; identifiers, retention and deletion:
  [hearthpulse-privacy](../hearthpulse-privacy/SKILL.md).
- Which Battlegrounds number comes from where today: `docs/bg-meta-sources.md`.

## Changing or adding a metric

1. Write the definition in the catalogue first: unit, numerator, denominator, fields, missing-data rule, grouping,
   small-sample rule, display. A changed definition is a new metric `version`; a changed computation of an
   implemented metric is a new `rulesVersion`.
2. Check every field in `fieldMatrix`: `have` or `derived` means computable; `add` or `unavailable` means the metric
   is `needs_fields` and no code can produce it yet. Status `defined` means "computable, not computed" - it is not
   on any screen.
3. The unit is one installation's observation. Two clients in one lobby are two observations, never one "match":
   there is no safe join.
4. An observation that is not fit leaves both numerator and denominator. `completeForAggregation` is not loosened
   to reach a sample.
5. Group by `start.build`; `null` is its own group; builds are never merged. Rank is a league, MMR a 500-point
   bracket: finer grouping does not exist in the data.
6. Thresholds are the catalogue's: 5 distinct installations (`owner`) among the observations the number is made of,
   then 10 in the denominator for a value. Below either, the field is `null` and the row `small_sample`. Never pad,
   interpolate or borrow from another group.
7. Uncertainty travels with the value: Wilson interval (z = 1.96) for a share, mean ± 1.96·s/√n for a mean, `null`
   below n = 2. A shrunk estimate states its prior, as the lobby-tribe effect does (`delta · n / (n + 150)`, used
   from 100 games).
8. Deletion is part of the computation: replaced or forgotten facts withdraw the snapshot in the same transaction.
   A metric that cannot be withdrawn is not finished.
9. Tests: an independent hand calculation for the server (`server/pool/test/analytics.test.mjs` is the model), the
   client parser with hidden fields as `null`, the catalogue test. Run with `node --test server/pool/test/*.test.mjs`
   and the .NET suite ([hearthpulse-testing](../hearthpulse-testing/SKILL.md)).

Before comparing two numbers, ranking rows or writing a claim about them, read
[reference/statistics.md](reference/statistics.md).

## Availability is a value, not an error

| Answer | Meaning | Screen |
|---|---|---|
| snapshot with `available: true` | published; `stale: true` means newer facts wait for the next build | numbers, with sample and interval |
| `not_published` | nothing was ever published, or no facts yet | empty state for own data |
| `rebuilding` | withdrawn after a deletion or replacement | empty state, no old numbers |
| `unsupported_mode` | the metric does not exist for this mode | section hidden |
| a `null` field, `quality: small_sample` | too few installations or games | counts where present, no value |
| client returns `null` | network error, non-success status, bad JSON, non-https endpoint | same as offline |

**Own data empty or stale, external data present.** The screen keeps the external numbers: their use is permitted
and recorded in [data-permissions.md](../../../docs/data-permissions.md), so this needs no new decision from the
owner. Label each number with its source and its date (`Updated` on `BattlegroundsMeta` and `ConstructedMeta`,
`FetchedAt` on a hero slice). A published own metric (the v2 snapshot) gets its own row with its own sample and
interval: an own value that is `null` or not published is shown as missing, never filled from the external number,
and an external number is never labelled as Hearthpulse's own. A stale external slice stays visible with its date;
it is not replaced by a guess.

One mix already exists and is deliberate: the in-game advisor's Battlegrounds meta lays the pool's shared-game
totals over the API numbers, weighted by sample size and bracket by bracket (`Core/Pool/PoolClient.Merge.cs`,
`docs/bg-meta-sources.md`). With an empty pool it is the API numbers unchanged. Do not extend that blending to a
new metric without writing its rule in the catalogue.

## "Ready" has three meanings

State which one you checked:

- **Static**: the code, catalogue and tests exist and agree. Reading the repository proves this and nothing more.
- **Deployed**: the route answers on a real host with the current migration. Only a request to that host proves it.
- **Producing**: games arrive and a number clears the thresholds. Only the live answer's `captures`, `sources` and
  `published` prove it.

The research of 2026-11-15 found the first true and the other two false in production (`PoolClient.DefaultEndpoint`
empty, the pool answering `games: 0`, the v2 route 404). On 2026-11-16 the pool was deployed with the v2 route, the
default endpoint was set and one pilot installation was sending; nothing was published yet (thresholds). Both are dated observations: re-check with a read-only GET
when the task depends on it, and write the date beside the result. Production deployment and endpoint changes need
authorization for that target; an explicit task may already supply it, so do not ask again when it is present.
The rollout order is in `analytics-api.md`.
