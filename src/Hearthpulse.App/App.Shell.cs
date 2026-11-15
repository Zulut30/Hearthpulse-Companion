using Hearthpulse.App.Infrastructure;
using Hearthpulse.Core;
using Hearthpulse.Core.Localization;
using Forms = System.Windows.Forms;

namespace Hearthpulse.App;

/// <summary>The host's place in Windows: the tray icon with its menu and notifications, and the global hotkeys.</summary>
public partial class App
{
    private const int HotkeyId = 0x484C;
    private const int LayoutHotkeyId = 0x484D;
    private const int RefreshHotkeyId = 0x484E;

    private Forms.NotifyIcon? _tray;
    private Forms.ToolStripMenuItem? _trayOverlayItem;
    private Forms.ToolStripMenuItem? _traySoftwareItem;
    private HotkeyWindow? _hotkeys;

    private void SetupTray()
    {
        using var iconStream = GetResourceStream(new Uri("pack://application:,,,/Assets/hearthpulse.ico"))?.Stream;
        var menu = new Forms.ContextMenuStrip { Renderer = new DarkMenuRenderer(), ShowImageMargin = false, ShowCheckMargin = true };
        // Each item keeps its text's key in Tag, so a language switch can retitle it (SetTrayTexts).
        menu.Items.Add(Loc.T("Host.Tray.Open"), null, (_, _) => ShowMain()).Tag = "Host.Tray.Open";
        _trayOverlayItem = new Forms.ToolStripMenuItem(Loc.T("Host.Tray.Overlay"), null, (_, _) => ToggleOverlaySetting())
        {
            Checked = _settings.OverlayEnabled,
            Tag = "Host.Tray.Overlay",
        };
        menu.Items.Add(_trayOverlayItem);
        menu.Items.Add(StreamerTrayItem());
        menu.Items.Add(Loc.T("Host.Tray.Arrange"), null, (_, _) => _overlayViewModel.LayoutEditing = !_overlayViewModel.LayoutEditing).Tag = "Host.Tray.Arrange";
        menu.Items.Add(Loc.T("Host.Tray.HardRefresh"), null, (_, _) => HardRefresh("tray menu")).Tag = "Host.Tray.HardRefresh";
        _traySoftwareItem = new Forms.ToolStripMenuItem(Loc.T("Host.Tray.SoftwareRendering"), null, (_, _) => ToggleSoftwareRendering())
        {
            Checked = _settings.OverlaySoftwareRendering,
            Tag = "Host.Tray.SoftwareRendering",
        };
        menu.Items.Add(_traySoftwareItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Loc.T("Host.Tray.Quit"), null, (_, _) => Quit()).Tag = "Host.Tray.Quit";
        foreach (Forms.ToolStripItem item in menu.Items)
        {
            item.ForeColor = System.Drawing.Color.FromArgb(0xF4, 0xE7, 0xC9);
        }

        _tray = new Forms.NotifyIcon
        {
            Icon = iconStream is null ? System.Drawing.SystemIcons.Application : new System.Drawing.Icon(iconStream),
            Text = StreamerTrayTip(),
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => ShowMain();
    }

    private void Notify(string title, string text)
    {
        _tray?.ShowBalloonTip(6000, title, text, Forms.ToolTipIcon.Info);
    }

    /// <summary>Retitles the tray menu in the current language (each item's Tag holds its key).</summary>
    private void SetTrayTexts()
    {
        if (_tray?.ContextMenuStrip is not { } menu)
        {
            return;
        }

        foreach (Forms.ToolStripItem item in menu.Items)
        {
            if (item.Tag is string key)
            {
                item.Text = Loc.T(key);
            }
        }
    }

    private void ToggleOverlaySetting()
    {
        _settings.OverlayEnabled = !_settings.OverlayEnabled;
        _main?.SyncOverlaySwitch();
        OnSettingsChanged();
    }

    private void ToggleSoftwareRendering()
    {
        _settings.OverlaySoftwareRendering = !_settings.OverlaySoftwareRendering;
        if (_traySoftwareItem is not null)
        {
            _traySoftwareItem.Checked = _settings.OverlaySoftwareRendering;
        }

        _settings.Save();
        Notify(Loc.T("Host.Notify.RendererTitle"), _settings.OverlaySoftwareRendering
            ? Loc.T("Host.Notify.SoftwareRendering")
            : Loc.T("Host.Notify.HardwareRendering"));
    }

    private void RegisterHotkey()
    {
        // Not on the main window: a theme change replaces it and the tray lets it go.
        _hotkeys = new HotkeyWindow();
        if (!_hotkeys.Register(HotkeyId, 0x48, () =>
            {
                _overlay.Suppressed = !_overlay.Suppressed;
                Notify(_overlay.Suppressed ? Loc.T("Host.Notify.OverlayHiddenTitle") : Loc.T("Host.Notify.OverlayShownTitle"),
                    _overlay.Suppressed ? Loc.T("Host.Notify.OverlayHidden") : Loc.T("Host.Notify.OverlayShown"));
            }))
        {
            Diag.Info("Ctrl+Shift+H is taken by another app; overlay hotkey disabled.");
        }

        if (!_hotkeys.Register(LayoutHotkeyId, 0x4C, () => _overlayViewModel.LayoutEditing = !_overlayViewModel.LayoutEditing))
        {
            Diag.Info("Ctrl+Shift+L is taken by another app; layout hotkey disabled.");
        }

        if (!_hotkeys.Register(RefreshHotkeyId, 0x52, () => HardRefresh("Ctrl+Shift+R")))
        {
            Diag.Info("Ctrl+Shift+R is taken by another app; hard refresh hotkey disabled.");
        }
    }

    /// <summary>Tray menu in the app's brown and gold palette (Surface, Surface3, Border and Accent).</summary>
    private sealed class DarkMenuRenderer : Forms.ToolStripProfessionalRenderer
    {
        private static readonly System.Drawing.Color Background = System.Drawing.Color.FromArgb(0x21, 0x16, 0x0E);
        private static readonly System.Drawing.Color Hover = System.Drawing.Color.FromArgb(0x3A, 0x28, 0x17);
        private static readonly System.Drawing.Color Line = System.Drawing.Color.FromArgb(0x3D, 0x2A, 0x19);

        protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e) => e.Graphics.Clear(Background);

        // The check column is on (ShowCheckMargin); left to the base renderer it is a light grey strip.
        protected override void OnRenderImageMargin(Forms.ToolStripRenderEventArgs e)
        {
            using var brush = new System.Drawing.SolidBrush(Background);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
        {
            using var pen = new System.Drawing.Pen(Line);
            e.Graphics.DrawRectangle(pen, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
        }

        protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
        {
            using var brush = new System.Drawing.SolidBrush(e.Item.Selected ? Hover : Background);
            e.Graphics.FillRectangle(brush, new System.Drawing.Rectangle(System.Drawing.Point.Empty, e.Item.Size));
        }

        protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new System.Drawing.Pen(Line);
            var y = e.Item.Height / 2;
            e.Graphics.DrawLine(pen, 4, y, e.Item.Width - 4, y);
        }

        protected override void OnRenderItemCheck(Forms.ToolStripItemImageRenderEventArgs e)
        {
            using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0xE9, 0xB9, 0x49));
            e.Graphics.FillEllipse(brush, e.ImageRectangle.X + 4, e.ImageRectangle.Y + 4, 8, 8);
        }
    }
}
