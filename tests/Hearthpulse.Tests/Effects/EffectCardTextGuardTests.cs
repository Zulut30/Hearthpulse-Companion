using System.Reflection;
using System.Text.Json;
using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;

namespace Hearthpulse.Tests.Sim;

/// <summary>
/// Guard against stale numbers in the combat effects. Battlegrounds balance changes on the server without a new client
/// build (Ship Master Eudora +6/+6 → +8/+8, Stalwart Kodo ×1 → ×2, The Last One Standing +15 → +10 and "improve this"
/// came that way), so each number an effect uses (its printed fallback when the minion carries no script data) is listed
/// here as the card text it comes from, and the test checks that text against the latest card data in the fixture
/// <c>Fixtures/Effects/effect-card-texts.json</c>. After a balance patch: regenerate the fixture with
/// <c>python3 -I tools/effects/gen_effect_texts.py &lt;cards.json&gt;</c> (HearthstoneJSON latest enUS), run this test, fix the
/// effects it names and their rows. Offline and deterministic: the test never reads the network.
/// </summary>
public sealed class EffectCardTextGuardTests
{
    /// <summary>
    /// The card text each effect in Effects/attack, deaths, ongoing and spells implements, with the numbers its code uses:
    /// the normal card and the golden card. Every registered card id of those folders has a row (a new effect adds one).
    /// Effects/player joins with package E3.
    /// </summary>
    private static readonly (string Id, string Normal, string? Golden)[] Rows =
    [
        // attack/
        ("BG20_104", "Rally: This plays a Blood Gem on all your other minions.", "Rally: This plays 2 Blood Gems on all your other minions."),
        ("BG25_016", "Rally: Remove Reborn and Taunt from the target.", "Rally: Remove Reborn and Taunt from the target."),
        ("BG26_817", "Also damages the enemies next to whomever this attacks.", "Also damages the enemies next to whomever this attacks."),
        ("BG27_017", "Deal damage equal to this minion's Attack to the target and an adjacent minion.", "Deal damage equal to this minion's Attack to the target and its neighbors."),
        ("BG29_816", "Whenever another friendly Dragon attacks, give it +3/+1.", "Whenever another friendly Dragon attacks, give it +6/+2."),
        ("BG29_888", "Rally: Gain +2 Attack.", "Rally: Gain +4 Attack."),
        ("BG33_318", "Rally: Give another friendly Murloc Venomous.", "Rally: Give 2 other friendly Murlocs Venomous."),
        ("BG33_430", "Whenever another friendly minion attacks, this plays a Blood Gem on it.", "Whenever another friendly minion attacks, this plays 2 Blood Gems on it."),
        ("BG33_883", "Rally: This plays 4 permanent Blood Gems on itself.", "Rally: This plays 8 permanent Blood Gems on itself."),
        ("BG33_886", "Rally: This plays a Blood Gem on itself.", "Rally: This plays 2 Blood Gems on itself."),
        ("BG34_140", "Attack Murloc from your hand for this combat only.", "Rally: Summon the 2 highest-Attack Murlocs from your hand for this combat only."),
        ("BG34_320", "Rally: Give a friendly minion of each type +10/+10 permanently and improve this.", "Rally: Give a friendly minion of each type +10/+10 permanently and improve this, twice."),
        ("BG34_604", "Rally: Gain the target's Attack.", "Rally: Gain double the target's Attack."),
        ("BG36_102", "After this attacks, deal damage equal to this minion's Attack to the highest- Health enemy minion.", "deal damage equal to double this minion's Attack to the highest- Health enemy minion."),
        ("BG36_200", "Rally: Summon a 1/1 Beast.", "Rally: Summon two 1/1 Beasts."),
        ("BG36_207", "Rally: Give your other minions +4/+1.", "Rally: Give your other minions +8/+2."),
        ("BG36_208", "After a friendly Rally minion attacks, trigger your left-most Deathrattle.", "After a friendly Rally minion attacks, trigger your left-most Deathrattle twice."),
        ("BG36_210", "Rally: Summon a Tasty Lobster.", "Rally: Summon a Golden Tasty Lobster."),
        ("BG36_211", "Whenever a friendly Beast attacks, give your Beasts +2/+1.", "Whenever a friendly Beast attacks, give your Beasts +4/+2."),
        ("BG36_241", "Rally: Cast Mighty Dragonbreath.", "Rally: Cast Mighty Dragonbreath twice."),
        ("BG36_246", "Give your minions +3/+2. Repeat for your Dragons. Repeat for your minions with Divine Shield.", null),
        ("BG36_333", "Rally: Summon a Golem with this minion's stats to attack the target first.", "Rally: Summon a Golem with double this minion's stats to attack the target first."),
        ("BG36_849", "Rally: Gain Divine Shield.", "Rally: Gain Divine Shield."),
        ("BG20_GEM", "Give a minion +1/+1.", null),
        ("BGS_126", "After this attacks and kills a minion, deal excess damage to an adjacent enemy.", "After this attacks and kills a minion, deal excess damage to both adjacent enemies."),
        ("BG33_885", "Rally: Your Blood Gems give an extra +1/+1 this game.", "Rally: Your Blood Gems give an extra +2/+2 this game."),
        ("BG27_513", "When this attacks and kills a minion, give its maximum stats to a minion in your hand.", "When this attacks and kills a minion, give double its maximum stats to a minion in your hand."),
        ("BG34_Giant_041", "Rally: Trigger your right-most Battlecry and Deathrattle.", "Rally: Trigger your right-most Battlecry and Deathrattle twice."),

        // deaths/
        ("BG19_010", "Deathrattle: Summon a 2/3 Turtle with Taunt.", "Deathrattle: Summon a 4/6 Turtle with Taunt."),
        ("BG23_318", "Deathrattle: Destroy the minion that killed this.", "Deathrattle: Destroy the minion that killed this."),
        ("BG25_010", "Deathrattle: Summon a 2/1 Hand with Reborn.", "Deathrattle: Summon two 2/1 Hands with Reborn."),
        ("BG25_022", "Deathrattle: Give a friendly Undead +1/+2.", "Deathrattle: Give a friendly Undead +2/+4."),
        ("BG25_354", "Your Deathrattles trigger an extra time.", "Your Deathrattles trigger 2 extra times."),
        ("BG26_350", "Deathrattle: Summon the highest-Health Murloc from your hand for this combat only.", "Deathrattle: Summon the 2 highest-Health Murlocs from your hand for this combat only."),
        ("BG28_300", "Deathrattle: Summon two 1/1 Skeletons.", "Deathrattle: Summon four 1/1 Skeletons."),
        ("BG28_309", "Deathrattle: Give a different friendly Undead Reborn.", "Deathrattle: Give 2 different friendly Undead Reborn."),
        ("BG29_611", "Deathrattle: Summon a 1/1 Microbot.", "Deathrattle: Summon a 2/2 Microbot."),
        ("BG30_125", "Deathrattle: Summon three 1/1 Skeletons.", "Deathrattle: Summon six 1/1 Skeletons."),
        ("BG31_801", "Deathrattle: Summon a 2/2 Beetle.", "Deathrattle: Summon two 2/2 Beetles."),
        ("BG31_803", "Deathrattle: Summon a 2/2 Beetle.", "Deathrattle: Summon two 2/2 Beetles."),
        ("BG31_809", "Deathrattle: Your Beetles have +5/+5 this game. Summon a 2/2 Beetle.", "Deathrattle: Your Beetles have +10/+10 this game. Summon two 2/2 Beetles."),
        ("BG31_925", "Deathrattle: Give all your Naga +2/+1.", "Deathrattle: Give all your Naga +4/+2."),
        ("BG31_999", "Destroy the minion to the left. Deathrattle: Summon an exact copy of it.", "Destroy adjacent minions. Deathrattle: Summon exact copies of them."),
        ("BG33_828", "Deathrattle: Give your minions +8/+8.", "Deathrattle: Give your minions +8/+8 twice."),
        ("BG34_690", "Deathrattle: Your Undead have +2 Attack this game", "Deathrattle: Your Undead have +4 Attack this game"),
        ("BG35_604", "Deathrattle: Summon two Sewer Rats that summon 2/3 Turtles with Taunt.", "Deathrattle: Summon two Golden Sewer Rats that summon 4/6 Turtles with Taunt."),
        ("BG36_202", "Deathrattle: Give a random friendly Beast +2/+1.", "Deathrattle: Give a random friendly Beast +4/+2."),
        ("BG36_209", "After a friendly minion attacks, your Beetles have +4/+4 this game. Deathrattle: Summon a 2/2 Beetle.", "After a friendly minion attacks, your Beetles have +8/+8 this game. Deathrattle: Summon two 2/2 Beetles."),
        ("BG36_116", "Deathrattle: Summon an 0/2 Tentacle with Taunt. Give your minions +1 Attack.", "Deathrattle: Summon two 0/2 Tentacles with Taunt. Give your minions +2 Attack."),
        ("BG36_MidGameEffect_000t12e", "Reborn. Is Reborn with full stats and Bonus Keywords.", null),
        ("BG36_MidGameEffect_000t2e2", "Deathrattle: Give this minion's max Health to another friendly minion.", null),
        ("BG36_MidGameEffect_000te2", "Deathrattle: Give this minion's Attack to another friendly minion.", null),
        ("BGS_008", "Deathrattle: Summon 2 random Deathrattle minions.", "Deathrattle: Summon 4 random Deathrattle minions."),
        ("BGS_018", "Deathrattle: Your Beasts have +7/+7 until next turn.", "Deathrattle: Your Beasts have +14/+14 until next turn."),
        ("BGS_040", "Deathrattle: Give 3 friendly Dragons Divine Shield.", "Deathrattle: Give 6 friendly Dragons Divine Shield."),
        ("BG23_017", "Battlecry and Deathrattle: Your Blood Gems give an extra +2/+1 this game.", "Battlecry and Deathrattle: Your Blood Gems give an extra +4/+2 this game."),
        ("BG36_205", "This can't gain stats. Deathrattle: Give the minion that killed this +5/+5.", "This can't gain stats. Deathrattle: Give the minion that killed this +10/+10."),

        // ongoing/
        ("BG25_008", "Has +4/+2 for each friendly Eternal Knight that died this game", "Has +8/+4 for each friendly Eternal Knight that died this game"),
        ("BG25_009", "Deathrattle: Summon 1 Eternal Knight.", "Deathrattle: Summon a Golden Eternal Knight."),
        ("BG26_802", "After you summon a Beast in combat, double its Attack.", "After you summon a Beast in combat, triple its Attack."),
        ("BG26_805", "For the rest of this combat, your Beasts have +1 Attack.", "For the rest of this combat, your Beasts have +2 Attack."),
        ("BG27_023", "Whenever this kills a minion, give a minion in your hand +3/+3.", "Whenever this kills a minion, give a minion in your hand +6/+6."),
        ("BG27_556", "When you have space, summon the highest-Attack Murloc from your hand for this combat only.", "When you have space, summon the two highest-Attack Murlocs from your hand for this combat only."),
        ("BG31_835", "Avenge (4): Get a random Undead. Deathrattle: Summon it from your hand for this combat only.", "Avenge (4): Get 2 random Undead. Deathrattle: Summon them from your hand for this combat only."),
        ("BG33_155", "After another friendly Demon deals damage, gain +2/+2 permanently.", "After another friendly Demon deals damage, gain +4/+4 permanently."),
        ("BG34_322", "give it double this minion's maximum stats. (3 times per combat.)", "give it triple this minion's maximum stats. (3 times per combat.)"),
        ("BG34_403", "Avenge (5): Summon an Eternal Knight. It attacks immediately.", "Avenge (5): Summon a Golden Eternal Knight. It attacks immediately."),
        ("BG34_405", "Has Taunt while this has Divine Shield. Avenge (3): Gain Divine Shield.", "Has Taunt while this has Divine Shield. Avenge (3): Gain Divine Shield."),
        ("BG35_342", "Has +4/+2 for each Deathrattle you've triggered this game", "Has +8/+4 for each Deathrattle you've triggered this game"),
        ("BG35_602", "Whenever you summon a Beast, give it +3 Attack and improve this permanently.", "Whenever you summon a Beast, give it +6 Attack and improve this permanently."),
        ("BG35_814", "Once this reaches 6 Attack, gain Divine Shield.", "Once this reaches 6 Attack, gain Divine Shield."),
        ("BG36_113", "Deathrattle: Give your Deity +2/+1.", "Deathrattle: Give your Deity +4/+2."),
        ("BG36_318", "Deathrattle: Give your Deity +2/+1.", "Deathrattle: Give your Deity +4/+2."),
        ("BG36_360t4", "This minion's Divine Shield takes 3 hits to break.", "This minion's Divine Shield takes 4 hits to break."),
        ("BG36_360_Gt4", "This minion's Divine Shield takes 4 hits to break.", null),
        ("BG36_360t9", "Deathrattle: Summon a Golem with this minion's stats.", "Deathrattle: Summon a Golem with double this minion's stats."),
        ("BG36_360_Gt9", "Deathrattle: Summon a Golem with double this minion's stats.", null),
        ("BG36_514", "After a friendly minion is Reborn, gain Divine Shield and +8/+8.", "After a friendly minion is Reborn, gain Divine Shield and +16/+16."),
        ("BG36_515", "After a friendly minion is Reborn, give stats equal to its Attack to your right-most Undead.", "After a friendly minion is Reborn, give stats equal to double its Attack to your right-most Undead."),
        ("BG36_MidGameEffect_000t15e", "This minion's Divine Shield takes 3 hits to break.", null),
        ("BG36_MidGameEffect_000t60e", "Immune while attacking.", null),
        ("BG36_MidGameEffect_000t61e", "Deathrattle: Summon a Golem with this minion's stats.", null),
        ("BGDUO31_208", "Has +4/+4 for each of your team's San'layn Scribes that died this game", "Has +8/+8 for each of your team's San'layn Scribes that died this game"),
        ("BGDUO_701", "Battlecry, Deathrattle, and Rally: Give your team's Deities +4/+4.", "Battlecry, Deathrattle, and Rally: Give your team's Deities +8/+8."),
        ("BGFYM_000", "After this awakens, give this minion's stats split amongst your other minions.", "After this awakens, give double this minion's stats split amongst your other minions."),
        ("BGFYM_011", "Deathrattle: Summon your first 2 Aberrations that died this combat with their maximum stats (except Deities).", "Deathrattle: Summon your first 4 Aberrations that died this combat with their maximum stats (except Deities)."),
        ("BG35_883", "Your spells that target friendly minions cast twice.", "Your spells that target friendly minions cast three times."),
        ("BG_LOE_077", "Your Battlecries trigger twice.", "Your Battlecries trigger three times."),
        ("BGS_041", "After you trigger a Battlecry, give your Dragons +2/+2.", "After you trigger a Battlecry, give your Dragons +4/+4."),
        ("TB_BaconShop_HERO_33_Buddy", "Whenever your Amalgam gains stats, this gains them too.", "Whenever your Amalgam gains stats, this gains them twice."),
        ("BG25_013", "Has +1 Attack for each friendly minion that died this combat.", "Has +2 Attack for each friendly minion that died this combat."),
        ("BG36_110", "Battlecry: Give your Deity +2/+1.", "Battlecry: Give your Deity +4/+2."),
        ("BG31_148", "Battlecry: Give your other minions +3/+2.", "Battlecry: Give your other minions +6/+4."),
        ("BG25_011", "Battlecry: Your Undead have +1 Attack this game", "Battlecry: Your Undead have +2 Attack this game"),
        ("BG26_963", "Battlecry and Start of Combat: Give your other Dragons +1/+1.", "Battlecry and Start of Combat: Give your other Dragons +2/+2."),
        ("BG36_360", "This has a different Dark Gift, stats, and Tier each game!", "This has a different Dark Gift, stats, and Tier each game!"),
        ("BG36_360t6", "Has +4/+4 for each Battlecry you've triggered this game", "Has +8/+8 for each Battlecry you've triggered this game"),
        ("BG36_360_Gt6", "Has +8/+8 for each Battlecry you've triggered this game", null),

        // spells/ (and the Tavern spells cast in combat)
        ("BG35_921", "Has +2/+1 for each Tavern spell you've cast this game.", "Has +4/+2 for each Tavern spell you've cast this game."),
        ("BG32_111", "Battlecry and Deathrattle: Get a Misplaced Tea Set.", "Battlecry and Deathrattle: Get 2 Misplaced Tea Sets."),
        ("BG32_336", "Battlecry and Deathrattle: Get a Seafood Stew.", "Battlecry and Deathrattle: Get 2 Seafood Stews."),
        ("BG32_820", "Battlecry and Deathrattle: Get a Shiny Ring.", "Battlecry and Deathrattle: Get 2 Shiny Rings."),
        ("BG33_821", "Battlecry and Deathrattle: Get a random Bounty.", "Battlecry and Deathrattle: Get 2 random Bounties."),
        ("BG34_171", "Deathrattle: Get a random Magnetic Volumizer.", "Deathrattle: Get 2 random Magnetic Volumizers."),
        ("BG34_633", "Battlecry and Deathrattle: Get a random Chromadrake.", "Battlecry and Deathrattle: Get 2 random Chromadrakes."),
        ("BG34_682", "Battlecry and Deathrattle: Get a Blood Gem Barrage.", "Battlecry and Deathrattle: Get 2 Blood Gem Barrages."),
        ("BG35_881", "Battlecry and Deathrattle: Get an Arcane Absorption.", "Battlecry and Deathrattle: Get 2 Arcane Absorptions."),
        ("BG36_101", "Deathrattle: Get a random 1-Cost Tavern spell.", "Deathrattle: Get two random 1-Cost Tavern spells."),
        ("BG36_103", "Battlecry and Deathrattle: Get an Energizing Chamber.", "Battlecry and Deathrattle: Get 2 Energizing Chambers."),
        ("BG36_731", "Deathrattle: Get a Methodical Madness.", "Deathrattle: Get 2 copies of Methodical Madness."),
        ("BG36_854", "Deathrattle: Get a Repair Job.", "Deathrattle: Get 2 Repair Jobs."),
        ("BGDUO_111", "Deathrattle: You and your teammate each get a Blood Gem.", "Deathrattle: You and your teammate each get 2 Blood Gems."),
        ("BG20_101", "Rally: Get a Blood Gem.", "Rally: Get 2 Blood Gems."),
        ("BG33_822", "Rally: Get a random Bounty.", "Rally: Get 2 random Bounties."),
        ("BG36_204", "Rally: Get a random Beast.", "Rally: Get 2 random Beasts."),
        ("BG36_242", "Rally: Get a random Chromadrake.", "Rally: Get 2 random Chromadrakes."),
        ("BG36_331", "Rally: Get a random Choose One card.", "Rally: Get 2 random Choose One cards."),
        ("BG34_319", "Battlecry, Deathrattle, and Rally: Get a random Tier 6 minion.", "Battlecry, Deathrattle, and Rally: Get two random Tier 6 minions."),
        ("BG32_324", "Avenge (4): Get a Butchering.", "Avenge (4): Get 2 Butcherings."),
        ("BG36_848", "Avenge (3): Get a plain copy of a minion that started in your warband this combat.", "Avenge (3): Get 2 plain copies of a minion that started in your warband this combat."),
        ("BG28_741", "Whenever you cast a Tavern spell, give your minions with Divine Shield +4 Attack.", "Whenever you cast a Tavern spell, give your minions with Divine Shield +8 Attack."),
        ("BG36_921", "Whenever you cast a spell on this, gain +1 Health.", "Whenever you cast a spell on this, gain +2 Health."),
        ("BG34_692", "After you cast a Tavern spell, your Undead have +3 Attack this game", "After you cast a Tavern spell, your Undead have +6 Attack this game"),
        ("BG36_640", "Whenever you cast a spell on this, it casts Misplaced Tea Set.", "Whenever you cast a spell on this, it casts Misplaced Tea Set twice."),
        ("BG28_583", "Whenever a Blood Gem is played on this, this plays a Blood Gem on a different friendly minion.", "Whenever a Blood Gem is played on this, this plays 2 Blood Gems on a different friendly minion."),
        ("BG36_853", "Whenever you cast a spell on a Mech, Magnetize a 5/5 Satellite to it.", "Whenever you cast a spell on a Mech, Magnetize a 5/5 Satellite to it twice."),
        ("BG34_925", "Rally: Cast Chef's Choice on the minion to the right.", "Rally: Cast Chef's Choice on the minion to the right twice."),
        ("BG36_111", "Whenever you cast a Tavern spell, give your minions and Deity +3/+3.", "Whenever you cast a Tavern spell, give your minions and Deity +6/+6."),
        ("BG36_704", "Whenever you cast a spell on a Murloc, give Murlocs in your hand and board +3/+4.", "Whenever you cast a spell on a Murloc, give Murlocs in your hand and board +6/+8."),
        ("BG36_MagicItem_211", "After you cast a spell on a friendly minion, this casts it on another friendly minion.", null),
        ("BG36_MagicItem_404", "After you cast a Tavern spell, give your Deity +4/+4.", null),
        ("BG36_MagicItem_404t", "After you cast a Tavern spell, give your Deity +10/+10.", null),
        ("BG35_MagicItem_710", "After you cast a Tavern spell, give your Pirates +4/+4.", null),
        ("BG30_MagicItem_547", "Whenever you cast a Tavern spell, your Undead have +1 Attack this game", null),
        ("BG30_MagicItem_547t", "Whenever you cast a Tavern spell, your Undead have +2 Attack this game", null),
        ("BG32_MagicItem_893", "Whenever you cast a Tavern spell, give the left-most minion in your hand and warband +3/+3.", null),
        ("BG36_MagicItem_820", "After you cast a Tavern spell, give Flighty Scouts in your hand and warband +3/+3.", null),
        ("BG36_109", "Whenever a card is added to your hand, give your Deity +4/+5.", "Whenever a card is added to your hand, give your Deity +8/+10."),
        ("BG27_005", "Whenever you cast a Tavern spell, give your minions +1 Attack.", "Whenever you cast a Tavern spell, give your minions +1 Attack twice."),
        ("BG36_622", "Whenever you cast a spell on a Naga, give your minions +2/+3.", "Whenever you cast a spell on a Naga, give your minions +4/+6."),
        ("BG36_703", "Whenever you cast a spell on this, give the left-most minion in your hand +9/+9.", "Whenever you cast a spell on this, give the left-most minion in your hand +18/+18."),
        ("BG36_108", "Whenever you cast a Tavern spell, give this and your Deity +1/+2.", "Whenever you cast a Tavern spell, give this and your Deity +2/+4."),
        ("BG28_888", "Give a friendly minion of each type +3/+3.", null),
        ("BG28_518", "Get a different minion of the same type.", null),
    ];

