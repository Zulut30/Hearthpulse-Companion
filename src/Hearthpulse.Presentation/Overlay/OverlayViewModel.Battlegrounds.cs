using System.Globalization;
#if HEARTHPULSE_WPF
using Hearthpulse.App.Infrastructure;
#else
using Hearthpulse.UI.Infrastructure;
#endif
using Hearthpulse.Core;
using Hearthpulse.Core.Api;
using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Tracking;

#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Overlay;
#else
namespace Hearthpulse.UI.Overlay;
#endif

public sealed partial class OverlayViewModel
{
    private void UpdateBattlegrounds(BattlegroundsView view, LiveSnapshot snapshot)
    {
        Turn = view.Turn;
        IsDuos = view.IsDuos;
        Phase = view.IsHeroSelection ? Loc.T("Overlay.Phase.HeroSelection") : view.InCombat ? Loc.T("Overlay.Phase.Combat") : Loc.T("Overlay.Phase.Recruit");
        var anomaly = view.AnomalyCardId is { } a ? _cards.NameOf(a) : string.Empty;
        Anomaly = anomaly;

        var rating = snapshot.Rating is null ? 0 : view.IsDuos ? snapshot.Rating.Duos : snapshot.Rating.Solo;
        Rating = rating > 0 ? rating.ToString("N0", CultureInfo.CurrentCulture) : "—";
        var delta = view.IsDuos ? null : snapshot.Session.Delta;
        RatingDeltaValue = delta ?? 0;
        RatingDelta = delta is { } dv && dv != 0 ? (dv > 0 ? $"+{dv}" : $"−{-dv}") : string.Empty;

        Replace(nameof(Tribes), view.Races, r => r, () =>
        {
            Tribes = view.Races.Select(r => Races.Display(r, _locale)).ToList();
            RebuildTribeChips();
        });
        if (view.CombatOdds is { } odds)
        {
            OddsWin = OddsPercent(odds.Win, odds.Runs);
            OddsTie = OddsPercent(odds.Tie, odds.Runs);
            OddsLoss = OddsPercent(odds.Loss, odds.Runs);
            SetOddsShares(odds.Win, odds.Tie, odds.Loss);
            // The bottom line only when the hero damage cap cuts the range shown: a cap the damage never reaches says
            // nothing, and beside "You take 19-27" it read as a contradiction. Unsimulated effects are in the "≈" tooltip.
            OddsNote = CapReached(odds) ? Loc.F("Overlay.Odds.CapOnly", odds.DamageCap) : string.Empty;
            UpdateOddsEstimate(odds, view.CombatEstimates);

            // Damage as the range of 8 fights in 10 ("You deal 8–16"); the cells' labels say who dies.
            OddsDamageWin = odds.DamageDealt is null ? string.Empty : Loc.F("Overlay.Odds.DamageDealt", CombatOddsText.Damage(odds.DamageDealt));
            OddsDamageWinTip = CombatOddsText.DamageTip(odds.DamageDealt, dealt: true);
            OddsDamageLoss = odds.DamageTaken is null ? string.Empty : Loc.F("Overlay.Odds.DamageTaken", CombatOddsText.Damage(odds.DamageTaken));
            OddsDamageLossTip = CombatOddsText.DamageTip(odds.DamageTaken, dealt: false);
            OddsLethal = odds.LethalTaken is > 0 and var lethal ? OddsPercent(lethal, odds.Runs) : string.Empty;
            OddsKill = odds.LethalDealt is > 0 and var kill ? OddsPercent(kill, odds.Runs) : string.Empty;
            LethalDanger = OddsLethal.Length > 0;
        }
        RebuildTiers();

        // Only "Mark the best" singles an option out; "Stats only" keeps the advisor's numbers but every flag false.
        var highlight = ShowBestBadges;

        // Lobby-adjusted numbers only mean something once the tribes are known.
        var lobbyKnown = view.Races.Count >= 4;
        var heroTierTip = Loc.T("Overlay.Tier.Heroes");
        Replace(nameof(HeroPicks), view.HeroOptions, h => $"{heroTierTip}{lobbyKnown}{h.CardId}{h.Meta?.AdjustedPosition:0.000}{h.Meta?.PickRate:0.000}{highlight && h.Recommended}{string.Join(',', h.TopComps ?? [])}", () => HeroPicks = !view.HeroOptions.Any(h => h.Meta is not null) ? [] : view.HeroOptions
            .Select(h =>
            {
                var adjusted = h.Meta is not null && lobbyKnown;
                var delta = adjusted ? h.Meta!.AdjustedPosition - h.Meta.AveragePosition : 0;
                // The comps line only when the order is this hero's own (BgHeroComp.ByHero): without enough of the hero's
                // games every hero gets the lobby's same two comps, a line repeated on every badge. They stay in the tooltip.
                var comps = h.TopComps ?? [];
                var ownComps = comps.Take(2).Any(c => c.ByHero);
                var compsTip = CompsTip(comps);
                // How often players pick the hero says nothing of its strength: a tooltip line under the tier scale.
                var pickRate = h.Meta is null ? string.Empty : Loc.F("Overlay.Badge.PickRate", h.Meta.PickRate.ToString("P0", CultureInfo.CurrentCulture));
                return new HeroPickCard(
                    h.CardId,
                    h.Name,
                    h.Meta?.Tier ?? "–",
                    h.Meta is null ? "—" : (adjusted ? h.Meta.AdjustedPosition : h.Meta.AveragePosition).ToString("0.00", CultureInfo.CurrentCulture),
                    h.Meta is null ? Loc.T("Overlay.Badge.NoData") : adjusted ? Loc.T("Overlay.Badge.AvgPlaceLobby") : Loc.T("Overlay.Badge.AvgPlace"),
                    Math.Abs(delta) < 0.005 ? string.Empty : delta < 0 ? $"▲{-delta:0.00}" : $"▼{delta:0.00}",
                    -delta,
                    h.Stats is { Games: > 0 } s ? Loc.P("Overlay.Hero.Personal", s.Games, s.AveragePlace) : string.Empty,
                    ownComps ? string.Join(" · ", comps.Take(2).Select(c => c.Name)) : string.Empty,
                    highlight && h.Recommended,
                    h.Meta is not null)
                {
                    TierTip = string.Join("\n\n", new[] { pickRate.Length > 0 ? heroTierTip + "\n" + pickRate : heroTierTip, ownComps ? string.Empty : compsTip }
                        .Where(s => s.Length > 0)),
                    CompsTip = ownComps ? compsTip : string.Empty,
                };
            })
            .ToList());

        var trinketTierTip = Loc.T("Overlay.Tier.Heroes");
        var trinketsLate = _dataHealth.Section(DataSection.BgTrinkets).Delayed;
        Replace(nameof(TrinketPicks), view.TrinketOptions, t => $"{trinketTierTip}{t.CardId}{t.Meta}{highlight && t.Recommended}{trinketsLate}", () => TrinketPicks = !view.TrinketOptions.Any(t => t.Meta is not null) ? [] : view.TrinketOptions
            .Select(t =>
            {
                // The date and an "update delayed" line only while the data API's collector of these numbers is late:
                // fresh numbers need no date on the badge (it is in the tooltip, with the source).
                var delay = DataHealthText.OverlayDelay(t.Meta?.Updated, _dataHealth, t.Meta is { External: true } ? [DataSection.BgTrinkets] : []);
                return new TrinketBadge(
                    t.CardId,
                    t.Name,
                    t.Meta?.Tier ?? "–",
                    t.Meta is null ? "—" : t.Meta.AveragePlacement.ToString("0.00", CultureInfo.CurrentCulture),
                    t.Meta is null ? Loc.T("Overlay.Badge.NoData") : string.Empty,
                    highlight && t.Recommended,
                    t.Meta is not null)
                {
                    TierTip = t.Meta is null
                        ? trinketTierTip
                        : string.Join("\n", trinketTierTip, Loc.F("Overlay.Badge.PickRate", t.Meta.PickRate.ToString("P0", CultureInfo.CurrentCulture)), TrinketSource(t.Meta)),
                    Updated = delay.Length > 0 ? DataHealthText.OverlayDate(t.Meta?.Updated) : string.Empty,
                    Delay = delay,
                };
            })
            .ToList());

        var mode = $"{_pickHints}";

        // One slot per option in screen order, so each badge lands above its own card.
        Replace(nameof(DiscoverBadges), view.DiscoverAdvice, d => $"{mode}{d.Slot}{d.Score}{d.Best}{d.Reason}{d.Placement:0.000}{d.TechLevel}", () =>
        {
            var gifts = view.DiscoverOptions;
            var ranked = view.DiscoverAdvice.OrderByDescending(d => d.Score).Select(d => d.Slot).ToList();
            DiscoverBadges = view.DiscoverAdvice
                .OrderBy(d => d.Slot)
                .Select(d => new DiscoverBadge(
                    highlight ? ranked.IndexOf(d.Slot) + 1 : 0,
                    ShortReason(d.Reason, highlight),
                    d.Slot < gifts.Count ? gifts[d.Slot].DarkGift ?? string.Empty : string.Empty,
                    d.Placement is { } p ? p.ToString("0.00", CultureInfo.CurrentCulture) : "—",
                    d.Placement is null ? Loc.T("Overlay.Badge.NoDataYet") : Loc.T("Overlay.Badge.AvgPlace"),
                    highlight && d.Best,
                    d.Placement is not null,
                    d.TechLevel))
                .ToList();
        });
        // A caption only in "Mark the best", where it says whether one was marked; in "Stats only" it named the source,
        // which the game shows itself.
        var source = view.DiscoverSource ?? Loc.T("Overlay.Discover.Name");
        DiscoverTitle = view.DiscoverAdvice.Count == 0 || !highlight
            ? string.Empty
            : view.DiscoverAdvice.Any(d => d.Best)
                ? Loc.F("Overlay.Discover.BestMarked", source)
                : Loc.F("Overlay.Discover.CloseCall", source);

        UsageNote = view.UsageNote ?? string.Empty;

        var shopSize = view.Shop.Count;
        Replace(nameof(ShopBadges), view.ShopAdvice, s => $"{mode}{shopSize}{s.Slot}{s.Score}{s.Best}{s.Reason}{s.Placement:0.000}", () => ShopBadges = view.ShopAdvice
            .Select(s => (Pick: s, Label: ShortReason(s.Reason, highlight)))
            // "Mark the best" badges the best buy and other strong ones; "Stats only" shows what is known about every minion.
            .Where(x => highlight ? x.Pick.Best || x.Pick.Score >= StrongPick : x.Label.Length > 0 || x.Pick.Placement is not null)
            .Select(x => new ShopBadge(
                ShopRowCenter + ((x.Pick.Slot - ((shopSize - 1) / 2.0)) * ShopMinionPitch) - (ShopBadgeWidth / 2),
                x.Label,
                highlight && x.Pick.Best,
                x.Pick.Placement is { } p ? Loc.F("Overlay.Badge.AvgPlaceValue", p.ToString("0.00", CultureInfo.CurrentCulture)) : string.Empty))
            .ToList());

        if (SelectedTier > 0 && _fingerprints.GetValueOrDefault("BrowserRaces") != string.Join(',', view.Races))
        {
            RebuildBrowser();
        }
    }

