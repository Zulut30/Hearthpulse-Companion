using System.Globalization;
#if HEARTHPULSE_WPF
using Hearthpulse.App.Infrastructure;
#else
using Hearthpulse.UI.Infrastructure;
#endif
using Hearthpulse.Core;
using Hearthpulse.Core.Constructed;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Tracking;

#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Overlay;
#else
namespace Hearthpulse.UI.Overlay;
#endif

/// <summary>
/// The bar over one offered card: the guide's call, a gauge from "replace" to "keep" with the edge in points, and flags;
/// the keep rate, the win rate when drawn and the rest in the tooltip.
/// </summary>
/// <param name="Call">"Keep", "Replace" or "No data": what the guide recommends.</param>
/// <param name="Number">What keeping is worth against replacing, in points ("+3.1"); empty without numbers.</param>
/// <param name="Tier">The keep tier of that edge (<see cref="OverlayTiers.Keep"/>), S to D; empty without numbers.</param>
/// <param name="Good">The edge is at least <see cref="MulliganGuide.CloseCall"/>; <paramref name="Bad"/> the same below 0.</param>
/// <param name="FillLeft">Width of the gauge's fill left of its middle (a card to replace), in overlay units.</param>
/// <param name="FillRight">Width of the fill right of the middle (a card to keep).</param>
/// <param name="Kept">The player's current choice (starts as the recommendation; a click toggles it).</param>
/// <param name="Duplicate">A second copy (counts once, heuristic).</param>
/// <param name="Late">Usually played late (curve rule).</param>
/// <param name="Soft">The call comes from the cards' general results, not the deck's mulligan stats (Arena): "keep" in
/// the accent colour, "replace" muted, instead of green and red.</param>
public sealed record MulliganPlate(
    int EntityId,
    string Name,
    string Call,
    bool KeepCall,
    bool MulliganCall,
    string Number,
    string Tier,
    bool Good,
    bool Bad,
    double FillLeft,
    double FillRight,
    bool Synergy,
    bool Duplicate,
    bool Late,
    bool Kept,
    string Tooltip,
    bool Soft = false) : IImmutableView
{
    /// <summary>Neither keep nor replace: no numbers for the card.</summary>
    public bool NoCall => !KeepCall && !MulliganCall;

    /// <summary>The bar leads with the keep tier letter instead of the call's icon.</summary>
    public bool HasTier => Tier.Length > 0;

    /// <summary>A close call: the edge is within half a point either way (drawn neutral).</summary>
    public bool Close => !NoCall && !Good && !Bad;
}

/// <summary>
/// The mulligan bars (MulliganPanel.xaml): a bar over each offered card, lined up with the cards of the mulligan screen,
/// under one summary line. It is up while the local MULLIGAN_STATE is INPUT or DEALING (gone at WAITING), with or without numbers.
/// </summary>
public sealed partial class OverlayViewModel
{
    /// <summary>Width of the row of offered cards on the mulligan screen, in units of a 1080-high screen: 3 cards 340 apart, 4 cards 255.</summary>
    public const double MulliganRowGameWidth = 1020;

    /// <summary>
    /// Top of the summary panel (<c>mulligan-summary</c>) on the mulligan screen, in units of a 1080-high screen: the
    /// default margin of both overlay files. The panel is anchored to the game, so its top stays here at any overlay
    /// scale and only its height grows with the scale.
    /// </summary>
    public const double MulliganSummaryTop = 700;

    /// <summary>
    /// Where the Confirm button's zone begins on the mulligan screen, in the same units (the button is drawn at about
    /// 815-885; an estimate from the screen's layout, not a measured frame). The one-line summary must end above it at the
    /// largest overlay scale.
    /// </summary>
    public const double MulliganConfirmTop = 800;

    /// <summary>Half the gauge's track, in overlay units (MulliganPanel's track is twice this).</summary>
    public const double MulliganGaugeHalf = 38;

    /// <summary>An edge of this many win rate points fills half the gauge.</summary>
    private const double MulliganGaugeFull = 0.04;

    private double _mulliganRowWidth = MulliganRowGameWidth;

