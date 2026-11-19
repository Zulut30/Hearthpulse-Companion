namespace Hearthpulse.Core.Battlegrounds.Effects.Player;

// Hero powers and trinkets whose number grows over the game (English card text, patch 37.0). The number is read from the
// card's own entity at capture (SimSide.ExtraTags): TAG_SCRIPT_DATA_NUM_1 / TAG_SCRIPT_DATA_NUM_2 hold the numbers the
// card's text shows now, not a bonus on top of the printed ones. Real logs (2026-11-15, D:\Hearthstone\Logs): Dragon
// Skull "+5/+5" carries 5/5, Nomi Sticker "+3/+2" 3/2, Sky Golem Portrait "+3/+3" 3/3, Corrupted Baton "+10/+10" 10/10,
// Natural Balance "(20 left!)" counts NUM_1 down from 20. None of the cards here was in those logs, so each one's mapping
// is the same pattern, not checked on the card itself. Only the local player's cards are captured: the opponent's report
// missing_input and play with the printed numbers (a lower bound: the numbers only grow).

/// <summary>The captured current number of a player-level card.</summary>
internal static class GrowingValue
{
    public const string AttackTag = "TAG_SCRIPT_DATA_NUM_1";
    public const string HealthTag = "TAG_SCRIPT_DATA_NUM_2";
    public const string Description = "TAG_SCRIPT_DATA_NUM_1/2 своей карты — число из её текста сейчас (допущение по логам других карт; у соперника не читается, тогда напечатанное)";

    /// <summary>True when the card's number was captured.</summary>
    public static bool Captured(SimSide side, string cardId) => side.ExtraTag(cardId, AttackTag) is not null;

