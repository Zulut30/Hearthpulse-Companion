#if HEARTHPULSE_WPF
using Hearthpulse.App.Infrastructure;
#else
using Hearthpulse.UI.Infrastructure;
#endif
using System.Globalization;
using Hearthpulse.Core;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Memory;
using Hearthpulse.Core.Progression;

#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Main;
#else
namespace Hearthpulse.UI.Main;
#endif

/// <summary>
/// The achievements page (docs/achievements-page.md): the summary of points and completed tiers, the tracked chains
/// with their latest gain, the chains close to done, the recent completions and the browse view of one subcategory at a
/// time. The book comes from the host (<see cref="SetAchievements"/>); its views are worked out off the UI thread, only
/// once the page has been shown, and a newer book wins over an older one still being worked out.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>How many chains "Nearly done" and "Recently completed" show.</summary>
    internal const int AchievementListLength = 12;

    private const string GlyphConfirmed = "\U000F05E0";
    private const string GlyphSaved = "\U000F02DA";
    private const string GlyphUnsupported = "\U000F002A";
    private const string GlyphNone = "\U000F0538";
    private const string GlyphWaiting = "\U000F051F";
    private const string GlyphFailed = "\U000F05D6";

    private AchievementBook? _achievementBook;
    private bool _achievementsReadable = HearthstoneMemory.IsSupported;
    private AchievementsData? _achievementsData;
    private bool _achievementsWanted;
    private bool _achievementsFailed;
    private int _achievementsBuild;
    private int _achievementCategory;
    private int _achievementSubcategory;
    private AchievementSummaryView _achievementSummary = AchievementSummaryView.Empty;
    private IReadOnlyList<AchievementRow> _nearlyDoneRows = [];
    private IReadOnlyList<AchievementRecentRow> _recentAchievementRows = [];
    private IReadOnlyList<MetaChoice> _achievementCategories = [];
    private SyncList<AchievementSubcategoryRow> _achievementSubcategories = [];
    private IReadOnlyList<object> _achievementBrowseRows = [];
    private int _trackedMissing;
    private RelayCommand? _toggleAchievementStar;
    private RelayCommand? _showAchievementCategory;
    private RelayCommand? _showAchievementSubcategory;
    private RelayCommand? _retryAchievements;

    /// <summary>
    /// Whether this system can read achievements from the game client at all (Windows). The host leaves the default; a
    /// test sets it to see the other system's empty state.
    /// </summary>
    public bool AchievementsReadable
    {
        get => _achievementsReadable;
        set
        {
            if (Set(ref _achievementsReadable, value))
            {
                RaiseAchievementsStatus();
            }
        }
    }

    /// <summary>The book the page shows (render scenes and tests).</summary>
    internal AchievementBook? AchievementBookShown => _achievementBook;

    /// <summary>The work of the last change of book or page; a render scene waits for it.</summary>
    internal Task AchievementsWork { get; private set; } = Task.CompletedTask;

    /// <summary>Writes the settings once a star changed them (<see cref="AppSettings.Save()"/>); a test gives a file of its own.</summary>
    internal Action<AppSettings> SaveSettings { get; init; } = static settings => settings.Save();

    /// <summary>
    /// The newest known achievement book, from the host on the UI thread (it listens to
    /// <c>GameSession.AchievementsChanged</c> and reads <c>GameSession.Achievements</c> after dispatching). Null: none known.
    /// </summary>
    public void SetAchievements(AchievementBook? book)
    {
        if (ReferenceEquals(book, _achievementBook))
        {
            return;
        }

        _achievementBook = book;
        _achievementsFailed = false;
        RaiseAchievementsStatus();
        WrappedBookChanged();
        if (_achievementsWanted)
        {
            BuildAchievements();
        }
        else
        {
            // Nothing is worked out for a page nobody opened; the next opening starts from this book.
            _achievementsData = null;
        }
    }

    /// <summary>A book is known, read from the game now or saved earlier.</summary>
    public bool HasAchievementBook => _achievementBook is not null;

    /// <summary>The known book is a saved one: its numbers are marked and its graphics dimmed.</summary>
    public bool AchievementsSaved => _achievementBook is { Confirmed: false };

    /// <summary>
    /// The status line (design §0, <see cref="LibraryStatus"/>): read from the game today at 14:05, or saved data of a
    /// date with the next step and the "from saved data" mark. Null without a book: the page's empty state says why.
    /// </summary>
    public LibraryStatus? AchievementsStatusLine
    {
        get
        {
            if (_achievementBook is not { } book)
            {
                return null;
            }

            var read = book.ReadAt.Kind == DateTimeKind.Local ? book.ReadAt : DateTime.SpecifyKind(book.ReadAt, DateTimeKind.Utc).ToLocalTime();
            if (book.Confirmed)
            {
                return new LibraryStatus(GlyphConfirmed,
                    Loc.F("Achievements.Status.Confirmed", Loc.F(read.Date == DateTime.Today ? "Collection.When.Today" : "Collection.When.Date", read)),
                    string.Empty, string.Empty);
            }

            // Saved data is always named with its full date: it is "as of then".
            return new LibraryStatus(GlyphSaved,
                Loc.F(!_achievementsReadable ? "Achievements.Status.SavedUnsupported" : AchievementsReadOff ? "Achievements.Status.SavedOff"
                    : HearthstoneRunning ? "Achievements.Status.SavedNotRead" : "Achievements.Status.Saved",
                    Loc.F("Collection.When.Date", read)),
                Loc.T("Achievements.Mark.Saved"), Loc.T("Achievements.Mark.SavedTip"));
        }
    }

    /// <summary>The page's blocks are shown: the book's views are worked out.</summary>
    public bool AchievementsShown => _achievementsData is not null;

    /// <summary>Bars and glyphs of a saved book are dimmed (<see cref="DeckBuildView.SavedOpacity"/>); text stays readable.</summary>
    public double AchievementsGraphicsOpacity => AchievementsSaved ? DeckBuildView.SavedOpacity : 1;

    /// <summary>Points and completed tiers in all and by category.</summary>
    public AchievementSummaryView AchievementSummary { get => _achievementSummary; private set => Set(ref _achievementSummary, value); }

    /// <summary>The tracked chains in the order they were added; a star press adds or removes one row in place.</summary>
    public SyncList<AchievementRow> TrackedAchievementRows { get; } = [];

    /// <summary>"3 of 10": chains tracked of the limit (the ones missing from the book count too: they stay in the settings).</summary>
    public string TrackedAchievementsCount => Loc.F("Achievements.Tracked.Count", Settings.TrackedAchievements.Count, AppSettings.MaxTrackedAchievements);

    /// <summary>The limit is reached: the other stars are off, and this line says why.</summary>
    public bool TrackedAchievementsFull => Settings.TrackedAchievements.Count >= AppSettings.MaxTrackedAchievements;

    public string TrackedAchievementsFullText => Loc.F("Achievements.Tracked.Full", AppSettings.MaxTrackedAchievements);

    /// <summary>"1 more tracked chain isn't in the journal…": tracked chains the book does not have.</summary>
    public string TrackedAchievementsMissing => _trackedMissing > 0 ? Loc.P("Achievements.Tracked.Missing", _trackedMissing) : string.Empty;

    public bool HasTrackedAchievementsMissing => _trackedMissing > 0;

    /// <summary>Nothing tracked: the block says how to track a chain.</summary>
    public bool TrackedAchievementsEmpty => Settings.TrackedAchievements.Count == 0;

    /// <summary>Chains close to done, the largest share first.</summary>
    public IReadOnlyList<AchievementRow> NearlyDoneRows { get => _nearlyDoneRows; private set => Set(ref _nearlyDoneRows, value); }

    public bool NearlyDoneEmpty => _nearlyDoneRows.Count == 0;

    /// <summary>Tiers completed most recently, newest first.</summary>
    public IReadOnlyList<AchievementRecentRow> RecentAchievementRows { get => _recentAchievementRows; private set => Set(ref _recentAchievementRows, value); }

    public bool RecentAchievementsEmpty => _recentAchievementRows.Count == 0;

    /// <summary>The categories of the browse view, the chosen one marked (keys are the categories' ids).</summary>
    public IReadOnlyList<MetaChoice> AchievementCategories { get => _achievementCategories; private set => Set(ref _achievementCategories, value); }

    /// <summary>The chosen category's subcategories, the chosen one marked.</summary>
    public IReadOnlyList<AchievementSubcategoryRow> AchievementSubcategories => _achievementSubcategories;

    /// <summary>The chosen subcategory's rows: a heading for each named section, then its chains (one height for all).</summary>
    public IReadOnlyList<object> AchievementBrowseRows { get => _achievementBrowseRows; private set => Set(ref _achievementBrowseRows, value); }

    public bool AchievementBrowseEmpty => _achievementBrowseRows.Count == 0;

    /// <summary>Tracks the chain whose key (head id) is the parameter, or stops tracking it; saves the settings.</summary>
    public RelayCommand ToggleAchievementStar => _toggleAchievementStar ??= new RelayCommand(p =>
    {
        if (KeyOf(p) is { } head)
        {
            ToggleAchievement(head);
        }
    });

    /// <summary>Shows the category whose key is the parameter in the browse view, at its first subcategory.</summary>
    public RelayCommand ShowAchievementCategory => _showAchievementCategory ??= new RelayCommand(p =>
    {
        if (KeyOf(p) is { } id && id != _achievementCategory)
        {
            _achievementCategory = id;
            _achievementSubcategory = 0;
            ShowAchievementBrowse();
        }
    });

    /// <summary>Shows the subcategory whose key is the parameter in the browse view.</summary>
    public RelayCommand ShowAchievementSubcategory => _showAchievementSubcategory ??= new RelayCommand(p =>
    {
        if (KeyOf(p) is { } id && id != _achievementSubcategory)
        {
            _achievementSubcategory = id;
            ShowAchievementBrowse();
        }
    });

    /// <summary>The chosen category's id (0 before the page was built).</summary>
    internal int ShownAchievementCategory => _achievementCategory;

    /// <summary>The chosen subcategory's id (0 before the page was built).</summary>
    internal int ShownAchievementSubcategory => _achievementSubcategory;

    /// <summary>
    /// Tracks <paramref name="headId"/> or stops tracking it and saves the settings at once. A chain is added only while
    /// fewer than the limit are tracked and when its current tier is not a secret one (the game shows nothing of it).
    /// </summary>
    internal void ToggleAchievement(int headId)
    {
        var tracked = Settings.TrackedAchievements;
        if (tracked.Contains(headId))
        {
            Settings.TrackedAchievements = [.. tracked.Where(id => id != headId)];
        }
        else if (tracked.Count < AppSettings.MaxTrackedAchievements && _achievementBook?.Chain(headId) is { } chain
                 && chain is not { Hidden: true, Completed: false })
        {
            Settings.TrackedAchievements = [.. tracked, headId];
        }
        else
        {
            return;
        }

        SaveSettings(Settings);
        ShowTrackedAchievements(reuse: true);
        UpdateAchievementStars();
    }

    // ---- hooks of the library pages (MainViewModel.LibraryPages.cs) ---------------------------------------------------

    partial void AchievementsPageShown()
    {
        _achievementsWanted = true;
        if (_achievementBook is not null && !ReferenceEquals(_achievementsData?.Book, _achievementBook))
        {
            BuildAchievements();
        }
    }

    partial void RebuildAchievementsPage()
    {
        if (_achievementsData is not null)
        {
            ShowAchievementRows();
        }

        RaiseAchievementsStatus();
    }

    partial void HearthstoneRunningChanged()
    {
        RaiseAchievementsStatus();
    }

    // ---- building -----------------------------------------------------------------------------------------------------

    private void BuildAchievements()
    {
        var build = ++_achievementsBuild;
        _achievementsFailed = false;
        if (_achievementBook is not { } book)
        {
            _achievementsData = null;
            ShowAchievementRows();
            RaiseAchievementsStatus();
            return;
        }

        // A retry turns the error back into "loading" at once.
        RaiseAchievementsStatus();
        AchievementsWork = BuildAchievementsAsync(book, build);
    }

    private async Task BuildAchievementsAsync(AchievementBook book, int build)
    {
        try
        {
            // The chains and their sums are worked out once per book, on first use; a journal of thousands of tiers
            // is not something to do on the UI thread.
            var data = await Background.RunAsync(() => AchievementsData.Of(book)).ConfigureAwait(true);
            if (build != _achievementsBuild)
            {
                return;
            }

            _achievementsData = data;
            ShowAchievementRows();
            RaiseAchievementsStatus();
        }
        catch (Exception ex)
        {
            // Any failure, not only the expected kinds: without a result the page would say "loading" for good.
            Diag.Error("Achievements page not built", ex);
            if (build == _achievementsBuild)
            {
                _achievementsFailed = true;
                RaiseAchievementsStatus();
            }
        }
    }

    /// <summary>Every block from the worked-out data, in the current language.</summary>
    private void ShowAchievementRows()
    {
        if (_achievementsData is not { } data)
        {
            AchievementSummary = AchievementSummaryView.Empty;
            NearlyDoneRows = [];
            RecentAchievementRows = [];
            AchievementCategories = [];
            _achievementSubcategories.Clear();
            AchievementBrowseRows = [];
            TrackedAchievementRows.Sync([]);
            _trackedMissing = 0;
            RaiseAchievementBlocks();
            return;
        }

        var opacity = AchievementsGraphicsOpacity;
        var summary = data.Book.Summary;
        AchievementSummary = new AchievementSummaryView(
            summary.PointsEarned.ToString("N0", CultureInfo.CurrentCulture),
            Loc.F("Achievements.PointsOf", summary.PointsTotal, AchShare(summary.PointsEarned, summary.PointsTotal).ToString("P0", CultureInfo.CurrentCulture)),
            AchShare(summary.PointsEarned, summary.PointsTotal),
            Loc.F("Achievements.Completed", summary.Completed, summary.Total),
            [.. summary.Categories.Select(c => new AchievementCategoryRow(
                c.Category.Id.ToString(CultureInfo.InvariantCulture),
                c.Category.Name,
                CategoryGlyph(c.Category.Icon),
                Loc.F("Achievements.Category.Line", c.Completed, c.Total),
                Loc.F("Achievements.Fraction", c.PointsEarned, c.PointsTotal),
                AchShare(c.PointsEarned, c.PointsTotal),
                Loc.F("Achievements.Category.A11y", c.Category.Name, c.PointsEarned, c.PointsTotal, c.Completed, c.Total),
                opacity))],
            opacity);

        NearlyDoneRows = [.. data.Nearly.Select(c => Row(data, c))];
        RecentAchievementRows = [.. data.Recent.Select(c => RecentRow(data, c))];
        ShowTrackedAchievements(reuse: false);
        ShowAchievementBrowse();
    }

    private void ShowTrackedAchievements(bool reuse)
    {
        var rows = new List<AchievementRow>();
        var missing = 0;
        if (_achievementsData is { } data)
        {
            foreach (var head in Settings.TrackedAchievements)
            {
                if (data.Book.Chain(head) is not { } chain)
                {
                    missing++;
                    continue;
                }

                rows.Add((reuse ? TrackedAchievementRows.FirstOrDefault(r => r.HeadId == head) : null) ?? Row(data, chain));
            }
        }

        TrackedAchievementRows.Sync(rows);
        _trackedMissing = missing;
        Raise(nameof(TrackedAchievementsCount));
        Raise(nameof(TrackedAchievementsFull));
        Raise(nameof(TrackedAchievementsMissing));
        Raise(nameof(HasTrackedAchievementsMissing));
        Raise(nameof(TrackedAchievementsEmpty));
    }

    /// <summary>The browse view: the categories, the chosen one's subcategories, and the chosen subcategory's rows.</summary>
    private void ShowAchievementBrowse()
    {
        if (_achievementsData is not { } data)
        {
            return;
        }

        var categories = data.Book.Categories;
        if (!categories.Any(c => c.Id == _achievementCategory))
        {
            _achievementCategory = categories.Count > 0 ? categories[0].Id : 0;
            _achievementSubcategory = 0;
        }

        var subcategories = data.Book.Subcategories.Where(s => s.CategoryId == _achievementCategory).ToList();
        if (!subcategories.Exists(s => s.Id == _achievementSubcategory))
        {
            _achievementSubcategory = subcategories.Count > 0 ? subcategories[0].Id : 0;
        }

        var opacity = AchievementsGraphicsOpacity;
        AchievementCategories = [.. categories.Select(c => new MetaChoice(c.Id.ToString(CultureInfo.InvariantCulture), c.Name, c.Id == _achievementCategory))];
        var choices = subcategories.Select(s =>
        {
            var (completed, total) = data.Counts.GetValueOrDefault(s.Id);
            return new AchievementSubcategoryRow(
                s.Id.ToString(CultureInfo.InvariantCulture),
                s.Name,
                Loc.F("Achievements.Fraction", completed, total),
                AchShare(completed, total),
                s.Id == _achievementSubcategory,
                Loc.F("Achievements.Browse.SubA11y", s.Name, completed, total),
                Loc.F("Achievements.Browse.Chip", s.Name, completed, total),
                opacity);
        }).ToList();
        // A different category replaces the whole menu. Within one category only the two selection marks move.
        if (choices.Count > 0 && (_achievementSubcategories.Count == 0 || _achievementSubcategories[0].Key != choices[0].Key))
        {
            _achievementSubcategories = new SyncList<AchievementSubcategoryRow>(choices);
            Raise(nameof(AchievementSubcategories));
        }
        else
        {
            _achievementSubcategories.Sync(choices);
        }

        var rows = new List<object>();
        if (data.BySubcategory.TryGetValue(_achievementSubcategory, out var chains))
        {
            var section = -1;
            foreach (var chain in chains)
            {
                if (chain.SectionId != section)
                {
                    section = chain.SectionId;
                    if (data.Sections.TryGetValue(section, out var s) && s.Name.Length > 0)
                    {
                        rows.Add(new AchievementSectionRow(s.Name));
                    }
                }

                rows.Add(Row(data, chain));
            }
        }

        AchievementBrowseRows = rows;
        UpdateAchievementStars();
        RaiseAchievementBlocks();
    }

    /// <summary>Lights the stars of the tracked chains and turns the others off at the limit, in every block, in place.</summary>
    private void UpdateAchievementStars()
    {
        var tracked = Settings.TrackedAchievements.ToHashSet();
        var room = tracked.Count < AppSettings.MaxTrackedAchievements;
        foreach (var row in TrackedAchievementRows.Concat(_nearlyDoneRows).Concat(_achievementBrowseRows.OfType<AchievementRow>()))
        {
            row.SetStar(tracked.Contains(row.HeadId), room);
        }
    }

    private AchievementRow Row(AchievementsData data, AchievementChain chain) =>
        new(chain, data.Texts(chain.CategoryId, chain.SubcategoryId), data.Book.Gains.GetValueOrDefault(chain.HeadId), AchievementsGraphicsOpacity);

    private static AchievementRecentRow RecentRow(AchievementsData data, CompletedAchievement done)
    {
        var chain = data.Book.Chain(done.HeadId);
        var texts = chain is null ? default : data.Texts(chain.CategoryId, chain.SubcategoryId);
        var muted = done.Achievement.Name.Length == 0;
        var title = muted ? Loc.T("Achievements.Hidden") : done.Achievement.Name;
        var local = done.CompletedAt.ToLocalTime();
        var date = Loc.F(local.Year == DateTime.Today.Year ? "Achievements.Gain.Date" : "Achievements.Gain.DateYear", local);
        var points = Loc.P("Wrapped.Ach.Points", done.Achievement.Points);
        return new AchievementRecentRow(title, muted, texts.Path ?? string.Empty, date, points,
            string.Join(Environment.NewLine, new[] { title, texts.Path ?? string.Empty, Loc.F("Achievements.CompletedOn", local), points }.Where(l => l.Length > 0)));
    }

    private void RaiseAchievementsStatus()
    {
        Raise(nameof(HasAchievementBook));
        Raise(nameof(AchievementsSaved));
        Raise(nameof(AchievementsStatusLine));
        Raise(nameof(AchievementsEmpty));
        Raise(nameof(AchievementsEmptyGlyph));
        Raise(nameof(AchievementsEmptyShown));
        Raise(nameof(AchievementsFailed));
        Raise(nameof(AchievementsShown));
        Raise(nameof(AchievementsGraphicsOpacity));
    }

    private void RaiseAchievementBlocks()
    {
        Raise(nameof(NearlyDoneEmpty));
        Raise(nameof(RecentAchievementsEmpty));
        Raise(nameof(AchievementBrowseEmpty));
    }

    /// <summary>The MDI glyph of a category by the client's icon name; a trophy for one this list does not know.</summary>
    internal static string CategoryGlyph(string icon) => icon switch
    {
        "general" => "\U000F01A5",
        "gameplay" => "\U000F0787",
        "collection" => "\U000F0639",
        "adventures" => "\U000F034D",
        "game_modes" => "\U000F0297",
        _ => "\U000F0538",
    };

    private static double AchShare(long part, long whole) => whole > 0 ? Math.Clamp((double)part / whole, 0, 1) : 0;

    private static int? KeyOf(object? parameter) => parameter switch
    {
        int id => id,
        string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) => id,
        AchievementRow row => row.HeadId,
        _ => null,
    };

    /// <summary>What the page needs of one book, worked out off the UI thread; texts are made from it on the UI thread.</summary>
    private sealed class AchievementsData
    {
        private AchievementsData(AchievementBook book)
        {
            Book = book;
            Categories = book.Categories.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
            Subcategories = book.Subcategories.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
            Sections = book.Sections.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
            BySubcategory = book.Chains.GroupBy(c => c.SubcategoryId).ToDictionary(g => g.Key, g => (IReadOnlyList<AchievementChain>)[.. g]);
            Counts = BySubcategory.ToDictionary(p => p.Key, p => (p.Value.Sum(c => c.CompletedTiers), p.Value.Sum(c => c.Tiers.Count)));
            Nearly = book.NearlyDone(AchievementListLength);
            Recent = book.RecentlyCompleted(AchievementListLength);
            _ = book.Summary;
        }

        public AchievementBook Book { get; }

        public IReadOnlyDictionary<int, AchievementCategory> Categories { get; }

        public IReadOnlyDictionary<int, AchievementSubcategory> Subcategories { get; }

        public IReadOnlyDictionary<int, AchievementSection> Sections { get; }

        /// <summary>Each subcategory's chains in the book's order (by section, then the first tier's order).</summary>
        public IReadOnlyDictionary<int, IReadOnlyList<AchievementChain>> BySubcategory { get; }

        /// <summary>Completed and all tiers of each subcategory.</summary>
        public IReadOnlyDictionary<int, (int Completed, int Total)> Counts { get; }

        public IReadOnlyList<AchievementChain> Nearly { get; }

        public IReadOnlyList<CompletedAchievement> Recent { get; }

        public static AchievementsData Of(AchievementBook book) => new(book);

        /// <summary>A chain's category and "category › subcategory" (the category alone when the two are named alike).</summary>
        public AchievementRowTexts Texts(int categoryId, int subcategoryId)
        {
            var category = Categories.GetValueOrDefault(categoryId)?.Name ?? string.Empty;
            var subcategory = Subcategories.GetValueOrDefault(subcategoryId)?.Name ?? string.Empty;
            var path = subcategory.Length == 0 || string.Equals(subcategory, category, StringComparison.Ordinal) ? category
                : category.Length == 0 ? subcategory
                : Loc.F("Achievements.Path", category, subcategory);
            return new AchievementRowTexts(category, path);
        }
    }
}
