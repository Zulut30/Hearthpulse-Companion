using Hearthpulse.Core.Battlegrounds.Effects.Timewarped;

namespace Hearthpulse.Core.Battlegrounds.Effects.Ongoing;

// Buddies (TB_BaconShop_HERO_*_Buddy, BG2x_HERO_*_Buddy): in today's game through the anomaly Bring in the Buddies, which
// puts them in the Tavern (hsbg.cards, 2026-11-19). Found by the relevance audit: no relevance table knew them. These are
// the buddies whose combat text maps onto the engine's hooks directly; the rest of the combat buddies are labelled
// Relevant without an effect (docs/bg-sim-matrix.md). Text and golden text: HearthstoneJSON enUS, build 256589; no game
// of the owner has a buddy in a fight.

/// <summary>
/// Spirit of Air (TB_BaconShop_HERO_76_Buddy): "Deathrattle: Give a random friendly minion Windfury, Divine Shield, and
/// Taunt." Golden: "2 random friendly minions".
/// </summary>
[CardEffect("TB_BaconShop_HERO_76_Buddy")]
internal sealed class SpiritOfAir : CombatEffect
{
    public override void Deathrattle(Fight fight, Unit self)
    {
        foreach (var unit in Deaths.DeathKit.Choose(fight, fight.Alive(self.Side), self.Pick(1, 2), self))
        {
            fight.GiveKeywords(unit, Keyword.Windfury | Keyword.DivineShield | Keyword.Taunt);
        }
    }
}

/// <summary>
/// Sinestra (TB_BaconShop_HERO_52_Buddy): "Whenever a friendly minion gains Attack during combat, give it +1 Health
/// permanently." Golden: "+2". As Timewarped Whelp Smuggler.
/// </summary>
[CardEffect("TB_BaconShop_HERO_52_Buddy")]
internal sealed class Sinestra : CombatEffect
{
    public override void AfterFriendlyGainedStats(Fight fight, Unit self, Unit unit, int attack, int health)
    {
        if (attack > 0 && self.IsAlive && unit.IsAlive)
        {
            fight.Buff(unit, 0, self.Pick(1, 2));
        }
    }
}

/// <summary>
/// Tentacle of C'Thun (TB_BaconShop_HERO_29_Buddy): "After a different friendly minion gains stats, gain +1/+1 until next
/// turn." Golden: "+2/+2". The fight is within the turn.
/// </summary>
[CardEffect("TB_BaconShop_HERO_29_Buddy")]
internal sealed class TentacleOfCThun : CombatEffect
{
    public override void AfterFriendlyGainedStats(Fight fight, Unit self, Unit unit, int attack, int health)
    {
        if (unit != self && self.IsAlive && unit.CardId != self.CardId)
        {
            var bonus = self.Pick(1, 2);
            fight.Buff(self, bonus, bonus);
        }
    }
}

/// <summary>
/// Baby Y'Shaarj (TB_BaconShop_HERO_92_Buddy): "After you summon a minion of your Tier, give your minions +1/+1." Golden:
/// "+2/+2". The Tier: the owner's Tavern tier at capture; the minion's: its tier (tokens have none).
/// </summary>
[CardEffect("TB_BaconShop_HERO_92_Buddy")]
internal sealed class BabyYShaarj : CombatEffect
{
    public override void AfterFriendlySummoned(Fight fight, Unit self, Unit summoned)
    {
        if (self.IsAlive && summoned.Spec.TechLevel > 0 && summoned.Spec.TechLevel == self.Side.Info.TavernTier)
        {
            var bonus = self.Pick(1, 2);
            TimewarpedKit.BuffAll(fight, self.Side, Tribe.All, bonus, bonus);
        }
    }
}

/// <summary>
/// Icesnarl the Mighty (BG20_HERO_100_Buddy): "After a friendly minion kills an enemy, gain +3 Health permanently."
/// Golden: "+6". A kill as for Mutalisk.
/// </summary>
[CardEffect("BG20_HERO_100_Buddy")]
internal sealed class IcesnarlTheMighty : CombatEffect
{
    private sealed class Credited
    {
        public bool Done;
    }

    public override void AfterFriendlyDealtDamage(Fight fight, Unit self, Unit dealer, Unit victim, int amount)
    {
        if (self.IsAlive && victim.IsDying && victim.Killer == dealer && UnitState<Credited>.Of(victim) is { Done: false } credit)
        {
            credit.Done = true;
            fight.Buff(self, 0, self.Pick(3, 6));
        }
    }
}

/// <summary>Many Whelps (BG22_HERO_305_Buddy): "Whenever you summon a Whelp, gain +2/+2 permanently." Golden: "+4/+4".</summary>
/// <remarks>A Whelp: Onyxia's Broodmother token (BG22_HERO_305t), the minion whose name the text means.</remarks>
[CardEffect("BG22_HERO_305_Buddy")]
internal sealed class ManyWhelps : CombatEffect
{
    public const string WhelpId = "BG22_HERO_305t";

