using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace Hearthpulse.Core.Collection;

/// <summary>
/// The last card backs and coins read from the client, in one JSON file of the data folder (<c>cosmetics.json</c>), so
/// the app knows them while the game is closed. Local only (docs/collection-cosmetics.md), the same rules as
/// <see cref="CollectionStore"/>: a file that is missing, damaged, out of bounds or of another format version is "none
/// yet", never repaired; the next read of the client replaces it. What <see cref="Load"/> returns is never
/// <see cref="CosmeticsCollection.Confirmed"/>. The file holds the catalog and the player's ids, no account, name or region.
/// </summary>
public sealed class CosmeticsStore
{
    /// <summary>The file's format; a file of another version is not read.</summary>
    public const int FormatVersion = 1;

    // The file is some ten kilobytes; anything far larger is not ours.
    private const long MaxBytes = 8 * 1024 * 1024;

    // By name exactly: Enum.TryParse would also take numbers and "Base, Fireside".
    private static readonly Dictionary<string, CardBackCategory> Categories =
        Enum.GetValues<CardBackCategory>().ToDictionary(c => c.ToString(), StringComparer.Ordinal);

    // Names and descriptions in the client's language stay readable (and smaller) instead of escaped character by character.
    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) };

    private readonly string _path;

    public CosmeticsStore(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _path = path;
    }

    /// <summary>The file a save is written to before it is moved over the saved one.</summary>
    internal string TempPath => _path + ".tmp";

    /// <summary>The saved cosmetics, or null when there are none that can be read. Never throws.</summary>
    public CosmeticsCollection? Load()
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
                || !StoreJson.TryTime(root, "readAt", out var readAt)
                || !StoreJson.TryArray(root, "cardBacks", out var backsValue) || !StoreJson.TryArray(root, "coins", out var coinsValue)
                || StoreJson.Ids(root, "ownedCardBacks") is not { } ownedBacks || StoreJson.Ids(root, "favoriteCardBacks") is not { } favoriteBacks
                || StoreJson.Ids(root, "ownedCoins") is not { } ownedCoins || StoreJson.Ids(root, "favoriteCoins") is not { } favoriteCoins
                || backsValue.GetArrayLength() > CosmeticsLimits.MaxEntries || coinsValue.GetArrayLength() > CosmeticsLimits.MaxEntries)
            {
                return null;
            }

            var backs = new List<CosmeticCardBack>(backsValue.GetArrayLength());
            foreach (var item in backsValue.EnumerateArray())
            {
                if (!StoreJson.TryInt(item, "id", out var id) || !StoreJson.TryText(item, "name", out var name)
                    || !StoreJson.TryText(item, "description", out var description) || !StoreJson.TryInt(item, "sortOrder", out var sortOrder)
                    || !item.TryGetProperty("category", out var categoryValue) || categoryValue.ValueKind != JsonValueKind.String
                    || !Categories.TryGetValue(categoryValue.GetString()!, out var category))
                {
                    return null;
                }

                backs.Add(new CosmeticCardBack(id, name, description, category, sortOrder));
            }

            var coins = new List<CosmeticCoin>(coinsValue.GetArrayLength());
            foreach (var item in coinsValue.EnumerateArray())
            {
                if (!StoreJson.TryInt(item, "id", out var id) || !StoreJson.TryInt(item, "cardDbfId", out var cardDbfId)
                    || !StoreJson.TryText(item, "name", out var name) || !StoreJson.TryText(item, "description", out var description))
                {
                    return null;
                }

                coins.Add(new CosmeticCoin(id, cardDbfId, name, description));
            }

            var cosmetics = new CosmeticsCollection(backs, ownedBacks, favoriteBacks, coins, ownedCoins, favoriteCoins, readAt);
            return CosmeticsLimits.Fits(cosmetics) ? cosmetics : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException
                                       or FormatException or NotSupportedException or ArgumentException or System.Security.SecurityException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes the cosmetics over the saved ones through a temp file beside them and a move (as
    /// <see cref="CollectionStore.Save"/>). Throws what the file system throws; the caller logs it and tries again.
    /// </summary>
    public void Save(CosmeticsCollection cosmetics)
    {
        ArgumentNullException.ThrowIfNull(cosmetics);
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
                json.WriteString("readAt", cosmetics.ReadAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                json.WriteStartArray("cardBacks");
                foreach (var back in cosmetics.CardBacks)
                {
                    json.WriteStartObject();
                    json.WriteNumber("id", back.Id);
                    json.WriteString("name", back.Name);
                    json.WriteString("description", back.Description);
                    json.WriteString("category", back.Category.ToString());
                    json.WriteNumber("sortOrder", back.SortOrder);
                    json.WriteEndObject();
                }

                json.WriteEndArray();
                StoreJson.WriteIds(json, "ownedCardBacks", cosmetics.OwnedCardBacks);
                StoreJson.WriteIds(json, "favoriteCardBacks", cosmetics.FavoriteCardBacks);
                json.WriteStartArray("coins");
                foreach (var coin in cosmetics.Coins)
                {
                    json.WriteStartObject();
                    json.WriteNumber("id", coin.Id);
                    json.WriteNumber("cardDbfId", coin.CardDbfId);
                    json.WriteString("name", coin.Name);
                    json.WriteString("description", coin.Description);
                    json.WriteEndObject();
                }

                json.WriteEndArray();
                StoreJson.WriteIds(json, "ownedCoins", cosmetics.OwnedCoins);
                StoreJson.WriteIds(json, "favoriteCoins", cosmetics.FavoriteCoins);
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
}

/// <summary>Reading and writing helpers of <see cref="CosmeticsStore"/> (the achievements' store has its own, with its bounds).</summary>
internal static class StoreJson
{
    /// <summary>The earliest and latest time a saved file may name (a read time, a completion, a gain).</summary>
    public static readonly DateTime Earliest = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static readonly DateTime Latest = new(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static bool TryInt(JsonElement item, string name, out int value)
    {
        value = 0;
        return item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value);
    }

    /// <summary>A string member within <see cref="CosmeticsLimits.MaxText"/>.</summary>
    public static bool TryText(JsonElement item, string name, out string value)
    {
        value = string.Empty;
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(name, out var element)
            || element.ValueKind != JsonValueKind.String || element.GetString() is not { Length: <= CosmeticsLimits.MaxText } text)
        {
            return false;
        }

        value = text;
        return true;
    }

    /// <summary>An ISO 8601 time within <see cref="Earliest"/>..<see cref="Latest"/>, as UTC.</summary>
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
        return value >= Earliest && value < Latest;
    }

    public static bool TryArray(JsonElement item, string name, out JsonElement value)
    {
        value = default;
        return item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Array;
    }

    /// <summary>A set of ids, none negative, within <see cref="CosmeticsLimits.MaxEntries"/>, or null.</summary>
    public static HashSet<int>? Ids(JsonElement root, string name)
    {
        if (!TryArray(root, name, out var array) || array.GetArrayLength() > CosmeticsLimits.MaxEntries)
        {
            return null;
        }

        var ids = new HashSet<int>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number || !item.TryGetInt32(out var id) || id < 0 || !ids.Add(id))
            {
                return null;
            }
        }

        return ids;
    }

    public static void WriteIds(Utf8JsonWriter json, string name, IEnumerable<int> ids)
    {
        json.WriteStartArray(name);
        foreach (var id in ids.Order())
        {
            json.WriteNumberValue(id);
        }

        json.WriteEndArray();
    }
}
