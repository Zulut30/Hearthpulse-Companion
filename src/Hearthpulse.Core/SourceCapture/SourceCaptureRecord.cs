using System.Text.Json;

namespace Hearthpulse.Core.SourceCapture;

/// <summary>A validated observation. Construction stays inside the visible projection, never a raw-log API.</summary>
public sealed class SourceCaptureRecord
{
    /// <summary>
    /// The wire version new observations are written in. A spooled capture keeps the version it started with. v3 = v2 plus
    /// the <c>bg_combat</c> record (<see cref="SourceCaptureCombat"/>).
    /// </summary>
    public const int CurrentVersion = 3;

    internal SourceCaptureRecord(int seq, string kind, int turn, string side, int entity, string? card, int target, object data, int v = CurrentVersion)
    {
        V = v; Seq = seq; Kind = kind; Turn = Math.Clamp(turn, 0, 1000); Side = side;
        Entity = entity; Card = card; Target = target; Data = data;
    }

    public int V { get; }
    public int Seq { get; }
    public string Kind { get; }
    public int Turn { get; }
    public string Side { get; }
    public int Entity { get; }
    public string? Card { get; }
    public int Target { get; }
    public object Data { get; }

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    /// <summary>Compact source line (v1 or v2); the spool adds the mandatory LF.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Json);
}

/// <summary>Metadata and one observation passed to a background spool; no source game/account identifiers.</summary>
public sealed record SourceCaptureFrame(string CaptureId, string CaptureKind, string Mode, int Day, SourceCaptureRecord Record);

/// <summary>
/// A transport-neutral technical state, suitable for presentation without exposing source contents. Status
/// <c>other_pool</c> and the two <c>Held</c> counts tell of what is kept for a pool address other than the current
/// one: it is not sent to the current pool and waits for its own.
/// </summary>
/// <param name="Revision">
/// The number of this report as the host published it, growing with every report: a listener that gets reports
/// through its own queue drops one older than what it already shows. Zero for a state nobody published.
/// </param>
public sealed record SourceCaptureState(string Status, long Dropped, int PendingCaptures, int PendingDeletions, int HeldCaptures = 0, int HeldDeletions = 0, long Revision = 0);

internal static class SourceCaptureTokens
{
    internal static bool Card(string? value) => value is { Length: > 0 and <= 80 }
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
    internal static bool Hex(string? value, int length) => value?.Length == length
        && value.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f');
}
