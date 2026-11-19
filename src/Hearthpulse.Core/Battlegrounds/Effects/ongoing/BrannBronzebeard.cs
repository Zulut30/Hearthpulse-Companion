namespace Hearthpulse.Core.Battlegrounds.Effects.Ongoing;

/// <summary>
/// Brann Bronzebeard (BG_LOE_077): "Your Battlecries trigger twice." Golden (TB_BaconUps_045): "trigger three times."
/// </summary>
/// <remarks>
/// In combat a Battlecry runs only when an effect triggers it (Timewarped Greenskeeper's Rally:
/// <see cref="Fight.TriggerBattlecry"/>); while Brann is alive each such trigger repeats. "Twice" is a total, so Branns do
/// not add up: two Branns trigger twice, a golden one three times (the engine takes the largest,
/// <see cref="Fight.BattlecryTriggers"/>). A Brann that dies in the same death phase as the trigger no longer counts.
/// </remarks>
[CardEffect("BG_LOE_077")]
internal sealed class BrannBronzebeard : CombatEffect
{
    public override int ExtraBattlecries(Fight fight, Unit self) => self.Pick(1, 2);
}
