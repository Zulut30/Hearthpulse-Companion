using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hearthpulse.UI.Behaviors;
using Hearthpulse.UI.Controls;
using Microsoft.Extensions.Time.Testing;

namespace Hearthpulse.Tests;

/// <summary>
/// The Avalonia adapters of the shared UI behaviors (docs/ui-behaviors.md) on a headless window: card rows move the
/// focus by arrows and by typed letters and tell a screen reader their place, and a flyout with
/// <c>PopupFocus.ReturnFocus</c> takes the focus in and gives it back. The markup check makes sure the views use them.
/// </summary>
[Collection(nameof(GlobalLanguage))]
public sealed class UiBehaviorsHeadlessTests
{
    [Fact]
    public void Card_rows_find_a_card_by_its_first_letters_and_say_their_place() => LocBindingTests.Headless.Run(() =>
    {
        var theme = PageRowsPanelAvaloniaTests.Theme();
        var clock = new FakeTimeProvider();
        RovingFocus.Clock = clock;
        (string Id, string Name)[] cards = [("A", "Fireball"), ("B", "Frostbolt"), ("C", "Arcane Intellect"), ("D", "Flamestrike")];
        var list = new ItemsControl { VerticalAlignment = VerticalAlignment.Top };
        foreach (var (id, name) in cards)
        {
            var row = new Button { Content = name };
            CardLink.SetCardId(row, id);
            CardLink.SetCardName(row, name);
            list.Items.Add(row);
        }

        var window = new Window { Width = 400, Height = 400, Content = list };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var rows = list.GetVisualDescendants().OfType<Button>().ToList();
            Assert.Equal(4, rows.Count);
            Assert.Equal(KeyboardNavigationMode.Once, KeyboardNavigation.GetTabNavigation(list));

            Button Focused()
            {
                Dispatcher.UIThread.RunJobs();
                return rows.Single(r => r.IsFocused);
            }

            void Type(string text)
            {
                var on = Focused();
                on.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text, Source = on });
            }

            // The focused row says where it is: "1 of 4", then "2 of 4" after Down.
            Assert.True(rows[0].Focus(NavigationMethod.Tab));
            Assert.Equal((1, 4), (AutomationProperties.GetPositionInSet(rows[0]), AutomationProperties.GetSizeOfSet(rows[0])));
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Assert.Same(rows[1], Focused());
            Assert.Equal(2, AutomationProperties.GetPositionInSet(rows[1]));

            // "a" goes to Arcane Intellect; "f" again steps through the F cards from there, wrapping to the first.
            Type("a");
            Assert.Same(rows[2], Focused());
            clock.Advance(Typeahead.DefaultTimeout);
            Type("f");
            Assert.Same(rows[3], Focused());
            Type("f");
            Assert.Same(rows[0], Focused());
            clock.Advance(Typeahead.DefaultTimeout);

            // "fl" is one query: from Fireball, "f" alone would go to Frostbolt, "fl" goes on to Flamestrike.
            Type("f");
            Assert.Same(rows[1], Focused());
            Type("l");
            Assert.Same(rows[3], Focused());

            // No card starts with "flz": the focus stays, the text is not taken.
            var z = new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "z", Source = rows[3] };
            rows[3].RaiseEvent(z);
            Assert.False(z.Handled);
            Assert.Same(rows[3], Focused());

            // A space is the button's own (it opens the card), never part of a query.
            var space = new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = " ", Source = rows[3] };
            rows[3].RaiseEvent(space);
            Assert.False(space.Handled);
        }
        finally
        {
            RovingFocus.Clock = TimeProvider.System;
            window.Close();
            Application.Current!.Styles.Remove(theme);
            Dispatcher.UIThread.RunJobs();
        }
    });

    [Fact]
    public void A_flyout_with_return_focus_takes_the_focus_in_and_gives_it_back_on_escape() => LocBindingTests.Headless.Run(() =>
    {
        var theme = PageRowsPanelAvaloniaTests.Theme();
        var details = new ContentControl { Content = "Two epics missing", Focusable = true };
        var flyout = new Flyout { Content = details };
        PopupFocus.SetReturnFocus(flyout, true);
        var label = new Button { Content = "Missing 2", Flyout = flyout };
        var other = new Button { Content = "Other" };
        var window = new Window { Width = 400, Height = 300, Content = new StackPanel { Children = { label, other } } };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(label.Focus(NavigationMethod.Tab));

            flyout.ShowAt(label);
            Dispatcher.UIThread.RunJobs();
            Assert.True(flyout.IsOpen);
            var popup = TopLevel.GetTopLevel(details)!;
            Assert.Same(details, popup.FocusManager!.GetFocusedElement());

            popup.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.False(flyout.IsOpen);
            Assert.Same(label, window.FocusManager!.GetFocusedElement());

            // Closed by code with the label hidden: the focus is not forced onto a control nobody sees.
            flyout.ShowAt(label);
            Dispatcher.UIThread.RunJobs();
            Assert.True(other.Focus(NavigationMethod.Tab));
            label.IsVisible = false;
            flyout.Hide();
            Dispatcher.UIThread.RunJobs();
            Assert.False(label.IsFocused);

            // Turned off, the flyout does not put the focus on its label as it closes.
            label.IsVisible = true;
            PopupFocus.SetReturnFocus(flyout, false);
            Assert.True(other.Focus(NavigationMethod.Tab));
            flyout.ShowAt(label);
            Dispatcher.UIThread.RunJobs();
            flyout.Hide();
            Dispatcher.UIThread.RunJobs();
            Assert.False(label.IsFocused);
        }
        finally
        {
            window.Close();
            Application.Current!.Styles.Remove(theme);
            Dispatcher.UIThread.RunJobs();
        }
    });

    [Fact]
    public void The_views_use_the_shared_behaviors_rather_than_their_own_copies()
    {
        static string Read(string path) => File.ReadAllText(Path.Combine(RepoFiles.Root, path.Replace('/', Path.DirectorySeparatorChar)));

        var avaloniaMeta = Read("src/Hearthpulse.UI/Main/Meta/ConstructedMetaView.axaml");
        Assert.Contains("b:PopupFocus.ReturnFocus=\"True\"", avaloniaMeta, StringComparison.Ordinal);
        Assert.DoesNotContain("OnDeckBuildOpened", avaloniaMeta, StringComparison.Ordinal);
        Assert.Contains("PopupFocus _deckBuildFocus", Read("src/Hearthpulse.App/Main/Meta/ConstructedMetaView.Collection.cs"), StringComparison.Ordinal);
        foreach (var ui in new[] { "Hearthpulse.App", "Hearthpulse.UI" })
        {
            Assert.Contains("RovingFocus.Move(", Read($"src/{ui}/Controls/CardLink.cs"), StringComparison.Ordinal);
            Assert.Contains("RovingFocus.Search(", Read($"src/{ui}/Controls/CardLink.cs"), StringComparison.Ordinal);
            Assert.Contains("FocusRecovery<", Read($"src/{ui}/Main/{(ui == "Hearthpulse.App" ? "MainWindow" : "MainView")}.CardFocus.cs"), StringComparison.Ordinal);
            Assert.Contains("ListNavigation.Target(", Read($"src/{ui}/Main/Match/ReplayLibraryView.{(ui == "Hearthpulse.App" ? "xaml" : "axaml")}.cs"), StringComparison.Ordinal);
        }
    }
}
