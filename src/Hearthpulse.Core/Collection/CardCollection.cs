namespace Hearthpulse.Core.Collection;

/// <summary>
/// The copies of one card the player owns. What counts is <see cref="Total"/>: a copy of any finish fills a deck slot.
/// The split into <see cref="Normal"/>, <see cref="Golden"/> and <see cref="Other"/> rests on an assumption about the
/// client's finish numbers (0 plain, 1 golden, the rest diamond, signature and later ones) that was not compared with
/// the game's screen (docs/collection.md): do not show it as a fact. Nothing in the deck cost depends on it.
/// </summary>
public readonly record struct OwnedCopies(int Normal, int Golden, int Other)
{
    /// <summary>Copies of any finish.</summary>
    public int Total => Normal + Golden + Other;
}

/// <summary>
/// The player's card collection as the game client held it at <paramref name="ReadAt"/> (UTC): copies by card id and
/// the arcane dust balance (docs/collection.md). Kept on this machine only.
/// </summary>
/// <param name="Cards">Owned copies by card id (HearthstoneJSON <c>id</c>); a card that is not owned has no entry.</param>
/// <param name="Dust">Arcane dust the player could spend at the read. Always a read value: a balance that could not be read gives no collection.</param>
/// <param name="ReadAt">When the client's memory was read, UTC.</param>
public sealed record CardCollection(IReadOnlyDictionary<string, OwnedCopies> Cards, long Dust, DateTime ReadAt)
{
    /// <summary>
    /// True only for a collection read from the client that is running now. False for one loaded from the file, and
    /// again once that client was closed, restarted or sent back to its login screen: the numbers may then be old, or
    /// another account's (the file carries no account), and are to be shown as "as of <see cref="ReadAt"/>", not as fact.
    /// </summary>
    public bool Confirmed { get; init; }

    /// <summary>The copies of a card, zero of each when it is not owned.</summary>
    public OwnedCopies Owned(string cardId) => Cards.GetValueOrDefault(cardId);
}

/// <summary>The bounds a collection is held to, the same for the memory read and the saved file.</summary>
internal static class CollectionLimits
{
    /// <summary>More stacks than any collection has (one per card and finish).</summary>
    public const int MaxStacks = 100_000;

    /// <summary>More different cards than the game has.</summary>
    public const int MaxCards = 100_000;

    /// <summary>Copies of one card and finish, and of one card in all; far above what an account holds, far below an overflow.</summary>
    public const int MaxCopies = 10_000;

    public const long MaxDust = 1_000_000_000;

    /// <summary>A card id as the game writes them: Latin letters, digits and underscores, at most 64. Safe in a file name or a URL.</summary>
    public static bool IsCardId(string? id) =>
        id is { Length: > 0 and <= 64 } && id.All(c => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_');

    /// <summary>Copies of one card within the bounds, each finish and their sum.</summary>
    public static bool Fits(OwnedCopies copies) =>
        copies is { Normal: >= 0 and <= MaxCopies, Golden: >= 0 and <= MaxCopies, Other: >= 0 and <= MaxCopies } && copies.Total <= MaxCopies;
}
