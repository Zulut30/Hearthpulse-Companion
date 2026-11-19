using Hearthpulse.Core.Battlegrounds.Effects.Deaths;

namespace Hearthpulse.Core.Battlegrounds.Effects.Ongoing;

// Pool minions whose Battlecry changes the board but whose text does nothing else in combat: they act only when an effect
// triggers their Battlecry there (Timewarped Greenskeeper: "Rally: Trigger your right-most Battlecry",
// Fight.TriggerBattlecry). The numbers are the captured TAG_SCRIPT_DATA_NUM_1/2 where real logs (2026-11-15) show the
// card carrying them, else the HearthstoneJSON latest text (2026-11-19). Battlecries that get cards, Gold or Discover
// only count as triggered. Battlecries with a chosen target that destroy or make Golden (Disguised Graverobber, Maw Caster,
// Elite Navigator, Captain Sanders) and "a random Bonus Keyword" (Bubble Gunner): CombatBattlecries.cs. Not modelled:
// gold-scaled ones (Lovesick Balladist).

/// <summary>Joyous (BG36_110): "Battlecry: Give your Deity +2/+1." Golden: "+4/+2". Script data 2/1 in a real log.</summary>
[CardEffect("BG36_110")]
internal sealed class Joyous : CombatEffect
{
    public override void Battlecry(Fight fight, Unit self)
    {
        var (attack, health) = DeathKit.Shown(self, (2, 1), (4, 2));
        Deity.Give(fight, self.Side, attack, health);
    }
}

/// <summary>
/// Hackerfin (BG31_148): "Battlecry: Give your other minions +3/+2. (Improved by each different Bonus Keyword in your
/// warband!)" Golden: "+6/+4". The captured script data holds the improved value (3/2 on a plain one in a real log).
/// </summary>
[CardEffect("BG31_148")]
internal sealed class Hackerfin : CombatEffect
{
    public override void Battlecry(Fight fight, Unit self)
    {
        var (attack, health) = DeathKit.Shown(self, (3, 2), (6, 4));
        foreach (var unit in fight.Alive(self.Side, except: self))
        {
            fight.Buff(unit, attack, health);
        }
    }
}

/// <summary>
/// Nerubian Deathswarmer (BG25_011): "Battlecry: Your Undead have +1 Attack this game (wherever they are)." Golden: "+2".
/// Living friendly Undead (itself included) get it now, Undead summoned later this fight when they arrive.
/// </summary>
[CardEffect("BG25_011")]
internal sealed class NerubianDeathswarmer : CombatEffect
{
    public override void Battlecry(Fight fight, Unit self)
    {
        var attack = DeathKit.Shown1(self, 1, 2);
        DeathKit.BuffAll(fight, self.Side, Tribe.Undead, attack, 0);
        self.Side.AddSummonBuff(Tribe.Undead, attack, 0);
    }
}

/// <summary>
/// Electric Synthesizer (BG26_963): "Battlecry and Start of Combat: Give your other Dragons +1/+1." Golden: "+2/+2". The
/// Start of Combat part resolved before capture; the Battlecry gives the living other friendly Dragons the stats. A real
/// log shows no script data on it, so the numbers are the card text's.
/// </summary>
[CardEffect("BG26_963")]
internal sealed class ElectricSynthesizer : CombatEffect
{
    public override void Battlecry(Fight fight, Unit self)
    {
        var amount = self.Pick(1, 2);
        foreach (var unit in fight.Alive(self.Side, Tribe.Dragon, except: self))
        {
            fight.Buff(unit, amount, amount);
        }
    }
}
