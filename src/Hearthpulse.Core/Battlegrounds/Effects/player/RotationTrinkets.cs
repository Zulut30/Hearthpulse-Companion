using Hearthpulse.Core.Battlegrounds.Effects.Attack;
using Hearthpulse.Core.Battlegrounds.Effects.Ongoing;

namespace Hearthpulse.Core.Battlegrounds.Effects.Player;

// Trinkets out of today's pool that come back with a rotation (package E6b), from their English card text (HearthstoneJSON
// latest, Last-Modified 2026-11-03, checked 2026-11-19). None of them is in the kept real logs (D:\Hearthstone\Logs, 15.11),
// so every timing below follows the text and the engine's existing hooks, not a log. Numbers a trinket shows are read from
// its captured TAG_SCRIPT_DATA_NUM_1/2 when present (our own trinkets, GrowingValue), the text's number otherwise.

/// <summary>
/// Battle Horn (BG32_MagicItem_415): "Discover a Battlecry minion. Avenge (2): Trigger a friendly Battlecry." The Avenge
/// triggers the Battlecry of a random living friendly minion that has one (Brann's multiplier applies, as for other
/// "trigger a Battlecry" effects); nothing without such a minion.
/// </summary>
[CardEffect("BG32_MagicItem_415")]
internal sealed class BattleHorn : PlayerAvengeEffect
{
    protected override int PrintedThreshold(Unit self) => 2;

    protected override void Avenge(Fight fight, Unit self)
    {
        var withBattlecry = fight.Alive(self.Side);
        withBattlecry.RemoveAll(static unit => !Fight.HasBattlecry(unit));
        if (fight.RandomOf(withBattlecry, self) is { } unit)
        {
            fight.TriggerBattlecry(unit);
        }
    }
}

/// <summary>
/// Belcher Portrait (BG30_MagicItem_432): "Get an Operatic Belcher. Whenever a friendly minion loses Venomous, it gains
/// +4/+4 permanently." Golden (…432t): "+14/+14". A minion loses Venomous when it damages a minion with it
/// (<see cref="Fight.DealDamage"/>); the stats come after that damage resolves (the damage hooks).
/// </summary>
[CardEffect("BG30_MagicItem_432")]
[CardEffect("BG30_MagicItem_432t")]
internal sealed class BelcherPortrait : CombatEffect
{
    public override void OnFightStart(Fight fight, Unit self)
    {
        var golden = self.CardId.EndsWith('t');
        (self.Counter, self.Counter2) = GrowingValue.Current(self.Side.Info, self.CardId, golden ? 14 : 4, golden ? 14 : 4);
    }

    public override void AfterFriendlyVenomLost(Fight fight, Unit self, Unit unit)
    {
        if (unit.IsAlive)
        {
            fight.Buff(unit, self.Counter, self.Counter2);
        }
    }
}

/// <summary>
/// Blood Amulet (BG35_MagicItem_432): "After you trigger a Deathrattle, this plays a permanent Blood Gem on 3 random friendly
/// minions." One Blood Gem of the player (<see cref="BloodGems.Value"/>) on each of up to 3 different living friendly
/// minions, after every Deathrattle trigger (each extra trigger from Titus counts).
/// </summary>
[CardEffect("BG35_MagicItem_432")]
internal sealed class BloodAmulet : CombatEffect
{
    public const int Targets = 3;

    public override void OnFriendlyDeathrattle(Fight fight, Unit self, Unit source)
    {
        foreach (var target in fight.RandomDistinct(fight.Alive(self.Side), Targets, self))
        {
            BloodGems.Play(fight, self, target, 1);
        }
    }
}

