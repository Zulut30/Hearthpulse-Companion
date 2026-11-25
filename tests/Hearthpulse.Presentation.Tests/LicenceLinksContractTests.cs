using Hearthpulse.Core;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using WpfMain = Hearthpulse.App.Main.MainViewModel;
using AvaMain = Hearthpulse.UI.Main.MainViewModel;

namespace Hearthpulse.Presentation.Tests;

/// <summary>
/// Settings → About, the licence line (legal review L-M3): "Licence agreement" and "Third-party notices" open the files
/// shipped next to the executable in the default app; a missing file or a failed open says so, in both apps.
/// </summary>
public sealed class LicenceLinksContractTests
{
    private static dynamic Model(bool wpf, string folder, List<string> opened, bool openWorks = true)
    {
        var history = new HistoryStore(Path.Combine(AppPaths.Root, Guid.NewGuid().ToString("N") + ".db"));
        dynamic model = wpf ? new WpfMain(history, new AppSettings(), TestApp.Data) : new AvaMain(history, new AppSettings(), TestApp.Data);
        model.ShippedFilesFolder = folder;
        model.OpenDocument = new Func<string, bool>(path =>
        {
            opened.Add(path);
            return openWorks;
        });
        return model;
    }

    private static string Folder(params string[] files)
    {
        var folder = Path.Combine(AppPaths.Root, "licence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        foreach (var file in files)
        {
            File.WriteAllText(Path.Combine(folder, file), "text");
        }

        return folder;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Both_links_open_the_shipped_files(bool wpf)
    {
        var folder = Folder("LICENSE.txt", "THIRD-PARTY-NOTICES.txt");
        var opened = new List<string>();
        var model = Model(wpf, folder, opened);

        model.OpenLicence.Execute(null);
        model.OpenNotices.Execute(null);

        Assert.Equal([Path.Combine(folder, "LICENSE.txt"), Path.Combine(folder, "THIRD-PARTY-NOTICES.txt")], opened);
        Assert.Equal(string.Empty, (string)model.LicenceStatus);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_missing_file_is_not_opened_and_the_line_says_how_to_get_it_back(bool wpf)
    {
        Loc.Instance.SetLanguage(Loc.English);
        var opened = new List<string>();
        var model = Model(wpf, Folder("LICENSE.txt"), opened);

        model.OpenNotices.Execute(null);

        Assert.Empty(opened);
        Assert.Equal("Couldn't find THIRD-PARTY-NOTICES.txt next to the app. Reinstall Hearthpulse to restore it.", (string)model.LicenceStatus);
        Loc.Instance.SetLanguage(Loc.Russian);
        Assert.Contains("THIRD-PARTY-NOTICES.txt", (string)model.LicenceStatus, StringComparison.Ordinal);
        Assert.StartsWith("Не найден файл", (string)model.LicenceStatus, StringComparison.Ordinal);

        // The file that is there still opens, and the note goes.
        model.OpenLicence.Execute(null);
        Assert.Single(opened);
        Assert.Equal(string.Empty, (string)model.LicenceStatus);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_file_no_app_opens_says_so(bool wpf)
    {
        Loc.Instance.SetLanguage(Loc.English);
        var opened = new List<string>();
        var model = Model(wpf, Folder("LICENSE.txt"), opened, openWorks: false);

        model.OpenLicence.Execute(null);

        Assert.Single(opened);
        Assert.Equal("Couldn't open LICENSE.txt: no app is set to open text files.", (string)model.LicenceStatus);
    }
}
