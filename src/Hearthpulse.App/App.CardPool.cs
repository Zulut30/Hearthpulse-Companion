using System.IO;
using Hearthpulse.App.Infrastructure;
using Hearthpulse.Core;
using Hearthpulse.Core.CardPool;
using Hearthpulse.Core.Hosting;
using Hearthpulse.Core.Tracking;

namespace Hearthpulse.App;

/// <summary>The card pool panel's render scenes (the catalog itself loads in <see cref="Core.Hosting.AppServices.LoadCardPoolAsync"/>).</summary>
public partial class App
{
    /// <summary>
    /// --render-ui: the card pool panel beside the options of a Discover (Blazing Invocation as a Shaman), with the pick
    /// hint, and at overlay scale 0.8; then a hand card's pool at the panel's own spot with Ctrl held (the same card on
    /// 16:9, 16:10 and 4:3 screens, a large pool, Scion of Insight's rune pools, a pool of 12 cards or fewer, an Excavate
    /// treasure, N'Zoth's "this game" list and the card preview of its row), then the pool browser for Thief's Tools
    /// with its filters open, and with one filter on.
    /// </summary>
    private async Task RenderCardPoolAsync(string folder)
    {
        var catalog = await CardPoolCatalog.LoadAsync(_api, _http);
        var wiki = new WikiPoolSource(new Core.Api.HearthpulseData(_api));
        await wiki.LoadAsync("BE_298");
        var engine = new CardPoolEngine(catalog.WithRaces(_cards), _cards, CardPoolEngine.MetaGames(_constructedLoader.Default, _cards), wiki);
        if (engine.Build("CORE_GIL_836", new PoolContext("FT_STANDARD", "SHAMAN", "WARRIOR"), PoolTrigger.Choice) is not { } pool
            || engine.Build("JAIL_706", new PoolContext("FT_STANDARD", "ROGUE", "WARRIOR"), PoolTrigger.Request) is not { } browse)
        {
            Console.WriteLine("card pool: no catalog, scenes skipped");
            return;
        }

        // Beside the three Discover options; then with the stats pool's picks of those options (sample totals); then at
        // overlay scale 0.8 (the popup is drawn at 0.85 of a game card at least).
        var choice = new CardPoolAnchor(PoolAnchorZone.Choice, 0, 3);
        Show(pool, choice);
        await _overlay.RenderToFileAsync(Path.Combine(folder, "overlay-card-pool.png"), 1920, 1080, 0);
        Show(pool, choice, DiscoverPicks.RenderSample(pool, _cards));
        await _overlay.RenderToFileAsync(Path.Combine(folder, "overlay-card-pool-picks.png"), 1920, 1080, 0);
        var scale = _settings.OverlayScale;
        _settings.OverlayScale = 0.8;
        Show(pool, choice);
        await _overlay.RenderToFileAsync(Path.Combine(folder, "overlay-card-pool-scale08.png"), 1920, 1080, 0);
        _settings.OverlayScale = scale;

        // A card in hand with Ctrl held: the panel at its own spot, on the three screen shapes of the visual baselines.
        _overlayViewModel.UpdateCardPoolKeys(true);
        if (engine.Build("CORE_GIL_836", new PoolContext("FT_STANDARD", "SHAMAN", "WARRIOR"), PoolTrigger.Hover) is { } hovered)
        {
            Show(hovered);
            foreach (var (name, width, height) in VisualBaseline.Screens)
            {
                var file = name == "16x9" ? "overlay-card-pool-hover.png" : $"overlay-card-pool-hover-{name}.png";
                await _overlay.RenderToFileAsync(Path.Combine(folder, file), width, height, 0);
            }
        }

        // A large pool (Swashburglar, a card from another class), Death Knight runes (from HearthstoneJSON; then the
        // browser with the wiki's second pool, Frost Rune, picked), a pool small enough to list whole, an Excavate treasure.
        foreach (var (cardId, context, name) in new (string?, PoolContext, string)[]
                 {
                     ("CORE_KAR_069", new PoolContext("FT_STANDARD", "ROGUE", "WARRIOR"), "overlay-card-pool-large.png"),
                     ("BE_298", new PoolContext("FT_STANDARD", "DEATHKNIGHT", "WARRIOR"), "overlay-card-pool-runes.png"),
                     (CardPoolScenes.SmallPoolCard(catalog, engine, new PoolContext("FT_STANDARD", "MAGE", "WARRIOR")), new PoolContext("FT_STANDARD", "MAGE", "WARRIOR"),
                         "overlay-card-pool-small.png"),
                     ("WW_001t", new PoolContext("FT_STANDARD", "DEATHKNIGHT", "WARRIOR") { Excavate = new ExcavateState(2, 4) }, "overlay-card-pool-excavate.png"),
                 })
        {
            if (cardId is not null && engine.Build(cardId, context, PoolTrigger.Hover) is { Uncomputed: null } extra)
            {
                Show(extra);
                await _overlay.RenderToFileAsync(Path.Combine(folder, name), 1920, 1080, 0);
                if (extra.Sections.Count > 1 && extra.Excavate is null)
                {
                    _overlayViewModel.OpenCardPoolBrowser.Execute(null);
                    _overlayViewModel.ToggleCardPoolSection.Execute(extra.Sections[1].Label);
                    await RenderBrowserAsync(Path.Combine(folder, "overlay-card-pool-sections.png"));
                    _overlayViewModel.CloseCardPoolBrowser.Execute(null);
                }
            }
            else
            {
                Console.WriteLine($"{Path.GetFileNameWithoutExtension(name)}: no such pool in the catalog, skipped");
            }
        }

        // N'Zoth in hand with two Deathrattle minions summoned: his "this game" list; then the hover card of the list's
        // N'Zoth row with his abilities in a grid beside it (the related cards live in the card preview).
        if (_session.RelatedCardsOf("BE_017") is { Count: > 0 })
        {
            string Name(string id) => _cards.Get(id)?.Name ?? id;
            ShowList(new MatchListView("BE_017", Name("BE_017"), Core.Localization.Loc.T("Pool.List.Summoned"), 2,
                [new MatchListRow("BE_017", Name("BE_017"), 10, "LEGENDARY", 1), new MatchListRow("FP1_001", Name("FP1_001"), 1, "COMMON", 1)]));
            await _overlay.RenderToFileAsync(Path.Combine(folder, "overlay-card-pool-nzoth.png"), 1920, 1080, 0);
            if (_overlay.PreviewHost.ShowFirst(_overlay, e => Controls.CardPreview.GetCardId(e) == "BE_017"))
            {
                await RenderBrowserAsync(Path.Combine(folder, "overlay-card-preview-related.png"));
                _overlay.PreviewHost.Hide();
            }
        }

        _overlayViewModel.UpdateCardPoolKeys(false);

        // A requested pool opens the browser; card renders load after the first layout.
        Show(browse);
        _overlayViewModel.ToggleCardPoolFilters.Execute(null);
        await RenderBrowserAsync(Path.Combine(folder, "overlay-card-pool-browser.png"));
        _overlayViewModel.ToggleCardPoolMechanic.Execute(browse.Filters[0].Tag);
        await RenderBrowserAsync(Path.Combine(folder, "overlay-card-pool-browser-filtered.png"));
        _overlayViewModel.ToggleCardPoolMechanic.Execute(browse.Filters[0].Tag);
        _overlayViewModel.ToggleCardPoolFilters.Execute(null);
        _overlayViewModel.CloseCardPoolBrowser.Execute(null);

        void Show(CardPoolView view, CardPoolAnchor? anchor = null, DiscoverPicksView? picks = null)
        {
            var constructed = new ConstructedView(5, null, false, "HERO_03", "ROGUE", "HERO_01", "WARRIOR", null, [], 0, 0, [], [], 0, 0, 0, null, null)
            {
                CardPool = view,
                CardPoolAnchor = anchor,
                DiscoverPicks = picks,
            };
            var snapshot = LiveSnapshot.Empty with { Scene = "GAMEPLAY", HearthstoneRunning = true, InGame = true, GameType = "GT_RANKED", Constructed = constructed };
            _overlayViewModel.Update(snapshot, _cards, _settings, null, null);
        }

        void ShowList(MatchListView list)
        {
            var constructed = new ConstructedView(5, null, false, "HERO_03", "ROGUE", "HERO_01", "WARRIOR", null, [], 0, 0, [], [], 0, 0, 0, null, null)
            {
                MatchList = list,
            };
            var snapshot = LiveSnapshot.Empty with { Scene = "GAMEPLAY", HearthstoneRunning = true, InGame = true, GameType = "GT_RANKED", Constructed = constructed };
            _overlayViewModel.Update(snapshot, _cards, _settings, null, null);
        }

        async Task RenderBrowserAsync(string path)
        {
            await _overlay.RenderToFileAsync(path, 1920, 1080, 0);
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (CardRender.InFlight > 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(250);
            }

            await _overlay.RenderToFileAsync(path, 1920, 1080, 0);
        }
    }
}
