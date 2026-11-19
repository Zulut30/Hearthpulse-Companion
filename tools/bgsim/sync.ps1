<#
.SYNOPSIS
  Copies the Battlegrounds combat simulator from Hearthpulse into a BGSim checkout (Manacost-Labs/BGSim), builds and
  tests BGSim there and commits "Sync from Hearthpulse <sha>". Pushes only with -Push.

.DESCRIPTION
  Hearthpulse is the source of the simulator (owner, 2026-11-19); BGSim is its standalone copy. The lists below decide
  what is copied ($Include), which tests stay behind ($ExcludedTests) and which BGSim files are BGSim's own ($Owned).
  Change the lists, not the BGSim checkout by hand. tools/bgsim/README.md explains how to resolve a failed build.

  The copy comes from the commit -Ref names (default main), not from the working tree, so the commit message names
  exactly what was copied.

.EXAMPLE
  powershell -File tools/bgsim/sync.ps1 -DryRun                  # what would be copied and removed; nothing written
  powershell -File tools/bgsim/sync.ps1                          # scratch clone: copy, build, test, commit
  powershell -File tools/bgsim/sync.ps1 -Target D:\BGSim -Push   # an existing checkout, then push
#>
[CmdletBinding()]
param(
    # BGSim checkout. Cloned from -Remote when the folder is missing or empty.
    [string]$Target = (Join-Path ([IO.Path]::GetTempPath()) 'bgsim-sync\BGSim'),
    # Hearthpulse repository (default: the one this script is in).
    [string]$Source = '',
    # Hearthpulse commit to copy from.
    [string]$Ref = 'main',
    [string]$Remote = 'https://github.com/Manacost-Labs/BGSim.git',
    # List what would be copied, updated and removed, then stop.
    [switch]$DryRun,
    # Also run the wall-clock tests (Category=Timing), which a busy PC skews; left out like tools/dev/check.ps1 does.
    [switch]$Timing,
    # Push the BGSim commit. Without it the commit stays local.
    [switch]$Push
)
$ErrorActionPreference = 'Stop'

# --- What is synced -----------------------------------------------------------------------------------------------

# Hearthpulse paths copied to BGSim, as git pathspecs at -Ref. A pathspec that matches nothing stops the sync: a listed
# file was renamed or removed upstream and the list needs a look.
$Include = @(
    # The simulator.
    'src/Hearthpulse.Core/Battlegrounds/CombatSimulator*.cs'
    'src/Hearthpulse.Core/Battlegrounds/CombatDamage.cs'
    'src/Hearthpulse.Core/Battlegrounds/SimInputs.cs'
    'src/Hearthpulse.Core/Battlegrounds/Simulation'
    'src/Hearthpulse.Core/Battlegrounds/Effects'
    # Self-contained Core files outside Battlegrounds/ that the simulator or a portable test reaches.
    'src/Hearthpulse.Core/Tracking/SimEstimates.cs'
    # Portable tests.
    'tests/Hearthpulse.Tests/Sim'
    'tests/Hearthpulse.Tests/Effects'
    'tests/Hearthpulse.Tests/Properties/SimulatorProperties.cs'
    'tests/Hearthpulse.Tests/TimingCollection.cs'
    'tests/Hearthpulse.Tests/RepoFiles.cs'
    # The coverage matrix (SimMatrixTests checks it against the code).
    'docs/bg-sim-matrix.md'
)

