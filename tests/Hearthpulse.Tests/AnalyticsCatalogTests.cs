using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hearthpulse.Tests;

/// <summary>docs/analytics-metrics.json: every metric is fully defined and every field it names has a checked source.</summary>
public sealed partial class AnalyticsCatalogTests
{
    private static readonly string[] Required =
        ["id", "version", "area", "numerator", "denominator", "unit", "fields", "missing", "groupBy", "smallSample", "display", "status"];
    private static readonly string[] Areas = ["results", "bg", "heroes", "shop", "economy", "combat", "builds", "arena", "mulligan", "constructed"];
    private static readonly string[] FieldStatuses = ["have", "derived", "add", "unavailable"];

    // A metric over game records is computed by one view of the pool (task contract K5): besides the common properties it
    // names its line, its interval, the number its rows are ordered by and how it is withdrawn.
    private static readonly string[] RecordText = ["title", "line", "statistics", "rankable", "withdrawal"];

    private static JsonDocument Catalog() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoFiles.Root, "docs", "analytics-metrics.json")));

    private static string Page() => File.ReadAllText(Path.Combine(RepoFiles.Root, "docs", "analytics-metrics.md"));

    [Fact]
    public void Catalog_is_version_1_and_every_metric_has_every_required_property()
    {
        using var doc = Catalog();
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var metric in root.GetProperty("metrics").EnumerateArray())
        {
            foreach (var name in Required)
                Assert.True(metric.TryGetProperty(name, out _), $"{Text(metric, "id")}: missing {name}");
            string id = Text(metric, "id");
            Assert.True(ids.Add(id), $"duplicate metric {id}");
            Assert.Contains(Text(metric, "area"), Areas);
            Assert.True(metric.GetProperty("version").GetInt32() >= 1);
            Assert.Contains(Text(metric, "status"), new[] { "implemented", "defined", "needs_fields" });
            foreach (var name in new[] { "numerator", "denominator", "unit", "missing", "smallSample", "display" })
                Assert.False(string.IsNullOrWhiteSpace(Text(metric, name)), $"{id}: empty {name}");
            Assert.NotEqual(0, metric.GetProperty("fields").GetArrayLength());
            Assert.NotEqual(0, metric.GetProperty("groupBy").GetArrayLength());
        }
    }

    [Fact]
    public void Every_field_and_group_is_in_the_field_matrix_with_a_status_and_a_source()
    {
        using var doc = Catalog();
        var matrix = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in doc.RootElement.GetProperty("fieldMatrix").EnumerateArray())
        {
            string field = Text(row, "field");
            Assert.Contains(Text(row, "status"), FieldStatuses);
            Assert.False(string.IsNullOrWhiteSpace(Text(row, "source")), $"{field}: no source");
            Assert.True(matrix.TryAdd(field, Text(row, "status")), $"duplicate field {field}");
        }
        foreach (var metric in doc.RootElement.GetProperty("metrics").EnumerateArray())
        {
            string id = Text(metric, "id");
            var fields = metric.GetProperty("fields").EnumerateArray().Select(f => f.GetString()!).ToArray();
            foreach (var field in fields.Concat(metric.GetProperty("groupBy").EnumerateArray().Select(f => f.GetString()!)))
                Assert.True(matrix.ContainsKey(field), $"{id}: {field} is not in fieldMatrix");
            // The status follows the fields: a number cannot be computed from a field nobody records.
            bool available = fields.All(f => matrix[f] is "have" or "derived");
            Assert.True(available == (Text(metric, "status") != "needs_fields"), $"{id}: status {Text(metric, "status")} contradicts its fields");
        }
    }

    [Fact]
    public void Every_area_of_the_catalog_has_a_metric()
    {
        using var doc = Catalog();
        Assert.Equal(Areas, doc.RootElement.GetProperty("areas").EnumerateArray().Select(a => Text(a, "id")));
        var used = doc.RootElement.GetProperty("metrics").EnumerateArray().Select(m => Text(m, "area")).ToHashSet(StringComparer.Ordinal);
        Assert.All(Areas, area => Assert.Contains(area, used));
    }

    [Fact]
    public void The_bg_hero_metric_is_implemented_with_rules_bg_hero_1_over_captures()
    {
        using var doc = Catalog();
        var hero = Assert.Single(doc.RootElement.GetProperty("metrics").EnumerateArray(), m => Text(m, "id") == "bg.hero.offer_pick_placement");
        Assert.Equal("implemented", Text(hero, "status"));
        Assert.Equal("bg-hero-1", Text(hero, "rulesVersion"));
        Assert.Equal("heroes", Text(hero, "area"));
        Assert.Equal(new[] { "offers", "outcome" }, hero.GetProperty("eligibility").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(new[] { "start.build" }, hero.GetProperty("groupBy").EnumerateArray().Select(e => e.GetString()));
    }

    // The rule, not a list of ids: a package of wave 2 switches its own block (status, rulesVersion, api) and nothing here.
    [Fact]
    public void Only_an_implemented_metric_names_its_rules_version_and_its_route()
    {
        using var doc = Catalog();
        foreach (var metric in doc.RootElement.GetProperty("metrics").EnumerateArray())
        {
            string id = Text(metric, "id");
            bool implemented = Text(metric, "status") == "implemented";
            foreach (var name in new[] { "rulesVersion", "api" })
            {
                bool named = metric.TryGetProperty(name, out var value);
                Assert.True(named == implemented, implemented ? $"{id}: implemented without {name}" : $"{id}: {name} on a metric that is computed nowhere");
                if (named)
                    Assert.False(string.IsNullOrWhiteSpace(value.GetString()), $"{id}: empty {name}");
            }
        }
    }

    [Fact]
    public void The_pair_metric_is_implemented_with_rules_mull_pairs_1_over_the_compact_record()
    {
        using var doc = Catalog();
        var pair = Assert.Single(doc.RootElement.GetProperty("metrics").EnumerateArray(), m => Text(m, "id") == "mulligan.pair_winrate");
        Assert.Equal("implemented", Text(pair, "status"));
        // The client reads only the rules it knows (MulliganPairs.Rules) and the server writes them (events.js).
        Assert.Equal(Hearthpulse.Core.Pool.MulliganPairs.Rules, Text(pair, "rulesVersion"));
        Assert.Contains($"const PAIR_RULES = \"{Hearthpulse.Core.Pool.MulliganPairs.Rules}\";",
            File.ReadAllText(Path.Combine(RepoFiles.Root, "server", "pool", "src", "events.js")), StringComparison.Ordinal);
        Assert.Equal("mulligan", Text(pair, "area"));
        Assert.Equal(new[] { "record.fmt", "record.cls" }, pair.GetProperty("groupBy").EnumerateArray().Select(e => e.GetString()));
        Assert.All(pair.GetProperty("fields").EnumerateArray(), f => Assert.StartsWith("record.", f.GetString(), StringComparison.Ordinal));
        Assert.Contains("both cards were kept", Text(pair, "denominator"), StringComparison.Ordinal);
        foreach (var part in new[] { "200 games", "MIN_PAIR_GAMES", "5 installations", "MIN_CELL", "excludes zero" })
            Assert.Contains(part, Text(pair, "smallSample"), StringComparison.Ordinal);
        Assert.Contains("mulligan-pairs", Text(pair, "api"), StringComparison.Ordinal);
    }

    // The pool publishes no number made of fewer than 5 installations (docs/analytics-api.md); the catalog says so per metric.
    [Fact]
    public void Every_small_sample_rule_carries_the_five_installation_threshold()
    {
        using var doc = Catalog();
        var metrics = doc.RootElement.GetProperty("metrics").EnumerateArray().ToDictionary(m => Text(m, "id"), m => Text(m, "smallSample"), StringComparer.Ordinal);
        foreach (var (id, rule) in metrics)
        {
            var resolved = rule.StartsWith("as ", StringComparison.Ordinal) ? metrics[rule[3..]] : rule;
            Assert.Contains("5", resolved, StringComparison.Ordinal);
            Assert.Contains("installations", resolved, StringComparison.Ordinal);
            Assert.Contains("10", resolved, StringComparison.Ordinal);
            Assert.False(resolved.StartsWith("as ", StringComparison.Ordinal), $"{id}: chained reference");
        }
        string hero = metrics["bg.hero.offer_pick_placement"];
        foreach (var part in new[] { "ANALYTICS_MIN_SOURCES", "ANALYTICS_MIN_CELL", "owner", "suppressed", "picked and pickRate", "placement, top4 and first", "dist" })
            Assert.Contains(part, hero, StringComparison.Ordinal);
    }

    // AC8, the catalog's part: every metric of a record view has a unit, numerator, denominator, groups, windows, a
    // small-sample rule and an interval, reads only the record and what the pool derives from it, and is grouped by
    // balance patch (ADR 0014).
    [Fact]
    public void Every_metric_over_game_records_is_defined_for_its_view_by_balance_patch()
    {
        using var doc = Catalog();
        var root = doc.RootElement;
        var records = root.GetProperty("records");
        var lines = records.GetProperty("lines").EnumerateObject()
            .ToDictionary(l => l.Name, l => l.Value.EnumerateArray().Select(m => m.GetString()!).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
        var windows = records.GetProperty("windows").EnumerateArray().Select(w => Text(w, "window")).ToHashSet(StringComparer.Ordinal);
        var views = new HashSet<string>(StringComparer.Ordinal);
        foreach (var metric in root.GetProperty("metrics").EnumerateArray().Where(m => m.TryGetProperty("view", out _)))
        {
            string id = Text(metric, "id");
            foreach (var name in RecordText)
                Assert.False(string.IsNullOrWhiteSpace(Text(metric, name)), $"{id}: empty {name}");
            string view = Text(metric, "view");
            Assert.Matches(ViewKey(), view);
            Assert.True(views.Add(view), $"{id}: view {view} computes another metric too");
            string line = Text(metric, "line");
            Assert.True(lines.ContainsKey(line), $"{id}: line {line} is not in records.lines");
            var modes = Strings(metric, "modes");
            Assert.NotEmpty(modes);
            Assert.All(modes, mode => Assert.Contains(mode, lines[line]));
            var kinds = Strings(metric, "windows");
            Assert.NotEmpty(kinds);
            Assert.All(kinds, kind => Assert.Contains(kind, windows));
            var groups = Strings(metric, "groupBy");
            Assert.All(Strings(metric, "fields").Concat(groups), field => Assert.True(
                field.StartsWith("record.", StringComparison.Ordinal) || field.StartsWith("derived.", StringComparison.Ordinal),
                $"{id}: {field} is neither a record field nor derived from one"));
            Assert.Contains("derived.patch", groups);
        }
        Assert.NotEmpty(views);
    }

    // AC8: a view the pool loads computes an implemented metric with the same rules version, and a metric without its
    // view is defined. The loader checks the same at start; this keeps the catalog from claiming a view nobody wrote.
    [Fact]
    public void A_record_metric_is_implemented_exactly_when_its_view_module_computes_it()
    {
        using var doc = Catalog();
        var metrics = doc.RootElement.GetProperty("metrics").EnumerateArray().Where(m => m.TryGetProperty("view", out _))
            .ToDictionary(m => Text(m, "view"), m => m, StringComparer.Ordinal);
        string folder = Path.Combine(RepoFiles.Root, "server", "pool", "node", "records", "views");
        var modules = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.mjs") : [];
        var computed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in modules)
        {
            string name = Path.GetFileName(file);
            var exports = Exports().Matches(File.ReadAllText(file))
                .ToDictionary(m => m.Groups["name"].Value, m => m.Groups["value"].Value, StringComparer.Ordinal);
            Assert.True(exports.ContainsKey("key") && exports.ContainsKey("metric") && exports.ContainsKey("rules"),
                $"{name}: export const key, metric and rules as string literals (task contract K2)");
            string key = exports["key"];
            Assert.Equal(Path.GetFileNameWithoutExtension(file), key);
            Assert.True(metrics.TryGetValue(key, out var metric), $"{name}: no metric of the catalog names view {key}");
            Assert.Equal(exports["metric"], Text(metric, "id"));
            Assert.Equal("implemented", Text(metric, "status"));
            Assert.Equal(exports["rules"], Text(metric, "rulesVersion"));
            Assert.Contains("/v2/analytics/records/" + key, Text(metric, "api"), StringComparison.Ordinal);
            computed.Add(key);
        }
        foreach (var (view, metric) in metrics)
            Assert.True(computed.Contains(view) || Text(metric, "status") == "defined",
                $"{Text(metric, "id")}: {Text(metric, "status")} without server/pool/node/records/views/{view}.mjs");
    }

    [Fact]
    public void The_rules_of_the_record_views_are_the_same_in_the_json_and_on_the_page()
    {
        using var doc = Catalog();
        var records = doc.RootElement.GetProperty("records");
        string page = Page();
        var thresholds = records.GetProperty("thresholds");
        // The floor of every published number (docs/analytics-api.md): 5 installations, then 10 for a value.
        Assert.Equal(5, thresholds.GetProperty("minInstalls").GetInt32());
        Assert.Equal(10, thresholds.GetProperty("minGames").GetInt32());
        Assert.Contains($"{thresholds.GetProperty("deckMinGames").GetInt32()} games", page, StringComparison.Ordinal);
        Assert.Contains($"last {records.GetProperty("readDays").GetInt32()} days", page, StringComparison.Ordinal);
        foreach (var slice in records.GetProperty("slices").EnumerateObject())
            foreach (var value in slice.Value.EnumerateArray())
                Assert.Contains($"`{value.GetString()}`", page, StringComparison.Ordinal);
        foreach (var window in records.GetProperty("windows").EnumerateArray())
            Assert.Contains($"`{Text(window, "window")}`", page, StringComparison.Ordinal);
        var half = records.GetProperty("rankable").GetProperty("maxHalfWidth");
        Assert.Contains((half.GetProperty("share").GetDouble() * 100).ToString("0.#", CultureInfo.InvariantCulture) + " points", page, StringComparison.Ordinal);
        Assert.Contains(half.GetProperty("placement").GetDouble().ToString(CultureInfo.InvariantCulture) + " places", page, StringComparison.Ordinal);
        Assert.Contains(half.GetProperty("wins").GetDouble().ToString(CultureInfo.InvariantCulture) + " wins", page, StringComparison.Ordinal);
        Assert.Contains("adr/0014-record-analytics-and-balance-patches.md", page, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(RepoFiles.Root, "docs", "adr", "0014-record-analytics-and-balance-patches.md")));
    }

    [Fact]
    public void The_markdown_page_lists_every_metric_and_view_of_the_json()
    {
        using var doc = Catalog();
        string page = Page();
        foreach (var metric in doc.RootElement.GetProperty("metrics").EnumerateArray())
        {
            Assert.Contains("`" + Text(metric, "id") + "`", page, StringComparison.Ordinal);
            if (metric.TryGetProperty("view", out var view))
                Assert.Contains("`" + view.GetString() + "`", page, StringComparison.Ordinal);
        }
    }

    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString() ?? "";

    private static string[] Strings(JsonElement element, string name) =>
        element.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(e => e.GetString() ?? "").ToArray()
            : [];

    // A view's key is the segment of its route: lower-case words joined by hyphens (task contract K2).
    [GeneratedRegex("^[a-z]+(-[a-z]+)*$")]
    private static partial Regex ViewKey();

    // export const key = 'constructed-mulligan'; (single, double or back quotes, no interpolation)
    [GeneratedRegex("""export\s+const\s+(?<name>key|metric|rules)\s*=\s*(?<q>['"`])(?<value>[^'"`$\\]+)\k<q>""")]
    private static partial Regex Exports();
}