    private bool _showMulligan;
    private bool _showMulliganBars;
    private IReadOnlyList<MulliganPlate> _mulliganCards = [];
    private string _mulliganSummary = string.Empty;
    private string _mulliganSource = string.Empty;
    private string _mulliganScopeLine = string.Empty;
    private string _mulliganDetails = string.Empty;
    private bool _mulliganImpactGood;
    private bool _mulliganImpactBad;
    private bool _mulliganHasWarnings;
    private MulliganView? _mulligan;
    private string? _mulliganHand;
    private string? _mulliganPrint;
    private bool _mulliganTouched;
    private bool _mulliganOffNoted;
    private readonly HashSet<int> _mulliganKept = new();
    private RelayCommand? _toggleMulliganCard;

    public bool ShowMulligan { get => _showMulligan; private set => Set(ref _showMulligan, value); }

    /// <summary>
    /// The bars over the cards, only with numbers to show: without them the summary says so once ("No mulligan stats for
    /// this deck yet"), where a "No data" bar over every card said it three times.
    /// </summary>
    public bool ShowMulliganBars { get => _showMulliganBars; private set => Set(ref _showMulliganBars, value); }

    /// <summary>
    /// The strip's row in overlay units: the game's card row, whatever the player's overlay scale (which only sizes the
    /// badges), so each badge stays under its card.
    /// </summary>
    public double MulliganRowWidth { get => _mulliganRowWidth; private set => Set(ref _mulliganRowWidth, value); }

    public IReadOnlyList<MulliganPlate> MulliganCards { get => _mulliganCards; private set => Set(ref _mulliganCards, value); }

    /// <summary>"Expected impact of your keeps: +2.1%", or why there are no numbers.</summary>
    public string MulliganSummary { get => _mulliganSummary; private set => Set(ref _mulliganSummary, value); }

    /// <summary>"Egg Death Knight · HSReplay Legend EU · on the coin", "Arena · card win rate when drawn" (small, under the summary).</summary>
    public string MulliganSource { get => _mulliganSource; private set => Set(ref _mulliganSource, value); }

    /// <summary>
    /// Where a decklist's numbers come from, not an archetype's: "By your deck, 26,946 games, data from 14.11.2026" or
    /// "By a close deck, ..."; empty for an archetype's numbers. A line of the summary's tooltip, after the source.
    /// </summary>
    public string MulliganScopeLine { get => _mulliganScopeLine; private set => Set(ref _mulliganScopeLine, value); }

    /// <summary>The summary's tooltip: average hand, full mulligan, recommended keeps, curve and synergy notes.</summary>
    public string MulliganDetails { get => _mulliganDetails; private set => Set(ref _mulliganDetails, value); }

    public bool MulliganImpactGood { get => _mulliganImpactGood; private set => Set(ref _mulliganImpactGood, value); }

    public bool MulliganImpactBad { get => _mulliganImpactBad; private set => Set(ref _mulliganImpactBad, value); }

    /// <summary>A curve or synergy remark is in <see cref="MulliganDetails"/> (an info mark next to the summary).</summary>
    public bool MulliganHasWarnings { get => _mulliganHasWarnings; private set => Set(ref _mulliganHasWarnings, value); }

    /// <summary>Click a badge: mark the card as kept or replaced; the summary follows.</summary>
    public RelayCommand ToggleMulliganCard => _toggleMulliganCard ??= new RelayCommand(p =>
    {
        if (p is int id && !_mulliganKept.Remove(id))
        {
            _mulliganKept.Add(id);
        }

        _mulliganTouched = true;

        RebuildMulligan();
    });

    private void UpdateMulligan(MulliganView? mulligan, AppSettings settings)
    {
        ShowMulligan = settings.ShowMulligan && mulligan is not null;
        MulliganRowWidth = MulliganRowGameWidth / SurfaceScale(settings);
        // The tracker logs why the strip has or lacks numbers; only the setting is known here.
        var offBySetting = mulligan is not null && !settings.ShowMulligan;
        if (offBySetting && !_mulliganOffNoted)
        {
            Diag.Info("Mulligan panel hidden: \"Mulligan stats over your opening hand\" is off in Settings");
        }

        _mulliganOffNoted = offBySetting;
        _mulligan = mulligan;
        if (mulligan is null)
        {
            _mulliganHand = null;
            _mulliganPrint = null;
            ShowMulliganBars = false;
            return;
        }

        // A new hand starts from the recommendation. The player's clicks last for the whole hand, also when the numbers
        // arrive from another source mid-mulligan; untouched keeps follow the newest recommendation.
        var hand = string.Join(',', mulligan.Cards.Select(c => $"{c.EntityId}:{c.CardId}"));
        if (hand != _mulliganHand)
        {
            _mulliganHand = hand;
            _mulliganTouched = false;
        }

        // Without numbers there is no advice: every card starts kept, so no badge looks like "replace it".
        var recommended = mulligan.NoStats is null ? mulligan.Recommended : mulligan.Cards.Select(c => c.EntityId).ToList();
        if (!_mulliganTouched && !_mulliganKept.SetEquals(recommended))
        {
            _mulliganKept.Clear();
            _mulliganKept.UnionWith(recommended);
        }

        RebuildMulligan();
        ShowMulliganBars = ShowMulligan && MulliganCards.Count > 0;
    }

