namespace Hearthpulse.Core.Battlegrounds.Effects.Attack;

/// <summary>
/// Hoarding Hyena (BG36_210): "Rally: Summon a Tasty Lobster." Golden: "Rally: Summon a Golden Tasty Lobster."
/// The Lobster (BG36_202, 2/1 Beast; golden 4/2) appears to the right of the Hyena, before damage. Its deathrattle
/// ("Give a random friendly Beast +{0}/+{1}") is the Lobster's own effect, registered separately.
/// </summary>
/// <remarks>
/// The summoned Lobster carries no script data, so its deathrattle uses the player's current Lobster value
/// (<see cref="Deaths.TastyLobster.Bonus"/>: the best value among the friendly Lobsters captured on the board or in hand,
/// else the printed +2/+1, golden +4/+2). A Lobster the game creates shows the player's current value from the start
/// ("Improve your future Tasty Lobsters"; real log 15.11.2026: every Lobster entity carries TAG_SCRIPT_DATA_NUM_1/2 when
/// it is created, also in combat), so stamping the printed numbers on it would drop the improvements. No Hoarding Hyena
/// attacked in the kept logs.
/// </remarks>
[CardEffect("BG36_210")]
internal sealed class HoardingHyena : CombatEffect
{
    public override void OnAttack(Fight fight, Unit self, Unit target)
    {
        fight.SummonAt(self, Tokens.TastyLobster(self.Golden));
    }
}
