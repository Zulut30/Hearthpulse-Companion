using Hearthpulse.Core.Battlegrounds.Effects.Attack;
using Hearthpulse.Core.Battlegrounds.Effects.Spells;

namespace Hearthpulse.Core.Battlegrounds.Effects.Timewarped;

// Timewarped minions of today's pool that the text heuristic of tools/effects/gen_combat_relevance.py called "no effect"
// and the relevance audit (tests/Hearthpulse.Tests/Sim/RelevanceAuditTests.cs, 2026-11-19) found acting in combat. Numbers
// from the latest card text (HearthstoneJSON enUS, build 256589); no game of the owner has one of them in a fight.

/// <summary>
/// Timewarped Bonker (BG34_Giant_102): "Windfury. Rally: This plays 2 permanent Blood Gems on all your other minions."
/// Golden: "4". As Bonker: the Rally runs on each Windfury swing; "permanent" only matters after the fight.
/// </summary>
[CardEffect("BG34_Giant_102")]
internal sealed class TimewarpedBonker : CombatEffect
{
    public override void OnAttack(Fight fight, Unit self, Unit target)
    {
        var gems = self.Pick(2, 4);
        foreach (var unit in fight.Alive(self.Side, except: self))
        {
            BloodGems.Play(fight, self, unit, gems);
        }
    }
}

/// <summary>
/// Timewarped Sea Glass (BG34_Giant_110): "Divine Shield. Rally: Double this minion's stats. (2 times per combat.)" Golden:
/// "Triple". Its current Attack and Health are what it doubles; Counter = uses spent this fight.
/// </summary>
[CardEffect("BG34_Giant_110")]
internal sealed class TimewarpedSeaGlass : CombatEffect
{
    private const int Uses = 2;

    public override void OnAttack(Fight fight, Unit self, Unit target)
    {
        if (self.Counter >= Uses)
        {
            return;
        }

        self.Counter++;
        var times = self.Pick(1, 2);
        fight.Buff(self, self.Attack * times, Math.Max(0, self.Health) * times);
    }
}

/// <summary>
/// Timewarped Ghoul-acabra (BG34_Giant_609): "After you trigger a Deathrattle, give your minions +3/+2 permanently."
/// Golden: "+6/+4". Each friendly Deathrattle triggered in the fight, while it lives.
/// </summary>
[CardEffect("BG34_Giant_609")]
internal sealed class TimewarpedGhoulAcabra : CombatEffect
{
    public override void OnFriendlyDeathrattle(Fight fight, Unit self, Unit source)
    {
        if (self.IsAlive)
        {
            TimewarpedKit.BuffAll(fight, self.Side, Tribe.All, self.Pick(3, 6), self.Pick(2, 4));
        }
    }
}

/// <summary>Timewarped Saurolisk (BG34_Giant_202): "After you trigger a Deathrattle, gain +3/+2 permanently." Golden: "+6/+4".</summary>
[CardEffect("BG34_Giant_202")]
internal sealed class TimewarpedSaurolisk : CombatEffect
{
    public override void OnFriendlyDeathrattle(Fight fight, Unit self, Unit source)
    {
        if (self.IsAlive)
        {
            fight.Buff(self, self.Pick(3, 6), self.Pick(2, 4));
        }
    }
}

/// <summary>
/// Timewarped Whelp Smuggler (BG34_Giant_064): "Whenever a friendly minion gains Attack, give it +3 Health." Golden: "+6".
/// Every buff with Attack in it (itself included); the Health it gives has no Attack, so it does not set itself off.
/// </summary>
[CardEffect("BG34_Giant_064")]
internal sealed class TimewarpedWhelpSmuggler : CombatEffect
{
    public override void AfterFriendlyGainedStats(Fight fight, Unit self, Unit unit, int attack, int health)
    {
        if (attack > 0 && self.IsAlive && unit.IsAlive)
        {
            fight.Buff(unit, 0, self.Pick(3, 6));
        }
    }
}

