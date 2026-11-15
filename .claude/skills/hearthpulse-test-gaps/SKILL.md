---
name: hearthpulse-test-gaps
description: Use when the question is which behaviour of a changed or risky Hearthpulse module could break without any existing test failing - QA of a package that changed a behavioural contract, "would the tests catch this?", a module outside the Stryker targets, an acceptance criterion whose only evidence is a green suite. Works by inventorying caller-visible outcomes and mapping each to an assertion; applies a temporary pseudo-mutation only when verification is explicitly asked for or a gap is being closed. Not for writing or running tests in general (hearthpulse-testing), coverage percentages, or changes that touch only Markdown, docs or skills. Triggers - "поймают ли тесты", "что не покрыто тестами", "пробелы в тестах", "выжившие мутанты", "test gaps", "would tests catch", "surviving mutation".
---

> Source: [dotnet/skills](https://github.com/dotnet/skills) `plugins/dotnet-test/skills/test-gap-analysis/`
> (`SKILL.md`, `references/mutation-catalog.md`) at `2877c71`, MIT, © .NET Foundation and Contributors, see
> [THIRD-PARTY-NOTICES](../THIRD-PARTY-NOTICES.md). Hearthpulse changes: the decision flow, result labels and
> observable-candidate rules kept and shortened; scope, baseline commands, risk ranking and the equivalence list
> rewritten for this repository's suites, roles and Stryker targets; the polyglot tables, the `grade-tests`
> composition mode and the money and authorization walkthroughs dropped. Codex also has the upstream skill as a
> plugin; in this repository use this one, it already carries the method.

# Which behaviour could change without a test failing

Mutation thinking is the probe, not the goal. First list what a caller can observe, then find the assertion that
would notice each change. Tools, fixtures and commands for the suites: [hearthpulse-testing](../hearthpulse-testing/SKILL.md).

## Scope before anything else

| The change touches | Do |
|---|---|
| Only `*.md`, `docs/`, `.claude/`, `.agents/` | Nothing here. No test is owed; the skill checks cover the catalogue (`tools/skill_checks`). Say "not applicable" in one line |
| Only views, styles, Loc strings | Not this skill: the binding, localization and visual checks own those (`hearthpulse-visual-check`, `LocBindingTests`, `LocalizationTests`) |
| Generated `*.g.cs` | Skip the file; analyse the generator's inputs or the code that reads the table |
| A module with a Stryker target (`docs/mutation-testing.md`: replay share and import filter, crash-report scrubber, likely deck, combat simulator) | Use its recorded survivors first; the Stryker run in CI is the canonical verifier, started by hand and never as a side effect |
| Any other behaviour in `src/Hearthpulse.Core`, `src/Hearthpulse.Presentation`, `tools/Hearthpulse.SourceWorker`, `server/pool` | The flow below, limited to the changed contract and what it reaches |

Keep to the request: one changed contract is not an audit of the repository.

## Flow

1. **Name the public surface that changed**: the method, record, route or view-model member a caller sees, and for
   QA the acceptance criterion it serves.
2. **Inventory its outcomes** as rows `input or sequence → expected outcome → asserting test → gap`:
   - each branch of a classifier, each side of a compound condition, the default case;
   - guard boundaries: the last allowed value, the first refused one, one beyond; empty, `null`, zero and negative
     where they behave differently;
   - each independent observation: returned value or field, exception type, stored row, queued file, raised
     event or `PropertyChanged`, line written, request sent **and request not sent**;
   - private helpers, constants, rounding, retry and cancellation as seen through the public caller.
   One asserted field does not cover another; "sent when on" does not cover "not sent when off".
3. **Map each row to an assertion that would fail.** A test that runs the code without asserting the outcome
   covers nothing. Trace through callers before writing "no coverage": the assertion may sit in a contract test of
   the other UI, a pool test, or a real-log test.
4. **Admit only observable gaps.** State `witness → original observation → changed observation`; drop the row when
   the two are the same to every caller. What counts as equivalent here:
   [reference/mutations.md](reference/mutations.md).
5. **Rank** by what breaks for the player: (1) something leaves the machine that should not, hidden information
   shown during a game, a consent switch ignored, data lost or a migration that drops rows; (2) a wholly unasserted
   outcome; (3) a boundary or exact value behind a loose assertion; (4) another variant of asserted behaviour.
6. **Verdict** from the finished inventory: **Strong** (core branches and boundaries protected, few default-case
   gaps), **Mixed** (real coverage, at least one important partition open), **Weak** (important outcomes broadly
   open). Do not let two validation gaps turn a broad suite into "Mixed".

## Labels

| Label | Meaning |
|---|---|
| **Likely killed** | an existing assertion observes the changed outcome (by reading) |
| **Candidate survivor (unverified)** | an observable change looks unasserted; nothing was executed |
| **No coverage** | no test reaches the outcome at all |
| **Killed** / **Survived** | the exact change was applied and the covering test failed / stayed green |
| **Equivalent** | no caller can tell; left out of the findings |

Only an executed change earns **Killed** or **Survived**. Reading alone never produces a kill ratio.

## Executing a pseudo-mutation

Only when verification is asked for in so many words, or a developer is closing a gap with a test. A reviewer or
the QA role stays read-only: QA reports **Candidate survivor (unverified)** or **No coverage** with the witness and
the smallest test, and returns it to the developer.

1. Baseline once: the narrowest filter that covers the outcome, and confirm tests actually ran.
   `dotnet test tests/Hearthpulse.Tests/Hearthpulse.Tests.csproj --no-restore --filter "FullyQualifiedName~<TestClass>"`
   (xunit 2 on VSTest; restore first with the explicit source); the Windows presentation suite for shared view
   models; `node --test server/pool/test/<file>.test.mjs` for the pool.
2. Apply one change to one existing expression; confirm the diff is exactly that.
3. Run the same filter. Green means **Survived**; a failure at a relevant assertion means **Killed**; a build or
   unrelated failure leaves it unverified.
4. Revert only the temporary mutation and restore the pre-mutation diff, preserving legitimate in-progress
   changes. A mutation is never committed, never left in a
   worktree another session reads, and never applied in a tree that is being built for a release.
5. After a green run, re-check that the change is observable; a surviving inert change is not a finding.

Closing a gap: add the one behaviour-focused test for a verified survivor or a demonstrated no-coverage outcome,
re-apply the change to see the new test fail, revert, run the narrow suite clean. One test per behaviour, not per
operator. Coverage floors only go up (`hearthpulse-testing`); a gap test does not need to raise them.

## Output

One verdict line with its reason; for **Strong**, one sentence naming the protected boundaries. Then one row per
actionable gap:

| Risk | Public outcome | Change | Result and evidence | Smallest test |
|---|---|---|---|---|

Every row has a concrete witness and a test that could be written from it (name the fixture kind: synthetic
Power.log lines, a JSON fixture, a fake `HttpMessageHandler`, a temp data folder). State once what was not run
(no baseline, static reading only, Windows suite not available). No table of discarded candidates, no chronology.
For QA, the rows go into `qa-P<n>.md` under "Вернуть разработчику" with the AC they weaken.
