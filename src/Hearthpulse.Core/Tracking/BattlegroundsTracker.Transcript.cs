using System.Globalization;
using System.Text.Json.Serialization;
using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Power;

namespace Hearthpulse.Core.Tracking;

/// <summary>
/// The course of a fight after its boards were captured, as the log showed it (<see cref="CombatTranscript"/>): what a
/// replay of the simulator against the real fight checks it with (Bench <c>simreplay</c>, docs/bg-combat-sim.md).
/// </summary>
public sealed partial class BattlegroundsTracker
{
    private const string ProposedDefender = "PROPOSED_DEFENDER";

    // Recorded from the capture at the first regular attack until the fight ends; null when no fight is being recorded.
    private TranscriptRecorder? _transcript;

    /// <summary>Record each fight's <see cref="CombatTranscript"/> (on by default; off only to measure what it costs).</summary>
    internal bool RecordsTranscripts { get; set; } = true;

    /// <summary>What kind of step of a fight a <see cref="CombatEvent"/> records.</summary>
    internal enum CombatEventKind
    {
        /// <summary><c>Entity</c> attacks <c>Other</c>; <c>Value</c> 1 = inside another attack, 0 = a regular attack.</summary>
        Attack = 1,

        /// <summary><c>Entity</c> (a board minion) took <c>Value</c> damage (a negative value: healed).</summary>
        Damage,

        /// <summary><c>Entity</c> (a board minion) died: it went from the board to the graveyard.</summary>
        Death,

        /// <summary>
        /// <c>Entity</c> came onto a board: <c>Card</c> as logged, <c>Golden</c>, <c>Value</c> its zone position, <c>Opponent</c> whose.
        /// </summary>
        Summon,

        /// <summary><c>Tag</c> of <c>Entity</c> (a board minion or a minion in the local player's hand) became <c>Value</c>.</summary>
        Tag,

        /// <summary>
        /// A triggered effect of <c>Entity</c> (<c>Card</c>, <c>Opponent</c>) starts; for an enchantment <c>Other</c> is the
        /// minion it is attached to. Everything up to the matching <see cref="End"/> is that effect's work.
        /// </summary>
        Trigger,

        /// <summary>The innermost open <see cref="Trigger"/> ended.</summary>
        End,

        /// <summary><c>Entity</c> (<c>Card</c>) came into the local player's hand (the opponent's hand is hidden).</summary>
        Hand,
    }

    /// <summary>One step of a fight. Unused fields are 0 or null (and left out of the saved file).</summary>
    internal sealed record CombatEvent(CombatEventKind Kind, int Entity)
    {
        public int Other { get; init; }

        public int Value { get; init; }

        public string? Card { get; init; }

        public string? Tag { get; init; }

        public bool Opponent { get; init; }

        /// <summary>Summon, Hand: the minion is golden (its PREMIUM tag).</summary>
        public bool Golden { get; init; }
    }

    /// <summary>A board minion as the log showed it: card id as logged (a golden minion's own id), stats and keywords.</summary>
    internal sealed record MinionState(int Entity, string Card, int Attack, int Health, Keyword Keywords);

    /// <summary>Both boards, left to right, when <see cref="Event"/> events had been recorded.</summary>
    internal sealed record BoardSnapshot(int Event, IReadOnlyList<MinionState> Ours, IReadOnlyList<MinionState> Theirs);

    /// <summary>
    /// The visible course of one fight from its capture on: attacks, damage, deaths, summons, stat and keyword changes of
    /// board minions, triggered effects, and both boards before each regular attack (<see cref="Boards"/>[k] before attack
    /// k + 1) and, when <see cref="Complete"/>, once more at the end. Combat is on the player's screen; the opponent's hand
    /// and hidden entities are never recorded.
    /// </summary>
    internal sealed record CombatTranscript
    {
        /// <summary>The version of the transcript's content.</summary>
        public const int CurrentVersion = 1;

        public int Version { get; init; } = CurrentVersion;

        public IReadOnlyList<CombatEvent> Events { get; init; } = [];

        public IReadOnlyList<BoardSnapshot> Boards { get; init; } = [];

        /// <summary>The log reached the end of the fight; the last board is the one the fight ended with.</summary>
        public bool Complete { get; init; }

