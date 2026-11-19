namespace Hearthpulse.Core.Battlegrounds.Effects.Spells;

/// <summary>
/// "Get a card" in combat, so "whenever a card is added to your hand" (The Shadow of Doubt) sees it. Card text from
/// HearthstoneJSON (enUS, 2026-11-19); the golden versions get two of each.
/// </summary>
/// <remarks>
/// Each card is added through <see cref="Fight.AddToHand"/>. Spells and random cards (random Bounty, Chromadrake, Beast,
/// Tier 6 minion...) only count as a card: which card it is does not change this combat. Locked-up Mutineer and Dark
/// Paradox's "Get" form: CardToHandMore.cs. Not covered: Duos cards whose card goes to the teammate.
/// </remarks>
internal static class CardToHand
{
    /// <summary>A card nobody can name before it is drawn (a random one).</summary>
    public const string AnyCard = "";

    /// <summary>The card a "Get" minion gets (normal version: one; golden: two), or null for other minions.</summary>
    public static string? CardOf(string minionId) => minionId switch
    {
        "BG32_111" => TavernSpells.MisplacedTeaSet, // Nightmare Par-tea Guest: Battlecry and Deathrattle
        "BG32_336" => "BG32_337",                    // Gormling Gourmet: Seafood Stew
        "BG32_820" => "BG28_168",                    // Firescale Hoarder: Shiny Ring
        "BG33_821" => AnyCard,                        // Shipwrecked Rascal: a random Bounty
        "BG34_171" => AnyCard,                        // Conveyor Construct: a random Magnetic Volumizer
        "BG34_319" => AnyCard,                        // Highkeeper Ra: a random Tier 6 minion (Deathrattle and Rally)
        "BG34_633" => AnyCard,                        // Draconic Warden: a random Chromadrake
        "BG34_682" => "BG34_689",                    // Razorfen Flapper: Blood Gem Barrage
        "BG35_881" => "BG35_911",                    // Leyline Surfacer: Arcane Absorption
        "BG36_101" => AnyCard,                        // Unwilling Slacker: a random 1-Cost Tavern spell
        "BG36_103" => "BG36_371",                    // N'raqi Sapper: Energizing Chamber
        "BG36_731" => "BG36_880",                    // Imp-lusionist: Methodical Madness
        "BG36_854" => "BG36_624",                    // Rescue Bot: Repair Job
        "BGDUO_111" => "BG20_GEM",                   // Generous Geomancer (Duos): you (and your teammate) get a Blood Gem
        "BG20_101" => "BG20_GEM",                    // Roadboar: Rally
        "BG33_822" => AnyCard,                        // Bigwig Bandit: Rally, a random Bounty
        "BG36_204" => AnyCard,                        // Headhunter Gryphon: Rally, a random Beast
        "BG36_242" => AnyCard,                        // Bronze Timewalker: Rally, a random Chromadrake
        "BG36_331" => AnyCard,                        // Bramble Tunneler: Rally, a random Choose One card
        "BG32_324" => "BG28_604",                    // Drustfallen Butcher: Avenge (4), Butchering
        _ => null,
    };

    /// <summary>The normal version gets one card, the golden one two.</summary>
    public static void Get(Fight fight, Unit self)
    {
        if (CardOf(self.CardId) is not { } card)
        {
            return;
        }

        for (var i = 0; i < self.Pick(1, 2); i++)
        {
            fight.AddToHand(self.Side, card);
        }
    }
}

