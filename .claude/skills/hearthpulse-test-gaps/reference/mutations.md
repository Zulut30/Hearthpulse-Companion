# Candidate changes and what makes them equivalent

> Adapted from [dotnet/skills](https://github.com/dotnet/skills)
> `plugins/dotnet-test/skills/test-gap-analysis/references/mutation-catalog.md` and the "Admit only observable
> candidates" section of its `SKILL.md` at `2877c71`, MIT, © .NET Foundation and Contributors, see
> [THIRD-PARTY-NOTICES](../../THIRD-PARTY-NOTICES.md). C# rows kept, other languages dropped, project examples added.

Read this when judging whether a gap is real, or for an exhaustive audit of one module that was explicitly asked for.

## Categories

| Category | Typical change | What a killing test observes | Where it bites here |
|---|---|---|---|
| Boundary | `<` ↔ `<=`, off by one, first/last index | the exact value at and beside the boundary | publication thresholds (5 installations, 10 games), queue caps, retention days, the turn or round a tracker switches on |
| Logic | `&&` ↔ `\|\|`, a condition removed or negated | each condition changing the outcome by itself | "signed in **and** switched on **and** notice shown" gates before an upload |
| Return value | value ↔ `null` / empty / `default`, count ± 1 | the returned value or the state built from it | a parser returning `null` for a refused file; a view returning an empty list instead of the fallback |
| Guard and error | guard removed, exception type changed, error swallowed | the invalid input and the exact result | "never throws" network methods; a `TaskCanceledException` that is not an `HttpRequestException` |
| Arithmetic | `+` ↔ `-`, `*` ↔ `/`, wrong base | the exact number, not a range | rates, means, intervals, damage and lethal in the simulator |
| Collection | first or last item dropped, order reversed, duplicates merged | contents, count and order | deck rows, offered options (order and repeats are part of the contract), `dist` of places |
| State | an assignment skipped, old state kept | the result **and** the stored state | a queue cursor after restart, a history column written, a snapshot withdrawn on deletion |
| C# specifics | `??` fallback replaced, `?.` removed, `default` returned, cancellation not propagated, `StringComparison` changed | the `null` path, the cancelled path, a differently cased input | name matching in the scrubber; cancellation of loaders |

## Not a finding

- Generated code, records that only hold data, auto-properties, one-line forwarding methods.
- A logging-only or formatting-only change, unless the text is the contract (a crash report, a shared file, a Loc key).
- A guard whose removal falls through to the same result, exception, state and side effects.
- A boundary edit that returns the same value at the distinguishing input (`x < min ? min : x` against `<=`).
- A private representation change no public input sequence can tell apart, even when the suite stays green.
- An impossible value under the domain (a ninth place in a solo lobby, a negative turn).
- Survivors already recorded as equivalent or accepted in `docs/mutation-testing.md` (the simulator's seed, format
  strings of the crash report, and the others listed there).
- Several syntax variants of one missing behaviour: one row.
- A change that needs a statement inserted or reordered rather than an existing expression changed.
- Hypothetical future callers.

A missing direct assertion makes an **observable** candidate a survivor. It does not make an inert change meaningful.

## Exhaustive audit of one module (only when asked)

1. Enumerate candidates by behaviour, not by operator.
2. For each: the public input and the two different observations.
3. Map to the covering tests and their assertions; classify the obviously killed and equivalent by reading.
4. Execute every candidate that would be reported as Survived, one at a time, reverting after each.
5. Report executed Killed and Survived counts separately from Likely killed, unverified and no-coverage counts, and
   say what was left out of scope.

For a module that has a Stryker target, propose a Stryker run in CI instead of a hand audit; it is the canonical
verifier and its results belong in `docs/mutation-testing.md`.
