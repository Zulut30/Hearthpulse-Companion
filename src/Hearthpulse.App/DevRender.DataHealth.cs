using System.IO;
using System.Windows;
using System.Windows.Controls;
using Hearthpulse.Core.Api;

namespace Hearthpulse.App;

/// <summary>
/// Render-check scenes of the data summary (<c>HEARTHPULSE_RENDER_ONLY=datahealth</c>), on made-up data
/// (<see cref="DataHealthScene"/>): the "Data status" card of Settings, Data (<c>main-datahealth-settings.png</c>) and
/// the Arena page with the delay line under its community numbers (<c>main-datahealth-arena.png</c>).
/// </summary>
public partial class App
{
    private async Task RenderDataHealthAsync(string folder)
    {
        // The start-up reads go first, so neither replaces the scene's data afterwards.
        await LoadDataHealthAsync();
        await LoadArenaMetaAsync();
        _mainViewModel.SetArenaMeta(DataHealthScene.ArenaMeta());
        _mainViewModel.SetDataHealth(DataHealthScene.Report());

        _main.ShowPage("Settings");
        _main.ShowSettingsTab("Data");
        await Task.Delay(800);
        _main.UpdateLayout();
        if (Named<FrameworkElement>(_main, "DataHealthCard") is { } card)
        {
            card.BringIntoView();
            await Task.Delay(300);
            _main.UpdateLayout();
        }

        Save(_main, Path.Combine(folder, "main-datahealth-settings.png"));

        _main.ShowPage("Arena");
        await Task.Delay(2500);
        _main.UpdateLayout();
        if (_main.FindName("PageArena") is ScrollViewer { Content: FrameworkElement content })
        {
            SaveFull(content, Path.Combine(folder, "main-datahealth-arena.png"));
        }

        _main.ShowSettingsTab("General");
    }
}
