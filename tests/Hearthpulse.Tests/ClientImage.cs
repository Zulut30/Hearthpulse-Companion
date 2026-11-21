using Hearthpulse.Core.Memory;

namespace Hearthpulse.Tests;

/// <summary>What a test breaks in a <see cref="ClientImage"/>.</summary>
public enum ClientDamage
{
    None,

    /// <summary>A card back record of another class.</summary>
    CardBackOfAnotherClass,

    /// <summary>A card back's name is no DbfLocValue.</summary>
    NameOfAnotherClass,

    /// <summary>A card back's name points at memory that cannot be read.</summary>
    UnreadableName,

    /// <summary>The owned card backs count one more than their live slots.</summary>
    SetCountsMore,

    /// <summary>A live slot of the owned card backs has a hash code that is not its value's.</summary>
    HashNotValue,

    /// <summary>The owned card backs are no HashSet.</summary>
    SetOfAnotherClass,

    /// <summary>The card back list says it is longer than its array.</summary>
    ListBeyondItsArray,

    /// <summary>An achievement record of another class.</summary>
    AchievementOfAnotherClass,

    /// <summary>A state's key is not its achievement id.</summary>
    KeyNotItsAchievement,

    /// <summary>The state map counts one more than it holds.</summary>
    MapCountsMore,

    /// <summary>A state of another class.</summary>
    StateOfAnotherClass,

    /// <summary>A completion date outside 2000..2100.</summary>
    DateOutOfRange,

    /// <summary>An achievement's description points at memory that cannot be read.</summary>
    UnreadableDescription,

    /// <summary>The class metadata puts DbfLocValue's text a megabyte into the object.</summary>
    LocOffsetOutOfRange,

    /// <summary>The class metadata puts a card back record's field sixteen megabytes into the record.</summary>
    RecordOffsetOutOfRange,

    /// <summary>The owned card backs' last index (and slots) far past what a set of their bound has.</summary>
    SetLastIndexOutOfRange,
}

/// <summary>
/// A hand-built client (<see cref="MonoImageBuilder"/>) with what the cosmetics and achievements reads look at, in the
/// layout the client had on 2026-11-19 (docs/collection-cosmetics.md): NetCache with <c>NetCacheCardBacks</c> and
/// <c>NetCacheCoins</c> (sets of ints), the <c>GameDbf</c> tables (records with localized values), and the achievement
/// manager's player state map. Made-up data only.
/// </summary>
internal sealed class ClientImage
{
    // Mono type codes of the fields below.
    private const byte Bool = 0x02, I4 = 0x08, I8 = 0x0A, String = 0x0E, ValueType = 0x11, Class = 0x12, GenericInst = 0x15, SzArray = 0x1D;

    private const ulong Unmapped = 0x7000_0000;

    public static readonly long Nov1 = new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

    public (int Id, string Name, string Description, int Category, int SortOrder, bool Enabled, bool Random)[] CardBacks { get; init; } =
    [
        (0, "Classic", "The first card back.", 1, 0, true, false),
        (1, "Random", string.Empty, 0, 1, true, true),
        (7, "Сезонная рубашка", "Описание.", 5, 2, true, false),
        (9, "Disabled", string.Empty, 3, 3, false, false),
        (12, "Odd group", string.Empty, 99, 4, true, false),
    ];

    public int[] OwnedCardBacks { get; init; } = [0, 7, 12, 400];

    public int[] FavoriteCardBacks { get; init; } = [7];

    public (int Id, int CardDbfId, string Name, bool Enabled)[] Coins { get; init; } =
    [
        (1, 1746, "The Coin", true),
        (2, 0, "Off", false),
        (3, 1747, "Монетка", true),
    ];

    public int[] OwnedCoins { get; init; } = [1, 3];

    public int[] FavoriteCoins { get; init; } = [3];

