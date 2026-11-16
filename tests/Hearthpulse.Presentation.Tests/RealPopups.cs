using Xunit.Abstractions;

[assembly: TestCollectionOrderer("Hearthpulse.Presentation.Tests.RealPopupsLast", "Hearthpulse.Presentation.Tests")]

namespace Hearthpulse.Presentation.Tests;

/// <summary>
/// The collection of the tests that open a real WPF popup (a <c>Popup</c>, a context menu, a tool tip: anything that
/// becomes a window of its own). It runs after every other test of the assembly (<see cref="RealPopupsLast"/>).
/// <para>
/// Why: on a machine where anything listens to UI Automation (a screen reader, an input or desktop tool; the
/// developer's machine has one), the first popup opened in a process changes what later windows of that process are
/// kept by: UI Automation then holds elements of a later main window for about a minute after it closed (measured:
/// 60 to 68 s; found in process dumps as ref-counted handles on <c>ElementProxy</c> of the window's lists). A stock
/// context menu, a tool tip and an empty <c>Popup</c> do it as well, on the commit before the collection's interface
/// too (2 of 2 runs each; without a popup 0 of 2). <c>MainWindowLifetimeTests</c> gives a closed window five turns of
/// the dispatcher, so it fails whenever a popup test happened to run before it: 5 of 8 full runs, exactly the runs
/// where the order was that one (docs/collection.md, "The closed window and UI Automation").
/// </para>
/// <para>A new test that opens a real popup goes into this collection.</para>
/// </summary>
[CollectionDefinition(Name)]
public sealed class RealPopups
{
    public const string Name = "Real popups (run last)";
}

/// <summary>Runs the collections in the order xunit found them, <see cref="RealPopups"/> after all the others.</summary>
public sealed class RealPopupsLast : ITestCollectionOrderer
{
    public IEnumerable<ITestCollection> OrderTestCollections(IEnumerable<ITestCollection> testCollections)
    {
        var all = testCollections.ToList();
        return all.Where(c => !IsLast(c)).Concat(all.Where(IsLast));

        static bool IsLast(ITestCollection collection) => string.Equals(collection.DisplayName, RealPopups.Name, StringComparison.Ordinal);
    }
}
