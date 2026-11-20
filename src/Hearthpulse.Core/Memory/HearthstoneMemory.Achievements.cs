using System.Buffers.Binary;
using Hearthpulse.Core.Progression;

namespace Hearthpulse.Core.Memory;

public sealed partial class HearthstoneMemory
{
    // The catalog is some five thousand achievements at a record and two localized texts each, plus some thousands of
    // states; well under this. The time is short on purpose: the read shares its thread with the Arena draft and the ratings.
    private const int AchievementsReadBudget = 600_000;
    private static readonly TimeSpan AchievementsTimeBudget = TimeSpan.FromSeconds(3);

    private static readonly string[] CategoryFields = ["m_ID", "m_name", "m_icon", "m_sortOrder"];
    private static readonly string[] SubcategoryFields = ["m_ID", "m_achievementCategoryId", "m_name", "m_sortOrder"];
    private static readonly string[] SectionFields = ["m_ID", "m_name"];
    private static readonly string[] SectionItemFields = ["m_achievementSubcategoryId", "m_achievementSectionId", "m_sortOrder"];

    private static readonly string[] AchievementFields =
    [
        "m_ID", "m_achievementSectionId", "m_sortOrder", "m_enabled", "m_retired", "m_name", "m_description", "m_achievementVisibility",
        "m_quota", "m_points", "m_nextTierId",
    ];

    private string? _achievementsMissLogged;

    /// <summary>Why the last <see cref="GetAchievements(Func{bool}?, AchievementBook?)"/> gave nothing; null after a read that gave a book.</summary>
    internal string? LastAchievementsMiss { get; private set; }

    /// <summary>
    /// The client's achievements (<c>GameDbf.Achievement</c>, <c>AchievementCategory</c>, <c>AchievementSubcategory</c>,
    /// <c>AchievementSection</c>, <c>AchievementSectionItem</c>) with the player's states (service
    /// <c>Hearthstone.Progression.AchievementManager</c>, <c>m_playerState.m_playerState</c>: a map of achievement id to
    /// state), by the rules of <see cref="AchievementBook.FromClient"/> (docs/collection-cosmetics.md). With
    /// <paramref name="catalog"/> (a book read from the same session of the client) only the states are read and set into
    /// it. Null on another OS than Windows, before the client loaded them, when <paramref name="cancelled"/> says the read
    /// is no longer wanted, or when anything read does not fit together: never a part. Read only; the progress of a game
    /// in progress is not read. The reason for a null goes to the log once per change of reason.
    /// </summary>
    public AchievementBook? GetAchievements(Func<bool>? cancelled = null, AchievementBook? catalog = null) =>
        GetAchievements(cancelled, catalog, AchievementsReadBudget, AchievementsTimeBudget);

    internal AchievementBook? GetAchievements(Func<bool>? cancelled, AchievementBook? catalog, int maxReads, TimeSpan maxTime)
    {
        string? miss = null;
        var book = Safe(rt =>
        {
            var read = new ClientRead(this, rt, cancelled);
            var result = ReadAchievements(read, catalog);
            miss = read.Miss;
            return result;
        }, quiet: true, maxReads, maxTime);
        LastAchievementsMiss = book is not null ? null : miss ?? (IsAttached ? BudgetMiss : "not attached to the client");
        if (LastAchievementsMiss != _achievementsMissLogged)
        {
            _achievementsMissLogged = LastAchievementsMiss;
            if (LastAchievementsMiss is { } reason)
            {
                Diag.Info("Achievements not read: " + reason);
            }
        }

        return book;
    }

