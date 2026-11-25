using System.ComponentModel;
using System.Diagnostics;
using System.IO;
#if HEARTHPULSE_WPF
using Hearthpulse.App.Infrastructure;
#else
using Hearthpulse.UI.Infrastructure;
#endif
using Hearthpulse.Core;
using Hearthpulse.Core.Localization;

#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Main;
#else
namespace Hearthpulse.UI.Main;
#endif

/// <summary>
/// Settings → About, the licence line (legal review L-M3): the licence agreement and the third-party notices the
/// installers ship next to the executable (<c>LICENSE.txt</c>, <c>THIRD-PARTY-NOTICES.txt</c>, copied by the App and
/// Desktop projects), opened in the system's default app. A missing file or a failed open says so under the links.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>The licence agreement next to the executable.</summary>
    public const string LicenceFileName = "LICENSE.txt";

    /// <summary>The third-party notices next to the executable.</summary>
    public const string NoticesFileName = "THIRD-PARTY-NOTICES.txt";

    private RelayCommand? _openLicence;
    private RelayCommand? _openNotices;
    private Func<string>? _licenceStatus;

    /// <summary>Opens <see cref="LicenceFileName"/>.</summary>
    public RelayCommand OpenLicence => _openLicence ??= new RelayCommand(() => OpenShippedFile(LicenceFileName));

    /// <summary>Opens <see cref="NoticesFileName"/>.</summary>
    public RelayCommand OpenNotices => _openNotices ??= new RelayCommand(() => OpenShippedFile(NoticesFileName));

    /// <summary>Why the last file did not open, in the interface language; empty while nothing failed.</summary>
    public string LicenceStatus => _licenceStatus?.Invoke() ?? string.Empty;

    /// <summary>The folder the files are looked up in, swapped in tests.</summary>
    internal string ShippedFilesFolder { get; set; } = AppContext.BaseDirectory;

    /// <summary>The system call that opens a file in its default app, swapped in tests.</summary>
    internal Func<string, bool> OpenDocument { get; set; } = OpenInDefaultApp;

    private void OpenShippedFile(string name)
    {
        var path = Path.Combine(ShippedFilesFolder, name);
        if (!File.Exists(path))
        {
            Diag.Info($"About: {path} is missing");
            SetLicenceStatus(() => Loc.F("Settings.About.FileMissing", name));
            return;
        }

        SetLicenceStatus(OpenDocument(path) ? null : () => Loc.F("Settings.About.FileNotOpened", name));
    }

    private void SetLicenceStatus(Func<string>? status)
    {
        _licenceStatus = status;
        Raise(nameof(LicenceStatus));
    }

    /// <summary>Hands <paramref name="path"/> to the shell (Windows), <c>open</c> (macOS) or <c>xdg-open</c>.</summary>
    private static bool OpenInDefaultApp(string path)
    {
        try
        {
            ProcessStartInfo start;
            if (OperatingSystem.IsWindows())
            {
                start = new ProcessStartInfo(path) { UseShellExecute = true };
            }
            else
            {
                start = new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open") { UseShellExecute = false };
                start.ArgumentList.Add(path);
            }

            using var process = Process.Start(start);
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            Diag.Error($"About: could not open {path}", ex);
            return false;
        }
    }
}
