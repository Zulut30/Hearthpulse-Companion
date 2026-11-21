# BGSim sync

Hearthpulse is the source of the Battlegrounds combat simulator (owner, 2026-11-19). The public repository
[Manacost-Labs/BGSim](https://github.com/Manacost-Labs/BGSim) is a standalone copy: a net10.0 library `src/BGSim` and
its tests `tests/BGSim.Tests`. `sync.ps1` copies the simulator from a Hearthpulse commit into a BGSim checkout, builds
and tests BGSim there, and commits `Sync from Hearthpulse <sha>`. Run it after the final merge to `main`.

```
powershell -File tools/bgsim/sync.ps1 -DryRun                  # + new, ~ changed, - removed; writes nothing
powershell -File tools/bgsim/sync.ps1                          # scratch clone in %TEMP%\bgsim-sync\BGSim
powershell -File tools/bgsim/sync.ps1 -Target D:\BGSim -Push   # an existing checkout, then publish
```

- `-Ref` (default `main`) is the Hearthpulse commit copied; the working tree is never read.
- A missing or empty `-Target` is cloned. A real run needs a clean checkout and pulls it first.
- Build: Release, `-warnaserror`. Tests: without `Category=Timing` unless `-Timing`. Both wait for the other local
  checks on the same mutex as `tools/dev/check.ps1`.
- The commit stays local without `-Push`. Nothing is committed when a build or a test fails; the checkout keeps the
  copied files for a look.

## The lists

The lists at the top of `sync.ps1` decide everything; edit them, not BGSim by hand.

| List | Holds |
|---|---|
| `$Include` | Hearthpulse paths copied: the simulator, self-contained Core files it reaches, portable tests, `docs/bg-sim-matrix.md` |
| `$ExcludedTests` | Test files that stay behind (tracker, replay, history, Hearthpulse.Bench, fixtures), one reason each |
| `$Map` | Folder mapping: `Core/Battlegrounds/` and the rest of `Core/` go to `src/BGSim/`, `Hearthpulse.Tests/` to `BGSim.Tests/` |
| `$Edits` | Text BGSim needs changed in a copy (`RepoFiles.cs` finds `BGSim.slnx`) |
| `$Owned` | BGSim's own files the sync never writes or removes: both csproj files, the shims |

Inside `src/BGSim/` and `tests/BGSim.Tests/` the copy is a mirror: a file that is no longer copied is removed, unless
it is owned. BGSim's README, LICENSE, solution and other docs are outside the synced folders.

## BGSim's own parts

BGSim also has parts of its own outside the mirrored folders, never synced: `src/BGSim.Server` (HTTP API, Native AOT),
`tests/BGSim.Server.Tests`, `tools/BGSim.LoadTest`, `Dockerfile`, `compose.yaml`, `.github/workflows/docker.yml` and
`docs/server.md`. `sync.ps1` builds and tests the whole `BGSim.slnx`, the server included.

The server keeps the simulator whole with `<TrimmerRootAssembly Include="BGSim" />`, but its AOT publish still warns
about reflection it cannot prove. New reflection in the simulator (`Effects/`, `Simulation/`) must stay AOT-compatible,
or BGSim's AOT publish of the server prints an IL2xxx warning. Suppress such a warning in place with a justification, as
`EffectRegistry` and `CombatEffect` do (`[UnconditionalSuppressMessage("Trimming", "IL20xx", Justification = ...)]`).

## Shims

A shim is a BGSim-only stand-in for a Hearthpulse type the simulator reaches but BGSim cannot carry.
`src/BGSim/Cards/Card.cs` replaces the card database (HearthstoneJSON, caches, app paths) with the fields
`CombatRelevance` reads. New shims go to `src/BGSim/Shims/<Type>.cs` and are committed in BGSim itself.

## A failed BGSim build

The script lists each missing type, the Core file that declares it and the files that use it. For each one:

1. The declaring file needs only the simulator and the BCL: add it to `$Include`.
2. It drags in the tracker, the card database, the app paths or a package: write a shim in BGSim, or decouple the
   simulator from it in Hearthpulse.
3. Only a test needs it: add the test file to `$ExcludedTests` with the reason.

A member a shim lacks (CS1061 on `Card`) is added to the shim in BGSim. A test package new to `Hearthpulse.Tests`
goes into BGSim's own `tests/BGSim.Tests/BGSim.Tests.csproj`. Then `-DryRun` again, then the sync.
