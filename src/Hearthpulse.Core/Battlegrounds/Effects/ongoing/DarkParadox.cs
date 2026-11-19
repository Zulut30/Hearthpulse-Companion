namespace Hearthpulse.Core.Battlegrounds.Effects.Ongoing;

// Dark Paradox (BG36_360): "This has a different Dark Gift, stats, and Tier each game!" In a game it is one of five
// forms, each its own card (BG36_360t3/t4/t5/t6/t9, golden BG36_360_Gt3...). Form t4 = Toreth's Blessing ("Divine
// Shield takes N hits to break"), form t9 = Golemancy ("Deathrattle: Summon a Golem with this minion's stats", golden:
// double) and form t6 ("Has +4/+4 for each Battlecry you've triggered this game") matter in combat; t6 only when a
// Battlecry is triggered there (Timewarped Greenskeeper). Form t3 ("whenever you play a card") acts in the Tavern only;
// form t5 ("Rally: Get a random minion of your most common type") puts a random card in the hand, which is not modelled
// (it could matter only to a later summon from the hand). The classes below register those forms by card id, and
// the same Dark Gifts by their enchantment ids, in case the log shows the gift as an enchantment on a minion. The
// untransformed card (BG36_360) has no gift of its own (DarkParadoxUntransformed).

/// <summary>"This minion's Divine Shield takes {0} hits to break": every hit that would break it lets it take one hit less instead.</summary>
internal abstract class MultiHitShield : CombatEffect
{
    /// <summary>Hits one Divine Shield takes.</summary>
    protected abstract int Hits(Unit self);

    public sealed class State
    {
        /// <summary>Hits left before the current shield breaks; 0 without a shield.</summary>
        public int Remaining;
    }

    /// <summary>A shield with no hit count yet (captured, or given by another effect) starts with all of its hits.</summary>
    public override void Refresh(Fight fight, Unit self)
    {
        var state = UnitState<State>.Of(self);
        if (self.DivineShield && state.Remaining <= 0)
        {
            state.Remaining = Hits(self);
        }
    }

    /// <summary>The engine removes the shield on a hit; hand it back while hits are left.</summary>
    public override void OnFriendlyShieldLost(Fight fight, Unit self, Unit unit)
    {
        if (unit != self)
        {
            return;
        }

        var state = UnitState<State>.Of(self);
        if (state.Remaining <= 0)
        {
            return;
        }

        if (--state.Remaining > 0 && self.IsOnBoard)
        {
            if (fight.Tracing) fight.Trace($"{self} Divine Shield holds ({state.Remaining} hits left)");
            fight.GiveKeywords(self, Keyword.DivineShield);
        }
    }
}

/// <summary>Dark Paradox, Divine Shield form (BG36_360t4): shield takes {0} hits to break, 3 (golden 4; the captured TAG_SCRIPT_DATA_NUM_1).</summary>
[CardEffect("BG36_360t4")]
[CardEffect("BG36_360_Gt4", Golden = GoldenFilter.GoldenOnly)]
internal sealed class DarkParadoxShield : MultiHitShield
{
    protected override int Hits(Unit self) => self.Spec.ScriptData1 > 0 ? self.Spec.ScriptData1 : self.Pick(3, 4);
}

/// <summary>Toreth's Blessing (Dark Gift enchantment BG36_MidGameEffect_000t15e): "This minion's Divine Shield takes 3 hits to break."</summary>
[CardEffect("BG36_MidGameEffect_000t15e")]
internal sealed class TorethsBlessing : MultiHitShield
{
    protected override int Hits(Unit self) => 3;
}

/// <summary>
/// Dark Paradox, Golem form (BG36_360t9): "Deathrattle: Summon a Golem with this minion's stats." Golden (BG36_360_Gt9):
/// "with double this minion's stats". The Golem is a normal Deserted Golem with the Attack and maximum Health the minion had.
/// </summary>
[CardEffect("BG36_360t9")]
[CardEffect("BG36_360_Gt9", Golden = GoldenFilter.GoldenOnly)]
internal sealed class DarkParadoxGolem : CombatEffect
{
    public override void Deathrattle(Fight fight, Unit self) => Golemancy.SummonGolem(fight, self, self.Pick(1, 2));
}

/// <summary>Golemancy (Dark Gift enchantment BG36_MidGameEffect_000t61e): "Deathrattle: Summon a Golem with this minion's stats."</summary>
[CardEffect("BG36_MidGameEffect_000t61e")]
internal sealed class Golemancy : CombatEffect
{
    public override void Deathrattle(Fight fight, Unit self) => SummonGolem(fight, self, 1);

    internal static void SummonGolem(Fight fight, Unit self, int factor) =>
        fight.SummonAt(self, Tokens.Make(Tokens.DesertedGolemId, self.Attack * factor, Math.Max(self.MaxHealth, 1) * factor, Tribe.None));
}

/// <summary>
/// Dark Paradox, Battlecry form (BG36_360t6): "Has +4/+4 for each Battlecry you've triggered this game (wherever this
/// is)." Golden (BG36_360_Gt6): "+8/+8". The captured stats hold the Battlecries before the fight; each one triggered in
/// combat (<see cref="Fight.TriggerBattlecry"/>, multipliers included) adds the bonus while it is on the board. No log
/// shows this form (2026-11-19): +4/+4 is the HearthstoneJSON latest text, no captured tag is known to carry it.
/// </summary>
[CardEffect("BG36_360t6")]
[CardEffect("BG36_360_Gt6", Golden = GoldenFilter.GoldenOnly)]
internal sealed class DarkParadoxBattlecries : CombatEffect
{
    public override void AfterFriendlyBattlecry(Fight fight, Unit self, Unit source)
    {
        if (self.IsAlive)
        {
            var bonus = self.Pick(4, 8);
            fight.Buff(self, bonus, bonus);
        }
    }
}

/// <summary>
/// Dark Paradox, untransformed (BG36_360): "This has a different Dark Gift, stats, and Tier each game!" The text itself
/// does nothing in combat: the gift belongs to the transformed forms above (real logs of 2026-11-15 show only those in
/// play, hand and the Tavern), and a gift another card gives it is an enchantment with its own effect. A captured
/// BG36_360 therefore fights as a plain minion with its captured stats and keywords.
/// </summary>
[CardEffect("BG36_360")]
internal sealed class DarkParadoxUntransformed : CombatEffect
{
}
