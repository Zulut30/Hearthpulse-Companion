using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hearthpulse.Core;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using Hearthpulse.UI.Main;
using Hearthpulse.UI.Main.Match;

namespace Hearthpulse.Tests;

public sealed partial class LocBindingTests
{
    /// <summary>
    /// "Export to CSV" in the Avalonia History bar (<c>docs/history-export.md</c>): bound to the model's command and its
    /// "busy" state, named in the interface language (switched live), and inside the page at the narrowest card with the
    /// longer Russian text (the bar wraps rather than clips).
    /// </summary>
    [Fact]
    public void History_export_button_binds_follows_the_language_and_fits_the_narrow_page() => Headless.Run(() =>
    {
        Loc.Instance.SetLanguage(Loc.English);
        var theme = Theme();
        var model = new MainViewModel(new HistoryStore(Path.Combine(AppPaths.Root, $"hp-export-{Guid.NewGuid():N}.db")), new AppSettings(), TestDataFolder.App);
        var view = new ReplayLibraryView { DataContext = model.Library };
        // The card of History at the minimum window (LocBindingTests.Library: about 676 px).
        var window = new Window { Width = 676, Height = 600, Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var button = Named<Button>(view, "ExportButton");

            Assert.Same(model.Library.Export, button.Command);
            Assert.True(button.IsEffectivelyEnabled);
            Assert.True(ToolTip.GetShowOnDisabled(button));
            Assert.Equal(Loc.T("Library.Export.Tip"), ToolTip.GetTip(button));
            Assert.Contains(button.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Export to CSV");

            Loc.Instance.SetLanguage(Loc.Russian);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Contains(button.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Экспорт в CSV");
            Assert.Equal("Сохранить все игры выбранного режима таблицей для Excel или Google Таблиц. Имён игроков в файле нет.", ToolTip.GetTip(button));
            var end = button.TranslatePoint(new Point(button.Bounds.Width, 0), view)!.Value.X;
            var start = button.TranslatePoint(new Point(0, 0), view)!.Value.X;
            Assert.True(start >= 0 && end <= view.Bounds.Width + 0.5, $"The button spans {start}..{end}, the page is {view.Bounds.Width} wide");
        }
        finally
        {
            Loc.Instance.SetLanguage(Loc.English);
            window.Close();
            Application.Current!.Styles.Remove(theme);
            Dispatcher.UIThread.RunJobs();
        }
    });
}
