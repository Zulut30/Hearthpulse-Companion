using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Hearthpulse.Core;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using WpfMain = Hearthpulse.App.Main.MainViewModel;
using AvaMain = Hearthpulse.UI.Main.MainViewModel;
using WpfLibraryView = Hearthpulse.App.Main.Match.ReplayLibraryView;

namespace Hearthpulse.Presentation.Tests;

/// <summary>
/// "Export to CSV" on History (<c>docs/history-export.md</c>) is one feature in both apps: the same file for the same
/// filter, the same status line, the same request to the view, and a button in the WPF library bar bound as the
/// Avalonia one is (that one is checked headless in Hearthpulse.Tests' <c>LocBindingTests</c>).
/// </summary>
public sealed class HistoryExportContractTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private static GameRecord Game(int number, bool battlegrounds) => new()
    {
        Key = "export-contract-" + number,
        StartedAt = new DateTime(2026, 11, 2, 10, 0, 0, DateTimeKind.Utc).AddMinutes(number * 15),
        EndedAt = new DateTime(2026, 11, 2, 10, 9, 0, DateTimeKind.Utc).AddMinutes(number * 15),
        Mode = battlegrounds ? "Battlegrounds" : "Ranked",
        GameType = battlegrounds ? "GT_BATTLEGROUNDS" : "GT_RANKED",
        Format = battlegrounds ? string.Empty : "FT_STANDARD",
        Result = "Win",
        Placement = battlegrounds ? 2 : 0,
        PlayerName = "Me#1234", OpponentName = "Foe#5678",
        PlayerClass = battlegrounds ? string.Empty : "MAGE",
        OpponentClass = battlegrounds ? string.Empty : "WARRIOR",
        HeroCardId = battlegrounds ? "TB_BaconShop_HERO_08" : "HERO_08",
        DeckName = battlegrounds ? null : "=Deck, \"quoted\"",
        RatingBefore = battlegrounds ? 7000 : null,
        RatingAfter = battlegrounds ? 6950 : null,
        Turns = 9,
    };

    [Fact]
    public async Task Both_hosts_write_the_same_file_for_the_same_filter_and_say_the_same()
    {
        Loc.Instance.SetLanguage(Loc.English);
        var folder = Directory.CreateDirectory(Path.Combine(AppPaths.Root, "export-" + Guid.NewGuid().ToString("N"))).FullName;
        var history = new HistoryStore(Path.Combine(folder, "history.db"));
        for (var i = 0; i < 5; i++)
        {
            Assert.True(history.Add(Game(i, battlegrounds: i % 2 == 1)));
        }

        var wpf = new WpfMain(history, new AppSettings(), TestApp.Data);
        var ava = new AvaMain(history, new AppSettings(), TestApp.Data);
        var asked = new List<string>();
        wpf.Library.ExportFileRequested += name => asked.Add("wpf:" + name);
        ava.Library.ExportFileRequested += name => asked.Add("ava:" + name);
        await wpf.RefreshAsync();
        await ava.RefreshAsync();
        wpf.SetHistoryFilter.Execute("Ranked");
        ava.SetHistoryFilter.Execute("Ranked");

        Assert.True(wpf.Library.CanExport);
        Assert.True(ava.Library.CanExport);
        wpf.Library.Export.Execute(null);
        ava.Library.Export.Execute(null);
        var wpfFile = Path.Combine(folder, "wpf.csv");
        var avaFile = Path.Combine(folder, "ava.csv");
        await wpf.Library.ExportTo(wpfFile).WaitAsync(Wait);
        await ava.Library.ExportTo(avaFile).WaitAsync(Wait);

        var name = WpfMain.HistoryExportFileName(DateTime.Now);
        Assert.Equal(AvaMain.HistoryExportFileName(DateTime.Now), name);
        Assert.Equal(["wpf:" + name, "ava:" + name], asked);
        var bytes = File.ReadAllBytes(wpfFile);
        Assert.Equal(bytes, File.ReadAllBytes(avaFile));
        Assert.Equal(HistoryExport.Bytes(history.Recent(100).Where(g => g.Mode == "Ranked")), bytes);
        var text = File.ReadAllText(wpfFile);
        Assert.Equal(1 + 3 + 1, text.Split("\r\n").Length);
        Assert.Contains(",\"'=Deck, \"\"quoted\"\"\",", text, StringComparison.Ordinal);
        Assert.DoesNotContain("#1234", text, StringComparison.Ordinal);
        Assert.DoesNotContain("#5678", text, StringComparison.Ordinal);
        Assert.Equal("Saved 3 games.", wpf.Library.Status);
        Assert.Equal((wpf.Library.Status, wpf.Library.StatusKey, wpf.Library.StatusGlyph), (ava.Library.Status, ava.Library.StatusKey, ava.Library.StatusGlyph));

        // No games of the mode: neither host leaves a file, both say the same.
        wpf.SetHistoryFilter.Execute("Arena");
        ava.SetHistoryFilter.Execute("Arena");
        await wpf.Library.ExportTo(Path.Combine(folder, "wpf-none.csv")).WaitAsync(Wait);
        await ava.Library.ExportTo(Path.Combine(folder, "ava-none.csv")).WaitAsync(Wait);
        Assert.False(File.Exists(Path.Combine(folder, "wpf-none.csv")));
        Assert.False(File.Exists(Path.Combine(folder, "ava-none.csv")));
        Assert.Equal(Loc.T("Library.Export.Empty"), wpf.Library.Status);
        Assert.Equal((wpf.Library.Status, wpf.Library.StatusKey, wpf.Library.StatusGlyph), (ava.Library.Status, ava.Library.StatusKey, ava.Library.StatusGlyph));

        // Nowhere to save: the same words in both.
        wpf.Library.ExportUnavailable();
        ava.Library.ExportUnavailable();
        Assert.Equal(Loc.T("Library.Export.Failed"), wpf.Library.Status);
        Assert.Equal((wpf.Library.Status, wpf.Library.StatusKey), (ava.Library.Status, ava.Library.StatusKey));
    }

    [Fact]
    public void Wpf_library_bar_has_the_export_button_bound_to_the_model() => WpfThread.RunThemed(() =>
    {
        Loc.Instance.SetLanguage(Loc.English);
        var history = new HistoryStore(Path.Combine(AppPaths.Root, Guid.NewGuid().ToString("N") + ".db"));
        var model = new WpfMain(history, new AppSettings(), TestApp.Data);
        var view = new WpfLibraryView { DataContext = model.Library };
        view.Measure(new Size(900, 600));
        view.Arrange(new Rect(0, 0, 900, 600));
        view.UpdateLayout();

        var button = Assert.IsType<Button>(view.FindName("ExportButton"));
        Assert.Same(model.Library.Export, button.Command);
        Assert.True(button.IsEnabled);
        Assert.Equal("CanExport", BindingOperations.GetBinding(button, UIElement.IsEnabledProperty)?.Path.Path);
        Assert.Equal(Loc.T("Library.Export.Tip"), button.ToolTip);
        Assert.True(ToolTipService.GetShowOnDisabled(button));
        Assert.Contains(Descendants(button).OfType<TextBlock>(), t => t.Text == "Export to CSV");
    });

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var below in Descendants(child))
            {
                yield return below;
            }
        }
    }
}
