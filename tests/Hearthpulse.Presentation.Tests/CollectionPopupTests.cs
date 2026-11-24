using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Hearthpulse.Core;
using Hearthpulse.Core.Localization;
using Hearthpulse.Tests;
using static Hearthpulse.Presentation.Tests.CollectionContractTests;
using WpfMain = Hearthpulse.App.Main.MainViewModel;
using WpfMeta = Hearthpulse.App.Main.Meta.ConstructedMetaView;

namespace Hearthpulse.Presentation.Tests;

/// <summary>
/// The WPF popup of a label's missing cards, opened for real: its content, focus in it and back on Escape, closing on
/// Enter, Space, a new collection and a new list, the pricing lines; and that a closed popup, and one whose window was
/// closed under it, reaches neither the view nor the window through anything it holds.
/// <para>
/// In the collection that runs last (<see cref="RealPopups"/>): a real popup opened in the process changes what a
/// later closed window of the same process is kept by, and <c>MainWindowLifetimeTests</c> must not come after it.
/// That is also why the closed window is checked here by what the popup reaches, not by a garbage collection.
/// </para>
/// </summary>
[Collection(RealPopups.Name)]
public sealed class CollectionPopupTests
{
    [Fact]
    public void The_WPF_popup_takes_focus_gives_it_back_on_escape_and_keeps_nothing_of_its_row() => WpfThread.RunFresh(() =>
    {
        var model = TopDecksModel();
        model.SetCards(CollectionFixture.Cards);
        var window = OffScreen(model);
        try
        {
            TopDecks(window, model);
            var view = Descendants(window).OfType<WpfMeta>().Single();
            ContentControl Details() => Descendants(view.DeckBuildPopup!.Child).OfType<ContentControl>().First(c => c.Content is Hearthpulse.App.Main.DeckBuildView);

            // Never opened: there is no popup at all, in the view's tree or beside it.
            Assert.Null(view.DeckBuildPopup);
            Assert.False(view.DeckBuildOpen);
            Assert.Empty(Descendants(view).OfType<Popup>());
            Assert.Null(view.FindName("DeckBuildPopup"));

            // Opened as a press of the label opens it: the first label with missing cards.
            var content = view.OpenFirstDeckBuild();
            Pump();
            Assert.NotNull(content);
            var popup = Assert.IsType<Popup>(view.DeckBuildPopup);
            Assert.True(popup.IsOpen);
            // It stands by itself: no parent and no placement target to inherit this window from.
            Assert.Null(popup.Parent);
            Assert.Null(popup.PlacementTarget);
            var opener = Descendants(view).OfType<Button>().First(b => b.IsVisible && b.Content is Hearthpulse.App.Main.DeckBuildView { HasMissing: true });
            var build = Assert.IsType<Hearthpulse.App.Main.DeckBuildView>(opener.Content);
            var details = Details();
            Assert.Same(build, details.Content);
            Assert.Equal("NeedsDust", build.Kind);
            Assert.Equal(build.Details, AutomationProperties.GetName(details));
            var shown = Descendants(popup.Child).OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text).ToList();
            Assert.Contains(build.Tip, shown);
            Assert.Contains(CollectionFixture.LongName, shown);
            Assert.All(build.Missing, m => Assert.Contains(m.Price, shown));
            Assert.Contains(build.Total, shown);
            Assert.Contains(Loc.T("Collection.Missing.Title"), shown);

            // Focus is in the list: it is the focused element of its own focus scope (the popup).
            Assert.True(details.Focusable);
            Assert.Same(details, FocusManager.GetFocusedElement(FocusManager.GetFocusScope(details)));

            // Escape closes it, and focus is back on the label it was opened from.
            details.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(details)!, 0, Key.Escape) { RoutedEvent = UIElement.KeyDownEvent });
            Pump();
            Assert.False(popup.IsOpen);
            Assert.Same(opener, FocusManager.GetFocusedElement(FocusManager.GetFocusScope(opener)));

            // Closed, it holds neither the label it was placed at nor the row's cards.
            Assert.Null(view.DeckBuildPopup);
            Assert.Equal((null, null), (popup.Child, details.Content));

