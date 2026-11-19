using Hearthpulse.Core.Battlegrounds.Effects.Ongoing;

namespace Hearthpulse.Core.Battlegrounds.Effects.Spells;

/// <summary>Shared pieces of "whenever / after you cast a spell" effects.</summary>
internal static class SpellTriggers
{
    /// <summary>Gives every living minion of <paramref name="side"/> (of <paramref name="tribe"/>) +attack/+health.</summary>
    public static void BuffAll(Fight fight, Side side, int attack, int health, Tribe tribe = Tribe.All)
    {
        foreach (var unit in fight.Alive(side, tribe))
        {
            fight.Buff(unit, attack, health);
        }
    }

    /// <summary>
    /// "Your X have +attack/+health this game (wherever they are)" during a fight: living ones now, and every one summoned
    /// later this fight (tokens, reborn copies, minions summoned from the hand) when it arrives.
    /// </summary>
    public static void BuffThisGame(Fight fight, Side side, Tribe tribe, int attack, int health)
    {
        BuffAll(fight, side, attack, health, tribe);
        side.AddSummonBuff(tribe, attack, health);
    }

    /// <summary>Gives the minions in the side's hand list that match <paramref name="which"/> +attack/+health.</summary>
    public static void BuffHand(Side side, int attack, int health, Func<SimMinion, bool> which)
    {
        var hand = side.Hand;
        for (var i = 0; i < hand.Count; i++)
        {
            var card = hand[i];
            if (which(card))
            {
                hand[i] = Buffed(card, attack, health);
            }
        }
    }

    /// <summary>A hand card with +attack/+health (its maximum Health too).</summary>
    public static SimMinion Buffed(SimMinion card, int attack, int health) => card with
    {
        Attack = (int)Math.Clamp((long)card.Attack + attack, 0, int.MaxValue),
        Health = (int)Math.Clamp((long)card.Health + health, 1, int.MaxValue),
        MaxHealth = (int)Math.Clamp((long)Math.Max(card.MaxHealth, card.Health) + health, 1, int.MaxValue),
    };

    /// <summary>"Give your Deity +attack/+health" (see <see cref="Deity.Give"/>).</summary>
    public static void GiveDeity(Fight fight, Side side, int attack, int health) => Deity.Give(fight, side, attack, health);

    /// <summary>True while an "after you cast" hook runs for a Tavern spell.</summary>
    public static bool AfterTavernSpell(Fight fight) => fight.IsCasting && fight.CurrentSpell.IsTavernSpell;

    /// <summary>
    /// The card's first number: its captured TAG_SCRIPT_DATA_NUM_1, else the printed value of the latest card text.
    /// Balance changes reach the game server-side, so the live value on the entity wins. Real logs (2026-11-09..16) show
    /// these minions carrying their numbers there (the golden entity its golden ones): Hooktail 1, Czarina 4, Sha of Fear
    /// 3/3, Mindslasher 1/2 (golden 2/4), Shadow of Doubt 4/5, Tidecaller 3/4, Glambot 5/5, Tidehunter 9/9 (golden 18/18).
    /// Minions created in the fight (reborn copies) have none and use the printed value.
    /// </summary>
    public static int Shown1(Unit self, int normal, int golden) => self.Spec.ScriptData1 > 0 ? self.Spec.ScriptData1 : self.Pick(normal, golden);

    /// <summary>The card's second number: its captured TAG_SCRIPT_DATA_NUM_2, else the latest printed value (see <see cref="Shown1"/>).</summary>
    public static int Shown2(Unit self, int normal, int golden) => self.Spec.ScriptData2 > 0 ? self.Spec.ScriptData2 : self.Pick(normal, golden);

    /// <summary>
    /// A trinket's number: the captured TAG_SCRIPT_DATA_NUM_1/2 of the local player's trinket, else the latest printed
    /// value. The opponent's trinket tags are not captured, so theirs always use the printed value (unverified in a log:
    /// no capture shows these trinkets yet).
    /// </summary>
    public static int TrinketShown(Unit self, int which, int printed) =>
        self.Side.Info.ExtraTag(self.CardId, which == 1 ? "TAG_SCRIPT_DATA_NUM_1" : "TAG_SCRIPT_DATA_NUM_2") is > 0 and var value ? value : printed;
}
