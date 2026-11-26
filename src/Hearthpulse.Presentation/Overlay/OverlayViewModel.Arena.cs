// The draft-screen geometry below (ArenaCardPitch, ArenaHeroPitch, ArenaHeroDrop and the panel margin in the overlay XAML)
// is adapted from HdtArenaHelper's ArenaOverlayWindow (live-tuned anchors for the Arena draft screen):
//
// MIT License
// Copyright (c) 2026 Alessandro Colace
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
// documentation files (the "Software"), to deal in the Software without restriction, including without limitation the
// rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit
// persons to whom the Software is furnished to do so, subject to the following conditions:
// The above copyright notice and this permission notice shall be included in all copies or substantial portions of the
// Software.
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
// WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
// COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

using System.Globalization;
using Hearthpulse.Core;
using Hearthpulse.Core.Arena;
using Hearthpulse.Core.Localization;

#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Overlay;
#else
namespace Hearthpulse.UI.Overlay;
#endif

/// <summary>The badge over one offered draft card (or hero): tier, score (or class win rate), a caption.</summary>
/// <param name="Number">ArenaSmith score ("54") or class win rate ("55.4%"); "—" without numbers.</param>
/// <param name="Tier">Tier letter for the colour, or empty.</param>
/// <param name="Caption">
/// "No score yet", a hero's 7+ wins share (or its class), or empty for a plain score. The best option says nothing more:
/// the star and the gold frame mark it.
/// </param>
/// <param name="FitCost">A hero's place among the classes ("#3 of 11 classes"); empty for a card.</param>
/// <param name="FitType">
/// The package a card brings ("+2 cards with it"); empty otherwise. Its curve bucket and type are left out: the draft
/// panel's curve and types line show the deck.
/// </param>
/// <param name="DeckFit">
/// Our own adjustment for the deck so far, apart from <paramref name="Number"/> (the source's score): "Deck fit: −1.5";
/// empty when nothing applies.
/// </param>
/// <param name="DeckFitWhy">Why: "Many cards at 5 mana · Few minions" (the two strongest reasons); empty with <paramref name="DeckFit"/>.</param>
public sealed record ArenaPickBadge(string CardId, string Number, string Tier, string Caption, bool IsBest, string FitCost = "", string FitType = "",
    string DeckFit = "", string DeckFitWhy = "") : IImmutableView
{
    /// <summary>
    /// Nothing to show (a hero without class numbers): the badge keeps its slot, so the others stay over their own
    /// cards, and draws nothing - a blank seal and a dash read as a broken picture.
    /// </summary>
    public bool Empty { get; init; }
}

/// <summary>One bar of the draft's mana curve.</summary>
/// <param name="Offered">An offered card costs this much: the bar is highlighted.</param>
public sealed record ArenaCurveBar(string Label, int Count, double Height, bool Offered = false) : IImmutableView;

/// <summary>One distinct card of the deck so far.</summary>
public sealed record ArenaDeckRow(string CardId, string Cost, string Name, string Copies, string Score, string Tier) : IImmutableView;

/// <summary>One figure of the run's class under the panel title: "55.2%" over "win rate".</summary>
/// <param name="Tier">Tier letter for the value's colour, or empty for plain text.</param>
public sealed record ArenaStat(string Value, string Label, string Tier) : IImmutableView;

/// <summary>A class in the hero-pick list: its community win rate and 7+ share.</summary>
public sealed record ArenaClassRow(string Name, string WinRate, string SevenPlus, string Tier, bool Offered) : IImmutableView;

/// <summary>
/// The Arena draft helper (ArenaPicksPanel, ArenaDraftPanel): a badge over each offered card with its ArenaSmith score
/// (class win rate at the hero pick), the best one marked, and a panel with the deck so far, its curve and average.
/// Up while the draft screen is open; nothing on it takes clicks, so the cards stay clickable.
/// </summary>
public sealed partial class OverlayViewModel
{
    // HdtArenaHelper lays the offer out on the 4:3 safe area (1440 wide on a 1080-high screen), centred on its left
    // 3.25/4.25 (the client's deck list owns the rest): cards 0.26 of that width apart, heroes 0.29, heroes 0.05 of the
    // height lower. The panel's default margin in the XAML (right 339 = twice the left shift of that centre) comes from it too.
    private const double ArenaPlayWidth = 1080 * 4.0 / 3 * 3.25 / 4.25;