    /// <summary>
    /// The tooltip of a hero's comps: one block per comp - name, the source's tier letter and difficulty, then its
    /// advice. Empty without comps.
    /// </summary>
    private static string CompsTip(IReadOnlyList<BgHeroComp>? comps) =>
        comps is not { Count: > 0 }
            ? string.Empty
            : Loc.T("Overlay.Hero.CompsTitle") + "\n" + string.Join('\n', comps.Take(2).Select(c =>
            {
                var facts = new[]
                {
                    c.Tier.Length > 0 ? Loc.F("Overlay.Hero.CompTier", c.Tier) : string.Empty,
                    c.Difficulty switch
                    {
                        "easy" => Loc.T("Main.Bg.CompDifficulty.Easy"),
                        "medium" => Loc.T("Main.Bg.CompDifficulty.Medium"),
                        "hard" => Loc.T("Main.Bg.CompDifficulty.Hard"),
                        _ => c.Difficulty,
                    },
                }.Where(f => f.Length > 0).ToList();
                return "\n" + c.Name + (facts.Count > 0 ? " · " + string.Join(" · ", facts) : string.Empty)
                    + (c.Tip is { Length: > 0 } tip ? "\n" + tip : string.Empty);
            }));

    /// <summary>
    /// Where a trinket's numbers come from: the source by its name (Hearthpulse data, or the stats pool for our own),
    /// its date, and the sample - a lower bound is said to be one, never shown as a count of games.
    /// </summary>
    private static string TrinketSource(TrinketMeta meta)
    {
        var parts = new List<string> { meta.External ? Loc.T("Overlay.Source.External") : Loc.T("Overlay.Source.Pool") };
        if (meta.Updated is { } at)
        {
            parts.Add(Loc.F("Overlay.Source.Updated", at.ToLocalTime()));
        }

        if (meta.DataPoints > 0)
        {
            parts.Add(Loc.P(meta.GamesAreMinimum ? "Overlay.Source.GamesAtLeast" : "Overlay.Source.Games", meta.DataPoints));
        }

        return string.Join(" · ", parts);
    }

