using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Hearthpulse.Core.Power;

namespace Hearthpulse.Core.Diagnostics;

/// <summary>
/// What the settings copy keeps: only the fields this version reads, with every key, token, password and similar value of
/// a settings field replaced. A dictionary's keys (a panel's id, a counter's id) are data, not field names: the panel
/// <c>secrets</c> keeps its place.
/// </summary>
public sealed partial class DiagnosticsBundle
{
    /// <summary>What stands for a removed value; the field itself stays, so the copy shows it was set.</summary>
    internal const string Removed = "<removed>";

    /// <summary>Parts of a field name that mark a secret wherever they stand: "password", "accessToken".</summary>
    private static readonly string[] SecretParts =
        ["passw", "passphrase", "credential", "cookie", "apikey", "privatekey", "sessionid", "accesstoken", "refreshtoken", "idtoken"];

    /// <summary>
    /// Whole words of a field name that mark a secret: "PoolSecret", "ApiKey", "OAuthToken". Not "ShowSecrets" or
    /// "Tokens": Secrets and tokens are cards in Hearthstone.
    /// </summary>
    private static readonly HashSet<string> SecretWords =
        new(["secret", "token", "key", "keys", "pwd", "pin", "otp", "auth", "authorization", "bearer", "salt", "signature", "sig"], StringComparer.Ordinal);

    /// <summary>Indented, and non-Latin text (a Cyrillic folder name) stays readable instead of \u escapes.</summary>
    private static readonly JsonSerializerOptions CopyJson = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly JsonDocumentOptions ReadJson = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    /// <summary>The user's home folder, cut from every text (swapped in tests).</summary>
    internal string UserProfile { get; init; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>
    /// The home folder's 8.3 form (<c>C:\Users\IVANPE~1</c>), which <c>%TEMP%</c> often carries; null when Windows gives
    /// none (swapped in tests).
    /// </summary>
    internal string? UserProfileShort { get; init; } = ShortForm(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    /// <summary>The user's account name, cut from Hearthpulse's own files (swapped in tests).</summary>
    internal string UserName { get; init; } = Environment.UserName;

    /// <summary>A scrubber for one archive: its stand-ins are the same in every file of it.</summary>
    internal ArchiveScrubber NewScrubber() => new(UserProfile, UserProfileShort, UserName);

    /// <summary>
    /// The settings copy: only the top-level fields <see cref="AppSettings"/> has (counted in <c>Unknown</c> otherwise);
    /// a value whose field name says secret is <see cref="Removed"/> at any depth (a true/false switch stays: it holds
    /// none), a dictionary's keys are kept as they are, every text goes through <paramref name="scrub"/>. Null when
    /// <paramref name="json"/> is not a JSON object.
    /// </summary>
    internal static (string Json, int Unknown)? SettingsCopy(string json, Func<string, string> scrub)
    {
        JsonObject root;
        try
        {
            if (JsonNode.Parse(json, documentOptions: ReadJson) is not JsonObject parsed)
            {
                return null;
            }

            root = parsed;
        }
        catch (JsonException)
        {
            return null;
        }

        var copy = new JsonObject();
        var unknown = 0;
        foreach (var (name, value) in root)
        {
            if (PropertyType(typeof(AppSettings), name) is not { } type)
            {
                unknown++;
                continue;
            }

            copy[name] = Clean(name, value?.DeepClone(), type, scrub);
        }

        return (copy.ToJsonString(CopyJson), unknown);
    }

    /// <param name="name">The settings field holding the value; null for a dictionary's value or a list's item.</param>
    /// <param name="type">The value's type in <see cref="AppSettings"/>, when known.</param>
    private static JsonNode? Clean(string? name, JsonNode? value, Type? type, Func<string, string> scrub)
    {
        if (value is null || value.GetValueKind() is JsonValueKind.True or JsonValueKind.False)
        {
            return value;
        }

        if (name is not null && IsSecretName(name))
        {
            return JsonValue.Create(Removed);
        }

        switch (value)
        {
            case JsonObject node:
                var values = DictionaryValues(type);
                foreach (var (key, child) in node.ToList())
                {
                    // A dictionary's key is data (a panel or counter id) and is never read as a field name.
                    node[key] = values is not null
                        ? Clean(null, child?.DeepClone(), values, scrub)
                        : Clean(key, child?.DeepClone(), PropertyType(type, key), scrub);
                }

                return node;
            case JsonArray list:
                var items = ListItems(type);
                for (var i = 0; i < list.Count; i++)
                {
                    list[i] = Clean(null, list[i]?.DeepClone(), items, scrub);
                }

                return list;
            default:
                return value.GetValueKind() == JsonValueKind.String ? JsonValue.Create(scrub(value.GetValue<string>())) : value;
        }
    }

    private static Type? PropertyType(Type? owner, string name) =>
        owner?.GetProperty(name, BindingFlags.Public | BindingFlags.Instance) is { CanRead: true, CanWrite: true } property
            ? Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType
            : null;

    /// <summary>The value type of a dictionary keyed by text, else null.</summary>
    private static Type? DictionaryValues(Type? type) => type?.GetInterfaces().Append(type)
        .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>) && i.GetGenericArguments()[0] == typeof(string))
        ?.GetGenericArguments()[1];

