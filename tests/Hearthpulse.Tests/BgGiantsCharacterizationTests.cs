using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Pool;
using Hearthpulse.Core.Power;
using Hearthpulse.Core.Replay;
using Hearthpulse.Core.Tracking;
using Xunit.Abstractions;

namespace Hearthpulse.Tests;

/// <summary>
/// Characterization of the Battlegrounds giants split in Etalon Q4 (`Fight`, `BattlegroundsTracker`,
/// `BattlegroundsReplayRecorder`): fingerprints of everything they produce on fixed inputs, taken before the split. A
/// move of code that changes behaviour changes a fingerprint. `PoolClient` is pinned by `PoolTests`' exact values.
/// </summary>
public sealed class BgGiantsCharacterizationTests(ITestOutputHelper output)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false, IncludeFields = true };

    private static string Fingerprint(StringBuilder text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..16];

    private static CardDb FixtureCards()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "BattlegroundsGame");
        using var gz = new GZipStream(File.OpenRead(Path.Combine(folder, "cards.json.gz")), CompressionMode.Decompress);
        return CardDb.Parse("ruRU", gz);
    }

    [Fact]
    public void The_tracker_and_the_replay_recorder_produce_what_they_did_before_the_split()
    {
        var cards = FixtureCards();
        var parser = new PowerParser(new DateOnly(2026, 10, 23));
        var tracker = new BattlegroundsTracker { Cards = cards };
        var recorder = new ReplayRecorder();
        parser.AddListener(tracker);
        parser.AddListener(recorder);
        var views = new StringBuilder();
        foreach (var line in BattlegroundsFixtureTests.Lines())
        {
            parser.Feed(line);
            if (line.Contains("BLOCK_END", StringComparison.Ordinal) && parser.Game is { IsBattlegrounds: true, LocalPlayerId: > 0 } game)
            {
                parser.Flush();
                // Combat odds are Monte Carlo on the thread pool: pinned by the simulator's own fingerprint instead.
                var view = tracker.BuildView(game);
                view = view with { CombatOdds = null, Combats = [.. view.Combats.Select(c => c with { Odds = null })] };
                views.Append(JsonSerializer.Serialize(view, Json)).Append('\n');
            }
        }

        parser.Flush();
        var replay = new StringBuilder(recorder.Battlegrounds.Text(new ReplayHeader(), tracker.ReplayData(parser.Game!)));
        var data = new StringBuilder(JsonSerializer.Serialize(tracker.ReplayData(parser.Game!), Json));

        var actual = $"views {Fingerprint(views)} replay {Fingerprint(replay)} data {Fingerprint(data)}";
        output.WriteLine(actual);
        // 2026-11-19 (odds estimate mark): every view carries BattlegroundsView.CombatEstimates (empty throughout this game);
        // without that property the views hash is still 353E1634374395D9.
        Assert.Equal("views 911DF8F2524AB527 replay 3DB931AD881A2307 data 2C492810FA8A5101", actual);
    }

    /// <summary>The pool's totals with missing numbers, malformed rows and brackets (review m10).</summary>
    [Fact]
    public void The_pool_reads_its_totals_as_it_did_before_the_split()
    {
        using var doc = JsonDocument.Parse("""
            { "games": 140, "updated": "2026-10-23T10:00:00.000Z", "heroes": [
              { "hero": "BG_HERO_1", "games": 120, "avgPlacement": 3.9, "top": 0.55, "first": 0.15, "offered": 200, "picked": 100 },
              { "hero": "BG_HERO_3", "games": 60 },
              { "hero": "BG_HERO_2", "games": 19, "avgPlacement": 2.0, "top": 0.9, "first": 0.5, "offered": 30, "picked": 19 },
              { "hero": 5, "games": 500 } ],
              "cards": [ { "card": "BGS_004", "games": 80 }, { "card": "BGS_039", "games": 300, "avgPlacement": 4.2 } ],
              "trinkets": [ { "trinket": "BG30_MagicItem_442", "games": 70 } ],
              "tribes": [ { "tribe": "BEAST", "games": 150 } ] }
            """);
        var meta = PoolClient.Parse(doc.RootElement);
        var actual = $"pool {Fingerprint(new StringBuilder(JsonSerializer.Serialize(meta, Json)))}";
        output.WriteLine(actual);
        // 2026-11-15: the meta gained the public HeroComps (empty for the pool); without that property the text still
        // hashes to the fingerprint of before, D9E8D2E649E91FDA.
        Assert.Equal("pool 3F718920D0D5A1D8", actual);
    }

    [Fact]
    public void The_simulator_fights_as_it_did_before_the_split()
    {
        var registry = EffectRegistry.Default;
        var ids = registry.NormalCardIds.Order(StringComparer.Ordinal).ToArray();
        var keywords = new[] { Keyword.None, Keyword.Taunt, Keyword.DivineShield, Keyword.Reborn, Keyword.Windfury, Keyword.Venomous, Keyword.Poisonous };
        var tribes = new[] { Tribe.None, Tribe.Beast, Tribe.Demon, Tribe.Dragon, Tribe.Elemental, Tribe.Mech, Tribe.Murloc, Tribe.Naga, Tribe.Pirate, Tribe.Quilboar, Tribe.Undead };
        var text = new StringBuilder();
        for (var i = 0; i < 400; i++)
        {
            var random = new Random(i * 7919 + 17);
            SimSide Board() => new(Enumerable.Range(0, random.Next(1, 8)).Select(_ => SimMinion.Create(
                ids[random.Next(ids.Length)], random.Next(1, 30), random.Next(1, 30), tribes[random.Next(tribes.Length)],
                keywords[random.Next(keywords.Length)], random.Next(5) == 0)).ToArray());
            var ours = Board();
            var theirs = Board();
            var result = CombatSimulator.RunSingle(ours, theirs, random.Next(2) == 0, new Random(i), registry, trace: true);
            text.Append(CultureInfo.InvariantCulture, $"{i} {result.Outcome} ")
                .Append(string.Join(',', result.Ours.Select(m => $"{m.CardId}:{m.Attack}/{m.Health}")))
                .Append(" | ")
                .Append(string.Join(',', result.Theirs.Select(m => $"{m.CardId}:{m.Attack}/{m.Health}")))
                .Append('\n')
                .AppendJoin('\n', result.Log)
                .Append('\n');
        }

        var actual = $"fights {Fingerprint(text)}";
        output.WriteLine(actual);
        // 2026-11-09 (S1): four new effect ids change the random boards; without them the fingerprint is still 6C3E80D601C5A873.
        // 2026-11-15 (S2): Murcules and Bassgill add two ids; without them (checked) the fingerprint is still 4B6145B26A89595C.
        // 2026-11-19 (sim-minions): sixteen new effect ids (Sanguine Champion/Refiner, Fishbait, Tide Oracle Morgl, Brann,
        // Balinda, Timewarped Greenskeeper, Kalecgos, Dark Paradox and its t6 form, Rot Hide Gnoll, Mishmash, Joyous,
        // Hackerfin, Nerubian Deathswarmer, Electric Synthesizer); without them (checked) the fingerprint is still
        // FECFFCB0BDD005AE.
        // 2026-11-19 (sim-all-mechanics P0): Ghastcoiler and Undead pools regenerated from patch 37.0.2 card data (new
        // stats, Rot Hide Gnoll); with the old pool files (checked) the fingerprint is still A488D069E48228A5.
        // 2026-11-19 (E1 sim-spells): the spell, card-to-hand and spell-trinket effects add ids and two new trace lines
        // ("... gets a Blood Gem", "we/they get ..."); without those types and lines (checked) it is still 60958721CA247FFC.
        // 2026-11-19 (sim-all-mechanics E4): 29 new effect ids (Death's Embrace, Turbulent Tombs, Gilnean War Horn,
        // Temporal Tampering, Echoes of Argus, 23 Timewarped minions); without them (checked) the fingerprint is still
        // 30FC3B58C163F3F4.
        // 2026-11-19 (P1, decisions through ICombatChooser): Best's tie-break no longer draws for a tie a higher value later
        // beats. Such ties occur on these boards: with those draws put back (checked) the fingerprint is still E4's
        // D257E5976B8595DA. The picks of E1 (Lichling Hoarder, Lovely Locket, Misplaced Tea Set) and E4 (Timewarped Tide
        // Razor, Warghoul, Chimera, Scourfin) moved to the chooser draw as before.
        // 2026-11-19 (E3): twelve new player-card ids change the random boards, and player cards changed on purpose (as a
        // minion without a capture Broodmother and Tentacular now summon their printed Whelp/Tentacle, Fang Anklet gives
        // its printed +1/+1, golden Beetle Band is Avenge (8) with the player's Beetle); not split up and checked one by one.
        // 2026-11-19 (FIX sim-effects-audit, merged after E3): effects corrected to the live card text (The Last One
        // Standing, Eudora, Kodo, C'Thun, Diremuck Forager, Hoarding Hyena, Nadina) change these fights on purpose.
        // 2026-11-19 (sim-rules): engine rules from real Power.log fights change these fights on purpose (rules-evidence
        // 2b, 4a, 4d, 4e; effects-audit E1, E2: a dead attacker's summons attack next, deaths in order of play, Reborn
        // after Avenge, reborn max Health, friendly-attack hooks before Rally, deathrattles A,A,B,B; simreplay: Diremuck
        // Forager's pending Murloc at the right end after Reborn); the boards are the
        // same, and before those rules (main at 21a1bb33) the fingerprint is B38EEFBF3604898A.
        // 2026-11-19 (E5 sim-leftovers): 21 new effect ids (Bubble Gunner, Captain Sanders, Elite Navigator, Disguised
        // Graverobber, Maw Caster, Locked-up Mutineer, Dark Paradox t5, Nellie's Ship, Boon of Beetles, three spell trinkets,
        // Rallying Cry, seven quests) change the boards and, through the Pirate/Undead pools, the summons; with a registry
        // without those classes (checked) the fingerprint is still 686C34B3497F4560 (sim-rules).
        // 2026-11-19 (ACC sim-replay-residuals): Very Hungry Winterfinner and golden Tarecgosa get effects (real fights
        // #2/9, #2/12, #5/8 of the 15.11 log); before them (main at ef122f83) the fingerprint is 5887EA944D5F7F33.
        // 2026-11-20 (E6a sim-rotation-minions-quests): 20 new effect ids (12 Timewarped minions, Exhume the Bones, Witness
        // Protection, six quest rewards) change the boards; with a registry without those classes (checked) the fingerprint is
        // still 489D3D39263EF5AB.
        Assert.Equal("fights E6CE70F6677465C0", actual);
    }
}
