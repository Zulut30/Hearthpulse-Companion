namespace Hearthpulse.Core.Battlegrounds.Effects.Player;

// Trinkets whose text acts in combat after the boards are captured (PlayerCardTable), from their English card text
// (HearthstoneJSON latest, checked 2026-11-19). A trinket is an off-board extra unit of its side: it gets the side's hooks,
// and its Counter / Counter2 / AvengeCount are per-fight state. "(N times per combat.)" counts in Counter. Golden ("…t")
// versions are separate cards. "(N times per combat.)" starts from the trinket's NUM_1 = uses left (PlayerCards.UsesLeft).
// Balance changes on the server without a new client, so a number the trinket shows is read
// from its captured TAG_SCRIPT_DATA_NUM_1/2 when present (our own trinkets; GrowingValue), the text's number otherwise.
// Avenge trinkets do not read NUM_1 for their stats: on the one Avenge card in the logs NUM_1 holds the Avenge number;
// their X and progress come from SCORE_VALUE_1/2 (PlayerAvengeEffect).

/// <summary>Ceremonial Sword (BG30_MagicItem_925): "Whenever a friendly minion attacks, give it +4 Attack."</summary>
[CardEffect("BG30_MagicItem_925")]
internal sealed class CeremonialSword : CombatEffect
{
    // Counter: the Attack given, read once per fight.
    public override void OnFightStart(Fight fight, Unit self) => self.Counter = GrowingValue.Current(self.Side.Info, self.CardId, 4, 0).Attack;

    public override void OnFriendlyAttack(Fight fight, Unit self, Unit attacker, Unit target) => fight.Buff(attacker, self.Counter, 0);
}

/// <summary>Lionfish Portrait (BG36_MagicItem_201): "… Whenever a friendly Beast attacks, give it +4/+2."</summary>
[CardEffect("BG36_MagicItem_201")]
internal sealed class LionfishPortrait : CombatEffect
{
    public override void OnFightStart(Fight fight, Unit self) => (self.Counter, self.Counter2) = GrowingValue.Current(self.Side.Info, self.CardId, 4, 2);

    public override void OnFriendlyAttack(Fight fight, Unit self, Unit attacker, Unit target)
    {
        if (attacker.Is(Tribe.Beast))
        {
            fight.Buff(attacker, self.Counter, self.Counter2);
        }
    }
}

/// <summary>
/// Faerie Dragon Scale (BG32_MagicItem_363): "Whenever a friendly Dragon attacks, give it Divine Shield. (3 times per
/// combat.)" Its NUM_1 counts the uses left down 3, 2, 1 in a fight (real log 2026-11-15, game 4, the effects audit), so
/// the captured value says how many are left at the first attack; 3 without it.
/// </summary>
[CardEffect("BG32_MagicItem_363")]
internal sealed class FaerieDragonScale : CombatEffect
{
    private const int Uses = 3;

    // Counter: uses left (PlayerCards.UsesLeft).
    public override void OnFightStart(Fight fight, Unit self) => self.Counter = PlayerCards.UsesLeft(self.Side.Info, self.CardId, Uses);

    public override void OnFriendlyAttack(Fight fight, Unit self, Unit attacker, Unit target)
    {
        if (attacker.Is(Tribe.Dragon) && self.Counter > 0)
        {
            self.Counter--;
            fight.GiveKeywords(attacker, Keyword.DivineShield);
        }
    }
}

/// <summary>
/// Feral Talisman (BG30_MagicItem_880): "Your minions have +2/+1." Golden (…880t): "+8/+5." The captured minions already
/// show the aura; minions summoned in combat get it too.
/// </summary>
[CardEffect("BG30_MagicItem_880")]
[CardEffect("BG30_MagicItem_880t")]
internal sealed class FeralTalisman : CombatEffect
{
    public override void OnFightStart(Fight fight, Unit self)
    {
        var golden = self.CardId.EndsWith('t');
        var (attack, health) = GrowingValue.Current(self.Side.Info, self.CardId, golden ? 8 : 2, golden ? 5 : 1);
        self.Side.AddSummonBuff(Tribe.All, attack, health);
    }
}

