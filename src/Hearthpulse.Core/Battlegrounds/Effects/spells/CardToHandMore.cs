namespace Hearthpulse.Core.Battlegrounds.Effects.Spells;

// More "Get a card" effects in combat (TASK-2026-11-19 E5), beside CardToHand.cs: the card only matters to "whenever a
// card is added to your hand" (The Shadow of Doubt, Peggy Sturdybone, Thorncaptain...). Card text from HearthstoneJSON
// enUS, build 256589.

/// <summary>
/// Dark Paradox, its fifth form (BG36_360t5): "Rally: Get a random minion of your most common type." Golden
/// (BG36_360_Gt5): "Get 2 random minions". The minion is random: it counts as a card, which one does not change this
/// combat. Every attack (both Windfury swings).
/// </summary>
[CardEffect("BG36_360t5")]
internal sealed class DarkParadoxGetForm : CombatEffect
{
    public override void OnAttack(Fight fight, Unit self, Unit target)
    {
        for (var i = 0; i < self.Pick(1, 2); i++)
        {
            fight.AddToHand(self.Side, CardToHand.AnyCard);
        }
    }
}

/// <summary>
/// Locked-up Mutineer (BG36_521): "Deathrattle: Get a Lockbox. If you already have one, it opens 1 turn sooner instead."
/// Golden: "2 turns sooner".
/// </summary>
/// <remarks>
/// A Lockbox (BG36_520t, an Unplayable spell) is added only when the player has none: then it counts as a card added to
/// the hand; "opens sooner" is for the Tavern. Whether the player holds one at the start of the fight is not in the
/// capture (the hand list keeps minions only, the opponent's hand is hidden), so it is assumed from what both players
/// see: a player with Lockbox Portrait (BG36_MagicItem_301, "Get a Lockbox ... At the start of each turn, repeat this")
/// holds one, anyone else does not. The first Lockbox got in the fight is then held for the rest of it.
/// </remarks>
[CardEffect("BG36_521")]
internal sealed class LockedUpMutineer : CombatEffect
{
    public const string LockboxId = "BG36_520t";
    private const string LockboxPortrait = "BG36_MagicItem_301";
    private const string HasLockboxKey = "e5_has_lockbox";

    public override void Deathrattle(Fight fight, Unit self)
    {
        var side = self.Side;
        if (side.Counter(HasLockboxKey) > 0 || side.Info.Extras.Contains(LockboxPortrait))
        {
            return;
        }

        side.SetCounter(HasLockboxKey, 1);
        fight.AddToHand(side, LockboxId);
    }
}
