# Collecting counters, traces and dumps

> Adapted from [dotnet/skills](https://github.com/dotnet/skills)
> `plugins/dotnet-diag/skills/dotnet-trace-collect/SKILL.md` and `references/dotnet-trace-collect.md` at `2877c71`,
> MIT, © .NET Foundation and Contributors, see [THIRD-PARTY-NOTICES](../../THIRD-PARTY-NOTICES.md). Cut to a desktop
> process on Windows, macOS or Linux and the Linux source worker.

Use this when `tools/Hearthpulse.Bench` shows a cost without a cause, or the symptom needs the running app.

**The commands below are upstream's, copied at the pinned revision; they were not run in this repository.** Check
each tool's `--help` on the machine before relying on a flag. None of the tools is in `dotnet-tools.json`:
installing a global tool changes the machine, so use one that is already installed
(`dotnet-trace --version`, `dotnet-counters --version`) or ask the owner before installing.

## What to collect for which symptom

| Symptom | Collect | Why |
|---|---|---|
| CPU high while idle or in a game | a sampled CPU trace over the busy period | shows which stacks burn time |
| The overlay or a page stalls | a thread-time trace (time blocked as well as running) | a stall is usually a wait: a lock, I/O, a blocked dispatcher |
| Memory grows over hours or games | **two** process dumps, one early and one after growth, then diff the heaps; a GC trace while it grows | one dump shows what is alive, only the difference shows what leaks |
| Many or long GC pauses | a GC-events trace | a dump cannot show pause times |
| Slow or failing requests | thread-time plus the `System.Net.Http` event source | status codes, pool waits and DNS time are events, not stacks |
| A start-up failure loading an assembly | launch under the tracer with assembly-loader events | the process dies before you can attach |

Before a leak hunt, read stage 11 of `docs/perf-architecture-audit.md`: growth after games was measured there and
found not to be a leak. Compare against its numbers first.

## The running app

Start it yourself with `HEARTHPULSE_DATA` on a copy of the data folder, a Release build, and note the process id
(`dotnet-trace ps` lists .NET processes). Attaching to the player's real session needs the owner's request.

```
dotnet-trace ps
dotnet-trace collect -p <PID> --duration 00:00:30
dotnet-trace collect -p <PID> --profile gc-verbose
dotnet-trace collect -p <PID> --format speedscope
dotnet-trace collect -p <PID> --providers System.Net.Http
```

- The default profiles sample CPU and thread time for managed frames; `--providers` replaces the defaults, so add
  `--profile` again when you need both.
- `dotnet-trace` needs no administrator rights and works on all three OSes, but sees managed frames only.
- On Windows with administrator rights, PerfView gives richer data (native frames, ETW): `PerfView /ThreadTime collect`
  for stalls, `PerfView collect /GCCollectOnly` for GC. For a long reproduction use a circular buffer and a stop
  trigger that fires on the **symptom**, never on recovery, or the buffer has already overwritten the interesting part.
- On Linux with .NET 10, root and a recent kernel, `dotnet-trace collect-linux` adds native and kernel frames.

Counters for a first look (CPU, GC heap, allocation rate, thread-pool queue) without a trace: `dotnet-counters
monitor -p <PID>` (the tool's standard form, not part of the upstream text); the audit used `dotnet-counters` on a
Release build with Hearthstone closed.

## The source worker and the pool

`Hearthpulse.SourceWorker` is a short-lived process per job: attach is too late. Launch it under the tracer
(`dotnet-trace collect -- <path-to-worker> --input ... --output ...`) on a synthetic source. The pool itself is
Node; its timing questions are answered by its tests and `/healthz`, not by .NET tools.

## Reading the result

| File | Open with |
|---|---|
| `.nettrace` | PerfView or Visual Studio on Windows; `--format speedscope` output in Speedscope |
| `.etl.zip` | PerfView |
| a process dump | Visual Studio, or WinDbg / lldb with SOS; diff two dumps' heaps in PerfView |

## Handing a trace over

A trace or dump of the app holds whatever was in memory: card data, settings, possibly the signed-in session. Treat
it like the data folder: keep it out of the repository and out of any upload, and record with it the commit, build
configuration, OS, .NET version, process id, UTC start and end, the exact command and the scenario played.
