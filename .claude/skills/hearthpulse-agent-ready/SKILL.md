---
name: hearthpulse-agent-ready
description: Use when a Hearthpulse view must be driven by name by an agent, Narrator or a UI Automation test - every interactive control named and unique in its view, row actions that carry their row, items and tabs by visible text, decoration out of the tree, state through Toggle/ExpandCollapse/SelectionItem/Value patterns, dangerous actions behind a confirmation named apart from its trigger, secrets unreadable through the tree, content behind tabs and expanders reachable - in WPF and Avalonia; when writing the agent test that walks a page's automation tree; when AgentReadyNameTests fails. Triggers - "agent-ready", "агент найдёт по имени", "дерево автоматизации", "UI Automation", "имена контролов", "агентский тест".
---

> Source: [vercel-labs/ui](https://github.com/vercel-labs/ui) `packages/core/skills/ui-sdk/references/agent-ready.md`
> and `testing.md` at `e9085ba`, Apache-2.0, © 2026 Vercel, Inc., see
> [THIRD-PARTY-NOTICES](../THIRD-PARTY-NOTICES.md), licence text [LICENSE-Apache-2.0.txt](LICENSE-Apache-2.0.txt).
> Hearthpulse changes: the checklist retold for UI Automation in WPF and Avalonia in place of the `createAgent` bridge
> and the `data-agent-*` attributes; naming rules measured on both frameworks; the agent test rewritten for automation
> peers; the markup ratchet `AgentReadyNameTests`.

# Agent-ready views

A view is agent-ready when an agent drives it by name: it reads the automation tree, finds a control by its name and
role, reads the control's state from a pattern and acts through that pattern, never by pixels. The agents are our QA
sessions, Narrator and Accessibility Insights, and UI Automation tests. Both UIs build an automation peer per control,
and on Windows both serve one UI Automation tree: that tree is the bridge.

This skill owns what an agent finds a control by and acts through, and the tests that prove it. Keyboard paths, focus
and announcements belong to `better-accessibility`; the words of a name belong to `better-writing`. The target is the
main window and its dialogs. The overlay is click-through over the game and no agent flow runs there, but its few
clickable elements still get names.

## How each framework names a control

Measured on WPF for .NET 10 and Avalonia 12.1.4 (2026-11-26); `AgentReadyNameTests` reads markup by these rules:

| The control has | WPF name | Avalonia name |
|---|---|---|
| `AutomationProperties.Name` | that | that |
| `AutomationProperties.LabeledBy` | the label's name, once the label's peer exists (a walk from the window creates it) | the label's name |
| a text, `{i:Loc}` or a bound string as `Content`, or one `TextBlock` | the text | the text |
| a `StackPanel`, `Grid` or `Border` as content (icon and label) | empty | the panel's type name, `Avalonia.Controls.StackPanel` |
| no content, its `ControlTemplate` draws the label | empty | empty |
| a data object as content (`Content="{Binding}"`) | `ToString()`: `GameRow { When = … }` | the text of the template's root `TextBlock`, else `ToString()` |
| an MDI glyph as content | the private-use character | the private-use character |
| `ToolTip` | help text, not a name | help text, not a name |
| `Label Target=…`, Avalonia `PlaceholderText` | not a name | not a name |
| `Expander Header="…"` | the header | empty: needs `AutomationProperties.Name` |

The project's common button, an icon and a label in a `StackPanel`, therefore has no usable name in either UI until it
gets `AutomationProperties.Name="{i:Loc Key}"` with its visible label's key (WCAG 2.5.3: the name starts with the
words the player sees).

Rows of an `ItemsControl`: WPF wraps each in a `DataItem` named by the item's `ToString()`, and the row's controls are
its children; Avalonia lists the row's controls flat under the list. A fixed name in a row is ambiguous in both.

## The checklist

1. **Every interactive control has a name.** Buttons, toggles, check boxes, radio buttons, menu items, links, text
   boxes, combo boxes, sliders, lists. Text content names it; everything else in the table takes
   `AutomationProperties.Name="{i:Loc Key}"`, the same key in both UIs. An input takes
   `AutomationProperties.LabeledBy="{Binding ElementName=<its label>}"` or `AutomationProperties.Name` with the
   label's key. A name is words a person would say: never a glyph, a type name or a record printout.
2. **Unique in its view.** Two controls with one name on one page, in one popup or in one card set leave the agent
   guessing. The name carries what tells them apart, after the visible words: "Сбросить настройки оверлея", "Все
   столбцы: муллиган". Two twins that never show together (the Constructed and Battlegrounds tab bars of the match
   page) are no clash at run time; the markup ratchet still counts them.
3. **Row actions carry their row.** A control in an item template is built once per row, so a fixed name repeats in
   every row. Give each control of a row a verb of its own plus the row: "Удалить колоду Охотник 2", "Открыть
   Охотник 2"; none of them is the bare row title. Build it once in the shared view model, a row property such as
   `DeleteName => Loc.F("Deck.DeleteName", Name)` bound as `AutomationProperties.Name="{Binding DeleteName}"` in
   both views; `CardLink.CardName` ("Open the card page of …") is the existing example.
4. **Items and tabs by visible text.** A tab or segment (`RadioButton` with the `Tab`, `Segment` or `NavItem` style), a
   menu item and a list item are named by the text they show. A list of data objects gets a name per item: an item
   template whose root is a `TextBlock` (Avalonia), `AutomationProperties.Name` bound to the row's title on the item
   container (WPF `ItemContainerStyle`), or a `ToString()` that returns that title. Segments repeated per row
   ("Авто / Всегда / Выкл." of each counter) follow item 3.
