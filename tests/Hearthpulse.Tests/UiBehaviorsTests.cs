using System.Globalization;
using Hearthpulse.UI.Behaviors;
using Microsoft.Extensions.Time.Testing;

namespace Hearthpulse.Tests;

/// <summary>
/// The framework-neutral UI behaviors (docs/ui-behaviors.md, ported from UI SDK): list navigation, typeahead, selection,
/// dismissal, popup focus and focus recovery, through their own interfaces with no window. The same sources compile
/// into the WPF app; its adapters are checked in the Windows suite.
/// </summary>
public sealed class UiBehaviorsTests
{
    // ---- ListNavigation ----

    [Fact]
    public void Arrows_move_one_item_and_stop_at_the_ends()
    {
        var list = ListNavigationOptions.Vertical;
        Assert.Equal(1, ListNavigation.Target(0, NavKey.Down, 3, list));
        Assert.Equal(2, ListNavigation.Target(2, NavKey.Down, 3, list));
        Assert.Equal(0, ListNavigation.Target(0, NavKey.Up, 3, list));
        Assert.Equal(1, ListNavigation.Target(2, NavKey.Up, 3, list));
    }

    [Fact]
    public void A_looping_list_wraps_around()
    {
        var loop = ListNavigationOptions.Vertical with { Loop = true };
        Assert.Equal(0, ListNavigation.Target(2, NavKey.Down, 3, loop));
        Assert.Equal(2, ListNavigation.Target(0, NavKey.Up, 3, loop));
    }

    [Fact]
    public void Keys_of_the_other_axis_are_not_the_lists()
    {
        Assert.Equal(-1, ListNavigation.Target(1, NavKey.Left, 3, ListNavigationOptions.Vertical));
        Assert.Equal(-1, ListNavigation.Target(1, NavKey.Down, 3, ListNavigationOptions.Horizontal));
        Assert.Equal(-1, ListNavigation.Target(1, NavKey.None, 3, ListNavigationOptions.Vertical));
        Assert.Equal(-1, ListNavigation.Target(0, NavKey.Down, 0, ListNavigationOptions.Vertical));
    }

    [Fact]
    public void Right_to_left_swaps_left_and_right()
    {
        var row = ListNavigationOptions.Horizontal;
        Assert.Equal(2, ListNavigation.Target(1, NavKey.Right, 3, row));
        Assert.Equal(0, ListNavigation.Target(1, NavKey.Right, 3, row with { RightToLeft = true }));
        Assert.Equal(2, ListNavigation.Target(1, NavKey.Left, 3, row with { RightToLeft = true }));
    }

    [Fact]
    public void Both_axes_move_like_radios()
    {
        var radios = ListNavigationOptions.Vertical with { Orientation = NavOrientation.Both };
        Assert.Equal(2, ListNavigation.Target(1, NavKey.Right, 3, radios));
        Assert.Equal(2, ListNavigation.Target(1, NavKey.Down, 3, radios));
        Assert.Equal(0, ListNavigation.Target(1, NavKey.Left, 3, radios));
        Assert.Equal(0, ListNavigation.Target(1, NavKey.Up, 3, radios));
    }

    [Fact]
    public void Home_and_end_go_to_the_edges_unless_the_list_leaves_them_to_the_page()
    {
        Assert.Equal(0, ListNavigation.Target(3, NavKey.Home, 5, ListNavigationOptions.Vertical));
        Assert.Equal(4, ListNavigation.Target(1, NavKey.End, 5, ListNavigationOptions.Vertical));
        var noEdges = ListNavigationOptions.Vertical with { HomeEnd = false };
        Assert.Equal(-1, ListNavigation.Target(3, NavKey.Home, 5, noEdges));
        Assert.Equal(-1, ListNavigation.Target(3, NavKey.End, 5, noEdges));
    }