    private AchievementBook? ReadAchievements(ClientRead read, AchievementBook? catalog)
    {
        var rt = read.Runtime;
        if (!read.Wanted())
        {
            return null;
        }

        if (ReadAchievementStates(read) is not { } states)
        {
            return null;
        }

        var readAt = DateTime.UtcNow;
        if (catalog is not null)
        {
            return catalog.WithClientStates(states, readAt) ?? read.Fail<AchievementBook>("the states are out of bounds, an id repeats or a date is out of range");
        }

        var gameDbf = rt.FindClass("GameDbf");
        if (gameDbf is null)
        {
            return read.Fail<AchievementBook>("the client's tables were not found");
        }

        var categories = read.Table(gameDbf, "AchievementCategory", CategoryFields, AchievementLimits.MaxCategories);
        var subcategories = categories is null ? null : read.Table(gameDbf, "AchievementSubcategory", SubcategoryFields, AchievementLimits.MaxSubcategories);
        var sections = subcategories is null ? null : read.Table(gameDbf, "AchievementSection", SectionFields, AchievementLimits.MaxEntries);
        var items = sections is null ? null : read.Table(gameDbf, "AchievementSectionItem", SectionItemFields, AchievementLimits.MaxEntries);
        var achievements = items is null ? null : read.Table(gameDbf, "Achievement", AchievementFields, AchievementLimits.MaxEntries);
        if (categories is null || subcategories is null || sections is null || items is null || achievements is null)
        {
            return null;
        }

        var categoryRows = new List<AchievementCategory>(categories.Count);
        for (var i = 0; i < categories.Count; i++)
        {
            if (!read.Wanted() || read.Localized(categories.Ptr(i, "m_name")) is not { } name || read.Text(categories.Ptr(i, "m_icon")) is not { } icon)
            {
                return null;
            }

            categoryRows.Add(new AchievementCategory(categories.Int(i, "m_ID"), name, icon, categories.Int(i, "m_sortOrder")));
        }

        var subcategoryRows = new List<AchievementSubcategory>(subcategories.Count);
        for (var i = 0; i < subcategories.Count; i++)
        {
            if (!read.Wanted() || read.Localized(subcategories.Ptr(i, "m_name")) is not { } name)
            {
                return null;
            }

            subcategoryRows.Add(new AchievementSubcategory(subcategories.Int(i, "m_ID"), subcategories.Int(i, "m_achievementCategoryId"), name,
                subcategories.Int(i, "m_sortOrder")));
        }

        var sectionRows = new List<ClientSection>(sections.Count);
        for (var i = 0; i < sections.Count; i++)
        {
            if (!read.Wanted() || read.Localized(sections.Ptr(i, "m_name")) is not { } name)
            {
                return null;
            }

            sectionRows.Add(new ClientSection(sections.Int(i, "m_ID"), name));
        }

        var itemRows = new List<ClientSectionItem>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            itemRows.Add(new ClientSectionItem(items.Int(i, "m_achievementSubcategoryId"), items.Int(i, "m_achievementSectionId"), items.Int(i, "m_sortOrder")));
        }

        var achievementRows = new List<ClientAchievement>(achievements.Count);
        for (var i = 0; i < achievements.Count; i++)
        {
            if (!read.Wanted()
                || read.Localized(achievements.Ptr(i, "m_name")) is not { } name
                || read.Localized(achievements.Ptr(i, "m_description")) is not { } description)
            {
                return null;
            }

            achievementRows.Add(new ClientAchievement(
                achievements.Int(i, "m_ID"), achievements.Int(i, "m_achievementSectionId"), achievements.Int(i, "m_sortOrder"),
                achievements.Bool(i, "m_enabled"), achievements.Bool(i, "m_retired"), name, description,
                achievements.Int(i, "m_achievementVisibility"), achievements.Int(i, "m_quota"), achievements.Int(i, "m_points"),
                achievements.Int(i, "m_nextTierId")));
        }

