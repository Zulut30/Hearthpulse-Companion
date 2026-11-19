using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hearthpulse.Core.Constructed;
using Hearthpulse.Core.Data;
using Hearthpulse.UI.Infrastructure;
using Hearthpulse.UI.Main;
using Hearthpulse.UI.Main.Meta;

namespace Hearthpulse.Tests;

/// <summary>
/// Calm content changes in the Avalonia build (<see cref="ContentMotion"/>, the twin of the WPF ContentMotionTests): the end
/// states each change leaves (the headless platform turns motion off, so they come at once), and the meta tab during a
/// reload: the old rows dimmed in place, the busy cue lit in the card's head, no loading box, the card's height unchanged.
/// </summary>
[Collection(nameof(GlobalLanguage))]
public sealed class ContentMotionAvaloniaTests
{
    [Fact]
    public void Without_motion_every_change_takes_its_end_state_at_once() => LocBindingTests.Headless.Run(() =>
    {
        Assert.False(Motion.On);

        // Waiting content dims and comes back.
        var rows = new Border();
        ContentMotion.SetDim(rows, true);
        Assert.Equal(ContentMotion.DimOpacity, rows.Opacity);
        ContentMotion.SetDim(rows, false);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, rows.Opacity);

        // A cue keeps its room: shown and hidden by opacity alone.
        var cue = new Border();
        ContentMotion.SetLit(cue, false);
        Assert.Equal((0d, true), (cue.Opacity, cue.IsVisible));
        ContentMotion.SetLit(cue, true);
        Assert.Equal((1d, true), (cue.Opacity, cue.IsVisible));

        // A part shown in place of a visibility binding: whole and in place.
        var part = new Border { RenderTransform = new TranslateTransform(0, 6) };
        ContentMotion.SetShown(part, false);
        Assert.False(part.IsVisible);
        ContentMotion.SetShown(part, true);
        Assert.Equal((true, 1d, 0d), (part.IsVisible, part.Opacity, ((TranslateTransform)part.RenderTransform!).Y));

        // A loading section keeps its reserve, dimmed, and lets it go once loaded.
        var section = new Border();
        ContentMotion.SetReserve(section, 120);
        ContentMotion.SetPending(section, true);
        Assert.Equal((120d, ContentMotion.DimOpacity), (section.MinHeight, section.Opacity));
        ContentMotion.SetPending(section, false);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal((0d, 1d), (section.MinHeight, section.Opacity));

        // A replaced content and a revealed tab: nothing to hold without motion.
        var content = new Border();
        ContentMotion.SetRefresh(content, "a");
        ContentMotion.SetRefresh(content, "b");
        Assert.Equal(1, content.Opacity);
        var tab = new Border { Opacity = 0.2 };
        ContentMotion.Reveal(tab, 8);
        Assert.Equal(1, tab.Opacity);
    });

    [Fact]
    public void A_filter_change_dims_the_rows_in_place_and_the_tier_card_keeps_its_height() => Run((window, view, model) =>
    {
        view.ShowPage("Standard");
        model.SetConstructedMeta(MetaTierScene.States());
        view.ShowConstructedTab("Tiers");
        PageRowsPanelAvaloniaTests.Settle(window);
        var meta = view.GetVisualDescendants().OfType<ConstructedMetaView>().Single();
        var card = Named<Border>(meta, "TierCard");
        var lists = Named<StackPanel>(meta, "MetaLists");
        var cue = card.GetVisualDescendants().OfType<StackPanel>().Single(p => ContentMotion.GetLit(p) is not null);
        var height = card.Bounds.Height;
        Assert.Equal((1d, 0d), (lists.Opacity, cue.Opacity));

        // Another rank: the old rows stay, dimmed; the cue lights in the head; no loading box; nothing moves.
        model.SetMetaRank.Execute("diamond");
        PageRowsPanelAvaloniaTests.Settle(window);
        Assert.Equal(ContentMotion.DimOpacity, lists.Opacity);
        Assert.Equal(1, cue.Opacity);
        Assert.DoesNotContain(card.GetVisualDescendants().OfType<ContentControl>(), c => c.Classes.Contains("emptyState") && c.IsEffectivelyVisible);
        Assert.Equal(height, card.Bounds.Height);

        // The wait ends (here: the rank shown is asked for again): back to full, the cue out, still the same height.
        model.SetMetaRank.Execute("legend");
        PageRowsPanelAvaloniaTests.Settle(window);
        Assert.Equal((1d, 0d), (lists.Opacity, cue.Opacity));
        Assert.Equal(height, card.Bounds.Height);
    });

    [Fact]
    public void The_tiers_and_table_toggle_shows_one_list_at_a_time() => Run((window, view, model) =>
    {
        view.ShowPage("Standard");
        model.SetConstructedMeta(MetaTierScene.States());
        view.ShowConstructedTab("Tiers");
        PageRowsPanelAvaloniaTests.Settle(window);
        var meta = view.GetVisualDescendants().OfType<ConstructedMetaView>().Single();
        var (tiers, table) = (Named<ItemsControl>(meta, "MetaTierList"), Named<ItemsControl>(meta, "MetaTable"));
        Assert.Equal((true, false), (tiers.IsVisible, table.IsVisible));

        model.SetMetaView.Execute("Table");
        PageRowsPanelAvaloniaTests.Settle(window);
        Assert.Equal((false, true, 1d), (tiers.IsVisible, table.IsVisible, table.Opacity));

        model.SetMetaView.Execute("Tiers");
        PageRowsPanelAvaloniaTests.Settle(window);
        Assert.Equal((true, false, 1d), (tiers.IsVisible, table.IsVisible, tiers.Opacity));
    });

    private static T Named<T>(Control root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    private static void Run(Action<Window, MainView, MainViewModel> body) => LocBindingTests.Headless.Run(() =>
    {
        var theme = PageRowsPanelAvaloniaTests.Theme();
        var db = TestDataFolder.Scratch("hp-content-motion") + ".db";
        var model = new MainViewModel(new HistoryStore(db), PageRowsPanelAvaloniaTests.NoPool(), TestDataFolder.App);
        var view = new MainView { DataContext = model };
        var window = new Window { Width = 1280, Height = 800 };
        // The headless platform has no Inter: the app swaps UiFont at startup the same way (HearthpulseApp).
        window.Resources["UiFont"] = FontFamily.Default;
        window.Content = view;
        try
        {
            window.Show();
            PageRowsPanelAvaloniaTests.Settle(window);
            body(window, view, model);
        }
        finally
        {
            window.Close();
            Application.Current!.Styles.Remove(theme);
            Dispatcher.UIThread.RunJobs();
            TestDataFolder.TryDelete(db);
        }
    });
}
