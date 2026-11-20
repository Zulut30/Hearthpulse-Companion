using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Hearthpulse.Core;
using Hearthpulse.Core.Accounts;
using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Collection;
using Hearthpulse.Core.Constructed;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Diagnostics;
using Hearthpulse.Core.Hosting;
using Hearthpulse.Core.Pool;
using Hearthpulse.Core.Replay;
using Hearthpulse.Core.Tracking;
using Hearthpulse.Core.Updates;
using Hearthpulse.UI.Main;

namespace Hearthpulse.Tests;

/// <summary>
/// The collection never leaves the machine, checked by what goes over the wire (docs/collection.md, "Privacy"): the
/// app's services are built as a host builds them (<see cref="AppServices"/>) on a handler that records every request;
/// the session holds a collection with a marker card id and a marker dust balance, the main view model shows it; then
/// the usual senders run - the meta download, a finished game as a game record, shared games and a source capture,
/// the profile upload, a crash report, the update check - and no request has a marker in its address, a header or its
/// body (also inside gzip and base64). A check of names in source text cannot see this; a sender that is not run here
/// is not covered, and the class says which those are.
/// </summary>
public sealed partial class CollectionPrivacyTests
{
    /// <summary>A card id only the collection holds: a valid id (Latin letters, digits, underscore), in no card data.</summary>
    private const string MarkerCard = "HPMARKERCARD_7Q9X";

    /// <summary>A dust balance only the collection holds (under the 1 000 000 000 bound; not a real balance).</summary>
    private const long MarkerDust = 918_273_645;

    /// <summary>How many different cards the made-up collection holds: the one number of it the log writes down.</summary>
    private const int MarkerCount = 7391;

    /// <summary>A card back's name and an achievement's name only the session's cosmetics and achievements hold.</summary>
    private const string MarkerBack = "HPMARKERBACK_3K7Z";

    private const string MarkerAchievement = "HPMARKERACHV_5V2P";

    private const string Pool = "https://pool.test";
    private const string Site = "https://site.test/api/v1/";

