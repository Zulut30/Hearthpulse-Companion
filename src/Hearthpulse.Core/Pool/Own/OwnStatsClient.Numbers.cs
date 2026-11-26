using System.Text.Json;

namespace Hearthpulse.Core.Pool.Own;

/// <summary>
/// The number objects of contract K4 in a view's data (<c>RATE</c>, <c>MEAN</c>, <c>PLACEMENT</c>), read without
/// trusting the pool: a value that cannot be true is hidden, never repaired.
/// </summary>
public sealed partial class OwnStatsClient
{
    /// <summary>
    /// A <c>RATE</c> object, <c>{num, den, value, low, high, rankable}</c>, with the share and its interval as fractions
    /// 0..1. Null when the pool hid it (too few installations) or it cannot be true: counts that are not whole and
    /// non-negative, more hits than games, a share outside 0..1, an interval upside down. A missing value or
    /// <c>rankable</c> reads as null or false. Never throws.
    /// </summary>
    public static OwnRate? Rate(JsonElement x) =>
        x.ValueKind == JsonValueKind.Object && Count(x, "num") is { } num && Count(x, "den") is { } den && num <= den
        && Number(x, "value", 0, 1, out var value) && Number(x, "low", 0, 1, out var low) && Number(x, "high", 0, 1, out var high)
        && !(low > high)
            ? new OwnRate(num, den, value, low, high, Rankable(x))
            : null;

    /// <summary>A <c>MEAN</c> object, <c>{n, mean, low, high, rankable}</c>; null as for <see cref="Rate"/>. Never throws.</summary>
    public static OwnMean? Mean(JsonElement x) =>
        x.ValueKind == JsonValueKind.Object && Count(x, "n") is { } n
        && Number(x, "mean", double.MinValue, double.MaxValue, out var mean) && Number(x, "low", double.MinValue, double.MaxValue, out var low)
        && Number(x, "high", double.MinValue, double.MaxValue, out var high) && !(low > high)
            ? new OwnMean(n, mean, low, high, Rankable(x))
            : null;

    /// <summary>
    /// A <c>PLACEMENT</c> object, <c>{n, mean, low, high, dist, rankable}</c>: a mean place 1..8 and the count of each
    /// place (eight solo, four duos; null below the minimum). Null as for <see cref="Rate"/>, and for a distribution of
    /// another length or with a count that is not whole and non-negative. Never throws.
    /// </summary>
    public static OwnPlacement? Placement(JsonElement x)
    {
        if (x.ValueKind != JsonValueKind.Object || Count(x, "n") is not { } n || !Number(x, "mean", 1, 8, out var mean)
            || !Number(x, "low", double.MinValue, double.MaxValue, out var low) || !Number(x, "high", double.MinValue, double.MaxValue, out var high)
            || low > high)
        {
            return null;
        }

        int[]? places = null;
        if (x.TryGetProperty("dist", out var dist) && dist.ValueKind != JsonValueKind.Null)
        {
            if (dist.ValueKind != JsonValueKind.Array || dist.GetArrayLength() is not (4 or 8))
            {
                return null;
            }

            places = new int[dist.GetArrayLength()];
            var place = 0;
            foreach (var count in dist.EnumerateArray())
            {
                if (Count(count) is not { } games)
                {
                    return null;
                }

                places[place++] = games;
            }
        }

        return new OwnPlacement(n, mean, low, high, places, Rankable(x));
    }

    private static int? Count(JsonElement element, string name) => element.TryGetProperty(name, out var value) ? Count(value) : null;

    /// <summary>A whole non-negative number; null for anything else.</summary>
    private static int? Count(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number >= 0 ? number : null;

    /// <summary>A finite number in <paramref name="min"/>..<paramref name="max"/>, or null where it is null or missing; false for anything else.</summary>
    private static bool Number(JsonElement element, string name, double min, double max, out double? value)
    {
        value = null;
        if (!element.TryGetProperty(name, out var raw) || raw.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (raw.ValueKind != JsonValueKind.Number || !raw.TryGetDouble(out var number) || !double.IsFinite(number) || number < min || number > max)
        {
            return false;
        }

        value = number;
        return true;
    }

    private static bool Rankable(JsonElement element) =>
        element.TryGetProperty("rankable", out var value) && value.ValueKind == JsonValueKind.True;
}
