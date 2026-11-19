namespace Hearthpulse.Core.Battlegrounds.Effects.Anomalies;

// Anomalies that act in combat. The anomaly is lobby-wide and visible to every player: the tracker reads it from the
// game entity (BACON_GLOBAL_ANOMALY_DBID) and gives it to both sides as an extra, so "your" in its text is each side's
// own. No anomaly of today's rotation changes a fight after capture (Hearthpulse API /api/v1/anomalies in_pool and
// hsbg.cards agree on 2026-11-19: Start of Combat or Tavern text only); Echoes of Argus is out of the pool and kept for
// older games.

/// <summary>Echoes of Argus (BG27_Anomaly_802): "Your Battlecries and Deathrattles trigger an extra time."</summary>
/// <remarks>
/// Each side's deathrattles trigger once more. Battlecries trigger in the Tavern; the engine triggers none in combat,
/// so that half has nothing to act on. Assumption (no log with the anomaly): it adds to Titus Rivendare's extra
/// triggers, as the engine sums every extra-trigger source of a side.
/// </remarks>
[CardEffect("BG27_Anomaly_802")]
internal sealed class EchoesOfArgus : CombatEffect
{
    public override int ExtraDeathrattles(Fight fight, Unit self) => 1;
}
