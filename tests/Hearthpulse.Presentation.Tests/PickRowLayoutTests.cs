using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Hearthpulse.App.Infrastructure;
using Hearthpulse.App.Overlay;
using Hearthpulse.Core;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Tracking;
using WpfOverlay = Hearthpulse.App.Overlay.OverlayViewModel;
using AvaOverlay = Hearthpulse.UI.Overlay.OverlayViewModel;
using WpfRow = Hearthpulse.App.Overlay.PickRowLayout;
using AvaRow = Hearthpulse.UI.Overlay.PickRowLayout;
using WpfMain = Hearthpulse.App.Main.MainViewModel;

namespace Hearthpulse.Presentation.Tests;

/// <summary>
/// The hero and trinket badge rows on a 1920 x 1080 game at overlay scales 100%, 140% and 160%: every badge whole on
/// the screen and over its own card of the game (the step is the game's, whatever the scale), no leaderboard label
/// under a trinket badge, and the size slider reaching the scale the settings allow. The shared numbers in both
/// compiled overlays, and the WPF overlay window laid out for real.
/// </summary>
public sealed class PickRowLayoutTests
{
    private static readonly DateTime Fetched = new(2026, 11, 15, 17, 1, 0, DateTimeKind.Utc);

    public static TheoryData<bool, double, int> Cases()
    {
        var data = new TheoryData<bool, double, int>();
        foreach (var wpf in new[] { true, false })
        {
            foreach (var scale in new[] { 1.0, 1.4, 1.6 })
            {
                foreach (var count in new[] { 2, 3, 4 })
                {
                    data.Add(wpf, scale, count);
                }
            }
        }

        return data;
    }

    /// <summary>The shared rule, as each compiled overlay has it: slots one game step apart, badges inside the screen and inside their step.</summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void Badges_keep_the_games_step_and_stay_on_the_screen_at_any_overlay_scale(bool wpf, double scale, int count)
    {
        var settings = new AppSettings { OverlayScale = scale };
        dynamic model = wpf ? new WpfOverlay() : new AvaOverlay();
        model.Update(LiveSnapshot.Empty with { InGame = true, HearthstoneRunning = true, Battlegrounds = HeroPick(count) }, CardDb.Empty, settings, null, null);

        foreach (var (pitch, card, shift, slot, zoom, row, badges) in new[]
                 {
                     (340.0, 232.0, 7.0, (double)model.HeroPickSlot, (double)model.HeroPickZoom, (double)model.HeroRowWidth,
                         wpf ? WpfRow.Heroes.OnScreen(count, 1920, 1080, scale) : AvaRow.Heroes.OnScreen(count, 1920, 1080, scale)),
                     (277.0, 196.0, 10.0, (double)model.TrinketPickSlot, (double)model.TrinketPickZoom, (double)model.TrinketRowWidth,
                         wpf ? WpfRow.Trinkets.OnScreen(count, 1920, 1080, scale) : AvaRow.Trinkets.OnScreen(count, 1920, 1080, scale)),
                 })
        {
            // A slot is one step of the game on screen, and the panel four of them, whatever the overlay scale.
            Assert.Equal(pitch, slot * zoom * scale, 6);
            Assert.Equal(4 * pitch, row * scale, 6);
            Assert.InRange(zoom, 0.5, 1);
            Assert.Equal(count, badges.Count);
            for (var i = 0; i < count; i++)
            {
                var (left, right, centre) = badges[i];
                Assert.Equal(960 + shift + ((i - ((count - 1) / 2.0)) * pitch), centre, 6);
                Assert.Equal(centre, (left + right) / 2, 6);
                Assert.True(left >= 0 && right <= 1920, $"scale {scale}, {count} badges: badge {i} at {left:0.#}..{right:0.#}");
                // Never wider than its step, and as wide as the scale asks while that fits.
                Assert.Equal(Math.Min(card * scale, pitch * card / (card + WpfRow.Gap)), right - left, 6);
                Assert.True(i == 0 || left - badges[i - 1].Right >= WpfRow.Gap * zoom * scale - 1e-6);
            }
        }
    }

