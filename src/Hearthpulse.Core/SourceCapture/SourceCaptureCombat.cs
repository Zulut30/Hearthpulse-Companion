using System.Text;
using System.Text.Json;
using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.SourceCapture.Processing;
using Hearthpulse.Core.Tracking;
using S = Hearthpulse.Core.SourceCapture.Processing.SourceCaptureSchema;

namespace Hearthpulse.Core.SourceCapture;

/// <summary>
/// The <c>bg_combat</c> record of source v3 (docs/source-capture.md, "Battlegrounds fights"): one finished fight of a
/// Battlegrounds game as the combat simulator was given it at the first attack, with what really happened and the odds
/// the app showed. Built at the end of the game from the tracker's fights in memory, never from the local capture file.
/// An allowlist, not a copy of <see cref="SimSide"/>: card ids, fight-scoped entity references, stats, keyword, tribe
/// and mechanic flags and the few numeric tags effects read. Never the player's hand, the opponent's hand or card
/// values, names, BattleTags, accounts, ratings, paths or text. A fight outside these rules is left out whole, never cut:
/// the decoder (<see cref="Validate"/>) and the pool (<c>server/pool/node/validation.mjs</c>) apply the same ones.
/// </summary>
internal static class SourceCaptureCombat
{
    /// <summary>Fights of one game; the record's <c>fight</c> (the combat's number in the game) stays below this.</summary>
    public const int MaxFights = 64;

    public const int MaxMinions = 7;

    /// <summary>Minions a start-of-combat trigger removed before the capture, per minion.</summary>
    public const int MaxRemembered = 7;

    public const int MaxEnchantments = 32;

    /// <summary>Player-level cards of a side (hero power, trinkets, quest rewards, anomaly, Deity), and their values.</summary>
    public const int MaxExtras = 16;

    public const int MaxFacts = 2 * MaxMinions;

    /// <summary>Fight-scoped references: board minions, the minions they remembered, numbered 1.. in Power.log order.</summary>
    public const int MaxRef = 1000;

    public const int MaxStat = 1_000_000;

    /// <summary>UTF-8 bytes of a record's data, transcript included; the line stays under the 64 KiB line limit.</summary>
    public const int MaxDataBytes = 56 * 1024;

    /// <summary>
    /// Format version of the reserved <c>transcript</c>, the fight step by step. Until its allowlist is written the field
    /// is always null and anything else is refused; then it holds at most <see cref="MaxTranscriptSteps"/> steps in at
    /// most <see cref="MaxTranscriptBytes"/>.
    /// </summary>
    public const int TranscriptVersion = 1;

    public const int MaxTranscriptSteps = 512;

    public const int MaxTranscriptBytes = 16 * 1024;

    /// <summary>
    /// Numeric tags of a player entity that effects read (the local capture file keeps the same list). Other player tags
    /// may hold ratings or identifiers and are never sent. A new counter input needs this list, the decoder's and the
    /// pool's in the same change.
    /// </summary>
    internal static readonly string[] PlayerTags =
    [
        "BACON_BLOODGEMBUFFATKVALUE", "BACON_BLOODGEMBUFFHEALTHVALUE", "TAVERN_SPELL_ATTACK_INCREASE", "TAVERN_SPELL_HEALTH_INCREASE",
        "BACON_OLD_GOD_ATTACK", "BACON_OLD_GOD_HEALTH", "4914", "4915",
    ];

    /// <summary>Tags of the local player's player-level cards that effects read (<c>CombatEffect.CapturedTags</c>).</summary>
    internal static readonly string[] CardTags = ["TAG_SCRIPT_DATA_NUM_1", "TAG_SCRIPT_DATA_NUM_2"];

    /// <summary>Effect marker tags the log showed on a minion (<c>CombatRelevance.EffectMarkers</c>).</summary>
    internal static readonly string[] Markers = ["DEATHRATTLE", "TRIGGER_VISUAL", "AURA", "AVENGE", "OVERKILL", "BACON_RALLY"];

    /// <summary>Current keywords; Windfury and Mega-Windfury travel as <c>attacks</c>.</summary>
    internal const int KeywordMask = (int)(Keyword.Taunt | Keyword.DivineShield | Keyword.Poisonous | Keyword.Venomous | Keyword.Reborn | Keyword.Stealth);

