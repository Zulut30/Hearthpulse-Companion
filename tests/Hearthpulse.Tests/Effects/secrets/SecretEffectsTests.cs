using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Battlegrounds.Effects.Secrets;
using static Hearthpulse.Tests.Sim.SimTestKit;

namespace Hearthpulse.Tests.Sim.Secrets;

/// <summary>
/// Battlegrounds Secrets in combat (TASK-2026-11-19 E7a): The Great Akazamzarak's TB_Bacon_Secrets_* and the Eleventh Hour
/// anomaly's Secret, from <see cref="SimSide.Secrets"/>. Each triggers once. Expected results are worked out by hand in each
/// comment; the trace names a minion by its card id and current stats.
/// </summary>
public sealed class SecretEffectsTests
{
    private static SimSide With(SimSide side, params string[] secrets) => side with { Secrets = secrets };

    private static int Triggers(FightResult result, string name) => result.Lines($"our Secret {name} triggers").Count;

    [Fact]
    public void Venomstrike_trap_summons_a_poisonous_cobra_at_the_right_end_once()
    {
        // Their 1/50 attacks our 0/20 (the only target): the trap summons a 2/3 Poisonous Cobra at slot 1. Our 0/20 cannot
        // attack, so the Cobra does and poisons the 1/50.
        var result = Fight(With(Side(Minion(0, 20, cardId: "WALL")), VenomstrikeTrap.Id), Side(Minion(1, 50, cardId: "FOE")), oursFirst: false);

        Assert.Equal(["summoned our BG_EX1_170 2/3 Poisonous at 1"], result.Lines("summoned our"));
        Assert.Equal(1, Triggers(result, "Venomstrike Trap"));
        Assert.Equal(1, result.Outcome);
    }

    [Fact]
    public void Better_venomstrike_trap_gives_the_cobra_reborn()
    {
        var result = Fight(With(Side(Minion(0, 20, cardId: "WALL")), VenomstrikeTrap.BetterId), Side(Minion(1, 50, cardId: "FOE")), oursFirst: false);

        Assert.Equal(["summoned our BG_EX1_170 2/3 Poisonous, Reborn at 1"], result.Lines("summoned our"));
    }

    [Fact]
    public void Snake_trap_summons_three_snakes_once_and_waits_on_a_full_board()
    {
        // Their 1/100 attacks four times while our 1/1 Snakes trade into it: three Snakes come from the first attack only.
        var result = Fight(With(Side(Minion(0, 30, cardId: "WALL")), SnakeTrap.Id), Side(Minion(1, 100, cardId: "FOE")), oursFirst: false);
        Assert.Equal(3, result.Lines("summoned our BG_EX1_554t 1/1").Count);
        Assert.Equal(1, Triggers(result, "Snake Trap"));

        // Five minions: two Snakes fit.
        var five = Fight(With(Side([.. Enumerable.Range(0, 5).Select(i => Minion(0, 30, cardId: "W" + i))]), SnakeTrap.Id),
                         Side(Minion(1, 100, cardId: "FOE")), oursFirst: false);
        Assert.Equal(2, five.Lines("summoned our BG_EX1_554t").Count);

        // A full board: the trap waits (a summoning Secret needs room) until their 10/30 has killed W0, then one Snake fits.
        var full = Fight(With(Side([.. Enumerable.Range(0, 7).Select(i => Minion(0, 10, cardId: "W" + i))]), SnakeTrap.Id),
                         Side(Minion(10, 30, cardId: "FOE")), oursFirst: false);
        Assert.Equal(1, Triggers(full, "Snake Trap"));
        Assert.True(full.IndexOf("our Secret Snake Trap triggers") > full.IndexOf("our W0 0/0 dies"));
        Assert.Equal(["summoned our BG_EX1_554t 1/1 at 6"], full.Lines("summoned our"));
    }