        /// <summary>
        /// What ended the recording: hero (the closing hero attack), restore (the tavern board came back), cleanup (a board
        /// minion was cleared away), end (end of combat), cut (the log stopped or skipped the end: not complete).
        /// </summary>
        public string Ending { get; init; } = string.Empty;

        /// <summary>Regular attacks recorded (attacks not inside another attack).</summary>
        [JsonIgnore]
        public int RegularAttacks => Events.Count(e => e is { Kind: CombatEventKind.Attack, Value: 0 });
    }

    private sealed class TranscriptRecorder(int captureIndex, BattlegroundsTracker.CapturedFight capture)
    {
        public int CaptureIndex { get; } = captureIndex;

        public CapturedFight Capture { get; } = capture;

        public List<CombatEvent> Events { get; } = new(256);

        public List<BoardSnapshot> Boards { get; } = new();

        /// <summary>Every minion that stood on a board of this fight.</summary>
        public HashSet<int> Board { get; } = new();

        /// <summary>The first entity id created after the capture.</summary>
        public int FirstNewId { get; init; }

        /// <summary>Block depths of the recorded triggers still open, innermost last.</summary>
        public List<int> Open { get; } = new();

        /// <summary>The event of the attack whose defender is not known yet, or -1.</summary>
        public int PendingDefender { get; set; } = -1;

        /// <summary>
        /// Blocks open around the first regular attack. Real logs leave some tavern blocks (a PLAY) without their
        /// BLOCK_END when the client starts a new task list, so the parser's stack is not empty between fights: only the
        /// blocks above this depth belong to the fight.
        /// </summary>
        public int BaseDepth { get; init; }
    }

    /// <summary>Starts recording at the capture: the boards before the first attack, then that attack.</summary>
    private void StartTranscript(GameState game, Entity attacker)
    {
        _transcript = null;
        var capture = _captures[^1];
        var recorder = new TranscriptRecorder(_captures.Count - 1, capture)
        {
            BaseDepth = game.OpenBlocks.Count,
            FirstNewId = game.Entities.Count == 0 ? 0 : game.Entities.Keys.Max() + 1,
        };
        foreach (var minion in capture.Capture.Ours.Minions.Concat(capture.Capture.Theirs.Minions))
        {
            recorder.Board.Add(minion.EntityId);
        }

        _transcript = recorder;
        RecordAttack(game, recorder, attacker);
    }

    private void RecordAttack(GameState game, TranscriptRecorder recorder, Entity attacker)
    {
        var nested = false;
        for (var i = recorder.BaseDepth; i < game.OpenBlocks.Count; i++)
        {
            nested |= game.OpenBlocks[i].Type == "ATTACK";
        }

        if (!nested)
        {
            recorder.Boards.Add(Snapshot(game, recorder, leaving: null));
        }

        recorder.Events.Add(new CombatEvent(CombatEventKind.Attack, attacker.Id) { Value = nested ? 1 : 0 });
        recorder.PendingDefender = recorder.Events.Count - 1;
    }

    /// <summary>A block starts during a recorded fight: an attack, the closing hero attack, or a triggered effect.</summary>
    private void TranscriptBlockStart(GameState game, string blockType, Entity? source)
    {
        if (_transcript is not { } recorder || source is null)
        {
            return;
        }

        if (blockType == "ATTACK")
        {
            if (source.IsHero)
            {
                FinishTranscript(game, complete: true, "hero");
            }
            else if (source.IsMinion)
            {
                RecordAttack(game, recorder, source);
            }

            return;
        }

        if (blockType is "TRIGGER" or "POWER" && VisibleSource(game, source) is var (owner, visible) && visible)
        {
            recorder.Events.Add(new CombatEvent(CombatEventKind.Trigger, source.Id)
            {
                Other = owner,
                Card = source.CardId,
                Opponent = source.Controller != game.LocalPlayerId,
            });
            recorder.Open.Add(game.OpenBlocks.Count);
        }
    }

