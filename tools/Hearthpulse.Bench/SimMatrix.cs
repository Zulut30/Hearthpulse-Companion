using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Cards;
using static Hearthpulse.Bench.Measure;

namespace Hearthpulse.Bench;

/// <summary>
/// The combat simulator's coverage matrix of today's game (plan S1, task sim-all-mechanics P0): every kind of card that
/// can act in a Battlegrounds fight - pool minions, Timewarped minions, minions seen on boards this patch (tokens and
/// generated minions, from the API's community stats), Duos-only Tavern cards, the hero power of every
/// hero, trinkets, Dark Gifts, quests, quest rewards, anomalies, Tavern spells of the pool and spells other cards cast in
/// combat, combat enchantments - with one of the six statuses of <see cref="SimCoverage"/> and whether the card is in
/// today's game. Writes docs/bg-sim-matrix.md.
/// Inputs: the card data snapshot named by <c>HP_BENCH_BGDATA</c> (tools/effects/fetch_bg_data.py: HearthstoneJSON enUS
/// and the Hearthpulse data API v1 libraries with <c>in_pool</c>; the same data the effect tables are generated from) and
/// hsbg.cards from the data folder copy (HEARTHPULSE_DATA) for what the API has no pool flag for (heroes, Timewarped).
/// </summary>
internal static class SimMatrix
{
    private const string Yes = "да";
    private const string Unconfirmed = "пул не подтверждён";
    private const string OutOfPool = "вне пула";
    private const string CombatCast = "SP_COMBAT_CAST(";

    private sealed record Row(CoverageKind Kind, string Id, string Name, Coverage Coverage, string Pool, string ClassifyId, Card Card);

    /// <summary>A hero of today's game and every hero power it can have (the starting one and its variants).</summary>
    private sealed record HeroPowers(string HeroId, string Name, bool InPool, IReadOnlyList<string> Powers);

    /// <summary>One item of an API v1 library: card id, pool flag (null when the library has none), Duos-only, type slug.</summary>
    private sealed record Item(string Id, bool? InPool, bool DuosOnly, string Type);

    /// <summary>What snapshot.json says about the data: the card data build and dates.</summary>
    private sealed record Meta(int Build, string CardsDate, string FetchedAt);

