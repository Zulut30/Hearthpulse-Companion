using Hearthpulse.Core.Battlegrounds.Effects.Spells;
using Hearthpulse.Core.Battlegrounds.Effects.Timewarped;

namespace Hearthpulse.Core.Battlegrounds.Effects.Player;

// Player-level cards of today's pool and the enchantments they give, which the relevance audit
// (tests/Hearthpulse.Tests/Sim/RelevanceAuditTests.cs, 2026-11-19) found acting in combat while the matrix called them
// "no effect" or "resolved". Numbers from the latest card text (HearthstoneJSON enUS, build 256589) unless a captured tag
// is named; none of these cards is in a game of the owner's logs (D:\Hearthstone\Logs, 2026-11-19).

/// <summary>
/// Artisanal Urn, golden (BG30_MagicItem_989t): "Your Undead have +20 Attack." The captured Undead show the aura; Undead
/// summoned in combat get it too (as Feral Talisman). The number: TAG_SCRIPT_DATA_NUM_1 when captured (<see cref="GrowingValue"/>).
/// </summary>
[CardEffect("BG30_MagicItem_989t")]
internal sealed class ArtisanalUrn : CombatEffect
{
    public override void OnFightStart(Fight fight, Unit self) =>
        self.Side.AddSummonBuff(Tribe.Undead, GrowingValue.Current(self.Side.Info, self.CardId, 20, 0).Attack, 0);
}

/// <summary>
/// Automaton Portrait (BG30_MagicItem_303): "Start of Combat: When you have space, summon an Ancestral Automaton."
/// </summary>
/// <remarks>
/// With space at Start of Combat the Automaton is on the captured board (CREATOR = the trinket) and nothing is owed. With a
/// full board and no such Automaton, the first friendly death that leaves room summons it at the right end, once. The
/// Ancestral Automaton (BG_TTN_401, 3/4 Mech): "Has +3/+2 for each other Ancestral Automaton you've summoned this game".
/// The count: the player enchantment "Ancestral Automaton Player Enchant" (BG_TTN_401pe) NUM_1 when captured (assumption:
/// no log shows it); without it the printed 3/4 (the opponent's player enchantments are not shown).
/// </remarks>
[CardEffect("BG30_MagicItem_303")]
internal sealed class AutomatonPortrait : CombatEffect
{
    public const string AutomatonId = "BG_TTN_401";
    public const string CountEnchantment = "BG_TTN_401pe";

    public override string? CapturedInput => "число Ancestral Automaton этой игры (зачарование игрока BG_TTN_401pe, NUM_1)";

    public override void OnFightStart(Fight fight, Unit self) =>
        self.Counter = self.Side.IsFull && !PlayerCards.AnyCreatedBy(self.Side.Info, self.CardId) ? 1 : 0;

    public override void AfterFriendlyDeath(Fight fight, Unit self, Unit dead)
    {
        if (self.Counter == 0 || self.Side.IsFull)
        {
            return;
        }

        self.Counter = 0;
        var others = Math.Max(0, self.Side.Info.PlayerEnchantment(CountEnchantment)?.ScriptData1 ?? 0);
        fight.Summon(self.Side, Tokens.Make(AutomatonId, 3 + 3 * others, 4 + 2 * others, Tribe.Mech) with { CreatorCardId = self.CardId }, self.Side.Count);
    }
}

/// <summary>Wicked Tome, golden (BG32_MagicItem_270t): "Avenge (4): Your Tavern spells give an extra +1/+1 this game."</summary>
[CardEffect("BG32_MagicItem_270t")]
internal sealed class WickedTome : PlayerAvengeEffect
{
    protected override int PrintedThreshold(Unit self) => 4;

    protected override void Avenge(Fight fight, Unit self)
    {
        self.Side.AddCounter(TavernSpells.SpellAttackTag, 1);
        self.Side.AddCounter(TavernSpells.SpellHealthTag, 1);
    }
}

/// <summary>
/// Quilligraphy Set (BG30_MagicItem_410): "Avenge (3): Your Blood Gems give an extra +1 Health this game." Golden
/// (…410t2): "Avenge (4): … +1/+1". The better gems count for the Blood Gems played later in the fight.
/// </summary>
[CardEffect("BG30_MagicItem_410")]
[CardEffect("BG30_MagicItem_410t2")]
internal sealed class QuilligraphySet : PlayerAvengeEffect
{
    private static bool Greater(Unit self) => self.CardId.EndsWith("t2", StringComparison.Ordinal);

