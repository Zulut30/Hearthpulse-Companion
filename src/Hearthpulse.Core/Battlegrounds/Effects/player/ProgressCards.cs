using Hearthpulse.Core.Battlegrounds.Effects.Ongoing;
using Hearthpulse.Core.Battlegrounds.Effects.Spells;

namespace Hearthpulse.Core.Battlegrounds.Effects.Player;

// Player-level cards of today's game the relevance audit left as known gaps (TASK-2026-11-19, package E7b). Text: HearthstoneJSON
// enUS, build 256589.
//
// "After N ... , get a random X. (N left!)": the reward is a random card, which matters to a fight only through "whenever a
// card is added to your hand" (The Shadow of Doubt, Timewarped Peggy). The progress is the card's TAG_SCRIPT_DATA_NUM_1,
// which counts down what is left and returns to N when the reward is given: real logs of the same kind of card, For the
// Horde (BG20_HERO_102p, "Improves after you buy 3 minions. (3 left!)"; 2026-11-16 session, NUM_1 3 → 2 → 1 → 0 → 3 with
// NUM_2 the level), and Stormcoil Sticker itself offered at NUM_1 = 5 = its N (2026-11-19 18:06 session, L59138). The local
// player's card is captured (SimSide.ExtraTags); the opponent's number is hidden from the player: N left is the estimate
// (Tracking/SimEstimates, input progress_left), never a silent value.

/// <summary>"After N friendly minions die / you summon N Beasts, get a random X": the progress and the reward.</summary>
internal abstract class ProgressGet : CombatEffect
{
    public const string LeftTag = "TAG_SCRIPT_DATA_NUM_1";

    /// <summary>N of the text.</summary>
    protected abstract int Total { get; }

    public override string? CapturedInput => "сколько осталось (TAG_SCRIPT_DATA_NUM_1 своей карты; у соперника скрыто — оценка «N осталось»)";

    public override bool HasInputs(SimSide side, string cardId) => Known(side, cardId);