    /// <summary>The shared rule of the labels: none of those left meets a trinket badge, and only those that would are gone.</summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void No_leaderboard_label_meets_a_trinket_badge(bool wpf, double scale, int count)
    {
        Loc.Instance.SetLanguage(Loc.Russian);
        try
        {
            var settings = new AppSettings { OverlayScale = scale };
            dynamic model = wpf ? new WpfOverlay() : new AvaOverlay();
            var (width, height) = (1920 / scale, 1080 / scale);
            model.ArrangeLeaderboard(width, height, settings);
            model.Update(Live(Trinkets(count)), CardDb.Empty, settings, null, null);

            var badges = wpf ? WpfRow.Trinkets.OnSurface(count, width, height, scale) : AvaRow.Trinkets.OnSurface(count, width, height, scale);
            var labels = ((IEnumerable<dynamic>)model.BgBadges).Select(b => (Place: (int)b.Place, Left: (double)b.Left, Top: (double)b.Top, Height: (double)b.Height)).ToList();
            Assert.True((bool)model.ShowBgBadges);
            Assert.NotEmpty(labels);
            foreach (var label in labels)
            {
                var room = WpfOverlay.LabelRoom(label.Left, label.Top, label.Height);
                Assert.All(badges, b => Assert.False(room.Left < b.Right && b.Left < room.Right && room.Top < b.Bottom && b.Top < room.Bottom,
                    $"scale {scale}, {count} trinkets: the label of place {label.Place} {room} meets the badge {b}"));
            }

            // Seven opponents have a label; a label is gone only where it would meet a badge, and comes back after the choice.
            var gone = Enumerable.Range(1, 8).Except([Me]).Except(labels.Select(l => l.Place)).ToList();
            Assert.All(gone, place =>
            {
                var (top, tile) = WpfGeometryTile(place - 1, height, settings);
                Assert.True(WpfOverlay.LabelMeetsBadges(labels[0].Left, top, tile, badges), $"place {place} has no label and no badge near it");
            });
            model.Update(Live(Trinkets(0)), CardDb.Empty, settings, null, null);
            Assert.Equal(7, ((IEnumerable<dynamic>)model.BgBadges).Count());
        }
        finally
        {
            Loc.Instance.SetLanguage(Loc.English);
        }
    }

    /// <summary>A panel the player moved: the labels give way to the badges where they are now, not where they would be by default.</summary>
    [Fact]
    public void Labels_follow_a_trinket_row_the_player_moved()
    {
        var settings = new AppSettings { OverlayScale = 1.0 };
        var model = new WpfOverlay();
        model.ArrangeLeaderboard(1920, 1080, settings);
        model.Update(Live(Trinkets(4)), CardDb.Empty, settings, null, null);
        var atDefault = model.BgBadges.Count;
        Assert.InRange(atDefault, 1, 6);

        // Dragged to the bottom of the screen, clear of every tile's strip.
        settings.Panels["trinket-row"] = new PanelPlacement { X = 0.4, Y = 0.9, Scale = 1 };
        model.Update(Live(Trinkets(4)), CardDb.Empty, settings, null, null);
        Assert.Equal(7, model.BgBadges.Count);
    }

    /// <summary>
    /// The WPF overlay window laid out for a 1920 x 1080 game: the hero badges as drawn are whole on the screen, centred
    /// on the heroes' steps and no higher than the layout's bound, in both languages, for two and four heroes.
    /// </summary>
    [Fact]
    public void Wpf_hero_badges_are_whole_on_the_screen_over_their_heroes() => WpfThread.RunThemed(() =>
    {
        try
        {
            foreach (var language in new[] { Loc.Russian, Loc.English })
            {
                Loc.Instance.SetLanguage(language);
                foreach (var scale in new[] { 1.0, 1.4, 1.6 })
                {
                    foreach (var count in new[] { 2, 4 })
                    {
                        var (root, _) = Overlay(scale, HeroPick(count));
                        var cards = Named<Border>(Panel(root, "hero-row"), "Card").Select(c => Bounds(c, root)).OrderBy(b => b.Left).ToList();
                        var expected = WpfRow.Heroes.OnScreen(count, 1920, 1080, scale);
                        var tested = $"{language}, scale {scale}, {count} heroes";
                        Assert.Equal(count, cards.Count);
                        for (var i = 0; i < count; i++)
                        {
                            Assert.True(cards[i].Left >= 0 && cards[i].Right <= 1920, $"{tested}: badge {i} at {cards[i].Left:0.#}..{cards[i].Right:0.#}");
                            Assert.True(Math.Abs(((cards[i].Left + cards[i].Right) / 2) - expected[i].Centre) <= 1.5, $"{tested}: badge {i} is centred at {(cards[i].Left + cards[i].Right) / 2:0.##}, its hero at {expected[i].Centre:0.##}");
                            Assert.InRange(cards[i].Width - (expected[i].Right - expected[i].Left), -1.5, 1.5);
                            // From the row's top (212 of 1080) down no further than the bound the labels' rule relies on.
                            Assert.InRange(cards[i].Top, 212, 212 + (10 * scale));
                            Assert.True(cards[i].Bottom <= 212 + (WpfRow.Heroes.Height * WpfRow.Heroes.Zoom(scale) * scale) + 1, $"{tested}: badge {i} ends at {cards[i].Bottom:0.#}");
                        }
                    }
                }
            }
        }
        finally
        {
            Loc.Instance.SetLanguage(Loc.English);
        }
    });