/// <summary>
/// Boom Controller (BG30_MagicItem_440): "When you have space, summon an exact copy of your first Mech that died each
/// combat." The first friendly Mech to die in the fight is copied as it died (its Attack and maximum Health, the keywords of
/// its card and enchantments, at full Health); the copy comes at the right end as soon as a death phase leaves room, once
/// per fight. Assumption: a Mech that died at Start of Combat (before the capture) is not known, so the first one the
/// simulator sees counts.
/// </summary>
[CardEffect("BG30_MagicItem_440")]
internal sealed class BoomController : CombatEffect
{
    // Counter: 1 once the first Mech died.
    public override void AfterFriendlyDeath(Fight fight, Unit self, Unit dead)
    {
        if (self.Counter != 0 || dead.IsExtra || !dead.Is(Tribe.Mech))
        {
            return;
        }

        self.Counter = 1;
        var owed = UnitState<OwedCopies>.Of(self);
        owed.Copy = OwedCopies.CopyOf(dead, self.CardId, exact: true);
        owed.Left = 1;
    }

    public override void AfterDeathPhase(Fight fight, Unit self) => OwedCopies.SummonWhenRoom(fight, self);
}

/// <summary>
/// Crocheted Sungill (BG32_MagicItem_960): "Start of Combat: Give the highest-Health minion from your hand +4/+4. When you
/// have space, summon it for this combat only." Start of Combat resolved before the capture: the hand minion already shows
/// the +4/+4, and with room it already stands on the board (its creator is the trinket). With a full board at capture and
/// nothing on it from the trinket, the summon is still owed: the highest-Health minion of our hand comes at the right end
/// when a death phase leaves room. The opponent's hand is hidden, so their owed summon is missing_input.
/// </summary>
[CardEffect("BG32_MagicItem_960")]
internal sealed class CrochetedSungill : CombatEffect
{
    public override bool HasInputs(SimSide side, string cardId) =>
        side.Minions.Count < Side.MaxMinions || side.Hand.Count > 0 || PlayerCards.AnyCreatedBy(side, cardId);

    // Counter: 1 = the summon is owed.
    public override void OnFightStart(Fight fight, Unit self) =>
        self.Counter = self.Side.IsFull && self.Side.Hand.Count > 0 && !PlayerCards.AnyCreatedBy(self.Side.Info, self.CardId) ? 1 : 0;

    public override void AfterDeathPhase(Fight fight, Unit self)
    {
        var hand = self.Side.Hand;
        if (self.Counter == 0 || self.Side.IsFull || hand.Count == 0)
        {
            return;
        }

        self.Counter = 0;
        var index = fight.BestCard(hand, static _ => true, static card => card.Health, self);
        if (index < 0)
        {
            return;
        }

        var card = hand[index];
        hand.RemoveAt(index);
        fight.Summon(self.Side, card with { EntityId = 0, CreatorCardId = self.CardId }, self.Side.Count);
    }
}

/// <summary>
/// Dazzling Dagger (BG32_MagicItem_934): "Your minions have +1 Attack. (Improved by every 4 spells you've cast this game!)"
/// An aura: the captured minions already show it; minions summoned in combat get it too. The number is the trinket's
/// TAG_SCRIPT_DATA_NUM_1 (<see cref="GrowingValue"/>; the printed +1 without it, then missing_input; the opponent's is an
/// estimate). Not modelled: an improvement from spells cast in this combat (the progress to the next 4 is not captured).
/// </summary>
[CardEffect("BG32_MagicItem_934")]
internal sealed class DazzlingDagger : CombatEffect
{
    public override string CapturedInput => GrowingValue.Description;

    public override bool HasInputs(SimSide side, string cardId) => GrowingValue.Known(side, cardId);

    public override void OnFightStart(Fight fight, Unit self) =>
        self.Side.AddSummonBuff(Tribe.All, GrowingValue.Current(self.Side.Info, self.CardId, 1, 0).Attack, 0);
}

/// <summary>
/// Impulsive Portrait (BG32_MagicItem_820): "Get a 6/6 Impulsive Trickster. Your Impulsive Tricksters' Deathrattles target
/// adjacent minions." Registered without hooks: <see cref="Deaths.ImpulsiveTrickster"/> reads it from its side's trinkets.
/// </summary>
[CardEffect("BG32_MagicItem_820")]
internal sealed class ImpulsivePortrait : CombatEffect
{
    public const string Id = "BG32_MagicItem_820";
}

