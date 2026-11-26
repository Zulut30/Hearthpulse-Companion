---
name: hearthpulse-visual-check
description: Use whenever a Hearthpulse change affects what the player sees and you need proof - render the WPF and Avalonia UIs off-screen with --render-ui (always on a copy of the data folder via HEARTHPULSE_DATA), list or add render scenes (DevRender*.cs, OffscreenRenderer*.cs), compare before/after PNGs, render theme presets, and inspect PNGs for clipping, overlap, contrast, spacing and parity. Triggers - "покажи скриншот", "отрендерь", "проверь вёрстку", "before/after", "PNG", "how does the overlay look", any UI change before calling it done.
---

# Visual check with `--render-ui`

Both apps can render their main window and overlay to PNG without a game running, then exit. That is the only way
to *see* a UI change from a session - use it for every visual change, in both UIs. Token/style rules:
[hearthpulse-design](../hearthpulse-design/SKILL.md). Adding a panel or page (and its scene):
[hearthpulse-overlay-panel](../hearthpulse-overlay-panel/SKILL.md), [hearthpulse-main-page](../hearthpulse-main-page/SKILL.md).

## Never on the real data folder

Render startup suppresses live trackers, account restoration, update checks and changes to Hearthstone's
`log.config`/`client.config` in both hosts. It still reads history, may save settings and loads data/art caches.
The user's real app may be running at the same time. **Always** set `HEARTHPULSE_DATA` to a copy, excluding
`account.dat` (the signed-in session). This keeps render/cache writes isolated; it is not an offline guarantee.

## Render

Bundled script (copies the data once to `%TEMP%\hpd`, builds into the short `%TEMP%\hpa`, sets the env vars for the
child only):

```
python -B -I .claude/skills/hearthpulse-visual-check/scripts/render.py <out-dir> both
python -B -I .claude/skills/hearthpulse-visual-check/scripts/render.py <out-dir> wpf --theme Frost
```

**One render at a time.** `render.py` and `tools/visual/visual_check.py render` hold the named mutex
`Hearthpulse.LocalCheck` (the one `tools/dev/check.ps1` takes) for the whole build and render, so worktrees queue and
only one app window opens; `Waiting for the current Hearthpulse check or render to finish...` means queued. Wrap every
manual `dotnet run ... --render-ui` (or a built exe) in PowerShell, and never call `check.ps1` inside it (it would
wait for itself):

```
$m=[Threading.Mutex]::new($false,'Hearthpulse.LocalCheck'); try{$null=$m.WaitOne()}catch [Threading.AbandonedMutexException]{}; try{ <cmd> } finally { $m.ReleaseMutex() }
```

Manual equivalent (Git Bash; run its `dotnet run` lines through the wrapper above):

```
export HEARTHPULSE_DATA="$TEMP/hpd"          # a copy: settings.json, history.db, cache/
dotnet restore src/Hearthpulse.App --source https://api.nuget.org/v3/index.json --artifacts-path "$TEMP/hpa"
dotnet restore src/Hearthpulse.Desktop --source https://api.nuget.org/v3/index.json --artifacts-path "$TEMP/hpa"
dotnet run --project src/Hearthpulse.App --no-restore --artifacts-path "$TEMP/hpa" -- --render-ui <out>/wpf
dotnet run --project src/Hearthpulse.Desktop --no-restore --artifacts-path "$TEMP/hpa" -- --render-ui <out>/ava
```

- Takes 1-3 minutes per UI (it waits 2.5 s per page and for card art); run it in the background.
- `--artifacts-path` must be short; parallel sessions should each use their own.
- WPF is Windows only and prints nothing (WinExe) - inspect the exit code and current PNG list. Avalonia prints `wrote <path>` and
  "`<scene>`: no kept Battlegrounds log reaches this moment, skipped".
- Language: the data copy's `settings.json` decides (`"Language": "ru"` or `"en"`; `"auto"` = the machine's). Render
  each language from its own copy (`--data %TEMP%\hpd-ru`, `--data %TEMP%\hpd-en`); see `docs/localization.md`.
