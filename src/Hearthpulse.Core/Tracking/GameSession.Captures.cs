using Hearthpulse.Core.Data;
using Hearthpulse.Core.Power;

namespace Hearthpulse.Core.Tracking;

/// <summary>The fights of each finished Battlegrounds game, kept locally for measuring the simulator (docs/bg-combat-sim.md).</summary>
public sealed partial class GameSession
{
    /// <summary>Where the fights are kept (<see cref="AppPaths.CombatCaptures"/>); tests point it at a scratch folder, null keeps nothing.</summary>
    internal CombatCaptureStore? CombatCaptures { get; set; } = new(AppPaths.CombatCaptures);

    /// <summary>The last save, for tests to wait on; the session itself never waits for it.</summary>
    internal Task CombatCapturesSaved { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Hands the game's fights to the store. Only the list is put together here, on the log thread, from records that
    /// never change afterwards; packing and writing run on a pool thread, and a failure there is logged, never raised.
    /// </summary>
    private void SaveCombatCaptures(GameRecord record, GameState game)
    {
        if (CombatCaptures is not { } store || !record.IsBattlegrounds || _battlegrounds.Captures.Count == 0)
        {
            return;
        }

        var captures = SavedCaptures.From(game.StartedAt, game.BuildNumber, game.IsDuos, _battlegrounds.Captures, _battlegrounds.Combats);
        if (captures.Fights.Count == 0)
        {
            return;
        }

        var key = record.Key;
        CombatCapturesSaved = Task.Run(() =>
        {
            try
            {
                store.Save(key, captures);
            }
            catch (Exception ex)
            {
                Diag.Error("Saving the game's combat captures failed", ex);
            }
        });
    }
}