    [Fact]
    public void Splitting_image_summons_a_copy_of_the_attacked_minion_as_it_is_to_its_right()
    {
        // Our buffed 5/8 Taunt (printed 3/8) is attacked by their 2/30 before damage: the copy is a 5/8 Taunt at slot 1.
        var ours = Side(Minion(3, 8, Keyword.Taunt, "TAUNTER") with { Attack = 5 }, Minion(0, 9, cardId: "RIGHT"));
        var result = Fight(With(ours, SplittingImage.Id), Side(Minion(2, 30, cardId: "FOE")), oursFirst: false);

        Assert.Equal("summoned our TAUNTER 5/8 Taunt at 1", result.Lines("summoned our")[0]);
        Assert.Equal(1, Triggers(result, "Splitting Image"));
    }

    [Fact]
    public void A_copy_keeps_the_damage_the_minion_has_taken()
    {
        // Our 3/8 came to the fight damaged to 3/5: Splitting Image copies it as it is, 3/5 of 8.
        var ours = Side(Minion(3, 8, cardId: "HURT") with { Health = 5 });
        var result = Fight(With(ours, SplittingImage.Id), Side(Minion(1, 30, cardId: "FOE")), oursFirst: false);

        Assert.Equal("summoned our HURT 3/5 at 1", result.Lines("summoned our")[0]);
    }

    [Fact]
    public void Pack_tactics_summons_a_three_three_copy_and_the_better_one_a_full_copy()
    {
        var ours = Side(Minion(8, 8, Keyword.Taunt | Keyword.DivineShield, "BIG"));
        var plain = Fight(With(ours, PackTactics.Id), Side(Minion(1, 50, cardId: "FOE")), oursFirst: false);
        Assert.Equal("summoned our BIG 3/3 Taunt, DivineShield at 1", plain.Lines("summoned our")[0]);

        var better = Fight(With(ours, SplittingImage.BetterPackTacticsId), Side(Minion(1, 50, cardId: "FOE")), oursFirst: false);
        Assert.Equal("summoned our BIG 8/8 Taunt, DivineShield at 1", better.Lines("summoned our")[0]);
        Assert.Equal(1, Triggers(better, "Better Pack Tactics"));
    }

    [Fact]
    public void Autodefense_matrix_shields_the_attacked_minion_once()
    {
        // Our 0/3 is attacked by their 5/100 three times: the first hit breaks the Matrix's shield, the second kills it.
        var result = Fight(With(Side(Minion(0, 3, cardId: "SHIELDED")), AutodefenseMatrix.Id), Side(Minion(5, 100, cardId: "FOE")), oursFirst: false);

        Assert.Single(result.Lines("our SHIELDED 0/3 loses Divine Shield"));
        Assert.Equal(1, Triggers(result, "Autodefense Matrix"));
        Assert.Single(result.Lines("our SHIELDED"), l => l.EndsWith(" dies", StringComparison.Ordinal));
    }

    [Fact]
    public void Better_autodefense_matrix_takes_two_hits_to_break()
    {
        // The shield comes back after the first hit: two hits break, the third kills.
        var result = Fight(With(Side(Minion(0, 3, cardId: "SHIELDED")), AutodefenseMatrix.BetterId), Side(Minion(5, 100, cardId: "FOE")), oursFirst: false);

        Assert.Equal(2, result.Lines("our SHIELDED 0/3 loses Divine Shield").Count);
        Assert.Equal(1, Triggers(result, "Autodefense Matrix"));
    }

    [Fact]
    public void The_avenge_secret_buffs_a_random_friendly_minion_once()
    {
        // Their 1/100 attacks our 1/1 A (pick 0 of A and B) and kills it: the Secret gives B +3/+2 (3/12). Later deaths do not
        // trigger it again.
        var ours = Side(Minion(1, 1, cardId: "A"), Minion(0, 10, cardId: "B"), Minion(1, 1, cardId: "C"));
        var result = Fight(With(ours, AvengeSecret.Id), Side(Minion(1, 100, cardId: "FOE")), false, null, 0);

        Assert.Equal(1, Triggers(result, "Avenge"));
        Assert.NotEmpty(result.Lines("our B 3/12 attacks"));
    }

