#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Main.Decks;
#else
namespace Hearthpulse.UI.Main.Decks;
#endif

/// <summary>The collection block under the deck list: can the main version be built, and which cards are missing.</summary>
public sealed partial class DeckDetailsViewModel
{
    private DeckCollectionView _collection = DeckCollectionView.Empty;

    /// <summary>The collection's status line, the main version's build label and its missing cards (docs/collection.md).</summary>
    public DeckCollectionView Collection { get => _collection; private set => Set(ref _collection, value); }

    /// <summary>Reads the block again: the deck, the collection, the card data or the client's state changed.</summary>
    internal void RefreshCollection()
    {
        CollectionRefreshes++;
        Collection = _family is { } family ? _owner.DeckCollectionFor(family.Main.DeckCode) : DeckCollectionView.Empty;
    }

    /// <summary>How many times the block was read (a test counts the reads one change costs).</summary>
    internal int CollectionRefreshes { get; private set; }
}