    /// <summary>The item type of a list (not of text), else null.</summary>
    private static Type? ListItems(Type? type) => type is null || type == typeof(string)
        ? null
        : type.GetInterfaces().Append(type).FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            ?.GetGenericArguments()[0];

    /// <summary>The field's name says it holds a key, a token, a password or the like.</summary>
    internal static bool IsSecretName(string name)
    {
        var lower = name.ToLowerInvariant();
        return Array.Exists(SecretParts, part => lower.Contains(part, StringComparison.Ordinal))
            || Array.Exists(WordBreak().Split(name), word => SecretWords.Contains(word.ToLowerInvariant()));
    }

    /// <summary>The 8.3 form of <paramref name="path"/> when Windows has one that differs from it, else null.</summary>
    internal static string? ShortForm(string path)
    {
        if (!OperatingSystem.IsWindows() || path.Length == 0)
        {
            return null;
        }

        try
        {
            var buffer = new char[1024];
            var length = GetShortPathName(path, buffer, (uint)buffer.Length);
            var shortPath = length is > 0 and < 1024 ? new string(buffer, 0, (int)length) : null;
            return shortPath is null || string.Equals(shortPath, path, StringComparison.OrdinalIgnoreCase) ? null : shortPath;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetShortPathNameW")]
    private static extern uint GetShortPathName(string longPath, [Out] char[] shortPath, uint bufferSize);

    // Word breaks of a field name: separators, and the change from a lower-case letter or digit to a capital ("ApiKey"),
    // or from capitals to a capitalised word ("OAuthToken" -> "O", "Auth", "Token").
    [GeneratedRegex(@"[^A-Za-z0-9]+|(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", RegexOptions.CultureInvariant)]
    private static partial Regex WordBreak();
}

/// <summary>
/// Cuts what identifies people from the archive's texts, with one map for the whole archive: each player's name becomes
/// <c>Player1#1001</c> as a BattleTag and <c>Player1</c> where the game writes it bare (the Battlegrounds and Duos
/// opponent the game renames its stand-in player to before each combat, the lobby's names), and each account number
/// (the <c>lo</c> of <c>GameAccountId=[hi=… lo=…]</c>, not 0, and Achievements.log's <c>PlayerID</c>) becomes 1,
/// 2..., in order of first sight, so the same player is the same stand-in in every file and a game log still replays the
/// same game. A bare name is replaced only where the log puts a player (<c>Entity=</c> of TAG_CHANGE and BLOCK_START,
/// <c>PlayerName=</c>, the choices' <c>Player=</c>, <c>Source = </c>), never as a word of other text: a short name could
/// be part of one. The account's <c>hi</c> (its region and type, the same for every account of a region) and
/// <c>lo=0</c> (no account) stay. Everywhere: tokens (JWT, "Bearer …"), credentials and secret query values in addresses,
/// e-mails and the user's home folder (long and 8.3 forms, with forward slashes, JSON-escaped; only the whole folder).
/// In Hearthpulse's own files the account name too, as a word; never in a game log, where it could be part of a card's
/// name.
/// </summary>
internal sealed partial class ArchiveScrubber
{
    /// <summary>A player's number by name: a BattleTag's name part, or the bare name the game wrote.</summary>
    private readonly Dictionary<string, int> _players = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _accounts = new(StringComparer.Ordinal);
    private readonly List<Regex> _home = [];
    private readonly string _homeText = OperatingSystem.IsWindows() ? "%USERPROFILE%" : "~";
    private readonly Regex? _user;

    public ArchiveScrubber(string userProfile, string? userProfileShort, string userName)
    {
        foreach (var folder in new[] { userProfile, userProfileShort ?? string.Empty }.Where(f => f.Length >= 4))
        {
            foreach (var form in new[] { folder, folder.Replace('\\', '/'), folder.Replace(@"\", @"\\", StringComparison.Ordinal) }.Distinct(StringComparer.Ordinal))
            {
                // Only the whole folder: "C:\Users\Bob" in "C:\Users\Bobby" is someone else's.
                _home.Add(new Regex(Regex.Escape(form) + @"(?=[\\/""'\s,;)\]]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)));
            }
        }

        if (userName.Length >= 3)
        {
            _user = new Regex($@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(userName)}(?![\p{{L}}\p{{N}}_])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
        }
    }

    /// <summary>Hearthpulse's own text (logs, settings, crash reports): everything, the account name as a word too.</summary>
    public string Own(string text)
    {
        var scrubbed = Common(text);
        return _user is null ? scrubbed : _user.Replace(scrubbed, "<user>");
    }

    /// <summary>
    /// A line of a game log: names (BattleTags anywhere, bare names where the log puts a player) and account numbers by
    /// the archive's map, folders, e-mails and tokens.
    /// </summary>
    public string Game(string line)
    {
        var scrubbed = Common(line);
        scrubbed = PlayerReference().Replace(scrubbed, m =>
        {
            // A name the line ends with may carry the line's trailing spaces: they stay outside the name.
            var raw = m.Groups["name"].Value;
            var name = raw.TrimEnd();
            return m.Groups["head"].Value + BareName(name) + raw[name.Length..];
        });
        return AchievementPlayer().Replace(scrubbed, m => m.Groups["head"].Value + Account(m.Groups["id"].Value));
    }

    private string Common(string text)
    {
        var scrubbed = Jwt().Replace(text, "<token>");
        scrubbed = Bearer().Replace(scrubbed, "$1<token>");
        scrubbed = UrlCredentials().Replace(scrubbed, "$1" + DiagnosticsBundle.Removed + "@");
        scrubbed = UrlSecret().Replace(scrubbed, "$1" + DiagnosticsBundle.Removed);
        scrubbed = Email().Replace(scrubbed, "<email>");
        scrubbed = GameAccount().Replace(scrubbed, m => m.Groups["head"].Value + Account(m.Groups["lo"].Value) + "]");
        scrubbed = BattleTag().Replace(scrubbed, m => Name(m.Value));
        foreach (var home in _home)
        {
            scrubbed = home.Replace(scrubbed, _homeText);
        }

        return scrubbed;
    }

    /// <summary>A BattleTag's stand-in: <c>Player1#1001</c>, the number shared with the name's bare form.</summary>
    private string Name(string tag)
    {
        var n = PlayerNumber(tag[..tag.IndexOf('#', StringComparison.Ordinal)]);
        return string.Create(CultureInfo.InvariantCulture, $"Player{n}#{1000 + n}");
    }

    /// <summary>
    /// A bare player name's stand-in: <c>Player1</c>. Not a name: an entity number, the game entity, the unknown opponent,
    /// a BattleTag (already replaced, it holds a "#").
    /// </summary>
    private string BareName(string name)
    {
        if (name.Length == 0 || name.Contains('#', StringComparison.Ordinal) || name is "GameEntity" or GameState.UnknownPlayerName
            || name.All(char.IsAsciiDigit) || (name[0] == '-' && name.Length > 1 && name[1..].All(char.IsAsciiDigit)))
        {
            return name;
        }

        return string.Create(CultureInfo.InvariantCulture, $"Player{PlayerNumber(name)}");
    }

    private int PlayerNumber(string name)
    {
        if (!_players.TryGetValue(name, out var n))
        {
            n = _players.Count + 1;
            _players[name] = n;
        }

        return n;
    }

    private string Account(string lo)
    {
        if (lo == "0")
        {
            return lo;
        }

        if (!_accounts.TryGetValue(lo, out var standIn))
        {
            standIn = (_accounts.Count + 1).ToString(CultureInfo.InvariantCulture);
            _accounts[lo] = standIn;
        }

        return standIn;
    }

    // A JSON Web Token: header.payload.signature, the header always starting "eyJ" ('{"' in base64).
    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]{4,}\.[A-Za-z0-9_-]{4,}\.[A-Za-z0-9_-]*", RegexOptions.CultureInvariant)]
    private static partial Regex Jwt();

    [GeneratedRegex(@"\b(bearer\s+)[A-Za-z0-9._~+/=-]{8,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Bearer();

    // "user:password@" after a scheme.
    [GeneratedRegex(@"\b([a-z][a-z0-9+.-]*://)[^/\s:@""']+:[^/\s@""']+@", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlCredentials();

    // A secret in an address's query: ?token=…, &key=…, &code=… (an OAuth answer).
    [GeneratedRegex(@"([?&](?:access_token|refresh_token|id_token|token|api_?key|key|secret|password|code|sig|signature)=)[^&\s""'#]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlSecret();

    [GeneratedRegex(@"[\w.+-]+@[\w-]+\.[\w.-]+", RegexOptions.CultureInvariant)]
    private static partial Regex Email();

    // Power.log's account of a player: "GameAccountId=[hi=144115198130930503 lo=37660000]".
    [GeneratedRegex(@"(?<head>GameAccountId=\[hi=\d+ lo=)(?<lo>\d+)\]", RegexOptions.CultureInvariant)]
    private static partial Regex GameAccount();

    [GeneratedRegex(@"[\p{L}\p{N}_]{2,24}#\d{3,6}", RegexOptions.CultureInvariant)]
    private static partial Regex BattleTag();

    // Where Power.log puts a player by name (the parser reads the same places, PowerLine and PowerParser): TAG_CHANGE's
    // and BLOCK_START's Entity=, DebugPrintGame's PlayerName=, the choices' Player=, a block's "Source = ". An entity in
    // brackets ([entityName=… id=…], a card) never starts with a name.
    [GeneratedRegex(
        @"(?<head>\bTAG_CHANGE Entity=)(?<name>[^\[\r\n].*?)(?= tag=)" +
        @"|(?<head>\bBLOCK_START BlockType=\w+ Entity=)(?<name>[^\[\r\n].*?)(?= EffectCardId=)" +
        @"|(?<head>\bPlayerName=)(?<name>[^\r\n]+)" +
        @"|(?<head>\bid=\d+ Player=)(?<name>[^\[\r\n].*?)(?= TaskList=| EntitiesCount=)" +
        @"|(?<head>\bSource = )(?<name>[^\[\r\n][^\r\n]*)",
        RegexOptions.CultureInvariant)]
    private static partial Regex PlayerReference();

    // Achievements.log names the account by a number that never changes: "OnAchieveNotification PlayerID=123456 ID=…".
    [GeneratedRegex(@"(?<head>\bOnAchieveNotification PlayerID=)(?<id>\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex AchievementPlayer();
}