    /// <summary>Distance between two offered cards on the draft screen, in units of a 1080-high screen.</summary>
    public const double ArenaCardPitch = ArenaPlayWidth * 0.26;

    /// <summary>Distance between two offered heroes.</summary>
    public const double ArenaHeroPitch = ArenaPlayWidth * 0.29;

    /// <summary>How much lower the hero badges sit than the card badges.</summary>
    public const double ArenaHeroDrop = 1080 * 0.05;

    private const double ArenaCurveHeight = 34;

    private bool _showArenaPicks;
    private bool _showArenaDraft;
    private IReadOnlyList<ArenaPickBadge> _arenaPicks = [];
    private double _arenaPickRowWidth = ArenaCardPitch * 3;
    private double _arenaPickDrop;
    private string _arenaDraftTitle = string.Empty;
    private string _arenaDraftPicks = string.Empty;
    private string _arenaAverage = "—";
    private string _arenaAverageTier = string.Empty;
    private IReadOnlyList<ArenaCurveBar> _arenaCurve = [];
    private IReadOnlyList<ArenaDeckRow> _arenaDeck = [];
    private IReadOnlyList<ArenaClassRow> _arenaClasses = [];
    private string _arenaNoData = string.Empty;
    private string _arenaMode = string.Empty;
    private string _arenaTypes = string.Empty;
    private string _arenaSliceNote = string.Empty;
    private string _arenaTierLegend = string.Empty;
    private IReadOnlyList<ArenaStat> _arenaClassStats = [];
    private bool _arenaHasPicks;
    private string? _arenaPrint;

    public bool ShowArenaPicks { get => _showArenaPicks; private set => Set(ref _showArenaPicks, value); }

    public bool ShowArenaDraft { get => _showArenaDraft; private set => Set(ref _showArenaDraft, value); }

    public IReadOnlyList<ArenaPickBadge> ArenaPicks { get => _arenaPicks; private set => Set(ref _arenaPicks, value); }

    /// <summary>
    /// The badge row in overlay units: the game's card row whatever the player's overlay scale (which only sizes the
    /// badges), so each badge stays over its card. Wider for heroes.
    /// </summary>
    public double ArenaPickRowWidth { get => _arenaPickRowWidth; private set => Set(ref _arenaPickRowWidth, value); }

    /// <summary>Space above the badges: the hero portraits sit lower than the cards.</summary>
    public double ArenaPickDrop { get => _arenaPickDrop; private set => Set(ref _arenaPickDrop, value); }

    /// <summary>The run's class, or "Choose a hero".</summary>
    public string ArenaDraftTitle { get => _arenaDraftTitle; private set => Set(ref _arenaDraftTitle, value); }

    /// <summary>"12/30".</summary>
    public string ArenaDraftPicks { get => _arenaDraftPicks; private set => Set(ref _arenaDraftPicks, value); }

    public string ArenaAverage { get => _arenaAverage; private set => Set(ref _arenaAverage, value); }

    /// <summary>At least one card is picked: the average and the curve are shown.</summary>
    public bool ArenaHasPicks { get => _arenaHasPicks; private set => Set(ref _arenaHasPicks, value); }

    public string ArenaAverageTier { get => _arenaAverageTier; private set => Set(ref _arenaAverageTier, value); }

    public IReadOnlyList<ArenaCurveBar> ArenaCurve { get => _arenaCurve; private set => Set(ref _arenaCurve, value); }

    public IReadOnlyList<ArenaDeckRow> ArenaDeck { get => _arenaDeck; private set => Set(ref _arenaDeck, value); }

    /// <summary>Classes with community win rates; shown at the hero pick.</summary>
    public IReadOnlyList<ArenaClassRow> ArenaClasses { get => _arenaClasses; private set => Set(ref _arenaClasses, value); }

    /// <summary>Why there are no numbers (server and cache empty), else empty.</summary>
    public string ArenaNoData
    {
        get => _arenaNoData;
        private set
        {
            if (Set(ref _arenaNoData, value))
            {
                Raise(nameof(ArenaDataNote));
            }
        }
    }