    /// <summary>
    /// The WPF overlay window with a trinket choice and the leaderboard labels: as drawn, no label touches a badge, the
    /// badges are whole on the screen over the trinkets' steps, every line of a badge is whole inside it, and a label is
    /// no wider or higher than the rule takes it to be.
    /// </summary>
    [Fact]
    public void Wpf_trinket_badges_and_leaderboard_labels_do_not_meet() => WpfThread.RunThemed(() =>
    {
        try
        {
            foreach (var language in new[] { Loc.Russian, Loc.English })
            {
                Loc.Instance.SetLanguage(language);
                foreach (var scale in new[] { 1.0, 1.4, 1.6 })
                {
                    foreach (var count in new[] { 2, 3, 4 })
                    {
                        var (root, surface) = Overlay(scale, Trinkets(count));
                        var row = Panel(root, "trinket-row");
                        var cards = Named<Border>(row, "Card").OrderBy(c => Bounds(c, root).Left).ToList();
                        var labels = Named<Border>(root, "Label").Select(l => Bounds(l, root)).ToList();
                        var expected = WpfRow.Trinkets.OnScreen(count, 1920, 1080, scale);
                        var tested = $"{language}, scale {scale}, {count} trinkets";
                        Assert.Equal(count, cards.Count);
                        Assert.NotEmpty(labels);
                        for (var i = 0; i < count; i++)
                        {
                            var card = Bounds(cards[i], root);
                            Assert.True(card.Left >= 0 && card.Right <= 1920, $"{tested}: badge {i} at {card.Left:0.#}..{card.Right:0.#}");
                            Assert.True(Math.Abs(((card.Left + card.Right) / 2) - expected[i].Centre) <= 1.5, $"{tested}: badge {i} is centred at {(card.Left + card.Right) / 2:0.##}, its trinket at {expected[i].Centre:0.##}");
                            Assert.True(card.Bottom <= 226 + (WpfRow.Trinkets.Height * WpfRow.Trinkets.Zoom(scale) * scale) + 1, $"{tested}: badge {i} ends at {card.Bottom:0.#}");
                            Assert.All(labels, label => Assert.False(label.IntersectsWith(card), $"{tested}: a label {label} lies on badge {i} {card}"));
                            // Place, pick rate and date: each line whole inside the badge.
                            var lines = Descendants<TextBlock>(cards[i]).Where(t => Shown(t) && t.Text.Length > 0).ToList();
                            Assert.Contains(lines, t => t.Text.StartsWith(language == Loc.Russian ? "данные от " : "data from ", StringComparison.Ordinal));
                            Assert.All(lines, t =>
                            {
                                var line = Bounds(t, root);
                                Assert.True(line.Left >= card.Left && line.Right <= card.Right + 0.5 && line.Bottom <= card.Bottom + 0.5, $"{tested}: \"{t.Text}\" {line} leaves the badge {card}");
                            });
                        }

                        Assert.All(labels, label => Assert.True(label.Width <= (WpfOverlay.BgBadgeLabelWidth * scale) + 1 && label.Height <= (WpfOverlay.BgBadgeLabelHeight * scale) + 1, $"{tested}: a label is {label.Width:0.#} x {label.Height:0.#}"));
                        Assert.Equal(1920 / scale, surface.ActualWidth, 3);
                    }
                }
            }
        }
        finally
        {
            Loc.Instance.SetLanguage(Loc.English);
        }
    });

    /// <summary>
    /// The size slider of the WPF Overlay settings reaches what the settings allow, and opening the tab leaves a saved
    /// 160% alone (it went to 140%: the slider stopped there and wrote its own end back).
    /// </summary>
    [Fact]
    public void Wpf_size_slider_spans_the_scale_limits_and_keeps_a_saved_160_percent() => WpfThread.RunThemed(() =>
    {
        var settings = new AppSettings { OverlayScale = AppSettings.MaxScale };
        var model = new WpfMain(new HistoryStore(Path.Combine(AppPaths.Root, Guid.NewGuid().ToString("N") + ".db")), settings, TestApp.Data);
        var host = new Border();
        host.Resources.MergedDictionaries.Add(ThemeEngine.Build(ThemePalette.Resolve("Tavern", 0, 0, 1)));
        var tab = new Hearthpulse.App.Main.SettingsTabs.OverlaySettings { DataContext = model };
        host.Child = tab;
        host.Measure(new Size(900, 2000));
        host.Arrange(new Rect(0, 0, 900, 2000));
        Idle();
        host.UpdateLayout();

        var slider = (Slider)tab.FindName("SizeSlider");
        Assert.Equal((AppSettings.MinScale, AppSettings.MaxScale), (slider.Minimum, slider.Maximum));
        Assert.Equal(1.6, slider.Value, 6);
        Assert.Equal(1.6, settings.OverlayScale, 6);
        Assert.Equal(1.6, WpfOverlay.SurfaceScale(settings), 6);
    });

