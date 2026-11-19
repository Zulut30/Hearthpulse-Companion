namespace Hearthpulse.Core.Battlegrounds.Effects.Player;

/// <summary>
/// Snarky Portrait (BG36_MagicItem_450): "Get 2 Snarky Sharks. Whenever a Fishbait dies, give your Beasts +4/+4 and improve
/// this."
/// <para>
/// In combat: whenever a Fishbait (BG36_205, ours or theirs: the text says "a Fishbait") dies, every living friendly Beast
/// gets the current +X/+Y, except Fishbait ("This can't gain stats"); then the number improves for the next one. The
/// number at capture is TAG_SCRIPT_DATA_NUM_1/2 of the trinket (<see cref="GrowingValue"/>; printed +4/+4 without it, then
/// missing_input). Assumption: the text does not say by how much it improves; +1/+1 (<see cref="Improvement"/>), no log
/// has the trinket.
/// </para>
/// </summary>
[CardEffect("BG36_MagicItem_450")]
internal sealed class SnarkyPortrait : CombatEffect
{
    public const string FishbaitId = "BG36_205";
    public const int Printed = 4;
    public const int Improvement = 1;

    public override string CapturedInput => GrowingValue.Description;

    public override bool HasInputs(SimSide side, string cardId) => GrowingValue.Known(side, cardId);

    // Counter / Counter2: the Attack / Health the next Fishbait death gives.
    public override void OnFightStart(Fight fight, Unit self) =>
        (self.Counter, self.Counter2) = GrowingValue.Current(self.Side.Info, self.CardId, Printed, Printed);

    public override void AfterFriendlyDeath(Fight fight, Unit self, Unit dead) => OnDeath(fight, self, dead);

    public override void AfterEnemyDeath(Fight fight, Unit self, Unit dead) => OnDeath(fight, self, dead);

    private static void OnDeath(Fight fight, Unit self, Unit dead)
    {
        if (dead.CardId != FishbaitId)
        {
            return;
        }

        var board = self.Side.Minions;
        for (var i = 0; i < board.Count; i++)
        {
            if (board[i] is { IsAlive: true } unit && unit.Is(Tribe.Beast) && unit.CardId != FishbaitId)
            {
                fight.Buff(unit, self.Counter, self.Counter2);
            }
        }

        self.Counter += Improvement;
        self.Counter2 += Improvement;
    }
}
