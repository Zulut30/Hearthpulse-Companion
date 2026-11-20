using Hearthpulse.Core.Battlegrounds.Effects.Deaths;
using Hearthpulse.Core.Battlegrounds.Effects.Player;

namespace Hearthpulse.Core.Battlegrounds.Effects.Ongoing;

// Buddies of today's game (anomaly Bring in the Buddies, hsbg.cards 2026-11-19) the relevance audit left without an effect
// (TASK-2026-11-19, package E7b). Text and golden text: HearthstoneJSON enUS, build 256589; no game of the owner has one of
// these buddies (D:\Hearthstone\Logs, 2026-11-20), so the order details below follow the card text and the engine's rules.

/// <summary>
/// Eclipsion Illidari (TB_BaconShop_HERO_08_Buddy): "Your first minion that attacks has "Immune while Attacking" for one
/// attack only." Golden: "Your first two minions that attack".
/// </summary>
/// <remarks>
/// The first friendly attacks after the capture (the capture is at the first attack, so it is the fight's first ones) made
/// while the buddy is alive, itself included; one grant per attack. A Windfury minion's second attack is another attack:
/// with the golden buddy it can take the second grant. Two buddies give the same first attacker both grants, which changes
/// nothing (assumption: each buddy counts its own). Its enchantment "Darkened Heart" (TB_BaconShop_HERO_08_Buddy_e) is the
/// visual of the grant and is not read.
/// </remarks>
[CardEffect("TB_BaconShop_HERO_08_Buddy")]
internal sealed class EclipsionIllidari : CombatEffect
{
    private sealed class Grants
    {
        public int Given;
    }

    public override void OnFriendlyAttack(Fight fight, Unit self, Unit attacker, Unit target)
    {
        if (!self.IsAlive)
        {
            return;
        }

        var grants = UnitState<Grants>.Of(self);
        if (grants.Given >= self.Pick(1, 2))
        {
            return;
        }

        grants.Given++;
        fight.GiveImmuneForThisAttack(attacker);
    }
}

/// <summary>
/// Elementium Squirrel Bomb (TB_BaconShop_HERO_17_Buddy, Mech Beast): "Deathrattle: Deal 4 damage to a random enemy minion
/// for each of your Mechs that died this combat." Golden: "8 damage".
/// </summary>
/// <remarks>
/// "This combat": the friendly Mechs that died in the fight, the bomb itself included (it has died when its Deathrattle
/// runs), and those that died at Start of Combat before the capture (<see cref="SimSide.DeathsBeforeCapture"/>). One random
/// enemy per Mech, picked anew each time (a target may be hit again).
/// </remarks>
[CardEffect("TB_BaconShop_HERO_17_Buddy")]
internal sealed class ElementiumSquirrelBomb : CombatEffect
{
    public override void Deathrattle(Fight fight, Unit self)
    {
        var mechs = 0;
        foreach (var dead in self.Side.DeadThisFight)
        {
            mechs += dead.Is(Tribe.Mech) ? 1 : 0;
        }

        foreach (var dead in self.Side.Info.DeathsBeforeCapture)
        {
            mechs += (dead.Tribes & (Tribe.Mech | Tribe.All)) != 0 ? 1 : 0;
        }

        var damage = self.Pick(4, 8);
        for (var i = 0; i < mechs; i++)
        {
            if (fight.RandomEnemy(self) is not { } enemy)
            {
                return;
            }

            fight.DealDamage(self, enemy, damage);
        }
    }
}

/// <summary>Mawsworn Soulkeeper (TB_BaconShop_HERO_702_Buddy): "Deathrattle: Summon 2 random Undead." Golden: "4".</summary>
/// <remarks>
/// Each from the Undead of the pool (<see cref="UndeadPool"/>), any tier (assumption, as Ghastcoiler: a random summon is
/// not limited to the Tavern tier), drawn one after the other while there is room.
/// </remarks>
[CardEffect("TB_BaconShop_HERO_702_Buddy")]
internal sealed class MawswornSoulkeeper : CombatEffect
{
    public override void Deathrattle(Fight fight, Unit self) =>
        DeathKit.SummonInOrder(fight, self, CombatBuddyKit.Draw(fight, self, UndeadPool.Minions, self.Pick(2, 4)));
}

/// <summary>
/// Brann's Epic Egg (TB_BaconShop_HERO_43_Buddy): "Taunt. Deathrattle: Summon and get a random Battlecry minion." Golden:
/// "2 random Battlecry minions".
/// </summary>
/// <remarks>
/// From the Battlecry minions of the pool (<see cref="BattlecryPool"/>), any tier, in the lobby's tribes when known
/// (as Ghastcoiler). Each drawn minion is summoned (while there is room) and a copy goes to the hand ("whenever a card is
/// added to your hand" sees it); a minion that does not fit is still got. The summoned minion's Battlecry does not trigger:
/// it is summoned, not played.
/// </remarks>
[CardEffect("TB_BaconShop_HERO_43_Buddy")]
internal sealed class BrannsEpicEgg : CombatEffect
{
    public override void Deathrattle(Fight fight, Unit self)
    {
        var pool = SimInputs.InLobby(BattlecryPool.Minions, self.Side.Info.LobbyTribes);
        if (pool.Count == 0)
        {
            return;
        }

        var place = self;
        for (var n = self.Pick(1, 2); n > 0; n--)
        {
            var minion = pool[fight.Choose(ChoiceKind.RandomSummon, self, pool)];
            if (fight.SummonAt(place, minion) is { } summoned && self.IsOnBoard)
            {
                place = summoned;
            }

            fight.AddToHand(self.Side, minion.CardId, minion with { });
        }
    }
}

