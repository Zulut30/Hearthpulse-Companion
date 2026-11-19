namespace Hearthpulse.Core.Battlegrounds.Effects.Anomalies;

// Anomalies that act in combat. The anomaly is lobby-wide and visible to every player: the tracker reads it from the
// game entity (BACON_GLOBAL_ANOMALY_DBID) and gives it to both sides as an extra, so "your" in its text is each side's
// own. No anomaly of today's rotation changes a fight after capture (Hearthpulse API /api/v1/anomalies in_pool and
// hsbg.cards agree on 2026-11-19: Start of Combat or Tavern text only); Echoes of Argus is out of the pool and kept for
// older games.

/// <summary>Echoes of Argus (BG27_Anomaly_802): "Your Battlecries and Deathrattles trigger an extra time."</summary>
/// <remarks>
/// Each side's deathrattles trigger once more, and so do the Battlecries an effect triggers in combat
/// (<see cref="Fight.TriggerBattlecry"/>, e.g. Timewarped Greenskeeper's Rally). Assumptions (no log with the anomaly):
/// the extra deathrattle adds to Titus Rivendare's, as the engine sums every extra deathrattle source of a side; the
/// extra Battlecry follows the engine's Battlecry rule, where the largest multiplier wins, so with Brann Bronzebeard a
/// Battlecry triggers twice, not three times.
/// </remarks>
[CardEffect("BG27_Anomaly_802")]
internal sealed class EchoesOfArgus : CombatEffect
{
    public override int ExtraDeathrattles(Fight fight, Unit self) => 1;

    public override int ExtraBattlecries(Fight fight, Unit self) => 1;
}