            // Enter and Space close it from the keyboard as well.
            foreach (var key in new[] { Key.Enter, Key.Space })
            {
                Assert.NotNull(view.OpenFirstDeckBuild());
                Pump();
                Assert.True(view.DeckBuildOpen);
                details = Details();
                details.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(details)!, 0, key) { RoutedEvent = UIElement.KeyDownEvent });
                Pump();
                Assert.False(view.DeckBuildOpen);
            }

            // Open, and a new collection replaces the rows: the popup with the old numbers does not stay.
            Assert.NotNull(view.OpenFirstDeckBuild());
            Pump();
            Assert.True(view.DeckBuildOpen);
            model.SetCollection(CollectionFixture.Collection(dust: 5000));
            Pump();
            Assert.False(view.DeckBuildOpen);
            Assert.Null(view.DeckBuildPopup);

            // The same when the list is replaced by a filter while it is open.
            Assert.NotNull(view.OpenFirstDeckBuild());
            Pump();
            Assert.True(view.DeckBuildOpen);
            model.SetMetaDeckSort.Execute("Dust");
            Pump();
            Assert.False(view.DeckBuildOpen);

            // A pricing under way and a failed one have their lines here too; neither shows without cause.
            Assert.False(((FrameworkElement)view.FindName("MetaPricing")).IsVisible);
            Assert.False(((FrameworkElement)view.FindName("DeckCostsFailedLine")).IsVisible);
            var real = model.PriceDecksWith;
            model.PriceDecksWith = (_, _, _) => throw new InvalidOperationException("synthetic pricing failure");
            model.SetCollection(CollectionFixture.Collection(dust: 6000));
            Pump();
            var failed = (FrameworkElement)view.FindName("DeckCostsFailedLine");
            Assert.True(failed.IsVisible);
            var retry = Descendants(failed).OfType<Button>().Single();
            Assert.Equal(Loc.T("Collection.PricingRetry"), retry.Content);
            Assert.True(retry.ActualHeight >= 24);
            model.PriceDecksWith = real;
            Assert.Same(model.RetryDeckCosts, retry.Command);
            retry.Command.Execute(null);
            Pump();
            Assert.False(failed.IsVisible);
            Assert.False(model.DeckCostsFailed);
        }
        finally
        {
            window.CloseForReal();
            Pump();
        }
    });

    // ------------------------------------------------------------------------------------------ what a closed popup holds

    /// <summary>
    /// The defect found by a process dump of a closed main window that stayed alive (docs/collection.md): UI Automation
    /// held the popup root's peer, and the popup root led to the window, first as a child of the view, then, as a
    /// parentless popup placed at a label, through the values it inherited from that label. A popup of this view
    /// reaches neither once it is closed; the same walk does find the window from a stock popup placed at a label,
    /// which is what makes the walk a check.
    /// </summary>
    [Theory]
    [InlineData("closed by the view")]
    [InlineData("closed by Escape")]
    [InlineData("closed by a new collection")]
    [InlineData("left open when the window closed")]
    public void A_closed_popup_reaches_neither_its_view_nor_the_window(string how) => WpfThread.RunFresh(() =>
    {
        var model = TopDecksModel();
        model.SetCards(CollectionFixture.Cards);
        var window = OffScreen(model);
        var closed = false;
        try
        {
            TopDecks(window, model);
            var view = Descendants(window).OfType<WpfMeta>().Single();
            var label = Descendants(view).OfType<Button>().First(b => b.IsVisible && b.Content is Hearthpulse.App.Main.DeckBuildView { HasMissing: true });

            // The walk sees the defect: a stock popup that was placed at the label still leads to the window when it is
            // closed, emptied and no longer placed there (the values it inherited stay with it).
            var stock = new Popup { PlacementTarget = label, Child = new TextBlock { Text = "placed at a label" } };
            stock.IsOpen = true;
            Pump();
            stock.IsOpen = false;
            stock.PlacementTarget = null;
            stock.Child = null;
            Pump();
            Assert.NotNull(PathTo(stock, window));

            var handlers = (Handlers(label, FrameworkElement.UnloadedEvent), Handlers(view, FrameworkElement.UnloadedEvent));
            Assert.NotNull(view.OpenFirstDeckBuild());
            Pump();
            Assert.Equal((handlers.Item1 + 1, handlers.Item2 + 1), (Handlers(label, FrameworkElement.UnloadedEvent), Handlers(view, FrameworkElement.UnloadedEvent)));
            var popup = Assert.IsType<Popup>(view.DeckBuildPopup);
            var plate = Assert.IsType<Border>(popup.Child);
            var details = Assert.IsType<ContentControl>(plate.Child);
            Assert.True(popup.IsOpen);

            // Open, it is tied to its view only by what closes it (the key handler, the Closed handler).
            Assert.NotNull(PathTo(popup, view));

            switch (how)
            {
                case "closed by the view":
                    view.CloseDeckBuild();
                    break;
                case "closed by Escape":
                    details.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(details)!, 0, Key.Escape) { RoutedEvent = UIElement.KeyDownEvent });
                    break;
                case "closed by a new collection":
                    model.SetCollection(CollectionFixture.Collection(dust: 5000));
                    break;
                default:
                    window.CloseForReal();
                    closed = true;
                    break;
            }

            Pump();
            Assert.False(popup.IsOpen);
            Assert.False(view.DeckBuildOpen);
            Assert.Null(view.DeckBuildPopup);

            // Closed: nothing it holds, nor its content, leads to the view, the window, the label or the row's cards.
            Assert.Equal((null, null, null), (popup.Child, popup.PlacementTarget, popup.Parent));
            foreach (var kept in new object[] { popup, plate, details })
            {
                foreach (var held in new object[] { window, view, label })
                {
                    var path = PathTo(kept, held);
                    Assert.True(path is null, path);
                }
            }

            // And the view keeps no handler on the label or on itself for a popup that is gone.
            Assert.Equal(handlers, (Handlers(label, FrameworkElement.UnloadedEvent), Handlers(view, FrameworkElement.UnloadedEvent)));
        }
        finally
        {
            if (!closed)
            {
                window.CloseForReal();
            }

            Pump();
        }
    });

    /// <summary>How many handlers of a routed event an element has (its own, not class handlers).</summary>
    private static int Handlers(UIElement element, RoutedEvent routed)
    {
        var store = typeof(UIElement).GetProperty("EventHandlersStore", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(element);
        var handlers = store?.GetType().GetMethod("GetRoutedEventHandlers", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(store, [routed]) as RoutedEventHandlerInfo[];
        return handlers?.Length ?? 0;
    }

    /// <summary>
    /// The way from <paramref name="root"/> to <paramref name="target"/> through instance fields, as a garbage collector
    /// would follow them (the types on the way, for the failure message); null when there is none. Not followed: weak
    /// references (they keep nothing alive), types, and what the whole process shares and every element leads to: the
    /// dispatcher, the application, the localization service (a text bound with <c>{i:Loc}</c> names it, and it knows
    /// every listener to a language change). Those are roots of their own, not something this object holds.
    /// </summary>
    private static string? PathTo(object root, object target)
    {
        var from = new Dictionary<object, (object Parent, string Field)?>(ReferenceEqualityComparer.Instance) { [root] = null };
        var pending = new Queue<object>();
        pending.Enqueue(root);
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (ReferenceEquals(current, target))
            {
                var steps = new List<string>();
                for (object? at = current; at is not null;)
                {
                    var step = from[at];
                    steps.Add(step is { } s ? $"{s.Field} -> {at.GetType().Name}" : at.GetType().Name);
                    at = step?.Parent;
                }

                steps.Reverse();
                return string.Join(" | ", steps);
            }

            Assert.True(from.Count < 3_000_000, "the walk left the object asked about");
            var type = current.GetType();
            if (current is string or Type or MemberInfo or Dispatcher or WeakReference or Pointer or Loc or Application || type.IsPrimitive || type.IsEnum
                || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(WeakReference<>)))
            {
                continue;
            }

            void Follow(object value, string field)
            {
                if (from.TryAdd(value, (current, field)))
                {
                    pending.Enqueue(value);
                }
            }

            if (current is Array array)
            {
                var element = type.GetElementType()!;
                if (element.IsPrimitive || element.IsEnum || element == typeof(string) || element.IsPointer)
                {
                    continue;
                }

                foreach (var item in array)
                {
                    if (item is not null)
                    {
                        Follow(item, "[]");
                    }
                }

                continue;
            }

            for (var declaring = type; declaring is not null; declaring = declaring.BaseType)
            {
                foreach (var field in declaring.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    var fieldType = field.FieldType;
                    if (fieldType.IsPrimitive || fieldType.IsEnum || fieldType.IsPointer || fieldType.IsByRefLike || fieldType == typeof(string)
                        || fieldType == typeof(IntPtr) || fieldType == typeof(UIntPtr))
                    {
                        continue;
                    }

                    if (field.GetValue(current) is { } value)
                    {
                        Follow(value, field.Name);
                    }
                }
            }
        }

        return null;
    }
}
