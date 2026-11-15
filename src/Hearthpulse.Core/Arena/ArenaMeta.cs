using Hearthpulse.Core.Api;

namespace Hearthpulse.Core.Arena;

/// <summary>ArenaSmith score and results of one card across all Arena classes.</summary>
/// <param name="Score">ArenaSmith score (about 0-60, higher is a better pick); null when the server has too few games for it.</param>
/// <param name="WinRate">Deck win rate with the card, 0..1.</param>
/// <param name="PickRate">How often the card is taken when offered, 0..1.</param>
public sealed record ArenaCardScore(string CardId, int DbfId, double? Score, double? WinRate, double? PickRate, int Games)
{
    /// <summary>Tier letter of <see cref="Score"/>, or null without a score.</summary>
    public string? Tier => ArenaMeta.TierOf(Score);

    /// <summary>Win rate of the games the card was drawn in (any turn), 0..1; null when the server did not send it.</summary>
    public double? DrawnWinRate { get; init; }

    /// <summary>Win rate of the games the card was played in, 0..1; null when the server did not send it.</summary>
    public double? PlayedWinRate { get; init; }

    /// <summary>Class of the card as the server writes it (WARLOCK, NEUTRAL); null when the answer did not carry it.</summary>
    public string? CardClass { get; init; }

    /// <summary>
    /// The tier letter the server sends with the row. Kept as sent and not shown: it follows the win rate, also for a
    /// card with a handful of games and no score, while the badges use <see cref="Tier"/> (the score's letter).
    /// </summary>
    public string? SourceTier { get; init; }
}

/// <summary>Community results of one Arena class.</summary>
/// <param name="PlayerClass">Upper-case class key as the card data writes it (WARLOCK, DEATHKNIGHT, DEMONHUNTER...).</param>
/// <param name="WinRate">Win rate, 0..1.</param>
/// <param name="PickRate">How often the class is taken when offered, 0..1.</param>
/// <param name="SevenPlus">Share of runs that reach 7 wins or more, 0..1.</param>
public sealed record ArenaClassStat(string PlayerClass, double WinRate, double? PickRate, double? SevenPlus, int Drafts);

/// <summary>
/// Arena card scores (ArenaSmith) and class results from the Hearthpulse API. The server publishes one all-classes
/// slice of the cards, so a neutral card has one score whatever class drafts it.
/// </summary>
public sealed class ArenaMeta
{
    /// <summary>What the numbers go by in the UI.</summary>
    public const string Attribution = HearthpulseData.Attribution;

    private readonly Dictionary<string, ArenaCardScore> _byId;
    private readonly Dictionary<int, ArenaCardScore> _byDbf;
    private readonly Dictionary<string, ArenaClassStat> _byClass;

    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _packages;

    /// <param name="underground">The card slice is Underground Arena (true) or normal Arena (false); null when the server did not say.</param>
    /// <param name="timeRange">The period of the card numbers as the server names it (LAST_4_DAYS).</param>
    /// <param name="packages">Legendary packages of the Underground Arena by key card id.</param>
    public ArenaMeta(IEnumerable<ArenaCardScore> cards, IEnumerable<ArenaClassStat> classes, DateTime? fetchedAt = null, bool? underground = null,
        string? timeRange = null, IReadOnlyDictionary<string, IReadOnlyList<string>>? packages = null)
    {
        TimeRange = timeRange;
        _packages = packages ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        _byId = new Dictionary<string, ArenaCardScore>(StringComparer.Ordinal);
        _byDbf = new Dictionary<int, ArenaCardScore>();
        foreach (var card in cards)
        {
            _byId.TryAdd(card.CardId, card);
            if (card.DbfId > 0)
            {
                _byDbf.TryAdd(card.DbfId, card);
            }
        }

        Classes = classes.OrderByDescending(c => c.WinRate).ToList();
        _byClass = Classes.GroupBy(c => c.PlayerClass, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        FetchedAt = fetchedAt;
        Underground = underground;
    }

    public static ArenaMeta Empty { get; } = new([], []);

    /// <summary>Classes, best win rate first.</summary>
    public IReadOnlyList<ArenaClassStat> Classes { get; }

    /// <summary>When the server fetched the card numbers.</summary>
    public DateTime? FetchedAt { get; }

    /// <summary>Which Arena the card scores come from: Underground (true), normal (false) or unknown (null).</summary>
    public bool? Underground { get; }

    /// <summary>The period the card numbers cover as the server names it (LAST_4_DAYS); null when it did not say.</summary>
    public string? TimeRange { get; }

    public int CardCount => _byId.Count;

    /// <summary>Legendaries the server lists a package for.</summary>
    public int PackageCount => _packages.Count;

    /// <summary>
    /// The cards the server lists with a legendary of the Underground Arena, a copy per entry; empty for any other
    /// card. A reference list for a pick already made: never for the offer on screen, where only what the client
    /// itself showed may be named (docs/arena-draft.md, L-M1).
    /// </summary>
    public IReadOnlyList<string> PackageOf(string? keyCardId) =>
        keyCardId is { Length: > 0 } && _packages.TryGetValue(keyCardId, out var cards) ? cards : [];

    /// <summary>The same card scores without the class numbers (they have no Underground split).</summary>
    public ArenaMeta WithoutClasses() => new(Cards, [], FetchedAt, Underground, TimeRange, _packages);

    /// <summary>Every scored or listed card.</summary>
    public IEnumerable<ArenaCardScore> Cards => _byId.Values;

    public bool HasCards => _byId.Count > 0;

    public ArenaCardScore? Card(string? cardId) => cardId is { Length: > 0 } && _byId.TryGetValue(cardId, out var card) ? card : null;

    public ArenaCardScore? Card(int dbfId) => _byDbf.GetValueOrDefault(dbfId);

    public double? Score(string? cardId) => Card(cardId)?.Score;

    public ArenaClassStat? Class(string? playerClass) =>
        playerClass is { Length: > 0 } && _byClass.TryGetValue(playerClass.ToUpperInvariant(), out var stat) ? stat : null;

    /// <summary>
    /// Tier letter of an ArenaSmith score, with the hearthpulse.net tier list's thresholds (S 53+, A 45+, B 35+, C 24+,
    /// D 10+, else E), so the overlay and the site agree.
    /// </summary>
    public static string? TierOf(double? score) => score switch
    {
        null => null,
        >= 53 => "S",
        >= 45 => "A",
        >= 35 => "B",
        >= 24 => "C",
        >= 10 => "D",
        _ => "E",
    };

    /// <summary>
    /// Class key of the card data from the API's class names ("Deathknight", "Demon Hunter", "death-knight"), or
    /// null for something else.
    /// </summary>
    public static string? ClassKey(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var key = new string(name.Where(char.IsLetter).ToArray()).ToUpperInvariant();
        return key is "DEATHKNIGHT" or "DEMONHUNTER" or "DRUID" or "HUNTER" or "MAGE" or "PALADIN" or "PRIEST" or "ROGUE"
            or "SHAMAN" or "WARLOCK" or "WARRIOR"
            ? key
            : null;
    }
}
