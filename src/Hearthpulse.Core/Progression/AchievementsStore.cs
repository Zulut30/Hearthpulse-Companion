using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace Hearthpulse.Core.Progression;

/// <summary>
/// The last achievement book read from the client, in one JSON file of the data folder (<c>achievements.json</c>): the
/// catalog exists nowhere but in the client, so without the file the page is empty while the game is closed. Local
/// only (docs/collection-cosmetics.md), the same rules as the collection's store: a file that is missing, damaged, out
/// of bounds or of another format version is "none yet", never repaired; the next read of the client replaces it. What
/// <see cref="Load"/> returns is never <see cref="AchievementBook.Confirmed"/>. The file holds the catalog, the
/// player's states and the chains' last gains, no account, name or region.
/// </summary>
public sealed class AchievementsStore
{
    /// <summary>The file's format; a file of another version is not read.</summary>
    public const int FormatVersion = 1;

    // The file is a megabyte or two (thousands of descriptions); anything far larger is not ours.
    private const long MaxBytes = 32 * 1024 * 1024;

    // By name exactly: Enum.TryParse would also take numbers.
    private static readonly Dictionary<string, AchievementStatus> Statuses =
        Enum.GetValues<AchievementStatus>().ToDictionary(s => s.ToString(), StringComparer.Ordinal);

