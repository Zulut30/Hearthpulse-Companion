using System.Windows.Media;
using Hearthpulse.App.Infrastructure;
using Hearthpulse.Core.Cards;

namespace Hearthpulse.Presentation.Tests;

[Collection(MotionCollection.Name)]
public sealed class CardArtDispatchTests
{
    [Fact]
    public async Task Offline_disk_lookup_leaves_the_dispatcher_and_can_retry_after_a_miss()
    {
        var id = Guid.NewGuid().ToString("N");
        var probes = new List<int>();
        var dispatcherThread = 0;
        Task<ImageSource?>? load = null;
        bool Probe(string path)
        {
            probes.Add(Environment.CurrentManagedThreadId);
            return File.Exists(path);
        }

        var previous = CardArt.Http;
        try
        {
            CardArt.Http = null;
            WpfThread.Run(() =>
            {
                dispatcherThread = Environment.CurrentManagedThreadId;
                load = CardArt.GetAsync(ArtKind.Tile, id, Probe);
            });

            Assert.Null(await load!);
            Assert.NotEqual(dispatcherThread, Assert.Single(probes));
            CardArt.Absent.Found(CardImageFiles.Key(ArtKind.Tile, id));
            Assert.Null(await CardArt.GetAsync(ArtKind.Tile, id, Probe));
            Assert.Equal(2, probes.Count);
        }
        finally
        {
            CardArt.Http = previous;
        }
    }
}
