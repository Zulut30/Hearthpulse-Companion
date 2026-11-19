namespace Hearthpulse.Core.Battlegrounds.Effects.Deaths;

/// <summary>
/// Forest Rover (BG31_801): "Battlecry: Your Beetles have +2/+1 this game. Deathrattle: Summon a {0}/{1} Beetle."
/// Golden: "+4/+2 ... Summon two {0}/{1} Beetles." The Battlecry played in the tavern is already part of the Beetle stats
/// the card shows (its script data). A Battlecry triggered in combat (<see cref="Fight.TriggerBattlecry"/>) improves the
/// Beetles by the card text's +2/+1 (golden +4/+2): the log shows that number in TAG_SCRIPT_DATA_NUM_3/4, which the
/// capture does not keep.
/// </summary>
[CardEffect("BG31_801")]
internal sealed class ForestRover : CombatEffect
{
    public override void Deathrattle(Fight fight, Unit self) => Beetles.Summon(fight, self, self.Pick(1, 2));

    public override void Battlecry(Fight fight, Unit self) => Beetles.Improve(fight, self.Side, self.Pick(2, 4), self.Pick(1, 2));
}
