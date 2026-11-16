# Mutation testing

Stryker.NET (`dotnet-stryker` 5.0.0, a local tool in `dotnet-tools.json`; Apache-2.0 per the package's LICENSE, a dev
tool that is not part of any build, listed in `THIRD-PARTY-NOTICES.txt`) changes one operator, condition, string or
call at a time in the code below and runs the tests that cover it: a change no test notices (a **survivor**) marks a
behaviour nothing checks. It runs by hand only, in CI: Actions → Mutation tests → Run workflow
(`.github/workflows/mutation.yml`, Windows, since Stryker builds the whole solution with the WPF app). A full run takes
hours, so it never runs on the owner's PC; locally, at most one small target at Idle with the game closed:

```bash
dotnet tool restore --add-source https://api.nuget.org/v3/index.json
dotnet dotnet-stryker -f tests/mutation/likely-deck.json -c 2
```

## Targets

| Target (`tests/mutation/<name>.json`) | Code | Why |
|---|---|---|
| `replay-share` | `Replay/ReplayShare.cs` | The copy of a replay that leaves the machine |
| `replay-sanitize` | `Replay/ReplayImport.cs` (report counts `Sanitize` and the `Clean*` methods) | The import filter: what a foreign file may bring in |
| `crash-report` | `Diagnostics/CrashReport.cs` | What a crash report sends: the personal-data scrubber |
| `likely-deck` | `Constructed/LikelyDeck.cs` | What the overlay tells the player about the opponent's deck |
| `combat-simulator` | `Battlegrounds/CombatSimulator.cs`, `CombatDamage.cs` | The combat odds; runs only the simulator's own 405 tests (all 2,500 took more than two hours) |

## Runs (2026-11-05)

Score = killed / (killed + survived + not covered); mutants that do not compile or sit in ignored code are left out.
"Before" is the first run; "after" the re-run once the tests below were in (CI run 42072514527 for share, sanitize and
the crash report; a local run for `likely-deck`).

| Target | Before: killed / survived / not covered | Score | After: killed / survived / not covered | Score |
|---|---|---|---|---|
| `replay-share` | 13 / 5 / 11 | 45% | 17 / 5 / 7 | 59% |
| `replay-sanitize` (filter methods) | 183 / 67 / 26 | 66% | 201 / 52 / 23 | 73% |
| `crash-report` | 28 / 17 / 3 | 58% | 36 / 10 / 2 | 75% |
| `likely-deck` | 23 / 10 / 30 | 37% | 33 / 2 / 28 | 52% |
| `combat-simulator`: `CombatSimulator.cs` | 169 / 51 / 0 | 77% | not re-run | |
| `combat-simulator`: `CombatDamage.cs` | 54 / 7 / 0 | 89% | not re-run | |

`combat-simulator` was not run again: of its 58 survivors, one is closed by a test (the time budget), the cancellation
ones are covered once `HardeningTests` is in its filter, and the rest are equivalent or the seed (below).
What survives after the re-run and is equivalent: `crash-report`'s GUID and time formats, `ReplaceLineEndings(" ")`
on a one-line message, and the empty-frame filter that `RemoveEmptyEntries` already covers (each guards the other);
`replay-share`'s folder name length and the clean-up count; `likely-deck`'s `> 0` inside a sum.

`likely-deck`'s "not covered" are all in `Sample`, the render scene's fixed list. `replay-share`'s are the error paths
of writing and cleaning up (`IOException`), `replay-sanitize`'s the summary rebuilt for an old Battlegrounds file.

## Closed (tests added, each red against its mutant)

| Survivor | Risk | Test |
|---|---|---|
| CrashReport: the OS user name matched case-sensitively | A user name in another case reaches the report | `CrashReportTests.The_os_user_name_goes_only_as_a_whole_word_and_keeps_the_frame` |
| CrashReport: the marks `<email>`, `<ip>`, `<number>`, the length limit, five exceptions, empty frames | A report that is unreadable or longer than it should be | `CrashReportTests.Emails_addresses_and_long_numbers_leave_their_own_marks`, `A_message_is_cut_only_past_its_limit`, `A_report_keeps_five_exceptions_and_no_empty_frames` |
| ReplayShare: `All` → `Any` in the mode check | A mode like `../Duos` in the shared file's name | `ReplayShareTests.A_mode_that_is_not_one_word_of_letters_names_no_file` |
| ReplayImport: a bad card id on a board, in the lobby or the final board; a round result outside Win/Loss/Tie | A foreign file brings paths or text into the library | `ReplayImportTests.A_bad_minion_hero_or_result_in_battlegrounds_data_refuses_the_file` |
| ReplayImport: `GameType`, `PlayerClass`, `Result` kept unchecked | The same, through the summary | `ReplayImportTests.Summary_words_that_are_not_words_are_dropped` |
| LikelyDeck: names and costs of rows, unknown cards, copies of a card and its reprint summed, zero plays, the core fallback | A wrong list shown to the player | `OpponentDeckTests.Rows_name_and_cost_the_cards_and_played_copies_add_up_across_reprints`, `A_list_of_unknown_cards_falls_back_to_the_core_and_without_one_there_is_none` |

