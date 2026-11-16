using System.Buffers.Binary;
using Hearthpulse.Core.Collection;

namespace Hearthpulse.Core.Memory;

public sealed partial class HearthstoneMemory
{
    // A collection is some thousands of stacks at four reads each; the usual scan budget would cut it short. The time
    // is short on purpose: the read shares its thread with the Arena draft and the ratings.
    private const int CollectionReadBudget = 600_000;
    private static readonly TimeSpan CollectionTimeBudget = TimeSpan.FromSeconds(3);

    // How often a long read asks whether it is still wanted.
    private const int CancelCheckEvery = 256;

    // TAG_PREMIUM as this client declares it: NORMAL, GOLDEN, then DIAMOND, SIGNATURE and whatever comes later. The
    // numbers are an assumption (docs/collection.md): they only decide which of the three counts a copy lands in.
    private const int PremiumNormal = 0;
    private const int PremiumGolden = 1;
    private const int MaxPremium = 32;

    private const string BudgetMiss = "the read ran out of its read or time budget, or failed";

    private string? _collectionMiss;
    private string? _collectionMissLogged;

    /// <summary>Why the last <see cref="GetCollection(Func{bool}?)"/> gave nothing; null after a read that gave a collection.</summary>
    internal string? LastCollectionMiss { get; private set; }

    /// <summary>
    /// The player's collection and dust balance, read from the client's network cache (docs/collection.md):
    /// <c>NetCacheCollection.Stacks</c>, a list of card stacks (card id, finish, count), and
    /// <c>NetCacheArcaneDustBalance.Balance</c>. Null when the client has not loaded them yet, on another OS than
    /// Windows, when <paramref name="cancelled"/> says the read is no longer wanted (a game began), or when anything
    /// read does not fit together (a stack of another class, a card id that is no card id, a count out of range, a
    /// balance that cannot be read, a list that changed while it was read): a collection with holes would call owned
    /// cards missing, so it is never returned in part. Read only, like every other value here. The reason for a null
    /// goes to the log once per change of reason (never the collection itself).
    /// </summary>
    public CardCollection? GetCollection(Func<bool>? cancelled = null) => GetCollection(cancelled, CollectionReadBudget, CollectionTimeBudget);

    internal CardCollection? GetCollection(Func<bool>? cancelled, int maxReads, TimeSpan maxTime)
    {
        _collectionMiss = null;
        var collection = Safe(rt => ReadCollection(rt, cancelled), quiet: true, maxReads, maxTime);
        LastCollectionMiss = collection is not null ? null
            : _collectionMiss ?? (IsAttached ? BudgetMiss : "not attached to the client");
        if (LastCollectionMiss != _collectionMissLogged)
        {
            _collectionMissLogged = LastCollectionMiss;
            if (LastCollectionMiss is { } reason)
            {
                Diag.Info("Collection not read: " + reason);
            }
        }

        return collection;
    }

    private CardCollection? Miss(string reason)
    {
        // A read that failed because the budget was spent is a spent budget, whatever it was reading then.
        _collectionMiss = ProcessMemory.BudgetExhausted ? BudgetMiss : reason;
        return null;
    }