    protected override int PrintedThreshold(Unit self) => Greater(self) ? 4 : 3;

    protected override void Avenge(Fight fight, Unit self) => TimewarpedKit.ImproveBloodGems(self.Side, Greater(self) ? 1 : 0, 1);
}

/// <summary>
/// Avenge (3) of a trinket or quest reward that gets a random card: Fridge Magnet (BG30_MagicItem_545, a Magnetic
/// minion), Cycle of Energy (BG28_Reward_504, a Tavern spell). One card for "whenever a card is added to your hand".
/// </summary>
[CardEffect("BG30_MagicItem_545")]
[CardEffect("BG28_Reward_504")]
internal sealed class AvengeGetCard : PlayerAvengeEffect
{
    protected override int PrintedThreshold(Unit self) => 3;

    protected override void Avenge(Fight fight, Unit self) => fight.AddToHand(self.Side, CardToHand.AnyCard);
}

/// <summary>The Eye of Dalaran (BG30_MagicItem_981): "Whenever a friendly minion with no type dies, get a random Tavern spell."</summary>
[CardEffect("BG30_MagicItem_981")]
internal sealed class TheEyeOfDalaran : CombatEffect
{
    public override void AfterFriendlyDeath(Fight fight, Unit self, Unit dead)
    {
        if (dead.Tribes == Tribe.None)
        {
            fight.AddToHand(self.Side, CardToHand.AnyCard);
        }
    }
}

/// <summary>
/// Funeral Wreath (BG36_MagicItem_217): "After a friendly minion is Reborn, get a plain copy of it. (3 times per combat.)"
/// Uses left: <see cref="PlayerCards.UsesLeft"/>.
/// </summary>
[CardEffect("BG36_MagicItem_217")]
internal sealed class FuneralWreath : CombatEffect
{
    public override void OnFightStart(Fight fight, Unit self) => self.Counter = PlayerCards.UsesLeft(self.Side.Info, self.CardId, 3);

    public override void AfterFriendlyReborn(Fight fight, Unit self, Unit reborn)
    {
        if (self.Counter > 0)
        {
            self.Counter--;
            fight.AddToHand(self.Side, reborn.CardId);
        }
    }
}

// ---- Enchantments a hero power or trinket gives at Start of Combat, whose Deathrattle acts later in the fight. Before
// these effects the granted DEATHRATTLE marker made the fight "not simulated" (CombatRelevance.IsUnsimulated); the matrix
// called the cards "resolved".

/// <summary>Element: Earth (BG22_HERO_001p_t1e, Al'Akir's Earth Invocation): "Deathrattle: Summon a 1/1 Elemental."</summary>
[CardEffect("BG22_HERO_001p_t1e")]
internal sealed class ElementEarth : CombatEffect
{
    public const string StoneElementalId = "BG22_HERO_001p_t1et";

    public override void Deathrattle(Fight fight, Unit self) => fight.SummonAt(self, Tokens.Make(StoneElementalId, 1, 1, Tribe.Elemental));
}

/// <summary>
/// Frosty Globe (BG30_MagicItem_952e, Jarred Frostling): "Deathrattle: Summon a Flourishing Frostling." The Frostling
/// (BG26_537, 2/1 Elemental): "Has +2/+1 for each Elemental you played this game". The count: the player enchantment
/// BG26_537pe NUM_1 when captured (assumption, no log); without it the printed 2/1.
/// </summary>
[CardEffect("BG30_MagicItem_952e")]
internal sealed class FrostyGlobe : CombatEffect
{
    public const string FrostlingId = "BG26_537";
    public const string CountEnchantment = "BG26_537pe";

    public override string? CapturedInput => "Elementals разыграно за игру (зачарование игрока BG26_537pe, NUM_1)";

    public override void Deathrattle(Fight fight, Unit self)
    {
        var played = Math.Max(0, self.Side.Info.PlayerEnchantment(CountEnchantment)?.ScriptData1 ?? 0);
        fight.SummonAt(self, Tokens.Make(FrostlingId, 2 + 2 * played, 1 + played, Tribe.Elemental));
    }
}

