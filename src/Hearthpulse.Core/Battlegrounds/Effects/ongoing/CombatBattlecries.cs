namespace Hearthpulse.Core.Battlegrounds.Effects.Ongoing;

// Pool minions whose Battlecry picks a friendly minion to destroy or make Golden, or gains a random Bonus Keyword: they act
// in combat only when an effect triggers their Battlecry there (Timewarped Greenskeeper's Rally, Fight.TriggerBattlecry).
// Card text from HearthstoneJSON enUS, build 256589. A Battlecry triggered by an effect has no player to choose its target:
// the target is random among the valid ones (the usual Hearthstone rule for a targeted Battlecry cast by another effect),
// and never the minion itself, which a Battlecry played from the hand cannot target either. No real log has one of these
// Battlecries triggered in combat (2026-11-19).

/// <summary>Shared rules of the Battlecries in this file.</summary>
internal static class CombatBattlecry
{
    /// <summary>
    /// The Bonus Keywords: Taunt, Divine Shield, Windfury, Venomous and Reborn, as placeholder cards for the random pick.
    /// The kept logs of 2026-11-15 show Bubble Gunner and other "random Bonus Keyword" cards giving Taunt, Divine Shield,
    /// Windfury and Reborn; Venomous completes the Bonus Keyword list of the game's tooltip. Stealth is not one.
    /// </summary>
    private static readonly SimMinion[] BonusKeywords =
    [
        SimMinion.Create("bonus:" + nameof(Keyword.Taunt), 0, 1, keywords: Keyword.Taunt),
        SimMinion.Create("bonus:" + nameof(Keyword.DivineShield), 0, 1, keywords: Keyword.DivineShield),
        SimMinion.Create("bonus:" + nameof(Keyword.Windfury), 0, 1, keywords: Keyword.Windfury),
        SimMinion.Create("bonus:" + nameof(Keyword.Venomous), 0, 1, keywords: Keyword.Venomous),
        SimMinion.Create("bonus:" + nameof(Keyword.Reborn), 0, 1, keywords: Keyword.Reborn),
    ];

    /// <summary>
    /// Gives <paramref name="unit"/> <paramref name="count"/> random Bonus Keywords it does not have, one after another (a
    /// keyword it already has is never picked, as random keyword effects skip what the minion has).
    /// </summary>
    public static void GainBonusKeywords(Fight fight, Unit unit, int count)
    {
        var missing = new List<SimMinion>(BonusKeywords.Length);
        for (var i = 0; i < count; i++)
        {
            missing.Clear();
            foreach (var keyword in BonusKeywords)
            {
                if (!Has(unit, keyword.BaseKeywords))
                {
                    missing.Add(keyword);
                }
            }

            if (missing.Count == 0)
            {
                return;
            }

            fight.GiveKeywords(unit, missing[PickKeyword(fight, unit, missing)].BaseKeywords);
        }
    }

    /// <summary>Makes <paramref name="count"/> random other living friendly minions passing <paramref name="eligible"/> Golden.</summary>
    public static void MakeGolden(Fight fight, Unit self, int count, Func<Unit, bool> eligible)
    {
        var candidates = fight.Alive(self.Side, except: self).FindAll(u => !u.Golden && eligible(u));
        foreach (var unit in fight.RandomDistinct(candidates, count, self))
        {
            BecomeGolden(unit);
        }
    }

    /// <summary>
    /// The minion turns into the golden version of its card: the printed stats are added once more (the golden card's
    /// printed stats are twice the normal ones), its gained stats and keywords stay, and from now on its effect reads the
    /// golden text. Not a stat gain from a buff: "whenever this gains stats" hooks do not see it.
    /// </summary>
    public static void BecomeGolden(Unit unit)
    {
        var spec = unit.Spec;
        var attack = Math.Max(0, spec.RebornAttack);
        var health = Math.Max(0, spec.BaseHealth);
        unit.Spec = spec with { Golden = true, RebornAttack = attack * 2, BaseHealth = health * 2 };
        unit.Attack += attack;
        unit.Health += health;
        unit.MaxHealth += health;
    }