    public static void Run()
    {
        if (Environment.GetEnvironmentVariable("HEARTHPULSE_DATA") is not { Length: > 0 })
        {
            Console.WriteLine("[matrix] HEARTHPULSE_DATA is not set");
            return;
        }

        if (Environment.GetEnvironmentVariable("HP_BENCH_BGDATA") is not { Length: > 0 } snapshot
            || !File.Exists(Path.Combine(snapshot, "snapshot.json")) || !File.Exists(Path.Combine(snapshot, "cards.enUS.json")))
        {
            Console.WriteLine("[matrix] HP_BENCH_BGDATA must name a snapshot folder of tools/effects/fetch_bg_data.py");
            return;
        }

        var raw = Path.Combine(snapshot, "cards.enUS.json");
        CardDb cards;
        using (var stream = File.OpenRead(raw))
        {
            cards = CardDb.Parse("enUS", stream);
        }

        using var http = Http();
        var hsbg = BgCardDb.LoadAsync(http).GetAwaiter().GetResult();
        var meta = ReadMeta(Path.Combine(snapshot, "snapshot.json"));
        var (heroPowerDbf, rawDarkGifts) = ReadRawCards(raw);
        List<Item> Library(string name) => ReadLibrary(Path.Combine(snapshot, $"api-{name}.json"));
        var apiCards = Library("cards");
        var apiById = apiCards.GroupBy(i => i.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        string HsbgPool(string id) => hsbg.Count == 0 ? Unconfirmed : hsbg.Get(id) is not null ? Yes : OutOfPool;
        string PoolOf(Item item) => item.InPool is { } inPool ? inPool ? Yes : OutOfPool : HsbgPool(item.Id);
        bool IsDuos(string id) => id.StartsWith("BGDUO", StringComparison.Ordinal) || hsbg.Get(id)?.IsDuosOnly == true;

        var rows = new List<Row>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(CoverageKind kind, Card card, string pool, string? classifyId = null, string? note = null)
        {
            if (!seen.Add(card.Id))
            {
                return;
            }

            note = IsDuos(card.Id) && kind != CoverageKind.DuosOnly ? note is null ? "Duos" : $"Duos; {note}" : note;
            var target = cards.Get(classifyId ?? card.Id) ?? card;
            var coverage = SimCoverage.Classify(classifyId ?? card.Id, kind, target.Text);
            rows.Add(new Row(kind, card.Id, card.Name, note is null ? coverage : coverage with { Note = $"{note}; {coverage.Note}" }, pool,
                target.Id, target));
        }

        void AddLibrary(CoverageKind kind, string library)
        {
            foreach (var item in Library(library))
            {
                if (cards.Get(item.Id) is { } card)
                {
                    Add(kind, card, PoolOf(item));
                }
            }
        }

        // Minions of the Tavern pool: the API's in_pool (HearthstoneJSON's pool flag and hsbg.cards when the API list is
        // missing); Duos-only ones go to their own section.
        var poolMinions = apiCards.Where(i => i.Type == "minion" && i.InPool == true).ToList();
        if (poolMinions.Count == 0)
        {
            poolMinions = cards.All.Where(c => c.Type == "MINION" && c.IsBattlegroundsPoolMinion && c.BattlegroundsNormalDbfId == 0)
                .Select(c => new Item(c.Id, null, c.IsBattlegroundsDuosExclusive, "minion")).ToList();
        }

        foreach (var item in poolMinions.Where(i => !i.DuosOnly))
        {
            if (cards.Get(item.Id) is { } card)
            {
                Add(CoverageKind.Minion, card, PoolOf(item));
            }
        }

        // Timewarped minions: the API's library has no pool flag; hsbg.cards lists the current ones.
        foreach (var item in Library("timewarped-cards").Where(i => i.Type == "minion"))
        {
            if (cards.Get(item.Id) is { } card)
            {
                Add(CoverageKind.Minion, card, HsbgPool(item.Id), note: "Timewarped");
            }
        }

        // Duos-only Tavern cards (minions and spells).
        foreach (var item in apiCards.Where(i => i.DuosOnly && i.InPool == true))
        {
            if (cards.Get(item.Id) is { } card)
            {
                Add(CoverageKind.DuosOnly, card, Yes, note: item.Type == "spell" ? "заклинание" : null);
            }
        }

        // Minions seen on boards this patch (the API's community stats): tokens and generated minions no pool flag names.
        foreach (var item in Library("minions-seen"))
        {
            if (cards.Get(item.Id) is { Type: "MINION" } card)
            {
                Add(CoverageKind.Minion, card, Yes, note: "на досках этого патча (статистика API), не в пуле таверны");
            }
        }

        // Hero powers: of every hero (the API's hero library, else HearthstoneJSON's heroes), in today's game when
        // hsbg.cards lists the hero; then the hero powers hsbg.cards lists that no hero starts with (an anomaly's second
        // hero power, upgrades).
        var heroIds = Library("heroes").Select(i => i.Id).ToList();
        if (heroIds.Count == 0)
        {
            heroIds = cards.All.Where(c => c.IsBattlegroundsHero && c.BattlegroundsSkinParentId == 0).Select(c => c.Id).ToList();
        }

        // A hero's variants: hero powers hsbg.cards gives the hero as parent (a second hero power, a hero power it swaps to),
        // and HearthstoneJSON hero powers whose id extends the starting one's (King of Beasts, King of Mechs...).
        var bgHeroPowers = cards.All.Where(c => c.Type == "HERO_POWER" && c.Set == "BATTLEGROUNDS").ToList();
        var heroes = new List<HeroPowers>();
        foreach (var hero in heroIds.Select(cards.Get).OfType<Card>())
        {
            if (!heroPowerDbf.TryGetValue(hero.Id, out var dbf) || cards.Get(dbf) is not { } power)
            {
                continue;
            }

            Add(CoverageKind.HeroPower, power, HsbgPool(hero.Id), note: hero.Name);
            var powers = new List<string> { power.Id };
            var parent = hsbg.Get(hero.Id)?.Id ?? -1;
            var variants = hsbg.Cards.Where(c => c.CardType == "hero_power" && c.ParentId == parent).Select(c => cards.Get(c.ExternalId)).OfType<Card>()
                .Concat(bgHeroPowers.Where(c => c.Id.Length > power.Id.Length && c.Id.StartsWith(power.Id, StringComparison.Ordinal)))
                .Where(c => c.Id != power.Id)
                .DistinctBy(c => c.Id)
                .OrderBy(c => c.Id, StringComparer.Ordinal);
            foreach (var variant in variants)
            {
                Add(CoverageKind.HeroPower, variant, HsbgPool(hero.Id), note: $"{hero.Name}, вариант силы {power.Id}");
                powers.Add(variant.Id);
            }

            heroes.Add(new HeroPowers(hero.Id, hero.Name, HsbgPool(hero.Id) == Yes, powers));
        }

        foreach (var power in hsbg.Cards.Where(c => c.CardType == "hero_power").Select(c => cards.Get(c.ExternalId)).OfType<Card>())
        {
            Add(CoverageKind.HeroPower, power, Yes, note: "сила героя из пула hsbg.cards (не стартовая)");
        }

        AddLibrary(CoverageKind.Trinket, "trinkets");
        foreach (var card in cards.All.Where(c => c.Type == "BATTLEGROUND_TRINKET"))
        {
            Add(CoverageKind.Trinket, card, HsbgPool(card.Id));
        }

        // Dark Gifts: the spell is offered, the enchantment it leaves on the minion is what the simulator sees.
        var gifts = Library("dark-gifts");
        if (gifts.Count == 0)
        {
            gifts = rawDarkGifts.Select(id => new Item(id, null, false, "SPELL")).ToList();
        }

        foreach (var item in gifts)
        {
            if (cards.Get(item.Id) is not { } gift)
            {
                continue;
            }

            var enchantment = cards.All
                .Where(c => c.Type == "ENCHANTMENT" && c.Name == gift.Name && c.Id.StartsWith(gift.Id, StringComparison.Ordinal))
                .OrderByDescending(c => CombatRelevance.Find(c.Id) is not null || EffectRegistry.Default.Get(c.Id, golden: false) is not null)
                .ThenBy(c => c.Id, StringComparer.Ordinal)
                .FirstOrDefault();
            if (enchantment is not null)
            {
                seen.Add(enchantment.Id);
            }

            Add(CoverageKind.DarkGift, gift, PoolOf(item), enchantment?.Id, enchantment is null ? "зачарование не найдено" : enchantment.Id);
        }

        AddLibrary(CoverageKind.Quest, "quests");
        AddLibrary(CoverageKind.QuestReward, "rewards");
        AddLibrary(CoverageKind.Anomaly, "anomalies");

        // Tavern spells of the pool, then spells another card's combat effect casts (the table names the casters): in
        // today's game when one of their casters is.
        foreach (var item in apiCards.Where(i => i.Type == "spell" && i.InPool == true && !i.DuosOnly))
        {
            if (cards.Get(item.Id) is { } card)
            {
                Add(CoverageKind.Spell, card, Yes);
            }
        }

        foreach (var card in cards.All.Where(c => CombatRelevance.Find(c.Id)?.Category.StartsWith(CombatCast, StringComparison.Ordinal) == true))
        {
            var casters = Casters(CombatRelevance.Find(card.Id)!.Category);
            var inGame = casters.Any(c => apiById.TryGetValue(c, out var item) ? item.InPool == true : hsbg.Get(c)?.IsPoolCard == true);
            Add(CoverageKind.Spell, card, inGame ? Yes : OutOfPool, note: "кастуется в бою");
        }

        // Cards of today's game (hsbg.cards lists the current pool only) that no list above names: buddies (the anomaly Bring
        // in the Buddies puts them in the Tavern), minions hero powers make (Zerg, Protoss, the Battlecruiser, Fish of
        // N'Zoth...), tokens, Darkmoon prizes, Battlecruiser upgrades, Spellcraft and Timewarped spells. Skins are left out.
        foreach (var bg in hsbg.Cards.Where(c => c.CardType is "minion" or "spell" && !c.ExternalId.Contains("_SKIN_", StringComparison.Ordinal))
                     .OrderBy(c => c.ExternalId, StringComparer.Ordinal))
        {
            if (cards.Get(bg.ExternalId) is { } card && !card.Id.EndsWith("_G", StringComparison.Ordinal))
            {
                var kind = bg.IsDuosOnly ? CoverageKind.DuosOnly : bg.CardType == "minion" ? CoverageKind.Minion : CoverageKind.Spell;
                Add(kind, card, Yes, note: $"hsbg.cards: {string.Join(',', bg.Categories)}");
            }
        }

        // Other combat enchantments: table rows and registered effects on enchantments.
        var enchantmentIds = CombatRelevance.Table.Concat(PlayerCardTable.Entries).Where(r => r.Origin == Origin.Player).Select(r => r.CardId)
            .Concat(EffectRegistry.Default.NormalCardIds)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var card in enchantmentIds.Select(cards.Get).OfType<Card>().Where(c => c.Type == "ENCHANTMENT"))
        {
            Add(CoverageKind.Enchantment, card, Unconfirmed);
        }

        var path = Path.Combine(RepoRoot(), "docs", "bg-sim-matrix.md");
        File.WriteAllText(path, Markdown(rows, cards, meta, hsbg.Count > 0), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Console.WriteLine($"[matrix] {rows.Count} rows -> {path}");
        var audit = Path.Combine(RepoRoot(), "tests", "Hearthpulse.Tests", "Fixtures", "SimAudit", "matrix-cards.json.gz");
        WriteAuditFixture(audit, rows, heroes, cards, meta);
        Console.WriteLine($"[matrix] audit fixture -> {audit}");
        foreach (var group in rows.GroupBy(r => r.Coverage.Status).OrderBy(g => g.Key))
        {
            Console.WriteLine($"[matrix]   {SimCoverage.Code(group.Key),-15} {group.Count()}");
        }
    }

    /// <summary>
    /// The texts behind every matrix row, for the relevance audit (tests/Hearthpulse.Tests/Sim/RelevanceAuditTests.cs): the
    /// card the row is classified by, its golden text and mechanics, and every hero of the data with all its hero powers.
    /// Gzipped JSON, written deterministically so an unchanged snapshot gives the same bytes.
    /// </summary>
    private static void WriteAuditFixture(string path, List<Row> rows, List<HeroPowers> heroes, CardDb cards, Meta meta)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteString("source", $"Hearthpulse.Bench simmatrix; HearthstoneJSON enUS build {meta.Build} ({meta.CardsDate}), API v1 snapshot {meta.FetchedAt}");
            json.WriteStartArray("rows");
            foreach (var row in rows)
            {
                json.WriteStartObject();
                json.WriteString("kind", row.Kind.ToString());
                json.WriteString("id", row.Id);
                json.WriteString("classifyId", row.ClassifyId);
                json.WriteString("name", row.Name);
                json.WriteBoolean("inPool", row.Pool != OutOfPool);
                json.WriteString("status", SimCoverage.Code(row.Coverage.Status));
                json.WriteString("text", row.Card.Text);
                json.WriteString("goldenText", row.Card.BattlegroundsPremiumDbfId > 0 ? cards.Get(row.Card.BattlegroundsPremiumDbfId)?.Text ?? string.Empty : string.Empty);
                json.WriteStartArray("mechanics");
                foreach (var mechanic in row.Card.Mechanics.Order(StringComparer.Ordinal))
                {
                    json.WriteStringValue(mechanic);
                }

                json.WriteEndArray();
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteStartArray("heroes");
            foreach (var hero in heroes)
            {
                json.WriteStartObject();
                json.WriteString("id", hero.HeroId);
                json.WriteString("name", hero.Name);
                json.WriteBoolean("inPool", hero.InPool);
                json.WriteStartArray("powers");
                foreach (var power in hero.Powers)
                {
                    json.WriteStringValue(power);
                }

                json.WriteEndArray();
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = File.Create(path);
        using var gz = new GZipStream(file, CompressionLevel.SmallestSize);
        buffer.Position = 0;
        buffer.CopyTo(gz);
    }

    /// <summary>The caster ids a "SP_COMBAT_CAST(a,b,+3)" category names (the count of the cut ones is skipped).</summary>
    private static string[] Casters(string category) =>
        category[CombatCast.Length..].TrimEnd(')').Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Where(c => !c.StartsWith('+')).ToArray();

    private static string Markdown(List<Row> rows, CardDb cards, Meta meta, bool hsbgLoaded)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Матрица покрытия симулятора боя BG");
        sb.AppendLine();
        sb.AppendLine("<!-- Generated by `Hearthpulse.Bench simmatrix` (tools/Hearthpulse.Bench/SimMatrix.cs); do not edit by hand. -->");
        sb.AppendLine();
        var hsbgSource = hsbgLoaded ? "hsbg.cards (кэш)" : "hsbg.cards недоступен — для героев и Timewarped пул не подтверждён";
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"Таблица релевантности: патч {CombatRelevance.Patch}. Данные карт: HearthstoneJSON enUS, сборка {meta.Build} ({meta.CardsDate}, {cards.Count} карт); " +
            $"пул: `in_pool` API данных Hearthpulse v1 (снимок {meta.FetchedAt}, `tools/effects/fetch_bg_data.py`), для героев и Timewarped — {hsbgSource}.");
        sb.AppendLine();
        sb.AppendLine("Статусы (`SimCoverage`, те же правила, что помечают бой «not simulated»): `no_effect` — текст не меняет бой после " +
                      "снимка; `resolved` — Start of Combat, разрешился до снимка; `implemented` — есть эффект; `missing_effect` — меняет " +
                      "бой, эффекта нет; `missing_input` — эффекту нужна величина, которой нет в снимке; `unknown` — карты нет в таблицах. " +
                      "Пул: «да» — карта есть в сегодняшней игре (`in_pool` API; герой, его сила и Timewarped — в hsbg.cards; заклинание, " +
                      "которое кастуют в бою, — в пуле его кастер), «вне пула» — нет, «пул не подтверждён» — проверить нельзя. " +
                      "Как читать и обновлять — docs/bg-combat-sim.md.");
        sb.AppendLine();
        sb.AppendLine("## Итоги");
        sb.AppendLine();
        sb.AppendLine("| Вид | " + string.Join(" | ", SimCoverage.Codes) + " | всего |");
        sb.AppendLine("|---|" + string.Concat(Enumerable.Repeat("---:|", SimCoverage.Codes.Count + 1)));
        foreach (var kind in Enum.GetValues<CoverageKind>())
        {
            var ofKind = rows.Where(r => r.Kind == kind).ToList();
            var counts = Enum.GetValues<CoverageStatus>().Select(s => ofKind.Count(r => r.Coverage.Status == s).ToString(CultureInfo.InvariantCulture));
            sb.AppendLine(CultureInfo.InvariantCulture, $"| {Title(kind)} | {string.Join(" | ", counts)} | {ofKind.Count} |");
        }

        var inPool = rows.Where(r => r.Pool != OutOfPool).ToList();
        var inPoolCounts = string.Join(" | ", Enum.GetValues<CoverageStatus>().Select(s => inPool.Count(r => r.Coverage.Status == s).ToString(CultureInfo.InvariantCulture)));
        sb.AppendLine(CultureInfo.InvariantCulture, $"| **Без «вне пула»** | {inPoolCounts} | {inPool.Count} |");
        foreach (var kind in Enum.GetValues<CoverageKind>())
        {
            sb.AppendLine();
            sb.AppendLine(CultureInfo.InvariantCulture, $"## {Title(kind)}");
            sb.AppendLine();
            sb.AppendLine("| Id | Название | Статус | Пул | Примечание |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var row in rows.Where(r => r.Kind == kind)
                         .OrderBy(r => r.Coverage.Status).ThenBy(r => r.Name, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal))
            {
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"| {row.Id} | {Cell(row.Name)} | {SimCoverage.Code(row.Coverage.Status)} | {row.Pool} | {Cell(row.Coverage.Note)} |");
            }
        }