    /// <summary>
    /// The number is known: captured (our card), or estimated (the opponent's: hidden from the player, the printed number
    /// stands in, listed in <see cref="SimSide.Estimates"/> by Tracking/SimEstimates). Without either the fight is
    /// missing_input.
    /// </summary>
    public static bool Known(SimSide side, string cardId)
    {
        if (Captured(side, cardId))
        {
            return true;
        }

        foreach (var estimate in side.Estimates)
        {
            if (estimate.Input == SimInputs.TrinketValue && estimate.Basis.StartsWith(cardId + ":", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The card's current Attack/Health numbers: the captured ones (never below the printed ones), else the printed ones.
    /// A card with one number for both ("+1/+1") may carry it in NUM_1 only.
    /// </summary>
    public static (int Attack, int Health) Current(SimSide side, string cardId, int printedAttack, int printedHealth)
    {
        if (side.ExtraTag(cardId, AttackTag) is not { } attack)
        {
            return (printedAttack, printedHealth);
        }

        var health = side.ExtraTag(cardId, HealthTag) ?? (printedAttack == printedHealth ? attack : printedHealth);
        return (Math.Max(printedAttack, attack), Math.Max(printedHealth, health));
    }
}

/// <summary>
/// Broodmother (BG22_HERO_305p, Onyxia): "Avenge (4): Summon a 1/1 Whelp that attacks immediately. Improve this by +1/+1."
/// <para>
/// The Whelp (the 1/1 Dragon token BG22_HERO_305t) is summoned at the right end and attacks right after the attack in
/// progress; each trigger in the fight improves the next Whelp by +1/+1. Avenge (X) and the progress made before the
/// capture (deaths at Start of Combat, and whatever the game carries over) come from the hero power's SCORE_VALUE_1/2
/// (<see cref="PlayerAvengeEffect"/>).
/// </para>
/// <para>
/// The Whelp's current size: the hero power's TAG_SCRIPT_DATA_NUM_1/2 (<see cref="GrowingValue"/>), trusted only when the
/// two are equal, as every Whelp is (1/1, then +1/+1 per trigger): on the one Avenge card in the logs (Relentless
/// Deflector) NUM_1 holds the Avenge number, so a lone NUM_1 of 4 may be X, not the Whelp. Otherwise the printed 1/1 and
/// missing_input (always for the opponent, whose cards are not captured). Onyxia is in no owner's log yet. The ruRU text
/// of 2026-11-07 says 2/2; the token and the enUS text (HearthstoneJSON latest) say 1/1.
/// </para>
/// </summary>
[CardEffect("BG22_HERO_305p")]
internal sealed class Broodmother : PlayerAvengeEffect
{
    public const string WhelpId = "BG22_HERO_305t";

    public override string CapturedInput => "размер Whelp — TAG_SCRIPT_DATA_NUM_1 = NUM_2 своей силы героя (иначе 1/1); Avenge — SCORE_VALUE_1/2";

    public override bool HasInputs(SimSide side, string cardId) => WhelpSize(side, cardId) is not null || (!GrowingValue.Captured(side, cardId) && GrowingValue.Known(side, cardId));

    public override void OnFightStart(Fight fight, Unit self)
    {
        base.OnFightStart(fight, self);
        // Counter / Counter2: the next Whelp's stats, improved by +1/+1 after each trigger.
        (self.Counter, self.Counter2) = WhelpSize(self.Side.Info, self.CardId) ?? (1, 1);
    }

    protected override int PrintedThreshold(Unit self) => 4;

    /// <summary>The captured Whelp size, when the hero power's NUM_1 and NUM_2 agree (see the remarks).</summary>
    private static (int, int)? WhelpSize(SimSide side, string cardId) =>
        side.ExtraTag(cardId, GrowingValue.AttackTag) is >= 1 and var attack && side.ExtraTag(cardId, GrowingValue.HealthTag) == attack
            ? (attack, attack)
            : null;

    protected override void Avenge(Fight fight, Unit self)
    {
        var whelp = fight.Summon(self.Side, WhelpId, self.Counter, self.Counter2, tribes: Tribe.Dragon);
        self.Counter++;
        self.Counter2++;
        if (whelp is not null)
        {
            fight.AttackSoon(whelp);
        }
    }
}

/// <summary>
/// Tentacular (BG23_HERO_201p, Ozumat): "When you have space in combat, summon a 2/2 Tentacle with Taunt. (Gains +1/+1
/// after you sell a minion!)" One Tentacle (a Beast) per combat, as with Stormpike Strength: with space at Start of Combat
/// it is already on the captured board (CREATOR = the hero power) and nothing is owed; otherwise it comes once a friendly
/// minion has died and left room. Only that late Tentacle needs the captured size (<see cref="GrowingValue"/>); without
/// it the Tentacle is the printed 2/2 and the fight is missing_input.
/// </summary>
[CardEffect("BG23_HERO_201p")]
internal sealed class Tentacular : CombatEffect
{
    public const string Id = "BG23_HERO_201p";
    public const string TentacleId = "BG23_HERO_201pt";

    public override string CapturedInput => GrowingValue.Description;

    public override bool HasInputs(SimSide side, string cardId) => !Owed(side, cardId) || GrowingValue.Known(side, cardId);

    public override void OnFightStart(Fight fight, Unit self) => self.Counter = Owed(self.Side.Info, self.CardId) ? 1 : 0;

    public override void AfterFriendlyDeath(Fight fight, Unit self, Unit dead)
    {
        if (self.Counter == 0 || self.Side.IsFull)
        {
            return;
        }

        var (attack, health) = GrowingValue.Current(self.Side.Info, self.CardId, 2, 2);
        self.Counter = 0;
        fight.Summon(self.Side, TentacleId, attack, health, Keyword.Taunt, tribes: Tribe.Beast);
    }

    /// <summary>A Tentacle is owed: the board was full at capture and no Tentacle from this power is on it.</summary>
    internal static bool Owed(SimSide side, string cardId) =>
        side.Minions.Count >= Side.MaxMinions && !PlayerCards.AnyCreatedBy(side, cardId);
}

/// <summary>
/// Fang Anklet (BG35_MagicItem_701): "Start of Combat: Your Beasts have +1/+1 this combat. After you summon a Beast,
/// improve this permanently." The Start of Combat part resolved before capture, so the captured Beasts already have it
/// and are not buffed again; Beasts summoned later this combat get the same +X/+X (the current number,
/// <see cref="GrowingValue"/>; printed +1/+1 without it, then missing_input). The improvement is for later combats: it
/// does not raise this combat's aura.
/// </summary>
[CardEffect("BG35_MagicItem_701")]
internal sealed class FangAnklet : CombatEffect
{
    public override string CapturedInput => GrowingValue.Description;

    public override bool HasInputs(SimSide side, string cardId) => GrowingValue.Known(side, cardId);

    public override void OnFightStart(Fight fight, Unit self)
    {
        var (attack, health) = GrowingValue.Current(self.Side.Info, self.CardId, 1, 1);
        self.Side.AddSummonBuff(Tribe.Beast, attack, health);
    }
}
