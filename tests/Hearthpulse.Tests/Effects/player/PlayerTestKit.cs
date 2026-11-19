using Hearthpulse.Core.Battlegrounds;

namespace Hearthpulse.Tests.Sim.Player;

/// <summary>Sides with a player-level card and the tags the capture read from it (our own cards only).</summary>
internal static class PlayerTestKit
{
    /// <summary><paramref name="side"/> with <paramref name="cardId"/> in play and, when given, its captured tags.</summary>
    public static SimSide WithCard(SimSide side, string cardId, Dictionary<string, int>? tags = null) => side with
    {
        Extras = [.. side.Extras, cardId],
        ExtraTags = tags is null
            ? side.ExtraTags
            : new Dictionary<string, IReadOnlyDictionary<string, int>>(side.ExtraTags) { [cardId] = tags },
    };

    /// <summary>Captured TAG_SCRIPT_DATA_NUM_1/2.</summary>
    public static Dictionary<string, int> Numbers(int num1, int? num2 = null)
    {
        var tags = new Dictionary<string, int> { ["TAG_SCRIPT_DATA_NUM_1"] = num1 };
        if (num2 is { } value)
        {
            tags["TAG_SCRIPT_DATA_NUM_2"] = value;
        }

        return tags;
    }

    /// <summary>Index of the first log line ending in " dies".</summary>
    public static int FirstDeath(this FightResult result) => result.Log.ToList().FindIndex(l => l.EndsWith(" dies", StringComparison.Ordinal));

    /// <summary>Deaths logged before the first line containing <paramref name="text"/>.</summary>
    public static int DeathsBefore(this FightResult result, string text)
    {
        var index = SimTestKit.IndexOf(result, text);
        Assert.True(index >= 0, text + "\n" + string.Join('\n', result.Log));
        return result.Log.Take(index).Count(l => l.EndsWith(" dies", StringComparison.Ordinal));
    }
}