    [Fact]
    public void Page_keys_move_by_the_page_and_never_wrap()
    {
        var paged = ListNavigationOptions.Vertical with { PageSize = 10, Loop = true };
        Assert.Equal(12, ListNavigation.Target(2, NavKey.PageDown, 30, paged));
        Assert.Equal(29, ListNavigation.Target(25, NavKey.PageDown, 30, paged));
        Assert.Equal(0, ListNavigation.Target(4, NavKey.PageUp, 30, paged));
        Assert.Equal(-1, ListNavigation.Target(4, NavKey.PageUp, 30, ListNavigationOptions.Vertical));
    }

    [Fact]
    public void Items_that_cannot_take_the_focus_are_skipped()
    {
        bool Usable(int i) => i != 1 && i != 3;
        var list = ListNavigationOptions.Vertical;
        Assert.Equal(2, ListNavigation.Target(0, NavKey.Down, 5, list, Usable));
        Assert.Equal(4, ListNavigation.Target(2, NavKey.Down, 5, list, Usable));
        Assert.Equal(0, ListNavigation.Target(2, NavKey.Up, 5, list, Usable));
        Assert.Equal(4, ListNavigation.Target(0, NavKey.End, 5, list, i => i != 5));
        // From an item that cannot take it (or none), the nearest usable one in the key's direction.
        Assert.Equal(2, ListNavigation.Target(1, NavKey.Down, 5, list, Usable));
        Assert.Equal(0, ListNavigation.Target(-1, NavKey.Down, 5, list, Usable));
        Assert.Equal(2, ListNavigation.Target(3, NavKey.Up, 5, list, Usable));
        // Nothing usable: the focus stays where it is.
        Assert.Equal(1, ListNavigation.Target(1, NavKey.Down, 5, list, _ => false));
    }

    // ---- Typeahead ----

    private static Typeahead NewTypeahead(FakeTimeProvider clock, string culture = "en-US") =>
        new(clock, culture: CultureInfo.GetCultureInfo(culture));

    [Fact]
    public void Typed_letters_find_the_next_item_that_starts_with_them()
    {
        var clock = new FakeTimeProvider();
        var typeahead = NewTypeahead(clock);
        string[] labels = ["Bloodfen Raptor", "Boulderfist Ogre", "Chillwind Yeti"];
        Assert.Equal(0, typeahead.Search("b", labels, -1));
        Assert.Equal(1, typeahead.Search("o", labels, 0));
        Assert.True(typeahead.Active);
        Assert.Equal(-1, typeahead.Search("x", labels, 1));
    }

    [Fact]
    public void A_repeated_letter_steps_through_the_items_that_start_with_it()
    {
        var typeahead = NewTypeahead(new FakeTimeProvider());
        string[] labels = ["Lion", "Owl", "Lynx", "Lark"];
        var at = typeahead.Search("l", labels, -1);
        Assert.Equal(0, at);
        at = typeahead.Search("l", labels, at);
        Assert.Equal(2, at);
        at = typeahead.Search("L", labels, at);
        Assert.Equal(3, at);
        Assert.Equal(0, typeahead.Search("l", labels, at));

        // Unless a label starts with the repeated letters.
        var llama = NewTypeahead(new FakeTimeProvider());
        string[] animals = ["Lion", "Llama"];
        Assert.Equal(0, llama.Search("l", animals, -1));
        Assert.Equal(1, llama.Search("l", animals, 0));
    }

    [Fact]
    public void A_quiet_moment_starts_a_new_query()
    {
        var clock = new FakeTimeProvider();
        var typeahead = NewTypeahead(clock);
        string[] labels = ["Bear", "Owl", "Boar"];
        Assert.Equal(0, typeahead.Search("b", labels, -1));
        clock.Advance(TimeSpan.FromMilliseconds(499));
        Assert.True(typeahead.Active);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.False(typeahead.Active);
        Assert.Equal(1, typeahead.Search("o", labels, 0));
        typeahead.Reset();
        Assert.False(typeahead.Active);
    }

