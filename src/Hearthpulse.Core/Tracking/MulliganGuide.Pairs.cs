namespace Hearthpulse.Core.Tracking;

/// <summary>
/// The pair part of the guide's maths (docs/mulligan-v2.md 4.2, metric <c>mulligan.pair_winrate</c>): how the games
/// where two cards were kept together went against the games where one or neither was, inside Hearthpulse's own
/// statistics of that pair. An observed association: keeping is the player's choice, so nothing here says what keeping
/// both would do.
/// </summary>
public static partial class MulliganGuide
{
    /// <summary>
    /// The observed difference between the games with both cards kept and what the games with each kept alone add up
    /// to, with the half-width of its nominal 95 % interval:
    /// <c>p(both) − p(only A) − p(only B) + p(neither)</c> over the pair's own games (both cards on offer). The four
    /// cells are disjoint sets of games, so the variance of the difference is the sum of the cells' variances
    /// (<c>Σ p(1 − p) / n</c>, each share smoothed by one win and one loss so a 0 % or 100 % cell is not "certain");
    /// two separate intervals of two win rates would say nothing about the difference. The interval is nominal and too
    /// narrow: repeated games of one installation, the patches mixed in the 30-day window, the selection by the keep
    /// decision and the many pairs looked at are not in it.
    /// Null when a comparison cell is hidden or under <see cref="MinPairCellGames"/>, or the pair is under
    /// <see cref="MinPairGames"/>. Limit: games of one installation count as independent (the pool publishes counts only).
    /// </summary>
    public static (double Interaction, double Margin)? PairEffect(GuidePair pair)
    {
        if (pair.Both.Games < MinPairGames
            || pair.OnlyA is not { Games: >= MinPairCellGames } onlyA
            || pair.OnlyB is not { Games: >= MinPairCellGames } onlyB
            || pair.Neither is not { Games: >= MinPairCellGames } neither)
        {
            return null;
        }

        static double Variance(PairCell cell)
        {
            var share = (cell.Wins + 1.0) / (cell.Games + 2.0);
            return share * (1 - share) / cell.Games;
        }

        var interaction = pair.Both.WinRate - onlyA.WinRate - onlyB.WinRate + neither.WinRate;
        var margin = 1.96 * Math.Sqrt(Variance(pair.Both) + Variance(onlyA) + Variance(onlyB) + Variance(neither));
        return (interaction, margin);
    }

    /// <summary>
    /// The hand's pairs worth a line: a difference of at least <see cref="MinInteraction"/> whose nominal interval does
    /// not reach zero. A filter against noise, not a proof of an effect.
    /// </summary>
    private static List<MulliganSynergy> Synergies(IReadOnlyList<GuidePair> pairs, IReadOnlyList<OfferedCard> hand)
    {
        var result = new List<MulliganSynergy>();
        for (var i = 0; i < hand.Count; i++)
        {
            for (var j = i + 1; j < hand.Count; j++)
            {
                var a = hand[i];
                var b = hand[j];
                if (a.CardId == b.CardId)
                {
                    continue;
                }

                var pair = pairs.FirstOrDefault(p => (p.CardA == a.CardId && p.CardB == b.CardId) || (p.CardA == b.CardId && p.CardB == a.CardId));
                if (pair is null || PairEffect(pair) is not { } effect)
                {
                    continue;
                }

                var (interaction, margin) = effect;
                if (Math.Abs(interaction) >= MinInteraction && Math.Abs(interaction) > margin && !result.Exists(s => (s.EntityA == a.EntityId || s.EntityB == a.EntityId) && (s.EntityA == b.EntityId || s.EntityB == b.EntityId)))
                {
                    result.Add(new MulliganSynergy(a.EntityId, b.EntityId, a.Name, b.Name, interaction, pair.Games, margin));
                }
            }
        }

        return result;
    }
}
