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
/// and mechanic flags, the few numeric tags effects read, the inputs the simulator reads besides the boards (enchantment
/// values, deaths before the capture, the lobby's tribes) and the fight step by step (the transcript,
/// <c>SourceCaptureCombat.Transcript.cs</c>). Never the player's hand, the opponent's hand, card values or player
/// enchantments, names, BattleTags, accounts, ratings, paths or text. A fight outside these rules is left out whole,
/// never cut (a transcript outside them is left out alone): the decoder (<see cref="Validate"/>) and the pool
/// (<c>server/pool/node/validation.mjs</c>) apply the same ones.
/// </summary>
internal static partial class SourceCaptureCombat
{
    /// <summary>Fights of one game; the record's <c>fight</c> (the combat's number in the game) stays below this.</summary>
    public const int MaxFights = 64;

    public const int MaxMinions = 7;

    /// <summary>Minions a start-of-combat trigger removed before the capture, per minion.</summary>
    public const int MaxRemembered = 7;

    public const int MaxEnchantments = 32;

    /// <summary>Minions of a side that died during Start of Combat, before the capture.</summary>
    public const int MaxDeaths = 32;

    /// <summary>Player-level cards of a side (hero power, trinkets, quest rewards, anomaly, Deity), and their values.</summary>
    public const int MaxExtras = 16;

    public const int MaxFacts = 2 * MaxMinions;

    /// <summary>Fight-scoped references: board minions, the minions they remembered, numbered 1.. in Power.log order.</summary>
    public const int MaxRef = 1000;

    public const int MaxStat = 1_000_000;

    /// <summary>UTF-8 bytes of a record's data, transcript included; the line stays under the 64 KiB line limit.</summary>
    public const int MaxDataBytes = 56 * 1024;

    /// <summary>
    /// Format version of the <c>transcript</c>, the fight step by step (null when the fight has none or it is outside the
    /// rules): at most <see cref="MaxTranscriptSteps"/> steps in at most <see cref="MaxTranscriptBytes"/> of compact JSON.
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

    /// <summary>
    /// Tags of the local player's player-level cards that effects read (<c>CombatEffect.CapturedTags</c>): their script
    /// numbers, and Avenge (X) and its progress on hero powers and trinkets (SCORE_VALUE_1/2, captured from package E3 on).
    /// </summary>
    internal static readonly string[] CardTags = ["TAG_SCRIPT_DATA_NUM_1", "TAG_SCRIPT_DATA_NUM_2", "SCORE_VALUE_1", "SCORE_VALUE_2"];

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

            if (list.Count == MaxFights
                || Project(fight.CombatIndex, fight.Capture, fight.Options, fight.Facts, combats[fight.CombatIndex], fight.Transcript) is not { } combat)
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

    /// <summary>
    /// One fight as a <c>bg_combat</c> record, or null when it is outside the rules (then nothing of it is sent). Its
    /// <paramref name="transcript"/> goes with it when what of it may leave fits the rules; otherwise the fight goes without.
    /// </summary>
    internal static SourceCombat? Project(int index, BattlegroundsTracker.CombatCapture capture, SimOptions options,
                                         IReadOnlyList<BattlegroundsTracker.MinionFacts> facts, CombatRecord combat,
                                         BattlegroundsTracker.CombatTranscript? transcript = null)
    {
        var result = combat.Result switch { "Win" => "W", "Loss" => "L", "Tie" => "T", _ => null };
        if (result is null || index is < 0 or >= MaxFights || combat.Odds is { } o
            && !(double.IsFinite(o.Win) && double.IsFinite(o.Tie) && double.IsFinite(o.Loss)
                 && (o.LethalTaken is not { } t || double.IsFinite(t)) && (o.LethalDealt is not { } d || double.IsFinite(d))))
        {
            return null;
        }

        // After the game (options.PostGame) the opponent's player values are the ones the log had, not the estimates the
        // live odds used; the allowlist below decides what of them leaves, as before.
        var ours = PostGameIf(options, capture.Ours);
        var theirs = PostGameIf(options, capture.Theirs);
        var kept = transcript is null ? null : KeepTranscript(transcript, ours, theirs);
        return (kept is null ? null : Build(index, capture, options, facts, combat, result, ours, theirs, kept))
               ?? Build(index, capture, options, facts, combat, result, ours, theirs, kept: null);
    }