# Test files that stay in Hearthpulse: they need the log tracker, the replay, history and My Stats, Hearthpulse.Bench or
# fixtures BGSim does not carry. An entry that no longer matches a file is reported as stale.
$ExcludedTests = [ordered]@{
    'tests/Hearthpulse.Tests/Sim/CaptureTests.cs'                   = 'Power.log replay through BattlegroundsTracker'
    'tests/Hearthpulse.Tests/Sim/SimInputsTests.cs'                 = 'Power.log replay through BattlegroundsTracker'
    'tests/Hearthpulse.Tests/Sim/SimInputsTests.Deity.cs'           = 'Power.log replay through BattlegroundsTracker (PowerLog)'
    'tests/Hearthpulse.Tests/Sim/LeftoverCaptureTests.cs'           = 'GameState and BattlegroundsTracker'
    'tests/Hearthpulse.Tests/Sim/DuosCaptureTests.cs'               = 'Power.log replay through BattlegroundsTracker'
    'tests/Hearthpulse.Tests/Sim/DamageCalibrationReplayTests.cs'   = 'real Power.log games'
    'tests/Hearthpulse.Tests/Sim/GeneratedPoolsTests.cs'            = 'Fixtures/BgPools card data and CardDb.Parse'
    'tests/Hearthpulse.Tests/Effects/EffectCardTextGuardTests.cs'   = 'Fixtures/Effects/effect-card-texts.json (BGSim.Tests copies no fixtures)'
    'tests/Hearthpulse.Tests/Sim/SimMeasurementTests.cs'            = 'Hearthpulse.Bench measurement sources'
    'tests/Hearthpulse.Tests/Sim/SimReplayKit.cs'                   = 'Hearthpulse.Bench simreplay'
    'tests/Hearthpulse.Tests/Sim/SimReplayTests.cs'                 = 'Hearthpulse.Bench simreplay'
    'tests/Hearthpulse.Tests/Sim/SimReplayResidualTests.cs'         = 'Hearthpulse.Bench simreplay and real Power.log games'
    'tests/Hearthpulse.Tests/Sim/ParallelRunsTests.cs'              = 'Hearthpulse.Bench SimStats.Wilson and BattlegroundsTracker in two of its cases'
}

# Hearthpulse folder -> BGSim folder; the first matching prefix wins.
$Map = [ordered]@{
    'src/Hearthpulse.Core/Battlegrounds/' = 'src/BGSim/'
    'src/Hearthpulse.Core/'               = 'src/BGSim/'
    'tests/Hearthpulse.Tests/'            = 'tests/BGSim.Tests/'
    'docs/'                               = 'docs/'
}

# Text changes BGSim needs in a copied file (BGSim path, exact text). A text that no longer occurs stops the sync.
$Edits = @(
    @{ Path = 'tests/BGSim.Tests/RepoFiles.cs'; Find = 'Hearthpulse.slnx'; Replace = 'BGSim.slnx' }
)

# BGSim folders the sync owns: a file there that the copy no longer brings is removed.
$SyncedRoots = @('src/BGSim/', 'tests/BGSim.Tests/')

# BGSim's own files inside the synced folders: never written or removed. Shims stand in for Hearthpulse types the
# simulator reaches but BGSim cannot carry (Cards/Card.cs: the card database pulls HearthstoneJSON, caches, app paths).
$Owned = @(
    'src/BGSim/BGSim.csproj'
    'src/BGSim/Cards/Card.cs'
    'src/BGSim/Shims/*'
    'tests/BGSim.Tests/BGSim.Tests.csproj'
    'tests/BGSim.Tests/Shims/*'
)

$TrailerLine = 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>'

# --- Helpers ------------------------------------------------------------------------------------------------------

function Assert-Exit([string]$what) {
    if ($LASTEXITCODE) { throw "$what failed (exit $LASTEXITCODE)." }
}

function Test-Owned([string]$path) {
    foreach ($pattern in $Owned) { if ($path -like $pattern) { return $true } }
    return $false
}

# Text with CRLF folded to LF: a checkout's line endings are not a change.
function Get-Text([string]$path) {
    return [IO.File]::ReadAllText($path).Replace("`r`n", "`n")
}