    [Fact]
    public void Redemption_returns_the_minion_with_one_health_and_the_better_one_as_it_was()
    {
        // Their 10/100 kills our buffed 7/6 Divine Shield minion (printed 2/4, shield broken by the first hit, killed by the
        // second). Redemption: a fresh 2/1 copy with its printed shield; Better Redemption: 7/6 with its card's shield.
        var ours = Side(Minion(2, 4, Keyword.DivineShield, "SAVED") with { Attack = 7, Health = 6, MaxHealth = 6 });
        var plain = Fight(With(ours, Redemption.Id), Side(Minion(10, 100, cardId: "FOE")), oursFirst: false);
        Assert.Equal("summoned our SAVED 2/1 DivineShield at 0", Assert.Single(plain.Lines("summoned our")));

        var better = Fight(With(ours, Redemption.BetterId), Side(Minion(10, 100, cardId: "FOE")), oursFirst: false);
        Assert.Equal("summoned our SAVED 7/6 DivineShield at 0", Assert.Single(better.Lines("summoned our")));
        Assert.Equal(1, Triggers(better, "Better Redemption"));
    }

    [Fact]
    public void Hand_of_salvation_returns_the_second_minion_that_dies_in_the_combat()
    {
        // Their 10/100 kills A, then B: B comes back as a fresh 1/1 copy. With one death at Start of Combat already, A is
        // the second.
        var ours = Side(Minion(1, 1, cardId: "A"), Minion(1, 1, cardId: "B"));
        var result = Fight(With(ours, HandOfSalvation.Id), Side(Minion(10, 100, cardId: "FOE")), false, null, 0, 0);
        Assert.Equal("summoned our B 1/1 at 0", Assert.Single(result.Lines("summoned our")));

        var early = Fight(With(ours, HandOfSalvation.Id) with { DeathsBeforeCapture = [Minion(1, 1, cardId: "SOC")] },
                          Side(Minion(10, 100, cardId: "FOE")), false, null, 0);
        Assert.Equal("summoned our A 1/1 at 0", early.Lines("summoned our")[0]);
    }

    [Fact]
    public void Effigy_summons_a_random_pool_minion_of_the_same_tier()
    {
        // Our tier-3 minion dies: a tier-3 pool minion takes its slot (the scripted pick chooses which; index 0 is the first
        // tier-3 minion of the pool in card id order). A token counts as tier 1.
        var tier3 = MinionPool.OfTier(3, Tribe.None);
        Assert.NotEmpty(tier3);
        var ours = Side(Minion(1, 1, cardId: "DIES") with { TechLevel = 3 });
        var result = Fight(With(ours, Effigy.Id), Side(Minion(10, 100, cardId: "FOE")), false, null, 0, 0);

        Assert.StartsWith($"summoned our {tier3[0].CardId} ", result.Lines("summoned our")[0], StringComparison.Ordinal);
        Assert.Equal(1, Triggers(result, "Effigy"));
        Assert.All(MinionPool.Minions, m => Assert.InRange(m.TechLevel, 1, 7));
        Assert.Equal(1, MinionPool.IndexOf(MinionPool.OfTier(1, Tribe.None)[1].CardId, 1));
    }

    [Fact]
    public void Effigy_draws_only_from_the_lobby_tribes()
    {
        var beasts = MinionPool.OfTier(2, Tribe.Beast);
        Assert.NotEmpty(beasts);
        Assert.All(beasts, m => Assert.True(m.Tribes == Tribe.None || (m.Tribes & Tribe.Beast) != 0));
        Assert.True(beasts.Count < MinionPool.OfTier(2, Tribe.None).Count);
    }

