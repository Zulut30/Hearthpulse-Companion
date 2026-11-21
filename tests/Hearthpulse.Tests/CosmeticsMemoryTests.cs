using Hearthpulse.Core.Collection;
using Hearthpulse.Core.Memory;

namespace Hearthpulse.Tests;

/// <summary>
/// The card backs and coins read from the client (<see cref="HearthstoneMemory.GetCosmetics(Func{bool}?)"/>,
/// docs/collection-cosmetics.md) on a hand-built image (<see cref="ClientImage"/>): the catalog keeps the enabled
/// entries without the random back, owned and favourite ids come back one by one, names are the localized values, and
/// anything that does not fit together gives no cosmetics at all.
/// </summary>
public sealed class CosmeticsMemoryTests
{
    [Fact]
    public void The_catalog_the_owned_and_the_favourites_come_back_as_the_client_holds_them()
    {
        var before = DateTime.UtcNow;
        using var memory = ClientImage.Attach(new ClientImage().Build());

        var cosmetics = memory.GetCosmetics();

        Assert.NotNull(cosmetics);
        Assert.Null(memory.LastCosmeticsMiss);
        Assert.Equal(
            [
                new CosmeticCardBack(0, "Classic", "The first card back.", CardBackCategory.Base, 0),
                new CosmeticCardBack(7, "Сезонная рубашка", "Описание.", CardBackCategory.Seasonal, 2),
                new CosmeticCardBack(12, "Odd group", string.Empty, CardBackCategory.Unknown, 4),
            ],
            cosmetics.CardBacks);
        Assert.Equal([new CosmeticCoin(1, 1746, "The Coin", string.Empty), new CosmeticCoin(3, 1747, "Монетка", string.Empty)], cosmetics.Coins);
        Assert.Equal([0, 7, 12, 400], cosmetics.OwnedCardBacks.Order());
        Assert.Equal([7], cosmetics.FavoriteCardBacks);
        Assert.Equal([1, 3], cosmetics.OwnedCoins.Order());
        Assert.Equal([3], cosmetics.FavoriteCoins);
        Assert.InRange(cosmetics.ReadAt, before, DateTime.UtcNow);
        Assert.Equal(DateTimeKind.Utc, cosmetics.ReadAt.Kind);
        // Confirmation is the watch's to give, never the read's.
        Assert.False(cosmetics.Confirmed);
    }

    [Fact]
    public void Empty_sets_are_read_as_empty()
    {
        using var memory = ClientImage.Attach(new ClientImage { FavoriteCardBacks = [], FavoriteCoins = [], OwnedCoins = [] }.Build());

        var cosmetics = memory.GetCosmetics();

        Assert.NotNull(cosmetics);
        Assert.Empty(cosmetics.FavoriteCardBacks);
        Assert.Empty(cosmetics.OwnedCoins);
    }

    [Fact]
    public void A_client_that_has_not_loaded_its_cosmetics_gives_none_and_says_why()
    {
        using var memory = ClientImage.Attach(new ClientImage { WithCosmetics = false }.Build());

        Assert.Null(memory.GetCosmetics());
        Assert.Contains("not loaded", memory.LastCosmeticsMiss, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ClientDamage.CardBackOfAnotherClass, "another class")]
    [InlineData(ClientDamage.NameOfAnotherClass, "localized value")]
    [InlineData(ClientDamage.UnreadableName, "unreadable")]
    [InlineData(ClientDamage.SetCountsMore, "count")]
    [InlineData(ClientDamage.HashNotValue, "hash code")]
    [InlineData(ClientDamage.SetOfAnotherClass, "HashSet")]
    [InlineData(ClientDamage.ListBeyondItsArray, "no list, too many or unreadable")]
    [InlineData(ClientDamage.LocOffsetOutOfRange, "offset out of range")]
    [InlineData(ClientDamage.RecordOffsetOutOfRange, "offset out of range")]
    [InlineData(ClientDamage.SetLastIndexOutOfRange, "last index is out of range")]
    public void Any_damage_gives_no_cosmetics_rather_than_a_part_and_says_why(ClientDamage damage, string reason)
    {
        using var memory = ClientImage.Attach(new ClientImage { Damage = damage }.Build());

        Assert.Null(memory.GetCosmetics());
        Assert.Contains(reason, memory.LastCosmeticsMiss, StringComparison.Ordinal);
    }