    [Fact]
    public async Task No_request_of_the_usual_senders_carries_a_marker_of_the_collection()
    {
        var root = TestDataFolder.Scratch("hp-collection-privacy");
        Directory.CreateDirectory(root);
        var wire = new Wire();
        using var http = new HttpClient(wire);
        var settings = new AppSettings
        {
            PoolEndpoint = Pool,
            PoolSecret = new string('5', 64),
            UploadGamesToProfile = true,
            SendCrashReports = true,
        };
        var cards = CollectionFixture.Cards;
        var replays = new ReplayStore(Path.Combine(root, "replays"));
        var data = new AppData(root, replays, new ReplayLibrary(replays, Path.Combine(root, "saved"), Path.Combine(root, "imported")),
            new ReplayUploads(Path.Combine(root, "uploads.json")));
        var services = new AppServices(settings, http, () => cards, data, Path.Combine(root, "history.db"), Path.Combine(root, "usage.json"), "0.0.0-test");
        try
        {
            var session = services.Session;
            session.ReadsMemory = false;
            session.IsHearthstoneRunning = () => false;

            // ---- The collection: saved by an earlier run, loaded at start, then read from the "client". As in the hosts,
            // the event carries nothing and is handled on the UI thread (here: a queue the test runs).
            var file = Path.Combine(root, "collection.json");
            new CollectionStore(file).Save(Marked());
            var watch = new CollectionWatch(new CollectionStore(file));
            session.CollectionWatch = watch;
            var model = new MainViewModel(services.History, settings, data) { CollectionReadable = true };
            var ui = new ConcurrentQueue<Action>();
            session.CollectionChanged += () => ui.Enqueue(() => model.SetCollection(session.Collection));
            void RunUi()
            {
                while (ui.TryDequeue(out var action))
                {
                    action();
                }
            }

            // ---- The card backs and the achievements, read from the "client" and held by the session, saved in their files.
            var cosmeticsFile = Path.Combine(root, "cosmetics.json");
            var achievementsFile = Path.Combine(root, "achievements.json");
            var cosmeticsWatch = new CosmeticsWatch(new CosmeticsStore(cosmeticsFile));
            var achievementsWatch = new Hearthpulse.Core.Progression.AchievementsWatch(new Hearthpulse.Core.Progression.AchievementsStore(achievementsFile));
            session.CosmeticsWatch = cosmeticsWatch;
            session.AchievementsWatch = achievementsWatch;
            cosmeticsWatch.ClientStarted();
            achievementsWatch.ClientStarted();
            var readAt = session.Clock();
            cosmeticsWatch.Apply(MarkedCosmetics(readAt), cosmeticsWatch.Ticket(readAt, inGame: false), readAt);
            achievementsWatch.Apply(MarkedAchievements(readAt), achievementsWatch.Ticket(readAt, inGame: false), readAt);
            await cosmeticsWatch.Saved.WaitAsync(TimeSpan.FromSeconds(30));
            await achievementsWatch.Saved.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(MarkerBack, session.Cosmetics!.CardBacks[0].Name);
            Assert.True(session.Achievements!.Confirmed);
            Assert.Equal(MarkerAchievement, session.Achievements.Achievements[1].Name);

            model.SetCards(cards);
            model.ShowConstructedFormat("FT_STANDARD");
            model.SetConstructedMeta(CollectionFixture.Meta());
            watch.Start();
            await watch.Loaded.WaitAsync(TimeSpan.FromSeconds(30));
            RunUi();
            Assert.True(model.CollectionSaved, "the saved collection reached the view model");

            watch.ClientStarted();
            var now = session.Clock();
            watch.Apply(Marked(), watch.Ticket(now, inGame: false), now);
            await watch.Saved.WaitAsync(TimeSpan.FromSeconds(30));
            RunUi();
            Assert.Equal((true, MarkerDust), (session.Collection?.Confirmed, session.Collection?.Dust));
            Assert.True(session.Collection!.Owned(MarkerCard).Total > 0);

            // The view model uses it everywhere the collection shows: labels, the filter, the order, the deck page.
            Assert.True(model.CanBuildAvailable);
            model.CanBuildOnly = true;
            model.SetMetaDeckSort.Execute("Dust");
            Assert.Contains(model.MetaDecks, d => d.Build is not null);
            Assert.NotNull(model.DeckCollectionFor(CollectionFixture.NeedsDust).Build);
            Assert.Contains(MarkerDust.ToString("N0", System.Globalization.CultureInfo.CurrentCulture), model.MetaDecks.First(d => d.Build!.Kind == "CraftableNow").Build!.Tip, StringComparison.Ordinal);

            // ---- 1. The meta: Constructed and Battlegrounds stats, the archetype names, the card pool, the update check.
            var api = new Hearthpulse.Core.Api.HearthpulseData(services.Api, Path.Combine(root, "cache-api"));
            await ConstructedMeta.LoadAsync(api, cards, "standard", "legend", "past_week", "enUS");
            await BattlegroundsMeta.LoadAsync(api);
            await ArchetypeTranslations.LoadAsync(http, Path.Combine(root, "cache-names"));
            await services.LoadCardPoolAsync();
            await UpdateService.CheckAsync(http);
            await services.Usage.RefreshLimitsAsync(force: true);

            // ---- 2. A finished game: its game record, the shared games of both modes, the profile upload.
            var ranked = Ranked();
            Assert.True(services.History.Add(ranked));
            var outcome = new GameOutcome("Ranked", "Win", 0, null, null, "HERO_08")
            {
                Key = ranked.Key,
                GameType = "GT_RANKED",
                Format = "FT_STANDARD",
                ConstructedPicks = [new ConstructedPick(4, "C_RARE", ["C_COMMON", "C_RARE", "C_EPIC"], ["C_RARE"])],
            };
            services.PoolSharing.Delay = _ => Task.CompletedTask;
            services.PoolSharing.Start();
            foreach (var handler in Handlers(session, nameof(GameSession.GameFinished)))
            {
                ((Action<GameOutcome>)handler)(outcome);
            }

            Assert.True(SpinWait.SpinUntil(() => services.GameStats.PendingCount > 0, TimeSpan.FromSeconds(30)), "the game record was queued");
            await services.GameStats.PassAsync(CancellationToken.None);
            await services.PoolSharing.ShareAsync(outcome);
            await services.PoolSharing.ShareAsync(Battlegrounds());

            // The profile: the services' own uploader is signed out (a sign-in is a device flow against the site), so the
            // same uploader class is run with a made-up signed-in session on the same client and the same history.
            using var profile = new ProfileUploader(new AccountClient(http, Site), (_, _) => Task.FromResult<string?>("access-token"),
                () => new AccountSession("u-42", 1), () => settings.UploadGamesToProfile, services.History, new ProfileUploadQueue(Path.Combine(root, "profile-upload.json")));
            profile.OnGameFinished(outcome);
            await profile.PassAsync(CancellationToken.None);

            // ---- 3. A source capture of a game's log lines, sent to the pool.
            await services.SourceCaptures.ApplySettingsAsync();
            Assert.True(services.SourceCaptures.Capturing);
            session.OnSessionStarted("synthetic", new DateTime(2026, 11, 9, 0, 0, 0, DateTimeKind.Local), attachedMidSession: false);
            session.OnLines("Power.log", ReplayRecorderTests.Setup.Select(b => "D 12:30:00.0000000 GameState." + b).ToArray(), catchingUp: false);
            session.StopSourceCapture();
            await services.SourceCaptures.ApplySettingsAsync();
            await services.SourceCaptures.SendPendingAsync();

            // ---- 4. A crash report, with the log's tail (the log names the read, never the collection).
            var crashes = new CrashReporter(http, new Uri(Site + "crash-reports"), Path.Combine(root, "crashes"), () => true, "test", "0.0.0-test");
            Assert.False(await crashes.SendAsync("UI error", Thrown(new InvalidOperationException("The collection view failed"))), "the site answers nothing here");
            RunUi();

            // ---- Every sender did talk: a test that sent nothing would prove nothing.
            var sent = wire.Requests;
            void Talked(string what, Func<Request, bool> its) => Assert.True(sent.Any(its), $"{what}: no request among\n{string.Join('\n', sent.Select(r => r.Method + " " + r.Url))}");
            Talked("Constructed meta", r => r.Url.Contains("/v1/hsguru/", StringComparison.Ordinal));
            Talked("Battlegrounds meta", r => r.Url.Contains("battlegrounds", StringComparison.OrdinalIgnoreCase) || r.Url.Contains("/bg", StringComparison.OrdinalIgnoreCase));
            Talked("update check", r => r.Url.Contains("github", StringComparison.OrdinalIgnoreCase));
            Talked("card pool", r => r.Url.Contains("constructed-cards", StringComparison.Ordinal));
            Talked("usage limits", r => r.Url.EndsWith("usage/limits", StringComparison.Ordinal));
            Talked("game record", r => r.Method == "POST" && r.Url == Pool + "/v1/events" && r.Body.Length > 2);
            Talked("shared Battlegrounds game", r => r.Method == "POST" && r.Url == Pool + "/v1/games" && r.Body.Length > 2);
            Talked("shared Constructed picks", r => r.Method == "POST" && r.Url == Pool + "/v1/constructed" && r.Body.Length > 2);
            Talked("source capture", r => r.Method == "POST" && r.Url == Pool + "/v1/captures/chunks" && r.Body.Contains("gzip", StringComparison.Ordinal));
            Talked("profile upload", r => r.Method == "POST" && r.Url == Site + "tracker/events/batch" && r.Headers.Contains("access-token", StringComparison.Ordinal));
            Talked("crash report", r => r.Method == "POST" && r.Url == Site + "crash-reports" && r.Body.Contains("The collection view failed", StringComparison.Ordinal));

            // ---- The log names a read by its number of cards (CollectionWatch). A crash report is made from the exception
            // alone (CrashReport.Build reads no log), so that line is not in it: checked here on the report as sent.
            var counted = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Collection read from the client: {MarkerCount} cards");
            Assert.Contains(counted, TestDataFolder.ReadLog(), StringComparison.Ordinal);
            // The cosmetics' and the achievements' reads are logged as numbers only.
            Assert.Contains("Cosmetics read from the client: 1 card backs (1 owned), 0 coins (0 owned)", TestDataFolder.ReadLog(), StringComparison.Ordinal);
            Assert.Contains("Achievements read from the client: 1 achievements, 1 states, 1 completed", TestDataFolder.ReadLog(), StringComparison.Ordinal);
            Assert.DoesNotContain(Markers(TestDataFolder.ReadLog()), m => m.Contains("name", StringComparison.Ordinal));
            var crash = Assert.Single(sent, r => r.Url == Site + "crash-reports");
            Assert.DoesNotContain("Collection read", crash.Body, StringComparison.Ordinal);
            Assert.DoesNotContain(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{MarkerCount} cards"), crash.Body, StringComparison.Ordinal);
            Assert.DoesNotContain("HPFILL_", crash.Everything, StringComparison.Ordinal);
            Assert.All(sent, r => Assert.DoesNotContain("HPFILL_", r.Everything, StringComparison.Ordinal));

            // ---- And none of them said a word of the collection.
            Assert.All(sent, r => Assert.True(Markers(r.Everything).Count == 0, $"{r.Method} {r.Url} carries {string.Join(", ", Markers(r.Everything))}"));

            // What waits on disk to be sent (queues, spools, the crash folder) holds none either; the collection's own
            // file is the one place it is kept.
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                         .Where(p => !new[] { file, cosmeticsFile, achievementsFile }.Contains(p, StringComparer.OrdinalIgnoreCase)))
            {
                if (path.EndsWith(".db", StringComparison.OrdinalIgnoreCase) || path.Contains(".db-", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Assert.True(Markers(Unpacked(TryRead(path))).Count == 0, $"{Path.GetRelativePath(root, path)} holds a marker of the collection");
            }
        }
        finally
        {
            await services.SourceCaptures.DisposeAsync();
            services.GameStats.Dispose();
            services.Memory.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            TestDataFolder.TryDelete(root);
        }
    }

    /// <summary>The detector itself: a marker is found in an address, a header, a plain body, a gzip body and base64 of gzip.</summary>
    [Theory]
    [InlineData("card")]
    [InlineData("dust")]
    [InlineData("dust-grouped")]
    public async Task The_wire_check_finds_a_marker_wherever_a_request_could_carry_it(string kind)
    {
        var marker = kind switch
        {
            "card" => MarkerCard.ToLowerInvariant(),
            "dust" => MarkerDust.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => MarkerDust.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("ru-RU")),
        };
        var wire = new Wire();
        using var http = new HttpClient(wire);

        using (var inAddress = new HttpRequestMessage(HttpMethod.Get, "https://a.test/cards/" + Uri.EscapeDataString(marker) + ".png"))
        {
            await http.SendAsync(inAddress);
        }

        using (var inHeader = new HttpRequestMessage(HttpMethod.Get, "https://a.test/"))
        {
            inHeader.Headers.TryAddWithoutValidation("X-Thing", Uri.EscapeDataString(marker));
            await http.SendAsync(inHeader);
        }

        await http.PostAsync(new Uri("https://a.test/plain"), new StringContent("{\"x\":\"" + marker + "\"}"));
        using (var gzip = new ByteArrayContent(Gzip(Encoding.UTF8.GetBytes("{\"x\":\"" + marker + "\"}"))))
        {
            gzip.Headers.ContentEncoding.Add("gzip");
            await http.PostAsync(new Uri("https://a.test/gzip"), gzip);
        }

        await http.PostAsync(new Uri("https://a.test/base64"), new StringContent("{\"data\":\"" + Convert.ToBase64String(Gzip(Encoding.UTF8.GetBytes("line " + marker + " line"))) + "\"}"));
        await http.GetAsync(new Uri("https://a.test/clean?id=C_RARE&dust=1000"));

        Assert.Equal(new[] { true, true, true, true, true, false }, wire.Requests.Select(r => Markers(r.Everything).Count > 0));
    }

    /// <summary>
    /// The marker card and dust, the fixture's owned cards, and made-up cards up to <see cref="MarkerCount"/> in all:
    /// the number the log names for a read ("Collection read from the client: N cards").
    /// </summary>
    private static CardCollection Marked()
    {
        var cards = new Dictionary<string, OwnedCopies>(StringComparer.Ordinal)
        {
            [MarkerCard] = new(2, 0, 0),
            ["C_COMMON"] = new(2, 0, 0),
            ["C_RARE"] = new(1, 1, 0),
            ["CORE_ALIKE"] = new(1, 0, 0),
        };
        for (var i = 0; cards.Count < MarkerCount; i++)
        {
            cards["HPFILL_" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)] = new(1, 0, 0);
        }

        return new CardCollection(cards, MarkerDust, new DateTime(2026, 11, 14, 9, 30, 0, DateTimeKind.Utc));
    }

