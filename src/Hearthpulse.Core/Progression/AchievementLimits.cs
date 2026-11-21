namespace Hearthpulse.Core.Progression;

/// <summary>The bounds an achievement book is held to, the same for a read of the client and the saved file.</summary>
internal static class AchievementLimits
{
    public const int MaxCategories = 1_000;

    public const int MaxSubcategories = 10_000;

    /// <summary>Far more sections, achievements or states than the game has (some thousands at most).</summary>
    public const int MaxEntries = 100_000;

    /// <summary>A name, icon or description longer than any the client has.</summary>
    public const int MaxText = 4_000;

    public const int MaxQuota = 1_000_000_000;

    public const int MaxPoints = 1_000_000;

    /// <summary>More tiers than a chain has, far below an overflow.</summary>
    public const int MaxGainTiers = 10_000;

    /// <summary>2000-01-01 and 2100-01-01 UTC as unix seconds: no achievement was completed outside them.</summary>
    public const long EarliestSeconds = 946_684_800;

    public const long LatestSeconds = 4_102_444_800;

    public static readonly DateTime Earliest = DateTimeOffset.FromUnixTimeSeconds(EarliestSeconds).UtcDateTime;

    public static readonly DateTime Latest = DateTimeOffset.FromUnixTimeSeconds(LatestSeconds).UtcDateTime;

    /// <summary>
    /// True when every list is within the bounds, ids are positive and do not repeat, each record points at one that
    /// exists (subcategory to category, section to subcategory, achievement to section, state and gain to achievement),
    /// every text is short enough and every time is within 2000..2100.
    /// </summary>
    public static bool Fits(AchievementBook book)
    {
        ArgumentNullException.ThrowIfNull(book);
        if (book.Categories.Count > MaxCategories || book.Subcategories.Count > MaxSubcategories || book.Sections.Count > MaxEntries
            || book.Achievements.Count > MaxEntries || book.States.Count > MaxEntries || book.Gains.Count > MaxEntries)
        {
            return false;
        }

        var categories = new HashSet<int>();
        if (!book.Categories.All(c => c.Id > 0 && categories.Add(c.Id) && Text(c.Name) && Text(c.Icon)))
        {
            return false;
        }

        var subcategories = new HashSet<int>();
        if (!book.Subcategories.All(s => s.Id > 0 && subcategories.Add(s.Id) && categories.Contains(s.CategoryId) && Text(s.Name)))
        {
            return false;
        }

        var sections = new HashSet<int>();
        if (!book.Sections.All(s => s.Id > 0 && sections.Add(s.Id) && subcategories.Contains(s.SubcategoryId) && Text(s.Name)))
        {
            return false;
        }

        return book.Achievements.All(p => p.Key == p.Value.Id && p.Key > 0 && sections.Contains(p.Value.SectionId)
                   && Text(p.Value.Name) && Text(p.Value.Description) && p.Value.Quota is >= -MaxQuota and <= MaxQuota
                   && p.Value.Points is >= 0 and <= MaxPoints && p.Value.NextTierId >= 0)
            && book.States.All(p => p.Key == p.Value.Id && book.Achievements.ContainsKey(p.Key) && Enum.IsDefined(p.Value.Status)
                   && (p.Value.CompletedAt is null || Time(p.Value.CompletedAt.Value)))
            && book.Gains.All(p => book.Achievements.ContainsKey(p.Key) && p.Value.Tiers is >= 0 and <= MaxGainTiers && Time(p.Value.At));
    }

    private static bool Text(string? text) => text is not null && text.Length <= MaxText;

    private static bool Time(DateTime time)
    {
        var utc = time.Kind == DateTimeKind.Local ? time.ToUniversalTime() : time;
        return utc >= Earliest && utc < Latest;
    }
}
