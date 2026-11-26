using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Hearthpulse.Core.Diagnostics;

/// <summary>
/// What the archive cuts from Hearthpulse's own files before they leave the data folder: the settings copy keeps only
/// the fields this version reads, with every key, token, password and similar value replaced; every text loses the
/// user's folder and name, e-mails, BattleTags, tokens and credentials in addresses. Lighter than
/// <see cref="CrashReport.Scrub"/>: a log must still say which folder and which value broke.
/// </summary>
public sealed partial class DiagnosticsBundle
{
    /// <summary>What stands for a removed value; the field itself stays, so the copy shows it was set.</summary>
    internal const string Removed = "<removed>";

    /// <summary>Parts of a field name that mark a secret wherever they stand: "PoolSecret", "accessToken", "password".</summary>
    private static readonly string[] SecretParts =
        ["token", "secret", "passw", "passphrase", "credential", "cookie", "apikey", "privatekey", "sessionid"];

    /// <summary>Whole words of a field name that mark a secret: "ApiKey", "api_key", "OAuth"; not "Keyboard" or "Author".</summary>
    private static readonly HashSet<string> SecretWords =
        new(["key", "keys", "pwd", "pin", "otp", "auth", "authorization", "bearer", "salt", "signature", "sig"], StringComparer.Ordinal);

    /// <summary>The fields settings.json may hold: <see cref="AppSettings"/>' public read-write properties.</summary>
    private static readonly HashSet<string> KnownSettings = typeof(AppSettings)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p is { CanRead: true, CanWrite: true })
        .Select(p => p.Name)
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>Indented, and non-Latin text (a Cyrillic folder name) stays readable instead of \u escapes.</summary>
    private static readonly JsonSerializerOptions CopyJson = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly JsonDocumentOptions ReadJson = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    /// <summary>The user's home folder, cut from every text (swapped in tests).</summary>
    internal string UserProfile { get; init; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>The user's account name, cut from every text (swapped in tests).</summary>
    internal string UserName { get; init; } = Environment.UserName;

    /// <summary>
    /// The settings copy: only the top-level fields <see cref="AppSettings"/> has (counted in <c>Unknown</c> otherwise),
    /// every secret-named value at any depth replaced by <see cref="Removed"/> (a true/false switch stays: it holds no
    /// secret), every text through <paramref name="scrub"/>. Null when <paramref name="json"/> is not a JSON object.
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
            if (!KnownSettings.Contains(name))
            {
                unknown++;
                continue;
            }

            copy[name] = Clean(name, value?.DeepClone(), scrub);
        }

        return (copy.ToJsonString(CopyJson), unknown);
    }

    private static JsonNode? Clean(string name, JsonNode? value, Func<string, string> scrub)
    {
        if (value is null || value.GetValueKind() is JsonValueKind.True or JsonValueKind.False)
        {
            return value;
        }

        if (IsSecretName(name))
        {
            return JsonValue.Create(Removed);
        }

        switch (value)
        {
            case JsonObject node:
                foreach (var (key, child) in node.ToList())
                {
                    node[key] = Clean(key, child?.DeepClone(), scrub);
                }

                return node;
            case JsonArray list:
                for (var i = 0; i < list.Count; i++)
                {
                    list[i] = Clean(name, list[i]?.DeepClone(), scrub);
                }

                return list;
            default:
                return value.GetValueKind() == JsonValueKind.String ? JsonValue.Create(scrub(value.GetValue<string>())) : value;
        }
    }

    /// <summary>The field's name says it holds a key, a token, a password or the like.</summary>
    internal static bool IsSecretName(string name)
    {
        var lower = name.ToLowerInvariant();
        return Array.Exists(SecretParts, part => lower.Contains(part, StringComparison.Ordinal))
            || Array.Exists(WordBreak().Split(name), word => SecretWords.Contains(word.ToLowerInvariant()));
    }

    /// <summary><see cref="Scrub(string, string, string)"/> with this machine's folder and name.</summary>
    internal string Scrub(string text) => Scrub(text, UserProfile, UserName);

    /// <summary>
    /// Cuts from <paramref name="text"/>: tokens (JWT, "Bearer …"), credentials and secret query values in addresses,
    /// e-mails, BattleTags, the home folder <paramref name="userProfile"/> (as written, with forward slashes and as JSON
    /// escapes it) and the account name <paramref name="userName"/> as a word. Paths below the home folder stay readable:
    /// <c>%USERPROFILE%\AppData\…</c>.
    /// </summary>
    internal static string Scrub(string text, string userProfile, string userName)
    {
        var scrubbed = Jwt().Replace(text, "<token>");
        scrubbed = Bearer().Replace(scrubbed, "$1<token>");
        scrubbed = UrlCredentials().Replace(scrubbed, "$1" + Removed + "@");
        scrubbed = UrlSecret().Replace(scrubbed, "$1" + Removed);
        scrubbed = Email().Replace(scrubbed, "<email>");
        scrubbed = BattleTag().Replace(scrubbed, "<name>");
        if (userProfile.Length >= 4)
        {
            var home = OperatingSystem.IsWindows() ? "%USERPROFILE%" : "~";
            foreach (var form in new[] { userProfile, userProfile.Replace('\\', '/'), userProfile.Replace(@"\", @"\\", StringComparison.Ordinal) }.Distinct(StringComparer.Ordinal))
            {
                // Only the whole folder: "C:\Users\Bob" in "C:\Users\Bobby" is someone else's.
                scrubbed = Regex.Replace(scrubbed, Regex.Escape(form) + @"(?=[\\/""'\s,;)\]]|$)", home.Replace("$", "$$", StringComparison.Ordinal),
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
            }
        }

        if (userName.Length >= 3)
        {
            scrubbed = Regex.Replace(scrubbed, $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(userName)}(?![\p{{L}}\p{{N}}_])", "<user>",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
        }

        return scrubbed;
    }

    // Word breaks of a field name: separators, and the change from a lower-case letter or digit to a capital ("ApiKey"),
    // or from capitals to a capitalised word ("OAuthToken" -> "O", "Auth", "Token").
    [GeneratedRegex(@"[^A-Za-z0-9]+|(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", RegexOptions.CultureInvariant)]
    private static partial Regex WordBreak();

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

    [GeneratedRegex(@"[\p{L}\p{N}_]{2,24}#\d{3,6}", RegexOptions.CultureInvariant)]
    private static partial Regex BattleTag();
}