    [Fact]
    public void Case_and_accents_fold_as_the_language_folds_them()
    {
        Assert.Equal(1, NewTypeahead(new FakeTimeProvider(), "fr-FR").Search("e", ["Arcane", "Éclair"], -1));
        Assert.Equal(1, NewTypeahead(new FakeTimeProvider(), "ru-RU").Search("е", ["Огонь", "Ёж"], -1));
        Assert.Equal(1, NewTypeahead(new FakeTimeProvider(), "ru-RU").Search("к", ["Наполнитель", "Ксавий"], -1));
    }

    [Fact]
    public void Space_only_continues_a_query_and_control_characters_are_not_text()
    {
        var typeahead = NewTypeahead(new FakeTimeProvider());
        string[] labels = ["Ice Block", "Ice Lance"];
        Assert.Equal(-1, typeahead.Search(" ", labels, -1));
        Assert.False(typeahead.Active);
        Assert.Equal(-1, typeahead.Search("\r", labels, -1));
        var at = -1;
        foreach (var typed in new[] { "i", "c", "e", " " })
        {
            at = typeahead.Search(typed, labels, at);
        }

        Assert.Equal(0, at);
        Assert.Equal(1, typeahead.Search("l", labels, at));
    }

    [Fact]
    public void Items_without_a_label_are_never_found()
    {
        var typeahead = NewTypeahead(new FakeTimeProvider());
        Assert.Equal(2, typeahead.Search("a", ["Apple", "", "Avocado"], 0));
        Assert.Equal(-1, NewTypeahead(new FakeTimeProvider()).Search("a", [], -1));
    }

    // ---- SelectionModel ----

    private static readonly string[] Keys = ["a", "b", "c", "d", "e"];

    [Fact]
    public void Single_selection_chooses_one_and_toggling_it_clears_it()
    {
        var selection = new SelectionModel<string>(() => Keys);
        selection.Choose("b");
        Assert.Equal(["b"], selection.Selected);
        selection.Toggle("c");
        Assert.Equal(["c"], selection.Selected);
        selection.Toggle("c");
        Assert.Empty(selection.Selected);
        selection.Extend("d");
        Assert.Equal(["d"], selection.Selected);
        selection.ToggleAll();
        Assert.Equal(["d"], selection.Selected);
    }

    [Fact]
    public void Shift_ranges_run_from_the_anchor_and_a_new_range_replaces_the_last()
    {
        var selection = new SelectionModel<string>(() => Keys) { Multiple = true };
        selection.Choose("a");
        selection.Toggle("c");
        Assert.Equal(["a", "c"], selection.Selected);
        Assert.Equal("c", selection.Anchor);
        selection.Extend("e");
        Assert.Equal(["a", "c", "d", "e"], selection.Selected);
        // Extending again from the same anchor replaces the range, not what was selected before it.
        selection.Extend("b");
        Assert.Equal(["a", "c", "b"], selection.Selected);
        selection.Extend("d", additive: false);
        Assert.Equal(["c", "d"], selection.Selected);
    }

    [Fact]
    public void Without_an_anchor_a_range_starts_at_the_focused_item()
    {
        var selection = new SelectionModel<string>(() => Keys) { Multiple = true };
        Assert.False(selection.HasAnchor);
        selection.Extend("d", from: "b");
        Assert.Equal(["b", "c", "d"], selection.Selected);
        selection.SetAnchor("e");
        selection.Extend("d");
        Assert.Equal(["b", "c", "d", "e"], selection.Selected);
        selection.ClearAnchor();
        Assert.False(selection.HasAnchor);
    }

    [Fact]
    public void Select_all_selects_every_selectable_item_then_none()
    {
        var selection = new SelectionModel<string>(() => Keys) { Multiple = true, Selectable = k => k != "c" };
        selection.ToggleAll();
        Assert.Equal(["a", "b", "d", "e"], selection.Selected);
        selection.ToggleAll();
        Assert.Empty(selection.Selected);
        selection.Choose("c");
        Assert.Empty(selection.Selected);
        selection.Choose("a");
        selection.Extend("e");
        Assert.Equal(["a", "b", "d", "e"], selection.Selected);
    }

