using System.Globalization;
using System.Text;
using System.Text.Json;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Data;

namespace Hearthpulse.Tests.OwnStats;

/// <summary>
/// The stats pool reads a record's deck code the way the client does (TASK-2026-11-26-pro-collection B2, AC10).
/// <c>server/pool/test/fixtures/deckcodes.json</c> holds codes with the list, key and canonical code the server's
/// <c>node/records/derive/deck.mjs</c> gives them (<c>derive-deck.test.mjs</c> checks that side); here
/// <see cref="DeckString"/> must read the same lists, <see cref="DeckRecords.CardListKey"/> give the same keys and
/// <see cref="DeckString.Encode"/> write the same canonical codes.
/// </summary>
public sealed class DeckKeyAgreementTests
{
    private static readonly JsonElement Fixture = JsonSerializer.Deserialize<JsonElement>(
        File.ReadAllText(Path.Combine(RepoFiles.Root, "server", "pool", "test", "fixtures", "deckcodes.json")));

    public static TheoryData<string> Lists => Names("cases", IsList);

    public static TheoryData<string> BrokenCodes => Names("cases", c => !IsList(c));

    public static TheoryData<string> StricterCodes => Names("stricter", _ => true);

    [Theory]
    [MemberData(nameof(Lists))]
    public void The_client_reads_the_code_into_the_list_the_server_keys(string name)
    {
        var c = Case("cases", name);
        var code = c.GetProperty("code").GetString()!;
        var key = c.GetProperty("key").GetString()!;

        var deck = DeckString.TryDecode(code);

        Assert.NotNull(deck);
        Assert.Equal(ListOf(key), deck.Cards.OrderBy(e => e.Key).Select(e => (e.Key, e.Value)));
        Assert.Equal(key, DeckRecords.CardListKey(code));
        Assert.Equal(c.GetProperty("heroes").EnumerateArray().Select(h => h.GetInt32()), deck.HeroDbfIds);
        Assert.Equal(FormatName(c.GetProperty("fmt").GetString()), deck.Format);
        if (c.GetProperty("sideboard").GetString() is { } sideboard)
        {
            Assert.False(deck.SideboardUnread);
            Assert.Equal(ListOf(sideboard), deck.Sideboard.OrderBy(e => e.Key).Select(e => (e.Key, e.Value)));
        }
        else
        {
            Assert.True(deck.SideboardUnread);
        }
    }

    [Fact]
    public void Codes_of_one_list_share_the_client_key_and_other_lists_do_not()
    {
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var c in Fixture.GetProperty("cases").EnumerateArray().Where(IsList))
        {
            var key = DeckRecords.CardListKey(c.GetProperty("code").GetString()!);
            var group = c.GetProperty("group").GetString()!;
            if (keys.TryGetValue(group, out var same))
            {
                Assert.Equal(same, key);
            }
            else
            {
                keys[group] = key;
            }
        }