        return AchievementBook.FromClient(categoryRows, subcategoryRows, sectionRows, itemRows, achievementRows, states, readAt)
            ?? read.Fail<AchievementBook>("the achievements are out of bounds, an id repeats or a date is out of range");
    }

    /// <summary>
    /// The player's states from the achievement manager's map (Blizzard's <c>Map&lt;int, PlayerAchievementState&gt;</c>):
    /// the live entries below <c>touchedSlots</c>, as many as its <c>count</c>, each key the state's own achievement id,
    /// the map's generation unchanged over the read. Null when anything differs.
    /// </summary>
    private List<ClientAchievementState>? ReadAchievementStates(ClientRead read)
    {
        var rt = read.Runtime;
        var manager = FindService(rt, "Hearthstone.Progression.AchievementManager");
        var stateField = Field(manager.Class, "Achievements.playerState", ["m_playerState"]);
        var playerState = stateField is null ? default : manager[stateField];
        var mapField = Field(playerState.Class, "Achievements.playerState.map", ["m_playerState"]);
        var map = mapField is null ? default : playerState[mapField];
        if (manager.IsNull || map.IsNull)
        {
            return read.Fail<List<ClientAchievementState>>("the achievement manager or its player state was not found (not loaded yet, or renamed)");
        }

        var mapClass = map.Class;
        var keysField = Field(mapClass, "Map.keySlots", ["keySlots"]);
        var valuesField = Field(mapClass, "Map.valueSlots", ["valueSlots"]);
        var touchedField = Field(mapClass, "Map.touchedSlots", ["touchedSlots"], integer: true);
        var countField = Field(mapClass, "Map.count", ["count"], integer: true);
        var generationField = Field(mapClass, "Map.generation", ["generation"], integer: true);
        if (mapClass?.Name != "Map`2" || keysField is null || valuesField is null || touchedField is null || countField is null || generationField is null)
        {
            return read.Fail<List<ClientAchievementState>>("the player state is no map with the fields known");
        }

        var generation = map.Int(generationField);
        var touched = map.Int(touchedField, -1);
        var count = map.Int(countField, -1);
        var keys = map.Array(keysField);
        var values = map.Array(valuesField);
        if (touched < 0 || count < 0 || count > touched || touched > AchievementLimits.MaxEntries || keys.Length < touched || values.Length < touched)
        {
            return read.Fail<List<ClientAchievementState>>("the map's counts are out of range");
        }

        var states = new List<ClientAchievementState>(count);
        if (touched == 0)
        {
            return states;
        }

        var keyBytes = rt.Memory.ReadBytes(keys.Address + MonoOffsets.ArrayData, touched * 4);
        var valueBytes = rt.Memory.ReadBytes(values.Address + MonoOffsets.ArrayData, touched * 8);
        if (keyBytes is null || valueBytes is null)
        {
            return read.Fail<List<ClientAchievementState>>("the map's slots are unreadable");
        }

        MonoClass? stateClass = null;
        ulong stateVTable = 0;
        int idOffset = 0, statusOffset = 0, progressOffset = 0, dateOffset = 0;
        byte[]? stateBytes = null;
        for (var i = 0; i < touched; i++)
        {
            var value = BinaryPrimitives.ReadUInt64LittleEndian(valueBytes.AsSpan(i * 8));
            if (value == 0)
            {
                continue;
            }

            if (!read.Wanted())
            {
                return null;
            }

            if (stateClass is null)
            {
                var first = rt.Object(value);
                stateClass = first.Class;
                var id = Field(stateClass, "Achievements.state.id", ["_AchievementId"], integer: true);
                var status = Field(stateClass, "Achievements.state.status", ["_Status"]);
                var progress = Field(stateClass, "Achievements.state.progress", ["_Progress"], integer: true);
                var date = Field(stateClass, "Achievements.state.completedDate", ["_CompletedDate"]);
                if (stateClass?.Name != "PlayerAchievementState" || id is null || status is null || progress is null || date is null)
                {
                    return read.Fail<List<ClientAchievementState>>("a state is no PlayerAchievementState with the fields known");
                }

                MonoField[] fields = [stateClass.FindField(id)!, stateClass.FindField(status)!, stateClass.FindField(progress)!, stateClass.FindField(date)!];
                if (!Array.TrueForAll(fields, SaneOffset))
                {
                    return read.Fail<List<ClientAchievementState>>("a field of a state is at an offset out of range");
                }

                (idOffset, statusOffset, progressOffset, dateOffset) = (fields[0].Offset, fields[1].Offset, fields[2].Offset, fields[3].Offset);
                stateVTable = rt.Memory.ReadPtr(value);
                stateBytes = new byte[new[] { idOffset + 4, statusOffset + 4, progressOffset + 4, dateOffset + 8 }.Max()];
            }

            if (!rt.Memory.TryRead(value, stateBytes) || !IsOfClass(rt, stateBytes, stateVTable, stateClass))
            {
                return read.Fail<List<ClientAchievementState>>("a state is unreadable or of another class");
            }

            var key = BinaryPrimitives.ReadInt32LittleEndian(keyBytes.AsSpan(i * 4));
            var achievementId = BinaryPrimitives.ReadInt32LittleEndian(stateBytes.AsSpan(idOffset));
            if (key != achievementId || states.Count >= count)
            {
                return read.Fail<List<ClientAchievementState>>("a state's key is not its achievement, or the map holds more than it counts");
            }

            states.Add(new ClientAchievementState(achievementId, BinaryPrimitives.ReadInt32LittleEndian(stateBytes.AsSpan(statusOffset)),
                BinaryPrimitives.ReadInt32LittleEndian(stateBytes.AsSpan(progressOffset)), BinaryPrimitives.ReadInt64LittleEndian(stateBytes.AsSpan(dateOffset))));
        }

        if (states.Count != count)
        {
            return read.Fail<List<ClientAchievementState>>("the map holds fewer states than it counts");
        }

        // A state that changed while it was read (an achievement completed in the menu): the next read gets it whole.
        if (map.Int(generationField) != generation)
        {
            return read.Fail<List<ClientAchievementState>>("the states changed while they were read");
        }

        return states;
    }
}
