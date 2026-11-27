using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hearthpulse.Core;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Tracking;
using Hearthpulse.UI.Main;
using Hearthpulse.UI.Overlay;

namespace Hearthpulse.Tests;

public sealed partial class LocBindingTests
{
    /// <summary>
    /// The Avalonia overlay laid out for a 1920 x 1080 game at overlay scales 100%, 140% and 160%, in both languages:
    /// the hero badges (two and four heroes) are whole on the screen and centred on the heroes' steps of the game; the
    /// trinket badges (two to four) likewise, every line of a badge whole inside it (the date and a warning line of its
    /// own only while the trinket numbers' update is delayed, O-22), and no leaderboard label touches a badge.
    /// </summary>
    [Fact]
    public void Avalonia_pick_badges_stay_on_the_screen_over_their_cards_and_clear_of_the_labels() => Headless.Run(PickRows);

    /// <summary>
    /// The size slider of the Avalonia Overlay settings reaches what the settings allow, and building the page leaves a
    /// saved 160% alone (it went to 140%: the slider stopped there and wrote its own end back).
    /// </summary>
    [Fact]
    public void Avalonia_size_slider_spans_the_scale_limits_and_keeps_a_saved_160_percent() => Headless.Run(SizeSliderRange);

    private static void SizeSliderRange()
    {
        Loc.Instance.SetLanguage(Loc.English);
        var app = Application.Current!;
        var theme = Theme();
        var settings = new AppSettings { OverlayScale = AppSettings.MaxScale, SettingsTab = "Overlay" };
        var model = new MainViewModel(new Core.Data.HistoryStore(Path.Combine(AppPaths.Root, $"hp-scale-{Guid.NewGuid():N}.db")), settings, TestDataFolder.App);
        var window = new Window { Width = 1260, Height = 840 };
        try
        {
            var view = new MainView { DataContext = model };
            // Not shown: the main window's fonts are the app's own, which the headless platform lacks. The bindings
            // attach with the data context, which is when the slider used to write its own end back.
            window.Content = view;
            Dispatcher.UIThread.RunJobs();

            var slider = view.FindControl<UI.Main.SettingsTabs.OverlaySettings>("SettingsOverlay")!.FindControl<Slider>("SizeSlider")!;
            Assert.Equal((AppSettings.MinScale, AppSettings.MaxScale), (slider.Minimum, slider.Maximum));
            Assert.Equal(1.6, slider.Value, 6);
            Assert.Equal(1.6, settings.OverlayScale, 6);
            Assert.Equal(1.6, OverlayViewModel.SurfaceScale(settings), 6);
        }
        finally
        {
            window.Close();
            app.Styles.Remove(theme);
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void PickRows()
    {
        var app = Application.Current!;
        var theme = Theme();
        try
        {
            foreach (var language in new[] { Loc.Russian, Loc.English })
            {
                Loc.Instance.SetLanguage(language);
                foreach (var scale in new[] { 1.0, 1.4, 1.6 })
                {
                    foreach (var count in new[] { 2, 4 })
                    {
                        HeroRow(language, scale, count);
                    }

                    foreach (var count in new[] { 2, 3, 4 })
                    {
                        TrinketRow(language, scale, count, late: false);
                        TrinketRow(language, scale, count, late: true);
                    }
                }
            }
        }
        finally
        {
            Loc.Instance.SetLanguage(Loc.English);
            app.Styles.Remove(theme);
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void HeroRow(string language, double scale, int count)
    {
        var view = new BattlegroundsView(0, 1, 6, null, 30, 0, 1, false, false, true,
            [.. Enumerable.Range(0, count).Select(i => new BgHeroOption($"HERO_{i}", $"Hero {i}", 0, new HeroStats(12, 4.1, 0.5, 0.1), new HeroMeta(4.23, 4.11, 0.19, 0, "B"), i == 0,
                [new BgHeroComp("Свинобразы - APM-усиление таверны", "S", "easy", "Совет."), new BgHeroComp("Элементали - Живой азерит", "S", "medium", null)]))],
            ["DEMON", "BEAST", "MURLOC", "QUILBOAR", "ELEMENTAL"], true, [], [], [], [], null, 0, [], []);
        var (window, overlay) = PickOverlay(scale, view);
        try
        {
            var tested = $"{language}, scale {scale}, {count} heroes";
            var cards = PickCards(overlay, "hero-row");
            var expected = PickRowLayout.Heroes.OnScreen(count, 1920, 1080, scale);
            Assert.Equal(count, cards.Count);
            for (var i = 0; i < count; i++)
            {
                var card = OnScreen(cards[i], overlay);
                Assert.True(card.Left >= 0 && card.Right <= 1920, $"{tested}: badge {i} at {card.Left:0.#}..{card.Right:0.#}");
                Assert.True(Math.Abs(card.Center.X - expected[i].Centre) <= 1.5, $"{tested}: badge {i} is centred at {card.Center.X:0.##}, its hero at {expected[i].Centre:0.##}");
                Assert.InRange(card.Width - (expected[i].Right - expected[i].Left), -1.5, 1.5);
                Assert.InRange(card.Top, 212, 212 + (10 * scale));
                Assert.True(card.Bottom <= 212 + (PickRowLayout.Heroes.Height * PickRowLayout.Heroes.Zoom(scale) * scale) + 1, $"{tested}: badge {i} ends at {card.Bottom:0.#}");
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static void TrinketRow(string language, double scale, int count, bool late)
    {
        const int me = 5;
        BgMinion Elemental() => new("BG_ELEMENTAL", "Elemental", 2, 2, Golden: false, TechLevel: 1, Keywords: []);
        var opponents = Enumerable.Range(1, 8).Where(place => place != me)
            .Select(place => new BgOpponent(place, $"HERO_{place}", $"Opponent {place}", 30, 0, 3, place, false, 0, [Elemental(), Elemental(), Elemental()], 5, "Win", 3, false))
            .ToList();
        var fetched = new DateTime(2026, 11, 15, 17, 1, 0, DateTimeKind.Utc);
        var view = new BattlegroundsView(6, 3, 6, "HERO_ME", 30, 0, me, false, false, false, [], ["DEMON", "BEAST", "MURLOC", "QUILBOAR", "ELEMENTAL"], true, opponents, [], [], [], null, 0, [], [])
        {
            TrinketOptions = [.. Enumerable.Range(0, count).Select(i => new BgTrinketOption($"TRK_{i}", $"Trinket {i}",
                new TrinketMeta(4.52 - (i * 0.1), 0.14, 2528, "C", External: true, GamesAreMinimum: true, Updated: fetched), i == 0))],
        };
        var (window, overlay) = PickOverlay(scale, view, late ? PickScene.TrinketsLate() : null);
        try
        {
            var tested = $"{language}, scale {scale}, {count} trinkets{(late ? ", update delayed" : string.Empty)}";
            var cards = PickCards(overlay, "trinket-row");
            var labels = overlay.GetVisualDescendants().OfType<Border>().Where(b => b.Name == "Label" && b.IsEffectivelyVisible).Select(l => OnScreen(l, overlay)).ToList();
            var expected = PickRowLayout.Trinkets.OnScreen(count, 1920, 1080, scale);
            Assert.Equal(count, cards.Count);
            Assert.NotEmpty(labels);
            for (var i = 0; i < count; i++)
            {
                var card = OnScreen(cards[i], overlay);
                Assert.True(card.Left >= 0 && card.Right <= 1920, $"{tested}: badge {i} at {card.Left:0.#}..{card.Right:0.#}");
                Assert.True(Math.Abs(card.Center.X - expected[i].Centre) <= 1.5, $"{tested}: badge {i} is centred at {card.Center.X:0.##}, its trinket at {expected[i].Centre:0.##}");
                Assert.True(card.Bottom <= 226 + (PickRowLayout.Trinkets.Height * PickRowLayout.Trinkets.Zoom(scale) * scale) + 1, $"{tested}: badge {i} ends at {card.Bottom:0.#}");
                Assert.All(labels, label => Assert.False(label.Intersects(card), $"{tested}: a label {label} lies on badge {i} {card}"));
                // The place and, while late, the date: each line whole inside the badge. Fresh numbers carry no date.
                var lines = cards[i].GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && t.Text is { Length: > 0 }).ToList();
                Assert.Equal(late, lines.Any(t => t.Text!.StartsWith(language == Loc.Russian ? "данные от " : "data from ", StringComparison.Ordinal)));
                // A late update is a line of its own with a warning mark, not the end of the date line the badge cut off.
                Assert.Equal(late, lines.Any(t => t.Text == Loc.T("Source.Health.OverlayDelay")));
                Assert.Equal(late, lines.Any(t => t.Text == "\U000F0026"));
                Assert.All(lines, t =>
                {
                    var line = OnScreen(t, overlay);
                    Assert.True(line.Left >= card.Left && line.Right <= card.Right + 0.5 && line.Bottom <= card.Bottom + 0.5, $"{tested}: \"{t.Text}\" {line} leaves the badge {card}");
                });
            }

            Assert.All(labels, label => Assert.True(label.Width <= (OverlayViewModel.BgBadgeLabelWidth * scale) + 1 && label.Height <= (OverlayViewModel.BgBadgeLabelHeight * scale) + 1, $"{tested}: a label is {label.Width:0.#} x {label.Height:0.#}"));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>The overlay view with this Battlegrounds view, fitted to a 1920 x 1080 game at the overlay scale.</summary>
    private static (Window Window, OverlayView Overlay) PickOverlay(double scale, BattlegroundsView view, Core.Api.DataHealthReport? health = null)
    {
        var settings = new AppSettings { OverlayScale = scale };
        var cards = CardDb.Parse("enUS", new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
            """[{"id":"BG_ELEMENTAL","dbfId":21,"name":"Elemental","type":"MINION","races":["ELEMENTAL"]}]""")));
        var window = new Window { Width = 1920, Height = 1080 };
        // The overlay's UI font is Inter, which the app registers at startup and the headless platform does not have.
        window.Resources["UiFont"] = Avalonia.Media.FontFamily.Default;
        var model = new OverlayViewModel();
        var overlay = new OverlayView { DataContext = model, Settings = settings };
        window.Content = overlay;
        window.Show();
        overlay.Fit(1920, 1080);
        if (health is not null)
        {
            model.SetDataHealth(health);
        }

        model.Update(LiveSnapshot.Empty with { InGame = true, HearthstoneRunning = true, Battlegrounds = view }, cards, settings, null);
        for (var pass = 0; pass < 2; pass++)
        {
            overlay.Fit(1920, 1080);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }

        return (window, overlay);
    }

    /// <summary>The badge cards of a pick row, left to right.</summary>
    private static List<Border> PickCards(OverlayView overlay, string panel) =>
        [.. overlay.GetLogicalDescendants().OfType<MovablePanel>().Single(p => p.Id == panel)
            .GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("pickCard") && b.IsEffectivelyVisible)
            .OrderBy(b => OnScreen(b, overlay).Left)];

    /// <summary>A control's bounds in the overlay's own pixels (the surface's scale applied).</summary>
    private static Rect OnScreen(Visual control, Visual overlay) =>
        new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(overlay)!.Value);
}
