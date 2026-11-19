using System.Text;
using Hearthpulse.Core.Battlegrounds.Effects;

namespace Hearthpulse.Tests.Sim;

/// <summary>
/// The relevance classification proved card by card (task sim-all-mechanics, AUDIT2): every card of the coverage matrix -
/// minions, every hero's hero powers and their variants, trinkets, Dark Gifts, quests, quest rewards, anomalies, Tavern
/// spells, Duos-only cards - is read for combat signs by <see cref="RelevanceAudit"/>, which shares no code with the
/// classifier. A card <see cref="SimCoverage"/> calls no_effect or resolved that shows a sign needs a written reason in
/// <see cref="RelevanceAuditReasons"/>; a reason nothing needs any more fails too, so the list stays the truth.
/// Data: Fixtures/SimAudit/matrix-cards.json.gz, written by <c>Hearthpulse.Bench simmatrix</c> with docs/bg-sim-matrix.md.
/// </summary>
public sealed class RelevanceAuditTests
{
    private static string StatusNow(RelevanceAudit.Row row) =>
        SimCoverage.Code(SimCoverage.Classify(row.ClassifyId, row.Kind, row.Text).Status);

    private static bool Hidden(string status) => status is "no_effect" or "resolved";

    [Fact]
    public void The_fixture_covers_every_kind_of_card()
    {
        var kinds = RelevanceAudit.Rows.Select(r => r.Kind).ToHashSet();
        foreach (var kind in new[] { CoverageKind.Minion, CoverageKind.HeroPower, CoverageKind.Trinket, CoverageKind.DarkGift, CoverageKind.Quest,
                     CoverageKind.QuestReward, CoverageKind.Anomaly, CoverageKind.Spell, CoverageKind.DuosOnly })
        {
            Assert.Contains(kind, kinds);
        }

        Assert.True(RelevanceAudit.Rows.Count > 1000, $"{RelevanceAudit.Rows.Count} rows");
        Assert.True(RelevanceAudit.Heroes.Count(h => h.InPool) > 100, $"{RelevanceAudit.Heroes.Count(h => h.InPool)} heroes in the pool");
    }

    [Fact]
    public void The_fixture_says_what_the_code_says()
    {
        // The fixture is regenerated with docs/bg-sim-matrix.md; a classification change without a new matrix fails here
        // and in SimMatrixTests.The_generated_matrix_agrees_with_the_code.
        var stale = RelevanceAudit.Rows.Where(r => StatusNow(r) != r.Status).Select(r => $"{r.Id} {r.Name}: fixture {r.Status}, code {StatusNow(r)}").ToList();
        Assert.True(stale.Count == 0, "Run Hearthpulse.Bench simmatrix:\n" + string.Join('\n', stale));
    }

    [Fact]
    public void Every_hidden_card_with_a_combat_sign_has_a_reason()
    {
        var missing = new StringBuilder();
        foreach (var row in RelevanceAudit.Rows)
        {
            var status = StatusNow(row);
            var signs = RelevanceAudit.Signs(row with { Status = status });
            if (Hidden(status) && signs.Count > 0 && !RelevanceAuditReasons.ByCard.ContainsKey(row.Id) && !RelevanceAuditReasons.KnownGaps.ContainsKey(row.Id))
            {
                missing.AppendLine($"{row.Id} | {row.Kind} | {status} | {(row.InPool ? "pool" : "out")} | {row.Name} | {string.Join(" || ", signs)}");
            }
        }

        Assert.True(missing.Length == 0, "No reason for these combat signs:\n" + missing);
    }

    [Fact]
    public void Every_reason_is_still_needed()
    {
        var rows = RelevanceAudit.Rows.ToLookup(r => r.Id, StringComparer.Ordinal);
        var stale = new List<string>();
        foreach (var (id, reason) in RelevanceAuditReasons.ByCard)
        {
            Assert.False(string.IsNullOrWhiteSpace(reason), id);
            var row = rows[id].FirstOrDefault();
            if (row is null)
            {
                stale.Add($"{id}: not in the matrix");
                continue;
            }

            var status = StatusNow(row);
            if (!Hidden(status))
            {
                stale.Add($"{id} {row.Name}: now {status}");
            }
            else if (RelevanceAudit.Signs(row with { Status = status }).Count == 0)
            {
                stale.Add($"{id} {row.Name}: no combat sign any more");
            }
        }

        Assert.True(stale.Count == 0, "Reasons nothing needs:\n" + string.Join('\n', stale));
    }

    [Fact]
    public void A_known_gap_is_still_a_gap()
    {
        // Once its effect exists the card leaves the list (and its row shows implemented).
        var closed = RelevanceAuditReasons.KnownGaps.Keys.Where(id => EffectRegistry.Default.Get(id, golden: false) is not null).ToList();
        Assert.True(closed.Count == 0, "Effects exist for: " + string.Join(", ", closed));
        Assert.Empty(RelevanceAuditReasons.KnownGaps.Keys.Intersect(RelevanceAuditReasons.ByCard.Keys));
    }