/// <summary>Reinforced Shield (BG30_MagicItem_886): "Whenever you summon a minion, give it Divine Shield. (5 times per combat.)"</summary>
[CardEffect("BG30_MagicItem_886")]
internal sealed class ReinforcedShield : CombatEffect
{
    // Counter: uses left (PlayerCards.UsesLeft).
    public override void OnFightStart(Fight fight, Unit self) => self.Counter = PlayerCards.UsesLeft(self.Side.Info, self.CardId, 5);

    public override void WheneverFriendlySummoned(Fight fight, Unit self, Unit summoned)
    {
        if (self.Counter > 0)
        {
            self.Counter--;
            fight.GiveKeywords(summoned, Keyword.DivineShield);
        }
    }
}

/// <summary>Slamma Sticker (BG30_MagicItem_540): "After you summon a Beast in combat, double its Attack."</summary>
[CardEffect("BG30_MagicItem_540")]
internal sealed class SlammaSticker : CombatEffect
{
    public override void AfterFriendlySummoned(Fight fight, Unit self, Unit summoned)
    {
        if (summoned.Is(Tribe.Beast))
        {
            fight.Buff(summoned, summoned.Attack, 0);
        }
    }
}

/// <summary>Bassgill Portrait (BG32_MagicItem_301): "… After you summon a Murloc in combat, give it Divine Shield."</summary>
[CardEffect("BG32_MagicItem_301")]
internal sealed class BassgillPortrait : CombatEffect
{
    public override void AfterFriendlySummoned(Fight fight, Unit self, Unit summoned)
    {
        if (summoned.Is(Tribe.Murloc))
        {
            fight.GiveKeywords(summoned, Keyword.DivineShield);
        }
    }
}

/// <summary>Blingtron's Sunglasses (BG30_MagicItem_978): "After you summon a Mech in combat, give a friendly Mech Divine Shield."</summary>
[CardEffect("BG30_MagicItem_978")]
internal sealed class BlingtronsSunglasses : CombatEffect
{
    public override void AfterFriendlySummoned(Fight fight, Unit self, Unit summoned)
    {
        if (summoned.Is(Tribe.Mech) && fight.RandomOf(fight.Alive(self.Side, Tribe.Mech), self) is { } mech)
        {
            fight.GiveKeywords(mech, Keyword.DivineShield);
        }
    }
}

/// <summary>Mechagon Adapter (BG30_MagicItem_910): "After a friendly Mech loses Divine Shield, give it Divine Shield. (3 times per combat.)"</summary>
[CardEffect("BG30_MagicItem_910")]
internal sealed class MechagonAdapter : CombatEffect
{
    // Counter: uses left (PlayerCards.UsesLeft).
    public override void OnFightStart(Fight fight, Unit self) => self.Counter = PlayerCards.UsesLeft(self.Side.Info, self.CardId, 3);

    public override void OnFriendlyShieldLost(Fight fight, Unit self, Unit unit)
    {
        if (unit.Is(Tribe.Mech) && unit.IsOnBoard && self.Counter > 0)
        {
            self.Counter--;
            fight.GiveKeywords(unit, Keyword.DivineShield);
        }
    }
}

/// <summary>
/// Alliance Keychain (BG30_MagicItem_433): "The first time a friendly minion dies each combat, give its maximum stats to a
/// random friendly minion." Golden (…433t): "to 2 random friendly minions."
/// </summary>
[CardEffect("BG30_MagicItem_433")]
[CardEffect("BG30_MagicItem_433t")]
internal sealed class AllianceKeychain : CombatEffect
{
    public override void AfterFriendlyDeath(Fight fight, Unit self, Unit dead)
    {
        if (self.Counter > 0)
        {
            return;
        }

        self.Counter = 1;
        foreach (var target in fight.RandomDistinct(fight.Alive(self.Side), self.CardId.EndsWith('t') ? 2 : 1, self))
        {
            fight.Buff(target, dead.Attack, Math.Max(dead.MaxHealth, 0));
        }
    }
}

/// <summary>Deathwhisper Sticker (BG36_MagicItem_205): "After a friendly minion is Reborn, give your minions +2/+2."</summary>
[CardEffect("BG36_MagicItem_205")]
internal sealed class DeathwhisperSticker : CombatEffect
{
    public override void OnFightStart(Fight fight, Unit self) => (self.Counter, self.Counter2) = GrowingValue.Current(self.Side.Info, self.CardId, 2, 2);

