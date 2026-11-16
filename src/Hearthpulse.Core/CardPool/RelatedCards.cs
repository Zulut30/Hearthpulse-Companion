using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Localization;

namespace Hearthpulse.Core.CardPool;

/// <summary>
/// The cards a card in hand is about, shown beside it when hovered (<c>docs/discover-pool.md</c>, "Related cards"): the
/// tokens it makes and the collectible cards its text names. From the card data only, no hand-made table: a token's id
/// is its card's id with a suffix (<c>CS2_101</c> → <c>CS2_101t</c>, <c>BE_017</c> → <c>BE_017t3</c>, Choose One options
/// <c>EX1_154a</c> or <c>EDR_872A</c>; a <c>CORE_</c> or <c>Core_</c> reprint's tokens keep the original id), and a named
/// card is a collectible card's English name in the English text, where the text leads into it as a card and the name
/// means one card (<see cref="Resolve"/>); a name that could be another card gives no row rather than a guess. The
/// same popup and rows as the "this game" list (<see cref="MatchLists"/>).
/// </summary>
public static partial class RelatedCards
{
    /// <summary>Card types a related card can be: never an enchantment, a hero power or the game's own entities.</summary>
    private static readonly HashSet<string> Types = new(StringComparer.Ordinal) { "MINION", "SPELL", "WEAPON", "LOCATION", "HERO" };

    /// <summary>Game keywords that are also card names but no mechanic in the card data (the text uses them as keywords).</summary>
    private static readonly string[] TextKeywords = ["Rewind", "Resurrect", "Silence", "Freeze", "Discover", "Recruit", "Corrupt", "Overload", "Shatter"];

    /// <summary>Keywords whose object is a card: "Herald Sinestra", "a Temporary Backstab".</summary>
    private static readonly HashSet<string> CardKeywords = new(StringComparer.OrdinalIgnoreCase) { "Herald", "Temporary" };

    /// <summary>The set whose non-collectible ids are other skins, never tokens (<c>HERO_01b</c> → <c>HERO_01ba</c>).</summary>
    private const string SkinSet = "HERO_SKINS";

    /// <summary>The longest list: a card that names more is about something else (a pool, a class).</summary>
    private const int MaxRows = 12;

    private static readonly ConditionalWeakTable<CardDb, Dictionary<string, List<string>>> Tokens = new();

    private static readonly ConditionalWeakTable<CardPoolCatalog, NameIndex> Names = new();

    /// <summary>The related list of <paramref name="cardId"/>, or null when it has none.</summary>
    public static MatchListView? Of(string cardId, CardDb cards, CardPoolCatalog catalog)
    {
        if (cards.Get(cardId) is not { } source)
        {
            return null;
        }

        var ids = new List<string>();
        var tokens = Tokens.GetValue(cards, BuildTokens);
        foreach (var root in new[] { cardId, cardId.StartsWith("CORE_", StringComparison.OrdinalIgnoreCase) ? cardId[5..] : null }.OfType<string>())
        {
            if (tokens.TryGetValue(root, out var made))
            {
                ids.AddRange(made);
            }
        }

        if (catalog.Get(cardId) is { Text.Length: > 0 } english)
        {
            // A card that makes a card of its own has it as a token: Dreaded Mount's "endless Dreadsteed" is SW_087t,
            // not the Dreadsteed of another set. A named card of another set gives way to a token of its type.
            var made = ids.Select(cards.Get).OfType<Card>().Select(c => c.Type).Where(Types.Contains).ToHashSet(StringComparer.Ordinal);
            ids.AddRange(Named(english, Names.GetValue(catalog, BuildNames))
                .Where(id => catalog.Get(id) is { } named && (named.Set == english.Set || !made.Contains(named.Type))));
        }

        var rows = ids.Distinct(StringComparer.Ordinal)
            .Select(cards.Get)
            .OfType<Card>()
            .Where(c => Types.Contains(c.Type) && c.Id != cardId)
            // The same name and text twice (C'Thun's two identical Eyes, BE_013t3/t6) is one row, the lowest id.
            .GroupBy(c => (c.Name, c.Text))
            .Select(g => g.MinBy(c => c.Id, StringComparer.Ordinal)!)
            .Select(c => new MatchListRow(c.Id, c.Name, c.Cost, c.Rarity, 1))
            .OrderBy(r => r.Cost).ThenBy(r => r.Name, StringComparer.CurrentCulture)
            .ToList();
        return rows.Count is 0 or > MaxRows ? null : new MatchListView(cardId, source.Name, Loc.T("Related.Title"), rows.Count, rows);
    }