    /// <summary>"Underground Arena" or "Normal Arena" once the client told the run's mode, else empty.</summary>
    public string ArenaMode { get => _arenaMode; private set => Set(ref _arenaMode, value); }

    /// <summary>Card types of the deck so far: "12 minions · 5 spells · 1 weapon"; empty before the first card.</summary>
    public string ArenaTypes { get => _arenaTypes; private set => Set(ref _arenaTypes, value); }

    /// <summary>"from Underground Arena games" when the run is the other mode, else empty; shown in the header and its tip.</summary>
    public string ArenaSliceNote { get => _arenaSliceNote; private set => Set(ref _arenaSliceNote, value); }

    /// <summary>What the tier letters on screen mean: the class scale at the hero pick, the card scale after it.</summary>
    public string ArenaTierLegend { get => _arenaTierLegend; private set => Set(ref _arenaTierLegend, value); }

    /// <summary>
    /// The run's class results after the hero pick as three short stats (win rate, place, 7+ wins), so the line never
    /// wraps in the middle of a number; empty before the hero is known.
    /// </summary>
    public IReadOnlyList<ArenaStat> ArenaClassStats { get => _arenaClassStats; private set => Set(ref _arenaClassStats, value); }

    private void UpdateArena(ArenaDraftView? draft, AppSettings settings)
    {
        var shown = settings.OverlayEnabled && settings.ShowArenaDraft && draft is not null;
        ShowArenaDraft = shown;
        // Hero powers have no numbers: their pick shows only the panel.
        ShowArenaPicks = shown && draft!.Options.Count > 0 && draft.Phase is ArenaDraftPhase.Hero or ArenaDraftPhase.Cards;
        if (!shown)
        {
            _arenaPrint = null;
            return;
        }

        var scale = SurfaceScale(settings);
        ArenaPickRowWidth = (draft!.HeroPick ? ArenaHeroPitch : ArenaCardPitch) * Math.Max(draft.Options.Count, 1) / scale;
        ArenaPickDrop = draft.HeroPick ? ArenaHeroDrop / scale : 0;

        // Snapshots arrive every 100 ms while anything changes; rebuild only when what the panels show changed.
        var print = $"{Loc.Instance.Language}|{draft.PlayerClass}|{draft.HasData}|{draft.Score.Average:0.00}|{draft.Phase}|{draft.Underground}|{draft.ScoresFromOtherMode}|"
                    + string.Join(',', draft.Options.Select(o => $"{o.CardId}:{o.Score}:{o.WinRate}:{o.IsBest}:{o.Package?.Count}:{FitPrint(o.Fit)}")) + "|"
                    + string.Join(',', draft.Deck.Select(c => $"{c.CardId}:{c.Count}:{c.Score}")) + "|"
                    + string.Join(',', draft.Classes.Select(c => $"{c.PlayerClass}:{c.WinRate}:{c.Offered}")) + "|"
                    + $"{draft.RunClass?.WinRate}:{draft.RunClass?.SevenPlus}:{draft.RunClass?.Rank}";
        if (print == _arenaPrint)
        {
            ShowArenaPicks &= ArenaPicks.Any(b => !b.Empty);
            return;
        }

        _arenaPrint = print;
        var culture = CultureInfo.CurrentCulture;
        ArenaPicks = draft.Phase is ArenaDraftPhase.Hero or ArenaDraftPhase.Cards
            ? draft.Options.Select(o => draft.HeroPick ? HeroBadge(o, draft, culture) : CardBadge(o, culture)).ToList()
            : [];
        ShowArenaPicks &= ArenaPicks.Any(b => !b.Empty);

        // The hero power is picked after the hero, so the class is known by then: the pick still gets its own title.
        ArenaDraftTitle = draft.Phase == ArenaDraftPhase.HeroPower ? Loc.T("Arena.ChooseHeroPower")
            : draft.PlayerClass is { } cls ? ClassName(cls)
            : Loc.T("Arena.ChooseHero");
        ArenaDraftPicks = $"{draft.Picks}/{ArenaDraft.DeckSize}";
        ArenaMode = draft.Underground switch
        {
            true => Loc.T("Arena.Mode.Underground"),
            false => Loc.T("Arena.Mode.Normal"),
            null => string.Empty,
        };
        ArenaSliceNote = draft.ScoresFromOtherMode
            ? Loc.T(draft.Underground == true ? "Arena.Slice.Normal" : "Arena.Slice.Underground")
            : string.Empty;
        ArenaAverage = draft.Score.Average?.ToString("0.0", culture) ?? "—";
        ArenaHasPicks = draft.Picks > 0;
        ArenaAverageTier = draft.Score.Tier ?? string.Empty;
        var most = Math.Max(1, draft.Score.Curve.Max());
        var offeredCosts = draft.Phase == ArenaDraftPhase.Cards
            ? draft.Options.Where(o => o.Cost is not null).Select(o => ArenaDraft.Bucket(o.Cost!.Value)).ToHashSet()
            : [];
        ArenaCurve = draft.Score.Curve.Select((count, cost) => new ArenaCurveBar(
                cost == ArenaDraft.CurveBuckets - 1 ? $"{cost}+" : cost.ToString(culture),
                count,
                count == 0 ? 2 : Math.Max(4, ArenaCurveHeight * count / most),
                offeredCosts.Contains(cost)))
            .ToList();
        ArenaTypes = TypesLine(draft.Types ?? ArenaDeckTypes.Empty);
        ArenaDeck = draft.Deck.Select(c => new ArenaDeckRow(
                c.CardId,
                c.Cost.ToString(culture),
                c.Name,
                c.Count > 1 ? "×" + c.Count.ToString(culture) : string.Empty,
                c.Score?.ToString("0", culture) ?? "—",
                c.Tier ?? string.Empty))
            .ToList();
        ArenaClasses = draft.Classes.Select(c => new ArenaClassRow(
                ClassName(c.PlayerClass),
                c.WinRate.ToString("P1", culture),
                c.SevenPlus is { } seven ? Loc.F("Arena.SevenPlusShort", seven.ToString("P0", culture)) : string.Empty,
                ArenaDraft.TierOfWinRate(c.WinRate) ?? string.Empty,
                c.Offered))
            .ToList();
        ArenaClassStats = draft.RunClass is { } run
            ? new[]
            {
                new ArenaStat(OverlayFormat.Percent(run.WinRate, 1), Loc.T("Arena.Stat.WinRate"), ArenaDraft.TierOfWinRate(run.WinRate) ?? string.Empty),
                new ArenaStat(Loc.F("Arena.Stat.PlaceValue", run.Rank, run.Of), Loc.T("Arena.Stat.Place"), string.Empty),
                run.SevenPlus is { } runSeven ? new ArenaStat(OverlayFormat.Percent(runSeven), Loc.T("Arena.Stat.SevenPlus"), string.Empty) : null,
            }.OfType<ArenaStat>().ToList()
            : [];
        ArenaNoData = draft.HasData ? string.Empty : Loc.T("Arena.NoData");
        ArenaTierLegend = !draft.HasData ? string.Empty
            : draft.HeroPick ? Loc.T("Overlay.Tier.ArenaClasses")
            : Loc.T("Overlay.Tier.ArenaCards");
    }