    public override void AfterFriendlyReborn(Fight fight, Unit self, Unit reborn)
    {
        foreach (var minion in fight.Alive(self.Side))
        {
            fight.Buff(minion, self.Counter, self.Counter2);
        }
    }
}

/// <summary>Deathtouch Apple (BG35_MagicItem_731): "After a friendly Undead is Reborn, give it Reborn. (3 times per turn.)"</summary>
[CardEffect("BG35_MagicItem_731")]
internal sealed class DeathtouchApple : CombatEffect
{
    // Counter: uses left (PlayerCards.UsesLeft; "per turn", the combat is the rest of the turn).
    public override void OnFightStart(Fight fight, Unit self) => self.Counter = PlayerCards.UsesLeft(self.Side.Info, self.CardId, 3);

    public override void AfterFriendlyReborn(Fight fight, Unit self, Unit reborn)
    {
        if (reborn.Is(Tribe.Undead) && self.Counter > 0)
        {
            self.Counter--;
            fight.GiveKeywords(reborn, Keyword.Reborn);
        }
    }
}

/// <summary>
/// Unholy Sanctum (BG32_MagicItem_862): "After you trigger a Deathrattle, give your right-most minion +2/+2 permanently."
/// Golden (…862t): "+6/+4".
/// </summary>
[CardEffect("BG32_MagicItem_862")]
[CardEffect("BG32_MagicItem_862t")]
internal sealed class UnholySanctum : CombatEffect
{
    public override void OnFightStart(Fight fight, Unit self)
    {
        var golden = self.CardId.EndsWith('t');
        (self.Counter, self.Counter2) = GrowingValue.Current(self.Side.Info, self.CardId, golden ? 6 : 2, golden ? 4 : 2);
    }

    public override void OnFriendlyDeathrattle(Fight fight, Unit self, Unit source)
    {
        var board = self.Side.Minions;
        for (var i = board.Count - 1; i >= 0; i--)
        {
            if (board[i].IsAlive)
            {
                fight.Buff(board[i], self.Counter, self.Counter2);
                return;
            }
        }
    }
}

/// <summary>Deathly Phylactery (BG30_MagicItem_700): "… Your first Deathrattle each combat triggers an extra time."</summary>
[CardEffect("BG30_MagicItem_700")]
internal sealed class DeathlyPhylactery : CombatEffect
{
    public override void OnFriendlyDeathrattle(Fight fight, Unit self, Unit source)
    {
        if (self.Counter > 0)
        {
            return;
        }

        self.Counter = 1;
        fight.TriggerDeathrattles(source, 1, withMultiplier: false);
    }
}

/// <summary>
/// S'Thara Sticker (BG32_MagicItem_907): "After your last minion dies each combat, summon your first Demon that died with
/// its maximum stats."
/// </summary>
[CardEffect("BG32_MagicItem_907")]
internal sealed class STharaSticker : CombatEffect
{
    public override void AfterFriendlyDeath(Fight fight, Unit self, Unit dead)
    {
        if (self.Counter > 0 || fight.Alive(self.Side).Count > 0)
        {
            return;
        }

        if (self.Side.DeadThisFight.FirstOrDefault(u => u.Is(Tribe.Demon) && !u.IsExtra) is not { } demon)
        {
            return;
        }

        self.Counter = 1;
        var spec = demon.Spec;
        fight.Summon(self.Side, SimMinion.Create(spec.CardId, demon.Attack, Math.Max(demon.MaxHealth, 1), spec.Tribes, spec.BaseKeywords, spec.Golden, spec.Mechanics)
            with { BaseHealth = spec.BaseHealth, TechLevel = spec.TechLevel }, self.Side.Count);
    }
}

/// <summary>Bird Feeder (BG32_MagicItem_864): "Avenge (2): Give your minions +1/+1." Golden (…864t): "+4/+4".</summary>
[CardEffect("BG32_MagicItem_864")]
[CardEffect("BG32_MagicItem_864t")]
internal sealed class BirdFeeder : PlayerAvengeEffect
{
    protected override int PrintedThreshold(Unit self) => 2;

    protected override void Avenge(Fight fight, Unit self)
    {
        var bonus = self.CardId.EndsWith('t') ? 4 : 1;
        foreach (var minion in fight.Alive(self.Side))
        {
            fight.Buff(minion, bonus, bonus);
        }
    }
}

