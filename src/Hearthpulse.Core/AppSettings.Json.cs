using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using HintMode = Hearthpulse.Core.PickHints;

namespace Hearthpulse.Core;

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class AppSettingsJsonContext : JsonSerializerContext
{
    // The first read needs only the generated setters. Asking for Default.AppSettings also
    // configures contracts for the entire graph, even for absent or empty groups of overrides.
    private static class Reader
    {
        internal static readonly Dictionary<string, JsonPropertyInfo> Members =
            ((IJsonTypeInfoResolver)Default).GetTypeInfo(typeof(AppSettings), Default.Options)!.Properties
                .Where(p => p.Set is not null).ToDictionary(p => p.Name, StringComparer.Ordinal);
    }

    internal static AppSettings? Read(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return JsonSerializer.Deserialize(json, Default.AppSettings);
        }

        var result = new AppSettings();
        try
        {
            foreach (var member in document.RootElement.EnumerateObject())
            {
                if (!Reader.Members.TryGetValue(member.Name, out var property)) continue;
                if (property.CustomConverter is not null || property.NumberHandling is not null)
                {
                    return JsonSerializer.Deserialize(json, Default.AppSettings);
                }

                var element = member.Value;
                if (element.ValueKind == JsonValueKind.Object && !element.EnumerateObject().MoveNext()
                    && property.Get?.Invoke(result) is IDictionary { Count: 0 })
                {
                    continue;
                }

                var type = property.PropertyType;
                object? value;
                if (element.ValueKind == JsonValueKind.Null && property.IsSetNullable) value = null;
                else if (type == typeof(bool) || type == typeof(bool?)) value = element.GetBoolean();
                else if (type == typeof(double)) value = element.GetDouble();
                else if (type == typeof(int)) value = element.GetInt32();
                else if (type == typeof(string)) value = element.GetString();
                else if (type == typeof(PickHints) && element.ValueKind == JsonValueKind.String)
                {
                    value = element.GetString() switch
                    {
                        nameof(HintMode.Highlight) => HintMode.Highlight,
                        nameof(HintMode.StatsOnly) => HintMode.StatsOnly,
                        nameof(HintMode.Off) => HintMode.Off,
                        _ => element.Deserialize(Default.PickHints),
                    };
                }
                else value = element.Deserialize(Default.GetTypeInfo(type)!);
                property.Set!(result, value);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or JsonException)
        {
            // Let the serializer report the same property path and apply the normal recovery policy.
            return JsonSerializer.Deserialize(json, Default.AppSettings);
        }

        return result;
    }
}