    // ---- data ----------------------------------------------------------------------------------

    private const int Me = 5;

    private static LiveSnapshot Live(BattlegroundsView view) => LiveSnapshot.Empty with { Battlegrounds = view, InGame = true, HearthstoneRunning = true };

    private static BattlegroundsView HeroPick(int count) =>
        new(0, 1, 6, null, 30, 0, 1, false, false, true,
            [.. Enumerable.Range(0, count).Select(i => new BgHeroOption($"HERO_{i}", $"Hero {i}", 0, new HeroStats(12, 4.1, 0.5, 0.1), new HeroMeta(4.23, 4.11, 0.19, 0, "B"), i == 0,
                [new BgHeroComp("Свинобразы - APM-усиление таверны", "S", "easy", "Совет."), new BgHeroComp("Элементали - Живой азерит", "S", "medium", null)]))],
            ["DEMON", "BEAST", "MURLOC", "QUILBOAR", "ELEMENTAL"], true, [], [], [], [], null, 0, [], []);

    /// <summary>A game with seven opponents (boards seen, so their labels name a tribe) and <paramref name="count"/> trinkets offered.</summary>
    private static BattlegroundsView Trinkets(int count)
    {
        BgMinion Elemental() => new("BG_ELEMENTAL", "Elemental", 2, 2, Golden: false, TechLevel: 1, Keywords: []);
        var opponents = Enumerable.Range(1, 8).Where(place => place != Me)
            .Select(place => new BgOpponent(place, $"HERO_{place}", $"Opponent {place}", 30, 0, 3, place, false, 0, [Elemental(), Elemental(), Elemental()], 5, "Win", 3, false))
            .ToList();
        return new(6, 3, 6, "HERO_ME", 30, 0, Me, false, false, false, [], ["DEMON", "BEAST", "MURLOC", "QUILBOAR", "ELEMENTAL"], true, opponents, [], [], [], null, 0, [], [])
        {
            TrinketOptions = [.. Enumerable.Range(0, count).Select(i => new BgTrinketOption($"TRK_{i}", $"Trinket {i}",
                new TrinketMeta(4.52 - (i * 0.1), 0.14, 2528, "C", External: true, GamesAreMinimum: true, Updated: Fetched), i == 0))],
        };
    }

    private static readonly CardDb Cards = CardDb.Parse("enUS", new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
        """[{"id":"BG_ELEMENTAL","dbfId":21,"name":"Elemental","type":"MINION","races":["ELEMENTAL"]}]""")));

    private static (double Top, double Height) WpfGeometryTile(int index, double surfaceHeight, AppSettings settings) =>
        LeaderboardGeometry.Tile(index, surfaceHeight, settings, duos: false);

    /// <summary>The WPF overlay window with this Battlegrounds view, laid out for a 1920 x 1080 game at the overlay scale.</summary>
    private static (FrameworkElement Root, FrameworkElement Surface) Overlay(double scale, BattlegroundsView view)
    {
        var settings = new AppSettings { OverlayScale = scale };
        var model = new WpfOverlay();
        var window = new OverlayWindow(model, settings);
        var root = (FrameworkElement)window.Content;
        // The window is never put on screen: it learns its panels when loaded, so it is told it was.
        window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        model.Update(Live(view), Cards, settings, null, null);
        Assert.Equal(scale, window.LayOutFor(1920, 1080), 6);
        for (var pass = 0; pass < 2; pass++)
        {
            root.Measure(new Size(1920, 1080));
            root.Arrange(new Rect(0, 0, 1920, 1080));
            Idle();
            root.UpdateLayout();
        }

        return (root, (FrameworkElement)Panel(root, "hero-row").Parent);
    }

    private static MovablePanel Panel(DependencyObject root, string id) => Descendants<MovablePanel>(root).Single(p => p.Id == id);

    private static IEnumerable<T> Named<T>(DependencyObject root, string name) where T : FrameworkElement =>
        Descendants<T>(root).Where(e => e.Name == name && Shown(e));

    /// <summary>Shown in the window as laid out (it is never put on screen, so IsVisible says nothing): nothing up the tree is collapsed or hidden.</summary>
    private static bool Shown(DependencyObject element)
    {
        for (var e = element; e is not null; e = VisualTreeHelper.GetParent(e))
        {
            if (e is UIElement { Visibility: not Visibility.Visible })
            {
                return false;
            }
        }

        return true;
    }

    private static Rect Bounds(FrameworkElement element, FrameworkElement root) => element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));

    private static void Idle()
    {
        var frame = new DispatcherFrame();
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in Descendants<T>(child))
            {
                yield return nested;
            }
        }
    }
}
