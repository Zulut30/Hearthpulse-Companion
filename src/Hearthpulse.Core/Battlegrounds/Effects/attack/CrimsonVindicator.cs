using Hearthpulse.Core.Battlegrounds.Effects.Spells;

namespace Hearthpulse.Core.Battlegrounds.Effects.Attack;

/// <summary>
/// Crimson Vindicator (BG36_241): "Divine Shield. Windfury. Rally: Cast Mighty Dragonbreath." Golden: "Cast Mighty
/// Dragonbreath twice."
/// </summary>
/// <remarks>
/// Each cast goes through the spell pipeline (<see cref="Fight.CastSpell(in SpellCast)"/>): "whenever you cast a Tavern
/// spell" effects react first, then Mighty Dragonbreath (BG36_246) resolves with the player's Tavern spell bonus
/// (<see cref="TavernSpells"/>; checked against a real log, 2026-11-15), then "after you cast" effects. The Rally runs on
/// every attack, so on both Windfury swings. Divine Shield and Windfury are captured keywords.
/// </remarks>
[CardEffect("BG36_241")]
internal sealed class CrimsonVindicator : CombatEffect
{
    public const string MightyDragonbreathId = TavernSpells.MightyDragonbreath;
    public const string SpellAttackTag = TavernSpells.SpellAttackTag;
    public const string SpellHealthTag = TavernSpells.SpellHealthTag;

    public override void OnAttack(Fight fight, Unit self, Unit target)
    {
        for (var cast = 0; cast < self.Pick(1, 2); cast++)
        {
            TavernSpells.Cast(fight, self, MightyDragonbreathId, null, TavernSpells.MightyDragonbreathSpell);
        }
    }
}
