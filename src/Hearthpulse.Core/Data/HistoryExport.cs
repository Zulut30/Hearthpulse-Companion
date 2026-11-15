using System.Globalization;
using System.Text;

namespace Hearthpulse.Core.Data;

/// <summary>
/// History as a CSV file (<c>docs/history-export.md</c>): one row a game, only what the History row already holds and
/// shows. RFC 4180 (CRLF, quotes doubled, a field with a comma, a quote or a line break in quotes), UTF-8 with a byte
/// order mark so that Excel reads it, numbers and dates the same under any culture, English column names that do not
/// change with the interface language. No player or opponent names, no BattleTags, no card lists.
/// </summary>
public static class HistoryExport
{
    private static readonly (string Name, Func<GameRecord, Func<string, string>?, string> Value)[] Fields =
    [
        ("started_at", (g, _) => Time(g.StartedAt)),
        ("ended_at", (g, _) => Time(g.EndedAt)),
        ("duration_seconds", (g, _) => Duration(g)),
        ("mode", (g, _) => Text(g.Mode)),
        ("game_type", (g, _) => Text(g.GameType)),
        ("format", (g, _) => Text(g.Format)),
        ("result", (g, _) => Text(g.Result)),
        ("placement", (g, _) => Positive(g.Placement)),
        ("player_class", (g, _) => Text(g.PlayerClass)),
        ("hero_card_id", (g, _) => Text(g.HeroCardId)),
        ("hero", (g, name) => Text(HeroName(g.HeroCardId, name))),
        ("deck_name", (g, _) => Text(g.DeckName)),
        ("deck_code", (g, _) => Text(g.DeckCode)),
        ("opponent_class", (g, _) => Text(g.OpponentClass)),
        ("opponent_archetype", (g, _) => Text(g.OpponentArchetype)),
        ("went_first", (g, _) => g.WentFirst is { } first ? (first ? "true" : "false") : string.Empty),
        ("turns", (g, _) => Positive(g.Turns)),
        ("rating_before", (g, _) => Number(g.RatingBefore)),
        ("rating_after", (g, _) => Number(g.RatingAfter)),
        ("rating_change", (g, _) => Number(g.RatingDelta)),
        ("rank", (g, _) => Text(g.Rank)),
        ("rank_legend", (g, _) => Number(g.RankLegend)),
        ("tavern_tier", (g, _) => Positive(g.TavernTier)),
        ("source", (g, _) => Text(g.Source)),
    ];

    /// <summary>The column names, in file order.</summary>
    public static IReadOnlyList<string> Columns { get; } = Fields.Select(f => f.Name).ToArray();

    /// <summary>
    /// The file's text: the header line, then <paramref name="games"/> in the order given.
    /// </summary>
    /// <param name="heroName">A hero card's name for the <c>hero</c> column (the History card's title); without it the column stays empty.</param>
    public static string Csv(IEnumerable<GameRecord> games, Func<string, string>? heroName = null)
    {
        ArgumentNullException.ThrowIfNull(games);
        var text = new StringBuilder();
        text.AppendJoin(',', Columns).Append("\r\n");
        foreach (var game in games)
        {
            for (var i = 0; i < Fields.Length; i++)
            {
                if (i > 0)
                {
                    text.Append(',');
                }

                text.Append(Quoted(Fields[i].Value(game, heroName)));
            }

            text.Append("\r\n");
        }

        return text.ToString();
    }

    /// <summary>The file's bytes: <see cref="Csv"/> in UTF-8 after a byte order mark.</summary>
    public static byte[] Bytes(IEnumerable<GameRecord> games, Func<string, string>? heroName = null)
    {
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        var body = encoding.GetBytes(Csv(games, heroName));
        var mark = encoding.GetPreamble();
        var file = new byte[mark.Length + body.Length];
        mark.CopyTo(file, 0);
        body.CopyTo(file, mark.Length);
        return file;
    }

    /// <summary>
    /// Writes the file whole or not at all: to a temporary file beside <paramref name="path"/>, then moved over it. A
    /// failed write leaves the file that was there (or none) and throws.
    /// </summary>
    public static void Save(string path, byte[] file)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(file);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temp, file);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Delete(temp);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                // The temporary file stays; the failure that matters is the one rethrown.
            }

            throw;
        }
    }

    /// <summary>ISO 8601 to the second, with the zone the row was saved with ("Z", an offset, or none).</summary>
    private static string Time(DateTime at) =>
        at == DateTime.MinValue ? string.Empty : at.ToString("yyyy-MM-dd'T'HH:mm:ssK", CultureInfo.InvariantCulture);

    private static string Duration(GameRecord game) =>
        game.StartedAt == DateTime.MinValue || game.EndedAt < game.StartedAt
            ? string.Empty
            : ((long)Math.Round(game.Duration.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

    private static string Number(int? value) => value is { } v ? v.ToString(CultureInfo.InvariantCulture) : string.Empty;

    /// <summary>A count the row keeps as 0 when it is not known (placement outside Battlegrounds, turns, tavern tier).</summary>
    private static string Positive(int value) => value > 0 ? value.ToString(CultureInfo.InvariantCulture) : string.Empty;

    /// <summary>The name only when the resolver knows the card: an id echoed back says nothing the id column does not.</summary>
    private static string? HeroName(string cardId, Func<string, string>? name) =>
        cardId.Length > 0 && name?.Invoke(cardId) is { Length: > 0 } hero && !string.Equals(hero, cardId, StringComparison.Ordinal) ? hero : null;

    /// <summary>
    /// A text field, safe to open in a spreadsheet: a value that starts with <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>, a tab
    /// or a carriage return would be run as a formula, so it gets an apostrophe in front (deck names are the player's own
    /// text). Numbers and dates are written by this class and never pass through here, so "-25" stays a number.
    /// </summary>
    private static string Text(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;

    private static string Quoted(string field) =>
        field.AsSpan().IndexOfAny(",\"\r\n") < 0 && !Padded(field) ? field : "\"" + field.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    /// <summary>Spaces at either end are kept by the quotes: some readers trim an unquoted field.</summary>
    private static bool Padded(string field) => field.Length > 0 && (field[0] == ' ' || field[^1] == ' ');
}
