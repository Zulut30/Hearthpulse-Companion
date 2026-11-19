namespace Hearthpulse.Core.Battlegrounds.Effects.Ongoing;

/// <summary>
/// Rot Hide Gnoll (BG25_013, 1/4): "Has +1 Attack for each friendly minion that died this combat." Golden (BG25_013_G,
/// 2/8): "+2 Attack".
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Every friendly death of the simulated fight (tokens and the Gnoll's own earlier copy included) adds the bonus to
/// each living friendly Gnoll once (<see cref="CountingAura"/>). A captured Gnoll's Attack already includes the deaths
/// before capture (Start of Combat).</item>
/// <item>A Gnoll summoned in combat (a reborn copy) comes with the bonus for every friendly death of this combat: those of
/// the fight so far plus those before capture, inferred from the captured Gnolls' Attack above the printed 1 (other
/// buffs on every copy make that an overestimate; without a captured Gnoll it is 0).</item>
/// <item>The bonus per death is the captured TAG_SCRIPT_DATA_NUM_1 when the minion carries one, else the card text (+1,
/// golden +2). No real log with this minion was available (2026-11-19).</item>
/// </list>
/// </remarks>
[CardEffect(Id)]
internal sealed class RotHideGnoll : CountingAura
{
    public const string Id = "BG25_013";

    protected override string CardId => Id;

    protected override (int Attack, int Health) Printed => (1, 4);

    protected override (int Attack, int Health) PerEvent => (1, 0);

    protected override int EventsThisFight(Fight fight, Side side) => side.DeadThisFight.Count;

    public override void AfterFriendlyDeath(Fight fight, Unit self, Unit dead) => CatchUp(fight, self);
}