5. **Decoration stays out of the tree.** An icon beside a label, a divider, a background picture. Avalonia:
   `AutomationProperties.AccessibilityView="Raw"` takes it out of the control view. WPF has no such property: a glyph
   `TextBlock` in content is a Text element named by a private-use character, while a `TextBlock` the control's
   template draws is not a control element. Put decoration in the template or a style, and name the control.
6. **State through patterns.** An agent reads and changes state through Toggle (`CheckBox`, `ToggleButton`, the
   `Switch` style, `ToggleSwitch`), SelectionItem (tabs, segments, list items), ExpandCollapse (`Expander`,
   `ComboBox`), Value (`TextBox`), RangeValue (`Slider`) and Invoke (`Button`, `Hyperlink`). State shown only by a
   colour or a glyph has no pattern: a plain `Button` with a chevron that opens a section becomes a `ToggleButton`,
   a row that looks selected becomes a `RadioButton` or a list item.
7. **Dangerous actions ask first, and the confirm is named apart from its trigger.** Deleting games, history or data
   on the server, signing out, resetting settings. The trigger opens a confirmation (an inline row under it or a
   dialog), and the confirming button shares no phrase with the trigger: "Сбросить все настройки" asks, "Сбросить
   всё" confirms. A confirm called "Удалить" under a trigger "Удалить игру" is found by a search for the trigger's
   first word. Cancel takes `IsCancel` / Escape. A QA agent never confirms on the owner's data; a test
   confirms on the scratch data folder.
8. **Secrets stay unreadable.** A token, password or code the player types goes into a WPF `PasswordBox`: UI
   Automation marks it as a password and reads its value as empty. Avalonia's `TextBox PasswordChar="•"` hands the
   plain text to its Value pattern (measured on 12.1.4), so a secret never sits in an Avalonia text box: show it
   masked in a `TextBlock` with paste, replace and remove actions. The app holds no such field today (sign-in is a
   device code); this rule is for the first one.
9. **Hidden content has a named way in.** A closed tab, a collapsed expander or "Ещё" section and a page not yet
   opened (`MainWindowLazyPagesTests`) are out of the agent's view until opened: WPF leaves a collapsed expander's
   content out and marks a collapsed control offscreen and not a control element, Avalonia drops every invisible
   control. Each hidden region has an opener with a name that says what is behind
   it and the pattern to open it (SelectionItem of the tab, Toggle or ExpandCollapse of the toggle). A setting is
   also reachable through the settings search: a new setting goes into its index.

## Tests

| Test | What it holds | Suite |
|---|---|---|
| `AgentReadyNameTests` | Ratchet over both UIs' markup by the rules above: controls with no name, a fixed name twice in one view, popup or template, a fixed name in a row template. Counts per UI may not grow | `tests/Hearthpulse.Tests` (Linux too) |
| `AccessibleNameTests` | An icon-only button has `AutomationProperties.Name`; card rows are named buttons | `tests/Hearthpulse.Tests` |
| `AboutSettingsXamlTests`, `SetupWizardLayoutTests` | A heading's name and level read through its peer | Windows suite, Headless |
| `ShareCoverLayoutTests` | A click as a person makes it: `Select()` through a radio button's SelectionItem | Windows suite |
| `UiBehaviorsHeadlessTests`, `MainNavigationContractTests` | Roving focus, `PositionInSet`, keyboard paths | both |

Missing: a run-time walk of a page in both UIs, with names as the frameworks compute them, uniqueness among what is on
screen and an action through a pattern. Write that agent test for a new page or a reworked flow: [agent-test.md](agent-test.md).

When `AgentReadyNameTests` fails, its message lists every finding as `file:line control` with the reason; yours is in
the file you touched, and the checklist item says the fix. A change that lowers a count lowers `Baseline` in the same
commit. The full list without a failure: `dotnet test tests/Hearthpulse.Tests --no-build --filter AgentReadyNameTests
--logger "console;verbosity=detailed"`.

## Before you finish

| Pattern | Fix |
|---|---|
| `<Button>` or `<RadioButton>` holding a `StackPanel`, `Grid` or `Border`, with no `AutomationProperties.Name` | The visible label's Loc key as `AutomationProperties.Name`, both UIs |
| `Content="{Binding}"` on a button or list item | A name bound to the row's title |
| A `ToggleButton` or `Button` whose template draws its label | `AutomationProperties.Name` with that label's key |
| `TextBox`, `ComboBox`, `Slider` or `ListBox` with no name | `LabeledBy` to its visible label, or `AutomationProperties.Name` |
| `Label Target`, `PlaceholderText` or `ToolTip` as the only name | `LabeledBy` or `AutomationProperties.Name` |
| A fixed `{i:Loc}` name inside an `ItemTemplate` | A row property built with `Loc.F(…, row)` |
| One name twice on a page or in a popup | Add what tells them apart, after the visible words |
| A confirm whose words are part of its trigger's name | A verb of its own |
| An Avalonia `Expander` with only a `Header` | `AutomationProperties.Name` |
| A secret in an Avalonia `TextBox` | A masked `TextBlock` and actions |
| A fix that lowered a count | `Baseline` in `AgentReadyNameTests` lowered in the same commit |
