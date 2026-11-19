namespace Hearthpulse.Core.Battlegrounds.Effects.Ongoing;

/// <summary>
/// Balinda Stonehearth (BG35_883): "Your spells that target friendly minions cast twice." Golden (BG35_883_G): "cast
/// three times."
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Applies to spells its owner casts in combat through <see cref="Fight.CastSpell"/> with a friendly minion as the
/// target: each extra cast resolves the spell again and runs "after you cast a spell" hooks again. Spells without a
/// target (Mighty Dragonbreath: "Give your minions...") and spells on enemy minions are cast once.</item>
/// <item>Blood Gems that a minion plays in combat ("this plays a Blood Gem on...") do not go through
/// <see cref="Fight.CastSpell"/> (see <c>BloodGems</c>): whether they count as your spells is not established, so Balinda
/// does not repeat them. No real log with Balinda in combat was available (2026-11-19).</item>
/// <item>"Twice" is a total: copies do not add up, the largest multiplier wins (<see cref="Fight.SpellCasts"/>).</item>
/// </list>
/// </remarks>
[CardEffect("BG35_883")]
internal sealed class BalindaStonehearth : CombatEffect
{
    public override int ExtraSpellCasts(Fight fight, Unit self, string spellCardId, Unit? target) =>
        target is { IsExtra: false } && target.Side == self.Side ? self.Pick(1, 2) : 0;
}
