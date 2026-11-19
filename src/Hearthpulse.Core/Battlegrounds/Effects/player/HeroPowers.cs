using Hearthpulse.Core.Battlegrounds.Effects.Ongoing;

namespace Hearthpulse.Core.Battlegrounds.Effects.Player;

// Hero powers whose text acts in combat after the boards are captured (PlayerCardTable), from their English card text
// (HearthstoneJSON latest, checked 2026-11-19). The hero power is an off-board extra unit of its side, like a trinket. A
// number the power shows is read from its captured TAG_SCRIPT_DATA_NUM_1/2 when present (our own power; GrowingValue),
// the text's number otherwise: balance changes on the server without a new client.

/// <summary>Sprout It Out! (TB_BaconShop_HP_107): "Give +1/+2 and Taunt to minions you summon during combat."</summary>
[CardEffect("TB_BaconShop_HP_107")]
internal sealed class SproutItOut : CombatEffect
{
    public override void OnFightStart(Fight fight, Unit self) => (self.Counter, self.Counter2) = GrowingValue.Current(self.Side.Info, self.CardId, 1, 2);

    public override void AfterFriendlySummoned(Fight fight, Unit self, Unit summoned)
    {
        fight.Buff(summoned, self.Counter, self.Counter2);
        fight.GiveKeywords(summoned, Keyword.Taunt);
    }
}

/// <summary>Glory of Combat (BG20_HERO_100p): "After a friendly minion kills an enemy, give it +1 Attack permanently."</summary>
[CardEffect("BG20_HERO_100p")]
internal sealed class GloryOfCombat : CombatEffect
{
    private sealed class Credited
    {
        public bool Done;
    }

    // Counter: the Attack given, read once per fight.
    public override void OnFightStart(Fight fight, Unit self) => self.Counter = GrowingValue.Current(self.Side.Info, self.CardId, 1, 0).Attack;

    public override void AfterFriendlyDealtDamage(Fight fight, Unit self, Unit dealer, Unit victim, int amount)
    {
        // Later damage to a minion that is already dying does not kill it again.
        if (victim.IsDying && victim.Killer == dealer && dealer.IsOnBoard && UnitState<Credited>.Of(victim) is { Done: false } credit)
        {
            credit.Done = true;
            fight.Buff(dealer, self.Counter, 0);
        }
    }
}

/// <summary>
/// Stormpike Strength (BG22_HERO_003p, Vanndar): "When you have space in combat, summon a copy of your highest-Health minion.
/// (Unlocks on Turn 7.)" Frostwolf Fervor (BG22_HERO_002p, Drek'Thar): the same with the highest-Attack minion.
/// <para>
/// With space when the fight starts, the copy is already on the captured board: a minion whose CREATOR is the hero power
/// (<see cref="SimMinion.CreatorCardId"/>), then nothing is owed. What is left is a full board without such a copy: the
/// copy comes once a friendly minion has died and left room. One copy per combat (the text names one summon); the copy has
/// the minion's current stats and keywords, minus the summon auras it gets again (Feral Talisman...). Before turn 7 (when
/// the turn is known) the power does nothing, whatever its captured flag says (simreplay of the 2026-11-15 log: the copy
/// the simulator made on turns 3-5 never came in the log). The copy at Start of Combat (CREATOR = the power) is in that
/// log from turn 7 on.
/// </para>
/// </summary>
[CardEffect("BG22_HERO_003p")]
[CardEffect("BG22_HERO_002p")]
internal sealed class SpaceCopyHeroPower : CombatEffect
{
    public const int UnlockTurn = 7;

    public override void OnFightStart(Fight fight, Unit self)
    {
        var turn = self.Side.Info.Turn;
        // The flag (NUM_1) is 1 from the start of the game while the power is still locked (real log 2026-11-15, game 2:
        // 1 on turns 1-6, first copy and first 0 on turn 7, line 205932): it says "owed" only once the power is unlocked.
        var unlocked = turn == 0 || turn >= UnlockTurn;
        var copied = PlayerCards.AnyCreatedBy(self.Side.Info, self.CardId);
        // Counter: 1 = a copy is still owed.
        self.Counter = unlocked && (SimInputs.SpaceCopyOwed(self.Side.Info, self.CardId) ?? (self.Side.IsFull && !copied)) ? 1 : 0;
    }

    public override void AfterFriendlyDeath(Fight fight, Unit self, Unit dead)
    {
        if (self.Counter == 0 || self.Side.IsFull)
        {
            return;
        }

        var byHealth = self.CardId == "BG22_HERO_003p";
        if (fight.Best(fight.Alive(self.Side), u => byHealth ? u.Health : u.Attack, self) is not { } best)
        {
            return;
        }

        self.Counter = 0;
        var (auraAttack, auraHealth) = self.Side.SummonBuffsFor(best);
        var copy = best.ToSimMinion() with
        {
            EntityId = 0,
            Attack = Math.Max(0, best.Attack - auraAttack),
            Health = best.Health - auraHealth,
            MaxHealth = Math.Max(0, best.MaxHealth - auraHealth),
            CreatorCardId = self.CardId,
        };
        fight.Summon(self.Side, copy, self.Side.IndexOf(best) + 1);
    }
}