    /// <summary>Tokens the effects summon with printed stats from <see cref="Tokens"/>: card id and factory.</summary>
    private static readonly (string Id, Func<bool, SimMinion> Make)[] TokenStats =
    [
        (Tokens.BeetleId, golden => Tokens.Beetle(golden)),
        (Tokens.SkeletonId, golden => Tokens.Skeleton(golden)),
        (Tokens.MicrobotId, golden => Tokens.Microbot(golden)),
        (Tokens.HelpingHandId, golden => Tokens.HelpingHand(golden)),
        (Tokens.AberrantTentacleId, golden => Tokens.AberrantTentacle(golden)),
        (Tokens.ForagingBatId, golden => Tokens.ForagingBat(golden)),
        (Tokens.SewerRatId, golden => Tokens.SewerRat(golden)),
        (Tokens.HalfShellId, golden => Tokens.HalfShell(golden)),
        (Tokens.EternalKnightId, golden => Tokens.EternalKnight(golden)),
        (Tokens.TastyLobsterId, golden => Tokens.TastyLobster(golden)),
        (Tokens.AmalgamId, golden => Tokens.Amalgam(golden)),
    ];

    private static readonly string[] EffectNamespaces =
    [
        "Hearthpulse.Core.Battlegrounds.Effects.Attack",
        "Hearthpulse.Core.Battlegrounds.Effects.Deaths",
        "Hearthpulse.Core.Battlegrounds.Effects.Ongoing",
        "Hearthpulse.Core.Battlegrounds.Effects.Spells",
    ];