    public (int Id, string Name, string Icon, int SortOrder)[] Categories { get; init; } =
    [
        (2, "Игровой процесс", "gameplay", 2),
        (1, "Общие", "general", 1),
    ];

    public (int Id, int Category, string Name, int SortOrder)[] Subcategories { get; init; } =
    [
        (10, 1, "Прогресс", 1),
        (11, 1, string.Empty, 2),
        (20, 2, "Бой", 1),
    ];

    public (int Id, string Name)[] Sections { get; init; } = [(100, "Путь"), (110, string.Empty), (200, "Бой"), (300, "Unlinked")];

    public (int Subcategory, int Section, int SortOrder)[] Items { get; init; } = [(10, 100, 1), (11, 110, 1), (20, 200, 1)];

    public (int Id, int Section, bool Enabled, bool Retired, string Name, string Description, int Visibility, int Quota, int Points, int Next)[] Achievements { get; init; } =
    [
        (1, 100, true, false, "A I", "Соберите $q |4(карту,карты,карт).", 0, 10, 5, 2),
        (2, 100, true, false, "A II", "Соберите $q |4(карту,карты,карт).", 0, 50, 10, 3),
        (3, 100, true, false, "A III", "Соберите $q |4(карту,карты,карт).", 0, 100, 20, 0),
        (4, 100, true, false, string.Empty, string.Empty, 1, 1, 15, 0),
        (5, 110, true, true, "Retired", string.Empty, 0, 1, 50, 0),
        (6, 110, false, false, "Disabled", string.Empty, 0, 1, 50, 0),
        (7, 300, true, false, "Unlinked", string.Empty, 0, 1, 50, 0),
        (8, 200, true, false, "No quota", "<i>Win</i>.", 0, 0, 5, 0),
    ];

    public (int Id, int Status, int Progress, long CompletedDate)[] States { get; init; } =
    [
        (1, 4, 10, Nov1),
        (2, 1, 34, 0),
        (3, 0, 0, 0),
        (4, 1, 1, 0),
        (5, 4, 1, Nov1),
        (8, 2, 1, Nov1 + 3600),
    ];

    public bool WithCosmetics { get; init; } = true;

    public bool WithAchievementManager { get; init; } = true;

    public ClientDamage Damage { get; init; }

    /// <summary>Where the owned card backs' version and the state map's generation are, once built.</summary>
    public ulong CardBacksVersionAddress { get; private set; }

    public ulong MapGenerationAddress { get; private set; }

    public static HearthstoneMemory Attach(IMemorySource source)
    {
        var memory = new HearthstoneMemory();
        Assert.True(memory.AttachTo(ProcessMemory.FromSource(source)), memory.Status);
        return memory;
    }