    /// <summary>
    /// The count is known: our own card is in the capture (a count tag it never got means N left), or the opponent's has
    /// its estimate (<see cref="SimInputs.ProgressLeft"/>, basis "&lt;card id&gt;: …").
    /// </summary>
    public static bool Known(SimSide side, string cardId)
    {
        if (side.ExtraTags.ContainsKey(cardId))
        {
            return true;
        }

        foreach (var estimate in side.Estimates)
        {
            if (estimate.Input == SimInputs.ProgressLeft && estimate.Basis.StartsWith(cardId + ":", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>What is left at the capture: the captured count in 1..N, else N (also the opponent's estimate).</summary>
    public static int LeftAtCapture(SimSide side, string cardId, int total) =>
        side.ExtraTag(cardId, LeftTag) is > 0 and var left && left <= total ? left : total;

    public override void OnFightStart(Fight fight, Unit self) => self.Counter = LeftAtCapture(self.Side.Info, self.CardId, Total);

    /// <summary>One step of the progress; the reward when it completes, and the count starts again.</summary>
    protected void Step(Fight fight, Unit self)
    {
        if (--self.Counter > 0)
        {
            return;
        }

        self.Counter = Total;
        fight.AddToHand(self.Side, CardToHand.AnyCard);
    }
}

/// <summary>"After N friendly minions die, get a random X."</summary>
internal abstract class DeathsProgressGet : ProgressGet
{
    public override void AfterFriendlyDeath(Fight fight, Unit self, Unit dead) => Step(fight, self);
}

/// <summary>Lucky Tabby (BG30_MagicItem_931): "After 6 friendly minions die, get a random Beast. (6 left!)"</summary>
[CardEffect("BG30_MagicItem_931")]
internal sealed class LuckyTabby : DeathsProgressGet
{
    protected override int Total => 6;
}

/// <summary>Bleeding Heart (BG30_MagicItem_713): "After 8 friendly minions die, get a random Undead. (8 left!)"</summary>
[CardEffect("BG30_MagicItem_713")]
internal sealed class BleedingHeart : DeathsProgressGet
{
    protected override int Total => 8;
}

/// <summary>Stormcoil Sticker (BG35_MagicItem_302): "After 5 friendly minions die, get a random Mech. (5 left!)"</summary>
[CardEffect("BG35_MagicItem_302")]
internal sealed class StormcoilSticker : DeathsProgressGet
{
    protected override int Total => 5;
}

/// <summary>
/// MechGyver (BG22_HERO_200p, Ini Stormcoil): "After 9 friendly minions die, get a random Mech." Its text shows no count;
/// assumption: it keeps the count in TAG_SCRIPT_DATA_NUM_1 as the trinkets do (Ini is in no owner's log), 9 without it.
/// </summary>
[CardEffect("BG22_HERO_200p")]
internal sealed class MechGyver : DeathsProgressGet
{
    protected override int Total => 9;
}

/// <summary>
/// Wildfeather Duster (BG35_MagicItem_700): "After you summon 6 Beasts, get a random Beast. (6 left!)" A Beast summoned in
/// the fight (a token, a reborn copy) counts.
/// </summary>
[CardEffect("BG35_MagicItem_700")]
internal sealed class WildfeatherDuster : ProgressGet
{
    protected override int Total => 6;

    public override void AfterFriendlySummoned(Fight fight, Unit self, Unit summoned)
    {
        if (summoned.Is(Tribe.Beast) && summoned.Tribes != Tribe.None)
        {
            Step(fight, self);
        }
    }
}

/// <summary>
/// Dragon's Eye (BG36_MagicItem_215): "Your Dragons' Battlecries trigger an extra time." In combat a Battlecry triggers
/// through Timewarped Greenskeeper's Rally and the like (<see cref="Fight.TriggerBattlecry"/>): a Dragon's (Electric
/// Synthesizer's) triggers once more. "An extra time" adds up with Brann's "twice" (as Titus's extra Deathrattle does).
/// </summary>
[CardEffect("BG36_MagicItem_215")]
internal sealed class DragonsEye : CombatEffect
{
    public override int AddedBattlecries(Fight fight, Unit self, Unit source) => source.Is(Tribe.Dragon) && source.Tribes != Tribe.None ? 1 : 0;
}

/// <summary>
/// I'll Take That! (TB_BaconShop_HP_053, Arch-Villain Rafaam): "Next combat, get a plain copy of the first minion you kill."
/// </summary>
/// <remarks>
/// The first enemy minion a friendly minion's damage or destroy leaves dying (as Icesnarl), once per fight; the card is
/// "got" for "whenever a card is added to your hand". Whether the power was used this turn is not in the capture (no log of
/// Rafaam): it is assumed used, an estimate listed for both sides (Tracking/SimEstimates, input hero_power_used).
/// </remarks>
[CardEffect("TB_BaconShop_HP_053")]
internal sealed class IllTakeThat : CombatEffect
{
    public const string Id = "TB_BaconShop_HP_053";

    public override string? CapturedInput => "использована ли сила в этом ходу: не в снимке, оценка «использована» (hero_power_used)";

    public override bool HasInputs(SimSide side, string cardId) => SimInputs.Estimate(side, SimInputs.HeroPowerUsed) is not null;

    public override void AfterFriendlyDealtDamage(Fight fight, Unit self, Unit dealer, Unit victim, int amount)
    {
        if (self.Counter == 0 && victim.IsDying && victim.Killer == dealer)
        {
            self.Counter = 1;
            fight.AddToHand(self.Side, victim.CardId);
        }
    }
}

/// <summary>
/// Lock and Load (BG22_HERO_000p_Alt, Tavish Stormpike): "Remove a minion in the Tavern. When you have space next combat,
/// fire it at a random enemy minion." (mechanics START_OF_COMBAT.)
/// </summary>
/// <remarks>
/// With room on the board at Start of Combat the shot resolves before the capture, which shows its result: nothing is left
/// to simulate. With a full board it waits for room during the fight; which minion is loaded and what "fire it" does are
/// not established (the one log with this power, 2026-11-15 20:36, is the opponent's and shows no shot): the fight is then
/// missing_input, never a guess. A full board at capture stands for a full board at Start of Combat (deaths at Start of
/// Combat may have made room since: then the shot already happened, which this counts as missing_input too).
/// </remarks>
[CardEffect("BG22_HERO_000p_Alt")]
internal sealed class LockAndLoad : CombatEffect
{
    public const int FullBoard = 7;

    public override string? CapturedInput => "место на доске в начале боя: есть — выстрел разрешён до снимка; полная доска — заряженное существо и выстрел не в снимке";

    public override bool HasInputs(SimSide side, string cardId) => side.Minions.Count < FullBoard && side.DeathsBeforeCapture.Count == 0;
}