    [Fact]
    public void A_refused_change_keeps_the_selection_and_the_anchor()
    {
        var selection = new SelectionModel<string>(() => Keys) { Multiple = true };
        var changes = 0;
        selection.Changed += (_, _) => changes++;
        selection.Choose("a");
        selection.Changing += (_, e) => e.Cancel = e.Next.Contains("e");
        selection.Toggle("e");
        Assert.Equal(["a"], selection.Selected);
        Assert.Equal("a", selection.Anchor);
        selection.Toggle("b");
        Assert.Equal(["a", "b"], selection.Selected);
        Assert.Equal(2, changes);

        // A selection set from outside raises nothing and starts ranges over.
        selection.Reset(["d"]);
        Assert.Equal(["d"], selection.Selected);
        Assert.False(selection.HasAnchor);
        Assert.Equal(2, changes);
        Assert.True(selection.IsSelected("d"));
    }

    // ---- Dismissal ----

    [Fact]
    public void A_layer_that_holds_work_refuses_dismissals_but_not_its_own_close()
    {
        var dismissal = new Dismissal();
        Assert.All(Enum.GetValues<DismissReason>(), reason => Assert.True(dismissal.Allows(reason)));

        var attempts = new List<DismissReason>();
        dismissal.Dismissible = false;
        dismissal.Attempted += (_, reason) => attempts.Add(reason);
        Assert.False(dismissal.Allows(DismissReason.EscapeKey));
        Assert.False(dismissal.Allows(DismissReason.OutsidePress));
        Assert.False(dismissal.Allows(DismissReason.FocusOut));
        Assert.False(dismissal.Allows(DismissReason.WindowBlur));
        Assert.True(dismissal.Allows(DismissReason.ClosePress));
        Assert.True(dismissal.Allows(DismissReason.Imperative));
        // Focus or the window leaving is not a try to dismiss it: no question for those.
        Assert.Equal([DismissReason.EscapeKey, DismissReason.OutsidePress], attempts);
    }

    // ---- PopupState ----

    private sealed class Element(string name)
    {
        public string Name { get; } = name;

        public bool Usable { get; set; } = true;

        public override string ToString() => Name;
    }

    [Fact]
    public void Opening_moves_the_focus_in_except_on_hover_or_from_the_start()
    {
        var popup = new PopupState<Element>();
        var trigger = new Element("label");
        Assert.Equal(PopupInitialFocus.Content, popup.Open(trigger, PopupOpener.Keyboard));
        Assert.True(popup.IsOpen);
        Assert.Same(trigger, popup.Trigger);
        Assert.Equal(PopupInitialFocus.Content, popup.Open(trigger, PopupOpener.Pointer));
        Assert.Equal(PopupInitialFocus.Popup, popup.Open(trigger, PopupOpener.Touch));
        Assert.Equal(PopupInitialFocus.None, popup.Open(trigger, PopupOpener.Hover));
        Assert.Equal(PopupInitialFocus.None, popup.Open(trigger, PopupOpener.Initial));
        Assert.Equal(PopupOpener.Initial, popup.Opener);
    }

    [Theory]
    [InlineData(DismissReason.EscapeKey, false, false, true)]
    [InlineData(DismissReason.ClosePress, false, false, true)]
    [InlineData(DismissReason.OutsidePress, false, false, true)]
    [InlineData(DismissReason.OutsidePress, true, true, false)]
    [InlineData(DismissReason.FocusOut, true, false, false)]
    [InlineData(DismissReason.WindowBlur, true, false, false)]
    [InlineData(DismissReason.Imperative, true, false, true)]
    [InlineData(DismissReason.Imperative, false, false, false)]
    public void Closing_returns_the_focus_to_the_trigger_unless_it_went_somewhere_chosen(DismissReason reason, bool focusInside, bool pressReachedFocusable, bool returns)
    {
        var popup = new PopupState<Element>();
        var trigger = new Element("label");
        var closed = new List<DismissReason>();
        popup.Closed += (_, why) => closed.Add(why);
        popup.Open(trigger, PopupOpener.Keyboard);
        var result = popup.Close(reason, focusInside, pressReachedFocusable);
        Assert.True(result.Closed);
        Assert.Equal(returns ? trigger : null, result.ReturnFocusTo);
        Assert.False(popup.IsOpen);
        Assert.Equal([reason], closed);
        // A closed popup keeps no element, and closing it again does nothing.
        Assert.Null(popup.Trigger);
        Assert.Equal(new PopupCloseResult<Element>(false, null), popup.Close(reason));
    }

