using Hearthpulse.Core.Collection;
using Hearthpulse.Core.Memory;

namespace Hearthpulse.Tests;

/// <summary>
/// The collection read from the client's network cache (<see cref="HearthstoneMemory.GetCollection"/>,
/// docs/collection.md) on a hand-built image (<see cref="MonoImageBuilder"/>): the stacks come back as the same
/// numbers, and anything that does not fit together gives no collection at all, never a part of one.
/// </summary>
public sealed class CollectionMemoryTests
{
    // Mono type codes of the fields below.
    private const byte I4 = 0x08, I8 = 0x0A, String = 0x0E, ValueType = 0x11, Class = 0x12, GenericInst = 0x15, SzArray = 0x1D;

    private static readonly (string Id, int Premium, int Count)[] Stacks =
    [
        ("CORE_EX1_001", 0, 2),
        ("TOY_100", 0, 1),
        ("TOY_100", 1, 1),
        ("BT_134", 1, 2),
        ("BE_781", 3, 1),
        ("TLC_463", 2, 1),
        ("TLC_463", 3, 2),
        ("UNG_083", 0, 14),
        // A stack the client keeps with nothing in it (the last copy was disenchanted) is no owned card.
        ("ULD_309", 0, 0),
    ];

    [Fact]
    public void Every_stack_comes_back_as_plain_golden_and_other_copies_with_the_dust()
    {
        var before = DateTime.UtcNow;
        var collection = Read(new Client { Dust = 1350 });

        Assert.NotNull(collection);
        Assert.Equal(1350, collection.Dust);
        Assert.InRange(collection.ReadAt, before, DateTime.UtcNow);
        Assert.Equal(DateTimeKind.Utc, collection.ReadAt.Kind);
        Assert.Equal(
            new Dictionary<string, OwnedCopies>(StringComparer.Ordinal)
            {
                ["CORE_EX1_001"] = new(2, 0, 0),
                ["TOY_100"] = new(1, 1, 0),
                ["BT_134"] = new(0, 2, 0),
                ["BE_781"] = new(0, 0, 1),
                // Diamond and signature are both "other".
                ["TLC_463"] = new(0, 0, 3),
                ["UNG_083"] = new(14, 0, 0),
            },
            collection.Cards);
        Assert.Equal(new OwnedCopies(0, 0, 0), collection.Owned("ULD_309"));
        Assert.Equal(2, collection.Owned("TOY_100").Total);
    }

    [Fact]
    public void A_large_collection_fits_the_read_budget()
    {
        var stacks = Enumerable.Range(0, 9000).Select(i => ($"SET_{i:D4}", i % 4, 1 + (i % 2))).ToArray();
        var collection = Read(new Client { Cards = stacks });

        Assert.NotNull(collection);
        Assert.Equal(9000, collection.Cards.Count);
        Assert.Equal(stacks.Sum(s => s.Item3), collection.Cards.Values.Sum(c => c.Total));
        Assert.Equal(new OwnedCopies(0, 2, 0), collection.Owned("SET_0001"));
    }

    [Fact]
    public void A_client_that_has_not_loaded_its_collection_gives_none()
    {
        Assert.Null(Read(new Client { Cards = [] }));
        Assert.Null(Read(new Client { WithCollection = false }));
        Assert.Null(Read(new Client { WithDust = false }));
    }

    [Theory]
    [InlineData(Damage.StackOfAnotherClass)]
    [InlineData(Damage.NullStack)]
    [InlineData(Damage.NullDefinition)]
    [InlineData(Damage.NegativeCount)]
    [InlineData(Damage.NotACardId)]
    [InlineData(Damage.EmptyCardId)]
    [InlineData(Damage.PremiumOutOfRange)]
    [InlineData(Damage.SizeBeyondTheArray)]
    [InlineData(Damage.NegativeDust)]
    [InlineData(Damage.UnmappedStack)]
    public void One_damaged_stack_gives_no_collection_rather_than_a_part_of_it(Damage damage)
    {
        Assert.Null(Read(new Client { Damage = damage }));
    }