    /// <summary>One card back named <see cref="MarkerBack"/>, owned and favourite.</summary>
    private static CosmeticsCollection MarkedCosmetics(DateTime readAt) => new(
        [new CosmeticCardBack(3, MarkerBack, "Описание " + MarkerBack, CardBackCategory.Seasonal, 0)],
        new HashSet<int> { 3 }, new HashSet<int> { 3 }, [], new HashSet<int>(), new HashSet<int>(), readAt);

    /// <summary>One completed achievement named <see cref="MarkerAchievement"/>.</summary>
    private static Hearthpulse.Core.Progression.AchievementBook MarkedAchievements(DateTime readAt) => new(
        [new Hearthpulse.Core.Progression.AchievementCategory(1, "Общие", "general", 1)],
        [new Hearthpulse.Core.Progression.AchievementSubcategory(10, 1, "Прогресс", 1)],
        [new Hearthpulse.Core.Progression.AchievementSection(100, 10, string.Empty, 1)],
        new Dictionary<int, Hearthpulse.Core.Progression.AchievementDef>
        {
            [1] = new(1, 100, 1, MarkerAchievement, "Описание " + MarkerAchievement, false, 1, 10, 0),
        },
        new Dictionary<int, Hearthpulse.Core.Progression.AchievementState>
        {
            [1] = new(1, Hearthpulse.Core.Progression.AchievementStatus.Completed, 1, readAt),
        },
        new Dictionary<int, Hearthpulse.Core.Progression.AchievementGain>(),
        readAt);