    // Names and descriptions in the client's language stay readable (and smaller) instead of escaped character by character.
    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) };

    private readonly string _path;

    public AchievementsStore(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _path = path;
    }

    /// <summary>The file a save is written to before it is moved over the saved one.</summary>
    internal string TempPath => _path + ".tmp";

    /// <summary>The saved book, or null when there is none that can be read. Never throws.</summary>
    public AchievementBook? Load()
    {
        try
        {
            var file = new FileInfo(_path);
            if (!file.Exists || file.Length > MaxBytes)
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllBytes(_path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("version", out var version) || !version.TryGetInt32(out var number) || number != FormatVersion
                || !Json.TryTime(root, "readAt", out var readAt)
                || Rows(root, "categories", AchievementLimits.MaxCategories, Category) is not { } categories
                || Rows(root, "subcategories", AchievementLimits.MaxSubcategories, Subcategory) is not { } subcategories
                || Rows(root, "sections", AchievementLimits.MaxEntries, Section) is not { } sections
                || Rows(root, "achievements", AchievementLimits.MaxEntries, Achievement) is not { } achievements
                || Rows(root, "states", AchievementLimits.MaxEntries, State) is not { } states
                || Rows(root, "gains", AchievementLimits.MaxEntries, Gain) is not { } gains)
            {
                return null;
            }

            var defs = new Dictionary<int, AchievementDef>(achievements.Count);
            var stateById = new Dictionary<int, AchievementState>(states.Count);
            var gainByHead = new Dictionary<int, AchievementGain>(gains.Count);
            if (!achievements.TrueForAll(a => defs.TryAdd(a.Id, a)) || !states.TrueForAll(s => stateById.TryAdd(s.Id, s))
                || !gains.TrueForAll(g => gainByHead.TryAdd(g.HeadId, g.Gain)))
            {
                return null;
            }

            var book = new AchievementBook(categories, subcategories, sections, defs, stateById, gainByHead, readAt);
            return AchievementLimits.Fits(book) ? book : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException
                                       or FormatException or NotSupportedException or ArgumentException or System.Security.SecurityException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes the book over the saved one through a temp file beside it and a move, so a crash or a full disk leaves the
    /// old file whole. Throws what the file system throws; the caller logs it and tries again. Not for two writers at once.
    /// </summary>
    public void Save(AchievementBook book)
    {
        ArgumentNullException.ThrowIfNull(book);
        if (Path.GetDirectoryName(_path) is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
        }

        try
        {
            using (var stream = File.Create(TempPath))
            using (var json = new Utf8JsonWriter(stream, WriterOptions))
            {
                json.WriteStartObject();
                json.WriteNumber("version", FormatVersion);
                json.WriteString("readAt", Json.Time(book.ReadAt));
                Write(json, "categories", book.Categories, (w, c) =>
                {
                    w.WriteNumber("id", c.Id);
                    w.WriteString("name", c.Name);
                    w.WriteString("icon", c.Icon);
                    w.WriteNumber("sortOrder", c.SortOrder);
                });
                Write(json, "subcategories", book.Subcategories, (w, s) =>
                {
                    w.WriteNumber("id", s.Id);
                    w.WriteNumber("categoryId", s.CategoryId);
                    w.WriteString("name", s.Name);
                    w.WriteNumber("sortOrder", s.SortOrder);
                });
                Write(json, "sections", book.Sections, (w, s) =>
                {
                    w.WriteNumber("id", s.Id);
                    w.WriteNumber("subcategoryId", s.SubcategoryId);
                    w.WriteString("name", s.Name);
                    w.WriteNumber("sortOrder", s.SortOrder);
                });
                Write(json, "achievements", book.Achievements.Values.OrderBy(a => a.Id), (w, a) =>
                {
                    w.WriteNumber("id", a.Id);
                    w.WriteNumber("sectionId", a.SectionId);
                    w.WriteNumber("sortOrder", a.SortOrder);
                    w.WriteString("name", a.Name);
                    w.WriteString("description", a.Description);
                    w.WriteBoolean("hidden", a.Hidden);
                    w.WriteNumber("quota", a.Quota);
                    w.WriteNumber("points", a.Points);
                    w.WriteNumber("nextTierId", a.NextTierId);
                });
                Write(json, "states", book.States.Values.OrderBy(s => s.Id), (w, s) =>
                {
                    w.WriteNumber("id", s.Id);
                    w.WriteString("status", s.Status.ToString());
                    w.WriteNumber("progress", s.Progress);
                    if (s.CompletedAt is { } at)
                    {
                        w.WriteString("completedAt", Json.Time(at));
                    }
                    else
                    {
                        w.WriteNull("completedAt");
                    }
                });
                Write(json, "gains", book.Gains.OrderBy(g => g.Key), (w, g) =>
                {
                    w.WriteNumber("headId", g.Key);
                    w.WriteNumber("tiers", g.Value.Tiers);
                    w.WriteNumber("progress", g.Value.Progress);
                    w.WriteString("at", Json.Time(g.Value.At));
                });
                json.WriteEndObject();
            }

            File.Move(TempPath, _path, overwrite: true);
        }
        finally
        {
            RemoveLeftovers();
        }
    }

    /// <summary>Removes the temp file a failed or killed save left behind. Never throws; called after every save and once at start-up.</summary>
    public void RemoveLeftovers()
    {
        try
        {
            File.Delete(TempPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
        }
    }

    private static void Write<T>(Utf8JsonWriter json, string name, IEnumerable<T> rows, Action<Utf8JsonWriter, T> write)
    {
        json.WriteStartArray(name);
        foreach (var row in rows)
        {
            json.WriteStartObject();
            write(json, row);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    /// <summary>The rows of an array member, each read by <paramref name="read"/>; null when the member is missing, too long or any row is unreadable.</summary>
    private static List<T>? Rows<T>(JsonElement root, string name, int max, Func<JsonElement, T?> read)
        where T : class
    {
        if (!root.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > max)
        {
            return null;
        }

        var rows = new List<T>(array.GetArrayLength());
        foreach (var item in array.EnumerateArray())
        {
            if (read(item) is not { } row)
            {
                return null;
            }

            rows.Add(row);
        }

        return rows;
    }

    private static AchievementCategory? Category(JsonElement item) =>
        Json.TryInt(item, "id", out var id) && Json.TryText(item, "name", out var name) && Json.TryText(item, "icon", out var icon)
        && Json.TryInt(item, "sortOrder", out var sortOrder)
            ? new AchievementCategory(id, name, icon, sortOrder)
            : null;

    private static AchievementSubcategory? Subcategory(JsonElement item) =>
        Json.TryInt(item, "id", out var id) && Json.TryInt(item, "categoryId", out var categoryId) && Json.TryText(item, "name", out var name)
        && Json.TryInt(item, "sortOrder", out var sortOrder)
            ? new AchievementSubcategory(id, categoryId, name, sortOrder)
            : null;

    private static AchievementSection? Section(JsonElement item) =>
        Json.TryInt(item, "id", out var id) && Json.TryInt(item, "subcategoryId", out var subcategoryId) && Json.TryText(item, "name", out var name)
        && Json.TryInt(item, "sortOrder", out var sortOrder)
            ? new AchievementSection(id, subcategoryId, name, sortOrder)
            : null;

    private static AchievementDef? Achievement(JsonElement item) =>
        Json.TryInt(item, "id", out var id) && Json.TryInt(item, "sectionId", out var sectionId) && Json.TryInt(item, "sortOrder", out var sortOrder)
        && Json.TryText(item, "name", out var name) && Json.TryText(item, "description", out var description)
        && Json.TryBool(item, "hidden", out var hidden) && Json.TryInt(item, "quota", out var quota) && Json.TryInt(item, "points", out var points)
        && Json.TryInt(item, "nextTierId", out var nextTierId)
            ? new AchievementDef(id, sectionId, sortOrder, name, description, hidden, quota, points, nextTierId)
            : null;

    private static AchievementState? State(JsonElement item)
    {
        if (!Json.TryInt(item, "id", out var id) || !Json.TryInt(item, "progress", out var progress)
            || !item.TryGetProperty("status", out var statusValue) || statusValue.ValueKind != JsonValueKind.String
            || !Statuses.TryGetValue(statusValue.GetString()!, out var status) || !item.TryGetProperty("completedAt", out var at))
        {
            return null;
        }

        if (at.ValueKind == JsonValueKind.Null)
        {
            return new AchievementState(id, status, progress, null);
        }

        return Json.TryTime(at, out var completedAt) ? new AchievementState(id, status, progress, completedAt) : null;
    }

    private static HeadGain? Gain(JsonElement item) =>
        Json.TryInt(item, "headId", out var headId) && Json.TryInt(item, "tiers", out var tiers) && Json.TryInt(item, "progress", out var progress)
        && Json.TryTime(item, "at", out var at)
            ? new HeadGain(headId, new AchievementGain(tiers, progress, at))
            : null;

    private sealed record HeadGain(int HeadId, AchievementGain Gain);

    /// <summary>Member readers of the file; texts within <see cref="AchievementLimits.MaxText"/>, times within its years.</summary>
    private static class Json
    {
        public static string Time(DateTime time) => time.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

        public static bool TryInt(JsonElement item, string name, out int value)
        {
            value = 0;
            return item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var element)
                && element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value);
        }

        public static bool TryBool(JsonElement item, string name, out bool value)
        {
            value = false;
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(name, out var element)
                || element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return false;
            }

            value = element.GetBoolean();
            return true;
        }

        public static bool TryText(JsonElement item, string name, out string value)
        {
            value = string.Empty;
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(name, out var element)
                || element.ValueKind != JsonValueKind.String || element.GetString() is not { Length: <= AchievementLimits.MaxText } text)
            {
                return false;
            }

            value = text;
            return true;
        }

        public static bool TryTime(JsonElement item, string name, out DateTime value)
        {
            value = default;
            return item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var element) && TryTime(element, out value);
        }

        public static bool TryTime(JsonElement element, out DateTime value)
        {
            value = default;
            if (element.ValueKind != JsonValueKind.String
                || !DateTime.TryParse(element.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            {
                return false;
            }

            value = parsed.ToUniversalTime();
            return value >= AchievementLimits.Earliest && value < AchievementLimits.Latest;
        }
    }
}
