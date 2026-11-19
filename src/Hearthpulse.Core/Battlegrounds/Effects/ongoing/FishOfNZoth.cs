namespace Hearthpulse.Core.Battlegrounds.Effects.Ongoing;

/// <summary>
/// Fish of N'Zoth (TB_BaconShop_HP_105t, 2/2, from N'Zoth's hero power Avatar of N'Zoth): "After a different friendly
/// Deathrattle minion dies in combat, gain its Deathrattle." Golden (TB_BaconUps_307): "… twice."
/// </summary>
/// <remarks>
/// Found by the relevance audit (2026-11-19): the token was labelled combat-relevant with no effect, so every fight with it
/// was "not simulated". The Fish gains each Deathrattle effect the dead minion had (its card's and its enchantments'),
/// once per death, for the rest of the fight. A gained Deathrattle runs with the Fish as its owner (its slot, its stats
/// and its goldenness where the effect reads them: assumption, no log). A Deathrattle without an effect is not gained;
/// the dead minion already marks the fight "not simulated".
/// </remarks>
[CardEffect("TB_BaconShop_HP_105t")]
internal sealed class FishOfNZoth : CombatEffect
{
    public override void AfterFriendlyDeath(Fight fight, Unit self, Unit dead)
    {
        if (dead == self || !self.IsAlive || !dead.HasDeathrattle)
        {
            return;
        }

        foreach (var effect in dead.Effects)
        {
            if ((effect.Hooks & Hook.Deathrattle) == 0)
            {
                continue;
            }

            var gained = new GainedDeathrattle(effect);
            for (var n = self.Pick(1, 2); n > 0; n--)
            {
                self.AddEffect(gained);
            }
        }
    }

    /// <summary>Only the Deathrattle of another minion's effect.</summary>
    private sealed class GainedDeathrattle : CombatEffect
    {
        private readonly CombatEffect _inner;

        public GainedDeathrattle(CombatEffect inner) => _inner = inner;

        public override void Deathrattle(Fight fight, Unit self) => _inner.Deathrattle(fight, self);
    }
}
