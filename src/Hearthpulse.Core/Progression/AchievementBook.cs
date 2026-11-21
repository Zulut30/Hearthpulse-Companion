namespace Hearthpulse.Core.Progression;

/// <summary>
/// A Hearthstone achievement's state for the player. The client's <c>_Status</c> is mapped by declaration order of its
/// enum (UNKNOWN, ACTIVE, COMPLETED, REWARD_GRANTED, REWARD_ACKED, RESET; an assumption the read distribution agrees
/// with): 1 is <see cref="InProgress"/>, 2 to 4 <see cref="Completed"/>, anything else <see cref="NotStarted"/>.
/// </summary>
public enum AchievementStatus
{
    NotStarted,
    InProgress,
    Completed,
}

/// <summary>One of the client's achievement categories (five today). Names are in the client's language, as read.</summary>
/// <param name="Icon">The client's icon name ("general", "gameplay", "collection", "adventures", "game_modes").</param>
public sealed record AchievementCategory(int Id, string Name, string Icon, int SortOrder);

/// <summary>A subcategory of a category; a subcategory the client left unnamed carries its category's name.</summary>
public sealed record AchievementSubcategory(int Id, int CategoryId, string Name, int SortOrder);

/// <summary>A section of a subcategory; an empty name means the section has no heading.</summary>
/// <param name="SortOrder">The section's place within its subcategory.</param>
public sealed record AchievementSection(int Id, int SubcategoryId, string Name, int SortOrder);

/// <summary>
/// An achievement (one tier of a chain) as the client defines it. <paramref name="Description"/> holds the client's
/// markup (<c>$q</c>, <c>|4(…)</c>, <c>&lt;i&gt;</c>): show it through <see cref="AchievementText.Describe"/>.
/// </summary>
/// <param name="Hidden">The client keeps it secret until it is done (its name and description are then often empty).</param>
/// <param name="Quota">The progress that completes it; 0 or less: done or not, no progress.</param>
/// <param name="NextTierId">The next tier of its chain, 0 for none.</param>
public sealed record AchievementDef(int Id, int SectionId, int SortOrder, string Name, string Description,
                                    bool Hidden, int Quota, int Points, int NextTierId);

/// <summary>The player's state of one achievement, as read: progress may exceed the quota, a completion may have no date.</summary>
/// <param name="CompletedAt">When it was completed, UTC; null when not completed or the client has no date.</param>
public sealed record AchievementState(int Id, AchievementStatus Status, int Progress, DateTime? CompletedAt);

/// <summary>The last read that moved a chain on: new tiers completed (<paramref name="Tiers"/>), else more progress in the same tier.</summary>
/// <param name="At">The time of that read, UTC.</param>
public sealed record AchievementGain(int Tiers, int Progress, DateTime At);

/// <summary>
/// A chain of achievement tiers shown as one row, as the game shows it: the current tier is the first not completed
/// (the last when all are).
/// </summary>
/// <param name="HeadId">The chain's first tier: the chain's id (tracking, gains).</param>
/// <param name="Progress">The current tier's progress, at most its quota; with no quota 0 or 1.</param>
/// <param name="Quota">The current tier's quota; with none, 1.</param>
/// <param name="Hidden">The current tier is a secret one not completed yet: no description and no progress are shown.</param>
/// <param name="CompletedAt">When the last completed tier was completed (UTC), null without a completed tier or a date.</param>
/// <param name="PointsEarned">Points of the completed tiers.</param>
/// <param name="PointsTotal">Points of every tier.</param>
public sealed record AchievementChain(
    int HeadId, int CategoryId, int SubcategoryId, int SectionId,
    IReadOnlyList<AchievementDef> Tiers, int CompletedTiers,
    AchievementDef Current,
    int Progress, int Quota,
    bool Completed, bool Hidden,
    DateTime? CompletedAt,
    int PointsEarned, int PointsTotal);