/// <summary>
/// "Deathrattle: Get ...": Nightmare Par-tea Guest (BG32_111), Gormling Gourmet (BG32_336), Firescale Hoarder (BG32_820),
/// Shipwrecked Rascal (BG33_821), Conveyor Construct (BG34_171), Draconic Warden (BG34_633), Razorfen Flapper (BG34_682),
/// Leyline Surfacer (BG35_881), Unwilling Slacker (BG36_101), N'raqi Sapper (BG36_103), Imp-lusionist (BG36_731), Rescue
/// Bot (BG36_854), Generous Geomancer (BGDUO_111). See <see cref="CardToHand.CardOf"/>.
/// </summary>
[CardEffect("BG32_111")]
[CardEffect("BG32_336")]
[CardEffect("BG32_820")]
[CardEffect("BG33_821")]
[CardEffect("BG34_171")]
[CardEffect("BG34_633")]
[CardEffect("BG34_682")]
[CardEffect("BG35_881")]
[CardEffect("BG36_101")]
[CardEffect("BG36_103")]
[CardEffect("BG36_731")]
[CardEffect("BG36_854")]
[CardEffect("BGDUO_111")]
internal sealed class GetOnDeathrattle : CombatEffect
{
    public override void Deathrattle(Fight fight, Unit self) => CardToHand.Get(fight, self);
}

/// <summary>
/// "Rally: Get ...": Roadboar (BG20_101), Bigwig Bandit (BG33_822), Headhunter Gryphon (BG36_204), Bronze Timewalker
/// (BG36_242), Bramble Tunneler (BG36_331). Every attack (both Windfury swings).
/// </summary>
[CardEffect("BG20_101")]
[CardEffect("BG33_822")]
[CardEffect("BG36_204")]
[CardEffect("BG36_242")]
[CardEffect("BG36_331")]
internal sealed class GetOnRally : CombatEffect
{
    public override void OnAttack(Fight fight, Unit self, Unit target) => CardToHand.Get(fight, self);
}

/// <summary>Highkeeper Ra (BG34_319): "Battlecry, Deathrattle, and Rally: Get a random Tier 6 minion." Golden: "two".</summary>
[CardEffect("BG34_319")]
internal sealed class HighkeeperRa : CombatEffect
{
    public override void OnAttack(Fight fight, Unit self, Unit target) => CardToHand.Get(fight, self);

    public override void Deathrattle(Fight fight, Unit self) => CardToHand.Get(fight, self);
}

/// <summary>Drustfallen Butcher (BG32_324): "Avenge (4): Get a Butchering." Golden: "Get 2 Butcherings."</summary>
[CardEffect("BG32_324")]
internal sealed class DrustfallenButcher : AvengeEffect
{
    protected override int Threshold(Unit self) => 4;

    protected override void Avenge(Fight fight, Unit self) => CardToHand.Get(fight, self);
}

/// <summary>
/// Lichling Hoarder (BG36_848): "Avenge (3): Get a plain copy of a minion that started in your warband this combat."
/// Golden: "Get 2 plain copies".
/// </summary>
/// <remarks>
/// A random minion of the captured board (the warband when the fight started), each copy picked on its own. "Plain" =
/// the card's printed Attack, Health and keywords, golden if that minion was. The copy goes to the hand list, where a
/// later "summon from your hand" can find it.
/// </remarks>
[CardEffect("BG36_848")]
internal sealed class LichlingHoarder : AvengeEffect
{
    protected override int Threshold(Unit self) => 3;

    protected override void Avenge(Fight fight, Unit self)
    {
        var start = self.Side.Info.Minions;
        for (var i = 0; i < self.Pick(1, 2) && start.Count > 0; i++)
        {
            var source = start[PickStarter(fight, self, start)];
            var copy = SimMinion.Create(source.CardId, source.RebornAttack, source.BaseHealth > 0 ? source.BaseHealth : source.Health,
                source.Tribes, source.BaseKeywords, source.Golden, source.Mechanics) with { TechLevel = source.TechLevel };
            fight.AddToHand(self.Side, source.CardId, copy);
        }
    }

    /// <summary>The random minion of the starting warband (one decision of this Hoarder per copy).</summary>
    private static int PickStarter(Fight fight, Unit self, IReadOnlyList<SimMinion> start) => fight.Choose(ChoiceKind.RandomCard, self, start);
}