    [Fact]
    public void A_refused_dismissal_keeps_the_popup_open()
    {
        var popup = new PopupState<Element>(new Dismissal { Dismissible = false });
        var opened = 0;
        popup.Opened += (_, _) => opened++;
        popup.Open(new Element("label"), PopupOpener.Pointer);
        popup.Open(new Element("other"), PopupOpener.Pointer);
        Assert.Equal(1, opened);
        Assert.False(popup.Close(DismissReason.EscapeKey).Closed);
        Assert.True(popup.IsOpen);
        Assert.Equal("other", popup.Trigger?.Name);
        Assert.True(popup.Close(DismissReason.ClosePress).Closed);
    }

    // ---- FocusRecovery ----

    private sealed class Tree : IFocusTree<Element>
    {
        public Dictionary<string, List<Element>> Lists { get; } = [];

        public bool IsUsable(Element element) => element.Usable;

        public IReadOnlyList<Element> ItemsOf(object collection) => Lists.TryGetValue((string)collection, out var items) ? items.Where(i => i.Usable).ToList() : [];

        public string? KeyOf(Element element) => element.Name.Split('#')[0];
    }

    [Fact]
    public void The_focus_returns_to_the_element_while_it_is_there()
    {
        var tree = new Tree();
        tree.Lists["deck"] = [new("fireball#1"), new("frostbolt#1"), new("arcane#1")];
        var recovery = new FocusRecovery<Element>(tree);
        var row = tree.Lists["deck"][1];
        recovery.Remember(row, "deck");
        Assert.True(recovery.HasOrigin);
        var origin = recovery.Take();
        Assert.False(recovery.HasOrigin);
        Assert.Null(recovery.Take());
        Assert.Same(row, recovery.Resolve(origin));
        Assert.Equal(new FocusOrigin<Element>(row, "frostbolt", "deck", 1), origin);
    }

    [Fact]
    public void A_rebuilt_list_gives_the_focus_to_the_same_card_else_a_fallback_item()
    {
        var tree = new Tree();
        tree.Lists["deck"] = [new("fireball#1"), new("frostbolt#1"), new("arcane#1")];
        var recovery = new FocusRecovery<Element>(tree);
        recovery.Remember(tree.Lists["deck"][1], "deck");
        var origin = recovery.Take();

        // The list is built again: the old row is gone, the same card has a new row.
        tree.Lists["deck"][1].Usable = false;
        tree.Lists["deck"] = [new("fireball#2"), new("frostbolt#2"), new("arcane#2")];
        Assert.Equal("frostbolt#2", recovery.Resolve(origin)?.Name);

        // The card left the list: its first row, or the row now at its place.
        tree.Lists["deck"] = [new("fireball#3"), new("arcane#3")];
        Assert.Equal("fireball#3", recovery.Resolve(origin)?.Name);
        Assert.Equal("arcane#3", recovery.Resolve(origin, RecoveryFallback.SameIndex)?.Name);
        tree.Lists["deck"] = [new("fireball#4")];
        Assert.Equal("fireball#4", recovery.Resolve(origin, RecoveryFallback.SameIndex)?.Name);

        // Nothing left (or no list at all): the caller's own fallback.
        tree.Lists["deck"] = [];
        Assert.Null(recovery.Resolve(origin));
        var loose = new Element("loose") { Usable = false };
        recovery.Remember(loose, collection: null);
        Assert.Null(recovery.Resolve(recovery.Take()));
        Assert.Null(recovery.Resolve(null));
    }
}
