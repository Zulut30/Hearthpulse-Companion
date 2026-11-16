using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Hearthpulse.App.Infrastructure;
using Hearthpulse.App.Main;
using Hearthpulse.App.Overlay;
using Hearthpulse.Core;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Accounts;
using Hearthpulse.Core.Api;
using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Site;
using Hearthpulse.Core.Constructed;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Hearthstone;
using Hearthpulse.Core.Hosting;
using Hearthpulse.Core.Logs;
using Hearthpulse.Core.Memory;
using Hearthpulse.Core.Pool;
using Hearthpulse.Core.Tracking;
using Hearthpulse.Core.Updates;

namespace Hearthpulse.App;

/// <summary>
/// The WPF host: builds the services, the view models and the windows at start and wires them together. Its other
/// concerns live in partials of their own: errors (App.Crash.cs), the main window's life (App.MainWindow.cs), tray and
/// hotkeys (App.Shell.cs), the theme (App.Theme.cs), updates (App.Updates.cs), the pipeline watchdog (App.Watchdog.cs),
/// the OBS windows (App.Stream.cs) and the Arena, Brawl and card pool loads.
/// </summary>
public partial class App : Application
{
    private Mutex? _mutex;
    private AppSettings _settings = new();
    private HttpClient _http = null!;
    private BgSiteClient? _bgSite;
    private HearthpulseApi _api = null!;
    private AccountService _account = null!;
    private ProfileUploader _uploader = null!;
    private UsageGate _usage = null!;
    private HistoryStore _history = null!;
    private HearthstoneMemory _memory = null!;
    private GameSession _session = null!;
    private MulliganGuideSource _mulliganGuide = null!;
    private LogWatcher? _watcher;
    private MainViewModel _mainViewModel = null!;
    private OverlayWindow _overlay = null!;
    private OverlayViewModel _overlayViewModel = null!;
    private string? _installDir;
    private StartupPrefetch _prefetch = null!;
    private AppServices _services = null!;
    private CardDb _cards = CardDb.Empty;
    private LiveFeed _feed = null!;
    private DispatcherTimer _historyDebounce = null!;
    private DispatcherTimer _settingsDebounce = null!;
    private DispatcherTimer _metaRefresh = null!;
    private string? _renderDir;
    private ConstructedMetaLoader _constructedLoader = null!;
    private BattlegroundsMeta? _meta;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        NumberCulture.Apply();
        for (var i = 0; i < e.Args.Length - 1; i++)
        {
            if (e.Args[i] == "--render-ui")
            {
                _renderDir = e.Args[i + 1];
            }
        }

        // A --render-ui run never takes the single-instance lock: it closes by itself, and holding the lock would make the
        // player's own copy refuse to start while it runs.
        var firstInstance = true;
        if (_renderDir is null)
        {
            _mutex = new Mutex(true, @"Local\Hearthpulse.SingleInstance", out firstInstance);
        }

