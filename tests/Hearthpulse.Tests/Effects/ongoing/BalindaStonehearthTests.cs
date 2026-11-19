using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Battlegrounds.Effects.Attack;
using Hearthpulse.Core.Battlegrounds.Effects.Ongoing;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim.Ongoing;

/// <summary>
/// Balinda Stonehearth (BG35_883): "Your spells that target friendly minions cast twice." Golden: "three times". The
/// spells come from test Rallies (<see cref="RallyCastOnSelf"/> and friends) cast through <see cref="Fight.CastSpell"/>;
/// their minions have 0 Attack and never attack. Results are worked out by hand in each comment.
/// </summary>
public sealed class BalindaStonehearthTests
{
    private const string Balinda = "BG35_883";

    private static EffectRegistry Registry => Effects(
        typeof(BalindaStonehearth), typeof(RallyCastOnSelf), typeof(RallyCastUntargeted), typeof(RallyCastOnEnemy), typeof(CountSpells));

    private static List<(int, int)> Stats(IEnumerable<SimMinion> minions) => minions.Select(m => (m.Attack, m.Health)).ToList();

    [Fact]
    public void A_spell_on_a_friendly_minion_is_cast_twice()
    {
        // The caster 1/1 attacks a 0/3: its spell (+1/+1 on itself) is cast twice → 3/3, it deals 3 and kills the 0/3.
        // "After you cast a spell" runs per cast: the counter 0/1 gets +1 Attack twice.
        var ours = Side(Minion(1, 1, cardId: RallyCastOnSelf.Id), Minion(6, 6, cardId: Balinda), Minion(0, 1, cardId: CountSpells.Id));
        var result = Fight(ours, Side(Minion(0, 3, cardId: "W")), oursFirst: true, Registry);

        Assert.Equal(1, result.Outcome);
        Assert.Equal([(3, 3), (6, 6), (2, 1)], Stats(result.Ours));
        Assert.Equal(2, result.Lines("we cast TEST_SPELL").Count);
    }

    [Fact]
    public void Golden_balinda_casts_three_times_and_copies_do_not_add_up()
    {
        // Golden: three casts → 4/4. With a normal Balinda beside it: still three (the largest), not four.
        var ours = Side(Minion(1, 1, cardId: RallyCastOnSelf.Id), Minion(6, 6, cardId: Balinda), Minion(12, 12, cardId: Balinda, golden: true));
        var result = Fight(ours, Side(Minion(0, 4, cardId: "W")), oursFirst: true, Registry);

        Assert.Equal(1, result.Outcome);
        Assert.Equal((4, 4), Stats(result.Ours)[0]);
        Assert.Equal(3, result.Lines("we cast TEST_SPELL").Count);
    }

    [Fact]
    public void Spells_without_a_target_or_on_an_enemy_are_cast_once()
    {
        // The first 1/1 casts an untargeted +1/+1 on every friendly minion, once: 2/2, 2/2, Balinda 7/7; it deals 2 (0/5).
        // The second casts 2 damage on the 0/5, once (0/3), and deals 2 (0/1). Balinda's 7 kills it.
        var ours = Side(Minion(1, 1, cardId: RallyCastUntargeted.Id), Minion(1, 1, cardId: RallyCastOnEnemy.Id), Minion(6, 6, cardId: Balinda));
        var result = Fight(ours, Side(Minion(0, 7, cardId: "W")), oursFirst: true, Registry);

        Assert.Equal(1, result.Outcome);
        Assert.Equal([(2, 2), (2, 2), (7, 7)], Stats(result.Ours));
        Assert.Single(result.Lines("we cast TEST_SPELL_ALL"));
        Assert.Single(result.Lines("we cast TEST_SPELL_ENEMY"));
    }

    [Fact]
    public void A_dead_balinda_casts_nothing_extra_and_the_opponents_counts_for_them()
    {
        // Without a living Balinda: once.
        var alone = Fight(Side(Minion(1, 1, cardId: RallyCastOnSelf.Id)), Side(Minion(0, 2, cardId: "W")), oursFirst: true, Registry);
        Assert.Single(alone.Lines("we cast TEST_SPELL"));

        // Their Balinda doubles their spell, not ours.
        var theirs = Side(Minion(1, 1, cardId: RallyCastOnSelf.Id), Minion(6, 6, cardId: Balinda));
        var result = Fight(Side(Minion(0, 3, cardId: "W")), theirs, oursFirst: false, Registry);
        Assert.Equal(2, result.Lines("they cast TEST_SPELL").Count);
        Assert.True(EffectRegistry.Default.Implements(Balinda, golden: true, isOpponent: true));
    }

    [Fact]
    public void Mighty_dragonbreath_has_no_target_so_balinda_does_not_repeat_it()
    {
        // Crimson Vindicator's Rally casts Mighty Dragonbreath (no target) once: +3/+2 for every minion, again for Dragons
        // (none has Divine Shield here) → the 8/8 Dragon is 14/12 and kills the 0/11.
        var ours = Side(Minion(8, 8, cardId: "BG36_241", tribes: Tribe.Dragon), Minion(6, 6, cardId: Balinda));
        var result = Fight(ours, Side(Minion(0, 11, cardId: "W")), oursFirst: true, Effects(typeof(BalindaStonehearth), typeof(CrimsonVindicator)));

        Assert.Single(result.Lines("we cast BG36_246"));
        Assert.Equal((14, 12), Stats(result.Ours)[0]);
    }
}

/// <summary>Rally: cast TEST_SPELL ("+1/+1") on this minion.</summary>
[CardEffect(Id)]
internal sealed class RallyCastOnSelf : CombatEffect
{
    public const string Id = "TEST_RALLY_CAST_SELF";

    public override void OnAttack(Fight fight, Unit self, Unit target) =>
        fight.CastSpell(self.Side, "TEST_SPELL", self, f => f.Buff(self, 1, 1));
}

/// <summary>Rally: cast TEST_SPELL_ALL ("Give your minions +1/+1"), no target.</summary>
[CardEffect(Id)]
internal sealed class RallyCastUntargeted : CombatEffect
{
    public const string Id = "TEST_RALLY_CAST_ALL";

    public override void OnAttack(Fight fight, Unit self, Unit target) =>
        fight.CastSpell(self.Side, "TEST_SPELL_ALL", null, f =>
        {
            foreach (var unit in f.Alive(self.Side))
            {
                f.Buff(unit, 1, 1);
            }
        });
}

/// <summary>Rally: cast TEST_SPELL_ENEMY ("Deal 2 damage") on the attacked minion.</summary>
[CardEffect(Id)]
internal sealed class RallyCastOnEnemy : CombatEffect
{
    public const string Id = "TEST_RALLY_CAST_ENEMY";

    public override void OnAttack(Fight fight, Unit self, Unit target) =>
        fight.CastSpell(self.Side, "TEST_SPELL_ENEMY", target, f => f.DealDamage(null, target, 2));
}

/// <summary>After you cast a spell, gain +1 Attack.</summary>
[CardEffect(Id)]
internal sealed class CountSpells : CombatEffect
{
    public const string Id = "TEST_COUNT_SPELLS";

    public override void AfterFriendlySpellCast(Fight fight, Unit self, string spellCardId, Unit? target) => fight.Buff(self, 1, 0);
}