    /// <summary>
    /// A hero: class win rate, its place among the classes and the 7+ wins share. Without a win rate the badge is
    /// <see cref="ArenaPickBadge.Empty"/>: the draft panel already says why the numbers are missing.
    /// </summary>
    private static ArenaPickBadge HeroBadge(ArenaPickOption o, ArenaDraftView draft, CultureInfo culture)
    {
        if (o.WinRate is not { } winRate)
        {
            return new ArenaPickBadge(o.CardId, "—", string.Empty, string.Empty, false) { Empty = true };
        }

        var seven = draft.Classes.FirstOrDefault(c => c.PlayerClass == o.PlayerClass)?.SevenPlus;
        var caption = seven is { } s ? Loc.F("Arena.SevenPlus", s.ToString("P0", culture)) : ClassName(o.PlayerClass);
        var rank = o.ClassRank is { } place && draft.Classes.Count > 0 ? Loc.F("Arena.ClassRank", place, draft.Classes.Count) : string.Empty;
        return new ArenaPickBadge(o.CardId, winRate.ToString("P1", culture), o.Tier ?? string.Empty, caption, o.IsBest, rank);
    }

    /// <summary>A card: its score, the package it brings, and our adjustment for the deck so far.</summary>
    private static ArenaPickBadge CardBadge(ArenaPickOption o, CultureInfo culture)
    {
        // The number is the score: only a card without one gets a caption.
        var caption = o.Score is null ? Loc.T("Arena.NoScore") : string.Empty;
        var package = o.Package is { Count: > 0 } cards ? Loc.P("Arena.Package", cards.Count) : string.Empty;
        var (fit, why) = DeckFit(o.Fit, culture);
        return new ArenaPickBadge(o.CardId, o.Score?.ToString("0", culture) ?? "—", o.Tier ?? string.Empty, caption, o.IsBest, string.Empty, package, fit, why);
    }

