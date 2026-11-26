# Report a problem (roadmap E1, E2, M2)

The player puts together a diagnostics archive, saves it where they choose and attaches it to a GitHub issue by hand.
The app sends nothing: no request leaves the machine on this path. Security problems go a different way:
[SECURITY.md](../SECURITY.md).

## Where

- **Settings → About**, card "Feedback": the button "Report a problem" (`AboutSettings.xaml|.axaml`).
- **The BETA flag** under the logo in the sidebar: a button with the same action; its tip says so (`Main.BetaTip`).

Both open the window "Report a problem" (`Main/Report/ReportProblemWindow.xaml|.axaml`, not modal) on a fresh
`ReportProblemViewModel` (`src/Hearthpulse.Presentation/Main/Report/`).

## The window

1. **In the archive**: one line per file with what it holds and its size before compression; the crash reports and the
   game logs are one line each. Below it, the count and the total.
2. **Attach game logs** (off every time the window opens). Under it: what they hold and that they name players, or why
   there are none (Hearthstone not found; no log session yet), in which case the switch is off and disabled.
3. **Save archive…**: the system's save dialog, `hearthpulse-diagnostics-<yyyy-MM-dd-HHmm>.zip`. The archive is built
   then, from the files as they are at that moment.
4. After the save: the file name and size, with "Open the form on GitHub" (`issues/new/choose` of
   `Zulut30/Hearthpulse-Companion`) and "Open folder" (the file selected, where the system can). Over 25 MB (GitHub's
   limit for an attachment) the line says to turn the game logs off and save again. A failed save says so in the
   error colour and removes the partial file.

## What the archive holds

`Core/Diagnostics/DiagnosticsBundle.cs`:

| Entry | What | Changed on the way |
|---|---|---|
| `system.txt` | Created (UTC), version, `wpf`/`avalonia`, OS and architecture, .NET, interface language and cultures, portable or not, every monitor (pixels, scale, primary), the main window's scale, the newest Hearthstone log session's name and whether it is included, notes on what was cut or left out | written for the archive |
| `hearthpulse.log`, `hearthpulse.old.log` | Hearthpulse's own log and the one before the last rotation | the last 8 MB from a line start; text cleaned (below) |
| `settings.json` | A copy of the settings | only fields this version reads; every value whose field name says key, token, secret, password, credential, cookie, auth, pin, signature… is `<removed>` at any depth (a true/false switch stays); text cleaned. Not valid JSON: left out, `system.txt` says why |
| `crash-reports/*.json` | Crash reports waiting to be sent ([crash-reports.md](crash-reports.md)): there are some only with automatic reports on or after a send that failed | already scrubbed when written; text cleaned again |
| `game-logs/*.log` | Only with "Attach game logs": the logs Hearthpulse asks the game for (`LogConfig`: Power, LoadingScreen, Decks, Arena, FullScreenFX, Achievements) from the newest `Logs/Hearthstone_*` session | the last 32 MB of each from a line start; **not cleaned**: they are what the parser read, BattleTags included, and the window says so |

Never in it: `account.dat` (the signed-in session), `history.db`, replays, the collection, achievements, combat
captures, the caches.

**Text cleaning** (`DiagnosticsBundle.Scrub`, lighter than `CrashReport.Scrub` because a log must still say which
folder and value broke): JSON Web Tokens and `Bearer …` values become `<token>`; `user:password@` in an address and
`?token=`, `key=`, `secret=`, `password=`, `code=`, `sig=` query values become `<removed>`; e-mails `<email>`; BattleTags
`<name>`; the user's home folder `%USERPROFILE%` (`~` off Windows; as written, with forward slashes and JSON-escaped,
only the whole folder); the account name as a word `<user>`.

## Signals

- `Diag`: `Report a problem: archive saved, <n> file(s), <bytes> bytes, game logs included|not included`; a failed save
  with its exception; a game log gone between the list and the save. No paths, no names.
- No upload, queue or retry: nothing to watch on a server.

## GitHub

- `.github/ISSUE_TEMPLATE/`: `bug.yml` (version, mode, OS, steps, result, impact S1-S3 in words, archive, screenshots),
  `wrong-data.yml` (version, mode, OS, where, shown, expected and how known, steps, archive), `idea.yml` (idea, why,
  modes, version), `config.yml` (no blank issues; the private vulnerability report as a contact link). Labels used:
  `bug`, `wrong data`, `beta`, `enhancement`; GitHub applies only labels that exist in the repository.
- `SECURITY.md`: private vulnerability reporting, scope. The link works once private vulnerability reporting is on in
  the repository's settings (Settings → Code security).

## Tests

`DiagnosticsBundleTests` (contents with and without game logs, no token, key or password in any entry, the user's
folder and name cut, cut logs, unreadable settings, `system.txt`, the game log lookup, secret field names);
`ReportProblemContractTests` (both UIs' model: the list, the switch and its note, save by path and by stream, a failed
save, the folder and the form); `ReportProblemWindowXamlTests` (WPF: the BETA flag, the Feedback card, the window's
bindings) and `LocBindingTests.Report_a_problem_card_and_window_bind` (Avalonia, ru and en). Render group `report`
(`hearthpulse-visual-check`).