    private static GameRecord Ranked() => new()
    {
        Key = "20261114120000|Player#1234|GT_RANKED",
        StartedAt = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc),
        EndedAt = new DateTime(2026, 11, 14, 12, 9, 0, DateTimeKind.Utc),
        Mode = "Ranked",
        GameType = "GT_RANKED",
        Format = "FT_STANDARD",
        PlayerName = "Player#1234",
        OpponentName = "Rival#5678",
        HeroCardId = "HERO_08",
        PlayerClass = "MAGE",
        OpponentClass = "ROGUE",
        Result = "Win",
        Turns = 5,
        WentFirst = true,
        DeckCode = CollectionFixture.NeedsDust,
    };

    private static GameOutcome Battlegrounds() =>
        new("Battlegrounds", "Win", 1, 7000, null, "BG_HERO_1", ["BG_HERO_1", "BG_HERO_2"])
        {
            Key = "20261114130000|Player#1234|GT_BATTLEGROUNDS",
            Details = new BgGameDetails(14, 6, 2, "BG_ANOMALY", ["BEAST", "DEMON"],
                [new BgMinion("BEAST_A", "A", 40, 30, true, 4, [])], ["TRK_1"], ["TRK_1"], [new TierStep(3, 2)],
                [new CombatRecord(2, 5, "BG_HERO_OTHER", "Win", 0, 3)]),
        };

    private static Exception Thrown(Exception ex)
    {
        try
        {
            throw ex;
        }
        catch (Exception caught)
        {
            return caught;
        }
    }

    private static Delegate[] Handlers(object source, string eventName) =>
        (source.GetType().GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(source) as Delegate)?.GetInvocationList() ?? [];

    private static byte[] Gzip(byte[] bytes)
    {
        using var packed = new MemoryStream();
        using (var gzip = new GZipStream(packed, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(bytes);
        }

        return packed.ToArray();
    }

    private static byte[] TryRead(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return copy.ToArray();
        }
        catch (IOException)
        {
            return [];
        }
    }

    /// <summary>The markers found in a text: the card id in any case, the dust number plain or with digit grouping.</summary>
    private static List<string> Markers(string text)
    {
        var found = new List<string>(4);
        if (text.Contains(MarkerCard, StringComparison.OrdinalIgnoreCase))
        {
            found.Add("the marker card id");
        }

        if (text.Contains(MarkerBack, StringComparison.OrdinalIgnoreCase))
        {
            found.Add("the marker card back name");
        }

        if (text.Contains(MarkerAchievement, StringComparison.OrdinalIgnoreCase))
        {
            found.Add("the marker achievement name");
        }

        // "918,273,645", "918 273 645" (plain, no-break and narrow no-break spaces), "918.273.645" and "918273645".
        var digits = MarkerDust.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (Grouping().Replace(text, string.Empty).Contains(digits, StringComparison.Ordinal))
        {
            found.Add("the marker dust balance");
        }

        return found;
    }

    /// <summary>
    /// Bytes as every text they may hold: as they are, percent-decoded, un-gzipped, and each long base64 run decoded
    /// (and un-gzipped): a source capture travels as base64 of gzip inside JSON.
    /// </summary>
    private static string Unpacked(byte[] bytes)
    {
        var all = new StringBuilder();
        void Add(byte[] raw, int depth)
        {
            var plain = Gunzip(raw) ?? raw;
            var text = Encoding.UTF8.GetString(plain);
            all.Append(text).Append('\n').Append(Uri.UnescapeDataString(text)).Append('\n');
            if (depth >= 2)
            {
                return;
            }

            foreach (Match run in Base64().Matches(text))
            {
                var value = run.Value;
                var buffer = new byte[value.Length];
                if (Convert.TryFromBase64String(value.PadRight(value.Length + ((4 - (value.Length % 4)) % 4), '='), buffer, out var written))
                {
                    Add(buffer[..written], depth + 1);
                }
            }
        }

        Add(bytes, 0);
        return all.ToString();
    }

    private static byte[]? Gunzip(byte[] bytes)
    {
        if (bytes.Length < 3 || bytes[0] != 0x1F || bytes[1] != 0x8B)
        {
            return null;
        }

        try
        {
            using var gzip = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
            using var plain = new MemoryStream();
            gzip.CopyTo(plain);
            return plain.ToArray();
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"(?<=\d)[ ,.  ](?=\d)", RegexOptions.CultureInvariant)]
    private static partial Regex Grouping();

    [GeneratedRegex(@"[A-Za-z0-9+/]{24,}={0,2}", RegexOptions.CultureInvariant)]
    private static partial Regex Base64();

    /// <summary>One request as it went out.</summary>
    /// <param name="Everything">The address, the headers and the body, each also decoded, for the marker search.</param>
    private sealed record Request(string Method, string Url, string Headers, string Body, string Everything);

    /// <summary>Records every request with its address, headers and body, and answers "unavailable": each sender has sent by then.</summary>
    private sealed class Wire : HttpMessageHandler
    {
        private readonly List<Request> _requests = [];

        public IReadOnlyList<Request> Requests
        {
            get
            {
                lock (_requests)
                {
                    return [.. _requests];
                }
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            var headers = new StringBuilder();
            foreach (var header in request.Headers.Concat(request.Content?.Headers.AsEnumerable() ?? []))
            {
                headers.Append(header.Key).Append(": ").AppendJoin(", ", header.Value).Append('\n');
            }

            var raw = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            var body = Unpacked(raw);
            var everything = string.Join('\n', url, Uri.UnescapeDataString(url), headers.ToString(), Uri.UnescapeDataString(headers.ToString()), body);
            lock (_requests)
            {
                _requests.Add(new Request(request.Method.Method, url, headers.ToString(), body, everything));
            }

            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }
    }
}