        Assert.True(keys.Count >= 8, "the fixture lost its lists");
        Assert.Equal(keys.Count, keys.Values.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [MemberData(nameof(Lists))]
    public void The_client_writes_the_canonical_code_and_reads_it_back_with_the_default_hero(string name)
    {
        var c = Case("cases", name);
        var list = ListOf(c.GetProperty("key").GetString()!);
        var canonical = c.GetProperty("canonical");
        var cls = canonical.GetProperty("cls").GetString()!;
        var format = FormatName(canonical.GetProperty("fmt").GetString());
        var expected = canonical.GetProperty("code").GetString()!;
        var cards = CardsOf(list.Select(e => e.Dbf));

        var written = DeckString.Encode(list.ToDictionary(e => CardId(e.Dbf), e => e.Copies, StringComparer.Ordinal), cls, format, cards);
        var back = DeckString.TryDecode(expected);

        Assert.Equal(expected, written);
        Assert.NotNull(back);
        Assert.Equal(list, back.Cards.OrderBy(e => e.Key).Select(e => (e.Key, e.Value)));
        Assert.Equal([cards.Get(DeckString.ClassHero(cls))!.DbfId], back.HeroDbfIds);
        Assert.Equal(format, back.Format);
        Assert.Empty(back.Sideboard);
        Assert.False(back.SideboardUnread);
    }

    [Theory]
    [MemberData(nameof(BrokenCodes))]
    public void A_code_the_server_finds_broken_the_client_refuses_too(string name)
    {
        Assert.Null(DeckString.TryDecode(Case("cases", name).GetProperty("code").GetString()!));
    }

    /// <summary>
    /// What only the server refuses: codes no game writes (a count running past the cards, no cards, no copies, a
    /// six-byte varint, hero dbf 0). The client reads them as the fixture records, so the two differ only there.
    /// </summary>
    [Theory]
    [MemberData(nameof(StricterCodes))]
    public void A_code_no_game_writes_still_reads_on_the_client_as_recorded(string name)
    {
        var c = Case("stricter", name);
        var code = c.GetProperty("code").GetString()!;

        Assert.NotNull(DeckString.TryDecode(code));
        Assert.Equal(c.GetProperty("client").GetString(), DeckRecords.CardListKey(code));
    }

    [Fact]
    public void The_default_heroes_of_the_server_are_those_ClassHero_names()
    {
        var heroes = Fixture.GetProperty("heroes").EnumerateObject().ToList();

        Assert.Equal(11, heroes.Count);
        foreach (var hero in heroes)
        {
            Assert.Equal(hero.Value.GetProperty("id").GetString(), DeckString.ClassHero(hero.Name));
        }
    }

    /// <summary>A case with a list (its key is a string); the others are broken codes (key null).</summary>
    private static bool IsList(JsonElement c) => c.GetProperty("key").ValueKind == JsonValueKind.String;

    private static TheoryData<string> Names(string section, Func<JsonElement, bool> which)
    {
        var data = new TheoryData<string>();
        foreach (var c in Fixture.GetProperty(section).EnumerateArray().Where(which))
        {
            data.Add(c.GetProperty("name").GetString()!);
        }

        return data;
    }

    private static JsonElement Case(string section, string name) =>
        Fixture.GetProperty(section).EnumerateArray().Single(c => string.Equals(c.GetProperty("name").GetString(), name, StringComparison.Ordinal));

    /// <summary>"&lt;dbf&gt;x&lt;copies&gt;,..." as (dbf, copies) pairs in the order written.</summary>
    private static List<(int Dbf, int Copies)> ListOf(string key)
    {
        if (key.Length == 0)
        {
            return [];
        }

        return key.Split(',')
            .Select(entry => entry.Split('x'))
            .Select(p => (Dbf: int.Parse(p[0], CultureInfo.InvariantCulture), Copies: int.Parse(p[1], CultureInfo.InvariantCulture)))
            .ToList();
    }

    private static string FormatName(string? fmt) => fmt switch
    {
        "wild" => "Wild",
        "standard" => "Standard",
        "classic" => "Classic",
        "twist" => "Twist",
        _ => "Unknown",
    };

    private static string CardId(int dbf) => "T_" + dbf.ToString(CultureInfo.InvariantCulture);

    /// <summary>The fixture's default heroes (card id and HearthstoneJSON dbf id) and a stand-in card for each listed dbf id.</summary>
    private static CardDb CardsOf(IEnumerable<int> dbfs)
    {
        var cards = Fixture.GetProperty("heroes").EnumerateObject()
            .Select(h => new { id = h.Value.GetProperty("id").GetString(), dbfId = h.Value.GetProperty("dbf").GetInt32() })
            .Concat(dbfs.Select(dbf => new { id = (string?)CardId(dbf), dbfId = dbf }));
        return CardDb.Parse("enUS", new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cards))));
    }
}
