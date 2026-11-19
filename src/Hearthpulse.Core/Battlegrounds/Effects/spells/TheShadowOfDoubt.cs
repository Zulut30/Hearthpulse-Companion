namespace Hearthpulse.Core.Battlegrounds.Effects.Spells;

/// <summary>
/// The Shadow of Doubt (BG36_109): "Whenever a card is added to your hand, give your Deity +4/+5." Golden: "+8/+10".
/// </summary>
/// <remarks>
/// In combat, cards come to the hand from "Get ..." deathrattles, Rallies and Avenges (<see cref="CardToHand"/>), Deathly
/// Striker and Chef's Choice, all through <see cref="Fight.AddToHand"/>. A real log (2026-11-15) shows it triggering in
/// combat right after N'raqi Sapper's "Deathrattle: Get an Energizing Chamber". The hand is assumed to have room (a full
/// hand cannot be told from the capture; see <see cref="Fight.AddToHand"/>). Locked-up Mutineer is not counted: whether
/// it adds a Lockbox depends on one the capture does not show.
/// </remarks>
[CardEffect("BG36_109")]
internal sealed class TheShadowOfDoubt : CombatEffect
{
    public override void AfterCardAddedToHand(Fight fight, Unit self, string cardId) =>
        SpellTriggers.GiveDeity(fight, self.Side, SpellTriggers.Shown1(self, 4, 8), SpellTriggers.Shown2(self, 5, 10));
}