        return sb.ToString().ReplaceLineEndings("\n");
    }

    private static string Title(CoverageKind kind) => kind switch
    {
        CoverageKind.Minion => "Существа",
        CoverageKind.HeroPower => "Силы героя",
        CoverageKind.Trinket => "Тринкеты",
        CoverageKind.DarkGift => "Тёмные дары",
        CoverageKind.Quest => "Квесты",
        CoverageKind.QuestReward => "Награды квестов",
        CoverageKind.Anomaly => "Аномалии",
        CoverageKind.Spell => "Заклинания таверны",
        CoverageKind.DuosOnly => "Только Duos",
        _ => "Боевые зачарования",
    };

    private static string Cell(string text) => text.Replace("|", "\\|", StringComparison.Ordinal).Replace('\n', ' ');

    /// <summary>Hero card id → hero power dbfId, and the Dark Gift spell ids (fields CardDb does not keep).</summary>
    private static (Dictionary<string, int> HeroPowers, List<string> DarkGifts) ReadRawCards(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var heroPowers = new Dictionary<string, int>(StringComparer.Ordinal);
        var gifts = new List<string>();
        foreach (var card in doc.RootElement.EnumerateArray())
        {
            if (!card.TryGetProperty("id", out var id) || id.GetString() is not { } cardId)
            {
                continue;
            }

            if (card.TryGetProperty("heroPowerDbfId", out var hp) && hp.ValueKind == JsonValueKind.Number)
            {
                heroPowers[cardId] = hp.GetInt32();
            }

            if (card.TryGetProperty("isBattlegroundsDarkGift", out var gift) && gift.ValueKind == JsonValueKind.True
                && card.TryGetProperty("type", out var type) && type.GetString() == "SPELL")
            {
                gifts.Add(cardId);
            }
        }

        return (heroPowers, gifts);
    }

    /// <summary>The items of an API v1 library file <c>{"data": [...]}</c> (base cards only; golden variants are skipped).</summary>
    private static List<Item> ReadLibrary(string path)
    {
        var items = new List<Item>();
        if (!File.Exists(path))
        {
            return items;
        }

        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        foreach (var e in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            if (!e.TryGetProperty("card_id", out var id) || id.GetString() is not { Length: > 0 } cardId
                || (e.TryGetProperty("variant", out var variant) && variant.ValueKind == JsonValueKind.Object
                    && variant.TryGetProperty("kind", out var kind) && kind.GetString() is { } k && k != "base"))
            {
                continue;
            }

            bool? inPool = e.TryGetProperty("in_pool", out var pool) && pool.ValueKind is JsonValueKind.True or JsonValueKind.False ? pool.GetBoolean() : null;
            var duos = e.TryGetProperty("duos_only", out var d) && d.ValueKind == JsonValueKind.True;
            var type = e.TryGetProperty("card_type", out var t) && t.ValueKind == JsonValueKind.Object && t.TryGetProperty("slug", out var slug)
                ? slug.GetString() ?? string.Empty
                : string.Empty;
            items.Add(new Item(cardId, inPool, duos, type));
        }

        return items;
    }

    private static Meta ReadMeta(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = doc.RootElement;
        string Text(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;
        var build = root.TryGetProperty("build", out var b) && b.ValueKind == JsonValueKind.Number ? b.GetInt32() : 0;
        return new Meta(build, Text("hsjson_date"), Text("fetched_at"));
    }

    private static string RepoRoot()
    {
        // Above the bench itself, else above the working directory: a build with --artifacts-path lives outside the repository.
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Hearthpulse.slnx")))
            {
                dir = dir.Parent;
            }

            if (dir is not null)
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("Hearthpulse.slnx not found above the bench or the working directory");
    }
}
