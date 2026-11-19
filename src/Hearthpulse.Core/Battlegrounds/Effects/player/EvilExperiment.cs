using Hearthpulse.Core.Battlegrounds.Effects.Ongoing;

namespace Hearthpulse.Core.Battlegrounds.Effects.Player;

/// <summary>
/// Evil Experiment (BG36_MagicItem_416): "After your Deity awakens, give it Reborn." The Deity awakens in combat when
/// it is summoned by its sleeping holder (<see cref="DeityEffect"/>, after four friendly Aberrations died); the awakened
/// minion then gets Reborn. A reborn copy of the Deity, or a copy of it made by another card, is not an awakening.
/// </summary>
[CardEffect("BG36_MagicItem_416")]
internal sealed class EvilExperiment : CombatEffect
{
    public override void AfterFriendlySummoned(Fight fight, Unit self, Unit summoned)
    {
        // The holder marks itself awake only after the summon returns: while it is being summoned it is still asleep.
        if (summoned.RebornFrom is null && Deity.IsDeity(summoned.CardId) && Deity.Holder(self.Side) is { } holder && !Deity.IsAwake(holder))
        {
            fight.GiveKeywords(summoned, Keyword.Reborn);
        }
    }
}
