# The agent test

> Source: [vercel-labs/ui](https://github.com/vercel-labs/ui) `packages/core/skills/ui-sdk/references/agent-ready.md`
> ("Prove it with a test") and `testing.md` at `e9085ba`, Apache-2.0, © 2026 Vercel, Inc., see
> [THIRD-PARTY-NOTICES](../THIRD-PARTY-NOTICES.md). Hearthpulse changes: jsdom, Testing Library and `createAgent`
> replaced by automation peers in the WPF suite and Avalonia Headless; the framework differences measured on WPF for
> .NET 10 and Avalonia 12.1.4.

An agent test opens a page the way an agent meets it and proves the page can be driven by name: walk the automation
tree from the window, check every interactive control's name, find the controls the task names by the words a person
would use, act through their patterns, and assert what changed in the view model or the view. One test per page or
flow, in both UIs. The automation calls below were run against both frameworks on 2026-11-26; the page setup is the
one the existing tests use (`MainNavigationContractTests`, `AchievementsLayoutTests`).

## The walk and the name check

Collect the whole tree first, then read names: in WPF, `LabeledBy` names a control only once its label's peer exists,
and the walk is what creates it.

WPF, in `tests/Hearthpulse.Presentation.Tests` (Windows), inside `WpfThread.RunFresh`:

```csharp
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;

var model = new WpfMain(History(), new AppSettings(), TestApp.Data) { Background = BackgroundWork.Inline };
var window = OffScreen(new WpfWindow(model));      // MainNavigationContractTests' helper: Left -20000, not activated
window.Show();
window.ShowPage("Settings");
window.ShowSettingsTab("<tab>");
window.UpdateLayout();

var all = Walk(UIElementAutomationPeer.CreatePeerForElement(window)).ToList();
var controls = all.Where(Acting).ToList();
Assert.All(controls, p => Assert.True(Usable(p.GetName()), $"{p.GetClassName()} '{p.GetName()}' has no usable name"));

static IEnumerable<AutomationPeer> Walk(AutomationPeer peer)
{
    yield return peer;
    foreach (var child in peer.GetChildren() ?? [])
    {
        foreach (var nested in Walk(child))
        {
            yield return nested;
        }
    }
}

// What an agent presses, fills or picks. Every ItemsControl is a List too, so a ListBox is checked by class name.
static bool Acting(AutomationPeer p) => p.IsControlElement() && !p.IsOffscreen() && !InsidePart(p)
    && p.GetAutomationControlType() is AutomationControlType.Button or AutomationControlType.CheckBox
        or AutomationControlType.RadioButton or AutomationControlType.ComboBox or AutomationControlType.Edit
        or AutomationControlType.Slider or AutomationControlType.Hyperlink or AutomationControlType.ListItem
        or AutomationControlType.MenuItem or AutomationControlType.TabItem;

// What the frameworks hand out when a control has no real name: empty, a type name, a record printout, a glyph.
static bool Usable(string name) => name.Trim().Length > 0
    && !name.StartsWith("Avalonia.", StringComparison.Ordinal) && !name.Contains(" { ", StringComparison.Ordinal)
    && name.Any(c => !char.IsSurrogate(c) && c is not (>= '' and <= ''));

// A slider's track buttons and thumb and a scroll bar's arrows are template parts with no name of their own.
static bool InsidePart(AutomationPeer p)
{
    for (var up = p.GetParent(); up is not null; up = up.GetParent())
    {
        if (up.GetAutomationControlType() is AutomationControlType.Slider or AutomationControlType.ScrollBar)
        {
            return true;
        }
    }

    return false;
}
```

Avalonia, in `tests/Hearthpulse.Tests`, inside `LocBindingTests.Headless.Run` (as `AchievementsLayoutTests.Open`):

```csharp
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;

var theme = PageRowsPanelAvaloniaTests.Theme();
var model = new MainViewModel(HistoryStore.InMemory(), new AppSettings(), TestDataFolder.App)
{
    Background = BackgroundWork.Inline,
    SaveSettings = _ => { },
};
var view = new MainView { DataContext = model };
var window = new Window { Width = 1260, Height = 840, Content = view };
window.Resources["UiFont"] = FontFamily.Default;
window.Show();
try
{
    view.ShowPage("Settings");
    view.ShowSettingsTab("<tab>");
    PageRowsPanelAvaloniaTests.Settle(window);
    var controls = Walk(ControlAutomationPeer.CreatePeerForElement(window)).Where(Acting).ToList();
    // the same name check and steps as in WPF
}
finally
{
    window.Close();
    Application.Current!.Styles.Remove(theme);
}
```

Avalonia's `GetChildren()` returns a list (never null) and leaves invisible controls out; WPF keeps a collapsed control
with `IsOffscreen()` true and `IsControlElement()` false, hence the filter. A window at -20000 is not offscreen. The
names `Usable` turns away are the ones in the table of [SKILL.md](SKILL.md).

## Unique where it sits

```csharp
var clashes = controls.GroupBy(p => p.GetName()).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
Assert.True(clashes.Count == 0, "Two controls share a name: " + string.Join(", ", clashes));
```

Run it on the page as shown, not on all its modes at once. Rows with a fixed action name clash here; that is the
finding (checklist item 3), not a reason to filter rows out.

## Find by name and act through the pattern

List the controls the task names and find each by the words a person would use, through the same Loc key the view
binds (`Loc.T("<key>")`), so a renamed key fails the test rather than the agent.

| Act | WPF | Avalonia |
|---|---|---|
| Press | `((IInvokeProvider)p.GetPattern(PatternInterface.Invoke)).Invoke()`, then pump the dispatcher: the click arrives later | `p.GetProvider<IInvokeProvider>()!.Invoke()`: the click runs at once |
| Toggle | `((IToggleProvider)p.GetPattern(PatternInterface.Toggle)).Toggle()` | `p.GetProvider<IToggleProvider>()!.Toggle()` |
| Pick a tab, segment or item | `((ISelectionItemProvider)p.GetPattern(PatternInterface.SelectionItem)).Select()` | `p.GetProvider<ISelectionItemProvider>()!.Select()` |
| Open a section or a combo box | `IExpandCollapseProvider.Expand()` through `PatternInterface.ExpandCollapse` | `GetProvider<IExpandCollapseProvider>()!.Expand()` |
| Type | `IValueProvider.SetValue("…")` through `PatternInterface.Value` | `GetProvider<IValueProvider>()!.SetValue("…")` |
| Move a slider | `IRangeValueProvider.SetValue(…)` through `PatternInterface.RangeValue` | `GetProvider<IRangeValueProvider>()!.SetValue(…)` |

Pumping the WPF dispatcher after `Invoke()`:

```csharp
var frame = new DispatcherFrame();
Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
Dispatcher.PushFrame(frame);
```

Then assert the result where the player would see it: the setting in `model.Settings`, the page's text, the control's
new state read back through its pattern. After an act that opens or closes something, walk again: the tree changed.

Before trusting the test, break the page once: drop one `AutomationProperties.Name` or the confirmation, see the test
go red, restore it. A test that stays green proves nothing (`hearthpulse-test-gaps` holds the method).

## Hidden content and dangerous actions

- **Behind a tab or a "More" toggle.** Assert the inner control is absent from the walk, open the region through its
  opener's pattern, walk again, and find it by name.
- **A confirmation.** Press the trigger by name; walk again and find the confirm by its own name; assert the two
  names share no phrase; press Cancel and assert nothing changed; then press the confirm and assert the action on the
  test's scratch data (`TestDataFolder`). Never against real data.

## Where the tests live

A WPF agent test goes beside the page's contract tests in `tests/Hearthpulse.Presentation.Tests` (a page that opens a
real popup goes into the `RealPopups` collection, `hearthpulse-testing`); the Avalonia one beside the page's Headless
tests in `tests/Hearthpulse.Tests`. Name the test after what the agent does: `An_agent_finds_and_turns_on_every_counter`.
The ratchet `AgentReadyNameTests` keeps guarding the markup either way.
