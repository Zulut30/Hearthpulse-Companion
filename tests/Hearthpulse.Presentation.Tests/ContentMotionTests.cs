using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Hearthpulse.App.Infrastructure;
using Hearthpulse.App.Main.Meta;
using Hearthpulse.Core;
using Hearthpulse.Core.Constructed;
using Hearthpulse.Core.Data;
using AvaMain = Hearthpulse.UI.Main.MainViewModel;
using WpfMain = Hearthpulse.App.Main.MainViewModel;
using WpfWindow = Hearthpulse.App.Main.MainWindow;

namespace Hearthpulse.Presentation.Tests;

/// <summary>
/// Calm content changes in the WPF app (<see cref="ContentMotion"/>, the twin of the Avalonia ContentMotionAvaloniaTests):
/// the end states each change leaves with motion off, the meta tab during a reload (the old rows dimmed in place, the cue
/// lit, no loading box, the card's height unchanged), and the reload states of the shared view model in both builds.
/// </summary>
[Collection(MotionCollection.Name)]
public sealed class ContentMotionTests
{
    [Fact]
    public void Without_motion_every_change_takes_its_end_state_at_once() => WpfThread.RunFresh(() => Still(() =>
    {
        var rows = new Border();
        ContentMotion.SetDim(rows, true);
        Assert.Equal(ContentMotion.DimOpacity, rows.Opacity);
        ContentMotion.SetDim(rows, false);
        Pump();
        Assert.Equal(1, rows.Opacity);

        // A cue keeps its room: hidden, not collapsed, when off.
        var cue = new Border();
        ContentMotion.SetLit(cue, false);
        Assert.Equal((0d, Visibility.Hidden), (cue.Opacity, cue.Visibility));
        ContentMotion.SetLit(cue, true);
        Assert.Equal((1d, Visibility.Visible), (cue.Opacity, cue.Visibility));

        var part = new Border { RenderTransform = new TranslateTransform(0, 6) };
        ContentMotion.SetShown(part, false);
        Assert.Equal(Visibility.Collapsed, part.Visibility);
        ContentMotion.SetShown(part, true);
        Assert.Equal((Visibility.Visible, 1d, 0d), (part.Visibility, part.Opacity, ((TranslateTransform)part.RenderTransform).Y));

        var section = new Border();
        ContentMotion.SetReserve(section, 120);
        ContentMotion.SetPending(section, true);
        Assert.Equal((120d, ContentMotion.DimOpacity), (section.MinHeight, section.Opacity));
        ContentMotion.SetPending(section, false);
        Pump();
        Assert.Equal((0d, 1d), (section.MinHeight, section.Opacity));

        var content = new Border();
        ContentMotion.SetRefresh(content, "a");
        ContentMotion.SetRefresh(content, "b");
        Assert.Equal(1, content.Opacity);
    }));

    [Fact]
    public void A_filter_change_dims_the_rows_in_place_and_the_tier_card_keeps_its_height() => WpfThread.RunThemed(() => Still(() =>
    {
        var model = new WpfMain(History(), new AppSettings(), TestApp.Data);
        var window = Open(model);
        try
        {
            window.ShowPage("Standard");
            model.SetConstructedMeta(MetaTierScene.States());
            window.ShowConstructedTab("Tiers");
            Pump();
            var view = Logical<ConstructedMetaView>(window).Single();
            var card = (Border)view.FindName("TierCard");
            var lists = (StackPanel)view.FindName("MetaLists");
            var cue = Logical<StackPanel>(card).Single(p => ContentMotion.GetLit(p) is not null);
            var height = card.ActualHeight;
            Assert.Equal((1d, 0d), (lists.Opacity, cue.Opacity));

            // Another rank: the old rows stay, dimmed; the cue lights in the head; no loading box; nothing moves.
            model.SetMetaRank.Execute("diamond");
            Pump();
            Assert.Equal(ContentMotion.DimOpacity, lists.Opacity);
            Assert.Equal((1d, Visibility.Visible), (cue.Opacity, cue.Visibility));
            Assert.DoesNotContain(Logical<ContentControl>(card), c => c.GetType() == typeof(ContentControl) && c.IsVisible);
            Assert.Equal(height, card.ActualHeight);

            // The wait ends (the rank shown is asked for again): back to full, the cue out, the same height.
            model.SetMetaRank.Execute("legend");
            Pump();
            Assert.Equal((1d, 0d, Visibility.Hidden), (lists.Opacity, cue.Opacity, cue.Visibility));
            Assert.Equal(height, card.ActualHeight);
        }
        finally
        {
            window.CloseForReal();
        }
    }));

    [Fact]
    public void Both_builds_wait_for_new_stats_the_same_way() => WpfThread.Run(() =>
    {
        var wpf = new WpfMain(History(), new AppSettings(), TestApp.Data);
        var ava = new AvaMain(History(), new AppSettings(), TestApp.Data);
        foreach (dynamic m in new object[] { wpf, ava })
        {
            m.ShowConstructedFormat("FT_STANDARD");
            m.SetConstructedMeta(MetaTierScene.States());
            m.SetMetaRank.Execute("diamond");
        }

        Assert.Equal(States(wpf), States(ava));
        Assert.Equal((false, true, true), (wpf.MetaLoading, wpf.MetaRefreshing, wpf.MetaDecksRefreshing));

        wpf.SetMetaRank.Execute("legend");
        ava.SetMetaRank.Execute("legend");
        Assert.Equal(States(wpf), States(ava));
        Assert.False(wpf.MetaRefreshing);

        static (bool, bool, bool, bool, bool, bool, bool) States(dynamic m) =>
            ((bool)m.MetaLoading, (bool)m.MetaRefreshing, (bool)m.MetaDecksRefreshing, (bool)m.MetaMatchupsRefreshing,
                (bool)m.MetaMatchupsLoading, (bool)m.MulliganLoading, (bool)m.MulliganEmptyShown);
    });

    /// <summary>Runs <paramref name="body"/> with the app's "reduce motion" on: every change at once.</summary>
    private static void Still(Action body)
    {
        var saved = Motion.Reduced;
        try
        {
            Motion.Reduced = true;
            body();
        }
        finally
        {
            Motion.Reduced = saved;
        }
    }

    private static HistoryStore History() => new(Path.Combine(AppPaths.Root, Guid.NewGuid().ToString("N") + ".db"));

    private static WpfWindow Open(WpfMain model)
    {
        var window = new WpfWindow(model)
        {
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, Width = 1260, Height = 840,
            ShowActivated = false, ShowInTaskbar = false,
        };
        window.Show();
        Pump();
        return window;
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static IEnumerable<T> Logical<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T found)
            {
                yield return found;
            }

            foreach (var nested in Logical<T>(child))
            {
                yield return nested;
            }
        }
    }
}