- No other arguments exist (no size). A few frames instead of the whole set: `HEARTHPULSE_RENDER_PAGES=Overview,History`
  renders only those main-window pages (`Overview`, `Battlegrounds`, `Standard`, `Wild`, `History`, `Settings`) and
  stops, in both UIs. The wrapper `render.py` would then report the mandatory scenes as missing, so run the built exe or
  `dotnet run` directly (the same goes for `HEARTHPULSE_RENDER_ONLY` below).
  Scene filter: `HEARTHPULSE_RENDER_ONLY=<group>[,<group>]` renders only those groups: `brawl` (`main-brawl`, Avalonia
  also `main-brawl-compact`), `home` (`main-overview-full`, the whole home page, at `HEARTHPULSE_RENDER_WIDTH` when set: 420 = Avalonia compact), `pages` (`main-overview-<width>`, `main-battlegrounds-<width>`: the whole window at `HEARTHPULSE_RENDER_WIDTH`, 1080 high from 1600), `setup` (`main-setup-1`..`-5`, the setup wizard on each step, and `main-setup-1-off` with the logs off), `history` (`main-history`, `main-match-bg-table`: the first step of the newest Battlegrounds
  replay), `historylist` (`main-history` alone), `account` (`main-settings-account-<state>`, the account center and the connection light signed in as an in-memory sample; `HEARTHPULSE_RENDER_ACCOUNT=connected|offline|ended`, default connected), `ctmatch` (WPF only: `main-match-table`, the newest Constructed
  replay's table two thirds in), `overlay` (the Battlegrounds moments `overlay-pick/-trinket/-discover/-game/-bg-counters`,
  `overlay-constructed` with its previews, `overlay-layout`), `mulligan` (`overlay-mulligan-deck`, `-deck-near`: the mulligan panels with a collected deck's numbers), `picks` (`overlay-pick-2`, `-pick-4`, `overlay-trinket-2`, `-3`, `-4`: the hero and trinket badges on made-up offers at the data copy's `OverlayScale`, `overlay-trinket-late`: the trinket numbers' update delayed, and `overlay-picks-scale.txt` with the scale they were drawn at), `odds` (`overlay-odds`, `-odds-panel`, `-odds-estimated`, `-odds-estimated-panel`: the estimate mark, `-odds-full`, `-odds-full-panel`: the five-cell widest state, `overlay-bar-browser`: the bar with the tier list open), `cardpool` (the card pool scenes `overlay-card-pool*` of the table below and `main-settings-tracker`), `overlay-opacity` (WPF only: one frame,
  `overlay-opacity`, the Constructed panels and counters at the data copy's `OverlayOpacity`), `baseline` (the visual
  baselines' frames, below). New groups go into `RenderOnlyAsync`
  (`DevRender.Brawl.cs`, `OffscreenRenderer.Brawl.cs`). With a group, `HEARTHPULSE_RENDER_WIDTH=<px>` sets the main
  window's width (e.g. 1040, the minimum). Theme: `HEARTHPULSE_THEME=<preset>` (a name from `ThemePalette.Presets`),
  honoured only while rendering, in both UIs (without it the Avalonia renderer keeps the default colours).
- Data: main pages from `history.db`; overlay moments replayed from the **kept Hearthstone logs**
  (`<install>/Logs`, resolved from the configured install); counters fall back to a synthetic scene; meta and card pool come
  from the network/cache.

The script renders into a fresh temporary directory for each UI and publishes that run's PNGs, removing old PNGs
from the matching output subdirectory. Exit 0 requires a successful child process and seven nonempty mandatory
files: `main-overview`, `main-battlegrounds`, `main-standard`, `main-wild`, `main-history`, `main-settings`, and
`overlay-layout` (all `.png`). Missing log-dependent scenes are optional; missing baseline pages are failures.
Partial current output is kept for diagnosis. The manual commands above do not enforce this validation, so use a
fresh output folder and check these files yourself when bypassing the wrapper.

## Scenes (renderer code is authoritative)

`HEARTHPULSE_RENDER_ONLY=sourcecapture` renders `main-source-data` (Settings Data, the sending status under the game
records switch). Use a scratch profile with a synthetic HTTPS PoolEndpoint. Render mode never starts the source
delivery loop.
`HEARTHPULSE_RENDER_ONLY=mystats` checks the full-length statistics scenes separately.
`HEARTHPULSE_RENDER_ONLY=card` renders the card page (`docs/card-page.md`): `main-card`, `main-card-full`, `-loading`, `-error`, `-stale` (made-up states,
`CardPageScene`), `main-card-theme.txt` (the theme drawn and its colours), Avalonia also
`main-card-compact`, `-error-compact`; the card is `HEARTHPULSE_RENDER_CARD=<card id>`, else the first card of the loaded Standard meta's
mulligan numbers (skipped without a meta).
`HEARTHPULSE_RENDER_ONLY=collectionpage` (`DevRender.CollectionPage.cs` / `OffscreenRenderer.CollectionPage.cs`) renders the
Collection page (`docs/collection-page.md`): each tab on the data copy's own `collection.json` / `cosmetics.json`
(`main-collection-sets`, `-skins`, `-coins`, `-backs`, `-sets-golden`, `-sets-en`; Avalonia also `-sets-compact`,
`-backs-compact`) and the states on made-up data (`-none`, `-saved`, `-notread`, `-nocosmetics`, `-unsupported`,
`-readingoff`, `-nocards`). Frames of a player's own data go only to the task folder, never into the repository.
`HEARTHPULSE_RENDER_ONLY=datahealth` renders `main-datahealth-settings` (Settings, Data with the "Data status" card) and
`main-datahealth-arena` (the Arena page with the delay line) on the made-up summary of `Core/Api/DataHealthScene.cs`.
`HEARTHPULSE_RENDER_ONLY=library` (`DevRender.Library.cs` / `OffscreenRenderer.Library.cs`; writes to the history, so only
with `HEARTHPULSE_DATA`) adds the made-up Arena runs and the baseline games and renders the replay library:
`main-library`, `-onsite` (the first card with a file shown as uploaded, a made-up link), `-scrolled`, `-end`, `-1040`
(Avalonia also `-compact`), then the match header of the newest Constructed game with a file, `main-match-browser` and
`-browser-confirm` (the "View in browser" question; nothing is uploaded or opened), and `main-match-arena`.

| PNG | WPF | Avalonia | Source |
|---|---|---|---|
| `main-overview`, `-battlegrounds`, `-standard`, `-wild`, `-history`, `-settings` | 1260x840 window | same | `DevRender.cs` / `OffscreenRenderer.cs` page loop (Arena is not in the loop) |
| `main-settings-general`, `-overlay`, `-tracker`, `-account`, `-data`, `-about` | yes | yes | each Settings tab (`DevRender.Settings.cs` / `OffscreenRenderer.Settings.cs`) |
| `main-settings-counters` | yes | yes | Tracker tab, scrolled to the Battlegrounds counters |
| `main-constructed-tiers`, `main-constructed-top` | yes | - (no meta tabs) | `ShowConstructedTab` |
| `main-battlegrounds-heroes`, `-comps`, `-trinkets`, `-cards` | yes | yes | BG tier list tabs on the site's lists, scrolled to the tabs (`DevRender.BgMeta.cs` / `OffscreenRenderer.BgMeta.cs`); `HEARTHPULSE_RENDER_ONLY=bgtabs` renders only them, with their states: `main-bg-heroes`, `-heroes-loading` (a slice still loading), `-heroes-sorted`, `-heroes-top20`, `-heroes-duos`, `-comps`, `-trinkets`, `-trinkets-top50`, `-cards-t1`, `-cards-t4`, `-cards-spells`, `-heroes-stale`, `-comps-stale`, `-heroes-unavailable`, `-cards-unavailable`; Avalonia also the compact layout at 420: `-heroes-compact`, `-trinkets-compact`, `-cards-compact`, `-comps-compact`. The lists come from the data copy's `cache/api/bgsite-*.json` while those files are under 6 h old (else one live load) |
| `main-bg-own-ok`, `-old-copy`, `-small`, `-not-published`, `-rebuilding`, `-offline` | yes | yes | The block "Hearthpulse statistics (own games)" on the Heroes tab in each state (`DevRender.BgOwn.cs` / `OffscreenRenderer.BgOwn.cs`, states from `Core/Pool/OwnBgHeroesScenes.cs`); only with `HEARTHPULSE_RENDER_ONLY=bgown`, and `HEARTHPULSE_RENDER_BGOWN` set to `tests/Hearthpulse.Tests/Fixtures/Analytics` for the frames with rows; Avalonia also `-ok-compact` at 420. No pool is asked |
| `main-collection-top`, `-top-filter`, `-top-saved`, `-top-none`, `-missing`, `-side`, `-deck-full` | yes (also `-deck`) | yes (also `-top-compact`, `-deck-compact` at 420) | The collection in the top decks, a label's opened list of missing cards (`-missing`: the popup or flyout itself), the tier list's side card and the deck page (the render fails if the deck page with its collection block is not on screen) (`DevRender.Collection.cs` / `OffscreenRenderer.Collection.cs`) on the made-up collection and lists of `Core/Constructed/CollectionScene.cs`; only with `HEARTHPULSE_RENDER_ONLY=collection`. Never put a real `collection.json` into the data copy |
| `main-battlegrounds-full`, `-constructed-full`, `-arena-full` | page content only | full window, tall | `DevRender.MyStats.cs` / `OffscreenRenderer.MyStats.cs` |
| `main-battlegrounds-hover`, `main-constructed-hover` | if the chart has points | same | My Stats chart hover |
| `main-compact`, `main-*-compact` | - | 420 wide | compact layout |
| `overlay-pick`, `-trinket`, `-discover`, `-game`, `-bg-counters` | 1920x1080 | same | latest kept BG log; each skipped if never reached |
| `overlay-counters` + `-panel`, `-tooltip`, `-tooltip-counted`, `-open` | 2x crops | 1x crops + `*-full` | `CounterScene` (log or synthetic) |
| `overlay-mulligan`, `overlay-mulligan-4` | if a log has a 3- / 4-card mulligan | same | `MulliganReplay.LatestAsync(..., offered)` |
| `overlay-mulligan-deck`, `-deck-near` | yes | yes | `DevRender.Mulligan.cs` / `OffscreenRenderer.Mulligan.cs`: `MulliganReplay.DeckSample` (a fixed hand with a collected deck's numbers, no logs or network; the "By your deck / a close deck, N games, data from ..." line under the summary) |
| `overlay-odds`, `overlay-odds-panel`, `-estimated`, `-estimated-panel` | yes (panel 2x) | yes (panel 1x) | `CombatOddsScene.Synthetic` (made-up fight with a lethal risk; `-estimated`: `CombatOddsScene.Estimated`, the "≈" mark; `DevRender.CombatOdds.cs` / `OffscreenRenderer.CombatOdds.cs`) |
| `overlay-constructed`, `-deck-preview`, `-hand-preview` | yes | yes | `DevRender.Constructed.cs` / `OffscreenRenderer.Constructed.cs` |
| `overlay-card-pool`, `-picks`, `-scale08`, `-hover`, `-hover-16x10`, `-hover-4x3`, `-large`, `-runes`, `-sections`, `-small`, `-excavate`, `-nzoth`, `-browser`, `-browser-filtered` | yes (also `overlay-card-preview-related`) | yes | `App.CardPool.cs` / `Desktop/CardPoolRender.cs` (wiki pools of BE_298 from the API cache; the compact 300-wide panel beside the Discover options: `overlay-card-pool`, `-picks` = the pick hint with made-up totals, `DiscoverPicks.RenderSample`, `-scale08` at overlay scale 0.8; a hand card's pool with Ctrl held at the panel's own spot `card-pool`: `-hover` on 16:9 and the `VisualBaseline.Screens` shapes, `-small` = a pool listed whole, `CardPoolScenes.SmallPoolCard`; no board scenes, and no `-uncomputed` since such a pool shows no panel) |
| `overlay-layout` | always last | same | layout mode: every `MovablePanel` as a labelled placeholder at its default spot |

The total PNG count depends on the data and scenes in that commit; historical totals are not a success gate.
Report optional scenes skipped because no log reaches the moment, and investigate a scene that disappeared.

## Add a scene

Keep scenes inside the existing `try` so `Quit()`/exit still runs, and put them before `overlay-layout`. Fatal
startup/render errors and missing mandatory scenes must produce a nonzero exit. A scene that requires an
unavailable log moment may be explicitly reported as skipped; do not turn a mandatory scene failure into success.

- **Main page, WPF**: in `RenderUiAsync` (`src/Hearthpulse.App/DevRender.cs`) or a feature partial
  `DevRender.<Feature>.cs`: show the page/tab (`_main.ShowPage("Standard")`, or a new public helper on
  `MainWindow` like `ShowConstructedTab`), `await Task.Delay(2500)`, `_main.UpdateLayout()`,
  `Save(_main, Path.Combine(folder, "main-x.png"))`.
- **Main page, Avalonia**: `OffscreenRenderer.cs` `RenderAsync` or `OffscreenRenderer.<Feature>.cs`:
  `view.ShowPage(...)` (or a new public method on `MainView`), then `await Save(main, path)`.
- **Overlay, both**: build `LiveSnapshot.Empty with { Scene = "GAMEPLAY", HearthstoneRunning = true, InGame = true,
  GameType = ..., Constructed = view /* or Battlegrounds = ... */ }`, feed it to the overlay view model
  (`Update(snapshot, cards, settings, deck, null)`), then WPF `_overlay.RenderToFileAsync(path, 1920, 1080, hoverSlot)`,
  Avalonia `overlayView.Fit(1920, 1080)` + `Save(overlay, path)`. Prefer a real-log replay with a synthetic fallback
  (pattern: `CounterScene.FromLatestLog` / `CounterScene.Synthetic` in `src/Hearthpulse.Core/Tracking/Counters/CounterScene.cs`).
- Same PNG name in both UIs. Background colours come from theme resources, never `Color.FromRgb(...)` in render code.
- `DevRender.cs` and `OffscreenRenderer.cs` are the top merge-conflict files: add one call there and put the scene
  body in your own partial ([hearthpulse-parallel-work](../hearthpulse-parallel-work/SKILL.md)).

## Before / after

1. Render the base first: before editing, or from a clean worktree of the base commit
   (`git worktree add <short path> <base-sha>`, render there with the same data copy).
2. Render your branch into another folder with the **same** data copy (otherwise data differences show up as changes).
3. Compare:
   ```
   python -I .claude/skills/hearthpulse-visual-check/scripts/png_diff.py <before>/wpf <after>/wpf <out>/diff-wpf
   ```
   It lists new/removed scenes and the share of pixels whose maximum RGBA-channel difference exceeds 24 (small
   antialiasing noise is ignored), and writes `before | after | diff` sheets - Read the
   sheet to see the change. Unexpected changes outside your area are regressions until explained.
4. The same script on `<out>/wpf` vs `<out>/ava` is a quick **parity** check: expect differences from fonts,
   Avalonia's nav group markers and crop scale (WPF counter crops are 2x, `*-full` pages differ in size), not missing
   blocks.

## Visual baselines (CI)

`tools/visual/visual_check.py` keeps the baseline frames under watch: both UIs, Tavern / Frost / Shadow, main pages with
three made-up games and the overlay over two made-up moments on 16:9, 16:10 and 4:3 screens (render group `baseline`,
`VisualBaseline` in Core). It renders on the fixture data folder `tests/visual/data`, isolated
(`HEARTHPULSE_ISOLATED=1`: no network, no Hearthstone install, fixed text fonts), and compares with
`tests/visual/baselines/<ui>/<theme>-<scene>.webp` (lossless WebP): a channel moved by more than 8 on over 0.02 % of the
pixels fails; the app version under the sidebar is masked. CI job "Visual baselines" runs it on Windows, a job per
WPF theme (`--ui wpf --theme Tavern`, ...) and one for Avalonia, artifacts `visual-renders-<part>`; "Visual baselines
(both UIs)" joins them into `visual-renders` (renders and diff sheets) on any difference and on a manual run.

- Baselines are taken on the CI runner only. A wanted change: `gh workflow run ci.yml --ref <branch>`, download
  `visual-renders`, Read its `diff/` sheets, `python -B -I tools/visual/visual_check.py update <dir>`, commit with the
  change. Steps, the V3 round and local use: `tests/visual/README.md`.
- Screens in `PENDING` (being redesigned) are reported, never failed.

## What to look at in each PNG

Open the PNGs with the Read tool (they are images). For each one touched by the change:

- **Clipping**: text cut at the right edge or ellipsized where it should wrap; numbers like "1,204" or long card
  names - check both English and Russian UI text, plus card/hero/deck names in the card locale. Cyrillic text can
  be longer; also check tooltips leaving the 1920x1080 surface and the sidebar's status card (two lines at most).
- **Overlap**: panels on top of each other in `overlay-layout.png` - fine only between panels that never show in the
  same mode (e.g. `card-pool` vs `bgbar`); text over art.
- **Contrast**: parchment `Text` on wood surfaces; `Muted` for secondary text (`Faint` only for separators, glyphs and disabled things); semantic colours
  (`Positive`/`Negative`/`Tier*`/`Rarity*`) still distinct.
- **Spacing**: the same margins as neighbouring cards (24 between cards, page margin 40), aligned baselines in rows,
  segment bars right-aligned in headers, icons centred in their Scallop.
- **States**: empty state (no games, no data), loading, long lists, hover (`*-hover`), compact width (Avalonia
  `*-compact`, below 900 px).
- **Themes**: WPF with at least one non-default preset (`--theme Frost` or `Arcane`): gold accents must recolour,
  meaning colours must not. A hex literal in a view shows up here as a Tavern-gold spot in a blue theme.
- **Parity**: the same feature, the same information in both UIs (allowed gaps are listed in
  [hearthpulse-design](../hearthpulse-design/SKILL.md)).

Report what you rendered (UIs, theme, data copy), which PNGs you inspected, and what you could not render (e.g. no
kept log reaches the Discover moment) - do not claim a scene looks right if it was skipped.

## Script regression checks

`python -B -I -m unittest discover -s tools/skill_checks -v` uses Pillow synthetic images and mocked child
processes to check RGB/alpha differences, noise thresholds, current-scene validation, stale output, failed
processes, data/output isolation and the `Hearthpulse.LocalCheck` hold (mutex calls mocked). It does not launch either GUI or access the network. These checks do not
replace opening the actual rendered PNGs.
