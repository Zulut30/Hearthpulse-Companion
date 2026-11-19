namespace Hearthpulse.Core.Battlegrounds.Effects.Spells;

/// <summary>
/// Boon of Beetles (BG28_603, Tavern spell): "When you have space in combat, summon two {0}/{1} Beetles and give them Taunt.
/// (2 left!)"
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Cast in the Tavern, the spell waits in the player's SECRET zone like other Tavern spells with a combat effect
/// (Power.log 2026-11-15: Brood of Nozdormu went from the hand to SECRET and left the game when its Start of Combat
/// resolved, L801212/L825221); in the shop the Boon carries TAG_SCRIPT_DATA_NUM_1/2 = 2/2 (the Beetle) and NUM_3 = 2 (the
/// Beetles left), OBJECTIVE and START_OF_COMBAT (L1523707). The tracker passes the local player's waiting spells as extras
/// with those numbers (<see cref="SimInputs.ObjectiveTags"/>); the opponent's secret zone is hidden, so theirs is never
/// simulated (nor counted).</item>
/// <item>Still waiting at the capture means Beetles are owed: the board had no space when combat started. Each time a
/// space is there (at the start of the fight, then after each friendly death) Beetles are summoned at the right end, one
/// per space, until none are left. The Beetle is the Boon's NUM_1/2 (the player's Beetle, bonus included), else the
/// player's Beetle from the capture, else 2/2; Beetle bonuses gained in the fight apply as to any Beetle.</item>
/// <item>No log has the Boon cast (2026-11-19): the waiting zone and the meaning of NUM_3 follow the shop entity's tags and
/// the text.</item>
/// </list>
/// </remarks>
[CardEffect("BG28_603")]
internal sealed class BoonOfBeetles : CombatEffect
{
    public const string CardId = "BG28_603";

    public override void OnFightStart(Fight fight, Unit self)
    {
        self.Counter = self.Side.Info.ExtraTag(CardId, "TAG_SCRIPT_DATA_NUM_3") ?? 2;
        SummonWhileSpace(fight, self);
    }

    public override void AfterFriendlyDeath(Fight fight, Unit self, Unit dead) => SummonWhileSpace(fight, self);

    private static void SummonWhileSpace(Fight fight, Unit self)
    {
        var side = self.Side;
        while (self.Counter > 0 && !side.IsFull)
        {
            var (attack, health) = Beetle(side.Info);
            if (fight.Summon(side, Tokens.BeetleId, attack, health, Keyword.Taunt, tribes: Tribe.Beast) is null)
            {
                return;
            }

            self.Counter--;
        }
    }

    private static (int Attack, int Health) Beetle(SimSide info) =>
        info.ExtraTag(CardId, "TAG_SCRIPT_DATA_NUM_1") is > 0 and var attack
            ? (attack, Math.Max(1, info.ExtraTag(CardId, "TAG_SCRIPT_DATA_NUM_2") ?? 1))
            : SimInputs.BeetleStatsOf(info) ?? (2, 2);
}
