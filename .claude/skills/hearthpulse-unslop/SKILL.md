---
name: hearthpulse-unslop
description: Use as the last pass over your own Hearthpulse diff before handover or commit, and when asked to clean up a diff - removes generated-looking code in C#, XAML/AXAML and Loc strings without changing behaviour - comments that restate the code, XML docs that repeat the name, null checks the nullable types rule out, one-call helpers, vague names, Diag lines and commented-out markup left from debugging, comments longer than three lines - and says what stays. Triggers - "unslop", "почисти дифф", "убери лишние комментарии", "clean up the diff".
---

> Source: [vercel-labs/ui](https://github.com/vercel-labs/ui) `.agents/skills/ui-sdk-unslop/SKILL.md` at `e9085ba`,
> Apache-2.0, © 2026 Vercel, Inc., see [THIRD-PARTY-NOTICES](../THIRD-PARTY-NOTICES.md), licence text
> [LICENSE-Apache-2.0.txt](LICENSE-Apache-2.0.txt). Hearthpulse changes: TypeScript, JSDoc and `pnpm` replaced by C#
> with nullable reference types, XML docs, XAML/AXAML and Loc files; the project's conventions on what stays; the
> check through `tools/dev/check.ps1`.

# Unslop your diff

The scope is your own diff: `git diff $(git merge-base main HEAD)` plus uncommitted files, or the files the brief
names. Another session's files stay untouched. Behaviour never changes: the same tests pass before and after, and
nothing a reader outside the file depends on is renamed: public and internal members that XAML binds, an `x:Name` that
code-behind or a test finds, Loc keys, settings and JSON names, automation names, panel Ids, render scene names and
`Diag` events other code or a skill reads.

## What goes

Each pattern with what replaces it.

**A comment that says what the next line does.** Delete it; if the line is unclear without it, rename something.

```csharp
// Hide the panel when the game ends.
if (state.GameOver) ShowPanel = false;
```

**A comment that explains the design** (what a value holds, where it is kept, who reads it). The code says that. Keep
the one fact the code cannot show, in one line, or delete the comment.

```csharp
// Before
// The opacity of the overlay panels' backgrounds, kept in settings.json as OverlayOpacity, which OverlayViewModel
// reads on start and applies to the backdrop brushes; text stays solid. 1.0 is the default.
public double OverlayOpacity { get; set; } = 1.0;

// After
/// <summary>Backgrounds only: text, numbers and icons stay solid (owner, 2026-11-05).</summary>
public double OverlayOpacity { get; set; } = 1.0;
```

**A comment longer than three lines.** Cut it to the reason. An explanation that needs more goes into the doc the
feature has under `docs/`, a test's name, or the commit message.

**A comment that narrates the task or its history.** "Now we", "changed to", "fixed the bug where", "as requested",
"was X before". Delete it: git holds the history, the card holds the task.

**A section banner.** `// ---- Helpers ----`, `#region`, `<!-- ===== Header ===== -->`. Delete it; a file that needs
signposts splits into partials by feature (`MainViewModel.<Feature>.cs`), as the project already does.

**An XML doc that repeats the name.** `/// <summary>The settings.</summary>` on `Settings`. Say what the reader cannot
see: the unit, the default, the thread, when it is raised, what a change does. On a private member with nothing to
add, delete it.

**A hedge or a sales word** in a comment, a doc or a Loc string: "robust", "seamless", "simply", "just", "easily",
"ensure", "leverage", "note that", "просто", "легко", "всего лишь", «обратите внимание». Cut the word; if nothing is
left, cut the comment. The voice of interface text is `better-writing`'s.

**A null check the types rule out.** `if (items is null) return;` on `IReadOnlyList<Row> items`, `?? []` on a value
that is never null, `?.` on a non-nullable reference, `ArgumentNullException.ThrowIfNull` on an internal parameter, a
`try` around code that cannot throw. Delete it. Keep the check where null really arrives: JSON read by
System.Text.Json, `settings.json`, a value XAML or a style sets (dependency and styled properties), `DataContext` and
`CommandParameter`, framework callbacks, Power.log, game memory and network data.

**A cast or a `!` with no reason.** Make the type right, or say why in a line.

**A one-call helper, or a wrapper that only renames.** Inline it, unless its name explains what the call cannot or a
test calls it.

**A name that says nothing.** `data`, `result`, `item2`, `temp`, `flag`, `value2`, `OnClick2`, `Helper`, `Manager`,
`DoWork`. Name what it is: `deckRows`, `isPoolOpen`, `OnPoolButtonClick`.

**An option nobody passes**, a parameter with one value, a settings object for one setting. Delete it.

**Debug leftovers.** A `Diag.Info`, `Diag.Debug` or `Console.WriteLine` added to see a value while debugging (keep a
`Diag` line that records an event someone reads later, `hearthpulse-observability`); commented-out C#; commented-out
markup `<!-- <Button … /> -->`; an `x:Name` nothing finds (search code-behind, `tests/` and `ElementName=` first).

**Loc keys nobody uses.** A key added in the diff with no `{i:Loc Key}`, `Loc.T("Key")` or `Loc.F("Key", …)` left:
delete it from `ru` and `en` together. A tooltip key whose text repeats its label: drop the tooltip.

## What stays

- A comment that says why, in three lines at most: a framework quirk with the framework and its version, an owner
  decision with its date or task id, a WCAG criterion, a measured number with where it was measured, a link to the
  doc, ADR or test that pins the behaviour.
- `/// <summary>` on public members (a project convention, `hearthpulse-dev`), and on `AppSettings` properties: make it
  say more, do not delete it.
- A `Justification` on a suppression: the analyzer policy requires it (`.editorconfig`).
- A pointer to the twin in the other UI or to the feature's doc (`<!-- Avalonia twin: Main/CollectionResources.axaml -->`):
  the project navigates by them.
- Identifiers in a comment, in backticks or parentheses: "the trigger (`MovablePanel.OnTool`)".

## Steps

1. List what you will change, by file, before touching anything.
2. Make the changes.
3. Run `powershell -File tools/dev/check.ps1`: exit 0, 0 warnings, and the same cases pass as before the pass (compare
   the counts with the run before it).
4. Delete each comment left in the diff and reread the code. If it still reads right, leave it deleted.
5. Read the diff once more and revert any change that did not make the code easier to read.
6. A pattern found in three or more files: name the check that would catch it (an analyzer rule in `.editorconfig`, a
   markup test beside `AccessibleNameTests`, a test in `tools/skill_checks/`) in the handover; this pass does not add
   it.
