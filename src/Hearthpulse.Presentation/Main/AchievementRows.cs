#if HEARTHPULSE_WPF
using Hearthpulse.App.Infrastructure;
#else
using Hearthpulse.UI.Infrastructure;
#endif
using System.Globalization;
using Hearthpulse.Core;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Progression;

#if HEARTHPULSE_WPF
namespace Hearthpulse.App.Main;
#else
namespace Hearthpulse.UI.Main;
#endif

// What both UIs show of the player's Hearthstone achievements (docs/achievements-page.md): the summary, the tracked
// chains, the chains close to done, the recent completions and the browse view. Built from AchievementBook; a view
// decides nothing itself. Names, descriptions, categories and sections are the client's texts, in the client's language.

/// <summary>Points and completed tiers of the whole book, then each category as a button that opens it below.</summary>
/// <param name="Points">The points earned, as a number.</param>
/// <param name="Of">"of 70,540 · 48 %".</param>
/// <param name="Share">Points earned over all points, 0..1.</param>
/// <param name="Completed">"3,098 of 5,183 completed" (tiers).</param>
/// <param name="GraphicsOpacity">The bars dim with a saved book; the text stays at full strength.</param>
public sealed record AchievementSummaryView(
    string Points, string Of, double Share, string Completed,
    IReadOnlyList<AchievementCategoryRow> Categories, double GraphicsOpacity) : IImmutableView
{
    public static AchievementSummaryView Empty { get; } = new(string.Empty, string.Empty, 0, string.Empty, [], 1);
}

/// <summary>A category of the summary: its glyph, the client's name, completed tiers and points.</summary>
/// <param name="Key">The category's id as text, the command parameter that opens it below.</param>
/// <param name="Line">"312 of 700 completed".</param>
/// <param name="Fraction">"4,210 / 9,800" points.</param>
/// <param name="Name">The row's accessible name: the category with its numbers.</param>
public sealed record AchievementCategoryRow(
    string Key, string Title, string Glyph, string Line, string Fraction, double Share, string Name, double GraphicsOpacity) : IImmutableView;

/// <summary>A subcategory in the browse view's table of contents: the client's name, completed tiers, chosen or not.</summary>
/// <param name="Count">"12 / 40" completed tiers.</param>
/// <param name="Name">The accessible name: "General: 12 of 40 completed".</param>
/// <param name="Chip">The compact layout's chip: "General · 12/40".</param>
public sealed record AchievementSubcategoryRow(
    string Key, string Title, string Count, double Share, bool IsSelected, string Name, string Chip, double GraphicsOpacity) : IImmutableView;

/// <summary>A section's heading as a row of the browse list, of the same height as a chain's row (the list panel takes one height).</summary>
public sealed record AchievementSectionRow(string Title) : IImmutableView;

/// <summary>A completed tier of "Recently completed" or the recap: name (or "Hidden achievement"), where, when, points.</summary>
/// <param name="Path">"Progress › General" (category › subcategory).</param>
/// <param name="Date">"November 18".</param>
/// <param name="Points">"+10 points".</param>
/// <param name="Tip">Everything as text: the row's help text.</param>
public sealed record AchievementRecentRow(string Title, bool Muted, string Path, string Date, string Points, string Tip) : IImmutableView;

/// <summary>
/// One chain of achievement tiers as a row: tracked, close to done, or in the browse list. Its texts are fixed for the
/// book it was made from; the star (<see cref="Tracked"/>, <see cref="StarEnabled"/>) changes in place, so a press keeps
/// the row and the focus on it.
/// </summary>
public sealed class AchievementRow : Observable
{
    /// <summary>The star in its two states (MDI star / star-outline).</summary>
    public const string StarOn = "\U000F04CE";

    public const string StarOff = "\U000F04D2";

    private bool _tracked;
    private bool _starEnabled = true;

