namespace Hearthpulse.Core.Collection;

/// <summary>What is known about the split of owned copies by finish (<see cref="OwnedCopies"/>).</summary>
public static class CollectionFinish
{
    /// <summary>
    /// The split by finish (plain, golden, other) was compared by the owner with the game's collection screen
    /// (docs/collection.md). Until then a view shows no golden counts. A property, not a constant: a constant false would
    /// make the code behind it unreachable for the compiler (CS0162).
    /// </summary>
    public static bool GoldenVerified { get; } = false;
}