    [Fact]
    public void Reckoning_destroys_an_enemy_minion_after_it_deals_three_or_more_damage()
    {
        // Their 3/50 hits our 0/30: Reckoning destroys it after the damage.
        var result = Fight(With(Side(Minion(0, 30, cardId: "WALL")), Reckoning.Id), Side(Minion(3, 50, cardId: "BIG"), Minion(2, 50, cardId: "SMALL")), oursFirst: false);
        Assert.Single(result.Lines("their BIG 3/50 is destroyed"));
        Assert.Equal(1, Triggers(result, "Reckoning"));

        // 2 damage does not trigger it, nor a hit Divine Shield absorbs: their 5/5 is destroyed after its second hit only.
        var small = Fight(With(Side(Minion(0, 30, cardId: "WALL")), Reckoning.Id), Side(Minion(2, 50, cardId: "SMALL")), oursFirst: false);
        Assert.Equal(0, Triggers(small, "Reckoning"));
        var shielded = Fight(With(Side(Minion(0, 30, Keyword.DivineShield, "WALL")), Reckoning.Id), Side(Minion(5, 5, cardId: "BIG")), oursFirst: false);
        Assert.True(shielded.IndexOf("their BIG 5/5 is destroyed") > shielded.IndexOf("our WALL 0/25 takes 5"));
        Assert.True(shielded.IndexOf("our WALL 0/25 takes 5") > shielded.IndexOf("our WALL 0/30 loses Divine Shield"));
    }

    [Fact]
    public void Two_secrets_on_one_attack_trigger_in_the_order_they_were_played()
    {
        // Snake Trap was played first: its Snakes take slots 1-3, then Venomstrike's Cobra slot 4.
        var result = Fight(With(Side(Minion(0, 20, cardId: "WALL")), SnakeTrap.Id, VenomstrikeTrap.Id), Side(Minion(1, 50, cardId: "FOE")), oursFirst: false);

        Assert.Equal(["summoned our BG_EX1_554t 1/1 at 1", "summoned our BG_EX1_554t 1/1 at 2", "summoned our BG_EX1_554t 1/1 at 3",
                      "summoned our BG_EX1_170 2/3 Poisonous at 4"], result.Lines("summoned our"));
    }

    [Fact]
    public void The_opponents_live_secrets_never_act_but_their_post_game_secrets_do()
    {
        // During the game the opponent's side never carries Secrets (the tracker leaves them out); after the game the ids the
        // log showed come back through SimInputs.PostGame.
        var theirs = Side(Minion(0, 20, cardId: "WALL")) with { HiddenSecrets = [SnakeTrap.Id] };
        var live = Fight(Side(Minion(1, 50, cardId: "FOE")), theirs, oursFirst: true);
        Assert.Empty(live.Lines("summoned their"));

        var post = Fight(Side(Minion(1, 50, cardId: "FOE")), SimInputs.PostGame(theirs), oursFirst: true);
        Assert.Equal(3, post.Lines("summoned their BG_EX1_554t").Count);
    }

    [Fact]
    public void Every_secret_of_the_pool_has_an_effect()
    {
        string[] ids =
        [
            VenomstrikeTrap.Id, VenomstrikeTrap.BetterId, SnakeTrap.Id, SplittingImage.Id, Effigy.Id, AutodefenseMatrix.Id, AutodefenseMatrix.BetterId,
            AvengeSecret.Id, Redemption.Id, Redemption.BetterId, HandOfSalvation.Id, DamageRules.IceBlock, Reckoning.Id, PackTactics.Id,
            SplittingImage.BetterPackTacticsId, DamageRules.EleventhHour,
        ];
        Assert.All(ids, id => Assert.NotNull(EffectRegistry.Default.Get(id, golden: false)));

        // A Secret no effect knows still counts as "not simulated" on our side.
        var side = Side(Minion(1, 1)) with { Secrets = ["TB_Bacon_Secrets_99"] };
        Assert.Single(CombatSimulator.Explain(side, isOpponent: false));
        Assert.Empty(CombatSimulator.Explain(side with { Secrets = [SnakeTrap.Id] }, isOpponent: false));
    }
}
