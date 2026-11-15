using Hearthpulse.Core.Constructed;
using Hearthpulse.Core.Localization;

namespace Hearthpulse.Core.Tracking;

/// <summary>
/// HSReplay mulligan numbers for one card of an archetype (rates as fractions, 0..1).
/// </summary>
/// <param name="Offered">Times the card was offered in the starting cards.</param>
/// <param name="InOpeningHand">Games the card was in hand after the mulligan, kept or drawn as a replacement.</param>
/// <param name="OpeningHandWinRate">Win rate of those games (HSReplay's "Mulligan WR").</param>
/// <param name="AvgTurnPlayed">The deck's average turn for playing the card; null when unknown (Firestone data).</param>
public sealed record GuideCard(string CardId, int DbfId, int Cost, int Offered, double KeepRate, int InOpeningHand, double? OpeningHandWinRate, double? AvgTurnPlayed)
{
    /// <summary>Games the card was drawn in (any turn, the opening hand included) and their win rate; HSReplay only.</summary>
    public int Drawn { get; init; }

    public double? DrawnWinRate { get; init; }
}

/// <summary>
/// HSGuru's numbers for one card in one mulligan slice (<c>/v1/hsguru/mulligan</c>). Impacts are win rate points
/// against the deck's own win rate in the same slice, as fractions (0.042 = +4.2 points).
/// </summary>
/// <param name="Offered">Games the card was offered in the mulligan.</param>
/// <param name="OfferedImpact">Win rate of those games against the deck's: independent of the player's choice.</param>
/// <param name="Kept">Games the offered card was kept.</param>
/// <param name="Drawn">Games the card was drawn (any turn).</param>
public sealed record SliceCard(string CardId, int DbfId, int Offered, double? OfferedImpact, int Kept, double? KeptImpact, int Drawn, double? DrawnImpact)
{
    public int Replaced => Math.Max(0, Offered - Kept);

    public double? KeepRate => Offered > 0 ? (double)Kept / Offered : null;

    /// <summary>Win rate of the games where the offered card went back, from offered = kept + replaced.</summary>
    public double? ReplacedImpact => OfferedImpact is { } offered && KeptImpact is { } kept && Replaced > 0
        ? (offered * Offered - kept * Kept) / Replaced
        : null;
}

/// <summary>One HSGuru mulligan slice of an archetype: a rank, the coin and the opponent's class ("any" for all).</summary>
/// <param name="Rank">"legend", "all", ... (HSGuru's keys).</param>
/// <param name="Coin"><see cref="MulliganGuide.AnyCoin"/>, "going_first" or "on_coin".</param>
/// <param name="OpponentClass"><see cref="MulliganGuide.AnyOpponent"/> or a class ("WARLOCK").</param>
public sealed record MulliganSlice(string Rank, string Coin, string OpponentClass, int Games, IReadOnlyList<SliceCard> Cards)
{
    private IReadOnlyDictionary<string, SliceCard>? _byId;

    /// <summary>The card's row; a slice has hundreds and the guide asks for every card of the deck on each update.</summary>
    public SliceCard? Card(string cardId) =>
        (_byId ??= Cards.GroupBy(c => c.CardId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal))
        .GetValueOrDefault(cardId);
}

/// <summary>Whose games the guide's numbers are: the archetype's, the player's exact list, or a list close to it.</summary>
public enum MulliganScope
{
    Archetype,
    Deck,
    NearDeck,
}

/// <summary>
/// One deck the server collects the per-deck mulligan for at one rank (<c>/v1/hsreplay/mulligan/decks</c>).
/// </summary>
/// <param name="Rank">The server's rank key: "legend", "diamond_4to1", "diamond" or "bronze_gold".</param>
public sealed record MulliganDeckEntry(string DeckId, string ArchetypeName, string PlayerClass, string Rank, int Games, double WinRate)
{
    /// <summary>
    /// The deck: card dbf id → copies. Null in an answer cached before the server sent it (2026-11-15): the list then
    /// comes with the deck's slice (<see cref="DeckMulliganSlice.DeckList"/>).
    /// </summary>
    public IReadOnlyDictionary<int, int>? DeckList { get; init; }
}

/// <summary>
/// The mulligan numbers of one exact decklist in one slice (<c>/v1/hsreplay/mulligan?deck=</c>): a rank, the initiative
/// and the opponent's class. Rates are fractions.
/// </summary>
/// <param name="Initiative">"all", "first" or "coin".</param>
/// <param name="OpponentClass"><see cref="MulliganGuide.AnyOpponent"/> or a class ("MAGE").</param>
/// <param name="Games">The deck's games in this slice.</param>
/// <param name="BaseWinRate">The deck's win rate over the same games: the cards' deltas are measured against it.</param>
/// <param name="AsOf">When the source computed the numbers (UTC); null when the answer does not say.</param>
/// <param name="DeckList">The deck: card dbf id → copies.</param>
/// <param name="Cards">
/// One row per card. The server publishes no per-card counts, so <see cref="GuideCard.Offered"/>,
/// <see cref="GuideCard.InOpeningHand"/> and <see cref="GuideCard.Drawn"/> are estimates (the slice's games times the
/// chance to be dealt the card): they only gate thin slices and are never shown.
/// </param>
public sealed record DeckMulliganSlice(string DeckId, string Rank, string Initiative, string OpponentClass, int Games, double BaseWinRate, DateTime? AsOf,
    IReadOnlyDictionary<int, int> DeckList, IReadOnlyList<GuideCard> Cards)
{
    /// <summary>The initiative key of "every game".</summary>
    public const string AllGames = "all";

    public int ArchetypeId { get; init; }

    public string ArchetypeName { get; init; } = string.Empty;

    public string PlayerClass { get; init; } = string.Empty;
}