    [Fact]
    public void Cosmetics_that_change_while_they_are_read_give_none()
    {
        var client = new ClientImage();
        var image = client.Build();

        // A card back was earned during the read: the set's version differs at the second look.
        using var memory = ClientImage.Attach(new ChangingSource(image, client.CardBacksVersionAddress, 18));

        Assert.Null(memory.GetCosmetics());
        Assert.Contains("changed while", memory.LastCosmeticsMiss, StringComparison.Ordinal);
    }

    [Fact]
    public void A_game_that_begins_in_the_middle_of_the_read_stops_it()
    {
        // More card backs than rows between two questions: the read is asked again in the middle of the table.
        var backs = Enumerable.Range(0, 300).Select(i => (i, "Back " + i, string.Empty, 1, i, true, false)).ToArray();
        using var memory = ClientImage.Attach(new ClientImage { CardBacks = backs, OwnedCardBacks = [0, 1] }.Build());
        var asked = 0;

        // Still wanted when the read starts, a game from the next question on.
        Assert.Null(memory.GetCosmetics(() => ++asked > 1));

        Assert.Equal(2, asked);
        Assert.Contains("no longer wanted", memory.LastCosmeticsMiss, StringComparison.Ordinal);
        Assert.Equal(300, memory.GetCosmetics(() => false)!.CardBacks.Count);
    }

    [Fact]
    public void A_read_out_of_budget_or_no_longer_wanted_gives_none_and_the_reason_is_logged_once_per_change()
    {
        using var memory = ClientImage.Attach(new ClientImage().Build());

        Assert.Null(memory.GetCosmetics(null, maxReads: 50, TimeSpan.FromSeconds(30)));
        Assert.Contains("budget", memory.LastCosmeticsMiss, StringComparison.Ordinal);
        Assert.Null(memory.GetCosmetics(null, maxReads: 200_000, TimeSpan.Zero));
        Assert.Contains("budget", memory.LastCosmeticsMiss, StringComparison.Ordinal);

        // A game began before the read, twice: the reason is logged once. (The tests of this class run one after
        // another, and no other class reads cosmetics: the count of the line moves only here.)
        static int Logged() => System.Text.RegularExpressions.Regex.Matches(TestDataFolder.ReadLog(), "Cosmetics not read: the read was no longer wanted").Count;
        var before = Logged();
        Assert.Null(memory.GetCosmetics(() => true));
        Assert.Contains("no longer wanted", memory.LastCosmeticsMiss, StringComparison.Ordinal);
        Assert.Null(memory.GetCosmetics(() => true));
        Assert.Equal(before + 1, Logged());

        // Nothing of that sticks.
        Assert.NotNull(memory.GetCosmetics(() => false));
        Assert.Null(memory.LastCosmeticsMiss);
    }

    /// <summary>An image whose one 4-byte value reads as another from its second read on.</summary>
    internal sealed class ChangingSource(MemoryImage image, ulong address, int newValue) : IMemorySource
    {
        private int _reads;

        public bool HasExited => false;

        public ulong FindModuleBase(string moduleName) => image.FindModuleBase(moduleName);

        public bool TryRead(ulong at, Span<byte> buffer)
        {
            if (!image.TryRead(at, buffer))
            {
                return false;
            }

            if (address != 0 && at <= address && address + 4 <= at + (ulong)buffer.Length && ++_reads >= 2)
            {
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(buffer[(int)(address - at)..], newValue);
            }

            return true;
        }
    }
}
