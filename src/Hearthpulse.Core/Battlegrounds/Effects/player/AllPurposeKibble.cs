namespace Hearthpulse.Core.Battlegrounds.Effects.Player;

/// <summary>
/// All-Purpose Kibble (BG32_MagicItem_200): "Whenever a friendly Beast attacks, give it +4 Attack and permanently improve
/// this."
/// <para>
/// Each attack of a friendly Beast (before damage) gives it the current number, then the number grows at once, so the next
/// Beast attack of the same fight gets more. The number at capture is the trinket's TAG_SCRIPT_DATA_NUM_1
/// (<see cref="GrowingValue"/>; printed 4 without it, then missing_input). Assumption: the text does not say by how much
/// it improves; +1 per trigger (<see cref="Improvement"/>), no log has the trinket.
/// </para>
/// </summary>
[CardEffect("BG32_MagicItem_200")]
internal sealed class AllPurposeKibble : CombatEffect
{
    public const int Printed = 4;
    public const int Improvement = 1;

    public override string CapturedInput => GrowingValue.Description;

    public override bool HasInputs(SimSide side, string cardId) => GrowingValue.Known(side, cardId);

    // Counter: the Attack the next Beast attack gets.
    public override void OnFightStart(Fight fight, Unit self) =>
        self.Counter = GrowingValue.Current(self.Side.Info, self.CardId, Printed, 0).Attack;

    public override void OnFriendlyAttack(Fight fight, Unit self, Unit attacker, Unit target)
    {
        if (attacker.Is(Tribe.Beast))
        {
            fight.Buff(attacker, self.Counter, 0);
            self.Counter += Improvement;
        }
    }
}
