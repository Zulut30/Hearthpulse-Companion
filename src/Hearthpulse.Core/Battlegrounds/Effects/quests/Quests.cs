using Hearthpulse.Core.Battlegrounds.Effects.Ongoing;

namespace Hearthpulse.Core.Battlegrounds.Effects.Quests;

// Battlegrounds quests whose task can progress during a fight (Sire Denathrius's "Whodunit?" offers them; all seven are in
// today's pool, Hearthpulse API v1 in_pool 2026-11-19). Card text from HearthstoneJSON enUS, build 256589, where the
// task's number is the entity's QUEST_PROGRESS_TOTAL.
//
// What the log shows (Power.log 2026-11-15, L500730-L504885, a quest of another player): a quest sits in its owner's
// SECRET zone with QUEST_PROGRESS, QUEST_PROGRESS_TOTAL and QUEST_REWARD_DATABASE_ID (the reward card's dbfId). The tracker
// passes the local player's quests as extras with those tags (SimInputs.ObjectiveTags); the opponent's quest is hidden and
// never simulated.
//
// Assumption (no kept log has a quest completed during a fight): progress made in combat counts as in the Tavern, and a
// quest completed mid-fight gives its reward at once, so a reward that acts in combat (Turbulent Tombs, Gilnean War Horn,
// Temporal Tampering, Rallying Cry) acts for the rest of that fight. A reward whose text gets a copy of a card adds a card
// to the hand then.

/// <summary>
/// A quest as a player-level effect: <see cref="Unit.Counter"/> is the progress, <see cref="Unit.Counter2"/> the task's
/// number while the quest is open (0 once completed or when the capture holds no task).
/// </summary>
internal abstract class QuestEffect : CombatEffect
{
    public override void OnFightStart(Fight fight, Unit self)
    {
        var info = self.Side.Info;
        self.Counter = Math.Max(0, info.ExtraTag(self.CardId, SimInputs.QuestProgress) ?? 0);
        var total = info.ExtraTag(self.CardId, SimInputs.QuestTotal) ?? 0;
        self.Counter2 = total > 0 && self.Counter < total ? total : 0;
    }

    /// <summary>The task moved by <paramref name="amount"/>.</summary>
    protected static void Progress(Fight fight, Unit self, int amount = 1)
    {
        if (self.Counter2 <= 0)
        {
            return;
        }

        self.Counter += amount;
        if (self.Counter >= self.Counter2)
        {
            Complete(fight, self);
        }
    }

    /// <summary>A task measured by a value (total Attack): completed once <paramref name="value"/> reaches the number.</summary>
    protected static void Reach(Fight fight, Unit self, int value)
    {
        if (self.Counter2 > 0 && value >= self.Counter2)
        {
            self.Counter = value;
            Complete(fight, self);
        }
    }

    private static void Complete(Fight fight, Unit self)
    {
        self.Counter2 = 0;
        var side = self.Side;
        if (QuestRewards.Of(side.Info.ExtraTag(self.CardId, SimInputs.QuestReward)) is not { } reward)
        {
            return;
        }

        if (reward.GetsCopy)
        {
            fight.AddToHand(side, Spells.CardToHand.AnyCard);
        }

        if (EffectRegistry.Default.Get(reward.CardId, golden: false) is { } effect)
        {
            PlayerEffects.Attach(side, reward.CardId, effect);
        }
    }
}

/// <summary>The quest rewards with a combat effect, by the dbfId a quest's QUEST_REWARD_DATABASE_ID names (HearthstoneJSON 256589).</summary>
internal static class QuestRewards
{
    public sealed record Reward(string CardId, bool GetsCopy);

