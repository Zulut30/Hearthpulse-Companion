using System.Windows;
using System.Windows.Automation;
using System.Windows.Documents;
using System.Windows.Threading;
using Hearthpulse.App.Infrastructure;
using Hearthpulse.Core;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using WpfAbout = Hearthpulse.App.Main.SettingsTabs.AboutSettings;
using WpfMain = Hearthpulse.App.Main.MainViewModel;

namespace Hearthpulse.Presentation.Tests;

/// <summary>
/// WPF Settings → About, "What's new" (TASK-2026-11-09-update-delivery, review m3/m5): the link tells a screen reader
/// whether the notes are open, and its accent follows a theme swapped in place (DynamicResource). The licence line
/// (legal review L-M3) opens the shipped files and says when one is missing; card headings are headings for UI
/// Automation (n5).
/// </summary>
public sealed class AboutSettingsXamlTests
{
    [Fact]
    public void Whats_new_link_names_its_state_and_follows_the_theme_accent()
    {
        Loc.Instance.SetLanguage(Loc.English);
        OnSta(Check);
    }

    [Fact]
    public void A_card_heading_is_a_level_two_heading_with_its_title_as_the_name() => OnSta(() =>
    {
        var heading = new Hearthpulse.App.Controls.Ornament { Text = "BATTLEGROUNDS RATING" };
        var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(heading);
        Assert.Equal("Battlegrounds rating", peer.GetName());
        Assert.Equal(System.Windows.Automation.Peers.AutomationControlType.Text, peer.GetAutomationControlType());
        Assert.Equal(AutomationHeadingLevel.Level2, peer.GetHeadingLevel());

        var sub = new Hearthpulse.App.Controls.Ornament { Text = "Tracked" };
        AutomationProperties.SetHeadingLevel(sub, AutomationHeadingLevel.Level3);
        Assert.Equal(AutomationHeadingLevel.Level3, System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(sub).GetHeadingLevel());
    });

    private static void OnSta(Action check)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                check();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new InvalidOperationException("About settings check failed", failure);
        }
    }

    private static void Check()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Clear();
        app.Resources.MergedDictionaries.Add(ThemeEngine.Build(ThemePalette.Resolve("Tavern", 0, 0, 1)));
        var model = new WpfMain(new HistoryStore(Path.Combine(AppPaths.Root, Guid.NewGuid().ToString("N") + ".db")), new AppSettings(), TestApp.Data);
        model.SetReleaseNotes("0.15.0", "Notes.", new Uri("https://github.com/Zulut30/Hearthpulse_App/releases/tag/v0.15.0"));
        // The theme sits on a parent here: a swap in Application.Resources reaches elements through their window, and
        // this control has none; a parent's swap reaches them the same way the app's reaches a window.
        var host = new System.Windows.Controls.Border();
        host.Resources.MergedDictionaries.Add(ThemeEngine.Build(ThemePalette.Resolve("Tavern", 0, 0, 1)));
        var about = new WpfAbout { DataContext = model };
        host.Child = about;
        host.Measure(new Size(900, 800));
        host.Arrange(new Rect(0, 0, 900, 800));
        host.UpdateLayout();

        var link = (Hyperlink)about.FindName("ReleaseNotesToggle");
        var glyph = (Run)about.FindName("ReleaseNotesGlyph");
        Assert.Equal("What's new in 0.15.0, collapsed", AutomationProperties.GetName(link));
        link.Command.Execute(null);
        about.UpdateLayout();
        Assert.Equal("What's new in 0.15.0, expanded", AutomationProperties.GetName(link));

        var tavern = host.FindResource("Accent");
        Assert.Same(tavern, link.Foreground);

        // A theme swapped in place: the links take the new accent without the window being rebuilt.
        host.Resources.MergedDictionaries.Clear();
        host.Resources.MergedDictionaries.Add(ThemeEngine.Build(ThemePalette.Resolve("Frost", 0, 0, 1)));
        host.UpdateLayout();
        var frost = host.FindResource("Accent");
        Assert.NotSame(tavern, frost);
        Assert.Same(frost, link.Foreground);
        Assert.Same(frost, glyph.Foreground);
        Assert.Same(frost, ((Hyperlink)about.FindName("ReleasePageLink")).Foreground);

        // The licence line: both links in the accent, bound to the files next to the exe; a missing one is said under them.
        var licence = (Hyperlink)about.FindName("LicenceLink");
        var notices = (Hyperlink)about.FindName("NoticesLink");
        var status = (System.Windows.Controls.TextBlock)about.FindName("LicenceStatusLine");
        Assert.Same(frost, licence.Foreground);
        Assert.Same(frost, notices.Foreground);
        Assert.Equal(Loc.T("Settings.About.Licence"), ((Run)licence.Inlines.FirstInline).Text);
        Assert.Equal(Loc.T("Settings.About.Notices"), ((Run)notices.Inlines.FirstInline).Text);
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(status));
        Assert.Equal(Visibility.Collapsed, status.Visibility);
        var opened = new List<string>();
        model.ShippedFilesFolder = Path.Combine(AppPaths.Root, "no-licence-" + Guid.NewGuid().ToString("N"));
        model.OpenDocument = new Func<string, bool>(path =>
        {
            opened.Add(path);
            return true;
        });
        Assert.Same(model.OpenNotices, notices.Command);
        licence.Command.Execute(null);
        host.UpdateLayout();
        Assert.Empty(opened);
        Assert.Equal(Visibility.Visible, status.Visibility);
        Assert.Equal(Loc.F("Settings.About.FileMissing", "LICENSE.txt"), status.Text);
    }
}
