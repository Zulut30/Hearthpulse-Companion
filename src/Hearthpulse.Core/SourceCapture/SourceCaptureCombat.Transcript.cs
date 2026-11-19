using System.Buffers;
using System.Text;
using System.Text.Json;
using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Power;
using BoardSnapshot = Hearthpulse.Core.Tracking.BattlegroundsTracker.BoardSnapshot;
using CombatEvent = Hearthpulse.Core.Tracking.BattlegroundsTracker.CombatEvent;
using CombatEventKind = Hearthpulse.Core.Tracking.BattlegroundsTracker.CombatEventKind;
using CombatTranscript = Hearthpulse.Core.Tracking.BattlegroundsTracker.CombatTranscript;
using MinionState = Hearthpulse.Core.Tracking.BattlegroundsTracker.MinionState;
using S = Hearthpulse.Core.SourceCapture.Processing.SourceCaptureSchema;

namespace Hearthpulse.Core.SourceCapture;

/// <summary>
/// The <c>transcript</c> of a <c>bg_combat</c> record (docs/source-capture.md, "Battlegrounds fights"): the tracker's
/// <see cref="CombatTranscript"/> (version 1) cut down to the steps of the boards, so the pool's fights can be replayed
/// step by step after the game (Bench <c>simreplay --pool</c>). Kept: attacks, damage, deaths, summons with their card,
/// side and position, stat and keyword changes of board minions, triggered effects of a board minion, an enchantment on
/// one or a hero power, trinket or other player-level card the record names, and both boards before each regular attack
/// and at the end. Never: a card into the hand, a hand card's stats or effects, any other entity. Entity ids are the
/// record's fight-scoped references. Wire form (compact JSON):
/// <code>
/// {"version":1,"complete":true,"ending":"hero",
///  "events":[["attack",a,defender,nested],["damage",e,amount],["death",e],["summon",e,card,position,opponent,golden],
///            ["tag",e,tag,value],["trigger",e,host,card,opponent],["end"]],
///  "boards":[[event,[[e,card,attack,health,keywords],..ours],[..theirs]]]}
/// </code>
/// </summary>
internal static partial class SourceCaptureCombat
{
    /// <summary>Tags a <c>tag</c> step may change: what a replay compares (stats and keywords of a board minion).</summary>
    internal static readonly string[] TranscriptTags =
    [
        GameTag.Atk, GameTag.Health, GameTag.Taunt, GameTag.DivineShield, GameTag.Poisonous, GameTag.Venomous, GameTag.Reborn,
        GameTag.Windfury, GameTag.MegaWindfury, GameTag.Stealth,
    ];

    /// <summary>What ended a transcript (<see cref="CombatTranscript.Ending"/>).</summary>
    internal static readonly string[] TranscriptEndings = ["hero", "restore", "cleanup", "end", "cut"];

    private static readonly string[] StepNames = ["attack", "damage", "death", "summon", "tag", "trigger", "end"];