    private static readonly Dictionary<int, Reward> ByDbf = new()
    {
        [104670] = new("BG27_Reward_803", GetsCopy: true),  // Turbulent Tombs: "Get a copy of '{0}'. Your Deathrattles trigger an extra time."
        [104673] = new("BG27_Reward_802", GetsCopy: true),  // Gilnean War Horn: "Get a copy of '{0}'. Your Battlecries trigger an extra time."
        [110299] = new("BG28_Reward_501", GetsCopy: false), // Temporal Tampering: "Your Tavern spells cast an extra time."
        [122905] = new("BG33_Reward_021", GetsCopy: true),  // Rallying Cry: "Get a copy of '{0}'. Your Rallies trigger an extra time."
        // Out of today's pool (E6a; HearthstoneJSON latest 2026-11-03): rewards that need no state from the start of the fight.
        // The Smoking Gun, Volatile Venom and Tumbling Disaster set their summon bonus at the fight's start, so they are not
        // attached mid-fight.
        [89483] = new("BG24_Reward_113", GetsCopy: false),  // Ritual Dagger: "After a friendly Deathrattle minion dies, give it +5/+5 permanently."
        [104697] = new("BG27_Reward_502", GetsCopy: false), // Boom Squad: "Avenge (3): Deal 10 damage to the highest-Health enemy minion."
        [110551] = new("BG28_Reward_518", GetsCopy: false), // Stable Amalgamation: "Avenge (7): When you have space, summon a 50/50 Amalgam."
    };

    /// <summary>The reward with a combat effect of dbfId <paramref name="dbf"/>, or null (none captured, or a Tavern-only reward).</summary>
    public static Reward? Of(int? dbf) => dbf is { } id && ByDbf.TryGetValue(id, out var reward) ? reward : null;
}

/// <summary>Assemble a Lineup (BG24_Quest_114): "Quest: Summon {N} minions." Each minion summoned in the fight counts.</summary>
[CardEffect("BG24_Quest_114")]
internal sealed class AssembleALineup : QuestEffect
{
    public override void AfterFriendlySummoned(Fight fight, Unit self, Unit summoned) => Progress(fight, self);
}

/// <summary>Crack the Case (BG24_Quest_352): "Quest: Have friendly minions attack {N} times." Each attack counts.</summary>
[CardEffect("BG24_Quest_352")]
internal sealed class CrackTheCase : QuestEffect
{
    public override void OnFriendlyAttack(Fight fight, Unit self, Unit attacker, Unit target) => Progress(fight, self);
}

/// <summary>Fill the Cauldron (BG28_Quest_500): "Quest: Cast {N} spells." Each spell the player casts in the fight counts.</summary>
[CardEffect("BG28_Quest_500")]
internal sealed class FillTheCauldron : QuestEffect
{
    public override void AfterFriendlySpellCast(Fight fight, Unit self, string spellCardId, Unit? target) => Progress(fight, self);
}

/// <summary>
/// Find the Murder Weapon (BG24_Quest_123): "Quest: Increase a friendly minion's stats {N} times." Each stat gain of a
/// friendly minion from an effect counts (<see cref="Fight.Buff"/>; auras and summon buffs are not reported by the engine).
/// </summary>
[CardEffect("BG24_Quest_123")]
internal sealed class FindTheMurderWeapon : QuestEffect
{
    public override void AfterFriendlyGainedStats(Fight fight, Unit self, Unit unit, int attack, int health) => Progress(fight, self);
}

/// <summary>
/// Pressure the Authorities (BG27_Quest_801): "Quest: Get your warband to {N} total Attack." The total Attack of the living
/// friendly minions is checked after each stat gain and summon.
/// </summary>
[CardEffect("BG27_Quest_801")]
internal sealed class PressureTheAuthorities : QuestEffect
{
    public override void AfterFriendlyGainedStats(Fight fight, Unit self, Unit unit, int attack, int health) => Check(fight, self);

    public override void AfterFriendlySummoned(Fight fight, Unit self, Unit summoned) => Check(fight, self);

    private static void Check(Fight fight, Unit self)
    {
        var total = 0L;
        foreach (var unit in fight.Alive(self.Side))
        {
            total += Math.Max(0, unit.Attack);
        }

        Reach(fight, self, (int)Math.Min(int.MaxValue, total));
    }
}

/// <summary>Reenact the Murder (BG24_Quest_124): "Quest: Have {N} friendly minions die." Each friendly death counts.</summary>
[CardEffect("BG24_Quest_124")]
internal sealed class ReenactTheMurder : QuestEffect
{
    public override void AfterFriendlyDeath(Fight fight, Unit self, Unit dead) => Progress(fight, self);
}

/// <summary>Round Up the Suspects (BG27_Quest_802): "Quest: Destroy {N} enemy minions." Each enemy minion that dies counts.</summary>
[CardEffect("BG27_Quest_802")]
internal sealed class RoundUpTheSuspects : QuestEffect
{
    public override void AfterEnemyDeath(Fight fight, Unit self, Unit dead) => Progress(fight, self);
}