    internal const int BaseKeywordMask = 255;

    internal const int TribeMask = (int)Tribe.All;

    internal const int MechanicMask = 63;

    /// <summary>
    /// The finished fights of a game, in order: each capture with the combat it became (as the local capture file keeps
    /// them). A fight whose combat never ended is not one; a fight outside the rules is left out and counted in the log.
    /// </summary>
    internal static IReadOnlyList<SourceCombat> FromGame(IReadOnlyList<BattlegroundsTracker.CapturedFight> fights, IReadOnlyList<CombatRecord> combats)
    {
        var list = new List<SourceCombat>();
        var left = 0;
        var last = -1;
        foreach (var fight in fights)
        {
            if (fight.CombatIndex <= last || fight.CombatIndex >= combats.Count)
            {
                continue;
            }

            if (list.Count == MaxFights || Project(fight.CombatIndex, fight.Capture, fight.Options, fight.Facts, combats[fight.CombatIndex]) is not { } combat)
            {
                left++;
                continue;
            }

            list.Add(combat);
            last = fight.CombatIndex;
        }

        if (left > 0)
        {
            Diag.Info($"Source capture: {left} fight(s) left out of the journal (outside the record's limits)");
        }

        return list;
    }

    /// <summary>One fight as a <c>bg_combat</c> record, or null when it is outside the rules (then nothing of it is sent).</summary>
    internal static SourceCombat? Project(int index, BattlegroundsTracker.CombatCapture capture, SimOptions options,
                                         IReadOnlyList<BattlegroundsTracker.MinionFacts> facts, CombatRecord combat)
    {
        var result = combat.Result switch { "Win" => "W", "Loss" => "L", "Tie" => "T", _ => null };
        if (result is null || index is < 0 or >= MaxFights || combat.Odds is { } o
            && !(double.IsFinite(o.Win) && double.IsFinite(o.Tie) && double.IsFinite(o.Loss)
                 && (o.LethalTaken is not { } t || double.IsFinite(t)) && (o.LethalDealt is not { } d || double.IsFinite(d))))
        {
            return null;
        }

        var refs = References(capture, facts);
        int Ref(int id) => id > 0 ? refs[id] : 0;
        var fight = new SourceCombat(
            index, combat.Turn, capture.OursFirst, Ref(capture.FirstAttackerEntityId), options.DamageCap, options.CountDamage,
            Side(capture.Ours, opponent: false, Ref), Side(capture.Theirs, opponent: true, Ref),
            facts.Select(f => new SourceCombatFact(Ref(f.EntityId), f.CardId, f.Markers.ToArray())).ToArray(),
            result, combat.DamageTaken, combat.DamageDealt, Odds(combat.Odds), Transcript: null);
        // The decoder's and the pool's rules on exactly the bytes that would be sent: a fight they would refuse (and with
        // it the whole journal) is left out here instead.
        try
        {
            using var document = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(fight, SourceCaptureRecord.Json));
            Validate(document.RootElement);
            return fight;
        }
        catch (SourceCaptureException)
        {
            return null;
        }
    }

    /// <summary>
    /// Power.log entity ids become fight-scoped references 1.. in the same order: the simulator only compares them, and
    /// the record carries no id of the game.
    /// </summary>
    private static Dictionary<int, int> References(BattlegroundsTracker.CombatCapture capture, IReadOnlyList<BattlegroundsTracker.MinionFacts> facts)
    {
        var ids = capture.Ours.Minions.Concat(capture.Theirs.Minions)
            .SelectMany(m => m.Remembered.Prepend(m))
            .Select(m => m.EntityId)
            .Concat(facts.Select(f => f.EntityId))
            .Append(capture.FirstAttackerEntityId)
            .Where(id => id > 0)
            .Distinct()
            .Order();
        var refs = new Dictionary<int, int>();
        foreach (var id in ids)
        {
            refs[id] = refs.Count + 1;
        }

        return refs;
    }

    private static SourceCombatSide Side(SimSide side, bool opponent, Func<int, int> reference) => new(
        side.HeroCardId.Length > 0 ? side.HeroCardId : null,
        side.HeroPowerCardId.Length > 0 ? side.HeroPowerCardId : null,
        side.Extras.ToArray(),
        side.TavernTier,
        side.Health,
        side.Turn,
        side.Tags.Where(t => PlayerTags.Contains(t.Key, StringComparer.Ordinal)).OrderBy(t => t.Key, StringComparer.Ordinal)
            .ToDictionary(t => t.Key, t => t.Value, StringComparer.Ordinal),
        // The opponent's card values are not shown by the client, so they are not captured (and never sent).
        opponent
            ? new Dictionary<string, IReadOnlyDictionary<string, int>>(StringComparer.Ordinal)
            : side.ExtraTags.OrderBy(t => t.Key, StringComparer.Ordinal).ToDictionary(
                t => t.Key,
                t => (IReadOnlyDictionary<string, int>)t.Value.Where(v => CardTags.Contains(v.Key, StringComparer.Ordinal))
                    .OrderBy(v => v.Key, StringComparer.Ordinal).ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal),
                StringComparer.Ordinal),
        side.Minions.Select(m => Minion(m, reference, withRemembered: true)).ToArray());

    private static SourceCombatMinion Minion(SimMinion m, Func<int, int> reference, bool withRemembered) => new(
        reference(m.EntityId),
        m.CardId.Length > 0 ? m.CardId : null,
        m.Golden,
        m.Attack,
        m.Health,
        m.MaxHealth,
        m.BaseHealth,
        m.RebornAttack,
        (int)m.Keywords & KeywordMask,
        m.AttacksPerTurn,
        (int)m.BaseKeywords,
        (int)m.Tribes,
        (int)m.Mechanics,
        m.TechLevel,
        m.ScriptData1,
        m.ScriptData2,
        m.CreatorCardId.Length > 0 ? m.CreatorCardId : null,
        m.Enchantments.ToArray(),
        withRemembered ? m.Remembered.Select(r => Minion(r, reference, withRemembered: false)).ToArray() : [],
        m.HasUnsimulatedEffect);

    private static SourceCombatOdds? Odds(CombatOdds? odds) => odds is null
        ? null
        : new(Round(odds.Win), Round(odds.Tie), Round(odds.Loss), odds.LethalTaken is { } taken ? Round(taken) : null,
            odds.LethalDealt is { } dealt ? Round(dealt) : null, odds.Unsimulated, odds.Runs);

    private static double Round(double value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);

    // ------------------------------------------------------------------------------------------------- the wire rules

    /// <summary>
    /// Checks a <c>bg_combat</c> data object field by field; throws <see cref="SourceCaptureException"/> on anything
    /// else. The pool's <c>validCombat</c> (server/pool/node/validation.mjs) is the same allowlist.
    /// </summary>
    internal static void Validate(JsonElement d)
    {
        S.Fields(d, "fight", "turn", "oursFirst", "firstAttacker", "damageCap", "countDamage", "ours", "theirs", "facts", "result",
            "damageTaken", "damageDealt", "odds", "transcript");
        S.Require(Encoding.UTF8.GetByteCount(d.GetRawText()) <= MaxDataBytes);
        S.Number(d.GetProperty("fight"), 0, MaxFights - 1);
        S.Number(d.GetProperty("turn"), 0, 1000);
        Nullable(d.GetProperty("oursFirst"), v => S.Boolean(v));
        var attacker = S.Number(d.GetProperty("firstAttacker"), 0, MaxRef);
        S.Number(d.GetProperty("damageCap"), 0, 1000);
        S.Boolean(d.GetProperty("countDamage"));
        var board = new HashSet<int>();
        ValidateSide(d.GetProperty("ours"), opponent: false, board);
        ValidateSide(d.GetProperty("theirs"), opponent: true, board);
        S.Require(attacker == 0 || board.Contains(attacker));
        var facts = d.GetProperty("facts");
        S.Require(facts.ValueKind == JsonValueKind.Array && facts.GetArrayLength() <= MaxFacts);
        var known = new HashSet<int>();
        foreach (var fact in facts.EnumerateArray())
        {
            S.Fields(fact, "entity", "card", "markers");
            var entity = S.Number(fact.GetProperty("entity"), 1, MaxRef);
            S.Require(board.Contains(entity) && known.Add(entity) && S.Card(fact.GetProperty("card")) is not null);
            var markers = fact.GetProperty("markers");
            S.Require(markers.ValueKind == JsonValueKind.Array && markers.GetArrayLength() <= Markers.Length);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var marker in markers.EnumerateArray())
            {
                S.Require(seen.Add(S.OneOf(marker, Markers)));
            }
        }

        S.OneOf(d.GetProperty("result"), "W", "T", "L");
        S.Number(d.GetProperty("damageTaken"), 0, 1000);
        S.Number(d.GetProperty("damageDealt"), 0, 1000);
        Nullable(d.GetProperty("odds"), odds =>
        {
            S.Fields(odds, "win", "tie", "loss", "lethalTaken", "lethalDealt", "unsimulated", "runs");
            Share(odds.GetProperty("win"));
            Share(odds.GetProperty("tie"));
            Share(odds.GetProperty("loss"));
            Nullable(odds.GetProperty("lethalTaken"), Share);
            Nullable(odds.GetProperty("lethalDealt"), Share);
            S.Number(odds.GetProperty("unsimulated"), 0, 1000);
            S.Number(odds.GetProperty("runs"), 0, 10_000_000);
        });
        // Reserved: null until the transcript's allowlist exists (TranscriptVersion, MaxTranscriptSteps, MaxTranscriptBytes).
        S.Require(d.GetProperty("transcript").ValueKind == JsonValueKind.Null);
    }

    private static void ValidateSide(JsonElement side, bool opponent, HashSet<int> board)
    {
        S.Fields(side, "hero", "heroPower", "extras", "tier", "health", "turn", "tags", "cardTags", "minions");
        S.Card(side.GetProperty("hero"));
        S.Card(side.GetProperty("heroPower"));
        DistinctCards(side.GetProperty("extras"), MaxExtras);
        S.Number(side.GetProperty("tier"), 0, 10);
        S.Number(side.GetProperty("health"), 0, 10_000);
        S.Number(side.GetProperty("turn"), 0, 1000);
        var tags = side.GetProperty("tags");
        S.Require(tags.ValueKind == JsonValueKind.Object);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tag in tags.EnumerateObject())
        {
            S.Require(PlayerTags.Contains(tag.Name, StringComparer.Ordinal) && seen.Add(tag.Name));
            S.Number(tag.Value, -MaxStat, MaxStat);
        }

        var cardTags = side.GetProperty("cardTags");
        S.Require(cardTags.ValueKind == JsonValueKind.Object);
        var cards = new HashSet<string>(StringComparer.Ordinal);
        foreach (var card in cardTags.EnumerateObject())
        {
            S.Require(!opponent && cards.Count < MaxExtras && cards.Add(card.Name) && SourceCaptureTokens.Card(card.Name)
                      && card.Value.ValueKind == JsonValueKind.Object);
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var tag in card.Value.EnumerateObject())
            {
                S.Require(CardTags.Contains(tag.Name, StringComparer.Ordinal) && names.Add(tag.Name));
                S.Number(tag.Value, -MaxStat, MaxStat);
            }
        }

        var minions = side.GetProperty("minions");
        S.Require(minions.ValueKind == JsonValueKind.Array && minions.GetArrayLength() <= MaxMinions);
        foreach (var minion in minions.EnumerateArray())
        {
            S.Require(board.Add(ValidateMinion(minion, onBoard: true)));
        }
    }

    private static int ValidateMinion(JsonElement m, bool onBoard)
    {
        S.Fields(m, "entity", "card", "golden", "attack", "health", "maxHealth", "baseHealth", "rebornAttack", "keywords", "attacks",
            "baseKeywords", "tribes", "mechanics", "tier", "script1", "script2", "creator", "enchantments", "remembered", "unsimulated");
        var entity = S.Number(m.GetProperty("entity"), onBoard ? 1 : 0, MaxRef);
        S.Card(m.GetProperty("card"));
        S.Boolean(m.GetProperty("golden"));
        foreach (var stat in new[] { "attack", "health", "maxHealth", "baseHealth", "rebornAttack" })
        {
            S.Number(m.GetProperty(stat), 0, MaxStat);
        }

        S.Require((S.Number(m.GetProperty("keywords"), 0, KeywordMask) & ~KeywordMask) == 0);
        S.Number(m.GetProperty("attacks"), 0, 8);
        S.Number(m.GetProperty("baseKeywords"), 0, BaseKeywordMask);
        S.Number(m.GetProperty("tribes"), 0, TribeMask);
        S.Number(m.GetProperty("mechanics"), 0, MechanicMask);
        S.Number(m.GetProperty("tier"), 0, 10);
        S.Number(m.GetProperty("script1"), -MaxStat, MaxStat);
        S.Number(m.GetProperty("script2"), -MaxStat, MaxStat);
        S.Card(m.GetProperty("creator"));
        var enchantments = m.GetProperty("enchantments");
        S.Require(enchantments.ValueKind == JsonValueKind.Array && enchantments.GetArrayLength() <= MaxEnchantments);
        foreach (var enchantment in enchantments.EnumerateArray())
        {
            S.Require(S.Card(enchantment) is not null);
        }

        var remembered = m.GetProperty("remembered");
        S.Require(remembered.ValueKind == JsonValueKind.Array && remembered.GetArrayLength() <= (onBoard ? MaxRemembered : 0));
        foreach (var minion in remembered.EnumerateArray())
        {
            ValidateMinion(minion, onBoard: false);
        }

        S.Boolean(m.GetProperty("unsimulated"));
        return entity;
    }

    private static void DistinctCards(JsonElement list, int max)
    {
        S.Require(list.ValueKind == JsonValueKind.Array && list.GetArrayLength() <= max);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var card in list.EnumerateArray())
        {
            S.Require(S.Card(card) is { } id && seen.Add(id));
        }
    }

    private static void Share(JsonElement value)
    {
        S.Require(value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var share) && double.IsFinite(share) && share is >= 0 and <= 1);
    }

    private static void Nullable(JsonElement value, Action<JsonElement> check)
    {
        if (value.ValueKind != JsonValueKind.Null)
        {
            check(value);
        }
    }

    // ------------------------------------------------------------------------------------- back to simulator input

    /// <summary>
    /// A checked record as simulator input again (the measurement reads the pool's export this way); null when it breaks
    /// a rule. The hand is empty: the record never holds one.
    /// </summary>
    internal static SourceCombatInput? Read(JsonElement data)
    {
        try
        {
            Validate(data);
        }
        catch (SourceCaptureException)
        {
            return null;
        }

        var fight = data.Deserialize<SourceCombat>(SourceCaptureRecord.Json)!;
        CombatOdds? odds = fight.Odds is { } o
            ? new CombatOdds(o.Win, o.Tie, o.Loss, o.Unsimulated) { Runs = o.Runs, LethalTaken = o.LethalTaken, LethalDealt = o.LethalDealt }
            : null;
        return new SourceCombatInput(
            fight.Fight,
            fight.Turn,
            new BattlegroundsTracker.CombatCapture(ToSim(fight.Ours), ToSim(fight.Theirs), fight.OursFirst, fight.FirstAttacker),
            fight.DamageCap,
            fight.CountDamage,
            fight.Facts.Select(f => new BattlegroundsTracker.MinionFacts(f.Entity, f.Card, f.Markers.ToArray())).ToArray(),
            fight.Result switch { "W" => "Win", "L" => "Loss", _ => "Tie" },
            fight.DamageTaken,
            fight.DamageDealt,
            odds);
    }

    private static SimSide ToSim(SourceCombatSide side) => new(side.Minions.Select(ToSim).ToArray())
    {
        HeroCardId = side.Hero ?? string.Empty,
        HeroPowerCardId = side.HeroPower ?? string.Empty,
        Extras = side.Extras.ToArray(),
        Tags = side.Tags.ToDictionary(t => t.Key, t => t.Value, StringComparer.Ordinal),
        ExtraTags = side.CardTags.ToDictionary(t => t.Key, t => (IReadOnlyDictionary<string, int>)t.Value.ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal), StringComparer.Ordinal),
        TavernTier = side.Tier,
        Health = side.Health,
        Turn = side.Turn,
    };

    private static SimMinion ToSim(SourceCombatMinion m)
    {
        var keywords = (Keyword)m.Keywords;
        bool Has(Keyword keyword) => (keywords & keyword) != 0;
        return new SimMinion(m.Attack, m.Health, Has(Keyword.Taunt), Has(Keyword.DivineShield), Has(Keyword.Poisonous), Has(Keyword.Venomous),
            Has(Keyword.Reborn), m.Attacks, Has(Keyword.Stealth), m.RebornAttack, m.Unsimulated)
        {
            CardId = m.Card ?? string.Empty,
            Golden = m.Golden,
            EntityId = m.Entity,
            Tribes = (Tribe)m.Tribes,
            MaxHealth = m.MaxHealth,
            BaseHealth = m.BaseHealth,
            BaseKeywords = (Keyword)m.BaseKeywords,
            Mechanics = (CardMechanic)m.Mechanics,
            TechLevel = m.Tier,
            ScriptData1 = m.Script1,
            ScriptData2 = m.Script2,
            CreatorCardId = m.Creator ?? string.Empty,
            Enchantments = m.Enchantments.ToArray(),
            Remembered = m.Remembered.Select(ToSim).ToArray(),
        };
    }
}