/// <summary>
/// Sr. Tomb Diver (TB_BaconShop_HERO_41_Buddy): "Taunt. Deathrattle: Make your right-most minion Golden." Golden: "your two
/// right-most minions".
/// </summary>
/// <remarks>
/// The right-most living friendly minions when the Deathrattle runs (the buddy has left the board). Literal text: a minion
/// that is golden already stays as it is, the next one to its left is not taken instead (assumption, no log).
/// </remarks>
[CardEffect("TB_BaconShop_HERO_41_Buddy")]
internal sealed class SrTombDiver : CombatEffect
{
    public override void Deathrattle(Fight fight, Unit self)
    {
        var alive = fight.Alive(self.Side);
        for (int i = alive.Count - 1, n = self.Pick(1, 2); i >= 0 && n > 0; i--, n--)
        {
            if (!alive[i].Golden)
            {
                CombatBattlecry.BecomeGolden(alive[i]);
            }
        }
    }
}

/// <summary>
/// Spirit Raptor (BG22_HERO_001_Buddy, Al'Akir's buddy): "After you call upon a new Element, this remembers it.
/// Deathrattle: Call upon those Elements." Golden: "… twice."
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>What it remembers: its enchantments "Earth/Fire/Water/Lightning Recollection" (BG22_HERO_001_Buddy_e1..e4), on the
/// board for both players, in the order it got them. The hero power calls its Element at Start of Combat, before the
/// capture, so a new one is already remembered. A raptor summoned in the fight remembers nothing.</item>
/// <item>Calling upon an Element does what the invocation's text says (BG22_HERO_001p_t1..t4): Earth — 4 random friendly
/// minions get "Deathrattle: Summon a 1/1 Elemental"; Fire — double the left-most minion's Attack; Water — the right-most
/// minion gets Divine Shield and Taunt; Lightning — 1 damage to 5 random enemy minions (different ones). Golden: the whole
/// list twice.</item>
/// </list>
/// </remarks>
[CardEffect("BG22_HERO_001_Buddy")]
internal sealed class SpiritRaptor : CombatEffect
{
    public const string EarthMemory = "BG22_HERO_001_Buddy_e1";
    public const string FireMemory = "BG22_HERO_001_Buddy_e2";
    public const string WaterMemory = "BG22_HERO_001_Buddy_e3";
    public const string LightningMemory = "BG22_HERO_001_Buddy_e4";

    private static readonly ElementEarth Earth = new();

    // Index = the Element: 0 Earth, 1 Fire, 2 Water, 3 Lightning.
    private static readonly string[] Memories = [EarthMemory, FireMemory, WaterMemory, LightningMemory];

    public override void Deathrattle(Fight fight, Unit self)
    {
        for (var round = self.Pick(1, 2); round > 0; round--)
        {
            var called = 0;
            foreach (var memory in self.Spec.Enchantments)
            {
                var element = Array.IndexOf(Memories, memory);
                if (element < 0 || (called & (1 << element)) != 0)
                {
                    continue;
                }

                called |= 1 << element;
                CallUpon(fight, self, element);
            }
        }
    }

    private static void CallUpon(Fight fight, Unit self, int element)
    {
        switch (element)
        {
            case 0:
                foreach (var unit in DeathKit.Choose(fight, fight.Alive(self.Side), 4, self))
                {
                    unit.AddEffect(Earth);
                }

                break;
            case 1:
                if (fight.Alive(self.Side) is [var left, ..])
                {
                    fight.Buff(left, Math.Max(0, left.Attack), 0);
                }

                break;
            case 2:
                if (fight.Alive(self.Side) is [.., var right])
                {
                    fight.GiveKeywords(right, Keyword.DivineShield | Keyword.Taunt);
                }

                break;
            default:
                foreach (var enemy in DeathKit.Choose(fight, fight.Alive(self.Enemy), 5, self))
                {
                    fight.DealDamage(self, enemy, 1);
                }

                break;
        }
    }
}

/// <summary>Shared draws of the buddies above.</summary>
internal static class CombatBuddyKit
{
    /// <summary>
    /// Up to <paramref name="count"/> random minions of <paramref name="pool"/> for <paramref name="self"/>, drawn lazily: the
    /// next one only after the previous was summoned and while there is room (repeats possible).
    /// </summary>
    public static IEnumerable<SimMinion> Draw(Fight fight, Unit self, IReadOnlyList<SimMinion> pool, int count)
    {
        for (var i = 0; i < count && pool.Count > 0 && !self.Side.IsFull; i++)
        {
            yield return pool[fight.Choose(ChoiceKind.RandomSummon, self, pool)];
        }
    }
}