    internal AchievementRow(AchievementChain chain, AchievementRowTexts texts, AchievementGain? gain, double graphicsOpacity)
    {
        HeadId = chain.HeadId;
        Key = chain.HeadId.ToString(CultureInfo.InvariantCulture);
        Secret = chain is { Hidden: true, Completed: false };
        var name = Secret || chain.Current.Name.Length == 0 ? Loc.T("Achievements.Hidden") : chain.Current.Name;
        Title = name;
        Muted = Secret || chain.Current.Name.Length == 0;
        Description = Secret ? Loc.T("Achievements.HiddenNote") : AchievementText.Describe(chain.Current);
        Level = chain.Tiers.Count > 1 ? Loc.F("Achievements.Level", Math.Min(chain.CompletedTiers + 1, chain.Tiers.Count), chain.Tiers.Count) : string.Empty;
        Path = texts.Path;
        Category = texts.Category;
        Done = chain.Completed;
        Binary = chain.Current.Quota <= 0;
        ShowsBar = !Secret && !Done && !Binary;
        Share = chain.Quota > 0 ? Math.Clamp((double)chain.Progress / chain.Quota, 0, 1) : 0;
        // Not done is never "100 %": the share is cut down to a whole percent (499 of 500 is 99 %), not rounded.
        Percent = (Done ? 1 : chain.Quota > 0 ? Math.Clamp((long)chain.Progress * 100 / chain.Quota, 0, 99) / 100.0 : 0)
            .ToString("P0", CultureInfo.CurrentCulture);
        ProgressText = Secret ? string.Empty
            : Done ? Loc.T(chain.Tiers.Count > 1 ? "Achievements.AllDone" : "Achievements.Done")
            : Binary ? Loc.T("Achievements.NotDone")
            : Loc.F("Achievements.Progress", chain.Progress, chain.Quota);
        Points = Secret ? string.Empty : Loc.P("Achievements.PointsCount", Done ? chain.PointsEarned : chain.Current.Points);
        // The row shows the current tier: a date belongs only to a chain done to its end (an earlier tier's date would
        // read as this tier's).
        Date = Done && chain.CompletedAt is { } at ? Loc.F("Achievements.CompletedOn", at.ToLocalTime()) : "—";
        GraphicsOpacity = graphicsOpacity;

        if (gain is not null)
        {
            GainValue = gain.Tiers > 0 ? Loc.P("Achievements.Gain.Tiers", gain.Tiers) : Loc.F("Achievements.Gain.Progress", gain.Progress);
            var when = When(gain.At);
            GainRest = Loc.F("Achievements.Gain.Line", string.Empty, when);
            GainLine = Loc.F("Achievements.Gain.Line", GainValue, when);
        }
        else
        {
            GainValue = string.Empty;
            GainRest = Loc.T("Achievements.Gain.None");
            GainLine = GainRest;
        }

        Detail = Level.Length > 0 ? Loc.F("Achievements.Detail", Level, Path) : Path;
        NearDetail = Level.Length > 0 ? Loc.F("Achievements.Detail", Level, Category) : Category;
        Tip = string.Join(Environment.NewLine, new[] { Title, Description, Level, ProgressText, Points, Done ? Date : string.Empty }
            .Where(line => line.Length > 0));
    }

    /// <summary>The chain's id: its first tier.</summary>
    public int HeadId { get; }

    /// <summary><see cref="HeadId"/> as text: the star's command parameter.</summary>
    public string Key { get; }

    /// <summary>A secret tier not done yet: no description, progress, points or star, as in the game.</summary>
    public bool Secret { get; }

    /// <summary>The current tier's name, or "Hidden achievement" for a secret one or one without a name.</summary>
    public string Title { get; }

    /// <summary>The name is the stand-in "Hidden achievement": shown muted.</summary>
    public bool Muted { get; }

    /// <summary>The current tier's description with its numbers filled in; for a secret one, "Revealed once you complete it".</summary>
    public string Description { get; }

    /// <summary>"Level 2 of 3", empty for a chain of one tier.</summary>
    public string Level { get; }

    /// <summary>"Collection › Outland" (category › subcategory; the category alone when they are named alike).</summary>
    public string Path { get; }

    /// <summary>The category's name.</summary>
    public string Category { get; }

    /// <summary>"Level 2 of 3 · Collection › Outland" (tracked rows).</summary>
    public string Detail { get; }

    /// <summary>"Level 2 of 3 · Gameplay" (close-to-done rows).</summary>
    public string NearDetail { get; }

