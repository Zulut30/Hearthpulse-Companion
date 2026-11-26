using Hearthpulse.Core;
using Hearthpulse.Core.Api;
using Hearthpulse.Core.CardPool;
using Hearthpulse.Core.Constructed;
using Hearthpulse.Core.Hosting;
using Hearthpulse.Core.Tracking;
using Hearthpulse.UI;
using Hearthpulse.UI.Infrastructure;
using Hearthpulse.UI.Overlay;

namespace Hearthpulse.Desktop;

/// <summary>
/// --render-ui: the card pool panel beside the options of a Discover (Blazing Invocation as a Shaman), with the pick hint,
/// and at overlay scale 0.8; then a hand card's pool at the panel's own spot with Ctrl held (the same card on 16:9, 16:10
/// and 4:3 screens, a large pool, Scion of Insight's rune pools, a pool of 12 cards or fewer, an Excavate treasure,
/// N'Zoth's "this game" list), then the pool browser for Thief's Tools with its filters open, and with one filter on.
/// </summary>
internal static class CardPoolRender
{
    /// <param name="save">Saves the 1920x1080 overlay.</param>
    /// <param name="saveSized">Saves the overlay drawn on a screen of the given width and height.</param>
    public static async Task RenderAsync(AppHost host, AppSettings settings, string folder, Func<string, Task> save, Func<string, int, int, Task> saveSized)
    {
        // Frames are captured right after each update: no fade-in.
        CardPoolPanel.Motion = false;
        var catalog = await CardPoolCatalog.LoadAsync(host.Api, host.Http);
        var meta = await ConstructedMeta.LoadAsync(new HearthpulseData(host.Api), host.Cards, settings.MetaFormat, settings.MetaRank, settings.MetaPeriod, host.Cards.Locale);
        var wiki = new WikiPoolSource(new HearthpulseData(host.Api));
        await wiki.LoadAsync("BE_298");
        var engine = new CardPoolEngine(catalog.WithRaces(host.Cards), host.Cards, CardPoolEngine.MetaGames(meta, host.Cards), wiki);
        if (engine.Build("CORE_GIL_836", new PoolContext("FT_STANDARD", "SHAMAN", "WARRIOR"), PoolTrigger.Choice) is not { } pool
            || engine.Build("JAIL_706", new PoolContext("FT_STANDARD", "ROGUE", "WARRIOR"), PoolTrigger.Request) is not { } browse)
        {
            Console.WriteLine("overlay-card-pool: no card catalog, skipped");
            return;
        }

        // Beside the three Discover options; then with the stats pool's picks of those options (sample totals); then at
        // overlay scale 0.8 (the popup is drawn at 0.85 of a game card at least).
        var choice = new CardPoolAnchor(PoolAnchorZone.Choice, 0, 3);
        Show(pool, choice);
        await save(Path.Combine(folder, "overlay-card-pool.png"));
        Show(pool, choice, DiscoverPicks.RenderSample(pool, host.Cards));
        await save(Path.Combine(folder, "overlay-card-pool-picks.png"));
        var scale = settings.OverlayScale;
        settings.OverlayScale = 0.8;
        Show(pool, choice);
        await save(Path.Combine(folder, "overlay-card-pool-scale08.png"));
        settings.OverlayScale = scale;

        // A card in hand with Ctrl held: the panel at its own spot, on the three screen shapes of the visual baselines.
        host.Overlay.UpdateCardPoolKeys(true);
        if (engine.Build("CORE_GIL_836", new PoolContext("FT_STANDARD", "SHAMAN", "WARRIOR"), PoolTrigger.Hover) is { } hovered)
        {
            Show(hovered);
            foreach (var (name, width, height) in VisualBaseline.Screens)
            {
                if (name == "16x9")
                {
                    await save(Path.Combine(folder, "overlay-card-pool-hover.png"));
                }
                else
                {
                    await saveSized(Path.Combine(folder, $"overlay-card-pool-hover-{name}.png"), width, height);
                }
            }
        }

        // A large pool (Swashburglar, a card from another class), Death Knight runes (from HearthstoneJSON; then the
        // browser with the wiki's second pool, Frost Rune, picked), a pool small enough to list whole, an Excavate treasure.
        var mage = new PoolContext("FT_STANDARD", "MAGE", "WARRIOR");
        foreach (var (cardId, context, name) in new (string?, PoolContext, string)[]
                 {
                     ("CORE_KAR_069", new PoolContext("FT_STANDARD", "ROGUE", "WARRIOR"), "overlay-card-pool-large.png"),
                     ("BE_298", new PoolContext("FT_STANDARD", "DEATHKNIGHT", "WARRIOR"), "overlay-card-pool-runes.png"),
                     (CardPoolScenes.SmallPoolCard(catalog, engine, mage), mage, "overlay-card-pool-small.png"),
                     ("WW_001t", new PoolContext("FT_STANDARD", "DEATHKNIGHT", "WARRIOR") { Excavate = new ExcavateState(2, 4) }, "overlay-card-pool-excavate.png"),
                 })
        {
            if (cardId is not null && engine.Build(cardId, context, PoolTrigger.Hover) is { Uncomputed: null } extra)
            {
                Show(extra);
                await save(Path.Combine(folder, name));
                if (extra.Sections.Count > 1 && extra.Excavate is null)
                {
                    host.Overlay.OpenCardPoolBrowser.Execute(null);
                    host.Overlay.ToggleCardPoolSection.Execute(extra.Sections[1].Label);
                    await SaveBrowserAsync(Path.Combine(folder, "overlay-card-pool-sections.png"));
                    host.Overlay.CloseCardPoolBrowser.Execute(null);
                }
            }
            else
            {
                Console.WriteLine($"{Path.GetFileNameWithoutExtension(name)}: no such pool in the catalog, skipped");
            }
        }

        // N'Zoth in hand with two Deathrattle minions summoned: his "this game" list (his abilities are the card preview's).
        if (host.Session.RelatedCardsOf("BE_017") is { Count: > 0 })
        {
            string Name(string id) => host.Cards.Get(id)?.Name ?? id;
            var constructed = new ConstructedView(5, null, false, "HERO_03", "ROGUE", "HERO_01", "WARRIOR", null, [], 0, 0, [], [], 0, 0, 0, null, null)
            {
                MatchList = new MatchListView("BE_017", Name("BE_017"), Hearthpulse.Core.Localization.Loc.T("Pool.List.Summoned"), 2,
                    [new MatchListRow("BE_017", Name("BE_017"), 10, "LEGENDARY", 1), new MatchListRow("FP1_001", Name("FP1_001"), 1, "COMMON", 1)]),
            };
            host.Overlay.Update(LiveSnapshot.Empty with { Scene = "GAMEPLAY", HearthstoneRunning = true, InGame = true, GameType = "GT_RANKED", Constructed = constructed },
                host.Cards, settings, null, null);
            await save(Path.Combine(folder, "overlay-card-pool-nzoth.png"));
        }

        host.Overlay.UpdateCardPoolKeys(false);

        // A requested pool opens the browser; card renders load after the first layout.
        Show(browse);
        host.Overlay.ToggleCardPoolFilters.Execute(null);
        await SaveBrowserAsync(Path.Combine(folder, "overlay-card-pool-browser.png"));
        host.Overlay.ToggleCardPoolMechanic.Execute(browse.Filters[0].Tag);
        await SaveBrowserAsync(Path.Combine(folder, "overlay-card-pool-browser-filtered.png"));
        host.Overlay.ToggleCardPoolMechanic.Execute(browse.Filters[0].Tag);
        host.Overlay.ToggleCardPoolFilters.Execute(null);
        host.Overlay.CloseCardPoolBrowser.Execute(null);

        void Show(CardPoolView view, CardPoolAnchor? anchor = null, DiscoverPicksView? picks = null)
        {
            var constructed = new ConstructedView(5, null, false, "HERO_03", "ROGUE", "HERO_01", "WARRIOR", null, [], 0, 0, [], [], 0, 0, 0, null, null)
            {
                CardPool = view,
                CardPoolAnchor = anchor,
                DiscoverPicks = picks,
            };
            var snapshot = LiveSnapshot.Empty with { Scene = "GAMEPLAY", HearthstoneRunning = true, InGame = true, GameType = "GT_RANKED", Constructed = constructed };
            host.Overlay.Update(snapshot, host.Cards, settings, null, null);
        }

        async Task SaveBrowserAsync(string path)
        {
            await save(path);
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (CardRender.InFlight > 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(250);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            }

            await save(path);
        }
    }
}