    [Fact]
    public void Renamed_backing_fields_are_still_found()
    {
        // A game update that turns the auto-properties into plain fields: the same names, spelled differently.
        var collection = Read(new Client { Dust = 40, PlainFieldNames = true });

        Assert.NotNull(collection);
        Assert.Equal(40, collection.Dust);
        Assert.Equal(new OwnedCopies(1, 1, 0), collection.Owned("TOY_100"));
    }

    [Fact]
    public void A_dust_balance_that_cannot_be_read_gives_no_collection_rather_than_zero_dust()
    {
        var client = new Client { Dust = 900 };
        var image = client.Build();

        // The page of the balance field fails to read, as memory freed by the game does.
        Assert.Null(Read(new Tampered(image) { Unreadable = client.DustAddress }));
        Assert.Equal(900, Read(new Tampered(image))!.Dust);
    }

    [Fact]
    public void A_collection_that_changes_while_it_is_read_gives_none()
    {
        var client = new Client { Dust = 900 };
        var image = client.Build();

        // A card was crafted during the read: the list is one longer at the second look, or the dust is lower.
        Assert.Null(Read(new Tampered(image) { ChangesOnSecondRead = client.SizeAddress, NewValue = Stacks.Length + 1 }));
        Assert.Null(Read(new Tampered(image) { ChangesOnSecondRead = client.DustAddress, NewValue = 860 }));
        // The list was moved to a new array (it grew).
        Assert.Null(Read(new Tampered(image) { ChangesOnSecondRead = client.ItemsAddress, NewValue = 0x2000_0100 }));
    }

    [Fact]
    public void A_read_that_runs_out_of_its_budget_or_is_no_longer_wanted_gives_none_and_says_why()
    {
        var stacks = Enumerable.Range(0, 3000).Select(i => ($"SET_{i:D4}", 0, 1)).ToArray();
        using var memory = new HearthstoneMemory();
        Assert.True(memory.AttachTo(ProcessMemory.FromSource(new Client { Cards = stacks }.Build())), memory.Status);

        // Too few reads for 3000 stacks, then no time at all.
        Assert.Null(memory.GetCollection(null, maxReads: 2000, TimeSpan.FromSeconds(30)));
        Assert.Contains("budget", memory.LastCollectionMiss, StringComparison.Ordinal);
        Assert.Null(memory.GetCollection(null, maxReads: 600_000, TimeSpan.Zero));

        // A game began before the read, or in the middle of it.
        Assert.Null(memory.GetCollection(() => true));
        Assert.Contains("no longer wanted", memory.LastCollectionMiss, StringComparison.Ordinal);
        var asked = 0;
        Assert.Null(memory.GetCollection(() => ++asked > 3));
        Assert.Equal(4, asked);

        // Nothing of that sticks: the next read is whole, and the reason is gone.
        Assert.Equal(3000, memory.GetCollection(() => false)!.Cards.Count);
        Assert.Null(memory.LastCollectionMiss);
        Assert.Contains("Collection not read: the read was no longer wanted", TestDataFolder.ReadLog(), StringComparison.Ordinal);
    }

    [Fact]
    public void Counts_are_held_to_the_bounds_also_when_stacks_repeat_a_card()
    {
        // The bound itself is read.
        Assert.Equal(10_000, Read(new Client { Cards = [("TOY_100", 0, 10_000)] })!.Owned("TOY_100").Total);
        Assert.Equal(new OwnedCopies(4000, 4000, 2000), Read(new Client { Cards = [("TOY_100", 0, 4000), ("TOY_100", 1, 4000), ("TOY_100", 2, 2000)] })!.Owned("TOY_100"));

        // One stack past it, and stacks of one card that add up past it (in one finish, or over the finishes).
        Assert.Null(Read(new Client { Cards = [("TOY_100", 0, 10_001)] }));
        Assert.Null(Read(new Client { Cards = [("TOY_100", 0, 6000), ("TOY_100", 0, 6000)] }));
        Assert.Null(Read(new Client { Cards = [("TOY_100", 0, 6000), ("BT_134", 0, 1), ("TOY_100", 1, 6000)] }));
        Assert.Null(Read(new Client { Cards = [("TOY_100", 0, int.MaxValue), ("TOY_100", 0, 5)] }));
        Assert.Null(Read(new Client { Dust = 1_000_000_001 }));
        Assert.Equal(1_000_000_000, Read(new Client { Dust = 1_000_000_000 })!.Dust);
    }

