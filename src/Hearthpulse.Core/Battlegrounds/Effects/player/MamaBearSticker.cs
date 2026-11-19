namespace Hearthpulse.Core.Battlegrounds.Effects.Player;

/// <summary>
/// Mama Bear Sticker (BG35_MagicItem_871): "Whenever you summon a Beast, give it +7/+7." In combat: every friendly Beast
/// summoned (tokens, reborn copies) gets the bonus as it arrives, before the "after you summon" triggers. The bonus is the
/// captured TAG_SCRIPT_DATA_NUM_1/2 of our sticker when present (<see cref="GrowingValue"/>), else the text's +7/+7.
/// </summary>
[CardEffect("BG35_MagicItem_871")]
internal sealed class MamaBearSticker : CombatEffect
{
    public override void OnFightStart(Fight fight, Unit self) => (self.Counter, self.Counter2) = GrowingValue.Current(self.Side.Info, self.CardId, 7, 7);

    public override void WheneverFriendlySummoned(Fight fight, Unit self, Unit summoned)
    {
        if (summoned.Is(Tribe.Beast))
        {
            fight.Buff(summoned, self.Counter, self.Counter2);
        }
    }
}