/// <summary>
/// Kaboom Bot Portrait (BG30_MagicItem_803): "Get a Kaboom Bot. Your Kaboom Bots' Deathrattles deal 10 extra damage."
/// Registered without hooks: <see cref="Deaths.KaboomBot"/> adds the trinket's number (<see cref="ExtraDamage"/>).
/// </summary>
[CardEffect("BG30_MagicItem_803")]
internal sealed class KaboomBotPortrait : CombatEffect
{
    public const string Id = "BG30_MagicItem_803";
    public const int Printed = 10;

    /// <summary>Extra damage of each Kaboom Bot deathrattle hit on <paramref name="side"/>: 10 per portrait (or its captured number).</summary>
    public static int ExtraDamage(SimSide side)
    {
        var extra = 0;
        foreach (var id in side.Extras)
        {
            if (id == Id)
            {
                extra += GrowingValue.Current(side, Id, Printed, 0).Attack;
            }
        }

        return extra;
    }
}

/// <summary>
/// Mug of the Sire (BG30_MagicItem_438t): "Whenever you would summon a minion that doesn't fit in your warband, give your
/// minions +5 Attack." Every summon refused for a full board (<see cref="Fight.Summon(Side, SimMinion, int)"/>) gives the
/// living friendly minions +5 Attack. A deathrattle summoning several minions stops at the first one that does not fit, so
/// it counts once.
/// </summary>
[CardEffect("BG30_MagicItem_438t")]
internal sealed class MugOfTheSire : CombatEffect
{
    public override void OnFightStart(Fight fight, Unit self) => self.Counter = GrowingValue.Current(self.Side.Info, self.CardId, 5, 0).Attack;

    public override void OnSummonDidNotFit(Fight fight, Unit self)
    {
        foreach (var minion in fight.Alive(self.Side))
        {
            fight.Buff(minion, self.Counter, 0);
        }
    }
}

/// <summary>
/// Scrapsmith Portrait (BG35_MagicItem_430): "Get a Bristlemane Scrapsmith. After a friendly minion with Taunt dies, play a
/// permanent Blood Gem on your Scrapsmiths." One of the player's Blood Gems on every living friendly Bristlemane Scrapsmith
/// (BG24_707, golden too) after each friendly death of a minion that had Taunt when it died.
/// </summary>
[CardEffect("BG35_MagicItem_430")]
internal sealed class ScrapsmithPortrait : CombatEffect
{
    public const string ScrapsmithId = "BG24_707";

    public override void AfterFriendlyDeath(Fight fight, Unit self, Unit dead)
    {
        if (!dead.Taunt)
        {
            return;
        }

        foreach (var unit in fight.Alive(self.Side))
        {
            if (unit.CardId == ScrapsmithId)
            {
                BloodGems.Play(fight, self, unit, 1);
            }
        }
    }
}

/// <summary>
/// Tiger Carving (BG30_MagicItem_427): "Whenever a friendly minion takes damage, give another friendly minion +3/+1
/// permanently." Golden (…427t): "+6/+2". Each damage a friendly minion takes (not a Divine Shield pop) gives a random other
/// living friendly minion the stats.
/// </summary>
[CardEffect("BG30_MagicItem_427")]
[CardEffect("BG30_MagicItem_427t")]
internal sealed class TigerCarving : CombatEffect
{
    public override void OnFightStart(Fight fight, Unit self)
    {
        var golden = self.CardId.EndsWith('t');
        (self.Counter, self.Counter2) = GrowingValue.Current(self.Side.Info, self.CardId, golden ? 6 : 3, golden ? 2 : 1);
    }

    public override void AfterFriendlyDamaged(Fight fight, Unit self, Unit damaged, Unit? source, int amount)
    {
        if (fight.RandomOf(fight.Alive(self.Side, except: damaged), self) is { } other)
        {
            fight.Buff(other, self.Counter, self.Counter2);
        }
    }
}