/// <summary>Skyward (BG35_MagicItem_740e2, Sky Golem Portrait): "Deathrattle: Give your minions +3/+3 permanently."</summary>
[CardEffect("BG35_MagicItem_740e2")]
internal sealed class Skyward : CombatEffect
{
    public override void Deathrattle(Fight fight, Unit self) => TimewarpedKit.BuffAll(fight, self.Side, Tribe.All, 3, 3);
}

/// <summary>
/// Fragrant (BG20_HERO_282pe, Tamsin's Fragrant Phylactery): "Start of Combat: Give your lowest-Attack minion
/// "Deathrattle: Give your other minions this minion's stats."" The stats are its Attack and maximum Health when it dies.
/// Assumption: the Deathrattle sits on this enchantment (its own text is "+0/+0"; no log shows the power); if it sits on
/// another one, the granted marker stays unexplained and the fight is flagged.
/// </summary>
[CardEffect("BG20_HERO_282pe")]
internal sealed class Fragrant : CombatEffect
{
    public override void Deathrattle(Fight fight, Unit self)
    {
        var (attack, health) = (self.Attack, Math.Max(0, self.MaxHealth));
        TimewarpedKit.BuffAll(fight, self.Side, Tribe.All, attack, health);
    }
}

/// <summary>
/// Granted "Get" Deathrattles and Rallies, each card counting for "whenever a card is added to your hand": one class per
/// enchantment, so a minion holding two of them gets each once.
/// </summary>
internal abstract class GrantedGet : CombatEffect
{
    protected abstract string Card { get; }

    protected virtual int Count => 1;

    protected void Get(Fight fight, Unit self)
    {
        for (var i = 0; i < Count; i++)
        {
            fight.AddToHand(self.Side, Card);
        }
    }
}

/// <summary>Hoggy Bank's enchantment (BG30_MagicItem_411e): "Deathrattle: Get 2 Blood Gems."</summary>
[CardEffect("BG30_MagicItem_411e")]
internal sealed class HoggyBankDeathrattle : GrantedGet
{
    protected override string Card => "BG20_GEM";

    protected override int Count => 2;

    public override void Deathrattle(Fight fight, Unit self) => Get(fight, self);
}

/// <summary>Rusty Trident's enchantment (BG30_MagicItem_917e): "Deathrattle: Get a random Spellcraft spell."</summary>
[CardEffect("BG30_MagicItem_917e")]
internal sealed class RustyTridentDeathrattle : GrantedGet
{
    protected override string Card => CardToHand.AnyCard;

    public override void Deathrattle(Fight fight, Unit self) => Get(fight, self);
}

/// <summary>Mystic Essence (Dark Gift enchantment BG36_MidGameEffect_000t5e2): "Deathrattle: Get a random Tavern spell."</summary>
[CardEffect("BG36_MidGameEffect_000t5e2")]
internal sealed class MysticEssence : GrantedGet
{
    protected override string Card => CardToHand.AnyCard;

    public override void Deathrattle(Fight fight, Unit self) => Get(fight, self);
}

/// <summary>Charisma (Dark Gift enchantment BG36_MidGameEffect_000t3e2): "Rally: Get a random minion of your most common type."</summary>
[CardEffect("BG36_MidGameEffect_000t3e2")]
internal sealed class Charisma : GrantedGet
{
    protected override string Card => CardToHand.AnyCard;

    public override void OnAttack(Fight fight, Unit self, Unit target) => Get(fight, self);
}

/// <summary>Consanguinity (Dark Gift enchantment BG36_MidGameEffect_000t80e): "Rally: Get 2 Blood Gems."</summary>
[CardEffect("BG36_MidGameEffect_000t80e")]
internal sealed class Consanguinity : GrantedGet
{
    protected override string Card => "BG20_GEM";

    protected override int Count => 2;

    public override void OnAttack(Fight fight, Unit self, Unit target) => Get(fight, self);
}

/// <summary>
/// Big Brann Play (Darkmoon prize BGS_Treasures_030): "Your Battlecries trigger an extra time this turn." The fight belongs
/// to the turn: a Battlecry an effect triggers in it (Timewarped Greenskeeper) triggers once more, as with Gilnean War Horn.
/// Assumption (no log): the cast prize waits in the SECRET zone like other lasting Tavern spells and reaches the fight as
/// an extra; the largest multiplier wins.
/// </summary>
[CardEffect("BGS_Treasures_030")]
internal sealed class BigBrannPlay : CombatEffect
{
    public override int ExtraBattlecries(Fight fight, Unit self) => 1;
}
