# Our own statistics come from game records, grouped by balance patch

Status: accepted, 2026-11-26 (TASK-2026-11-26-pro-collection, A2; the owner's answers to Q1 and Q6).

Three pipelines fed our own numbers: shared games (`/v1/games`), game records (`/v1/events`) and source captures
(`/v1/captures/*`). None of them had the build, the rating and the five-installation floor together, and the
production pool held no Battlegrounds capture at all (the task's gap analysis, 2026-11-26).

**Game records are the main path** (Q1). Every product we compute — the Constructed tier list, classes, matchups,
decks, cards, the mulligan guide and choices; Battlegrounds heroes, trinkets, comps and tier-ups; Arena classes — is a
view over game records, computed by the Node pool (`server/pool/node/records/`) and served at
`GET /stats/v2/analytics/records/<view>`. The fields are in the record already, each record carries its installation
(the floor of five installations works), a forget already deletes them, and `mulligan.pair_winrate` set the
precedent. Source captures stay the audited path: for the Arena draft, and for one metric computed both ways
(`bg.hero.offer_pick_placement` against `bg.hero.records`).

**Windows follow the balance patch** (Q6). `server/pool/data/balance-patches.json` lists, for each line
(Constructed, Battlegrounds, Arena), the first build of every balance patch; the builds up to the next entry are one
patch, so a hotfix that changes no card does not split the sample. The newest patch is published whole and as its
last 7 and 30 days, the previous patch whole, and records of an unknown build in a 30-day window of their own. The
pool reads 90 days of records. The definitions are in [the catalog](../analytics-metrics.md#metrics-over-game-records).

## Considered options

- Q1, source captures as the main path: closer to "raw source, then reprocess", but it needs the fixes of the partial
  captures, new fields (a capture holds no rating) and Battlegrounds captures, which production does not have.
- Q6, strictly by build: every hotfix starts an empty group, so the cells stay under the thresholds longer. One
  rolling window without patches (the pair metric's 30 days) mixes balance states.

## Consequences

- The catalog's "builds are never merged" now holds for capture metrics only; a record view reads "two balance patches
  are never merged".
- The patch list is kept by hand from the patch notes: nothing tells automatically whether a build changed cards. A
  missing entry merges two balance patches until it is added (`server/pool/deploy/LINUX-RUNBOOK.md`); the views are
  rebuilt from the records, so a late entry changes no stored data.
- Records of app versions without `build`, and builds below the first entry, are only in the unknown window: until the
  app that sends `build` is released, every own number lives there.
- Patches older than the previous one are not published, and a long patch is cut at 90 days.
- The views run only in the Node service, which has real transactions and no CPU limit. The Cloudflare Worker does not
  serve them; going back to it means porting them.
- `mulligan.pair_winrate` (`mull-pairs-1`) keeps its 30-day window over all builds until a new rules version.
