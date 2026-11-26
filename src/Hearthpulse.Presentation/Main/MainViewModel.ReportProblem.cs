using Hearthpulse.Core.Updates;
#if HEARTHPULSE_WPF
using Hearthpulse.App.Main.Report;
#else
using Hearthpulse.UI.Main.Report;
#endif

#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Main;
#else
namespace Hearthpulse.UI.Main;
#endif

/// <summary>"Report a problem" (roadmap E1): Settings → About and the BETA flag open its window on a fresh model.</summary>
public sealed partial class MainViewModel
{
    /// <summary>A model for one "Report a problem" window: this data folder, the configured game folder, this version.</summary>
    public ReportProblemViewModel ReportProblem() =>
        new(DataFolder, Settings.HearthstonePath, UpdateService.CurrentVersion) { Background = Background };
}