    /// <summary>
    /// Whether a triggered effect's source is something the player sees in combat: a minion (on a board or just died), a
    /// hero power, trinket or quest reward, an enchantment on a minion, or a card of the local player's hand. Never the
    /// opponent's hand, a deck, hidden or cosmetic entities, or the game's own rules entities.
    /// </summary>
    private static (int Owner, bool Visible) VisibleSource(GameState game, Entity source)
    {
        if (source.CardId.Length == 0 || source.Zone is "DECK" or "SETASIDE" or "COSMETIC" or "")
        {
            return (0, false);
        }

        if (source.InHand)
        {
            return (0, source.Controller == game.LocalPlayerId);
        }

        if (source.IsMinion || IsTrinket(source) || source.CardType is "HERO_POWER" or "BATTLEGROUND_QUEST_REWARD")
        {
            return (0, true);
        }

        return source.CardType == "ENCHANTMENT" && game.Entities.GetValueOrDefault(source.GetInt(GameTag.Attached)) is { IsMinion: true } host
            ? (host.Id, true)
            : (0, false);
    }

    private void TranscriptBlockEnd(GameState game)
    {
        if (_transcript is { Open.Count: > 0 } recorder && recorder.Open[^1] == game.OpenBlocks.Count)
        {
            recorder.Open.RemoveAt(recorder.Open.Count - 1);
            recorder.Events.Add(new CombatEvent(CombatEventKind.End, 0));
        }
    }

    /// <summary>A tag changed during a recorded fight. Runs for every tag change of the fight: only cheap checks first.</summary>
    private void TranscriptTag(GameState game, Entity entity, string tag, string previous, string value)
    {
        if (_transcript is not { } recorder)
        {
            return;
        }

        if (tag == ProposedDefender)
        {
            if (recorder.PendingDefender >= 0 && entity.Id == game.GameEntityId && int.TryParse(value, CultureInfo.InvariantCulture, out var defender) && defender > 0)
            {
                recorder.Events[recorder.PendingDefender] = recorder.Events[recorder.PendingDefender] with { Other = defender };
                recorder.PendingDefender = -1;
            }

            return;
        }

        if (!entity.IsMinion || (entity.Controller != game.LocalPlayerId && entity.Controller != game.OpponentPlayerId))
        {
            return;
        }

        switch (tag)
        {
            case GameTag.Zone when previous == "PLAY" && value == "GRAVEYARD":
                recorder.Events.Add(new CombatEvent(CombatEventKind.Death, entity.Id));
                return;
            case GameTag.Zone when previous == "PLAY":
                // Board minions leave for another zone only when the fight is over and its boards are cleared.
                FinishTranscript(game, complete: true, "cleanup", leaving: entity);
                return;
            case GameTag.Zone when value == "PLAY" && previous == "SETASIDE" && entity.Controller == game.LocalPlayerId
                                   && entity.Id < recorder.FirstNewId && !BothBoardsStand(game, recorder):
                // The local tavern board, set aside before the fight, comes back once a board is empty: the fight is over
                // (a tie; a win or loss ended at the hero attack). Minions revealed or summoned during the fight are new
                // entities or the opponent's.
                FinishTranscript(game, complete: true, "restore", restored: entity);
                return;
            case GameTag.Zone when value == "PLAY":
                RecordSummon(game, recorder, entity);
                return;
            case GameTag.Zone when value == "HAND" && entity.Controller == game.LocalPlayerId:
                recorder.Events.Add(new CombatEvent(CombatEventKind.Hand, entity.Id) { Card = entity.CardId, Golden = entity.GetInt(GameTag.Premium) > 0 });
                return;
            case GameTag.Damage when entity.InPlay:
                if (int.TryParse(value, CultureInfo.InvariantCulture, out var now) && int.TryParse(previous.Length > 0 ? previous : "0", CultureInfo.InvariantCulture, out var before)
                    && now != before)
                {
                    recorder.Events.Add(new CombatEvent(CombatEventKind.Damage, entity.Id) { Value = now - before });
                }

                return;
            case GameTag.Atk or GameTag.Health
                when entity.InPlay || (entity.InHand && entity.Controller == game.LocalPlayerId):
            case GameTag.Taunt or GameTag.DivineShield or GameTag.Poisonous or GameTag.Venomous or GameTag.Reborn
                or GameTag.Windfury or GameTag.MegaWindfury or GameTag.Stealth
                when entity.InPlay:
                recorder.Events.Add(new CombatEvent(CombatEventKind.Tag, entity.Id)
                {
                    Tag = tag,
                    Value = int.TryParse(value, CultureInfo.InvariantCulture, out var number) ? number : 0,
                });
                return;
        }
    }