# Missing types in a failed build, with the Core file that declares each: copy it ($Include) when it is
# self-contained, shim it under src/BGSim/Shims when it drags the tracker or the app in, or exclude the test.
function Show-BuildHints([string[]]$lines, [string]$sha) {
    $pattern = "^(?<file>[^(]+)\(\d+,\d+\): error (?<code>CS0246|CS0234|CS0103): .*?'(?<name>\w+)'( does not exist in the namespace '(?<ns>[\w.]+)')?"
    $missing = @{}
    foreach ($line in $lines) {
        $m = [regex]::Match($line.Trim(), $pattern)
        if (-not $m.Success) { continue }
        $file = $m.Groups['file'].Value
        $name = $m.Groups['name'].Value
        # CS0234 on 'Hearthpulse.Core.Data': a whole Core folder (namespace) BGSim does not carry.
        if ($m.Groups['ns'].Success) { $name = $m.Groups['ns'].Value + '.' + $name }
        if (-not $missing.ContainsKey($name)) { $missing[$name] = [Collections.Generic.HashSet[string]]::new() }
        [void]$missing[$name].Add($file)
    }
    foreach ($name in ($missing.Keys | Sort-Object)) {
        if ($name.Contains('.')) {
            $where = 'a namespace BGSim does not carry'
        } else {
            $decl = @(git -C $Source grep -l -E "\b(class|record|struct|enum|interface|delegate [^ ]+) $name\b" $sha -- 'src/Hearthpulse.Core') |
                ForEach-Object { $_.Substring($sha.Length + 1) }
            $where = if ($decl) { 'declared in ' + ($decl -join ', ') } else { 'not declared in Core' }
        }
        Write-Host "  $name - $where; used by:" -ForegroundColor Yellow
        $missing[$name] | Sort-Object | Select-Object -First 5 | ForEach-Object { Write-Host "      $_" }
    }
}

# --- Source and target --------------------------------------------------------------------------------------------

if (-not $Source) { $Source = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path }
$sha = (git -C $Source rev-parse --verify "$Ref^{commit}")
Assert-Exit "git rev-parse $Ref"
$short = (git -C $Source rev-parse --short $sha)

if (-not (Test-Path $Target) -or -not (Get-ChildItem -Force $Target | Select-Object -First 1)) {
    Write-Host "Cloning $Remote into $Target"
    git clone -q $Remote $Target
    Assert-Exit 'git clone'
}
$Target = (Resolve-Path $Target).Path
if (-not (Test-Path (Join-Path $Target 'BGSim.slnx'))) { throw "$Target is not a BGSim checkout (no BGSim.slnx)." }
# Changes inside the synced folders are fine: the mirror rewrites copies, and an edited shim goes into the sync commit
# (a rerun after a failed build). Anything else is someone's work in progress.
$dirty = @(git -C $Target status --porcelain --untracked-files=all | ForEach-Object { ($_.Substring(3) -split ' -> ')[-1].Trim('"') } |
    Where-Object { $p = $_; -not ($SyncedRoots + 'docs/bg-sim-matrix.md' | Where-Object { $p.StartsWith($_, [StringComparison]::Ordinal) }) })