    public override void WheneverFriendlySummoned(Fight fight, Unit self, Unit summoned)
    {
        if (self.IsAlive && summoned.CardId == WhelpId)
        {
            var bonus = self.Pick(2, 4);
            fight.Buff(self, bonus, bonus);
        }
    }
}

/// <summary>Monstrosity (BG20_HERO_282_Buddy): "After a friendly minion dies, gain its Attack." Golden: "… twice."</summary>
[CardEffect("BG20_HERO_282_Buddy")]
internal sealed class Monstrosity : CombatEffect
{
    public override void AfterFriendlyDeath(Fight fight, Unit self, Unit dead)
    {
        if (self.IsAlive && dead != self)
        {
            fight.Buff(self, self.Pick(1, 2) * Math.Max(0, dead.Attack), 0);
        }
    }
}

/// <summary>
/// Wandering Treant (TB_BaconShop_HERO_95_Buddy): "Whenever a friendly Taunt minion is attacked, give your minions +1/+1
/// permanently." Golden: "+2/+2". Before the hit.
/// </summary>
[CardEffect("TB_BaconShop_HERO_95_Buddy")]
internal sealed class WanderingTreant : CombatEffect
{
    public override void OnFriendlyAttacked(Fight fight, Unit self, Unit defender, Unit attacker)
    {
        if (self.IsAlive && defender.Taunt)
        {
            var bonus = self.Pick(1, 2);
            TimewarpedKit.BuffAll(fight, self.Side, Tribe.All, bonus, bonus);
        }
    }
}

/// <summary>Tamuzo (BG23_HERO_201_Buddy): "After you summon a minion in combat, double its stats." Golden: "triple".</summary>
[CardEffect("BG23_HERO_201_Buddy")]
internal sealed class Tamuzo : CombatEffect
{
    public override void AfterFriendlySummoned(Fight fight, Unit self, Unit summoned)
    {
        if (self.IsAlive && summoned.IsAlive)
        {
            var times = self.Pick(1, 2);
            fight.Buff(summoned, summoned.Attack * times, Math.Max(0, summoned.Health) * times);
        }
    }
}

/// <summary>Arfus (TB_BaconShop_HERO_22_Buddy): "After a friendly minion is Reborn, give it this minion's Attack." Golden: "twice".</summary>
[CardEffect("TB_BaconShop_HERO_22_Buddy")]
internal sealed class Arfus : CombatEffect
{
    public override void AfterFriendlyReborn(Fight fight, Unit self, Unit reborn)
    {
        if (self.IsAlive && reborn != self)
        {
            fight.Buff(reborn, self.Pick(1, 2) * self.Attack, 0);
        }
    }
}

/// <summary>
/// Shadowy Construct (BG25_HERO_103_Buddy): "Whenever a friendly minion dies, gain its maximum stats. (Once per combat.)"
/// Golden: "(Twice per combat.)" Counter = uses this fight.
/// </summary>
[CardEffect("BG25_HERO_103_Buddy")]
internal sealed class ShadowyConstruct : CombatEffect
{
    public override void AfterFriendlyDeath(Fight fight, Unit self, Unit dead)
    {
        if (!self.IsAlive || dead == self || self.Counter >= self.Pick(1, 2))
        {
            return;
        }

        self.Counter++;
        fight.Buff(self, Math.Max(0, dead.Attack), Math.Max(0, dead.MaxHealth));
    }
}

/// <summary>
/// Valithria Dreamwalker (TB_BaconShop_HERO_53_Buddy): "Whenever a Dragon enters your warband or the Tavern, gain +1/+1."
/// Golden: "+2/+2". In combat a Dragon summoned on its side enters the warband (assumption: summons count, no log).
/// </summary>
[CardEffect("TB_BaconShop_HERO_53_Buddy")]
internal sealed class ValithriaDreamwalker : CombatEffect
{
    public override void WheneverFriendlySummoned(Fight fight, Unit self, Unit summoned)
    {
        if (self.IsAlive && summoned.Is(Tribe.Dragon) && summoned.Tribes != Tribe.None)
        {
            var bonus = self.Pick(1, 2);
            fight.Buff(self, bonus, bonus);
        }
    }
}

/// <summary>
/// Titanic Guardian (TB_BaconShop_HERO_39_Buddy): "Whenever a different minion in your hand or board gains Health, this
/// also gains it." Golden: "twice that amount". In combat: friendly board minions. Another Titanic Guardian's gain is not
/// passed back (assumption: two Guardians would otherwise feed each other without end).
/// </summary>
[CardEffect("TB_BaconShop_HERO_39_Buddy")]
internal sealed class TitanicGuardian : CombatEffect
{
    public override void AfterFriendlyGainedStats(Fight fight, Unit self, Unit unit, int attack, int health)
    {
        if (health > 0 && unit != self && self.IsAlive && unit.CardId != self.CardId)
        {
            fight.Buff(self, 0, health * self.Pick(1, 2));
        }
    }
}