`likely-deck` re-run after its tests: 10 survivors → 2 (an equivalent `> 0` → `>= 0` inside a sum, and the name order of
two cards of one cost).

## Worth closing next

- **replay-sanitize, limits:** the counts at the edge (`> 200` rounds, `> 8` lobby heroes, `> 14` minions, `> 16` list
  items, `> 1000` shop cards, the event limits) survive as `>=`: no test sits exactly on a limit. One theory per limit,
  at the limit and one past it.
- **replay-sanitize, constructed summary:** `RebuildConstructedSummary` (result words, turns `(turns + 1) / 2`, the
  opponent's class) is checked only through other fields; a test on an old constructed file's summary would cover it.
- **replay-sanitize, Battlegrounds steps:** whether the opponent stays out of view outside combat (`fight`,
  `opponentInView` start values) survives: a step list where the first step is not a fight.
- **replay-share, clean-up:** a copy exactly a day old, and the count of deleted folders.
- Equivalent or harmless, leave: the GUID format and the time format in `CrashReport` (a server field), the `Sample`
  scene in `LikelyDeck`, `Safe(list) ?? []` on lists that are never null by then.

## Combat simulator

Run 42067756832, the simulator's own tests only. Most of the 58 survivors change nothing the player sees:

- **Equivalent:** the tie rate computed first (`(done - wins - losses) / done`) is replaced in every branch below it,
  and the branches for "all wins", "all losses" and "all ties" give what the general one would; the line and the
  blocks can go in a later clean-up.
- **The seed** (`Seed`, FNV-1a over the boards): a field left out only changes which random numbers a board gets.
- **Cancellation:** `HardeningTests.CancelledSimulationStopsEarly` checks it, but that class was not in the target's
  test filter in this run; it is now.
- **Closed:** a spent time budget stops exactly at `MinRuns`
  (`EngineTests.A_spent_time_budget_stops_exactly_at_the_minimum_runs`).
- **Worth closing next:** the damage tally when one side has no minions (the outcome sign passed to `DamageTally`),
  `LethalDealt` when the opponent's health is unknown, and the keyword flags `Poisonous`, `Venomous`, `Stealth`,
  `Windfury`/`MegaWindfury` built from a minion's properties (`CombatSimulator.cs` lines 70-72: no test reads
  `Keywords` back), and `CombatDamage`'s percentile and equality code (lines 81-95).

## Tests that wait (2026-11-16)

Not mutation testing, but the same question from the other side: which tests can fail with the code unchanged. Three
CI and full local runs in two days failed on one pattern, a real wait plus a free thread of the pool, and a fourth
(CI run 43902233433, `ArenaPageKindTests`) while this was written. The rule that came out of it is in the skill
`hearthpulse-testing`, "Waits: signals and injected time, a timeout only as a backstop".

### Inventory

Both suites at `1f4b2df1`, by what the test waits on (sites, not tests: one helper serves several tests).