/// <summary>
/// Timewarped Peggy (BG34_Giant_327): "Whenever a card is added to your hand, give your Pirates +1/+1." Golden: "… twice."
/// In combat the cards come from "Get ..." effects (<see cref="CardToHand"/>), as for The Shadow of Doubt.
/// </summary>
[CardEffect("BG34_Giant_327")]
internal sealed class TimewarpedPeggy : CombatEffect
{
    public override void AfterCardAddedToHand(Fight fight, Unit self, string cardId)
    {
        if (!self.IsAlive)
        {
            return;
        }

        for (var n = self.Pick(1, 2); n > 0; n--)
        {
            TimewarpedKit.BuffAll(fight, self.Side, Tribe.Pirate, 1, 1);
        }
    }
}

/// <summary>
/// Timewarped minions that "Get" cards in combat: what they get is random or a Tavern card, so each counts as one card for
/// "whenever a card is added to your hand" (The Shadow of Doubt, Timewarped Peggy). Normal gets one, golden two (their
/// golden texts say "2"), Timewarped Pashmar two kinds per trigger.
/// </summary>
internal static class TimewarpedGets
{
    public static void Get(Fight fight, Unit self, int kinds = 1)
    {
        for (var i = self.Pick(1, 2) * kinds; i > 0; i--)
        {
            fight.AddToHand(self.Side, CardToHand.AnyCard);
        }
    }
}

/// <summary>
/// "Deathrattle: Get ...": Timewarped Kil'rek (BG34_Giant_584, a random Demon), Pillager (BG34_Giant_204, a Tavern Coin),
/// Sapper (BG34_Giant_304, a Spitescale Special), Sporebat (BG34_Giant_582, a Tavern spell), Radio Star (BG34_Giant_330,
/// a copy of its killer), Riplash (BG34_Giant_325, a copy of the last Tavern spell cast: assumed one was cast); the buddies
/// Kil'rek (TB_BaconShop_HERO_37_Buddy, a random Demon) and Zippers (BG32_HERO_002_Buddy, a helpful card).
/// </summary>
[CardEffect("BG34_Giant_584")]
[CardEffect("BG34_Giant_204")]
[CardEffect("BG34_Giant_304")]
[CardEffect("BG34_Giant_582")]
[CardEffect("BG34_Giant_330")]
[CardEffect("BG34_Giant_325")]
[CardEffect("TB_BaconShop_HERO_37_Buddy")]
[CardEffect("BG32_HERO_002_Buddy")]
internal sealed class TimewarpedGetOnDeathrattle : CombatEffect
{
    public override void Deathrattle(Fight fight, Unit self) => TimewarpedGets.Get(fight, self);
}

/// <summary>
/// Timewarped Vaelastrasz (BG34_Giant_585) and the buddy Vaelastrasz (TB_BaconShop_HERO_56_Buddy): "Rally: Get a random
/// Dragon." Golden: "2 random Dragons".
/// </summary>
[CardEffect("BG34_Giant_585")]
[CardEffect("TB_BaconShop_HERO_56_Buddy")]
internal sealed class TimewarpedVaelastrasz : CombatEffect
{
    public override void OnAttack(Fight fight, Unit self, Unit target) => TimewarpedGets.Get(fight, self);
}

/// <summary>
/// Timewarped Calligrapher (BG34_PreMadeChamp_091): "Battlecry, Deathrattle, and Rally: Get a random Tavern spell." Golden:
/// "2". The buddy Tuskarr Raider (TB_BaconShop_HERO_18_Buddy) the same with a random Bounty. In combat: the Deathrattle
/// and each attack.
/// </summary>
[CardEffect("BG34_PreMadeChamp_091")]
[CardEffect("TB_BaconShop_HERO_18_Buddy")]
internal sealed class TimewarpedCalligrapher : CombatEffect
{
    public override void Deathrattle(Fight fight, Unit self) => TimewarpedGets.Get(fight, self);

    public override void OnAttack(Fight fight, Unit self, Unit target) => TimewarpedGets.Get(fight, self);
}

