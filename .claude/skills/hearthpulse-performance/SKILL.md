---
name: hearthpulse-performance
description: Use when Hearthpulse is slow, stutters, uses too much memory or CPU, or a change touches a hot path and its cost must be known - Power.log parsing, snapshot publishing, the Battlegrounds combat simulator, overlay updates on the UI thread, page opening, start-up, history queries, memory in the tray. Covers measuring first with tools/Hearthpulse.Bench, collecting a trace or counters when the bench cannot show it, and scanning hot code for .NET allocation and async anti-patterns. Not for a correctness bug that happens to be slow to reproduce (diagnosing-bugs). Triggers - "тормозит", "лагает оверлей", "жрёт память", "утечка памяти", "медленно открывается", "профилирование", "замер", "аллокации", "performance", "hot path", "dotnet-trace".
---

> Source: [dotnet/skills](https://github.com/dotnet/skills) `plugins/dotnet-diag/skills/analyzing-dotnet-performance/`
> (`SKILL.md`, `references/critical-patterns.md`) and `plugins/dotnet-diag/skills/dotnet-trace-collect/` (`SKILL.md`,
> `references/dotnet-trace-collect.md`) at `2877c71`, MIT, © .NET Foundation and Contributors, see
> [THIRD-PARTY-NOTICES](../THIRD-PARTY-NOTICES.md). Hearthpulse changes: one project workflow (measure, locate,
> scan, re-measure) in place of two generic ones; the pattern catalogue cut to what applies to a desktop tracker and
> moved to [reference/patterns.md](reference/patterns.md); the tool matrix cut to a Windows desktop process plus the
> Linux worker in [reference/trace-collection.md](reference/trace-collection.md); containers, Kubernetes, .NET
> Framework, ASP.NET, the grep-count checklist and the emoji report dropped. Codex also has the upstream skills as
> plugins; inside this repository this skill decides the order of work and they add nothing to load.

# Performance work in Hearthpulse

**Measure, then change, then measure again on the same input.** A finding without a number from this repository is
a hypothesis; say so in the report.

## Where time and memory go

| Path | Code | Thread | What bounds it |
|---|---|---|---|
| Log lines to game state | `Logs/LogWatcher`, `Logs/LogTail`, `Power/PowerParser.Feed`, the `IPowerListener` trackers | log thread | runs for every line of every game; per-line allocation is the cost |
| Snapshot publishing | `Tracking/GameSession` (builds the immutable `LiveSnapshot`, raised through `SnapshotChanged`) | log thread, then posted to the UI thread | throttled, so the cost is bytes and time per snapshot |
| Combat odds | `Battlegrounds/CombatSimulator` and `Simulation/`, called from `Tracking/BattlegroundsTracker.Combat.cs` | log thread | `Runs`, `MinRuns` and a `TimeBudget`; read the current values there, they have changed between stages |
| Overlay update | `OverlayViewModel.Update` and `Observable.Set` (shared presentation), then each UI's layout | UI thread | a `PropertyChanged` for equal content rebuilds visuals; equality of bound records decides it |
| Game memory reads | `Tracking/MemoryPoller`, `Memory/HearthstoneMemory` | own thread, Windows only | how often and under which conditions a read runs |
| Pages, start-up, history | main-window pages, `Cards/CardDb`, `Data/HistoryStore`, `Stats/` | UI thread for views; loaders are tasks | first-open time, working set, query time on a large history |

History of what was measured, changed and deliberately left alone, with numbers: `docs/perf-architecture-audit.md`
(eleven stages). Search it for the path before proposing a change: several obvious ideas are recorded there as
"measured and dropped".

## Workflow

1. **State the symptom as a number and a scenario**: which path, which input, what is observed, what would be
   acceptable. "The overlay lags in Battlegrounds combat" becomes "UI-thread wait above N ms while a snapshot applies".
2. **Measure with the bench.** `tools/Hearthpulse.Bench` replays kept logs through the real pipeline and prints
   plain text for a before/after diff. It targets `net10.0-windows`, so it runs on Windows only.
   - Set `HEARTHPULSE_DATA` to a **copy** of the data folder without `account.dat`; the bench refuses the real one.
   - Pass `--logs <dir>` with kept `Power.log` sessions. The built-in default is one machine's path, do not rely on it.
   - Print the command list first and pick the command for the path:
     `dotnet run --project tools/Hearthpulse.Bench -c Release -- --help` (`replay` for parsing and publishing,
     `overlay` for both UIs' view-model updates, `history`, `startup`, `ram`, `pages`, `frames`, `idle`, `rest`, ...).
   - Release build, game closed for the idle and rest commands, three rounds, report the spread and not one run.
   - No kept logs with the needed mode (the audit's first logs had no Battlegrounds games): say the scenario is
     unmeasured instead of extrapolating from another mode.
3. **Locate.** When the bench shows the cost but not the cause, or the symptom only appears in a live session,
   collect counters or a trace: [reference/trace-collection.md](reference/trace-collection.md). The player's own
   `hearthpulse.log` may already carry the watchdog's window timing lines (`App.Watchdog.cs`); read it before
   asking for a trace.
4. **Scan the code on that path** against [reference/patterns.md](reference/patterns.md). Only the measured path:
   the same pattern in code that runs once at start-up is not a finding.
5. **Change one thing**, keep behaviour identical, and prove that with the existing tests of the path
   ([hearthpulse-testing](../hearthpulse-testing/SKILL.md)); a faster wrong answer in the simulator or the parser
   is a regression.
6. **Re-measure the same command on the same input** and report before and after side by side, with the machine,
   the build configuration and the commit. No improvement means the change is reverted, not kept "for cleanliness".
7. **Record** a stage worth keeping in `docs/perf-architecture-audit.md` in its existing format, including what was
   measured and not done.

## Rules of this codebase

- The log thread never waits for the network, the disk cache or the UI. The UI thread never parses, simulates or
  reads game memory. A fix that moves work between threads names both sides.
- Bound records are immutable views (`docs/adr/0012-bound-records-are-immutable-views.md`): give a record that
  holds lists content equality rather than letting `Observable.Set` see a new reference every snapshot.
- Combat effects are shared across threads and hold no mutable fields
  (`src/Hearthpulse.Core/Battlegrounds/Effects/README.md`); caching inside an effect is a correctness bug.
- A wall-clock assertion in a test fails under coverage instrumentation, which slows hot code many times: assert on
  counts or allocations, or keep the timing in the bench.
- Both UIs: a presentation change is measured in WPF and Avalonia (`overlay` covers both view models); a WPF-only
  number says nothing about the Avalonia host on macOS or Linux, where game-memory reads do not exist at all.
- Build and bench output for parallel sessions goes to a short `--artifacts-path`; a long path breaks the SQLite
  native library and every history measurement with it (`hearthpulse-dev`, "Build and test").
- Never run the app, a render or a bench that opens the overlay while `Hearthstone.exe` runs unless the scenario
  needs the live game and the owner asked for it.

## Report

Lead with the measured result: scenario, command, before, after, spread. Then findings ranked by measured cost, each
with `file:line`, the pattern, and the fix; mark every finding that was not measured as "by reading, not measured".
State what could not be measured here (no Battlegrounds logs, no live game, not Windows) rather than leaving it out.