    private static SourceCombat? Build(int index, BattlegroundsTracker.CombatCapture capture, SimOptions options, IReadOnlyList<BattlegroundsTracker.MinionFacts> facts,
                                       CombatRecord combat, string result, SimSide ours, SimSide theirs, KeptTranscript? kept)
    {
        var refs = References(ours, theirs, capture.FirstAttackerEntityId, facts, kept);
        int Ref(int id) => id > 0 ? refs[id] : 0;
        // What made an enchantment is a reference only when the record holds that entity anyway; else unknown (0).
        int Known(int id) => id > 0 && refs.TryGetValue(id, out var known) ? known : 0;
        JsonElement? steps = null;
        if (kept is not null && (steps = WriteTranscript(kept, Ref)) is null)
        {
            return null;
        }

        var fight = new SourceCombat(
            index, combat.Turn, capture.OursFirst, Ref(capture.FirstAttackerEntityId), options.DamageCap, options.CountDamage,
            Side(ours, opponent: false, Ref, Known), Side(theirs, opponent: true, Ref, Known),
            facts.Select(f => new SourceCombatFact(Ref(f.EntityId), f.CardId, f.Markers.ToArray())).ToArray(),
            result, combat.DamageTaken, combat.DamageDealt, Odds(combat.Odds), steps);
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
    /// the record carries no id of the game. The boards, the minions they remembered, the deaths before the capture and
    /// the entities of the transcript's steps share one numbering.
    /// </summary>
    private static Dictionary<int, int> References(SimSide ours, SimSide theirs, int firstAttacker, IReadOnlyList<BattlegroundsTracker.MinionFacts> facts,
                                                   KeptTranscript? kept)
    {
        var ids = ours.Minions.Concat(theirs.Minions)
            .SelectMany(m => m.Remembered.Prepend(m))
            .Concat(ours.DeathsBeforeCapture)
            .Concat(theirs.DeathsBeforeCapture)
            .Select(m => m.EntityId)
            .Concat(facts.Select(f => f.EntityId))
            .Concat(kept?.Entities ?? [])
            .Append(firstAttacker)
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

    private static SimSide PostGameIf(SimOptions options, SimSide side) => options.PostGame ? SimInputs.PostGame(side) : side;

    private static SourceCombatSide Side(SimSide side, bool opponent, Func<int, int> reference, Func<int, int> known) => new(
        side.HeroCardId.Length > 0 ? side.HeroCardId : null,
        side.HeroPowerCardId.Length > 0 ? side.HeroPowerCardId : null,
        side.Extras.ToArray(),
        side.TavernTier,
        side.Health,
        side.Turn,
        // A tag an estimate stands in for is not the player's value: it is not sent.
        side.Tags.Where(t => PlayerTags.Contains(t.Key, StringComparer.Ordinal) && !side.Estimates.Any(e => e.Tags.Contains(t.Key, StringComparer.Ordinal)))
            .OrderBy(t => t.Key, StringComparer.Ordinal)
            .ToDictionary(t => t.Key, t => t.Value, StringComparer.Ordinal),
        // The opponent's card values are not shown by the client, so they are not captured (and never sent).
        opponent
            ? new Dictionary<string, IReadOnlyDictionary<string, int>>(StringComparer.Ordinal)
            : side.ExtraTags.OrderBy(t => t.Key, StringComparer.Ordinal).ToDictionary(
                t => t.Key,
                t => (IReadOnlyDictionary<string, int>)t.Value.Where(v => CardTags.Contains(v.Key, StringComparer.Ordinal))
                    .OrderBy(v => v.Key, StringComparer.Ordinal).ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal),
                StringComparer.Ordinal),
        // The player's own enchantments only, as the local capture file keeps them (the client may not show the opponent's).
        opponent ? [] : side.PlayerEnchantments.Select(e => Enchantment(e, known)).ToArray(),
        (int)side.LobbyTribes,
        // Both sides' minions that died at Start of Combat: the player saw them die.
        side.DeathsBeforeCapture.Select(m => Minion(m, reference, known, withRemembered: false)).ToArray(),
        side.Minions.Select(m => Minion(m, reference, known, withRemembered: true)).ToArray());

    private static SourceCombatEnchantment Enchantment(SimEnchantment e, Func<int, int> known) =>
        new(e.CardId, e.ScriptData1, e.ScriptData2, e.Attack, e.Health, known(e.CreatorEntityId));

    private static SourceCombatMinion Minion(SimMinion m, Func<int, int> reference, Func<int, int> known, bool withRemembered) => new(
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
        m.EnchantmentValues.Select(e => Enchantment(e, known)).ToArray(),
        m.FriendlyDeathsBeforeCapture,
        withRemembered ? m.Remembered.Select(r => Minion(r, reference, known, withRemembered: false)).ToArray() : [],
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
        var ours = ValidateSide(d.GetProperty("ours"), opponent: false, board);
        var theirs = ValidateSide(d.GetProperty("theirs"), opponent: true, board);
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
        Nullable(d.GetProperty("transcript"), t => ValidateTranscript(t, board, ours, theirs));
    }

    /// <summary>Checks a side and adds its board minions' references to <paramref name="board"/>; returns its player-level cards.</summary>
    private static HashSet<string> ValidateSide(JsonElement side, bool opponent, HashSet<int> board)
    {
        S.Fields(side, "hero", "heroPower", "extras", "tier", "health", "turn", "tags", "cardTags", "playerEnchantments", "lobbyTribes", "deaths",
            "minions");
        S.Card(side.GetProperty("hero"));
        var playerCards = DistinctCards(side.GetProperty("extras"), MaxExtras);
        if (S.Card(side.GetProperty("heroPower")) is { } heroPower)
        {
            playerCards.Add(heroPower);
        }
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

        // The opponent's player enchantments are not captured (whether the client shows them is not established).
        var enchantments = side.GetProperty("playerEnchantments");
        S.Require(enchantments.ValueKind == JsonValueKind.Array && enchantments.GetArrayLength() <= (opponent ? 0 : MaxEnchantments));
        foreach (var enchantment in enchantments.EnumerateArray())
        {
            ValidateEnchantment(enchantment);
        }

        S.Number(side.GetProperty("lobbyTribes"), 0, TribeMask);
        var deaths = side.GetProperty("deaths");
        S.Require(deaths.ValueKind == JsonValueKind.Array && deaths.GetArrayLength() <= MaxDeaths);
        foreach (var dead in deaths.EnumerateArray())
        {
            ValidateMinion(dead, onBoard: false);
        }

        var minions = side.GetProperty("minions");
        S.Require(minions.ValueKind == JsonValueKind.Array && minions.GetArrayLength() <= MaxMinions);
        foreach (var minion in minions.EnumerateArray())
        {
            S.Require(board.Add(ValidateMinion(minion, onBoard: true)));
        }

        return playerCards;
    }

    private static void ValidateEnchantment(JsonElement e)
    {
        S.Fields(e, "card", "script1", "script2", "attack", "health", "creatorEntity");
        S.Require(S.Card(e.GetProperty("card")) is not null);
        foreach (var value in new[] { "script1", "script2", "attack", "health" })
        {
            S.Number(e.GetProperty(value), -MaxStat, MaxStat);
        }

        S.Number(e.GetProperty("creatorEntity"), 0, MaxRef);
    }

    private static int ValidateMinion(JsonElement m, bool onBoard)
    {
        S.Fields(m, "entity", "card", "golden", "attack", "health", "maxHealth", "baseHealth", "rebornAttack", "keywords", "attacks",
            "baseKeywords", "tribes", "mechanics", "tier", "script1", "script2", "creator", "enchantments", "enchantmentValues", "friendlyDeaths",
            "remembered", "unsimulated");
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

        var values = m.GetProperty("enchantmentValues");
        S.Require(values.ValueKind == JsonValueKind.Array && values.GetArrayLength() <= MaxEnchantments);
        foreach (var value in values.EnumerateArray())
        {
            ValidateEnchantment(value);
        }

        S.Number(m.GetProperty("friendlyDeaths"), 0, 1000);
        var remembered = m.GetProperty("remembered");
        S.Require(remembered.ValueKind == JsonValueKind.Array && remembered.GetArrayLength() <= (onBoard ? MaxRemembered : 0));
        foreach (var minion in remembered.EnumerateArray())
        {
            ValidateMinion(minion, onBoard: false);
        }

        S.Boolean(m.GetProperty("unsimulated"));
        return entity;
    }

    private static HashSet<string> DistinctCards(JsonElement list, int max)
    {
        S.Require(list.ValueKind == JsonValueKind.Array && list.GetArrayLength() <= max);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var card in list.EnumerateArray())
        {
            S.Require(S.Card(card) is { } id && seen.Add(id));
        }

        return seen;
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
}
