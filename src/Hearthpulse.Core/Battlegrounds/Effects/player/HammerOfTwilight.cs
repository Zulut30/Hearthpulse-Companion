namespace Hearthpulse.Core.Battlegrounds.Effects.Player;

/// <summary>
/// Hammer of Twilight (BG36_MagicItem_403): "Your minions have +1 Attack. (Improved by each card you've discarded this
/// game!)" Greater version (…403t): "Your minions have +2/+1."
/// <para>
/// An aura: the captured minions already show it; minions summoned in combat (tokens, reborn copies) get it too. Cards
/// are not discarded in combat, so the number stays what it was at capture: TAG_SCRIPT_DATA_NUM_1/2 of the trinket
/// (<see cref="GrowingValue"/>; the printed +1/+0 or +2/+1 without it, then missing_input).
/// </para>
/// </summary>
[CardEffect("BG36_MagicItem_403")]
[CardEffect("BG36_MagicItem_403t")]
internal sealed class HammerOfTwilight : CombatEffect
{
    public override string CapturedInput => GrowingValue.Description;

    public override bool HasInputs(SimSide side, string cardId) => GrowingValue.Known(side, cardId);

    public override void OnFightStart(Fight fight, Unit self)
    {
        var greater = self.CardId.EndsWith('t');
        var (attack, health) = GrowingValue.Current(self.Side.Info, self.CardId, greater ? 2 : 1, greater ? 1 : 0);
        self.Side.AddSummonBuff(Tribe.All, attack, health);
    }
}