    private void RebuildMulligan()
    {
        if (_mulligan is not { } view)
        {
            return;
        }

        // Snapshots arrive every 100 ms while anything changes; rebuild the badges only when what they show changed. The
        // language is part of it: after a switch the host pushes the last snapshot again and the texts are made anew.
        var print = $"{Loc.Instance.Language}|{view.Context}|{view.Source}|{view.NoStats}|{view.BaseWinRate:0.0000}|{view.ReplaceDelta:0.0000}|{view.OpponentClass}|{view.ClassShift:0.0000}|{view.GoingFirst}|{view.SliceRank}|{view.ArenaBasis}|{view.Scope}|{view.DeckGames}|{view.DataAsOf:O}|{view.DeckDifference}|"
                    + $"{string.Join(',', _mulliganKept.Order())}|"
                    + string.Join(';', view.Cards.Select(c => $"{c.EntityId}|{c.CardId}|{c.Call}|{c.Delta:0.0000}|{c.Edge:0.0000}|{c.KeepRate:0.0000}|{c.KeptDelta:0.0000}|{c.DrawnDelta:0.0000}"));
        if (print == _mulliganPrint)
        {
            return;
        }

        _mulliganPrint = print;
        var culture = CultureInfo.CurrentCulture;
        var initiative = view.GoingFirst switch { true => Loc.T("Mulligan.GoingFirst"), false => Loc.T("Mulligan.OnTheCoin"), null => null };
        if (view.NoStats is { } missing)
        {
            MulliganSummary = missing;
            MulliganSource = initiative ?? string.Empty;
            MulliganScopeLine = string.Empty;
            // The source line heads the tooltip: the strip itself carries only the impact.
            MulliganDetails = string.Join('\n', new[] { MulliganSource, Loc.T("Mulligan.NoNumbers") }.Where(s => s.Length > 0));
            MulliganImpactGood = MulliganImpactBad = MulliganHasWarnings = false;
        }
        else
        {
            var impact = MulliganGuide.Evaluate(view, _mulliganKept);
            var vsAverage = impact.Expected - view.BaseWinRate;
            var vsClass = view.OpponentClass is { } opponent ? Loc.F("Mulligan.VsClass", ClassName(opponent)) : string.Empty;
            MulliganSummary = Loc.F("Mulligan.Impact", OverlayFormat.SignedPercent(vsAverage));
            // Archetype names come from the API in English.
            var context = string.IsNullOrEmpty(view.Context) ? view.Context : ArchetypeNames.Display(view.Context);
            // A source that already names the rank ("Hearthpulse data, Legend, 7 days") is not followed by "slice: Legend".
            var slices = view.SliceRank is { } rank && view.Source?.Contains(SliceRankName(rank), StringComparison.CurrentCultureIgnoreCase) != true
                ? Loc.F("Mulligan.SliceSource", SliceRankName(rank))
                : null;
            MulliganSource = string.Join(" · ", new[] { context, view.Source, slices, initiative }.Where(s => !string.IsNullOrEmpty(s)));
            MulliganScopeLine = ScopeLine(view, culture);
            var notes = impact.Warnings
                .Concat(impact.Synergies.Select(s => Loc.F("Mulligan.SynergyKept", s.NameA, s.NameB, Points(s.Interaction), Margin(s.Margin), s.Games)))
                .ToList();
            MulliganDetails = string.Join('\n', new[]
            {
                view.ArenaBasis is null ? string.Empty : Loc.T("Mulligan.Arena.Note"),
                MulliganSource,
                MulliganScopeLine,
                view.Scope == MulliganScope.NearDeck ? Loc.P("Mulligan.Scope.NearNote", view.DeckDifference) : string.Empty,
                Loc.F("Mulligan.AverageHand", vsClass + (initiative is null ? string.Empty : " " + initiative), view.BaseWinRate.ToString("P1", culture)),
                Loc.F("Mulligan.YourKeeps", impact.Kept, view.Cards.Count, impact.Expected.ToString("P1", culture)),
                Loc.F("Mulligan.FullMulligan", impact.FullMulligan.ToString("P1", culture), impact.Recommended.ToString("P1", culture)),
            }.Concat(notes).Append(view.HasPairData || view.ArenaBasis is not null ? string.Empty : Loc.T("Mulligan.NoPairData"))
             .Append(Loc.T("Overlay.Tier.KeepLegend"))
             .Where(s => s.Length > 0));
            MulliganImpactGood = impact.Expected >= impact.Recommended - OverlayFormat.HalfStep && vsAverage > OverlayFormat.HalfStep;
            MulliganImpactBad = impact.Expected < impact.Recommended - 0.005;
            MulliganHasWarnings = notes.Count > 0;
        }

        if (view.NoStats is not null)
        {
            MulliganCards = [];
            return;
        }

        MulliganCards = view.Cards.Select(c =>
        {
            var kept = _mulliganKept.Contains(c.EntityId);
            var synergy = view.Synergies.FirstOrDefault(s => s.EntityA == c.EntityId || s.EntityB == c.EntityId);
            var late = MulliganGuide.IsLate(c);
            var edge = c.Edge ?? 0;
            var fill = c.Edge is null ? 0 : Math.Min(1, Math.Abs(edge) / MulliganGaugeFull) * MulliganGaugeHalf;
            var arena = view.ArenaBasis is not null;
            var tier = OverlayTiers.Keep(c.Edge) ?? string.Empty;
            var lines = new List<string> { c.Name };
            if (tier.Length > 0)
            {
                lines.Add(Loc.F("Overlay.Tier.Keep", tier));
            }
            if (c.WinRate is { } rate && c.Delta is { } delta)
            {
                if (c.Edge is not null)
                {
                    lines.Add(Loc.F(arena ? "Mulligan.Arena.EdgeLine" : "Mulligan.EdgeLine", Points(edge), Points(delta), Points(view.ReplaceDelta)));
                    if (Math.Abs(edge - (delta - view.ReplaceDelta)) > OverlayFormat.HalfStep)
                    {
                        // The other copy changes what keeping this one adds.
                        lines.Add(Loc.T("Mulligan.EdgeWithOthers"));
                    }
                }

                if (view.ArenaBasis is { } basis)
                {
                    // Arena: the card's own results, no keep rate and no turn played.
                    lines.Add(Loc.P("Mulligan.Arena.CardWinRate", c.Sample, rate.ToString("P1", culture), view.CardBaseline.ToString("P1", culture),
                        Loc.T("Mulligan.Arena.Basis." + basis)));
                }
                else
                {
                    // A deck's slice has no per-card counts (its sample is the deck's games, on the source line).
                    lines.Add(view.Scope == MulliganScope.Archetype
                        ? Loc.P("Mulligan.CardWinRate", c.Sample, rate.ToString("P1", culture), view.CardBaseline.ToString("P1", culture))
                        : Loc.F("Mulligan.Deck.CardWinRate", rate.ToString("P1", culture), view.CardBaseline.ToString("P1", culture)));
                    lines.Add(Loc.F("Mulligan.KeptBy", c.KeepRate?.ToString("P0", culture), c.ExpectedTurn.ToString("0.#", culture)));
                }
                if (c.CoinShift is { } coinShift)
                {
                    lines.Add(Loc.F(view.GoingFirst == true ? "Mulligan.Shift.First" : "Mulligan.Shift.Coin", Points(coinShift)));
                }

                if (c.OpponentShift is { } opponentShift)
                {
                    lines.Add(Loc.F("Mulligan.Shift.Class", ClassName(view.OpponentClass), Points(opponentShift)));
                }
            }
            else
            {
                lines.Add(Loc.T("Mulligan.NotEnoughGames"));
            }

            if (c.KeptDelta is not null || c.ReplacedDelta is not null)
            {
                lines.Add(Loc.F("Mulligan.KeptReplaced", c.KeptDelta is { } k ? Points(k) : "—", c.ReplacedDelta is { } r ? Points(r) : "—", SliceName(c.Slice)));
            }

            if (c.DrawnDelta is { } drawnDelta)
            {
                lines.Add(Loc.F("Mulligan.DrawnLine", Points(drawnDelta)));
            }

            if (c.PickTier is { } pickTier)
            {
                lines.Add(Loc.F("Mulligan.Arena.Pick", pickTier, c.PickScore?.ToString("0", culture)));
            }

            if (c.Copy > 1)
            {
                lines.Add(Loc.T("Mulligan.SecondCopy"));
            }

            if (synergy is not null)
            {
                lines.Add(Loc.F("Mulligan.SynergyWith", synergy.EntityA == c.EntityId ? synergy.NameB : synergy.NameA, Points(synergy.Interaction), Margin(synergy.Margin), synergy.Games));
            }

            if (late)
            {
                lines.Add(Loc.F("Mulligan.LateCard", c.ExpectedTurn.ToString("0", culture)));
            }

            lines.Add(kept ? Loc.T("Mulligan.Keeping") : Loc.T("Mulligan.Replacing"));
            return new MulliganPlate(
                c.EntityId,
                c.Name,
                c.Call switch { Core.Tracking.MulliganCall.Keep => Loc.T("Mulligan.Call.Keep"), Core.Tracking.MulliganCall.Mulligan => Loc.T("Mulligan.Call.Replace"), _ => Loc.T("Mulligan.Call.NoData") },
                c.Call == Core.Tracking.MulliganCall.Keep,
                c.Call == Core.Tracking.MulliganCall.Mulligan,
                c.Edge is null ? string.Empty : Points(edge),
                tier,
                c.Edge >= MulliganGuide.CloseCall,
                c.Edge <= -MulliganGuide.CloseCall,
                edge < 0 ? fill : 0,
                edge > 0 ? fill : 0,
                synergy is not null,
                c.Copy > 1,
                late,
                kept,
                string.Join('\n', lines),
                arena);
        }).ToList();
    }

