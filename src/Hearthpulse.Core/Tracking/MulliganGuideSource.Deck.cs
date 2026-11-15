using Hearthpulse.Core.Api;
using Hearthpulse.Core.Cards;

namespace Hearthpulse.Core.Tracking;

/// <summary>
/// The per-deck part of the guide's sources (docs/mulligan-v2.md 2.2): matching the player's list to a collected deck
/// and reading that deck's slices.
/// </summary>
public sealed partial class MulliganGuideSource
{
    /// <summary>
    /// At most this many collected decks of the player's class are compared with the deck, most played first: each one
    /// not seen before costs one request for its list (the server collects about a dozen decks per format and rank).
    /// </summary>
    public const int MaxDeckCandidates = 6;

    /// <summary>A collected deck this many cards away from the player's still speaks for it, marked as a close deck.</summary>
    public const int NearDeckDifference = 2;

    /// <summary>
    /// The guide from the per-deck mulligan: the collected deck of the player's class whose list is the player's
    /// (<see cref="MulliganScope.Deck"/>), else the closest one at most <see cref="NearDeckDifference"/> cards away
    /// (<see cref="MulliganScope.NearDeck"/>; more played wins a tie). Null when no collected deck is that close, the
    /// class has none, or the server and the cache do not answer: the archetype guide speaks then.
    /// Requests: the list of decks, one all-games slice per candidate whose list is not known yet (at most
    /// <see cref="MaxDeckCandidates"/>, kept for good), and for a match its initiative / opponent slices. A deck that
    /// matches nothing asks for no slice once the candidates' lists are known.
    /// </summary>
    /// <param name="rank">The mulligan rank filter; the deck's slices are read at <see cref="DeckRankFor"/>.</param>
    public static async Task<GuideArchetype?> LoadDeckGuideAsync(HearthpulseData data, string format, string playerClass, IReadOnlyDictionary<int, int> deck, CardDb cards,
        string rank = MulliganGuide.DefaultRank, string? opponentClass = null, bool? goingFirst = null, CancellationToken cancellationToken = default)
    {
        var listed = await data.LoadHsreplayMulliganDecksAsync(format, cancellationToken).ConfigureAwait(false);
        var candidates = listed.Where(d => string.Equals(d.PlayerClass, playerClass, StringComparison.OrdinalIgnoreCase))
            .GroupBy(d => d.DeckId, StringComparer.Ordinal)
            .Select(g => new DeckCandidate(g.Key, DeckRankFor(rank, g.Select(d => d.Rank).ToList()), g.Max(d => d.Games), g.First().ArchetypeName))
            .OrderByDescending(c => c.Games)
            .Take(MaxDeckCandidates)
            .ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        var lists = await Task.WhenAll(candidates.Select(c => data.LoadHsreplayDeckListAsync(c.DeckId, format, c.Rank, cancellationToken))).ConfigureAwait(false);
        var found = candidates.Zip(lists, (c, list) => (Candidate: c, Difference: list is null ? int.MaxValue : MulliganGuide.DeckDifference(deck, list)))
            .Where(c => c.Difference <= NearDeckDifference)
            .OrderBy(c => c.Difference).ThenByDescending(c => c.Candidate.Games)
            .Select(c => c.Candidate with { Difference = c.Difference })
            .FirstOrDefault();
        if (found is null)
        {
            Diag.Info($"Mulligan guide: none of the {candidates.Count} collected {format} {playerClass} decks is within {NearDeckDifference} cards of this deck");
            return null;
        }

        if (await data.LoadHsreplayDeckMulliganAsync(found.DeckId, format, found.Rank, cancellationToken: cancellationToken).ConfigureAwait(false) is not { } all)
        {
            Diag.Info($"Mulligan guide: the collected deck {found.DeckId} has no {found.Rank} slice");
            return null;
        }

        all = WithCards(all, cards);
        var name = all.ArchetypeName.Length > 0 ? all.ArchetypeName : found.ArchetypeName;
        Diag.Info($"Mulligan guide: collected deck {found.DeckId} ({name}, {(found.Difference == 0 ? "the same list" : $"{found.Difference} cards differ")}), {found.Rank}, {all.Games} games, {all.Cards.Count} cards");
        var guide = new GuideArchetype(all.ArchetypeId, name, playerClass, all.Cards, [],
            Localization.Loc.F("Source.MulliganDeck", HearthpulseData.Attribution, Localization.Loc.T("Core.MulliganDeckRank." + found.Rank)))
        {
            BaselineWinRate = all.BaseWinRate,
            Rank = rank,
            Scope = found.Difference == 0 ? MulliganScope.Deck : MulliganScope.NearDeck,
            DeckId = found.DeckId,
            DeckDifference = found.Difference,
            DeckSlices = [all],
        };
        return await WithDeckSlicesAsync(data, guide, format, cards, opponentClass, goingFirst, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A collected deck of the player's class, the rank its slices are read at, and how far its list is from the player's.</summary>
    private sealed record DeckCandidate(string DeckId, string Rank, int Games, string ArchetypeName)
    {
        public int Difference { get; init; }
    }

    /// <summary>
    /// The server rank a collected deck's slices are read at for a mulligan rank filter: the closest of the ranks the
    /// deck is listed under (<paramref name="available"/>). The per-deck data has no all-ranks or top-Legend slice:
    /// "all" reads the widest ranks first, Top Legend reads Legend.
    /// </summary>
    public static string DeckRankFor(string? rank, IReadOnlyCollection<string> available)
    {
        IReadOnlyList<string> order = rank switch
        {
            "all" => ["bronze_gold", "diamond", "diamond_4to1", "legend"],
            "diamond" => ["diamond_4to1", "diamond", "legend", "bronze_gold"],
            _ => HearthpulseData.DeckMulliganRanks,
        };
        return order.FirstOrDefault(available.Contains) ?? order[0];
    }

    /// <summary>
    /// A deck guide with the slices this game can use added: the initiative, the opponent's class, and both together
    /// (three requests at most; a slice the server lacks is simply missing and its 404 is remembered). Slices already in
    /// the guide are not asked for again.
    /// </summary>
    public static async Task<GuideArchetype> WithDeckSlicesAsync(HearthpulseData data, GuideArchetype guide, string format, CardDb cards, string? opponentClass, bool? goingFirst,
        CancellationToken cancellationToken = default)
    {
        if (guide.DeckId is not { } deckId || guide.DeckSlices.Count == 0)
        {
            return guide;
        }

        var initiative = goingFirst switch { true => "first", false => "coin", null => null };
        var opponent = opponentClass is { Length: > 0 } ? opponentClass.ToUpperInvariant() : null;
        var keys = new List<(string Initiative, string Opponent)>();
        if (initiative is not null)
        {
            keys.Add((initiative, MulliganGuide.AnyOpponent));
        }

        if (opponent is not null)
        {
            keys.Add((DeckMulliganSlice.AllGames, opponent));
        }

        if (initiative is not null && opponent is not null)
        {
            keys.Add((initiative, opponent));
        }

        var wanted = keys.Where(k => !guide.DeckSlices.Any(s => s.Initiative == k.Initiative && s.OpponentClass == k.Opponent)).ToList();
        if (wanted.Count == 0)
        {
            return guide;
        }

        var deckRank = guide.DeckSlices[0].Rank;
        var loaded = await Task.WhenAll(wanted.Select(k => data.LoadHsreplayDeckMulliganAsync(deckId, format, deckRank, k.Initiative, k.Opponent, cancellationToken))).ConfigureAwait(false);
        var added = loaded.OfType<DeckMulliganSlice>().Select(s => WithCards(s, cards)).ToList();
        if (added.Count > 0)
        {
            Diag.Info($"Mulligan guide: deck {deckId} slices {string.Join(", ", added.Select(s => $"{s.Rank}/{s.Initiative}/{s.OpponentClass} ({s.Games} games)"))}");
        }

        return guide with { DeckSlices = [.. guide.DeckSlices, .. added] };
    }

    /// <summary>The slice with each row under the card database's id and cost for its dbf id (the answer has no cost).</summary>
    private static DeckMulliganSlice WithCards(DeckMulliganSlice slice, CardDb cards) =>
        slice with { Cards = slice.Cards.Select(c => cards.Get(c.DbfId) is { } card ? c with { CardId = card.Id, Cost = card.Cost } : c).ToList() };
}