/// <summary>
/// Twin Sky Lanterns (BG30_MagicItem_822): "When you have space, summon a copy of the first minion you summon each combat."
/// Golden (…822t2): "summon 2 copies". The copies (the minion as it was summoned, without the summon bonuses it got) come
/// right of it at once when there is room, else at the right end when a death phase leaves room. A minion the capture shows
/// as summoned at Start of Combat was the first one: its copies came then, unless the board was full and none of the
/// captured minions comes from the trinket, then they are still owed.
/// </summary>
[CardEffect("BG30_MagicItem_822")]
[CardEffect("BG30_MagicItem_822t2")]
internal sealed class TwinSkyLanterns : CombatEffect
{
    private static int Copies(Unit self) => self.CardId.EndsWith("t2", StringComparison.Ordinal) ? 2 : 1;

    // Counter: 1 once the first minion was summoned.
    public override void OnFightStart(Fight fight, Unit self)
    {
        Unit? first = null;
        foreach (var unit in self.Side.Minions)
        {
            if (unit.Spec.SummonedInCombat && (first is null || unit.Entry < first.Entry))
            {
                first = unit;
            }
        }

        if (first is null)
        {
            return;
        }

        self.Counter = 1;
        if (self.Side.IsFull && !PlayerCards.AnyCreatedBy(self.Side.Info, self.CardId))
        {
            var owed = UnitState<OwedCopies>.Of(self);
            owed.Copy = OwedCopies.CopyOf(first, self.CardId, exact: false);
            owed.Left = Copies(self);
        }
    }

    public override void AfterFriendlySummoned(Fight fight, Unit self, Unit summoned)
    {
        if (self.Counter != 0 || summoned.IsExtra)
        {
            return;
        }

        self.Counter = 1;
        var owed = UnitState<OwedCopies>.Of(self);
        owed.Copy = OwedCopies.CopyOf(summoned, self.CardId, exact: false);
        owed.Left = Copies(self);
        var index = summoned.IsOnBoard ? summoned.Position + 1 : self.Side.Count;
        while (owed.Left > 0 && !self.Side.IsFull)
        {
            owed.Left--;
            if (fight.Summon(self.Side, owed.Copy, index) is { } copy)
            {
                index = copy.Position + 1;
            }
        }
    }

    public override void AfterDeathPhase(Fight fight, Unit self) => OwedCopies.SummonWhenRoom(fight, self);
}

/// <summary>Copies a trinket owes "when you have space" (per trinket unit, one fight).</summary>
internal sealed class OwedCopies
{
    public SimMinion? Copy { get; set; }

    public int Left { get; set; }

    /// <summary>
    /// A copy of <paramref name="unit"/> made by <paramref name="creator"/>: its Attack and maximum Health without the summon
    /// bonuses of its side (the copy gets them again), at full Health. <paramref name="exact"/> keeps the keywords of its card
    /// and enchantments (Divine Shield or Reborn it used up come back); otherwise its keywords as they are now.
    /// </summary>
    public static SimMinion CopyOf(Unit unit, string creator, bool exact)
    {
        var (auraAttack, auraHealth) = unit.Side.SummonBuffsFor(unit);
        var health = Math.Max(1, unit.MaxHealth - auraHealth);
        var copy = unit.ToSimMinion() with
        {
            EntityId = 0,
            Attack = Math.Max(0, unit.Attack - auraAttack),
            Health = health,
            MaxHealth = health,
            CreatorCardId = creator,
        };
        return exact
            ? copy with { DivineShield = unit.Spec.DivineShield, Reborn = unit.Spec.Reborn, Venomous = unit.Spec.Venomous, Taunt = unit.Spec.Taunt }
            : copy;
    }

    /// <summary>Summons what <paramref name="self"/> still owes at the right end while there is room.</summary>
    public static void SummonWhenRoom(Fight fight, Unit self)
    {
        if (self.EffectStates is null)
        {
            return;
        }

        var owed = UnitState<OwedCopies>.Of(self);
        while (owed.Left > 0 && owed.Copy is { } copy && !self.Side.IsFull)
        {
            owed.Left--;
            fight.Summon(self.Side, copy, self.Side.Count);
        }
    }
}
