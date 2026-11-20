using Hearthpulse.Core.Battlegrounds.Effects.Spells;

namespace Hearthpulse.Core.Battlegrounds.Effects.Ongoing;

// Pool minions whose Deathrattle or Rally raises the player's Tavern spell bonus "this game" (relevance audit, 2026-11-19).
// In a fight the raise counts for the Tavern spells cast later in it (Seafloor Recruiter's Chef's Choice, Crimson
// Vindicator's Mighty Dragonbreath...), which read the bonus at cast time (TavernSpells.Stats), as Honeycomb Ring's raise
// does. Numbers from the latest card text (HearthstoneJSON enUS, build 256589); no log of the owner shows one of them
// dying before a spell cast in the same fight.

/// <summary>Raises the side's Tavern spell bonus for the rest of the fight.</summary>
internal static class TavernBonus
{
    public static void Raise(Side side, int attack, int health)
    {
        if (attack != 0)
        {
            side.AddCounter(TavernSpells.SpellAttackTag, attack);
        }

        if (health != 0)
        {
            side.AddCounter(TavernSpells.SpellHealthTag, health);
        }
    }
}

/// <summary>Dark Puppeteer (BG36_104): "Deathrattle: Your Tavern spells give an extra +4 Health this game." Golden: "+8".</summary>
[CardEffect("BG36_104")]
internal sealed class DarkPuppeteer : CombatEffect
{
    public override void Deathrattle(Fight fight, Unit self) => TavernBonus.Raise(self.Side, 0, self.Pick(4, 8));
}

/// <summary>Friendly Geist (BG32_880): "Deathrattle: Your Tavern spells give an extra +1 Attack this game." Golden: "+2".</summary>
[CardEffect("BG32_880")]
internal sealed class FriendlyGeist : CombatEffect
{
    public override void Deathrattle(Fight fight, Unit self) => TavernBonus.Raise(self.Side, self.Pick(1, 2), 0);
}

/// <summary>Blue Whelp (BG33_924): "Rally: Your Tavern spells give an extra +1 Health this game." Golden: "+2". Each attack.</summary>
[CardEffect("BG33_924")]
internal sealed class BlueWhelp : CombatEffect
{
    public override void OnAttack(Fight fight, Unit self, Unit target) => TavernBonus.Raise(self.Side, 0, self.Pick(1, 2));
}
