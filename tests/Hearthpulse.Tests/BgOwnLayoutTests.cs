using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Hearthpulse.Core;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Pool;
using Hearthpulse.UI.Controls;
using Hearthpulse.UI.Main;

namespace Hearthpulse.Tests;

/// <summary>
/// The layout of the block "Hearthpulse statistics (own games)" in the Avalonia page, laid out headless (no app
/// render): at the compact window's narrowest (420) and at the usual width, in Russian and English, the title, the
/// hero names and the place column are whole - nothing is cut by an ellipsis and nothing runs out of its room. The
/// rows are the render fixture (tools/analytics/render_fixture.py) with long hero names.
/// </summary>
[Collection(nameof(GlobalLanguage))]
public sealed class BgOwnLayoutTests
{
    /// <summary>
    /// The headless platform draws no glyphs and gives every character the width of its font size. The app's fonts
    /// average about half of that on these texts; 0.6 keeps a margin over them (a digit of Inter is 0.62 em, a
    /// letter less), so a line that passes here has its room on screen.
    /// </summary>
    private const double GlyphScale = 0.6;

    private static readonly DateTime Fetched = new(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);

    public BgOwnLayoutTests() => LocBindingTests.Headless.Run(() => Loc.Instance.SetLanguage(Loc.English));

    [Theory]
    [InlineData(420, "ru")]
    [InlineData(420, "en")]
    [InlineData(1280, "ru")]
    [InlineData(1280, "en")]
    public void The_title_the_hero_names_and_the_place_column_are_whole(double width, string language) => LocBindingTests.Headless.Run(() =>
    {
        var theme = PageRowsPanelAvaloniaTests.Theme();
        Loc.Instance.SetLanguage(language == "ru" ? Loc.Russian : Loc.English);
        var model = new MainViewModel(new HistoryStore(TestDataFolder.Scratch("hp-bg-own-layout") + ".db"), new AppSettings(), TestDataFolder.App);
        model.SetCards(Names(language));
        var view = new MainView { DataContext = model };
        var window = new Window { Width = width, Height = 1200 };
        window.Resources["UiFont"] = FontFamily.Default;
        window.Content = view;
        window.Show();
        try
        {
            view.ShowPage("Battlegrounds");
            view.ShowBattlegroundsTab("Heroes");
            var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Analytics", "bg-heroes-render.json"));
            model.SetBgOwn(OwnBgHeroesResult.FromJson(json, Fetched)!);
            PageRowsPanelAvaloniaTests.Settle(window);
            var compact = width < 900;
            Assert.Equal(compact, view.Classes.Contains("compact"));

            T Named<T>(string name) where T : Control => view.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
            var card = Named<Border>("BgOwnCard");
            Assert.True(card.IsEffectivelyVisible);
            var room = card.Bounds.Width - card.Padding.Left - card.Padding.Right - card.BorderThickness.Left - card.BorderThickness.Right;
            Assert.True(room > 200, $"room {room}");

            // The title: the one-line ornament where it has the room for the whole text, wrapping text in compact.
            var ornament = Named<Ornament>("BgOwnTitle");
            var plain = Named<TextBlock>("BgOwnTitleCompact");
            Assert.Equal((!compact, compact), (ornament.IsEffectivelyVisible, plain.IsEffectivelyVisible));
            if (compact)
            {
                Assert.Equal((TextWrapping.Wrap, TextTrimming.None), (plain.TextWrapping, plain.TextTrimming));
                Whole(plain, card, room, "title");
            }
            else
            {
                // As the ornament measures itself: bold, half a point over its size, after the icon and its gap.
                var text = new FormattedText(Loc.T("Main.BgOwn.Title"), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold), ornament.Size + 0.5, Brushes.Black);
                var iconWidth = Math.Round(ornament.Size * 2.3) + 10;
                Assert.True(iconWidth + (text.Width * GlyphScale) <= ornament.Bounds.Width, $"title {iconWidth + (text.Width * GlyphScale)} in {ornament.Bounds.Width}");
            }

            // Every text of the block that is on screen: inside the card, on as many lines as it needs, never cut.
            var shown = card.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text)).ToList();
            foreach (var block in shown)
            {
                Whole(block, card, room, block.Text!);
            }

            // The hero names (one of them long) and both lines of the place column are among them, in every row.
            var list = Named<ItemsControl>("BgOwnList");
            var texts = list.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
            foreach (var row in model.BgOwnRows)
            {
                Assert.Contains(row.Name, texts);
                Assert.Contains(row.Place, texts);
            }

            Assert.Contains(model.BgOwnRows[0].PlaceDetail, texts);
            Assert.Contains(model.BgOwnRows, r => r.Name.Length > 24);

            // The head of the place column stands over its values, and the name's column keeps a readable width.
            var head = card.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == Loc.T("Main.Table.AvgPlacement") && t.IsEffectivelyVisible);
            var value = list.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == model.BgOwnRows[0].Place && t.IsEffectivelyVisible);
            Assert.Equal(Right(head, card), Right(value, card), 1.0);
            var name = list.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == model.BgOwnRows[0].Name);
            Assert.True(name.Bounds.Width >= 96, $"name column {name.Bounds.Width}");
        }
        finally
        {
            window.Close();
            Application.Current!.Styles.Remove(theme);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Loc.Instance.SetLanguage(Loc.English);
        }
    });

    /// <summary>The text is laid out whole (no ellipsis, no line wider than the block) and the block lies inside the card's content.</summary>
    private static void Whole(TextBlock block, Border card, double room, string what)
    {
        Assert.True(block.TextTrimming == TextTrimming.None, $"{what}: trimming is on");
        var layout = block.TextLayout;
        Assert.All(layout.TextLines, line => Assert.False(line.HasCollapsed, what));
        Assert.All(layout.TextLines, line => Assert.True(line.Width * GlyphScale <= block.Bounds.Width + 0.5, $"{what}: line {line.Width * GlyphScale} in {block.Bounds.Width}"));
        var left = block.TranslatePoint(default, card)!.Value.X;
        var inner = card.Padding.Left + card.BorderThickness.Left;
        // A row's plate stands out of the list by its own padding, so the text itself starts at the content's edge.
        Assert.True(left >= inner - 0.5 && left + block.Bounds.Width <= inner + room + 0.5, $"{what}: {left}..{left + block.Bounds.Width} outside {inner}..{inner + room}");
    }

    private static double Right(TextBlock block, Border card) => block.TranslatePoint(new Point(block.Bounds.Width, 0), card)!.Value.X;

    /// <summary>The fixture's heroes under names as long as the game's longest, in the page's language.</summary>
    private static CardDb Names(string language)
    {
        var names = language == "ru"
            ? new[] { "Повелитель кошмаров Ксавий", "Зирелла", "Мастер Нгуен", "Кариэль Роум", "Бурекрыл", "Скаббс Маслорез" }
            : ["Xavius the Nightmare Lord", "Xyrella", "Master Nguyen", "Cariel Roame", "Galewing", "Scabbs Cutterbutter"];
        var ids = new[] { "BG20_HERO_100", "BG20_HERO_101", "BG20_HERO_202", "BG21_HERO_000", "BG20_HERO_283", "BG21_HERO_010" };
        var json = "[" + string.Join(',', ids.Select((id, i) => $$"""{ "id": "{{id}}", "dbfId": {{i + 1}}, "name": "{{names[i]}}", "type": "HERO" }""")) + "]";
        return CardDb.Parse(language == "ru" ? "ruRU" : "enUS", new MemoryStream(Encoding.UTF8.GetBytes(json)));
    }
}