/// <summary>A completed tier with a known date.</summary>
/// <param name="TierNumber">The tier's place in its chain, from 1.</param>
public sealed record CompletedAchievement(AchievementDef Achievement, int HeadId, int TierNumber, DateTime CompletedAt);

/// <summary>Points and completed tiers of one category.</summary>
public sealed record CategorySummary(AchievementCategory Category, int PointsEarned, int PointsTotal, int Completed, int Total);

/// <summary>Points and completed tiers of the whole book (every tier counts, as in the game's journal).</summary>
public sealed record AchievementSummary(int PointsEarned, int PointsTotal, int Completed, int Total, IReadOnlyList<CategorySummary> Categories);

/// <summary>
/// The client's achievements with the player's states (docs/collection-cosmetics.md, "Achievements"), read at
/// <see cref="ReadAt"/> (UTC). Holds only achievements that are enabled, not retired and in a section that belongs to a
/// subcategory: the game shows no other. The derived views (<see cref="Chains"/>, <see cref="Summary"/>) are worked out
/// once per book, on first use, and are safe to read from any thread.
/// </summary>
/// <param name="Achievements">By id; every tier of every chain.</param>
/// <param name="States">By achievement id; an achievement without one is not started.</param>
/// <param name="Gains">The last gain of each chain by its <see cref="AchievementChain.HeadId"/>.</param>
public sealed record AchievementBook(
    IReadOnlyList<AchievementCategory> Categories,
    IReadOnlyList<AchievementSubcategory> Subcategories,
    IReadOnlyList<AchievementSection> Sections,
    IReadOnlyDictionary<int, AchievementDef> Achievements,
    IReadOnlyDictionary<int, AchievementState> States,
    IReadOnlyDictionary<int, AchievementGain> Gains,
    DateTime ReadAt)
{
    private Derived? _derived;

    // A copy made by "with" works its views out again: they may depend on what was changed.
    private AchievementBook(AchievementBook original)
    {
        Categories = original.Categories;
        Subcategories = original.Subcategories;
        Sections = original.Sections;
        Achievements = original.Achievements;
        States = original.States;
        Gains = original.Gains;
        ReadAt = original.ReadAt;
        Confirmed = original.Confirmed;
    }

    /// <summary>
    /// True only for a book read from the client that is running now; false for the saved file and once that client was
    /// closed, restarted or sent to its login screen (as <c>CardCollection.Confirmed</c>).
    /// </summary>
    public bool Confirmed { get; init; }

    /// <summary>Every chain, one per first tier, in the client's order: category, subcategory, section, then the first tier's order.</summary>
    public IReadOnlyList<AchievementChain> Chains => View.Chains;

    /// <summary>Points and completed tiers, in all and by category (in the categories' order).</summary>
    public AchievementSummary Summary => View.Summary;

    private Derived View => LazyInitializer.EnsureInitialized(ref _derived, () => new Derived(this));

    /// <summary>The chain whose first tier is <paramref name="headId"/>, null when the book has none.</summary>
    public AchievementChain? Chain(int headId) => View.ByHead.GetValueOrDefault(headId);

    /// <summary>
    /// Chains close to done: not completed, not hidden, a quota of at least 2 and some progress, by the share done
    /// (largest first).
    /// </summary>
    public IReadOnlyList<AchievementChain> NearlyDone(int limit = 12) =>
        [.. Chains.Where(c => c is { Completed: false, Hidden: false, Quota: >= 2, Progress: > 0 })
            .OrderByDescending(c => (double)c.Progress / c.Quota)
            .ThenByDescending(c => c.Progress)
            .ThenBy(c => c.HeadId)
            .Take(Math.Max(0, limit))];

    /// <summary>Completed tiers with a date, newest first.</summary>
    public IReadOnlyList<CompletedAchievement> RecentlyCompleted(int limit = 12) => [.. View.Completed.Take(Math.Max(0, limit))];

    /// <summary>Completed tiers with a date in [<paramref name="fromUtc"/>, <paramref name="toUtc"/>), newest first.</summary>
    public IReadOnlyList<CompletedAchievement> CompletedBetween(DateTime fromUtc, DateTime toUtc)
    {
        var from = fromUtc.Kind == DateTimeKind.Local ? fromUtc.ToUniversalTime() : fromUtc;
        var to = toUtc.Kind == DateTimeKind.Local ? toUtc.ToUniversalTime() : toUtc;
        return [.. View.Completed.Where(c => c.CompletedAt >= from && c.CompletedAt < to)];
    }

    /// <summary>The achievement is completed: its status says so, whatever its progress.</summary>
    public bool IsCompleted(int achievementId) => States.TryGetValue(achievementId, out var state) && state.Status == AchievementStatus.Completed;

    /// <summary>
    /// The book from the client's rows, by the rules every reader shares (docs/collection-cosmetics.md): only enabled,
    /// not retired achievements in a section linked to a subcategory of a known category; the client's status and date
    /// numbers mapped (<see cref="StatusOf"/>, <see cref="TryCompletedAt"/>); states kept for the book's achievements only;
    /// an unnamed subcategory takes its category's name. Null when an id repeats, a date is out of range or anything is
    /// out of <see cref="AchievementLimits"/>: a reader then has no book rather than a part of one.
    /// </summary>
    public static AchievementBook? FromClient(
        IEnumerable<AchievementCategory> categories,
        IEnumerable<AchievementSubcategory> subcategories,
        IEnumerable<ClientSection> sections,
        IEnumerable<ClientSectionItem> sectionItems,
        IEnumerable<ClientAchievement> achievements,
        IEnumerable<ClientAchievementState> states,
        DateTime readAt)
    {
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(subcategories);
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(sectionItems);
        ArgumentNullException.ThrowIfNull(achievements);
        ArgumentNullException.ThrowIfNull(states);

        var categoryList = categories.OrderBy(c => c.SortOrder).ThenBy(c => c.Id).ToList();
        var categoryById = new Dictionary<int, AchievementCategory>();
        foreach (var category in categoryList)
        {
            if (!categoryById.TryAdd(category.Id, category))
            {
                return null;
            }
        }

        var subcategoryById = new Dictionary<int, AchievementSubcategory>();
        foreach (var subcategory in subcategories)
        {
            if (subcategoryById.ContainsKey(subcategory.Id))
            {
                return null;
            }

            if (categoryById.TryGetValue(subcategory.CategoryId, out var category))
            {
                subcategoryById[subcategory.Id] = subcategory.Name.Length > 0 ? subcategory : subcategory with { Name = category.Name };
            }
        }

        var sectionNames = new Dictionary<int, string>();
        foreach (var section in sections)
        {
            if (!sectionNames.TryAdd(section.Id, section.Name))
            {
                return null;
            }
        }

        var sectionById = new Dictionary<int, AchievementSection>();
        foreach (var item in sectionItems)
        {
            if (subcategoryById.ContainsKey(item.SubcategoryId) && sectionNames.TryGetValue(item.SectionId, out var name))
            {
                sectionById.TryAdd(item.SectionId, new AchievementSection(item.SectionId, item.SubcategoryId, name, item.SortOrder));
            }
        }

        var defs = new Dictionary<int, AchievementDef>();
        var seen = new HashSet<int>();
        foreach (var a in achievements)
        {
            if (!seen.Add(a.Id))
            {
                return null;
            }

            if (a.Enabled && !a.Retired && sectionById.ContainsKey(a.SectionId))
            {
                defs[a.Id] = new AchievementDef(a.Id, a.SectionId, a.SortOrder, a.Name, a.Description, a.Visibility != 0, a.Quota, a.Points, Math.Max(0, a.NextTierId));
            }
        }

        if (StatesOf(states, defs) is not { } stateById)
        {
            return null;
        }

        var subcategoryList = subcategoryById.Values.OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToList();
        var sectionList = sectionById.Values.OrderBy(s => s.SubcategoryId).ThenBy(s => s.SortOrder).ThenBy(s => s.Id).ToList();
        var book = new AchievementBook(categoryList, subcategoryList, sectionList, defs, stateById, new Dictionary<int, AchievementGain>(), readAt.ToUniversalTime());
        return AchievementLimits.Fits(book) ? book : null;
    }

    /// <summary>
    /// The same catalog with the player's states of a newer read (the catalog is read once per session of the client,
    /// the states after every game): the states of this book's achievements only, by the rules of
    /// <see cref="FromClient"/>; gains as this book has them (<see cref="WithGainsSince"/> works them out). Null when an
    /// id repeats or a date is out of range. Never <see cref="Confirmed"/>.
    /// </summary>
    public AchievementBook? WithClientStates(IEnumerable<ClientAchievementState> states, DateTime readAt)
    {
        ArgumentNullException.ThrowIfNull(states);
        if (StatesOf(states, Achievements) is not { } stateById)
        {
            return null;
        }

        var book = this with { States = stateById, ReadAt = readAt.ToUniversalTime(), Confirmed = false };
        return AchievementLimits.Fits(book) ? book : null;
    }

    /// <summary>
    /// This book with each chain's last gain since <paramref name="previous"/> (the book known before this read,
    /// docs/collection-cosmetics.md, "Gains"): a chain that completed tiers since then gains them
    /// (<see cref="AchievementGain.Tiers"/>), one that moved on in the same tier gains the difference
    /// (<see cref="AchievementGain.Progress"/>), both at this book's <see cref="ReadAt"/>; a chain that did not move on
    /// keeps the gain it had. Without a previous book, or for a chain it did not have, nothing is gained; when the previous
    /// book is another account's (<see cref="LostTiersSince"/>), nothing is gained and its gains are dropped.
    /// </summary>
    public AchievementBook WithGainsSince(AchievementBook? previous)
    {
        // Another account's or region's book (one account never loses tiers): nothing to count from.
        if (previous is null || LostTiersSince(previous))
        {
            return Gains.Count == 0 ? this : this with { Gains = new Dictionary<int, AchievementGain>() };
        }

        var gains = new Dictionary<int, AchievementGain>();
        foreach (var chain in Chains)
        {
            if (previous.Chain(chain.HeadId) is not { } before)
            {
                continue;
            }

            if (chain.CompletedTiers > before.CompletedTiers)
            {
                gains[chain.HeadId] = new AchievementGain(chain.CompletedTiers - before.CompletedTiers, 0, ReadAt);
            }
            else if (chain.CompletedTiers == before.CompletedTiers && chain.Current.Id == before.Current.Id && chain.Progress > before.Progress)
            {
                gains[chain.HeadId] = new AchievementGain(0, chain.Progress - before.Progress, ReadAt);
            }
            else if (previous.Gains.TryGetValue(chain.HeadId, out var kept))
            {
                gains[chain.HeadId] = kept;
            }
        }

        return this with { Gains = gains };
    }

    /// <summary>
    /// Some chain of this book has fewer completed tiers than in <paramref name="previous"/>: one account never loses a
    /// tier, so the two are books of different accounts or regions (the files name no account). Its gains are then not
    /// counted from <paramref name="previous"/> (<see cref="WithGainsSince"/>).
    /// </summary>
    public bool LostTiersSince(AchievementBook previous)
    {
        ArgumentNullException.ThrowIfNull(previous);
        return Chains.Any(chain => previous.Chain(chain.HeadId) is { } before && chain.CompletedTiers < before.CompletedTiers);
    }

    /// <summary>The states of the achievements in <paramref name="defs"/>; null when an id repeats or a date is out of range.</summary>
    private static Dictionary<int, AchievementState>? StatesOf(IEnumerable<ClientAchievementState> states, IReadOnlyDictionary<int, AchievementDef> defs)
    {
        var stateById = new Dictionary<int, AchievementState>();
        var seen = new HashSet<int>();
        foreach (var s in states)
        {
            if (!seen.Add(s.Id) || !TryCompletedAt(s.CompletedDate, out var completedAt))
            {
                return null;
            }

            if (defs.ContainsKey(s.Id))
            {
                stateById[s.Id] = new AchievementState(s.Id, StatusOf(s.Status), s.Progress, completedAt);
            }
        }

        return stateById;
    }

    /// <summary>The client's status number as a status (see <see cref="AchievementStatus"/>).</summary>
    public static AchievementStatus StatusOf(int clientStatus) => clientStatus switch
    {
        1 => AchievementStatus.InProgress,
        2 or 3 or 4 => AchievementStatus.Completed,
        _ => AchievementStatus.NotStarted,
    };

    /// <summary>
    /// The client's completion date (unix seconds, UTC; 0 = unknown) as a time: null for 0. False for a date outside
    /// 2000..2100, which no achievement has: the read is not trusted.
    /// </summary>
    public static bool TryCompletedAt(long unixSeconds, out DateTime? completedAt)
    {
        completedAt = null;
        if (unixSeconds == 0)
        {
            return true;
        }

        if (unixSeconds < AchievementLimits.EarliestSeconds || unixSeconds >= AchievementLimits.LatestSeconds)
        {
            return false;
        }

        completedAt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;
        return true;
    }

    /// <summary>The views of one book, worked out together once.</summary>
    private sealed class Derived
    {
        public Derived(AchievementBook book)
        {
            var sections = book.Sections.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
            var subcategories = book.Subcategories.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
            var categories = book.Categories.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
            var defs = book.Achievements;

            var pointed = defs.Values.Where(a => a.NextTierId != 0 && a.NextTierId != a.Id && defs.ContainsKey(a.NextTierId))
                .Select(a => a.NextTierId)
                .ToHashSet();
            var visited = new HashSet<int>();
            var chains = new List<AchievementChain>();
            var completed = new List<CompletedAchievement>();

            // First tiers first; what is left after them is a ring of tiers that point at each other, started at its lowest id.
            foreach (var start in defs.Values.Where(a => !pointed.Contains(a.Id)).OrderBy(a => a.Id)
                         .Concat(defs.Values.OrderBy(a => a.Id)))
            {
                if (visited.Contains(start.Id))
                {
                    continue;
                }

                var tiers = new List<AchievementDef>();
                for (var tier = start; tier is not null && visited.Add(tier.Id); tier = defs.GetValueOrDefault(tier.NextTierId))
                {
                    tiers.Add(tier);
                }

                var chain = ChainOf(book, tiers, sections, subcategories);
                chains.Add(chain);
                for (var i = 0; i < tiers.Count; i++)
                {
                    if (book.States.TryGetValue(tiers[i].Id, out var state) && state is { Status: AchievementStatus.Completed, CompletedAt: { } at })
                    {
                        completed.Add(new CompletedAchievement(tiers[i], chain.HeadId, i + 1, at));
                    }
                }
            }

            int Rank(Func<AchievementCategory, int> category, int id) => categories.TryGetValue(id, out var c) ? category(c) : int.MaxValue;
            Chains = [.. chains
                .OrderBy(c => Rank(x => x.SortOrder, c.CategoryId)).ThenBy(c => c.CategoryId)
                .ThenBy(c => subcategories.TryGetValue(c.SubcategoryId, out var s) ? s.SortOrder : int.MaxValue).ThenBy(c => c.SubcategoryId)
                .ThenBy(c => sections.TryGetValue(c.SectionId, out var s) ? s.SortOrder : int.MaxValue).ThenBy(c => c.SectionId)
                .ThenBy(c => c.Tiers[0].SortOrder).ThenBy(c => c.HeadId)];
            ByHead = Chains.ToDictionary(c => c.HeadId);
            Completed = [.. completed.OrderByDescending(c => c.CompletedAt).ThenBy(c => c.HeadId).ThenByDescending(c => c.TierNumber)];

            var byCategory = book.Categories.OrderBy(c => c.SortOrder).ThenBy(c => c.Id)
                .Select(category =>
                {
                    var tiers = Chains.Where(c => c.CategoryId == category.Id).SelectMany(c => c.Tiers).ToList();
                    return new CategorySummary(category,
                        tiers.Where(t => book.IsCompleted(t.Id)).Sum(t => t.Points), tiers.Sum(t => t.Points),
                        tiers.Count(t => book.IsCompleted(t.Id)), tiers.Count);
                })
                .ToList();
            var all = defs.Values.ToList();
            Summary = new AchievementSummary(
                all.Where(t => book.IsCompleted(t.Id)).Sum(t => t.Points), all.Sum(t => t.Points),
                all.Count(t => book.IsCompleted(t.Id)), all.Count, byCategory);
        }

        public IReadOnlyList<AchievementChain> Chains { get; }

        public IReadOnlyDictionary<int, AchievementChain> ByHead { get; }

        public IReadOnlyList<CompletedAchievement> Completed { get; }

        public AchievementSummary Summary { get; }

        private static AchievementChain ChainOf(
            AchievementBook book, List<AchievementDef> tiers,
            Dictionary<int, AchievementSection> sections, Dictionary<int, AchievementSubcategory> subcategories)
        {
            var done = tiers.Select(t => book.IsCompleted(t.Id)).ToList();
            var currentIndex = done.IndexOf(false);
            if (currentIndex < 0)
            {
                currentIndex = tiers.Count - 1;
            }

            var current = tiers[currentIndex];
            var currentDone = done[currentIndex];
            int progress, quota;
            if (current.Quota <= 0)
            {
                (progress, quota) = (currentDone ? 1 : 0, 1);
            }
            else
            {
                quota = current.Quota;
                progress = currentDone ? quota : Math.Clamp(book.States.GetValueOrDefault(current.Id)?.Progress ?? 0, 0, quota);
            }

            var lastDone = done.LastIndexOf(true);
            var head = tiers[0];
            var subcategoryId = sections.TryGetValue(head.SectionId, out var section) ? section.SubcategoryId : 0;
            var categoryId = subcategories.TryGetValue(subcategoryId, out var subcategory) ? subcategory.CategoryId : 0;
            return new AchievementChain(
                head.Id, categoryId, subcategoryId, head.SectionId,
                tiers, done.Count(d => d),
                current, progress, quota,
                done.TrueForAll(d => d), current.Hidden && !currentDone,
                lastDone < 0 ? null : book.States.GetValueOrDefault(tiers[lastDone].Id)?.CompletedAt,
                tiers.Where((_, i) => done[i]).Sum(t => t.Points), tiers.Sum(t => t.Points));
        }
    }
}

/// <summary>A row of the client's section table (<c>GameDbf.AchievementSection</c>) as read.</summary>
public sealed record ClientSection(int Id, string Name);

/// <summary>A row of the client's section item table (<c>GameDbf.AchievementSectionItem</c>): which subcategory a section is shown in.</summary>
public sealed record ClientSectionItem(int SubcategoryId, int SectionId, int SortOrder);

/// <summary>A row of the client's achievement table (<c>GameDbf.Achievement</c>) as read, before the book's rules.</summary>
/// <param name="Visibility">The client's <c>m_achievementVisibility</c>: 0 visible, anything else hidden.</param>
public sealed record ClientAchievement(int Id, int SectionId, int SortOrder, bool Enabled, bool Retired, string Name, string Description,
                                       int Visibility, int Quota, int Points, int NextTierId);

/// <summary>The player's state of an achievement as the client holds it (<c>PlayerAchievementState</c>).</summary>
/// <param name="CompletedDate">Unix seconds, UTC; 0 when unknown.</param>
public sealed record ClientAchievementState(int Id, int Status, int Progress, long CompletedDate);
