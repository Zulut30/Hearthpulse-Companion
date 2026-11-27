using Hearthpulse.Core.Data;
using Hearthpulse.Core.Tracking;
using Hearthpulse.UI.Overlay;

namespace Hearthpulse.Tests;

/// <summary>
/// The hosts' live-game pipeline (<see cref="LiveFeed"/>, shared by both apps): snapshots from the log thread coalesce
/// to the newest, are shown at most once a frame, the main window before the overlay, and the deck record follows the
/// deck being played. Nothing here runs on another thread or on real time: the UI thread and the worker that reads the
/// deck record are queues the test runs, the clock and the pacing wait are the test's too, so a busy thread pool or a
/// loaded machine cannot decide a result.
/// </summary>
public sealed class LiveFeedTests
{
    [Fact]
    public async Task The_default_worker_runs_the_read_on_another_thread()
    {
        var feed = new LiveFeed(_ => { }, _ => null, _ => { }, () => { });
        var finished = new TaskCompletionSource<(int Caller, int Worker)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var caller = Environment.CurrentManagedThreadId;
            feed.Offload(() => finished.SetResult((caller, Environment.CurrentManagedThreadId)));
        }) { IsBackground = true };
        thread.Start();
        var result = await finished.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotEqual(result.Caller, result.Worker);
    }

    [Fact]
    public void A_failed_dispatch_does_not_stop_later_snapshots()
    {
        var fail = true;
        var feed = new LiveFeed(action => { if (fail) throw new InvalidOperationException("dispatch test"); _ui.Enqueue(action); },
            _ => null, _ => { }, () => { });
        feed.Publish(Snap("one"));
        fail = false;
        feed.Publish(Snap("two"));
        Assert.Equal(1, RunUi());
        Assert.Equal("two", feed.Last.Scene);
    }

    [Fact]
    public void An_old_read_of_the_same_deck_cannot_replace_the_refreshed_record()
    {
        var feed = Feed();
        feed.Publish(Snap("one", Deck("Big Mage")));
        RunUi();
        var oldWork = _worker.Dequeue();
        feed.ForgetDeck();
        _now += 100;
        feed.Publish(Snap("two", Deck("Big Mage")));
        RunUi();
        var fresh = new DeckAggregate("Big Mage", null, "MAGE", 20, 12, 8, DateTime.UnixEpoch);
        _deckAnswer = () => fresh;
        RunWorker();
        RunUi();
        _deckAnswer = () => new DeckAggregate("Big Mage", null, "MAGE", 10, 6, 4, DateTime.UnixEpoch);
        oldWork();
        RunUi();
        Assert.Same(fresh, feed.Deck);
    }

    private readonly Queue<Action> _ui = new();
    private readonly Queue<Action> _worker = new();
    private readonly List<string> _shown = [];
    private readonly List<TaskCompletionSource> _waits = [];
    private readonly List<int> _waitLengths = [];
    private readonly List<ConstructedView> _deckReads = [];
    private Func<DeckAggregate?> _deckAnswer = () => null;
    private long _now = 1_000;

    private LiveFeed Feed() => new(
        post: _ui.Enqueue,
        readDeck: view =>
        {
            _deckReads.Add(view);
            return _deckAnswer();
        },
        showMain: s => _shown.Add("main " + s.Scene),
        showOverlay: () => _shown.Add("overlay"))
    {
        Clock = () => _now,
        Delay = ms =>
        {
            var wait = new TaskCompletionSource();
            _waits.Add(wait);
            _waitLengths.Add(ms);
            return wait.Task;
        },
        Offload = _worker.Enqueue,
    };

    private static LiveSnapshot Snap(string scene, ConstructedView? constructed = null) =>
        LiveSnapshot.Empty with { Scene = scene, Constructed = constructed };

    private static ConstructedView Deck(string name) =>
        new(5, name, true, "HERO_08", "MAGE", "HERO_01", "WARRIOR", null, [], 20, 4, [], [], 18, 5, 0, null, null);

    /// <summary>Runs what is queued for the UI thread now.</summary>
    private int RunUi() => Run(_ui);

    /// <summary>Runs the deck record reads handed to the worker, as a pool thread would.</summary>
    private int RunWorker() => Run(_worker);

    private static int Run(Queue<Action> queue)
    {
        var ran = 0;
        while (queue.TryDequeue(out var next))
        {
            next();
            ran++;
        }

        return ran;
    }

    [Fact]
    public void A_burst_of_snapshots_is_shown_once_with_the_newest_main_window_first()
    {
        var feed = Feed();

        feed.Publish(Snap("one"));
        feed.Publish(Snap("two"));
        feed.Publish(Snap("three"));

        Assert.Equal(1, RunUi());
        Assert.Equal(["main three", "overlay"], _shown);
        Assert.Equal("three", feed.Last.Scene);
        Assert.Equal(1, feed.TakeStats().Applied);
        Assert.Equal(0, RunUi());
    }

    [Fact]
    public void A_snapshot_within_a_frame_of_the_last_one_waits_out_the_rest_of_the_frame()
    {
        var feed = Feed();
        feed.Publish(Snap("one"));
        RunUi();

        _now += 10;
        feed.Publish(Snap("two"));
        Assert.Equal(0, RunUi());
        Assert.Equal([SnapshotPacer.MinGapMs - 10], _waitLengths);
        Assert.Equal(0, feed.Waiting); // scheduled at this very moment

        _now += 23;
        Assert.Equal(23, feed.Waiting);
        feed.Publish(Snap("three")); // joins the scheduled apply, no second wait
        Assert.Single(_waits);

        // Whoever ends the wait puts the apply on the UI thread itself: nothing in between that a busy pool could hold.
        _waits[0].SetResult();
        Assert.Equal(1, RunUi());
        Assert.Equal(["main one", "overlay", "main three", "overlay"], _shown);
        Assert.Equal(0, feed.Waiting);
        var stats = feed.TakeStats();
        Assert.Equal((2, 23L), (stats.Applied, stats.LongestWait));
        Assert.Equal((0, 0L, 0L), feed.TakeStats());
    }

    [Fact]
    public void The_deck_record_is_read_once_per_deck_and_shows_the_overlay_again_when_it_arrives()
    {
        var feed = Feed();
        feed.Publish(Snap("one", Deck("Big Mage")));
        RunUi();
        Assert.Null(feed.Deck);
        Assert.Empty(_deckReads); // not read on the UI thread

        var record = new DeckAggregate("Big Mage", null, "MAGE", 10, 6, 4, DateTime.UnixEpoch);
        _deckAnswer = () => record;
        Assert.Equal(1, RunWorker());
        Assert.Null(feed.Deck); // read, and on its way to the UI thread
        Assert.Equal(1, RunUi());
        Assert.Same(record, feed.Deck);
        Assert.Equal(["main one", "overlay", "overlay"], _shown);

        // The same deck: no new read; the history changed: read again.
        _now += 100;
        feed.Publish(Snap("two", Deck("Big Mage")));
        RunUi();
        Assert.Equal(0, RunWorker());
        Assert.Single(_deckReads);
        Assert.Same(record, feed.Deck);

        feed.ForgetDeck();
        _now += 100;
        feed.Publish(Snap("three", Deck("Big Mage")));
        RunUi();
        Assert.Null(feed.Deck);
        Assert.Equal(1, RunWorker());
        Assert.Equal(1, RunUi());
        Assert.Equal(2, _deckReads.Count);
        Assert.Same(record, feed.Deck);
    }

    /// <summary>
    /// The opponent's class comes in after the deck at the start of a game: the record does not depend on it, so the
    /// whole history is not read a second time for it (review of P2, m4).
    /// </summary>
    [Fact]
    public void The_opponents_class_arriving_does_not_read_the_deck_record_again()
    {
        var feed = Feed();
        var deck = Deck("Big Mage");
        feed.Publish(Snap("one", deck with { OpponentHeroCardId = "", OpponentClass = "" }));
        RunUi();
        var record = new DeckAggregate("Big Mage", null, "MAGE", 10, 6, 4, DateTime.UnixEpoch);
        _deckAnswer = () => record;
        RunWorker();
        RunUi();

        _now += 100;
        feed.Publish(Snap("two", deck));
        RunUi();
        Assert.Equal(0, RunWorker());
        Assert.Single(_deckReads);
        Assert.Same(record, feed.Deck);
    }

    [Fact]
    public void A_deck_record_that_fails_is_logged_and_nothing_reaches_the_screen()
    {
        var feed = Feed();
        feed.Publish(Snap("one", Deck("Big Mage")));
        RunUi();

        var marker = Guid.NewGuid().ToString("N");
        _deckAnswer = () => throw new InvalidOperationException(marker);
        Assert.Equal(1, RunWorker());

        Assert.Equal(0, RunUi());
        Assert.Null(feed.Deck);
        Assert.Equal(["main one", "overlay"], _shown);
        Assert.Contains($"Deck stats lookup failed: System.InvalidOperationException: {marker}", TestDataFolder.ReadLog(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_deck_record_that_arrives_after_the_deck_changed_is_dropped()
    {
        var feed = Feed();
        feed.Publish(Snap("one", Deck("Big Mage")));
        RunUi();

        _now += 100;
        feed.Publish(Snap("two")); // left the game: no deck
        RunUi();

        _deckAnswer = () => new DeckAggregate("Big Mage", null, "MAGE", 10, 6, 4, DateTime.UnixEpoch);
        Assert.Equal(1, RunWorker()); // the read of the deck that is gone ends only now
        Assert.Equal(1, RunUi());
        Assert.Null(feed.Deck);
        Assert.Equal(["main one", "overlay", "main two", "overlay"], _shown);
    }
}