    /// <summary>Token ids by the id they hang off: <c>CS2_101t</c>, <c>CS2_101t2</c>, <c>EX1_506a</c>, <c>EDR_872A</c> under their card.</summary>
    private static Dictionary<string, List<string>> BuildTokens(CardDb cards)
    {
        var tokens = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var card in cards.All)
        {
            if (!card.Collectible && Types.Contains(card.Type) && card.Set != SkinSet && TokenId().Match(card.Id) is { Success: true } match
                && cards.Get(match.Groups["root"].Value) is { } root && root.Set == card.Set)
            {
                (tokens.TryGetValue(root.Id, out var list) ? list : tokens[root.Id] = []).Add(card.Id);
            }
        }

        return tokens;
    }

    /// <summary>
    /// The cards <paramref name="source"/>'s text names. The longest name wins its place in the text ("Dragonbane Shot"
    /// is not Dragonbane), the card's own name included; a name counts only where the text leads into it as a card
    /// (<see cref="LeadsIn"/>) and it means one card (<see cref="Resolve"/>).
    /// </summary>
    private static List<string> Named(PoolCard source, NameIndex index)
    {
        var text = Bold().Replace(source.Text, " ");
        var taken = new List<(int Start, int End)>();
        var ids = new List<string>();
        foreach (var name in index.Names)
        {
            if (!text.Contains(name, StringComparison.Ordinal))
            {
                continue;
            }

            var found = Occurrences(text, name);
            if (name != source.Name)
            {
                // Each place by itself: what the text says before the name ("a 2-Cost Sabotage") is about that place.
                ids.AddRange(found
                    .Where(o => !taken.Exists(t => o.Start < t.End && t.Start < o.End) && LeadsIn(text, o.Start, o.End, index.Keywords))
                    .Select(o => Resolve(name, source, index, Qualifier.Before(text, o.Start)))
                    .OfType<string>());
            }

            taken.AddRange(found);
        }

        return ids;
    }

    /// <summary>
    /// The one card <paramref name="name"/> means in <paramref name="source"/>'s text, or null when the data cannot tell.
    /// The candidates are the cards of that name and the cards it is the short name of ("C'Thun": "C'Thun, the
    /// Shattered", "C'Thun, Eye of the Empire"). What the text says of the card comes first: a candidate whose cost or
    /// stats differ from the "2-Cost" or "3/6" before the name is not that card, whatever its set (Stickybomb
    /// Saboteur's "2-Cost Sabotage" is its own token, not the 4-Cost <c>GVG_047</c>). Reprints (the same type, cost,
    /// stats and text) are one card: the one in the source's set, else the lowest id, whatever the catalog's order. Of
    /// several different cards only the one in the source's own set, when it is the only one there: the sets write
    /// their own cards this way (a 2026 card's "C'Thun", an Old Gods card's), but that is a reading of the data, not
    /// a link in it. Otherwise none. The source's own short name is the source itself: none.
    /// </summary>
    private static string? Resolve(string name, PoolCard source, NameIndex index, Qualifier said)
    {
        IEnumerable<PoolCard> family = index.ByName[name];
        if (index.ByShortName.TryGetValue(name, out var longer))
        {
            if (longer.Exists(c => c.Name == source.Name))
            {
                return null;
            }

            family = family.Concat(longer);
        }

        var cards = family.Where(said.Fits).GroupBy(c => (c.Name, c.Type, c.Cost, c.Attack, c.Health, c.Text)).ToList();
        if (cards.Count > 1)
        {
            cards = cards.Where(g => g.Any(c => c.Set == source.Set)).ToList();
        }

        return cards.Count == 1
            ? cards[0].OrderBy(c => c.Set == source.Set ? 0 : 1).ThenBy(c => c.Id, StringComparer.Ordinal).First().Id
            : null;
    }

    /// <summary>
    /// Collectible cards by English name, the names a text can mention (two words or six letters at least, longest
    /// first) and the cards a name is the short name of. A name that is also a keyword (Charge, Windfury, Silence: a
    /// mechanic or a referenced tag of some card) is no mention: the text uses the keyword far more often than the card.
    /// </summary>
    private static NameIndex BuildNames(CardPoolCatalog catalog)
    {
        var keywords = catalog.Cards.SelectMany(c => c.Mechanics.Concat(c.ReferencedTags))
            .Select(tag => tag.Replace('_', ' '))
            .Concat(TextKeywords)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var byName = catalog.Cards.GroupBy(c => c.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var byShortName = new Dictionary<string, List<PoolCard>>(StringComparer.Ordinal);
        foreach (var card in catalog.Cards)
        {
            // "C'Thun, the Shattered", "Neptulon the Tidehunter": the name before the epithet. " the " names a character
            // only: a spell's "Infest the Scullery" is a phrase, not another Infest.
            var comma = card.Name.IndexOf(", ", StringComparison.Ordinal);
            var article = card.Type is "MINION" or "HERO" ? card.Name.IndexOf(" the ", StringComparison.Ordinal) : -1;
            var cut = comma > 0 && (article <= 0 || comma < article) ? comma : article;
            if (cut > 0)
            {
                var shortName = card.Name[..cut];
                (byShortName.TryGetValue(shortName, out var list) ? list : byShortName[shortName] = []).Add(card);
            }
        }

        var names = byName.Keys
            .Where(n => (n.Length >= 6 || n.Contains(' ', StringComparison.Ordinal)) && !keywords.Contains(n))
            .OrderByDescending(n => n.Length).ThenBy(n => n, StringComparer.Ordinal)
            .ToList();
        return new NameIndex(names, byName, byShortName, keywords);
    }

    /// <summary>Where <paramref name="text"/> holds <paramref name="name"/> as whole words (case as written).</summary>
    private static List<(int Start, int End)> Occurrences(string text, string name)
    {
        var found = new List<(int Start, int End)>();
        for (var at = text.IndexOf(name, StringComparison.Ordinal); at >= 0; at = text.IndexOf(name, at + 1, StringComparison.Ordinal))
        {
            var end = at + name.Length;
            if ((at == 0 || !IsWord(text[at - 1])) && (end == text.Length || !IsWord(text[end])))
            {
                found.Add((at, end));
            }
        }

        return found;
    }

    /// <summary>
    /// True when the words around a name show it is a card, and the whole of its name. Before it: a lowercase word
    /// ("summon a", "your"), a count, stats or a cost ("3", "1/3", "2-Cost"), a list comma, or a keyword whose object is
    /// a card (<see cref="CardKeywords"/>); or the first word of a clause with the name as its subject ("If Feugen also
    /// died"). Not a name that opens the text or a clause, where the capital letter is grammar ("Convert all but 1",
    /// "Tradeable Convert a minion", after a keyword's number "Spell Damage +1 Duplicate your hand", after "; or"), nor
    /// one with a capitalised word on either side: a longer name the catalog does not have ("a 1/3 Risen Footman",
    /// "Gorishi Colossus").
    /// </summary>
    private static bool LeadsIn(string text, int start, int end, HashSet<string> keywords)
    {
        var after = end < text.Length && text[end] == ' ' ? WordAt(text, end + 1) : string.Empty;
        if (after.Length > 0 && char.IsUpper(after[0]) && !keywords.Contains(after.TrimEnd('.', ':', ',', ';', '!', '?')))
        {
            return false;
        }

        if (start < 2 || text[start - 1] != ' ')
        {
            return false;
        }

        var beforeAt = text.LastIndexOf(' ', start - 2) + 1;
        var before = text[beforeAt..(start - 1)];
        if (before.Length == 0)
        {
            return false;
        }

        if (before[^1] == ',' || Count().IsMatch(before))
        {
            return true;
        }

        // "Choose One - Summon two copies; or Duplicate your hand": the second clause opens with "or".
        if (!IsPlainWord(before) || (before == "or" && beforeAt >= 2 && text[beforeAt - 2] == ';'))
        {
            return false;
        }

        if (char.IsLower(before[0]) || CardKeywords.Contains(before))
        {
            return true;
        }

        // "Deathrattle: If Feugen also died": the clause's first word, then the name, then the clause goes on.
        var opensClause = beforeAt == 0 || (beforeAt >= 2 && text[beforeAt - 1] == ' ' && text[beforeAt - 2] is '.' or ':' or ';' or '!' or '?');
        return opensClause && !keywords.Contains(before) && IsPlainWord(after) && char.IsLower(after[0]);
    }

    /// <summary>The characters from <paramref name="at"/> to the next space.</summary>
    private static string WordAt(string text, int at)
    {
        var end = text.IndexOf(' ', at);
        return text[at..(end < 0 ? text.Length : end)];
    }

    private static bool IsPlainWord(string word) => word.Length > 0 && word.All(c => char.IsLetter(c) || c == '\'');

    private static bool IsWord(char c) => char.IsLetterOrDigit(c) || c is '\'' or '_';

    /// <summary>
    /// What the text says of a card right before its name: "a 2-Cost Sabotage", "a 3/6 Water Elemental". A weapon's
    /// second number is its durability, which the catalog does not have: only its attack is compared.
    /// </summary>
    private readonly record struct Qualifier(int? Cost, int? Attack, int? Health, string? Type = null)
    {
        public static Qualifier Before(string text, int start)
        {
            if (start < 2 || text[start - 1] != ' ')
            {
                return default;
            }

            var at = text.LastIndexOf(' ', start - 2) + 1;
            var match = Count().Match(text[at..(start - 1)]);
            // A replacement hero/power can share a name with a collectible minion/spell. The text names its type.
            var prefix = text[..start];
            var type = prefix.EndsWith("Hero Power with ", StringComparison.OrdinalIgnoreCase) ? "HERO_POWER"
                : prefix.EndsWith("hero with ", StringComparison.OrdinalIgnoreCase) ? "HERO" : null;
            return new Qualifier(Number(match.Groups["cost"]), Number(match.Groups["attack"]), Number(match.Groups["health"]), type);
        }

        public bool Fits(PoolCard card) =>
            (Type is null || card.Type == Type)
            && (Cost is null || card.Cost == Cost)
            && (Attack is null || (card.Type is "MINION" or "WEAPON" && card.Attack == Attack && (card.Type != "MINION" || card.Health == Health)));

        private static int? Number(Group group) =>
            group.Success && int.TryParse(group.ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    /// <summary>The name lookups of one catalog.</summary>
    /// <param name="Names">The names a text can mention, longest first.</param>
    /// <param name="ByName">Cards by their English name.</param>
    /// <param name="ByShortName">Cards by the name before their epithet ("C'Thun" for "C'Thun, the Shattered").</param>
    /// <param name="Keywords">Mechanics, referenced tags and <see cref="TextKeywords"/>, as the text writes them.</param>
    private sealed record NameIndex(
        List<string> Names,
        Dictionary<string, List<PoolCard>> ByName,
        Dictionary<string, List<PoolCard>> ByShortName,
        HashSet<string> Keywords);

    [GeneratedRegex(@"^(?<root>[A-Za-z0-9_]+?)(?:t\d{0,2}[a-z]?|[abAB])$", RegexOptions.CultureInvariant)]
    private static partial Regex TokenId();

    /// <summary>The word before a name that counts the card or gives its cost or stats: "3", "2-Cost", "1/3".</summary>
    [GeneratedRegex(@"^(?:\d+|(?<cost>\d+)-Cost|(?<attack>\d+)/(?<health>\d+))$", RegexOptions.CultureInvariant)]
    private static partial Regex Count();

    [GeneratedRegex(@"<b>.*?</b>", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex Bold();
}
