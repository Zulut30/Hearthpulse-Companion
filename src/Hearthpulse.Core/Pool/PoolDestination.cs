using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Hearthpulse.Core.Pool;

/// <summary>
/// The pool address an upload queue is kept for: origin and path in one spelling, so a queue filled for one pool is
/// never sent to another (docs/game-stats.md, "Queue destination"). Local metadata only: it is never part of a
/// request body.
/// </summary>
public static class PoolDestination
{
    /// <summary>The pool the settings point at now; null when there is none (switched off, not https, isolated run).</summary>
    public static string? Of(AppSettings settings) => Normalize(PoolClient.Endpoint(settings));

    /// <summary>
    /// <c>https://host[:port]/path</c> with the host in lower case and no trailing slash. An address that is not https,
    /// or carries credentials, a query or a fragment, is not a destination: it could not be stored or logged safely.
    /// </summary>
    public static string? Normalize(string? endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Host.Length == 0
            || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
        {
            return null;
        }

        var host = uri.HostNameType == UriHostNameType.IPv6 ? uri.Host : uri.IdnHost;
        var port = uri.IsDefaultPort ? string.Empty : ":" + uri.Port.ToString(CultureInfo.InvariantCulture);
        return "https://" + host.ToLowerInvariant() + port + uri.AbsolutePath.TrimEnd('/');
    }

    /// <summary>The folder name of a destination's queue: 16 hex digits of its hash, so no address is spelled in a path.</summary>
    public static string Key(string destination) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(destination)), 0, 8);

    /// <summary>Whether <paramref name="name"/> is a queue folder made by <see cref="Key"/>.</summary>
    public static bool IsKey(string? name) => name is { Length: 16 } && name.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f');
}