    /// <summary>
    /// "By your deck, N games, data from <date>" for a decklist's numbers (the date in the player's local day and
    /// culture); empty for an archetype's.
    /// </summary>
    internal static string ScopeLine(MulliganView view, CultureInfo culture)
    {
        if (view.Scope == MulliganScope.Archetype || view.DeckGames is not { } games)
        {
            return string.Empty;
        }

        var own = view.Scope == MulliganScope.Deck;
        return view.DataAsOf is { } asOf
            ? Loc.P(own ? "Mulligan.Scope.Deck" : "Mulligan.Scope.NearDeck", games, DateTime.SpecifyKind(asOf, DateTimeKind.Utc).ToLocalTime().ToString("d", culture))
            : Loc.P(own ? "Mulligan.Scope.DeckNoDate" : "Mulligan.Scope.NearDeckNoDate", games);
    }

    /// <summary>The rank of a slice as the player reads it: Legend, all ranks, or the mulligan filter's rank (plan 2.4).</summary>
    internal static string SliceRankName(string rank) => MulliganGuide.FilterRankOf(rank) switch
    {
        "legend" => Loc.T("Mulligan.Slice.Legend"),
        "diamond" or "top_legend" => Loc.T("Core.MulliganRankSource." + MulliganGuide.FilterRankOf(rank)),
        _ => Loc.T("Mulligan.Slice.AllRanks"),
    };

    /// <summary>"slice: Legend, on the coin": where a card's kept / replaced numbers come from.</summary>
    private static string SliceName(MulliganSlice? slice) => slice is null
        ? string.Empty
        : string.Join(", ", new[]
        {
            Loc.F("Mulligan.SliceSource", SliceRankName(slice.Rank)),
            slice.Coin switch { "going_first" => Loc.T("Mulligan.GoingFirst"), "on_coin" => Loc.T("Mulligan.OnTheCoin"), _ => null },
            slice.OpponentClass == MulliganGuide.AnyOpponent ? null : Loc.F("Mulligan.VsClass", ClassName(slice.OpponentClass)).Trim(),
        }.Where(s => s is not null));

    /// <summary>A win rate difference in points: 0.023 → "+2.3".</summary>
    private static string Points(double delta) => (delta * 100).ToString("+0.0;−0.0;0.0", CultureInfo.CurrentCulture);

    /// <summary>The half-width of an interval in win rate points, unsigned ("1.4").</summary>
    private static string Margin(double margin) => (margin * 100).ToString("0.0", CultureInfo.CurrentCulture);
}
