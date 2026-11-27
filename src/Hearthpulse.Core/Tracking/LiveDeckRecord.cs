using Hearthpulse.Core.Data;
using Hearthpulse.Core.Logs;
using Hearthpulse.Core.Stats;

namespace Hearthpulse.Core.Tracking;

/// <summary>
/// The record of the deck being played, which both apps show in the overlay's deck panel. The hosts look it up off the
/// UI thread whenever <see cref="Key"/> changes.
/// </summary>
public static class LiveDeckRecord
{
    /// <summary>
    /// What the lookup depends on (deck, its code and id, the player's class); null outside a Constructed game. Not the
    /// opponent's class: the record does not depend on it, and its arrival would read the whole history once more.
    /// </summary>
    public static string? Key(ConstructedView? view) =>
        view is null ? null : $"{view.DeckName}|{view.DeckId}|{view.DeckCode}|{view.PlayerClass}";

    /// <summary>Reads the history (and Decks.log, for the deck's other codes); call it off the UI thread.</summary>
    /// <param name="installDir">The Hearthstone folder, when known.</param>
    public static DeckAggregate? Read(HistoryStore history, string? installDir, ConstructedView view)
    {
        var sameDeck = installDir is { } dir ? DecksLog.CodesOf(DecksLog.ReadAll(dir), view.DeckId) : [];
        var all = history.Recent(int.MaxValue, "Constructed");
        // Versions the player merged into one deck count together, as on the deck page.
        var games = DeckFamilies.WithFamily(all, DeckRecords.Matching(all, view.DeckName, view.DeckCode, view.PlayerClass, sameDeck), history.DeckFamilies());
        return DeckRecords.Aggregate(games, view.DeckName, view.DeckCode, view.PlayerClass);
    }
}
