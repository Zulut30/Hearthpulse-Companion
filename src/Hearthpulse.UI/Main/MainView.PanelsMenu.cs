using System.ComponentModel;
using Avalonia.Controls;

namespace Hearthpulse.UI.Main;

/// <summary>The live bar's menu of overlay panels, built in code and kept in step with the view model's toggles.</summary>
public partial class MainView
{
    /// <summary>
    /// The live bar's ⋯ menu: every overlay panel, its glyph and name dimmed while it is off. Built here rather than
    /// through an item theme, which would have to name the platform's MenuItem theme.
    /// </summary>
    private void BuildPanelsMenu(MainViewModel viewModel)
    {
        if (PanelsMenuButton.Flyout is not MenuFlyout menu)
        {
            return;
        }

        menu.Items.Clear();
        StopMenuListeners();
        foreach (var toggle in viewModel.PanelToggles)
        {
            var name = new TextBlock { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            name.Classes.Add("panelName");
            var glyph = new TextBlock { Text = toggle.Icon, FontSize = 15, Width = 26, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            glyph.Classes.Add("icon");
            var header = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Children = { glyph, name } };
            header.Classes.Add("panelItem");
            var item = new MenuItem { Header = header, Command = toggle.Toggle, StaysOpenOnClick = true };
            void Show()
            {
                name.Text = toggle.Name;
                header.Classes.Set("off", !toggle.IsOn);
                ToolTip.SetTip(item, toggle.Tip);
            }

            Show();
            // A toggle may change off the UI thread (a language switch from a worker): the item is touched on its own thread only.
            PropertyChangedEventHandler listener = (_, _) =>
            {
                if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                {
                    Show();
                }
                else
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(Show);
                }
            };
            toggle.PropertyChanged += listener;
            _menuListeners.Add(() => toggle.PropertyChanged -= listener);
            menu.Items.Add(item);
        }
    }

    /// <summary>
    /// The toggles live with the view model: a menu item still listening to them keeps this view (its flyout once opened
    /// is part of the window), and a new data context would add a second listener.
    /// </summary>
    private readonly List<Action> _menuListeners = [];

    private void StopMenuListeners()
    {
        foreach (var stop in _menuListeners)
        {
            stop();
        }

        _menuListeners.Clear();
    }
}
