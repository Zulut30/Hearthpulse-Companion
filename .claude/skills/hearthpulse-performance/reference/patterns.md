# .NET patterns worth checking on a measured hot path

> Adapted from [dotnet/skills](https://github.com/dotnet/skills)
> `plugins/dotnet-diag/skills/analyzing-dotnet-performance/SKILL.md` and `references/critical-patterns.md` at
> `2877c71`, MIT, © .NET Foundation and Contributors, see [THIRD-PARTY-NOTICES](../../THIRD-PARTY-NOTICES.md).
> Selected and reworded for this project; impact figures are upstream's general ones, not measurements of this code.

Apply to code the bench or a trace put on a hot path (per log line, per snapshot, per simulated fight, per frame).
Elsewhere these are style, not performance. Files under 500 lines: read the whole file before searching for
patterns. The project targets .NET 10, so every API below is available.

## Critical: wrong or orders of magnitude

| Pattern | Why | Instead |
|---|---|---|
| `.Result`, `.Wait()`, `GetAwaiter().GetResult()` on the UI or log thread | deadlock or a frozen overlay | `await`; or run the work as a task and post the result |
| Awaiting one `ValueTask` twice | undefined behaviour | await once, or `AsTask()` |
| `stackalloc` inside a loop | stack overflow, cannot be caught | hoist it above the loop |
| A regex with nested quantifiers on log or network text | catastrophic backtracking on a crafted line | restructure, or `RegexOptions.NonBacktracking` |
| `new HttpClient()` per request | socket exhaustion | the shared client built in the composition root (`hearthpulse-data-sources`) |
| `new JsonSerializerOptions()` per call | the serializer rebuilds its metadata every time | one static instance, as `AppSettings` keeps |
| Enumerating a lazy `IEnumerable` twice | the query runs twice, results can differ | materialise once |

## Per-line and per-snapshot allocation

| Pattern | Instead |
|---|---|
| `Substring` to parse or compare a slice of a log line | `AsSpan(...)` with `int.Parse(span)`, `span.SequenceEqual`, `span.StartsWith` |
| `IndexOf("x")`, `StartsWith("x")`, `Contains("x")` on machine text without a comparison | pass `StringComparison.Ordinal`: faster and culture-independent, which the parser needs anyway |
| `ToLower()` / `ToUpper()` to compare | `Equals(..., StringComparison.OrdinalIgnoreCase)` |
| Chained `.Replace()` or `+=` in a loop | one pass with a `StringBuilder` or `string.Create` |
| `string.Format` with value types | interpolation (no boxing) |
| `new Regex(...)` in a method, or a static `Regex` with `RegexOptions.Compiled` | `[GeneratedRegex]` on a partial method, as the code base already does elsewhere |
| LINQ (`Where`, `Select`, `Any`, `OrderBy`) inside the per-line or per-fight loop | a `foreach`; LINQ in code that runs per game or per page is fine |
| `ContainsKey` then the indexer | `TryGetValue` |
| `new List<T>()` / `new Dictionary<,>()` per call for scratch work | a reused buffer owned by the caller, or `ArrayPool<T>.Shared` with a `try/finally` return |
| `params` arrays, closures capturing locals, boxing through `object` or a non-generic interface in the loop | an overload, a static lambda, a generic method |
| A lookup table built per call, or a mutable `static readonly Dictionary` never changed after start | build once; `FrozenDictionary` only when it is truly read-only and looked up on the hot path |
| Repeated `IndexOfAny` over the same character set | `SearchValues<char>` created once |

## Snapshot and UI specifics

- A record with collection members compares by reference: two equal snapshots look different and every binding
  fires. Compare content (a custom `Equals`, or an equality-aware wrapper) before raising the change.
- Building display rows in the view model on every snapshot when only one value changed: keep the previous row
  instances and replace only the changed ones.
- Image decode and card-art lookup on the UI thread during a list rebuild: decode off-thread, cap the cache in bytes.
- SQLite: one query per row in a loop; `SELECT *` when two columns are needed; aggregates computed in C# over all
  rows. Push the aggregate into SQL and read only the needed columns.

## Do not suggest

- `Span<T>` in an `async` method (use `Memory<T>`), `unsafe` code, or `CollectionsMarshal.AsSpan` without a bench
  number that justifies the fragility.
- `ConfigureAwait(false)` as a performance fix in app code: it changes which thread continues, which here is a
  correctness question for view-model writes.
- `ValueTask` everywhere: only where a hot method usually completes synchronously.
- `sealed` on a class as a finding by itself: new types are already `sealed` by convention (`hearthpulse-dev`).
- Any pattern from this file in code that is not on a measured path.

## Counting

When a pattern repeats, count both sides ("12 of 15 call sites pass `StringComparison`") and report exact counts
from a search, not an impression. One to ten instances is a local fix; the same pattern across a whole folder is a
convention to fix once, with one finding.