    [Fact]
    public void Every_hero_of_the_pool_has_each_of_its_hero_powers_classified()
    {
        var powers = RelevanceAudit.Rows.Where(r => r.Kind == CoverageKind.HeroPower).ToDictionary(r => r.Id, StringComparer.Ordinal);
        var missing = new List<string>();
        foreach (var hero in RelevanceAudit.Heroes.Where(h => h.InPool))
        {
            Assert.NotEmpty(hero.Powers);
            foreach (var power in hero.Powers)
            {
                if (!powers.TryGetValue(power, out var row))
                {
                    missing.Add($"{hero.Id} {hero.Name}: {power} has no row");
                }
                else if (StatusNow(row) is "unknown" or "missing_effect" && !RelevanceAuditReasons.KnownGaps.ContainsKey(power))
                {
                    missing.Add($"{hero.Id} {hero.Name}: {power} {row.Name} is {StatusNow(row)}");
                }
            }
        }

        Assert.True(missing.Count == 0, string.Join('\n', missing));
    }

    [Fact]
    public void Every_combat_card_off_the_matrix_has_an_effect_or_a_known_gap()
    {
        // Tokens and hero power products (Fish of N'Zoth, Sneed's Shredder...) are not matrix rows: a relevant one without an
        // effect would only show in a live fight.
        var inMatrix = RelevanceAudit.Rows.Select(r => r.ClassifyId).Concat(RelevanceAudit.Rows.Select(r => r.Id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = CombatRelevance.Table.Concat(PlayerCardGenerated.Entries).Concat(PlayerCardTable.Entries)
            .Select(r => CombatRelevance.Find(r.CardId)!)
            .Where(r => r.Relevance is Relevance.Relevant or Relevance.Conditional && !inMatrix.Contains(r.CardId))
            .Where(r => EffectRegistry.Default.Get(r.CardId, golden: false) is null && !RelevanceAuditReasons.KnownGaps.ContainsKey(r.CardId))
            .Select(r => $"{r.CardId} {r.Name} ({r.Category}, {r.Origin})")
            .Distinct()
            .ToList();
        Assert.True(missing.Count == 0, string.Join('\n', missing));
    }

    [Fact]
    public void Cards_that_act_through_another_card_point_at_a_registered_effect()
    {
        foreach (var (id, via) in RelevanceAuditReasons.Via)
        {
            Assert.True(EffectRegistry.Default.Get(via, golden: false) is not null, $"{id}: {via} has no effect");
            Assert.True(RelevanceAuditReasons.ByCard.ContainsKey(id), id);
        }
    }

    [Fact]
    public void Heroes_with_variants_list_them()
    {
        // A sample the data shows: The Rat King's King of X (Discover by type) and Al'Akir's four Invocations.
        var heroes = RelevanceAudit.Heroes.ToDictionary(h => h.Id, StringComparer.Ordinal);
        Assert.Contains("TB_BaconShop_HP_041a", heroes.Values.First(h => h.Powers[0] == "TB_BaconShop_HP_041").Powers);
        Assert.Equal(["BG22_HERO_001p", "BG22_HERO_001p_t1", "BG22_HERO_001p_t2", "BG22_HERO_001p_t3", "BG22_HERO_001p_t4"],
            heroes.Values.First(h => h.Powers[0] == "BG22_HERO_001p").Powers);
    }

    [Fact]
    public void Every_anomaly_of_the_pool_is_classified()
    {
        var anomalies = RelevanceAudit.Rows.Where(r => r.Kind == CoverageKind.Anomaly && r.InPool).ToList();
        Assert.NotEmpty(anomalies);
        Assert.All(anomalies, a => Assert.True(StatusNow(a) is not ("unknown" or "missing_effect"), $"{a.Id} {a.Name}: {StatusNow(a)}"));
    }

    [Theory]
    [InlineData("<b>Rally:</b> This plays 2 permanent Blood Gems on all your other minions.", "rally")]
    [InlineData("After you trigger a <b>Deathrattle</b>, give your minions +3/+2 permanently.", "triggers_effects")]
    [InlineData("Whenever a friendly minion gains Attack, give it +3 Health.", "gains_stats")]
    [InlineData("Your Undead have +3 Attack.", "aura")]
    [InlineData("[x]<b>Start of Combat:</b> Give 4 random friendly minions \"<b>Deathrattle:</b> Summon a 1/1 Elemental.\"", "start_of_combat_grant")]
    [InlineData("<b>Start of Combat:</b> When you have space, summon an Ancestral Automaton.", "start_of_combat_grant")]
    [InlineData("Your Battlecries, Deathrattles, and Rallies trigger twice.", "extra_triggers")]
    [InlineData("After a friendly minion loses <b>Divine Shield</b>, get a random Tavern spell.", "divine_shield_lost")]
    public void The_audit_reads_combat_signs(string text, string sign) =>
        Assert.Contains(RelevanceAudit.TextSigns(text), s => s.StartsWith(sign + ":", StringComparison.Ordinal));

    [Theory]
    [InlineData("<b>Battlecry:</b> Get a Coin.")]
    [InlineData("<b>Start of Combat:</b> Gain +2/+2.")]
    [InlineData("<b>Taunt</b>. <b>Divine Shield</b>")]
    [InlineData("At the end of your turn, give your Beasts +1/+1.")]
    public void Tavern_and_start_of_combat_text_has_no_sign(string text) => Assert.Empty(RelevanceAudit.TextSigns(text));
}
