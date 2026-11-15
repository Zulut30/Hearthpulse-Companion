using Avalonia.Controls;
using Avalonia.Threading;
using Hearthpulse.Core.Api;
using Hearthpulse.UI;
using Hearthpulse.UI.Main;

namespace Hearthpulse.Desktop;

/// <summary>
/// Render-check scenes of the data summary (<c>HEARTHPULSE_RENDER_ONLY=datahealth</c>), on made-up data
/// (<see cref="DataHealthScene"/>): the "Data status" card of Settings, Data (<c>main-datahealth-settings.png</c>) and
/// the Arena page with the delay line under its community numbers (<c>main-datahealth-arena.png</c>).
/// </summary>
internal static partial class OffscreenRenderer
{
    private static async Task RenderDataHealthAsync(Window main, MainView view, AppHost host, string folder)
    {
        // The start-up reads go first, so neither replaces the scene's data afterwards.
        await host.LoadDataHealthAsync();
        await host.LoadArenaMetaAsync();
        host.Main.SetArenaMeta(DataHealthScene.ArenaMeta());
        host.Main.SetDataHealth(DataHealthScene.Report());

        view.ShowPage("Settings");
        view.ShowSettingsTab("Data");
        Dispatcher.UIThread.RunJobs();
        view.GetLogicalDescendantNamed("DataHealthCard")?.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        await Save(main, Path.Combine(folder, "main-datahealth-settings.png"));

        main.Height = 2000;
        view.ShowPage("Arena");
        await Task.Delay(2500);
        await Save(main, Path.Combine(folder, "main-datahealth-arena.png"));
        view.ShowSettingsTab("General");
    }

    private static Control? GetLogicalDescendantNamed(this Control root, string name) =>
        Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(root).OfType<Control>().FirstOrDefault(c => c.Name == name);
}
