using Hearthpulse.Bench;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Cards;

namespace Hearthpulse.Tests.Sim;

/// <summary>
/// Turns a real fight into a regression test: the fights of a log (a fixture's lines) with their transcripts, and an
/// assertion that one replays through the engine to the end exactly as the log fought it (Bench <c>simreplay</c>,
/// docs/bg-combat-sim.md). On a failure the message carries the first divergence and the whole replay dump.
/// </summary>
/// <example>
/// <code>
/// var fight = SimReplayKit.Fights(lines, day, cards).Single(f => f.Key.EndsWith("#1/3", StringComparison.Ordinal));
/// SimReplayKit.AssertReplays(fight, cards);
/// </code>
/// </example>
internal static class SimReplayKit
{
    /// <summary>Every fight of the log's Battlegrounds games that has a transcript, keyed "source#game/fight".</summary>
    public static IReadOnlyList<ReplayableFight> Fights(IEnumerable<string> lines, DateOnly date, CardDb cards, string source = "fixture") =>
        SimReplay.Read(lines, source, date, cards).SelectMany(g => g.Fights).Where(f => f.Fight.Transcript is not null).ToList();

    /// <summary>The fight's transcript is complete and the engine follows it to the end without a divergence.</summary>
    public static ReplayedFight AssertReplays(ReplayableFight fight, CardDb cards, EffectRegistry? effects = null)
    {
        var replayed = SimReplay.Replay(fight.Key, fight.Fight, cards, effects);
        Assert.NotNull(replayed);
        if (!replayed.Clean)
        {
            Assert.Fail($"{fight.Key}: {(replayed.Complete ? replayed.First!.ToString() : "incomplete transcript")}{Environment.NewLine}"
                        + string.Join(Environment.NewLine, SimReplay.Dump(fight.Fight, cards, effects)));
        }

        Assert.Equal(replayed.LogAttacks, replayed.Attacks);
        return replayed;
    }
}