    private static readonly Lazy<Dictionary<string, CardText>> Cards = new(Load);

    [Fact]
    public void Every_effect_number_still_appears_in_the_latest_card_text()
    {
        var stale = new List<string>();
        foreach (var (id, normal, golden) in Rows)
        {
            if (!Cards.Value.TryGetValue(id, out var card))
            {
                stale.Add($"{id}: not in the fixture (regenerate it with tools/effects/gen_effect_texts.py)");
                continue;
            }

            if (!card.Text.Contains(normal, StringComparison.Ordinal))
            {
                stale.Add($"{id} {card.Name}: expected \"{normal}\", card says \"{card.Text}\"");
            }

            if (golden is null)
            {
                continue;
            }

            if (card.Golden is not { } goldenCard)
            {
                stale.Add($"{id} {card.Name}: no golden card in the fixture");
            }
            else if (!goldenCard.Text.Contains(golden, StringComparison.Ordinal))
            {
                stale.Add($"{id} {card.Name} (golden): expected \"{golden}\", card says \"{goldenCard.Text}\"");
            }
        }

        Assert.Empty(stale);
    }

    [Fact]
    public void Summoned_tokens_have_the_latest_printed_stats()
    {
        var stale = new List<string>();
        foreach (var (id, make) in TokenStats)
        {
            var card = Cards.Value[id];
            Compare(stale, $"{id} {card.Name}", make(false), card.Attack, card.Health);
            if (card.Golden is { } golden)
            {
                Compare(stale, $"{id} {card.Name} (golden)", make(true), golden.Attack, golden.Health);
            }
        }

        Assert.Empty(stale);
    }