    [Fact]
    public void A_collection_of_empty_stacks_only_is_no_collection()
    {
        Assert.Null(Read(new Client { Cards = [("TOY_100", 0, 0), ("BT_134", 1, 0)] }));
    }

    private static CardCollection? Read(Client client) => Read(client.Build());

    private static CardCollection? Read(IMemorySource source)
    {
        using var memory = new HearthstoneMemory();
        Assert.True(memory.AttachTo(ProcessMemory.FromSource(source)), memory.Status);
        var collection = memory.GetCollection();
        // A read gives a collection or a reason, never neither.
        Assert.Equal(collection is null, memory.LastCollectionMiss is not null);
        return collection;
    }

    /// <summary>An image whose one address cannot be read, or holds another value from its second read on.</summary>
    private sealed class Tampered(MemoryImage image) : IMemorySource
    {
        private int _reads;

        public ulong Unreadable { get; init; }

        public ulong ChangesOnSecondRead { get; init; }

        public long NewValue { get; init; }

        public bool HasExited => false;

        public ulong FindModuleBase(string moduleName) => image.FindModuleBase(moduleName);

        public bool TryRead(ulong address, Span<byte> buffer)
        {
            if (address == Unreadable && address != 0)
            {
                return false;
            }

            if (!image.TryRead(address, buffer))
            {
                return false;
            }

            if (address == ChangesOnSecondRead && address != 0 && ++_reads >= 2)
            {
                Span<byte> value = stackalloc byte[8];
                System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(value, NewValue);
                value[..buffer.Length].CopyTo(buffer);
            }

            return true;
        }
    }

    public enum Damage
    {
        None,
        StackOfAnotherClass,
        NullStack,
        NullDefinition,
        NegativeCount,
        NotACardId,
        EmptyCardId,
        PremiumOutOfRange,
        SizeBeyondTheArray,
        NegativeDust,
        UnmappedStack,
    }

    /// <summary>A client's service locator with NetCache holding the collection and the dust balance.</summary>
    private sealed class Client
    {
        public (string Id, int Premium, int Count)[] Cards { get; init; } = Stacks;

        public long Dust { get; init; } = 1200;

        public bool WithCollection { get; init; } = true;

        public bool WithDust { get; init; } = true;

        public bool PlainFieldNames { get; init; }

        public Damage Damage { get; init; }

        /// <summary>Where the balance, the list's size and the list's array pointer are, once built.</summary>
        public ulong DustAddress { get; private set; }

        public ulong SizeAddress { get; private set; }

        public ulong ItemsAddress { get; private set; }