| Kind | Sites | Verdict |
|---|---|---|
| A `TaskCompletionSource` or event, with a bound of 10 s to 1 min (`WaitAsync(TimeSpan)`, `Wait(TimeSpan)`) | 154 in 40 files | Fine when the signal is raised by the test or a fake. A candidate when the work behind the signal crosses the thread pool (a view model's `Task.Run`): five such tests failed below |
| Polling with a pause (`UpdateTests.WaitUntil` and `Events.WaitAsync`, 20 ms steps for 15 s; `BrawlPageTests.Until`; `TwitchPublisherTests.SettleAsync`) | 23 uses in 4 files, 2 helpers more | Candidates, not reproduced |
| `SpinWait.SpinUntil` on a background read (`OverlayLayoutLogicTests`, `CollectionLayoutTests`, `CollectionPrivacyTests`, `GameProcessTests`) | 9 in 4 files | Candidates, not reproduced; `GameProcessTests` waits for the OS process list |
| A UI thread pumped until a flag or 30 s (`CardPageLayoutTests`, `BgSiteTabsTests.Settle`, `PumpUntil` in the presentation suite) | 17 in 5 files | Candidates, not reproduced |
| A real pause (`Task.Delay`, `Thread.Sleep` of 5 ms to 2 s), six of them a wait for "nothing happens" (`ApiLoadTests`, `UpdateTests` twice, `DeltaUpdateTests`, `UpdateLoopTests`, `MetaSegmentLookTests`) | 11 in 9 files | Cannot fail by timing, but pass when the thing they rule out is merely late |
| A fake that never answers through `Task.Delay(Timeout.Infinite, token)` | 7 in 7 files | Fine: ended by the test's own cancellation |
| Work started on the pool by the test (`Task.Run`, `StartNew`, `ContinueWith` on the default scheduler) | 7 in 6 files | One fixed (`LocBindingTests.Navigation`); the rest not reproduced |
| Wall-clock thresholds (`Stopwatch`) | 14 in 9 files | `EngineTimingTests` and `BattlegroundsReplayTimingTests` are `Category=Timing` and run alone; `LongReplayTests` has its own collection; `UpdateInstallScriptTests` asserts 1.5-50 s on a real script; the rest only print or bound a pump |

### How the pool was kept busy

Stopping every pool worker outright is not a test of this: timers stop with it, no timeout fires, and the test host
itself crawls (a 10 s wait "passed" after 3 minutes). What failed in CI is a pool whose workers are busy while timers
still fire, so the regime is that one: the test process on 4 cores (`start /affinity`), a CPU-bound `Parallel.For`
with one worker more than cores running for the whole run. A probe item queued to the pool then waited up to 12.5 s,
and one posted to the xunit context 84 ms. The same probe over CI's first half (classes A-H with `Sim.*`, coverage
collector, 4 cores) measured 6951 ms while `MultithreadedFiveThousandSimulationsTests` ran its `Parallel.For`, and
609 ms once that test used threads of its own. The hook that did this was temporary and is not in the repository.

### Results

Candidate classes under the busy pool, 5 runs each: 50 classes of the core suite (1239 test results a run) and 9 of
the presentation suite (74). Then each failing test alone, before and after its fix.

| Test | Busy pool, before | Busy pool, after | Normal, before | Normal, after |
|---|---|---|---|---|
| `ViewModelContractTests.Deck_matchup_filter_survives_a_refresh_but_resets_for_another_family` (Avalonia) | 41/52 | 0/50 | 0/200 | 0/200 |
| the same (WPF) | 4/52 | 0/50 | 0/200 | 0/200 |
| `ArenaRewardsPageTests.A_save_shows_the_rewards_and_gives_the_focus_back_to_the_chip` | 3/52 | 0/52 | 0/202 | 0/202 |
| `ArenaRewardsPageTests.The_editor_survives_a_rebuild_keeps_a_failed_save_and_closes_with_its_run` | 2/52 | 0/52 | 0/202 | 0/202 |
| `ArenaPageKindTests.Each_tab_shows_its_own_arena_and_underground_has_no_community_class_numbers` | 0/52; 1/10 beside the simulations test under coverage; CI | 0/52 | 0/202 | 0/202 |
| `LocBindingTests.A_game_opened_off_the_history_page_stays_open` | 1/5 in its class's run, 0/52 alone | 0/52 | 0/202 | 0/202 |
| `LocBindingTests.A_dashboard_let_go_with_its_data_context_is_collected_while_its_view_model_lives_on` | 20/50 (8/8 with the fix's wait left out, 0/8 with it) | 0/50 | 0/200 | 0/200 |

Every other test of the candidate classes: 0 failures in 5 runs. That is "not reproduced in this regime", not "cannot
fail": the polling helpers and the `SpinUntil` waits still depend on the pool, with bounds of 15 s to 1 min.

### What was changed

- The first six rows waited at most 10 s for a page a view model builds with `Task.Run`. `MainViewModel.Background`
  (`BackgroundWork`: the pool in the app, the calling thread in a test) now carries the history reads of a refresh, the
  My Stats builds, the deck page build and the match page load; these tests assert right after the call.
- The last row collected a closed view while three card art loads its pages had asked for were still queued on the
  pool, each holding its element. The test waits for the loads' own end (`CardArt.LoadsInFlightAsync`) first.
- `MultithreadedFiveThousandSimulationsTests` runs on threads of its own instead of `Parallel.For` (a commit of its
  own, the CI fix).