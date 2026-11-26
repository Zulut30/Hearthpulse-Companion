using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Stats;
using Hearthpulse.Core.Tracking;

namespace Hearthpulse.Bench;

/// <summary>A Battlegrounds fight as a <see cref="GameSession"/> replay showed and kept it.</summary>
/// <param name="Shown">The last odds the overlay's snapshots carried while the fight was on screen; null when none did.</param>
/// <param name="Recorded">The odds of the fight in the session's last published view (its combat list); null when the game
/// ended in the batch of that fight, before a view showed it.</param>
/// <param name="Kept">The prediction the game's history row kept for the fight; null when none, or no row was written.</param>
internal sealed record LiveFight(int Turn, string Result, int DamageTaken, int DamageDealt, CombatOdds? Shown, CombatOdds? Recorded, BgCombatPrediction? Kept);

/// <summary>A Battlegrounds game of a <see cref="GameSession"/> replay: its fights and whether a history row was written.</summary>
/// <param name="ParseErrors">Power.log lines the session could not read during the game (<see cref="LiveSnapshot.ParseErrors"/>).</param>
internal sealed record LiveGame(int Index, bool Duos, bool HasRow, IReadOnlyList<LiveFight> Fights, int ParseErrors);

/// <summary>
/// The app's live path for Battlegrounds odds (TASK-2026-11-26 AC5): fed every snapshot a <see cref="GameSession"/>
/// publishes and every finished game, it keeps per fight the odds the overlay showed while the fight was on screen and
/// the odds of the session's combat list; <see cref="Games"/> then joins them with the history rows the session wrote
/// (<c>combat_results</c> / <c>combat_odds</c>, what the Battlegrounds page's accuracy line reads).
/// </summary>
internal sealed class LiveOddsProbe
{
    private readonly List<Game> _games = [];

    private sealed class Game
    {
        public bool Duos { get; init; }

        /// <summary>Per fight (its index in the session's combat list): the last odds a snapshot showed while it was on.</summary>
        public Dictionary<int, CombatOdds> Shown { get; } = new();

        public IReadOnlyList<CombatRecord> Combats { get; set; } = [];

        public int LastTurn { get; set; }

        public int ParseErrorsAtStart { get; init; }

        public int ParseErrors { get; set; }

        public string? Key { get; set; }
    }

    /// <summary>A snapshot the session published (<see cref="GameSession.SnapshotChanged"/>, or its <see cref="GameSession.Current"/>).</summary>
    public void Observe(LiveSnapshot snapshot)
    {
        if (snapshot.Battlegrounds is not { } bg)
        {
            return;
        }

        var errors = snapshot.ParseErrors?.Total ?? 0;
        var game = _games.Count > 0 ? _games[^1] : null;
        // A new game: the turn or the fights went back (the session keeps one game's view until the next one starts).
        if (game is null || bg.Turn < game.LastTurn || bg.Combats.Count < game.Combats.Count)
        {
            game = new Game { Duos = bg.IsDuos, ParseErrorsAtStart = game?.ParseErrors ?? errors };
            _games.Add(game);
        }

        game.LastTurn = bg.Turn;
        game.Combats = bg.Combats;
        game.ParseErrors = errors;
        if (bg.InCombat && bg.CombatOdds is { } odds)
        {
            // The fight on screen: the last recorded one when these are its odds (the client plays a fight after the log
            // recorded its end, when the game may be on the next turn already), else the one in progress.
            var index = bg.Combats.Count > 0 && ReferenceEquals(bg.Combats[^1].Odds, odds) ? bg.Combats.Count - 1 : bg.Combats.Count;
            game.Shown[index] = odds;
        }
    }

    /// <summary>A game the session finished (<see cref="GameSession.GameFinished"/>): its history key, when it was a Battlegrounds one.</summary>
    public void Finished(GameOutcome outcome)
    {
        if (outcome.Mode is "Battlegrounds" or "Duos" && _games.Count > 0 && outcome.Key is { } key)
        {
            _games[^1].Key = key;
        }
    }

    /// <summary>The games seen so far with their fights, joined with the history rows of <paramref name="history"/>.</summary>
    public IReadOnlyList<LiveGame> Games(IReadOnlyList<GameRecord> history)
    {
        var rows = history.Where(r => r.IsBattlegrounds).GroupBy(r => r.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        return _games.Where(g => g.Combats.Count > 0 || g.Shown.Count > 0).Select((g, i) =>
        {
            var row = g.Key is not null && rows.TryGetValue(g.Key, out var r) ? r : null;
            IReadOnlyList<BgCombatPrediction?> kept = row is null ? [] : BattlegroundsStats.Predictions(row);
            IReadOnlyList<BgCombatResult> saved = row is null ? [] : BattlegroundsStats.Combats(row);
            // A finished game's fights are its history row's: the session publishes no view once the game is over, so the
            // fight that ended it is missing from the last view when the end came in the same batch. Without a row: the
            // fights of the last view. A row's prediction counts only on the fight's turn (as BattlegroundsStats.SimAccuracy).
            var fights = row is not null
                ? saved.Select((c, n) => new LiveFight(c.Turn, c.Result, c.DamageTaken, c.DamageDealt, g.Shown.GetValueOrDefault(n),
                    n < g.Combats.Count && g.Combats[n].Turn == c.Turn ? g.Combats[n].Odds : null,
                    n < kept.Count && kept[n] is { } p && p.Turn == c.Turn ? p : null)).ToList()
                : g.Combats.Select((c, n) => new LiveFight(c.Turn, c.Result, c.DamageTaken, c.DamageDealt, g.Shown.GetValueOrDefault(n), c.Odds, null))
                    .ToList();
            return new LiveGame(i + 1, g.Duos, row is not null, fights, g.ParseErrors - g.ParseErrorsAtStart);
        }).ToList();
    }
}