    /// <summary>Every tier is completed.</summary>
    public bool Done { get; }

    /// <summary>The current tier has no quota: done or not, no bar.</summary>
    public bool Binary { get; }

    /// <summary>A progress bar is shown: not secret, not done, with a quota.</summary>
    public bool ShowsBar { get; }

    /// <summary>The current tier's progress over its quota, 0..1.</summary>
    public double Share { get; }

    /// <summary><see cref="Share"/> as a percentage ("92 %").</summary>
    public string Percent { get; }

    /// <summary>"34 of 50", "Completed", "All levels completed" or "Not completed"; empty for a secret tier.</summary>
    public string ProgressText { get; }

    /// <summary>"10 points": the current tier's, or the earned ones of a completed chain; empty for a secret tier.</summary>
    public string Points { get; }

    /// <summary>When the last completed tier was completed ("November 18, 2026"), "—" without one.</summary>
    public string Date { get; }

    /// <summary>The latest gain's number ("+7", "+1 level"), empty without a gain.</summary>
    public string GainValue { get; }

    /// <summary>After the number: " · today at 14:05"; without a gain "no change yet".</summary>
    public string GainRest { get; }

    /// <summary>The gain as one line, for the accessible name.</summary>
    public string GainLine { get; }

    public bool HasGain => GainValue.Length > 0;

    /// <summary>Bars and glyphs dim with a saved book (<see cref="DeckBuildView.SavedOpacity"/>); text does not.</summary>
    public double GraphicsOpacity { get; }

    /// <summary>The row's help text: name, description, level, progress, points and date in full.</summary>
    public string Tip { get; }

    /// <summary>
    /// A star is shown: every chain but a secret one not done yet, unless it is tracked (a tracked chain whose current
    /// tier turned secret keeps its star, so its place can be freed).
    /// </summary>
    public bool CanStar => !Secret || _tracked;

    /// <summary>The chain is tracked (its star is lit).</summary>
    public bool Tracked
    {
        get => _tracked;
        private set
        {
            if (Set(ref _tracked, value))
            {
                Raise(nameof(StarGlyph));
                Raise(nameof(CanStar));
                Raise(nameof(StarName));
                Raise(nameof(StarTip));
            }
        }
    }

    /// <summary>The star can be pressed: a tracked chain always, another one while fewer than the limit are tracked.</summary>
    public bool StarEnabled
    {
        get => _starEnabled;
        private set
        {
            if (Set(ref _starEnabled, value))
            {
                Raise(nameof(StarTip));
            }
        }
    }

    public string StarGlyph => _tracked ? StarOn : StarOff;

    /// <summary>The star's accessible name: "Track “Name”" / "Stop tracking “Name”".</summary>
    public string StarName => Loc.F(_tracked ? "Achievements.Star.Remove" : "Achievements.Star.Add", Title);

    /// <summary>The star's help text: its name, or why it cannot be pressed (shown on the disabled star too).</summary>
    public string StarTip => _starEnabled ? StarName : Loc.P("Achievements.Star.Limit", AppSettings.MaxTrackedAchievements);

    /// <summary>Sets the star's state; the view model calls it for every row it shows after a change.</summary>
    internal void SetStar(bool tracked, bool enabled)
    {
        Tracked = tracked;
        StarEnabled = tracked || enabled;
    }

    /// <summary>A gain's time as the row says it: today with the time, yesterday, a date (with the year when not this year).</summary>
    internal static string When(DateTime at)
    {
        var local = at.Kind == DateTimeKind.Local ? at : DateTime.SpecifyKind(at, DateTimeKind.Utc).ToLocalTime();
        var today = DateTime.Today;
        return local.Date == today ? Loc.F("Achievements.Gain.Today", local)
            : local.Date == today.AddDays(-1) ? Loc.T("Achievements.Gain.Yesterday")
            : local.Year == today.Year ? Loc.F("Achievements.Gain.Date", local)
            : Loc.F("Achievements.Gain.DateYear", local);
    }
}

/// <summary>Where a chain stands in the book, as the row says it.</summary>
internal readonly record struct AchievementRowTexts(string Category, string Path);
