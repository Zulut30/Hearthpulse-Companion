namespace Hearthpulse.Core.Battlegrounds.Effects.Player;

/// <summary>
/// Soul Fermenter (BG35_MagicItem_732): "Start of Combat: Destroy your 3 left-most minions. After your last minion dies,
/// resummon them."
/// <para>
/// The destroy resolved before the capture; the capture remembers the destroyed minions as they were when they left the
/// board (<see cref="SimSide.RemovedByExtras"/>). After the last living friendly minion dies (its deathrattles and Reborn
/// resolved and nothing is left), they come back once, left to right in their old order, with the stats and keywords they
/// had (no enchantment effects: the capture keeps none for removed minions). If the board emptied already during Start of
/// Combat they came back before the capture (a captured minion created by the trinket): nothing is owed.
/// </para>
/// <para>
/// Assumptions, not checked against a real log (none of the owner's logs has the trinket): "them" are exact copies, the
/// removal happens inside the trinket's TRIGGER block and the copies' CREATOR is the trinket. Both sides.
/// </para>
/// </summary>
[CardEffect("BG35_MagicItem_732")]
internal sealed class SoulFermenter : CombatEffect
{
    public override string CapturedInput => "существа, уничтоженные тринкетом в начале боя (SimSide.RemovedByExtras)";

    // Counter: 1 = the destroyed minions are still owed.
    public override void OnFightStart(Fight fight, Unit self) => self.Counter = PlayerCards.Owed(self.Side.Info, self.CardId) is not null ? 1 : 0;

    public override void AfterFriendlyDeath(Fight fight, Unit self, Unit dead)
    {
        if (self.Counter == 0 || PlayerCards.AnyAlive(self.Side) || PlayerCards.Owed(self.Side.Info, self.CardId) is not { } removed)
        {
            return;
        }

        self.Counter = 0;
        foreach (var minion in removed)
        {
            fight.Summon(self.Side, PlayerCards.CopyOf(minion, self.CardId), self.Side.Count);
        }
    }
}