    /// <summary>How many reasons the badge names: the strongest ones, so the line stays short.</summary>
    private const int ArenaFitReasons = 2;

    /// <summary>
    /// Our adjustment as its own line ("Deck fit: −1.5") and the reasons under it. The source's score stays the badge's
    /// number; an adjustment that rounds to nothing (reasons that cancel out) shows no line.
    /// </summary>
    private static (string Fit, string Why) DeckFit(ArenaFit? fit, CultureInfo culture)
    {
        if (fit is null || fit.Parts.Count == 0)
        {
            return (string.Empty, string.Empty);
        }

        // This is a hand-set rule, not a measured gain. Show coarse half-point steps, with no signed zero.
        var shown = Math.Round(fit.Points * 2, MidpointRounding.AwayFromZero) / 2;
        if (shown == 0)
        {
            return (string.Empty, string.Empty);
        }

        var number = (shown > 0 ? "+" : "−") + Math.Abs(shown).ToString("0.#", culture);
        var why = string.Join(" · ", fit.Parts.Take(ArenaFitReasons).Select(p => p.Reason switch
        {
            ArenaFitReason.CurveCrowded => Loc.F("Arena.DeckFit.CurveCrowded", SlotLabel(p.Slot, culture)),
            ArenaFitReason.CurveThin => Loc.F("Arena.DeckFit.CurveThin", SlotLabel(p.Slot, culture)),
            ArenaFitReason.Copies => Loc.F("Arena.DeckFit.Copies", p.Count),
            ArenaFitReason.FewMinions => Loc.T("Arena.DeckFit.FewMinions"),
            ArenaFitReason.FewEarlyMinions => Loc.T("Arena.DeckFit.FewEarlyMinions"),
            ArenaFitReason.ManySpells => Loc.T("Arena.DeckFit.ManySpells"),
            ArenaFitReason.ManyWeapons => Loc.T("Arena.DeckFit.ManyWeapons"),
            _ => string.Empty,
        }).Where(text => text.Length > 0));
        return (Loc.F("Arena.DeckFit", number), why);
    }

    /// <summary>"0–1", "2" ... "6", "7+": the cost slots of the target curve.</summary>
    private static string SlotLabel(int slot, CultureInfo culture) =>
        slot <= 0 ? "0–1"
        : slot >= ArenaDeckFit.Slots - 1 ? (slot + 1).ToString(culture) + "+"
        : (slot + 1).ToString(culture);

    private static string FitPrint(ArenaFit? fit) =>
        fit is null ? string.Empty : fit.Points.ToString("0.00", CultureInfo.InvariantCulture) + "/" + string.Join('+', fit.Parts.Select(p => $"{p.Reason}{p.Slot}{p.Count}"));

    /// <summary>"12 minions · 5 spells · 1 weapon": the types the deck has, empty before the first card.</summary>
    private static string TypesLine(ArenaDeckTypes types)
    {
        var parts = new List<string>(4);
        if (types.Minions > 0) parts.Add(Loc.P("Arena.Types.Minions", types.Minions));
        if (types.Spells > 0) parts.Add(Loc.P("Arena.Types.Spells", types.Spells));
        if (types.Weapons > 0) parts.Add(Loc.P("Arena.Types.Weapons", types.Weapons));
        if (types.Locations > 0) parts.Add(Loc.P("Arena.Types.Locations", types.Locations));
        return string.Join(" · ", parts);
    }
}