    private static string StepName(CombatEventKind kind) => kind switch
    {
        CombatEventKind.Attack => "attack",
        CombatEventKind.Damage => "damage",
        CombatEventKind.Death => "death",
        CombatEventKind.Summon => "summon",
        CombatEventKind.Tag => "tag",
        CombatEventKind.Trigger => "trigger",
        CombatEventKind.End => "end",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "not a step that leaves"),
    };

    /// <summary>A transcript cut down to what may leave, still with the log's entity ids (the references come when it is written).</summary>
    private sealed record KeptTranscript(bool Complete, string Ending, IReadOnlyList<CombatEvent> Events, IReadOnlyList<BoardSnapshot> Boards)
    {
        /// <summary>The entities its steps and boards name.</summary>
        public IEnumerable<int> Entities => Events.SelectMany(e => new[] { e.Entity, e.Other })
            .Concat(Boards.SelectMany(b => b.Ours.Concat(b.Theirs)).Select(m => m.Entity))
            .Where(id => id > 0);
    }

    /// <summary>
    /// What of <paramref name="transcript"/> may leave, or null when it is not understood (another version, an attack that
    /// is no board minion's, a board with a minion that never stood on one). A board minion is one of the capture or one a
    /// step summoned before.
    /// </summary>
    private static KeptTranscript? KeepTranscript(CombatTranscript transcript, SimSide ours, SimSide theirs)
    {
        if (transcript.Version != CombatTranscript.CurrentVersion || !TranscriptEndings.Contains(transcript.Ending, StringComparer.Ordinal))
        {
            return null;
        }

        var board = ours.Minions.Concat(theirs.Minions).Select(m => m.EntityId).Where(id => id > 0).ToHashSet();
        var oursCards = PlayerCards(ours);
        var theirsCards = PlayerCards(theirs);
        var events = new List<CombatEvent>(transcript.Events.Count);
        // Steps kept before each step of the transcript: where a board taken before step i stands among the kept ones.
        var keptBefore = new int[transcript.Events.Count + 1];
        var open = new Stack<bool>();
        for (var i = 0; i < transcript.Events.Count; i++)
        {
            keptBefore[i] = events.Count;
            var e = transcript.Events[i];
            switch (e.Kind)
            {
                case CombatEventKind.Attack:
                    if (!board.Contains(e.Entity))
                    {
                        return null;
                    }

                    events.Add(new CombatEvent(e.Kind, e.Entity) { Other = board.Contains(e.Other) ? e.Other : 0, Value = e.Value });
                    break;
                case CombatEventKind.Damage or CombatEventKind.Death when board.Contains(e.Entity):
                    events.Add(new CombatEvent(e.Kind, e.Entity) { Value = e.Kind == CombatEventKind.Damage ? e.Value : 0 });
                    break;
                case CombatEventKind.Summon when e.Entity > 0:
                    board.Add(e.Entity);
                    events.Add(new CombatEvent(e.Kind, e.Entity) { Card = Token(e.Card), Value = e.Value, Opponent = e.Opponent, Golden = e.Golden });
                    break;
                // A hand card's stats are never sent: only a minion that stands, or stood, on a board of this fight.
                case CombatEventKind.Tag when board.Contains(e.Entity) && e.Tag is { } tag && TranscriptTags.Contains(tag, StringComparer.Ordinal):
                    events.Add(new CombatEvent(e.Kind, e.Entity) { Tag = tag, Value = e.Value });
                    break;
                case CombatEventKind.Trigger:
                    // A board minion's effect, an enchantment's on one, or a player-level card's the record names; a hand
                    // card's is left out with its end (what it did to the boards stays).
                    var visible = board.Contains(e.Entity) || board.Contains(e.Other)
                                  || (e.Card is { } card && (e.Opponent ? theirsCards : oursCards).Contains(card));
                    open.Push(visible);
                    if (visible)
                    {
                        events.Add(new CombatEvent(e.Kind, e.Entity) { Other = board.Contains(e.Other) ? e.Other : 0, Card = Token(e.Card), Opponent = e.Opponent });
                    }

                    break;
                case CombatEventKind.End:
                    if (open.Count > 0 && open.Pop())
                    {
                        events.Add(new CombatEvent(CombatEventKind.End, 0));
                    }

                    break;
                // A card into the hand (CombatEventKind.Hand), anything about an entity no board holds: never.
            }
        }

        keptBefore[^1] = events.Count;
        var boards = new List<BoardSnapshot>(transcript.Boards.Count);
        foreach (var snapshot in transcript.Boards)
        {
            if (snapshot.Event < 0 || snapshot.Event > transcript.Events.Count || snapshot.Ours.Count > MaxMinions || snapshot.Theirs.Count > MaxMinions
                || !snapshot.Ours.Concat(snapshot.Theirs).All(m => board.Contains(m.Entity)))
            {
                return null;
            }

            static MinionState[] Clean(IReadOnlyList<MinionState> side) =>
                side.Select(m => new MinionState(m.Entity, Token(m.Card) ?? string.Empty, m.Attack, m.Health, m.Keywords & (Keyword)BaseKeywordMask)).ToArray();
            boards.Add(new BoardSnapshot(keptBefore[snapshot.Event], Clean(snapshot.Ours), Clean(snapshot.Theirs)));
        }

        return new KeptTranscript(transcript.Complete, transcript.Ending, events, boards);
    }

    private static HashSet<string> PlayerCards(SimSide side)
    {
        var cards = side.Extras.Where(c => c.Length > 0).ToHashSet(StringComparer.Ordinal);
        if (side.HeroPowerCardId.Length > 0)
        {
            cards.Add(side.HeroPowerCardId);
        }

        return cards;
    }

    private static string? Token(string? card) => SourceCaptureTokens.Card(card) ? card : null;

    /// <summary>
    /// The kept transcript as the record's compact JSON, entity ids as references; null when it has more than
    /// <see cref="MaxTranscriptSteps"/> steps or more than <see cref="MaxTranscriptBytes"/> bytes (then the fight goes without it).
    /// </summary>
    private static JsonElement? WriteTranscript(KeptTranscript kept, Func<int, int> reference)
    {
        if (kept.Events.Count > MaxTranscriptSteps)
        {
            return null;
        }

        var buffer = new ArrayBufferWriter<byte>(4096);
        using (var w = new Utf8JsonWriter(buffer))
        {
            void Card(string? card)
            {
                if (card is null)
                {
                    w.WriteNullValue();
                }
                else
                {
                    w.WriteStringValue(card);
                }
            }

            void Board(IReadOnlyList<MinionState> side)
            {
                w.WriteStartArray();
                foreach (var m in side)
                {
                    w.WriteStartArray();
                    w.WriteNumberValue(reference(m.Entity));
                    Card(m.Card.Length > 0 ? m.Card : null);
                    w.WriteNumberValue(m.Attack);
                    w.WriteNumberValue(m.Health);
                    w.WriteNumberValue((int)m.Keywords);
                    w.WriteEndArray();
                }

                w.WriteEndArray();
            }

            w.WriteStartObject();
            w.WriteNumber("version", TranscriptVersion);
            w.WriteBoolean("complete", kept.Complete);
            w.WriteString("ending", kept.Ending);
            w.WriteStartArray("events");
            foreach (var e in kept.Events)
            {
                w.WriteStartArray();
                w.WriteStringValue(StepName(e.Kind));
                switch (e.Kind)
                {
                    case CombatEventKind.Attack:
                        w.WriteNumberValue(reference(e.Entity));
                        w.WriteNumberValue(reference(e.Other));
                        w.WriteNumberValue(e.Value == 0 ? 0 : 1);
                        break;
                    case CombatEventKind.Damage:
                        w.WriteNumberValue(reference(e.Entity));
                        w.WriteNumberValue(e.Value);
                        break;
                    case CombatEventKind.Death:
                        w.WriteNumberValue(reference(e.Entity));
                        break;
                    case CombatEventKind.Summon:
                        w.WriteNumberValue(reference(e.Entity));
                        Card(e.Card);
                        w.WriteNumberValue(e.Value);
                        w.WriteBooleanValue(e.Opponent);
                        w.WriteBooleanValue(e.Golden);
                        break;
                    case CombatEventKind.Tag:
                        w.WriteNumberValue(reference(e.Entity));
                        w.WriteStringValue(e.Tag);
                        w.WriteNumberValue(e.Value);
                        break;
                    case CombatEventKind.Trigger:
                        w.WriteNumberValue(reference(e.Entity));
                        w.WriteNumberValue(reference(e.Other));
                        Card(e.Card);
                        w.WriteBooleanValue(e.Opponent);
                        break;
                }

                w.WriteEndArray();
            }

            w.WriteEndArray();
            w.WriteStartArray("boards");
            foreach (var snapshot in kept.Boards)
            {
                w.WriteStartArray();
                w.WriteNumberValue(snapshot.Event);
                Board(snapshot.Ours);
                Board(snapshot.Theirs);
                w.WriteEndArray();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        if (buffer.WrittenCount > MaxTranscriptBytes)
        {
            return null;
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Checks a record's transcript step by step (the pool's <c>validTranscript</c> is the same): every step names a board
    /// minion (one of <paramref name="captured"/> or one summoned by an earlier step), except a summon (the minion it puts on
    /// a board) and a trigger, whose source is a board minion, an enchantment on one, or a player-level card of its side
    /// (<paramref name="ours"/>, <paramref name="theirs"/>); each end closes a trigger; boards name board minions only.
    /// </summary>
    private static void ValidateTranscript(JsonElement t, HashSet<int> captured, HashSet<string> ours, HashSet<string> theirs)
    {
        S.Fields(t, "version", "complete", "ending", "events", "boards");
        S.Require(Encoding.UTF8.GetByteCount(t.GetRawText()) <= MaxTranscriptBytes);
        S.Number(t.GetProperty("version"), TranscriptVersion, TranscriptVersion);
        S.Boolean(t.GetProperty("complete"));
        S.OneOf(t.GetProperty("ending"), TranscriptEndings);
        var events = t.GetProperty("events");
        S.Require(events.ValueKind == JsonValueKind.Array && events.GetArrayLength() <= MaxTranscriptSteps);
        var board = new HashSet<int>(captured);
        void OnBoard(JsonElement value) => S.Require(board.Contains(S.Number(value, 1, MaxRef)));
        var open = 0;
        foreach (var step in events.EnumerateArray())
        {
            S.Require(step.ValueKind == JsonValueKind.Array && step.GetArrayLength() >= 1);
            var parts = step.EnumerateArray().ToArray();
            void Arity(int count) => S.Require(parts.Length == count);
            switch (S.OneOf(parts[0], StepNames))
            {
                case "attack":
                    Arity(4);
                    OnBoard(parts[1]);
                    S.Require(S.Number(parts[2], 0, MaxRef) is var defender && (defender == 0 || board.Contains(defender)));
                    S.Number(parts[3], 0, 1);
                    break;
                case "damage":
                    Arity(3);
                    OnBoard(parts[1]);
                    S.Number(parts[2], -MaxStat, MaxStat);
                    break;
                case "death":
                    Arity(2);
                    OnBoard(parts[1]);
                    break;
                case "summon":
                    Arity(6);
                    board.Add(S.Number(parts[1], 1, MaxRef));
                    S.Card(parts[2]);
                    S.Number(parts[3], 0, 10);
                    S.Boolean(parts[4]);
                    S.Boolean(parts[5]);
                    break;
                case "tag":
                    Arity(4);
                    OnBoard(parts[1]);
                    S.OneOf(parts[2], TranscriptTags);
                    S.Number(parts[3], 0, MaxStat);
                    break;
                case "trigger":
                    Arity(5);
                    var source = S.Number(parts[1], 1, MaxRef);
                    var host = S.Number(parts[2], 0, MaxRef);
                    var card = S.Card(parts[3]);
                    var opponent = S.Boolean(parts[4]);
                    S.Require((host == 0 || board.Contains(host))
                              && (board.Contains(source) || host != 0 || (card is not null && (opponent ? theirs : ours).Contains(card))));
                    open++;
                    break;
                default:
                    Arity(1);
                    S.Require(open-- > 0);
                    break;
            }
        }

        var boards = t.GetProperty("boards");
        S.Require(boards.ValueKind == JsonValueKind.Array && boards.GetArrayLength() <= MaxTranscriptSteps + 1);
        var last = 0;
        foreach (var snapshot in boards.EnumerateArray())
        {
            S.Require(snapshot.ValueKind == JsonValueKind.Array && snapshot.GetArrayLength() == 3);
            var at = S.Number(snapshot[0], last, events.GetArrayLength());
            last = at;
            var seen = new HashSet<int>();
            for (var side = 1; side <= 2; side++)
            {
                var minions = snapshot[side];
                S.Require(minions.ValueKind == JsonValueKind.Array && minions.GetArrayLength() <= MaxMinions);
                foreach (var m in minions.EnumerateArray())
                {
                    S.Require(m.ValueKind == JsonValueKind.Array && m.GetArrayLength() == 5);
                    var entity = S.Number(m[0], 1, MaxRef);
                    S.Require(board.Contains(entity) && seen.Add(entity));
                    S.Card(m[1]);
                    S.Number(m[2], 0, MaxStat);
                    S.Number(m[3], -MaxStat, MaxStat);
                    S.Number(m[4], 0, BaseKeywordMask);
                }
            }
        }
    }

    /// <summary>A checked transcript back as the tracker's (what <c>simreplay</c> replays a fight against).</summary>
    private static CombatTranscript ReadTranscript(JsonElement t)
    {
        static string? Card(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : value.GetString();
        var events = new List<CombatEvent>();
        foreach (var step in t.GetProperty("events").EnumerateArray())
        {
            var kind = step[0].GetString() switch
            {
                "attack" => CombatEventKind.Attack,
                "damage" => CombatEventKind.Damage,
                "death" => CombatEventKind.Death,
                "summon" => CombatEventKind.Summon,
                "tag" => CombatEventKind.Tag,
                "trigger" => CombatEventKind.Trigger,
                _ => CombatEventKind.End,
            };
            events.Add(kind switch
            {
                CombatEventKind.Attack => new CombatEvent(kind, step[1].GetInt32()) { Other = step[2].GetInt32(), Value = step[3].GetInt32() },
                CombatEventKind.Damage => new CombatEvent(kind, step[1].GetInt32()) { Value = step[2].GetInt32() },
                CombatEventKind.Death => new CombatEvent(kind, step[1].GetInt32()),
                CombatEventKind.Summon => new CombatEvent(kind, step[1].GetInt32())
                {
                    Card = Card(step[2]), Value = step[3].GetInt32(), Opponent = step[4].GetBoolean(), Golden = step[5].GetBoolean(),
                },
                CombatEventKind.Tag => new CombatEvent(kind, step[1].GetInt32()) { Tag = step[2].GetString(), Value = step[3].GetInt32() },
                CombatEventKind.Trigger => new CombatEvent(kind, step[1].GetInt32()) { Other = step[2].GetInt32(), Card = Card(step[3]), Opponent = step[4].GetBoolean() },
                _ => new CombatEvent(kind, 0),
            });
        }

        static MinionState[] Side(JsonElement side) => side.EnumerateArray()
            .Select(m => new MinionState(m[0].GetInt32(), Card(m[1]) ?? string.Empty, m[2].GetInt32(), m[3].GetInt32(), (Keyword)m[4].GetInt32()))
            .ToArray();
        return new CombatTranscript
        {
            Events = events,
            Boards = t.GetProperty("boards").EnumerateArray().Select(b => new BoardSnapshot(b[0].GetInt32(), Side(b[1]), Side(b[2]))).ToArray(),
            Complete = t.GetProperty("complete").GetBoolean(),
            Ending = t.GetProperty("ending").GetString()!,
        };
    }
}