    public MemoryImage Build()
    {
        var b = new MonoImageBuilder();
        var objects = b.ArrayClass("Object[]", 8);
        var ints = b.ArrayClass("Int32[]", 4);
        var slotsClass = b.ArrayClass("Slot[]", 12);
        var entries = b.ArrayClass("Entry[]", 24);
        var list = b.Class("System.Collections.Generic.List`1", ("_items", 16, false, SzArray), ("_size", 24, false, I4));
        var dictionary = b.Class("System.Collections.Generic.Dictionary`2", ("_entries", 16, false, SzArray), ("_count", 24, false, I4));
        var hashSet = b.Class("System.Collections.Generic.HashSet`1",
            ("_buckets", 0x10, false, SzArray), ("_slots", 0x18, false, SzArray), ("_comparer", 0x20, false, GenericInst),
            ("_count", 0x30, false, I4), ("_lastIndex", 0x34, false, I4), ("_freeList", 0x38, false, I4), ("_version", 0x3C, false, I4));
        var map = b.Class("Blizzard.T5.Core.Map`2",
            ("table", 0x10, false, SzArray), ("keySlots", 0x20, false, SzArray), ("valueSlots", 0x28, false, SzArray),
            ("touchedSlots", 0x38, false, I4), ("count", 0x40, false, I4), ("generation", 0x48, false, I4));
        var jobs = b.Class("Hearthstone.HearthstoneJobs", ("s_dependencyBuilder", 8, true, Class));
        var builder = b.Class("Hearthstone.DependencyBuilder", ("m_serviceLocator", 16, false, Class));
        var locator = b.Class("Hearthstone.ServiceLocator", ("m_services", 16, false, Class));
        var service = b.Class("Hearthstone.ServiceRecord", ("<ServiceTypeName>k__BackingField", 16, false, String), ("<Service>k__BackingField", 24, false, Class));
        var netCacheClass = b.Class("NetCache", ("m_netCache", 16, false, Class));
        var otherCache = b.Class("NetCacheMedalInfo", ("m_unused", 16, false, I4));
        var backsCacheClass = b.Class("NetCacheCardBacks",
            ("<FavoriteCardBacks>k__BackingField", 0x10, false, GenericInst), ("<CardBacks>k__BackingField", 0x18, false, GenericInst));
        var coinsCacheClass = b.Class("NetCacheCoins",
            ("<FavoriteCoins>k__BackingField", 0x10, false, GenericInst), ("<Coins>k__BackingField", 0x18, false, GenericInst));
        var gameDbf = b.Class("GameDbf",
            ("CardBack", 8, true, GenericInst), ("CosmeticCoin", 16, true, GenericInst), ("Achievement", 24, true, GenericInst),
            ("AchievementCategory", 32, true, GenericInst), ("AchievementSubcategory", 40, true, GenericInst),
            ("AchievementSection", 48, true, GenericInst), ("AchievementSectionItem", 56, true, GenericInst));
        var dbf = b.Class("Dbf`1", ("m_name", 0x10, false, String), ("m_records", 0x18, false, GenericInst));
        var locValue = b.Class("DbfLocValue", ("m_locValues", 0x10, false, SzArray),
            ("m_currentLocaleValue", Damage == ClientDamage.LocOffsetOutOfRange ? 1 << 20 : 0x20, false, String));
        var cardBackRecord = b.Class("CardBackDbfRecord",
            ("m_ID", 0x10, false, I4), ("m_prefabName", 0x18, false, String), ("m_name", 0x20, false, Class), ("m_description", 0x28, false, Class),
            ("m_data1", 0x30, false, I8), ("m_source", 0x38, false, ValueType), ("m_enabled", 0x3C, false, Bool),
            ("m_sortCategory", 0x40, false, ValueType), ("m_sortOrder", Damage == ClientDamage.RecordOffsetOutOfRange ? 1 << 24 : 0x44, false, I4),
            ("m_isRandomCardBack", 0x48, false, Bool));
        var coinRecord = b.Class("CosmeticCoinDbfRecord",
            ("m_ID", 0x10, false, I4), ("m_cardId", 0x14, false, I4), ("m_name", 0x18, false, Class), ("m_description", 0x20, false, Class),
            ("m_enabled", 0x28, false, Bool));
        var categoryRecord = b.Class("AchievementCategoryDbfRecord",
            ("m_ID", 0x10, false, I4), ("m_name", 0x18, false, Class), ("m_icon", 0x20, false, String), ("m_sortOrder", 0x28, false, I4));
        var subcategoryRecord = b.Class("AchievementSubcategoryDbfRecord",
            ("m_ID", 0x10, false, I4), ("m_achievementCategoryId", 0x14, false, I4), ("m_name", 0x18, false, Class), ("m_sortOrder", 0x20, false, I4));
        var sectionRecord = b.Class("AchievementSectionDbfRecord", ("m_ID", 0x10, false, I4), ("m_name", 0x18, false, Class));
        var itemRecord = b.Class("AchievementSectionItemDbfRecord",
            ("m_ID", 0x10, false, I4), ("m_achievementSubcategoryId", 0x14, false, I4), ("m_achievementSectionId", 0x18, false, I4),
            ("m_sortOrder", 0x1C, false, I4));
        var achievementRecord = b.Class("AchievementDbfRecord",
            ("m_ID", 0x10, false, I4), ("m_achievementSectionId", 0x14, false, I4), ("m_sortOrder", 0x18, false, I4), ("m_enabled", 0x1C, false, Bool),
            ("m_retired", 0x1D, false, Bool), ("m_name", 0x20, false, Class), ("m_description", 0x28, false, Class),
            ("m_achievementVisibility", 0x30, false, ValueType), ("m_quota", 0x34, false, I4), ("m_points", 0x38, false, I4),
            ("m_nextTierId", 0x3C, false, I4));
        var managerClass = b.Class("Hearthstone.Progression.AchievementManager", ("m_playerState", 0x10, false, GenericInst));
        var playerStateClass = b.Class("Hearthstone.Progression.PlayerState`1", ("OnStateChanged", 0x10, false, GenericInst), ("m_playerState", 0x18, false, GenericInst));
        var stateClass = b.Class("PegasusUtil.PlayerAchievementState",
            ("_AchievementId", 0x1C, false, I4), ("_Status", 0x24, false, ValueType), ("_Progress", 0x2C, false, I4), ("_CompletedDate", 0x38, false, I8));

        ulong Loc(string text, bool damaged = false)
        {
            if (damaged && Damage == ClientDamage.NameOfAnotherClass)
            {
                return b.Object(otherCache, 0x30);
            }

            var value = b.Object(locValue, 0x38);
            b.Ptr(value + 0x20, damaged && Damage == ClientDamage.UnreadableName ? Unmapped : b.MonoString(text));
            return value;
        }

        ulong List(IReadOnlyList<ulong> items, bool damaged = false)
        {
            var array = b.Array(objects, items.Count);
            for (var i = 0; i < items.Count; i++)
            {
                b.Ptr(b.Element(array, i), items[i]);
            }

            var obj = b.Object(list);
            b.Ptr(obj + 16, array);
            b.Int(obj + 24, damaged ? items.Count + 5 : items.Count);
            return obj;
        }

        ulong Table(IReadOnlyList<ulong> records, bool damaged = false)
        {
            var table = b.Object(dbf, 0x28);
            b.Ptr(table + 0x18, List(records, damaged));
            return table;
        }

        ulong Set(int[] values, bool damaged = false)
        {
            if (damaged && Damage == ClientDamage.SetOfAnotherClass)
            {
                return b.Object(otherCache, 0x40);
            }

            // A removed value first (hash code -1), then the live ones.
            var last = damaged && Damage == ClientDamage.SetLastIndexOutOfRange ? 50_000 : values.Length + 1;
            var slots = b.Array(slotsClass, last, 12);
            b.Int(b.Element(slots, 0, 12), -1);
            for (var i = 0; i < values.Length; i++)
            {
                var slot = b.Element(slots, i + 1, 12);
                b.Int(slot, damaged && Damage == ClientDamage.HashNotValue && i == 0 ? (values[i] + 1) & 0x7FFFFFFF : values[i] & 0x7FFFFFFF);
                b.Int(slot + 4, -1);
                b.Int(slot + 8, values[i]);
            }

            var set = b.Object(hashSet, 0x40);
            b.Ptr(set + 0x18, slots);
            b.Int(set + 0x30, damaged && Damage == ClientDamage.SetCountsMore ? values.Length + 1 : values.Length);
            b.Int(set + 0x34, last);
            b.Int(set + 0x38, -1);
            b.Int(set + 0x3C, 17);
            if (damaged)
            {
                CardBacksVersionAddress = set + 0x3C;
            }

            return set;
        }

        // The card back and coin tables.
        var backs = new List<ulong>();
        for (var i = 0; i < CardBacks.Length; i++)
        {
            var (id, name, description, category, sortOrder, enabled, random) = CardBacks[i];
            var damaged = i == 2;
            var record = b.Object(damaged && Damage == ClientDamage.CardBackOfAnotherClass ? coinRecord : cardBackRecord, 0x50);
            b.Int(record + 0x10, id);
            b.Ptr(record + 0x20, Loc(name, damaged));
            b.Ptr(record + 0x28, Loc(description));
            b.Bool(record + 0x3C, enabled);
            b.Int(record + 0x40, category);
            b.Int(record + 0x44, sortOrder);
            b.Bool(record + 0x48, random);
            backs.Add(record);
        }

        var coins = new List<ulong>();
        foreach (var (id, cardDbfId, name, enabled) in Coins)
        {
            var record = b.Object(coinRecord, 0x30);
            b.Int(record + 0x10, id);
            b.Int(record + 0x14, cardDbfId);
            b.Ptr(record + 0x18, Loc(name));
            b.Ptr(record + 0x20, Loc(string.Empty));
            b.Bool(record + 0x28, enabled);
            coins.Add(record);
        }

        // The achievement tables.
        var categories = Categories.Select(c =>
        {
            var record = b.Object(categoryRecord, 0x30);
            b.Int(record + 0x10, c.Id);
            b.Ptr(record + 0x18, Loc(c.Name));
            b.Ptr(record + 0x20, b.MonoString(c.Icon));
            b.Int(record + 0x28, c.SortOrder);
            return record;
        }).ToList();
        var subcategories = Subcategories.Select(s =>
        {
            var record = b.Object(subcategoryRecord, 0x28);
            b.Int(record + 0x10, s.Id);
            b.Int(record + 0x14, s.Category);
            b.Ptr(record + 0x18, Loc(s.Name));
            b.Int(record + 0x20, s.SortOrder);
            return record;
        }).ToList();
        var sections = Sections.Select(s =>
        {
            var record = b.Object(sectionRecord, 0x20);
            b.Int(record + 0x10, s.Id);
            b.Ptr(record + 0x18, Loc(s.Name));
            return record;
        }).ToList();
        var items = Items.Select((item, i) =>
        {
            var record = b.Object(itemRecord, 0x20);
            b.Int(record + 0x10, i + 1);
            b.Int(record + 0x14, item.Subcategory);
            b.Int(record + 0x18, item.Section);
            b.Int(record + 0x1C, item.SortOrder);
            return record;
        }).ToList();
        var achievements = new List<ulong>();
        for (var i = 0; i < Achievements.Length; i++)
        {
            var a = Achievements[i];
            var damaged = i == 1;
            var record = b.Object(damaged && Damage == ClientDamage.AchievementOfAnotherClass ? sectionRecord : achievementRecord, 0x40);
            b.Int(record + 0x10, a.Id);
            b.Int(record + 0x14, a.Section);
            b.Int(record + 0x18, a.Id);
            b.Bool(record + 0x1C, a.Enabled);
            b.Bool(record + 0x1D, a.Retired);
            b.Ptr(record + 0x20, Loc(a.Name));
            var description = Loc(a.Description);
            if (damaged && Damage == ClientDamage.UnreadableDescription)
            {
                b.Ptr(description + 0x20, Unmapped);
            }

            b.Ptr(record + 0x28, description);
            b.Int(record + 0x30, a.Visibility);
            b.Int(record + 0x34, a.Quota);
            b.Int(record + 0x38, a.Points);
            b.Int(record + 0x3C, a.Next);
            achievements.Add(record);
        }

        var statics = b.Statics(gameDbf, 128);
        b.Ptr(statics + 8, Table(backs, Damage == ClientDamage.ListBeyondItsArray));
        b.Ptr(statics + 16, Table(coins));
        b.Ptr(statics + 24, Table(achievements));
        b.Ptr(statics + 32, Table(categories));
        b.Ptr(statics + 40, Table(subcategories));
        b.Ptr(statics + 48, Table(sections));
        b.Ptr(statics + 56, Table(items));

        // NetCache: another cache, the card backs, an empty slot, the coins.
        var backsCache = b.Object(backsCacheClass, 0x20);
        b.Ptr(backsCache + 0x10, Set(FavoriteCardBacks));
        b.Ptr(backsCache + 0x18, Set(OwnedCardBacks, damaged: true));
        var coinsCache = b.Object(coinsCacheClass, 0x20);
        b.Ptr(coinsCache + 0x10, Set(FavoriteCoins));
        b.Ptr(coinsCache + 0x18, Set(OwnedCoins));
        var cacheSlots = b.Array(objects, 4);
        b.Ptr(b.Element(cacheSlots, 0), b.Object(otherCache));
        if (WithCosmetics)
        {
            b.Ptr(b.Element(cacheSlots, 1), backsCache);
            b.Ptr(b.Element(cacheSlots, 3), coinsCache);
        }

        var netCacheMap = b.Object(map, 0x50);
        b.Ptr(netCacheMap + 0x28, cacheSlots);
        var netCache = b.Object(netCacheClass);
        b.Ptr(netCache + 16, netCacheMap);

        // The achievement manager: its player state's map, one empty slot after the states.
        var touched = States.Length + 1;
        var keys = b.Array(ints, touched, 4);
        var values = b.Array(objects, touched);
        for (var i = 0; i < States.Length; i++)
        {
            var (id, status, progress, date) = States[i];
            var damaged = i == 1;
            var state = b.Object(damaged && Damage == ClientDamage.StateOfAnotherClass ? otherCache : stateClass, 0x40);
            b.Int(state + 0x1C, id);
            b.Int(state + 0x24, status);
            b.Int(state + 0x2C, progress);
            b.Long(state + 0x38, damaged && Damage == ClientDamage.DateOutOfRange ? 99_999_999_999 : date);
            b.Int(b.Element(keys, i, 4), damaged && Damage == ClientDamage.KeyNotItsAchievement ? id + 1000 : id);
            b.Ptr(b.Element(values, i), state);
        }

        var stateMap = b.Object(map, 0x50);
        b.Ptr(stateMap + 0x20, keys);
        b.Ptr(stateMap + 0x28, values);
        b.Int(stateMap + 0x38, touched);
        b.Int(stateMap + 0x40, Damage == ClientDamage.MapCountsMore ? States.Length + 1 : States.Length);
        b.Int(stateMap + 0x48, 17);
        MapGenerationAddress = stateMap + 0x48;
        var playerState = b.Object(playerStateClass, 0x28);
        b.Ptr(playerState + 0x18, stateMap);
        var manager = b.Object(managerClass, 0x20);
        b.Ptr(manager + 0x10, playerState);

        // The service locator: NetCache and, when there is one, the achievement manager.
        var services = new List<(string Name, ulong Service)> { ("NetCache", netCache) };
        if (WithAchievementManager)
        {
            services.Add(("Hearthstone.Progression.AchievementManager", manager));
        }

        var records = b.Array(entries, services.Count, 24);
        for (var i = 0; i < services.Count; i++)
        {
            var record = b.Object(service);
            b.Ptr(record + 16, b.MonoString(services[i].Name));
            b.Ptr(record + 24, services[i].Service);
            b.Ptr(b.Element(records, i, 24) + 16, record);
        }

        var serviceDictionary = b.Object(dictionary);
        b.Ptr(serviceDictionary + 16, records);
        b.Int(serviceDictionary + 24, services.Count);
        var serviceLocator = b.Object(locator);
        b.Ptr(serviceLocator + 16, serviceDictionary);
        var dependencyBuilder = b.Object(builder);
        b.Ptr(dependencyBuilder + 16, serviceLocator);
        b.Ptr(b.Statics(jobs) + 8, List([dependencyBuilder]));
        return b.Build();
    }
}