/// <summary>Data of one <c>bg_combat</c> record (camelCase on the wire); see <see cref="SourceCaptureCombat"/>.</summary>
/// <param name="Fight">The combat's number in the game (0 = the first fight), growing within a capture.</param>
/// <param name="FirstAttacker">Reference of the minion that attacked first; 0 when unknown.</param>
/// <param name="Result">W, T or L from the player's side.</param>
/// <param name="Transcript">Reserved for the fight step by step; always null in this version.</param>
internal sealed record SourceCombat(
    int Fight, int Turn, bool? OursFirst, int FirstAttacker, int DamageCap, bool CountDamage, SourceCombatSide Ours, SourceCombatSide Theirs,
    IReadOnlyList<SourceCombatFact> Facts, string Result, int DamageTaken, int DamageDealt, SourceCombatOdds? Odds, object? Transcript);

/// <summary>One side: hero, hero power, player-level cards, tier, hero health with armor, allowlisted values, the board.</summary>
internal sealed record SourceCombatSide(
    string? Hero, string? HeroPower, IReadOnlyList<string> Extras, int Tier, int Health, int Turn, IReadOnlyDictionary<string, int> Tags,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> CardTags, IReadOnlyList<SourceCombatMinion> Minions);

/// <summary>A minion as the simulator gets it; <paramref name="Keywords"/> are <see cref="Keyword"/> bits without Windfury.</summary>
internal sealed record SourceCombatMinion(
    int Entity, string? Card, bool Golden, int Attack, int Health, int MaxHealth, int BaseHealth, int RebornAttack, int Keywords, int Attacks,
    int BaseKeywords, int Tribes, int Mechanics, int Tier, int Script1, int Script2, string? Creator, IReadOnlyList<string> Enchantments,
    IReadOnlyList<SourceCombatMinion> Remembered, bool Unsimulated);

/// <summary>What the log said about a board minion: its card id as logged and its effect markers.</summary>
internal sealed record SourceCombatFact(int Entity, string Card, IReadOnlyList<string> Markers);

/// <summary>The odds the app showed for the fight when the game ended, rounded to 4 decimals.</summary>
internal sealed record SourceCombatOdds(double Win, double Tie, double Loss, double? LethalTaken, double? LethalDealt, int Unsimulated, int Runs);

/// <summary>A <c>bg_combat</c> record read back as the simulator's input, what happened and the odds shown.</summary>
internal sealed record SourceCombatInput(
    int Fight, int Turn, BattlegroundsTracker.CombatCapture Capture, int DamageCap, bool CountDamage,
    IReadOnlyList<BattlegroundsTracker.MinionFacts> Facts, string Result, int DamageTaken, int DamageDealt, CombatOdds? Odds);
