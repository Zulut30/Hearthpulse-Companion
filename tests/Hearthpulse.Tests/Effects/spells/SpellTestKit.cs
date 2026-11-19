using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim.Spells;

/// <summary>
/// Boards for the spell tests. The two in-combat casters are the real cards: Seafloor Recruiter ("Rally: Cast Chef's
/// Choice on the minion to the right") for a spell on a minion, Crimson Vindicator ("Rally: Cast Mighty Dragonbreath")
/// for a Tavern spell without a target. Their wall is a 0-Attack minion our first attack kills, so a fight is one attack.
/// </summary>
internal static class SpellTestKit
{
    public const string Recruiter = "BG34_925";
    public const string Vindicator = "BG36_241";
    public const string ChefsChoice = "BG28_518";
    public const string Dragonbreath = "BG36_246";
    public const string TeaSet = "BG28_888";
    public const string DeityAttackTag = "BACON_OLD_GOD_ATTACK";
    public const string DeityHealthTag = "BACON_OLD_GOD_HEALTH";

    /// <summary>Seafloor Recruiter 3/5 Naga (golden 6/10).</summary>
    public static SimMinion RecruiterMinion(bool golden = false) =>
        Minion(golden ? 6 : 3, golden ? 10 : 5, cardId: Recruiter, tribes: Tribe.Naga, golden: golden, mechanics: CardMechanic.Rally);

    /// <summary>Crimson Vindicator as an 8/9 Divine Shield Dragon (no Windfury: one Rally per test).</summary>
    public static SimMinion VindicatorMinion() =>
        Minion(8, 9, Keyword.DivineShield, Vindicator, Tribe.Dragon, mechanics: CardMechanic.Rally);

    /// <summary>Their 0-Attack wall with this much Health.</summary>
    public static SimSide Wall(int health) => Side(Minion(0, health, cardId: "E"));

    /// <summary>A side with a sleeping Y'Shaarj of the given stats (the Deity holder needs its effect registered).</summary>
    public static SimSide WithDeity(SimSide side, int attack, int health) => side with
    {
        Extras = [.. side.Extras, Tokens.YShaarjId],
        Tags = new Dictionary<string, int>(side.Tags) { [DeityAttackTag] = attack, [DeityHealthTag] = health },
    };

    public static List<(int, int)> Stats(IEnumerable<SimMinion> minions) => minions.Select(m => (m.Attack, m.Health)).ToList();

    /// <summary>Test-only: traces "whenever <id>", "after <id>" and the hand list, to check order and hand buffs.</summary>
    [CardEffect("WATCH")]
    public sealed class Watcher : CombatEffect
    {
        public override void OnFriendlySpellCast(Fight fight, Unit self, in SpellCast spell) =>
            fight.Trace($"whenever {spell.CardId}");

        public override void AfterFriendlySpellCast(Fight fight, Unit self, string spellCardId, Unit? target) =>
            fight.Trace($"after {spellCardId} hand: {string.Join(", ", self.Side.Hand.Select(m => $"{m.CardId} {m.Attack}/{m.Health}"))}");
    }
}