/// <summary>
/// "Avenge (X): Get ...": Timewarped Copter (BG34_Giant_302, X = 3, a random Mech), Mothership (BG34_Giant_598, 4, a Protoss
/// minion), Pashmar (BG34_Giant_211, 3, a Spellcraft spell and a Tavern spell), Traveler (BG34_Giant_605, 4, a Minor
/// Timewarp card), Prismscale (BG34_PreMadeChamp_022, 2, an Azerite Empowerment).
/// </summary>
[CardEffect("BG34_Giant_302")]
[CardEffect("BG34_Giant_598")]
[CardEffect("BG34_Giant_211")]
[CardEffect("BG34_Giant_605")]
[CardEffect("BG34_PreMadeChamp_022")]
internal sealed class TimewarpedGetOnAvenge : AvengeEffect
{
    protected override int Threshold(Unit self) => self.CardId switch
    {
        "BG34_Giant_598" or "BG34_Giant_605" => 4,
        "BG34_PreMadeChamp_022" => 2,
        _ => 3,
    };

    protected override void Avenge(Fight fight, Unit self) => TimewarpedGets.Get(fight, self, self.CardId == "BG34_Giant_211" ? 2 : 1);
}

/// <summary>
/// Timewarped Stormcloud (BG34_PreMadeChamp_031): "Deathrattle and Avenge (3): Get a Tavern Tempest." Golden: "2".
/// </summary>
[CardEffect("BG34_PreMadeChamp_031")]
internal sealed class TimewarpedStormcloud : AvengeEffect
{
    protected override int Threshold(Unit self) => 3;

    protected override void Avenge(Fight fight, Unit self) => TimewarpedGets.Get(fight, self);

    public override void Deathrattle(Fight fight, Unit self) => TimewarpedGets.Get(fight, self);
}

/// <summary>
/// Timewarped Geomancer (BG34_Giant_305): "Avenge (5): Get a Blood Gem. Your Blood Gems give an extra +1/+1 this game."
/// Both sentences are read as the Avenge's (assumption: the golden text is the same, so no "passive" doubling to tell
/// them apart); the better gems count for the Blood Gems played later in the fight.
/// </summary>
[CardEffect("BG34_Giant_305")]
internal sealed class TimewarpedGeomancer : AvengeEffect
{
    protected override int Threshold(Unit self) => 5;

    protected override void Avenge(Fight fight, Unit self)
    {
        fight.AddToHand(self.Side, "BG20_GEM");
        TimewarpedKit.ImproveBloodGems(self.Side, 1, 1);
    }
}

/// <summary>
/// Timewarped Henchman (BG34_Giant_593): "After you kill a second minion each combat, get a plain copy of it." Golden:
/// "2 plain copies"; the buddy Loyal Henchman (TB_BaconShop_HERO_45_Buddy) has the same text. "You kill" = an enemy
/// minion dies; Counter = enemy deaths it has seen this fight.
/// </summary>
[CardEffect("BG34_Giant_593")]
[CardEffect("TB_BaconShop_HERO_45_Buddy")]
internal sealed class TimewarpedHenchman : CombatEffect
{
    public override void AfterEnemyDeath(Fight fight, Unit self, Unit dead)
    {
        if (!self.IsAlive || ++self.Counter != 2)
        {
            return;
        }

        for (var i = self.Pick(1, 2); i > 0; i--)
        {
            fight.AddToHand(self.Side, dead.CardId);
        }
    }
}

/// <summary>
/// Timewarped Geist (BG34_Giant_034): "Deathrattle: Your Tavern spells give an extra +2/+2 this game." Golden: "+4/+4".
/// Raises the player's Tavern spell bonus that spells cast later in the fight read (<see cref="TavernSpells.Stats"/>).
/// </summary>
[CardEffect("BG34_Giant_034")]
internal sealed class TimewarpedGeist : CombatEffect
{
    public override void Deathrattle(Fight fight, Unit self)
    {
        var bonus = self.Pick(2, 4);
        self.Side.AddCounter(TavernSpells.SpellAttackTag, bonus);
        self.Side.AddCounter(TavernSpells.SpellHealthTag, bonus);
    }
}