    /// <summary>An entity was created during a recorded fight: a summon onto a board, or a card into the local hand.</summary>
    private void TranscriptCreated(GameState game, Entity entity)
    {
        if (_transcript is not { } recorder || !entity.IsMinion)
        {
            return;
        }

        if (entity.InPlay && (entity.Controller == game.LocalPlayerId || entity.Controller == game.OpponentPlayerId))
        {
            RecordSummon(game, recorder, entity);
        }
        else if (entity.InHand && entity.Controller == game.LocalPlayerId)
        {
            recorder.Events.Add(new CombatEvent(CombatEventKind.Hand, entity.Id) { Card = entity.CardId, Golden = entity.GetInt(GameTag.Premium) > 0 });
        }
    }

    /// <summary>Both sides still have a minion of this fight on the board.</summary>
    private static bool BothBoardsStand(GameState game, TranscriptRecorder recorder)
    {
        bool ours = false, theirs = false;
        foreach (var id in recorder.Board)
        {
            if (game.Entities.GetValueOrDefault(id) is { IsMinion: true, InPlay: true } e)
            {
                ours |= e.Controller == game.LocalPlayerId;
                theirs |= e.Controller == game.OpponentPlayerId;
            }
        }

        return ours && theirs;
    }

    private static void RecordSummon(GameState game, TranscriptRecorder recorder, Entity entity)
    {
        recorder.Board.Add(entity.Id);
        recorder.Events.Add(new CombatEvent(CombatEventKind.Summon, entity.Id)
        {
            Card = entity.CardId,
            Value = entity.ZonePosition,
            Opponent = entity.Controller != game.LocalPlayerId,
            Golden = entity.GetInt(GameTag.Premium) > 0,
        });
    }

    /// <summary>
    /// Ends the recording and puts the transcript on its capture. A complete fight gets its last board: before the closing
    /// hero attack, at the end of combat, or (cleanup) as it stood when the first minion was cleared away.
    /// </summary>
    private void FinishTranscript(GameState game, bool complete, string ending, Entity? leaving = null, Entity? restored = null)
    {
        if (_transcript is not { } recorder)
        {
            return;
        }

        _transcript = null;
        if (complete)
        {
            recorder.Boards.Add(Snapshot(game, recorder, leaving, restored));
        }

        if (recorder.CaptureIndex < _captures.Count && ReferenceEquals(_captures[recorder.CaptureIndex], recorder.Capture))
        {
            _captures[recorder.CaptureIndex] = recorder.Capture with
            {
                Transcript = new CombatTranscript
                {
                    Events = recorder.Events.ToArray(),
                    Boards = recorder.Boards.ToArray(),
                    Complete = complete,
                    Ending = ending,
                },
            };
        }
    }

    /// <summary>
    /// Both boards now, from the minions of this fight (and <paramref name="leaving"/>, which just left the board; not
    /// <paramref name="restored"/>, a tavern minion that just came back).
    /// </summary>
    private static BoardSnapshot Snapshot(GameState game, TranscriptRecorder recorder, Entity? leaving, Entity? restored = null)
    {
        var ours = new List<(int Position, MinionState State)>(Side.MaxMinions);
        var theirs = new List<(int Position, MinionState State)>(Side.MaxMinions);
        foreach (var id in recorder.Board)
        {
            if (game.Entities.GetValueOrDefault(id) is not { IsMinion: true } e || (!e.InPlay && e != leaving) || e == restored)
            {
                continue;
            }

            var state = new MinionState(e.Id, e.CardId, e.Attack, e.Health, KeywordsOf(e));
            if (e.Controller == game.LocalPlayerId)
            {
                ours.Add((e.ZonePosition, state));
            }
            else if (e.Controller == game.OpponentPlayerId)
            {
                theirs.Add((e.ZonePosition, state));
            }
        }

        static MinionState[] Ordered(List<(int Position, MinionState State)> side) =>
            side.OrderBy(m => m.Position).ThenBy(m => m.State.Entity).Select(m => m.State).ToArray();

        return new BoardSnapshot(recorder.Events.Count, Ordered(ours), Ordered(theirs));
    }
}
