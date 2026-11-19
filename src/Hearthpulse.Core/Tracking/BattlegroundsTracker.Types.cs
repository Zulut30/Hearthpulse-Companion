using System.Globalization;
using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Memory;
using Hearthpulse.Core.Power;

namespace Hearthpulse.Core.Tracking;

/// <summary>The tracker's own records: a captured combat, a board seen, a hero hit.</summary>
public sealed partial class BattlegroundsTracker
{
    internal sealed record CombatCapture(SimSide Ours, SimSide Theirs, bool? OursFirst, int FirstAttackerEntityId);

    /// <summary>
    /// What the log said about a captured minion besides what the simulator reads: its card id as logged (a golden
    /// minion's own id) and the effect marker tags it carries (<see cref="CombatRelevance.EffectMarkers"/>). Enough to
    /// decide "not simulated" again with newer card data and effects (<see cref="IsUnsimulated"/>).
    /// </summary>
    internal sealed record MinionFacts(int EntityId, string CardId, IReadOnlyList<string> Markers);

    /// <summary>
    /// A fight as the simulator got it: the combat it belongs to, the boards, the options and the minions' facts; once the
    /// fight is over, its course as the log showed it (<see cref="Transcript"/>, null while it runs or when nothing was recorded).
    /// </summary>
    internal sealed record CapturedFight(int CombatIndex, CombatCapture Capture, SimOptions Options, IReadOnlyList<MinionFacts> Facts)
    {
        public CombatTranscript? Transcript { get; init; }
    }

    private sealed record SeenBoard(int Turn, IReadOnlyList<BgMinion> Minions);

    /// <summary>A hero attack of the fight: whose hero hit, the hero it hit and how sturdy that was, the block depth.</summary>
    private sealed record HeroHit(bool Ours, Entity? Defender, int Before, int Depth);
}
