using Hearthpulse.Core.Battlegrounds.Effects.Attack;

namespace Hearthpulse.Core.Battlegrounds.Effects.Spells;

/// <summary>
/// Geomagus Roogug (BG28_583): "Divine Shield. Whenever a Blood Gem is played on this, this plays a Blood Gem on a
/// different friendly minion." Golden: "this plays 2 Blood Gems on a different friendly minion."
/// </summary>
/// <remarks>
/// Every Blood Gem played on it in combat (Bonker, Prodigious Tusker, Razorfen Vineweaver, Tusked Camper, another
/// Roogug: all go through <see cref="Fight.PlayBloodGem"/>) sets it off once; it plays its own gems (the player's Blood
/// Gem value, <see cref="BloodGems.Value"/>) on one random other living friendly minion, both golden gems on the same one.
/// Two Roogugs pass gems back and forth until the engine's depth limit; the game's own limit is not known. Divine Shield
/// is a captured keyword. Not seen in a real log yet.
/// </remarks>
[CardEffect("BG28_583")]
internal sealed class GeomagusRoogug : CombatEffect
{
    public override void AfterFriendlyBloodGem(Fight fight, Unit self, Unit target, Unit? source)
    {
        if (target != self || !self.IsAlive || PickOther(fight, self) is not { } other)
        {
            return;
        }

        BloodGems.Play(fight, self, other, self.Pick(1, 2));
    }

    /// <summary>The random "different friendly minion".</summary>
    private static Unit? PickOther(Fight fight, Unit self) => fight.RandomFriendly(self);
}
