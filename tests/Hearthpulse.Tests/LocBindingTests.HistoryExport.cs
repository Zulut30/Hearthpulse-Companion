using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hearthpulse.Core;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using Hearthpulse.UI.Controls;
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

            // d2: the folder, Export and Import stand at the right of the tabs, as in WPF; the last one ends at the
            // row's right edge. Too narrow for both, they take a line of their own under the tabs; wide, the tabs' line.
            var head = view.GetVisualDescendants().OfType<TabRowPanel>().Single();
            var tabs = head.Children[0];
            var import = head.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, model.Library.Import));
            foreach (var width in new[] { 676.0, 1100.0 })
            {
                window.Width = width;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var exportBox = new Rect(button.TranslatePoint(default, head)!.Value, button.Bounds.Size);
                Assert.Equal(head.Bounds.Width, import.TranslatePoint(new Point(import.Bounds.Width, 0), head)!.Value.X, 1.0);
                Assert.True(head.Wrapped ? exportBox.Y >= tabs.Bounds.Bottom : exportBox.X >= tabs.Bounds.Right + head.Gap - 0.5,
                    $"At {width}: Export at {exportBox}, the tabs end at {tabs.Bounds.Right:0} (wrapped: {head.Wrapped})");
            }

            Assert.False(head.Wrapped);
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