    private CardCollection? ReadCollection(MonoRuntime rt, Func<bool>? cancelled)
    {
        var cache = FindNetCacheObject(rt, "NetCacheCollection");
        var dustCache = FindNetCacheObject(rt, "NetCacheArcaneDustBalance");
        if (cache.IsNull || dustCache.IsNull)
        {
            return Miss("the client has not loaded its collection or dust balance (or their classes were renamed)");
        }

        var stacksField = Field(cache.Class, "Collection.stacks", ["<Stacks>k__BackingField"]);
        var dustField = Field(dustCache.Class, "Collection.dust", ["<Balance>k__BackingField"]);
        if (stacksField is null || dustField is null)
        {
            return Miss("the stacks or the balance field was not found");
        }

        // A balance that cannot be read is no balance: it must not come back as 0.
        if (ReadDust(rt, dustCache, dustField) is not { } dust)
        {
            return Miss("the dust balance could not be read or is out of range");
        }

        var list = cache[stacksField];
        var items = list.Array("_items");
        var size = list.Int("_size", -1);
        // An empty list is a collection that is still loading: every account owns cards.
        if (size is <= 0 or > CollectionLimits.MaxStacks || items.Length < size
            || rt.Memory.ReadBytes(items.Address + MonoOffsets.ArrayData, size * 8) is not { } pointers)
        {
            return Miss("the stack list is empty, too long or unreadable");
        }

        var first = rt.Object(BinaryPrimitives.ReadUInt64LittleEndian(pointers));
        var stackClass = first.Class;
        var defField = Field(stackClass, "Collection.stack.def", ["<Def>k__BackingField"]);
        var countField = Field(stackClass, "Collection.stack.count", ["<Count>k__BackingField"], integer: true);
        var definition = defField is null ? default : first[defField];
        var defClass = definition.Class;
        var nameField = Field(defClass, "Collection.def.name", ["<Name>k__BackingField"]);
        var premiumField = Field(defClass, "Collection.def.premium", ["<Premium>k__BackingField"]);
        if (stackClass?.Name != "CardStack" || defClass is null || defField is null || countField is null || nameField is null || premiumField is null)
        {
            return Miss("the first stack is not a card stack with the fields known");
        }

        // Every stack is one object of the first one's class: the offsets are found once, then each stack costs one
        // read for the stack, one for its card definition and two for the card id.
        var stackVTable = rt.Memory.ReadPtr(first.Address);
        var defVTable = rt.Memory.ReadPtr(definition.Address);
        var defOffset = stackClass.FindField(defField)!.Offset;
        var countOffset = stackClass.FindField(countField)!.Offset;
        var nameOffset = defClass.FindField(nameField)!.Offset;
        var premiumOffset = defClass.FindField(premiumField)!.Offset;
        var stackBytes = new byte[Math.Max(defOffset + 8, countOffset + 4)];
        var defBytes = new byte[Math.Max(nameOffset + 8, premiumOffset + 4)];

        var cards = new Dictionary<string, OwnedCopies>(size, StringComparer.Ordinal);
        for (var i = 0; i < size; i++)
        {
            if (i % CancelCheckEvery == 0 && cancelled?.Invoke() == true)
            {
                return Miss("the read was no longer wanted (a game began or the client went away)");
            }

            var stack = BinaryPrimitives.ReadUInt64LittleEndian(pointers.AsSpan(i * 8));
            if (stack == 0 || !rt.Memory.TryRead(stack, stackBytes) || !IsOfClass(rt, stackBytes, stackVTable, stackClass))
            {
                return Miss("a stack is null, unreadable or of another class");
            }

            var def = BinaryPrimitives.ReadUInt64LittleEndian(stackBytes.AsSpan(defOffset));
            var count = BinaryPrimitives.ReadInt32LittleEndian(stackBytes.AsSpan(countOffset));
            if (def == 0 || count is < 0 or > CollectionLimits.MaxCopies || !rt.Memory.TryRead(def, defBytes)
                || !IsOfClass(rt, defBytes, defVTable, defClass))
            {
                return Miss("a stack's count is out of range or its card definition is null, unreadable or of another class");
            }

            var premium = BinaryPrimitives.ReadInt32LittleEndian(defBytes.AsSpan(premiumOffset));
            var id = rt.Memory.ReadMonoString(BinaryPrimitives.ReadUInt64LittleEndian(defBytes.AsSpan(nameOffset)));
            if (premium is < 0 or > MaxPremium || !CollectionLimits.IsCardId(id))
            {
                return Miss("a stack's finish is out of range or its card id is no card id");
            }

            if (count == 0)
            {
                continue;
            }

            var owned = cards.GetValueOrDefault(id!);
            owned = premium switch
            {
                PremiumNormal => owned with { Normal = owned.Normal + count },
                PremiumGolden => owned with { Golden = owned.Golden + count },
                _ => owned with { Other = owned.Other + count },
            };
            // Stacks repeating one card must not add up past the bounds either (each is at most MaxCopies, so no overflow).
            if (!CollectionLimits.Fits(owned) || (cards.Count >= CollectionLimits.MaxCards && !cards.ContainsKey(id!)))
            {
                return Miss("the copies of one card, or the number of cards, are out of range");
            }

            cards[id!] = owned;
        }

        // Cards crafted, disenchanted or opened while the list was read: the next read gets them whole.
        if (list.Int("_size", -1) != size || list.Array("_items").Address != items.Address || ReadDust(rt, dustCache, dustField) != dust)
        {
            return Miss("the collection or the dust changed while it was read");
        }

        return cards.Count == 0 ? Miss("every stack is empty") : new CardCollection(cards, dust, DateTime.UtcNow);
    }

    /// <summary>The balance field, or null when its bytes cannot be read or are no balance.</summary>
    private static long? ReadDust(MonoRuntime rt, MonoObject dustCache, string field)
    {
        Span<byte> bytes = stackalloc byte[8];
        var address = dustCache.FieldAddress(field);
        if (address == 0 || !rt.Memory.TryRead(address, bytes))
        {
            return null;
        }

        var dust = BinaryPrimitives.ReadInt64LittleEndian(bytes);
        return dust is < 0 or > CollectionLimits.MaxDust ? null : dust;
    }

    /// <summary>
    /// Whether the object whose first bytes were read is of <paramref name="cls"/>: its vtable is the one the first
    /// object of that class had (the usual case, no further read), or another vtable of the same class.
    /// </summary>
    private static bool IsOfClass(MonoRuntime rt, ReadOnlySpan<byte> objectBytes, ulong knownVTable, MonoClass cls)
    {
        var vtable = BinaryPrimitives.ReadUInt64LittleEndian(objectBytes);
        return vtable != 0 && (vtable == knownVTable || rt.Memory.ReadPtr(vtable) == cls.Address);
    }
}