        if (!firstInstance)
        {
            // Settings are not loaded yet; read the language only, so the message is in the player's language.
            Loc.Instance.SetLanguage(AppSettings.Load().InterfaceLanguage());
            MessageBox.Show(Loc.T("Host.AlreadyRunning"), "Hearthpulse", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        WatchForErrors();

        _settings = AppSettings.Load();
        if (_renderDir is null)
        {
            // Crash reports (plan 0.5) from here on: a crash while the history, the session or the windows start is
            // reported too. Never while rendering pages off-screen.
            _crashReporter = Hearthpulse.Core.Diagnostics.CrashReporter.ForApp(_settings, "wpf", UpdateService.CurrentVersion);
            _crashReporter.SendPendingAsync().Forget("Crash reports");
        }

        Loc.Instance.SetLanguage(_settings.InterfaceLanguage());
        ApplyTheme();
        Diag.Info($"Hearthpulse {UpdateService.CurrentVersion} (app {typeof(App).Assembly.GetName().Version?.ToString(3)}) starting on "
                  + $"{System.Runtime.InteropServices.RuntimeInformation.OSDescription}, {Environment.ProcessorCount} CPUs, "
                  + $"{GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024):N0} MB RAM, software rendering {(_settings.OverlaySoftwareRendering ? "on" : "off")}");
        if (_settings.OverlaySoftwareRendering)
        {
            // Some Windows 10 GPUs and drivers never compose the transparent layered window in hardware,
            // so the overlay is invisible; software rendering is the known workaround.
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        }
        var services = new AppServices(_settings, typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.1", () => _cards);
        (_http, _api, _prefetch, _history, _memory, _session, _mulliganGuide, _account, _uploader, _usage) =
            (services.Http, services.Api, services.Prefetch, services.History, services.Memory, services.Session, services.MulliganGuide, services.Account,
                services.Uploader, services.Usage);
        _services = services;
        _crashReporter?.Activity = services.Account.Activity; // the account center's journal of report sends
        CardArt.Http = _http;

        _mainViewModel = new MainViewModel(_history, _settings, _services.Data);
        _mainViewModel.UpdateRequested += OnInstallUpdateRequested;
        _mainViewModel.AttachAccount(_account);
        _mainViewModel.AttachTwitch(services.Twitch);
        StartDataHealth();
        _mainViewModel.CrashReportsChanged += () =>
        {
            // Switched off: consent withdrawn, what waits is not sent.
            if (!_settings.SendCrashReports)
            {
                _crashReporter?.ClearPending();
            }

            _settingsDebounce.Kick();
        };

        // A decision of the daily free use (plan 3.1) makes the session publish again.
        _usage.Changed += () => _ = Dispatcher.BeginInvoke(() => _session.SetMeta(_session.Advisor.Meta));
        _mainViewModel.AttachUploader(_uploader);
        _mainViewModel.UploadSettingChanged += () =>
        {
            _settingsDebounce.Kick();
        };
        if (_renderDir is null)
        {
            _account.RestoreAsync().Forget("Account restore");
        }
        _overlayViewModel = new OverlayViewModel();
        _feed = new LiveFeed(
            action => _ = Dispatcher.BeginInvoke(action, DispatcherPriority.Background),
            view => LiveDeckRecord.Read(_history, _installDir, view),
            _mainViewModel.Apply,
            RefreshOverlay);
        _constructedLoader = new ConstructedMetaLoader(
            (format, names, tierList, cancel) => ConstructedMeta.LoadAsync(new HearthpulseData(_api), _cards, format, _settings.MetaRank, _settings.MetaPeriod, Loc.CardLocale,
                tierList, cancel, names),
            () => ArchetypeTranslations.LoadAsync(_http),
            () => ConstructedMeta.Key(ConstructedMeta.Formats, _settings.MetaFormat),
            () => ConstructedMeta.FormatKeyOf(_mainViewModel.ConstructedFormat),
            _mainViewModel.SetConstructedMeta,
            _session.SetConstructedMeta,
            () => _session.RequestMemoryTrimWhenWindowAway(),
            names => (_mainViewModel.ArchetypeNames, _overlayViewModel.ArchetypeNames) = (names, names));
        _session.ApplySettings(_settings);
        _overlayViewModel.SecretToggleRequested += _session.ToggleSecret;
        _overlayViewModel.CardPoolBrowserResized += OnSettingsChanged;
        _overlayViewModel.CounterModeRequested += (id, mode) =>
        {
            _mainViewModel.ApplyCounterMode(id, mode);
            OnSettingsChanged();
        };
        _mainViewModel.CountersChanged += OnSettingsChanged;
        _session.SetCompFocus(_settings.CompFocus);

        _mainViewModel.ThemeChanged += OnThemeChanged;
        // Plan 2.4: the mulligan's rank filter on the meta page; the overlay's guide follows it.
        var mulliganData = new HearthpulseData(_api);
        _mainViewModel.LoadRankMulligan = (archetype, format, rank, cancel) => MulliganGuideSource.LoadRankSliceAsync(mulliganData, archetype, format, rank, cancel);
        _mainViewModel.MulliganRankChanged += () =>
        {
            _mulliganGuide.Rank = _settings.MulliganRank;
            _settingsDebounce.Kick();
        };
        _mainViewModel.GameStatsChanged += on =>
        {
            _settingsDebounce.Kick();
            if (on) _services.GameStats.Kick();
            else _services.GameStats.Withdraw();
            _services.SourceCaptures.ApplySettingsAsync(explicitEnable: on).Forget("Source capture settings");
        };
        _services.SourceCaptures.StateChanged += state => _ = Dispatcher.BeginInvoke(() => _mainViewModel.UpdateSourceCaptureState(state));
        _services.GameStats.StateChanged += state => _ = Dispatcher.BeginInvoke(() => _mainViewModel.UpdateGameStatsState(state));
        _mainViewModel.PoolSharingChanged += () =>
        {
            _settingsDebounce.Kick();
            _services.PoolSharing.ShareHistoryAsync().Forget("Sharing earlier games");
        };
        // The install's secret or "earlier games shared" is new in the settings; raised off the UI thread.
        _services.PoolSharing.SettingsChanged += () => _ = Dispatcher.BeginInvoke(() => _settingsDebounce.Kick());
        _mainViewModel.ConstructedMetaRequested += format =>
        {
            if (format is not null)
            {
                _constructedLoader.Ensure(format);
                return;
            }

            _settingsDebounce.Kick();
            _constructedLoader.ReloadAllAsync().Forget("Constructed meta load");
        };
        _releaseMain.Tick += (_, _) => ReleaseMain();
        // Started minimized, the window is built the first time it is opened from the tray.
        if (_renderDir is not null || !_settings.StartMinimized)
        {
            _mainLife.Open(CreateMainWindow);
        }

        _overlay = CreateOverlay();
        SetupStream();
        if (_renderDir is not null)
        {
            PrepareOffscreenRender();
        }
        else if (_settings.StartMinimized)
        {
            _overlay.Show();
        }
        else
        {
            // The window shown first pays for the first frame of the whole app: the main window is on screen about
            // 180 ms sooner when the overlay (with no game to cover yet) comes up after it.
            void ShowOverlay(object? sender, EventArgs e)
            {
                ((Window)sender!).ContentRendered -= ShowOverlay;
                _overlay.Show();
            }

            _main.ContentRendered += ShowOverlay;
            _main.Show();
        }

        _historyDebounce = Debounce.Timer(TimeSpan.FromMilliseconds(600), () => _mainViewModel.RefreshAsync().Forget("History refresh"));
        _settingsDebounce = Debounce.Timer(TimeSpan.FromMilliseconds(400), _settings.Save);
        _metaRefresh = new DispatcherTimer { Interval = BattlegroundsMeta.RefreshInterval };
        _metaRefresh.Tick += (_, _) => RefreshMetaAsync().Forget("Meta refresh");

        _session.SnapshotChanged += _feed.Publish;
        Controls.CardPreview.Related = _session.RelatedCardsOf;
        _session.ConstructedMetaNeeded += format => _ = Dispatcher.BeginInvoke(() => _constructedLoader.Ensure(format));
        _session.GameFinished += outcome => _ = Dispatcher.BeginInvoke(() => OnGameFinished(outcome));
        _history.Changed += () => _ = Dispatcher.BeginInvoke(() =>
        {
            _historyDebounce.Kick();
            _feed.ForgetDeck();
        });

        if (_renderDir is null)
        {
            SetupTray();
            RegisterHotkey();
        }

        Loc.Instance.LanguageChanged += OnLanguageChanged;
        InitializeAsync().Forget("Start-up");
        if (_renderDir is null)
        {
            StartUpdateChecks();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mainViewModel?.StopMigration();
        StopDataHealth();
        _watchdog?.Stop();
        _uiProbe?.Dispose();
        _watcher?.Dispose();
        _uploader?.Dispose();
        _services?.GameStats.Dispose();
        _services?.SourceCaptures.Dispose();
        _hotkeys?.Dispose();

        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }

        _memory?.Dispose();
        StopUpdateLoop();
        _updateLoop.Dispose();
        if (_stagedUpdate is { } staged && UpdateService.ApplyStaged(staged, relaunch: false))
        {
            _stagedUpdate = null;
        }

        _settings.Save();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private async Task InitializeAsync()
    {
        try
        {
            _mainViewModel.ShowBusy(() => Loc.T("Host.Busy.LookingForGame"));
            Task.Run(CacheCleanup.Run).Forget("Cache clean-up");
            (_installDir, var clientLocale) = await _prefetch.Game;
            if (_installDir is not null && _settings.HearthstonePath != _installDir)
            {
                _settings.HearthstonePath = _installDir;
                _settings.Save();
            }

            _session.InstallDirectory = _installDir;

            // "auto" language and cards follow the installed client (a Russian client shows Russian cards and interface).
            _settings.ClientLocale = clientLocale;
            Loc.Instance.SetLanguage(_settings.InterfaceLanguage());
            var locale = _settings.CardLocale();
            CardArt.Locale = locale;

            // HEARTHPULSE_DATA isolates our files, but Hearthstone's log.config is outside that folder.
            var logConfigChanged = _renderDir is null && await Task.Run(() =>
            {
                try
                {
                    return LogConfig.EnsureAll(_installDir);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Diag.Error("log.config not writable", ex);
                    return false;
                }
            });

            _mainViewModel.ShowBusy(() => Loc.T("Host.Busy.LoadingCards"));
            _cards = await _prefetch.CardsAsync(locale);
            CardArt.FallbackUrl = id => _cards.BgCards.ImageUrl(id);
            if (_cards.Count == 0 && locale != "enUS")
            {
                _cards = await CardDb.LoadAsync("enUS", _http);
            }

            _session.Cards = _cards;
            _mainViewModel.SetCards(_cards);
            _services.LoadCardPoolAsync().Forget("Card pool load");

            if (!_settings.HistoryImported || !_settings.HistoryStatsFilled || !_settings.HistoryCardsFilled)
            {
                await ImportLogsAsync(force: false);
            }
            else if (_installDir is { } install)
            {
                // Games saved before Decks.log was read correctly (and imported ones) have no deck yet.
                Task.Run(() => LogBackfill.FillDecks(install, _history, _cards)).Forget("Deck backfill");
            }

            if (_renderDir is not null)
            {
                await RenderUiAsync(_renderDir);
                return;
            }

            _watcher = new LogWatcher(_session, () => _installDir);
            _watcher.Start();
            _uploader.Start();
            _services.GameStats.Start();
            _services.SourceCaptures.Start();
            _services.PoolSharing.Start();
            _watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _watchdog.Tick += (_, _) => WatchPipeline();
            _watchdog.Start();
            _uiProbe = new System.Threading.Timer(ProbeUiThread, null, 2000, 1000);
            _mainViewModel.ShowBusy(_installDir is null
                ? () => Loc.T("Host.Busy.GameNotFound")
                : null);

            if (logConfigChanged && HearthstoneInstall.IsRunning())
            {
                Notify(Loc.T("Host.Notify.RestartTitle"), Loc.T("Host.Notify.LogSettingsChanged"));
            }

            await LoadMetaAsync();
            await _constructedLoader.ReloadAllAsync();
            _metaRefresh.Start();
            // Cards, the catch-up and the meta leave their parsing garbage behind; it is collected once things are quiet.
            _session.RequestMemoryTrim();
            _services.PoolSharing.ShareHistoryAsync().Forget("Sharing earlier games");
        }
        catch (Exception ex)
        {
            Diag.Error("Startup failed", ex);
            if (_renderDir is not null)
            {
                Quit(1);
                return;
            }

            _mainViewModel.ShowBusy(() => Loc.T("Host.Busy.StartupProblem"));
        }
    }

    /// <summary>
    /// Community stats: heroes and minions from the Hearthpulse API, the external comps and trinkets, and, when a stats
    /// pool is set up, the pool's meta laid over them (or the pool's own meta when the API did not load).
    /// docs/bg-meta-sources.md.
    /// </summary>
    private async Task LoadMetaAsync()
    {
        // The limits on their own: a slow answer never holds the meta back.
        _usage.RefreshLimitsAsync().Forget("Usage limits");
        // The tier tabs' lists come from another host: beside the meta, not after it, so a slow answer of either
        // holds the other back as little as a fresh cache does. The advisor's comps and trinkets are read from them.
        var site = LoadBgSiteAsync();
        site.Forget("Battlegrounds site stats load");
        LoadBgOwnAsync().Forget("Own Battlegrounds hero statistics load");
        LoadDataHealthAsync().Forget("Data health");
        var meta = await BattlegroundsMeta.LoadCommunityAsync(_api, _http, _settings, () => BattlegroundsMeta.LoadExternalAsync(_bgSite!, site), _cards);
        if (meta is not null)
        {
            _meta = meta;
            _session.SetMeta(meta);
            _mainViewModel.SetMeta(meta);
            _overlayViewModel.SetBgData(meta);
        }
        else
        {
            _mainViewModel.BattlegroundsMetaUnavailable();
        }

        LoadArenaMetaAsync().Forget("Arena stats load");
        LoadBrawlMetaAsync().Forget("Brawl stats load");
        _session.RequestMemoryTrimWhenWindowAway();
    }

    /// <summary>
    /// The lists of the main window's Battlegrounds tier tabs, from the open service the site itself reads
    /// (docs/bg-site-stats.md): loaded off the UI thread, with a client of its own that keeps no cookies and follows no
    /// redirects. The advisor reads its comps and trinkets from the same lists (docs/bg-meta-sources.md).
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The client is kept by the site client in a field of the host and lives as long as the app.")]
    private async Task<BgSiteStats?> LoadBgSiteAsync()
    {
        var site = _bgSite ??= new BgSiteClient(BgSiteClient.CreateHttp());
        _mainViewModel.BgSiteSlices ??= new BgSiteSliceLoader((duos, mmr) => site.LoadHeroesAsync(duos, mmr), mmr => site.LoadTrinketsAsync(mmr));
        var stats = await site.LoadAsync();
        _mainViewModel.SetBgSiteStats(stats);
        return stats;
    }

    /// <summary>
    /// Hearthpulse's own hero statistics for the Heroes tab (docs/analytics-api.md): read from the stats pool when one
    /// is set up, beside the meta and on the same refresh. Without a pool nothing is asked and the block stays hidden.
    /// </summary>
    private async Task LoadBgOwnAsync() => _mainViewModel.SetBgOwn(await OwnBgHeroes.LoadAsync(_http, _settings));

    /// <summary>The 6-hour refresh of both metas; their parsing garbage is collected once things are quiet.</summary>
    private async Task RefreshMetaAsync()
    {
        await Task.WhenAll(LoadMetaAsync(), _constructedLoader.ReloadAllAsync());
        _session.RequestMemoryTrim();
    }

    private Task ImportLogsAsync(bool force) => _mainViewModel.ImportLogsAsync(_installDir, force);

    // ---- live snapshots ---------------------------------------------------------------------

    private void RefreshOverlay() => _overlayViewModel.Update(_feed.Last, _cards, _settings, _feed.Deck, _feed.Matchup);

    private void OnGameFinished(GameOutcome outcome)
    {
        // Reads the history row, so off the UI thread; a game that ends while signed out is not sent.
        Task.Run(() => _uploader.OnGameFinished(outcome)).Forget("Profile upload");

        if (_settings.ShowGameToasts && _settings.OverlayEnabled)
        {
            _overlayViewModel.ShowOutcome(outcome);
        }
    }

    private OverlayWindow CreateOverlay()
    {
        var overlay = new OverlayWindow(_overlayViewModel, _settings);
        overlay.LayoutChanged += OnSettingsChanged;
        overlay.PanelLockChanged += () => _main?.SyncOverlaySwitch();
        overlay.HoveredHero = () => _memory.IsAttached ? _memory.GetHoveredLeaderboardHero() : null;
        return overlay;
    }

    private void OnSettingsChanged()
    {
        _settingsDebounce.Kick();
        if (_trayOverlayItem is not null)
        {
            _trayOverlayItem.Checked = _settings.OverlayEnabled;
        }

        _session.ApplySettings(_settings);
        _services.LoadCardPoolAsync().Forget("Card pool load");
        RefreshOverlay();
        // The theme's storyboards take their timings when it is built: "reduce motion" rebuilds it like a theme change.
        if (_settings.ReduceMotion != Motion.Reduced)
        {
            OnThemeChanged();
        }

        if (_renderDir is null)
        {
            Autostart.Apply(_settings.StartWithWindows);
        }
    }

    /// <summary>
    /// The interface language changed (on the UI thread): the overlay rebuilds its texts from the last snapshot, and the
    /// texts this host owns (the tray menu) are shown again in the new language; the busy line and update status follow
    /// by themselves (MainViewModel).
    /// </summary>
    private void OnLanguageChanged()
    {
        RefreshOverlay();
        // Meta texts (archetype labels, sources, empty states) are made when the meta loads; the caches make this cheap.
        if (_cards.Count > 0 && _renderDir is null)
        {
            LoadMetaAsync().Forget("Battlegrounds meta load");
            _constructedLoader.ReloadAllAsync().Forget("Constructed meta load");
        }

        SetTrayTexts();
    }

    private void Quit(int exitCode = 0)
    {
        _releaseMain.Stop();
        _mainLife.Release()?.CloseForReal();
        _overlay.Close();
        Shutdown(exitCode);
    }
}