/// <summary>One decklist HSReplay counts under an archetype: card dbf id → copies.</summary>
public sealed record GuideDeck(int Games, IReadOnlyDictionary<int, int> Cards);

/// <summary>The archetype's results against one opponent archetype.</summary>
public sealed record GuideMatchup(string OpponentClass, int Games, double WinRate);

/// <summary>Games and wins of one keep choice of a pair.</summary>
public sealed record PairCell(int Games, int Wins)
{
    public double WinRate => Games > 0 ? (double)Wins / Games : 0;
}

/// <summary>
/// Two different cards offered together in the opening hand, from Hearthpulse's own statistics (metric
/// <c>mulligan.pair_winrate</c>, <see cref="Pool.MulliganPairs"/>; docs/mulligan-v2.md, 4.2): the games of the player's
/// class and format by what was kept. Every game here had both cards on offer, so all four cells come from decks that
/// hold both, and the comparison cells are the pair's own base numbers.
/// </summary>
/// <param name="Installations">Installations behind <paramref name="Both"/> (the pool publishes none under five).</param>
/// <param name="Both">Both cards kept.</param>
/// <param name="OnlyA">Only <paramref name="CardA"/> kept; null when the pool hid the cell as too small.</param>
/// <param name="OnlyB">Only <paramref name="CardB"/> kept; null when hidden.</param>
/// <param name="Neither">Both replaced; null when hidden.</param>
public sealed record GuidePair(string CardA, string CardB, int Installations, PairCell Both, PairCell? OnlyA, PairCell? OnlyB, PairCell? Neither)
{
    /// <summary>Games with both cards kept.</summary>
    public int Games => Both.Games;
}

/// <summary>Everything the guide knows about the archetype the player's deck matched.</summary>
public sealed record GuideArchetype(int Id, string Name, string Class, IReadOnlyList<GuideCard> Cards, IReadOnlyList<GuideMatchup> Matchups, string Source)
{
    public IReadOnlyList<GuidePair> Pairs { get; init; } = [];

    /// <summary>Win rates going first / on the coin, when the source splits them (Firestone does, the API does not).</summary>
    public double? FirstWinRate { get; init; }

    public double? CoinWinRate { get; init; }

    /// <summary>The archetype's overall win rate in the same source as <see cref="FirstWinRate"/>.</summary>
    public double? WinRate { get; init; }

    /// <summary>
    /// The deck's win rate over the same games as the card numbers, when the source publishes it (Firestone does);
    /// otherwise <see cref="MulliganGuide.Baseline"/> estimates it from the cards.
    /// </summary>
    public double? BaselineWinRate { get; init; }

    /// <summary>The HSGuru archetype the deck matches, whose mulligan slices are in <see cref="Slices"/>; null when none.</summary>
    public string? HsguruArchetype { get; init; }

    /// <summary>HSGuru mulligan slices loaded so far (all games, and the coin and the opponent's class once known).</summary>
    public IReadOnlyList<MulliganSlice> Slices { get; init; } = [];

    /// <summary>The mulligan rank filter the guide was built for (<see cref="MulliganGuide.Ranks"/>); its slices follow it.</summary>
    public string Rank { get; init; } = MulliganGuide.DefaultRank;

    /// <summary>Whose games <see cref="Cards"/> are: the archetype's (every older source) or one decklist's.</summary>
    public MulliganScope Scope { get; init; }

    /// <summary>The collected deck the player's list matched (exactly or closely); null for an archetype guide.</summary>
    public string? DeckId { get; init; }

    /// <summary>Cards of the player's deck the matched list lacks: 0 for an exact match, 1-2 for a close deck.</summary>
    public int DeckDifference { get; init; }

    /// <summary>
    /// The matched deck's slices loaded so far, all at one rank: all games first, then the initiative and the opponent's
    /// class once the mulligan knows them. <see cref="MulliganGuide.Compute"/> reads the most specific one that fits.
    /// </summary>
    public IReadOnlyList<DeckMulliganSlice> DeckSlices { get; init; } = [];
}

/// <summary>A card offered in the mulligan, left to right.</summary>
public sealed record OfferedCard(int EntityId, string CardId, string Name, int Cost);

public enum MulliganCall
{
    NoData,
    Keep,
    Mulligan,
}

/// <summary>
/// Two offered cards whose games with both kept went better (or worse) than the games with each kept alone add up to,
/// in Hearthpulse's own statistics (<see cref="MulliganGuide.PairEffect"/>). An observation shown beside the cards; it
/// moves no call and no estimate.
/// </summary>
/// <param name="Interaction">The difference, as a fraction (0.03 = 3 points).</param>
/// <param name="Games">Games with both cards kept.</param>
/// <param name="Margin">Half-width of the difference's nominal 95 % interval (approximate, too narrow); a pair is listed only when it is smaller than the difference.</param>
public sealed record MulliganSynergy(int EntityA, int EntityB, string NameA, string NameB, double Interaction, int Games, double Margin);

/// <summary>What the current choice of keeps is expected to do.</summary>
/// <param name="Expected">Estimated win rate when exactly these cards are kept.</param>
/// <param name="Recommended">The same estimate for the recommended keeps.</param>
/// <param name="FullMulligan">The same estimate when every card is replaced.</param>
/// <param name="Warnings">Curve and duplicate warnings for these keeps (heuristics, marked as such in the text).</param>
public sealed record MulliganImpact(double Expected, double Recommended, double FullMulligan, int Kept, IReadOnlyList<string> Warnings, IReadOnlyList<MulliganSynergy> Synergies);