    [Fact]
    public void Every_effect_of_the_minion_folders_has_a_row()
    {
        var registered = typeof(CombatEffect).Assembly.GetTypes()
            .Where(type => type.Namespace is { } ns && EffectNamespaces.Contains(ns, StringComparer.Ordinal))
            .SelectMany(type => type.GetCustomAttributes<CardEffectAttribute>())
            .Select(attribute => attribute.CardId)
            .ToHashSet(StringComparer.Ordinal);
        var rows = Rows.Select(row => row.Id).ToHashSet(StringComparer.Ordinal);

        Assert.Empty(registered.Except(rows).Order(StringComparer.Ordinal));
        Assert.Equal(Rows.Length, rows.Count);
    }

    private static void Compare(List<string> stale, string label, SimMinion token, int? attack, int? health)
    {
        if (token.Attack != attack || token.Health != health)
        {
            stale.Add($"{label}: code {token.Attack}/{token.Health}, card {attack}/{health}");
        }
    }

    private sealed record CardText(string Name, string Text, int? Attack, int? Health, CardText? Golden);

    private static Dictionary<string, CardText> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Effects", "effect-card-texts.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var cards = new Dictionary<string, CardText>(StringComparer.Ordinal);
        foreach (var card in document.RootElement.GetProperty("cards").EnumerateObject())
        {
            var golden = card.Value.TryGetProperty("golden", out var g) ? Read(g, string.Empty, null) : null;
            cards[card.Name] = Read(card.Value, card.Value.GetProperty("name").GetString() ?? string.Empty, golden);
        }

        return cards;
    }

    private static CardText Read(JsonElement element, string name, CardText? golden) =>
        new(name, element.GetProperty("text").GetString() ?? string.Empty, Number(element, "attack"), Number(element, "health"), golden);

    private static int? Number(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;
}