/// <summary>Gilnean Thorned Rose (BG30_MagicItem_864): "Avenge (3): Give your minions +4/+5 permanently and deal 1 damage to them."</summary>
[CardEffect("BG30_MagicItem_864")]
internal sealed class GilneanThornedRose : PlayerAvengeEffect
{
    protected override int PrintedThreshold(Unit self) => 3;

    protected override void Avenge(Fight fight, Unit self)
    {
        var minions = fight.Alive(self.Side);
        foreach (var minion in minions)
        {
            fight.Buff(minion, 4, 5);
        }

        foreach (var minion in minions)
        {
            fight.DealDamage(null, minion, 1);
        }
    }
}

/// <summary>Staff of the Scourge (BG30_MagicItem_437): "Avenge (5): Give a random friendly Undead Reborn."</summary>
[CardEffect("BG30_MagicItem_437")]
internal sealed class StaffOfTheScourge : PlayerAvengeEffect
{
    protected override int PrintedThreshold(Unit self) => 5;

    protected override void Avenge(Fight fight, Unit self)
    {
        if (fight.RandomOf(fight.Alive(self.Side, Tribe.Undead), self) is { } undead)
        {
            fight.GiveKeywords(undead, Keyword.Reborn);
        }
    }
}

/// <summary>
/// Beetle Band (BG32_MagicItem_860): "Avenge (5): Summon a 2/2 Beetle. Give it Taunt." Golden (…860t): "Avenge (8): Summon
/// two 2/2 Beetles. Give them Taunt." (HearthstoneJSON latest; 37.0 said Avenge (7).) A Beetle is the player's current
/// Beetle: 2/2 plus "your Beetles have +X/+Y this game" so far (<see cref="SimInputs.BeetleStatsOf"/>: the Beetle Army
/// enchantment, an estimate for the opponent; else what a Beetle summoner shows, <see cref="Deaths.Beetles.Stats"/>), plus
/// bonuses gained in this fight.
/// </summary>
[CardEffect("BG32_MagicItem_860")]
[CardEffect("BG32_MagicItem_860t")]
internal sealed class BeetleBand : PlayerAvengeEffect
{
    protected override int PrintedThreshold(Unit self) => self.CardId.EndsWith('t') ? 8 : 5;

    protected override void Avenge(Fight fight, Unit self)
    {
        // The player's Beetle: the Beetle Army enchantment or its estimate (SimInputs), else what a Beetle summoner shows.
        var (attack, health) = SimInputs.BeetleStatsOf(self.Side.Info) ?? Deaths.Beetles.Stats(self);
        for (var i = self.CardId.EndsWith('t') ? 2 : 1; i > 0; i--)
        {
            if (fight.Summon(self.Side, Tokens.Make(Tokens.BeetleId, attack, health, Tribe.Beast), self.Side.Count) is { } beetle)
            {
                fight.GiveKeywords(beetle, Keyword.Taunt);
            }
        }
    }
}

/// <summary>Cloud Serpent Horn (BG35_MagicItem_849): "Avenge (3): Give the Attack of your right-most minion to another friendly Dragon."</summary>
[CardEffect("BG35_MagicItem_849")]
internal sealed class CloudSerpentHorn : PlayerAvengeEffect
{
    protected override int PrintedThreshold(Unit self) => 3;

    protected override void Avenge(Fight fight, Unit self)
    {
        var alive = fight.Alive(self.Side);
        if (alive.Count == 0)
        {
            return;
        }

        var rightMost = alive[^1];
        if (fight.RandomOf(fight.Alive(self.Side, Tribe.Dragon, except: rightMost), self) is { } dragon)
        {
            fight.Buff(dragon, rightMost.Attack, 0);
        }
    }
}

/// <summary>
/// Bewitched Ribbon (BG35_MagicItem_923): "Whenever you cast a spell, give your minions +1/+1 permanently. (+2/+2 while in
/// combat!)" In combat: +2/+2 after each spell the side casts (<see cref="Fight.CastSpell"/>).
/// </summary>
[CardEffect("BG35_MagicItem_923")]
internal sealed class BewitchedRibbon : CombatEffect
{
    public override void AfterFriendlySpellCast(Fight fight, Unit self, string spellCardId, Unit? target)
    {
        foreach (var minion in fight.Alive(self.Side))
        {
            fight.Buff(minion, 2, 2);
        }
    }
}
