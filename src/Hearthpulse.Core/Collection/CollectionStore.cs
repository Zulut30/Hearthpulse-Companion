using System.Globalization;
using System.Text.Json;

namespace Hearthpulse.Core.Collection;

/// <summary>
/// The last collection read from the client, in one JSON file of the data folder (<c>collection.json</c>), so the app
/// knows it while the game is closed. Local only (docs/collection.md). A file that is missing, damaged, out of bounds
/// or of another format version is "no collection yet"; it is never repaired, the next read of the client replaces
/// it. What <see cref="Load"/> returns is never <see cref="CardCollection.Confirmed"/>: the file says nothing of whose
/// collection it is or how old.
/// </summary>
public sealed class CollectionStore
{
    /// <summary>The file's format; a file of another version is not read.</summary>
    public const int FormatVersion = 1;

    // A collection file is some hundred kilobytes; anything far larger is not ours.
    private const long MaxBytes = 16 * 1024 * 1024;

    private readonly string _path;

    public CollectionStore(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _path = path;
    }

    /// <summary>The file a save is written to before it is moved over the collection.</summary>
    internal string TempPath => _path + ".tmp";

    /// <summary>The saved collection, or null when there is none that can be read. Never throws.</summary>
    public CardCollection? Load()
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
                || !root.TryGetProperty("dust", out var dustValue) || !dustValue.TryGetInt64(out var dust) || dust is < 0 or > CollectionLimits.MaxDust
                || !root.TryGetProperty("readAt", out var readAtValue) || readAtValue.ValueKind != JsonValueKind.String
                || !DateTime.TryParse(readAtValue.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var readAt)
                || !root.TryGetProperty("cards", out var cardsValue) || cardsValue.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var cards = new Dictionary<string, OwnedCopies>(StringComparer.Ordinal);
            foreach (var card in cardsValue.EnumerateObject())
            {
                // [plain, golden, other]; a card with anything else makes the whole file unreadable, not that card. The
                // key must look like a card id: a view may put it into a file name or an address of card art.
                if (!CollectionLimits.IsCardId(card.Name) || card.Value.ValueKind != JsonValueKind.Array || card.Value.GetArrayLength() != 3
                    || !card.Value[0].TryGetInt32(out var normal) || !card.Value[1].TryGetInt32(out var golden)
                    || !card.Value[2].TryGetInt32(out var other))
                {
                    return null;
                }

                var copies = new OwnedCopies(normal, golden, other);
                if (!CollectionLimits.Fits(copies) || copies.Total == 0 || cards.Count >= CollectionLimits.MaxCards || !cards.TryAdd(card.Name, copies))
                {
                    return null;
                }
            }

            // No account owns nothing: the memory read gives no empty collection either.
            return cards.Count == 0 ? null : new CardCollection(cards, dust, readAt.ToUniversalTime());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException
                                       or FormatException or NotSupportedException or ArgumentException or System.Security.SecurityException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes the collection over the saved one: a temp file beside it, then a move, so a crash or a full disk leaves
    /// the old file whole. The temp file always has the same name: a save cut off by a crash leaves at most one, and
    /// the next save writes over it. Throws what the file system throws; the caller logs it and tries again. Not for two
    /// writers at once.
    /// </summary>
    public void Save(CardCollection collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        if (Path.GetDirectoryName(_path) is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
        }

        try
        {
            using (var stream = File.Create(TempPath))
            using (var json = new Utf8JsonWriter(stream))
            {
                json.WriteStartObject();
                json.WriteNumber("version", FormatVersion);
                json.WriteString("readAt", collection.ReadAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                json.WriteNumber("dust", collection.Dust);
                json.WriteStartObject("cards");
                foreach (var (cardId, copies) in collection.Cards.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    json.WriteStartArray(cardId);
                    json.WriteNumberValue(copies.Normal);
                    json.WriteNumberValue(copies.Golden);
                    json.WriteNumberValue(copies.Other);
                    json.WriteEndArray();
                }

                json.WriteEndObject();
                json.WriteEndObject();
            }

            File.Move(TempPath, _path, overwrite: true);
        }
        finally
        {
            RemoveLeftovers();
        }
    }

    /// <summary>
    /// Removes the temp file a failed, crashed or killed save left behind (it holds a collection). Never throws; called
    /// after every save and once at start-up.
    /// </summary>
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