    private void UpdateLobbyAndSession(BattlegroundsView view, LiveSnapshot snapshot)
    {
        var rows = view.Opponents
            .Select(o => WithMainTribe(new LobbyRow(
                o.Place,
                o.HeroCardId,
                o.HeroName,
                o.Health + o.Armor,
                o.TechLevel,
                o.Triples,
                // The player's last fight with them, from the player's side: what the player dealt or took.
                o.LastResult switch { "Win" => Loc.F("Overlay.Lobby.Won", o.LastDamage), "Loss" => Loc.F("Overlay.Lobby.Lost", o.LastDamage), "Tie" => Loc.T("Overlay.Lobby.Tie"), _ => string.Empty },
                o.LastResult ?? string.Empty,
                o.IsNextOpponent,
                o.Dead,
                false), o.LastBoard, view.Races))
            .ToList();
        if (view.HeroCardId is { } hero)
        {
            rows.Add(WithMainTribe(new LobbyRow(view.Place, hero, _cards.NameOf(hero), view.Health + view.Armor, view.TechLevel, view.Triples,
                string.Empty, string.Empty, false, view.Health <= 0, true), view.Board, view.Races));
        }

        // In place: a fight changes one or two rows, and the others keep their portraits.
        Replace(nameof(LobbyRows), rows, r => $"{r.Place}{r.HeroCardId}{r.Health}{r.Tier}{r.Triples}{r.Last}{r.Next}{r.Dead}{r.Tribe?.Key}{r.Tribe?.Plural}{r.TribeCount}{r.BoardCount}", () => Sync(ref _lobbyRows, nameof(LobbyRows), rows
            .OrderBy(r => Places.SortKey(r.Place))
            .ToList()));

        UpdateSession(view, snapshot);
    }