    /// <summary>
    /// Destroys a random other living friendly Undead for <paramref name="self"/> and returns it as it was (null when there
    /// is none).
    /// </summary>
    public static Unit? DestroyFriendlyUndead(Fight fight, Unit self)
    {
        if (fight.RandomFriendly(self, Tribe.Undead) is not { } victim)
        {
            return null;
        }

        fight.Destroy(victim, self);
        return victim;
    }

    private static bool Has(Unit unit, Keyword keyword) =>
        keyword == Keyword.Windfury ? unit.AttacksPerTurn >= 2 : (unit.Keywords & keyword) != 0;

    /// <summary>The random Bonus Keyword (one decision of <paramref name="unit"/>'s Battlecry per keyword).</summary>
    private static int PickKeyword(Fight fight, Unit unit, List<SimMinion> missing) => fight.Choose(ChoiceKind.RandomCard, unit, missing);
}

/// <summary>Bubble Gunner (BG31_149): "Battlecry: Gain a random Bonus Keyword." Golden: "Gain 2 random Bonus Keywords".</summary>
[CardEffect("BG31_149")]
internal sealed class BubbleGunner : CombatEffect
{
    public override void Battlecry(Fight fight, Unit self) => CombatBattlecry.GainBonusKeywords(fight, self, self.Pick(1, 2));
}

/// <summary>
/// Captain Sanders (BG25_034): "Battlecry: Make a friendly minion from Tier 6 or below Golden." Golden: "Make two friendly
/// minions". A minion without a known tier (a token made in combat) counts as one from Tier 6 or below.
/// </summary>
[CardEffect("BG25_034")]
internal sealed class CaptainSanders : CombatEffect
{
    public override void Battlecry(Fight fight, Unit self) =>
        CombatBattlecry.MakeGolden(fight, self, self.Pick(1, 2), static u => u.Spec.TechLevel <= 6);
}

/// <summary>
/// Elite Navigator (BG32_231): "Battlecry: Make a friendly Pirate from Tier 4 or below Golden." Golden: "Make 2 friendly
/// Pirates".
/// </summary>
[CardEffect("BG32_231")]
internal sealed class EliteNavigator : CombatEffect
{
    public override void Battlecry(Fight fight, Unit self) =>
        CombatBattlecry.MakeGolden(fight, self, self.Pick(1, 2), static u => u.Is(Tribe.Pirate) && u.Spec.TechLevel <= 4);
}

/// <summary>
/// Disguised Graverobber (BG28_303): "Battlecry: Destroy a friendly Undead to get a plain copy of it." Golden: "get 2 plain
/// copies". The copy is the card with its printed stats and keywords (golden if the destroyed one was), added to the hand.
/// </summary>
[CardEffect("BG28_303")]
internal sealed class DisguisedGraverobber : CombatEffect
{
    public override void Battlecry(Fight fight, Unit self)
    {
        if (CombatBattlecry.DestroyFriendlyUndead(fight, self) is not { } victim)
        {
            return;
        }

        var spec = victim.Spec;
        var copy = SimMinion.Create(spec.CardId, spec.RebornAttack, spec.BaseHealth > 0 ? spec.BaseHealth : Math.Max(1, spec.Health),
            spec.Tribes, spec.BaseKeywords, spec.Golden, spec.Mechanics) with { TechLevel = spec.TechLevel };
        for (var i = 0; i < self.Pick(1, 2); i++)
        {
            fight.AddToHand(self.Side, copy.CardId, copy);
        }
    }
}

/// <summary>
/// Maw Caster (BG32_340): "Battlecry: Destroy a friendly Undead to Discover an Undead." Golden: "Discover 2 Undead". The
/// Discovered card is the player's pick, not modelled: it counts as a card added to the hand.
/// </summary>
[CardEffect("BG32_340")]
internal sealed class MawCaster : CombatEffect
{
    public override void Battlecry(Fight fight, Unit self)
    {
        if (CombatBattlecry.DestroyFriendlyUndead(fight, self) is null)
        {
            return;
        }

        for (var i = 0; i < self.Pick(1, 2); i++)
        {
            fight.AddToHand(self.Side, Spells.CardToHand.AnyCard);
        }
    }
}