        public MemoryImage Build()
        {
            string Name(string property) => PlainFieldNames ? "m_" + char.ToLowerInvariant(property[0]) + property[1..] : $"<{property}>k__BackingField";

            var b = new MonoImageBuilder();
            var objects = b.ArrayClass("Object[]", 8);
            var entries = b.ArrayClass("Entry[]", 24);
            var list = b.Class("System.Collections.Generic.List`1", ("_items", 16, false, SzArray), ("_size", 24, false, I4));
            var dictionary = b.Class("System.Collections.Generic.Dictionary`2", ("_entries", 16, false, SzArray), ("_count", 24, false, I4));
            var jobs = b.Class("Hearthstone.HearthstoneJobs", ("s_dependencyBuilder", 8, true, Class));
            var builder = b.Class("Hearthstone.DependencyBuilder", ("m_serviceLocator", 16, false, Class));
            var locator = b.Class("Hearthstone.ServiceLocator", ("m_services", 16, false, Class));
            var service = b.Class("Hearthstone.ServiceRecord",
                ("<ServiceTypeName>k__BackingField", 16, false, String), ("<Service>k__BackingField", 24, false, Class));
            var netCacheClass = b.Class("NetCache", ("m_netCache", 16, false, Class));
            var map = b.Class("Blizzard.T5.Core.Map`2", ("valueSlots", 16, false, SzArray));
            var otherCache = b.Class("NetCacheMedalInfo", ("m_unused", 16, false, I4));
            // The layout the client had on 2026-11-16 (docs/collection.md).
            var collectionClass = b.Class("NetCacheCollection",
                (Name("Stacks"), 0x10, false, GenericInst), ("TotalCardsOwned", 0x20, false, I4), ("CoreCardsUnlockedPerClass", 0x18, false, GenericInst));
            var dustClass = b.Class("NetCacheArcaneDustBalance", (Name("Balance"), 0x10, false, I8));
            var stackClass = b.Class("CardStack",
                (Name("Def"), 0x10, false, Class), (Name("Date"), 0x18, false, I8), (Name("Count"), 0x20, false, I4), (Name("NumSeen"), 0x24, false, I4));
            var definitionClass = b.Class("CardDefinition", (Name("Name"), 0x10, false, String), (Name("Premium"), 0x18, false, ValueType));

            var stacks = b.Array(objects, Cards.Length + 2);
            for (var i = 0; i < Cards.Length; i++)
            {
                var (id, premium, count) = Cards[i];
                var damaged = i == 3 ? Damage : Damage.None;
                var definition = b.Object(definitionClass, 0x20);
                b.Ptr(definition + 0x10, b.MonoString(damaged switch { Damage.NotACardId => "Player One", Damage.EmptyCardId => string.Empty, _ => id }));
                b.Int(definition + 0x18, damaged == Damage.PremiumOutOfRange ? 4000 : premium);
                var stack = b.Object(damaged == Damage.StackOfAnotherClass ? otherCache : stackClass, 0x28);
                b.Ptr(stack + 0x10, damaged == Damage.NullDefinition ? 0 : definition);
                b.Int(stack + 0x20, damaged == Damage.NegativeCount ? -1 : count);
                b.Ptr(b.Element(stacks, i), damaged switch { Damage.NullStack => 0, Damage.UnmappedStack => 0x7000_0000, _ => stack });
            }

            var stackList = b.Object(list);
            b.Ptr(stackList + 16, stacks);
            b.Int(stackList + 24, Damage == Damage.SizeBeyondTheArray ? Cards.Length + 50 : Cards.Length);
            var collection = b.Object(collectionClass);
            b.Ptr(collection + 0x10, stackList);
            b.Int(collection + 0x20, unchecked((int)Cards.Sum(c => (long)c.Count)));
            var dust = b.Object(dustClass);
            b.Long(dust + 0x10, Damage == Damage.NegativeDust ? -5 : Dust);
            (DustAddress, SizeAddress, ItemsAddress) = (dust + 0x10, stackList + 24, stackList + 16);

            // NetCache's map: another cache first, then ours, and an empty slot.
            var slots = b.Array(objects, 4);
            b.Ptr(b.Element(slots, 0), b.Object(otherCache));
            if (WithCollection) b.Ptr(b.Element(slots, 1), collection);
            if (WithDust) b.Ptr(b.Element(slots, 3), dust);
            var netCacheMap = b.Object(map);
            b.Ptr(netCacheMap + 16, slots);
            var netCache = b.Object(netCacheClass);
            b.Ptr(netCache + 16, netCacheMap);

            // The service locator's dictionary: entries of 24 bytes (hash, next, key, value), the value last.
            var record = b.Object(service);
            b.Ptr(record + 16, b.MonoString("NetCache"));
            b.Ptr(record + 24, netCache);
            var records = b.Array(entries, 1, 24);
            b.Ptr(b.Element(records, 0, 24) + 16, record);
            var services = b.Object(dictionary);
            b.Ptr(services + 16, records);
            b.Int(services + 24, 1);
            var serviceLocator = b.Object(locator);
            b.Ptr(serviceLocator + 16, services);
            var dependencyBuilder = b.Object(builder);
            b.Ptr(dependencyBuilder + 16, serviceLocator);
            var builders = b.Array(objects, 1);
            b.Ptr(b.Element(builders, 0), dependencyBuilder);
            var builderList = b.Object(list);
            b.Ptr(builderList + 16, builders);
            b.Int(builderList + 24, 1);
            b.Ptr(b.Statics(jobs) + 8, builderList);
            return b.Build();
        }
    }
}