    // Tavern row geometry in 1080-unit overlay space (HDT: minion 0.12H plus a 0.0039H margin each side).
    public const double ShopRowWidth = 7 * ShopMinionPitch;
    public const double ShopBadgeWidth = 126;
    private const double ShopMinionPitch = 138;
    private const double ShopRowCenter = ShopRowWidth / 2;
    private const int StrongPick = 40;

    private static readonly string[] ReasonTags = ["Triple", "Pair", "Core", "Add-on", "Cycle", "Fits tribe", "Board synergy"];

    /// <summary>String keys of <see cref="ReasonTags"/> (and "Best stats"), for the badge text.</summary>
    private static readonly Dictionary<string, string> ReasonTagKeys = new(StringComparer.Ordinal)
    {
        ["Triple"] = "Overlay.Tag.Triple",
        ["Pair"] = "Overlay.Tag.Pair",
        ["Core"] = "Overlay.Tag.Core",
        ["Add-on"] = "Overlay.Tag.AddOn",
        ["Cycle"] = "Overlay.Tag.Cycle",
        ["Fits tribe"] = "Overlay.Tag.FitsTribe",
        ["Board synergy"] = "Overlay.Tag.BoardSynergy",
        ["Best stats"] = "Overlay.Tag.BestStats",
    };
    // Tags about a target comp: the overlay shows no comp since the game plan panel went, so they would point at nothing.
    private static readonly string[] CompReasonTags = ["Core", "Add-on", "Cycle", "Fits tribe"];

    /// <summary>
    /// Badge text from the advisor's reason: its tags only, without the comp name or the tags about a comp.
    /// <paramref name="bestStats"/> false drops the "Best stats" fallback.
    /// </summary>
    private static string ShortReason(string reason, bool bestStats)
    {
        var parts = reason.Split(" · ");
        var tags = parts.Where(p => ReasonTags.Contains(p) && !CompReasonTags.Contains(p)).Take(2).ToList();
        if (tags.Count == 0 && bestStats && parts.Contains("Best stats available"))
        {
            tags.Add("Best stats");
        }

        return string.Join(" · ", tags.Select(t => Loc.T(ReasonTagKeys[t])));
    }
}