if ($dirty -and -not $DryRun) { throw "$Target has changes outside the synced folders; commit or discard them first:`n$($dirty -join "`n")" }
if (-not $DryRun) {
    # Windows PowerShell turns a native command's stderr into a terminating error under 'Stop'; no upstream is fine.
    $ErrorActionPreference = 'Continue'
    $upstream = git -C $Target rev-parse --abbrev-ref '@{upstream}' 2>$null
    $ErrorActionPreference = 'Stop'
    if ($upstream) {
        git -C $Target pull -q --ff-only
        Assert-Exit 'git pull --ff-only in BGSim'
    }
}
$bgsimHead = (git -C $Target rev-parse --short HEAD)

$work = Join-Path ([IO.Path]::GetTempPath()) ('bgsim-sync-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$stage = Join-Path $work 'stage'
New-Item -ItemType Directory -Force $stage | Out-Null
try {
    # --- Plan: what the copy brings, what changes, what goes ------------------------------------------------------

    $zip = Join-Path $work 'source.zip'
    git -C $Source archive --format=zip -o $zip $sha -- @Include
    Assert-Exit 'git archive (a listed path may be gone upstream: check $Include)'
    Expand-Archive -LiteralPath $zip -DestinationPath $stage

    $stale = @()
    foreach ($path in $ExcludedTests.Keys) {
        $staged = Join-Path $stage $path
        if (Test-Path -LiteralPath $staged) { Remove-Item -LiteralPath $staged } else { $stale += $path }
    }

    $plan = @{}  # BGSim path -> staged file
    foreach ($file in Get-ChildItem -LiteralPath $stage -Recurse -File) {
        $rel = $file.FullName.Substring($stage.Length + 1).Replace('\', '/')
        $to = $null
        foreach ($prefix in $Map.Keys) {
            if ($rel.StartsWith($prefix, [StringComparison]::Ordinal)) { $to = $Map[$prefix] + $rel.Substring($prefix.Length); break }
        }
        if (-not $to) { throw "No BGSim folder for $rel; add its folder to `$Map." }
        if (Test-Owned $to) { throw "$rel would overwrite BGSim's own $to; shims are never synced." }
        if ($plan.ContainsKey($to)) { throw "$rel and another file both map to $to." }
        $plan[$to] = $file.FullName
    }

    $utf8 = New-Object Text.UTF8Encoding $false
    foreach ($edit in $Edits) {
        $file = $plan[$edit.Path]
        if (-not $file) { throw "Edit target $($edit.Path) is not synced; fix `$Edits." }
        $text = [IO.File]::ReadAllText($file)
        if (-not $text.Contains($edit.Find)) { throw "$($edit.Path) no longer contains '$($edit.Find)'; fix `$Edits." }
        [IO.File]::WriteAllText($file, $text.Replace($edit.Find, $edit.Replace), $utf8)
    }

    $adds = @(); $updates = @(); $same = 0
    foreach ($to in ($plan.Keys | Sort-Object)) {
        $dst = Join-Path $Target $to
        if (-not (Test-Path -LiteralPath $dst)) { $adds += $to }
        elseif ((Get-Text $dst) -cne (Get-Text $plan[$to])) { $updates += $to }
        else { $same++ }
    }
    $present = @(git -C $Target ls-files --cached --others --exclude-standard -- @SyncedRoots)
    Assert-Exit 'git ls-files in BGSim'
    $removes = @($present | Where-Object { -not $plan.ContainsKey($_) -and -not (Test-Owned $_) } | Sort-Object -Unique)

    Write-Host "Hearthpulse $short ($Ref) -> $Target (BGSim $bgsimHead)" -ForegroundColor Cyan
    Write-Host ("  {0} files to copy: {1} new, {2} changed, {3} unchanged; {4} to remove; {5} test files left behind" -f `
        $plan.Count, $adds.Count, $updates.Count, $same, $removes.Count, $ExcludedTests.Count)
    $adds | ForEach-Object { Write-Host "  + $_" -ForegroundColor Green }
    $updates | ForEach-Object { Write-Host "  ~ $_" }
    $removes | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    $stale | ForEach-Object { Write-Host "  stale exclude (no such file upstream): $_" -ForegroundColor Yellow }
    if ($DryRun) {
        Write-Host '  left behind:'
        $ExcludedTests.Keys | ForEach-Object { Write-Host "    $_ - $($ExcludedTests[$_])" }
        Write-Host 'Dry run: nothing written.' -ForegroundColor Cyan
        return
    }

    # --- Apply --------------------------------------------------------------------------------------------------

    foreach ($to in $adds + $updates) {
        $dst = Join-Path $Target $to
        New-Item -ItemType Directory -Force (Split-Path -Parent $dst) | Out-Null
        Copy-Item -LiteralPath $plan[$to] -Destination $dst -Force
    }
    foreach ($to in $removes) { Remove-Item -LiteralPath (Join-Path $Target $to) }
    foreach ($root in $SyncedRoots) {
        Get-ChildItem -LiteralPath (Join-Path $Target $root) -Recurse -Directory | Sort-Object { $_.FullName.Length } -Descending |
            Where-Object { -not (Get-ChildItem -Force -LiteralPath $_.FullName) } | Remove-Item
    }

    # --- Build and test, one at a time with the other local checks (tools/dev/check.ps1) -------------------------

    (Get-Process -Id $PID).PriorityClass = 'Idle'
    $env:DOTNET_CLI_UI_LANGUAGE = 'en'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = 'true'
    $env:DOTNET_NOLOGO = 'true'
    $env:MSBUILDDISABLENODEREUSE = '1'
    $sln = Join-Path $Target 'BGSim.slnx'
    $results = Join-Path $work 'results'
    $gate = New-Object Threading.Mutex($false, 'Hearthpulse.LocalCheck')
    $ownsGate = $false
    try {
        try {
            $ownsGate = $gate.WaitOne(0)
            if (-not $ownsGate) { Write-Host 'Waiting for the current Hearthpulse check to finish...'; $ownsGate = $gate.WaitOne() }
        } catch [Threading.AbandonedMutexException] { $ownsGate = $true }

        dotnet restore $sln --source https://api.nuget.org/v3/index.json -v:q | Out-Host
        Assert-Exit 'dotnet restore BGSim.slnx'
        $log = @(dotnet build $sln --no-restore -c Release -warnaserror -m:2 --disable-build-servers -p:UseSharedCompilation=false -nologo -v:q -clp:NoSummary)
        $code = $LASTEXITCODE
        $log | Out-Host
        if ($code) {
            Write-Host 'BGSim does not build. Missing types:' -ForegroundColor Red
            Show-BuildHints $log $sha
            Write-Host "The checkout keeps the copied files for a look: $Target"
            exit 1
        }

        $filter = if ($Timing) { @() } else { @('--filter', 'Category!=Timing') }
        dotnet test $sln --no-build -c Release @filter --nologo -v q --logger 'trx;LogFileName=bgsim.trx' --results-directory $results -- xUnit.MaxParallelThreads=2 | Out-Host
        $testCode = $LASTEXITCODE
    } finally {
        if ($ownsGate) { $gate.ReleaseMutex() }
        $gate.Dispose()
    }
    $trx = Get-ChildItem -LiteralPath $results -Filter *.trx -Recurse | Select-Object -First 1
    if ($trx) {
        $c = ([xml](Get-Content -Raw -LiteralPath $trx.FullName)).TestRun.ResultSummary.Counters
        $which = if ($Timing) { 'all' } else { 'Category!=Timing' }
        Write-Host ("BGSim tests [{0}]: {1} passed, {2} failed, {3} not run, {4} total" -f $which, $c.passed, $c.failed, $c.notExecuted, $c.total)
    }
    if ($testCode) { Write-Host "BGSim tests fail; nothing committed. Checkout: $Target" -ForegroundColor Red; exit 1 }

    # --- Commit and push ----------------------------------------------------------------------------------------

    git -C $Target add -A -- @SyncedRoots 'docs/bg-sim-matrix.md'
    Assert-Exit 'git add in BGSim'
    if (@(git -C $Target diff --cached --name-only).Count -eq 0) {
        Write-Host "BGSim already matches Hearthpulse $short; no commit." -ForegroundColor Green
    } else {
        $message = Join-Path $work 'message.txt'
        [IO.File]::WriteAllText($message, "Sync from Hearthpulse $short`n`n$TrailerLine`n", $utf8)
        git -C $Target commit -q -F $message
        Assert-Exit 'git commit in BGSim'
        Write-Host "Committed in BGSim: $(git -C $Target log -1 --format='%h %s')" -ForegroundColor Green
    }
    if ($Push) {
        git -C $Target push
        Assert-Exit 'git push BGSim'
    } else {
        Write-Host 'Not pushed (run with -Push to publish).'
    }
} finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
