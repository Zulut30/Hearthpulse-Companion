using System.Globalization;
using Hearthpulse.Core.Arena;
using Hearthpulse.Core.Power;

namespace Hearthpulse.Core.SourceCapture;

/// <summary>Low-level, ordered, locally visible Power observations. Never reads replay post-game data.</summary>
public sealed partial class SourceCaptureRecorder : IPowerListener
{
    // STEP is left out (source v4): Power.log writes the phase as a name, and no metric reads it.
    internal static readonly HashSet<string> SafeTags = new(StringComparer.Ordinal)
    {
        "TURN", "RESOURCES", "RESOURCES_USED", "TEMP_RESOURCES", "OVERLOAD_LOCKED", "OVERLOAD_OWED",
        "DAMAGE", "HEALTH", "ATK", "ARMOR", "DURABILITY", "COST", "TECH_LEVEL", "PREMIUM", "ZONE_POSITION",
        "TAUNT", "DIVINE_SHIELD", "WINDFURY", "REBORN", "STEALTH", "FROZEN", "POISONOUS", "VENOMOUS",
    };
    private readonly Dictionary<int, Shown> _shown = new();
    private readonly HashSet<int> _concealed = new();
    private readonly HashSet<int> _shop = new();
    private readonly Stack<CaptureBlock> _blocks = new();
    private GameState? _game;
    private string? _capture;
    private string _mode = "ranked";
    private string _kind = "game";
    private int _day;
    private int _seq;
    private int _nextRef;
    private int _gaps;
    private bool _stopped;
    private bool _enabled;
    private bool _late;
    private bool _initializing;
    private bool _combat;
    private bool _catchingUp;
    private int _choice;
    private string? _choiceSource;
    private string _choiceType = "other";
    private string[] _options = [];
    private int[] _optionEntities = [];
    private bool _choiceOpen;
    // The last record of the current choice is unresolved: its pick has not been seen (it may never be).
    private bool _choicePending;
    private readonly List<string> _picked = new();

    /// <summary>Called on the parser thread. Subscribers must only enqueue immutable observations.</summary>
    public event Action<SourceCaptureFrame>? Recorded;
    /// <summary>
    /// The finished fights of a Battlegrounds game, asked once when the game ends (on the parser thread) and written as
    /// <c>bg_combat</c> records just before its <c>end</c>: the journal is sent only after that, never during the game.
    /// </summary>
    internal Func<IReadOnlyList<SourceCombat>>? Combats { get; set; }
    /// <summary>Source IDs are random and scoped to one capture.</summary>
    public string? CaptureId => _capture;
    public bool Enabled => _enabled;

    /// <summary>Historical replay of a watcher is never a new observation to upload.</summary>
    public void SetCatchingUp(bool catchingUp)
    {
        _catchingUp = catchingUp;
        if (catchingUp) _late = true;
    }

    /// <summary>The host calls under its pipeline lock; enabling mid-game records an explicit late-attach gap.</summary>
    public void SetEnabled(bool enabled)
    {
        if (_enabled == enabled) return;
        if (!enabled) Stop("shutdown");
        _enabled = enabled;
        _late = true;
    }

    public void OnGameStarted(GameState game)
    {
        Stop("rotation");
        _game = game; _capture = null; _stopped = false; _late = _catchingUp;
        _shown.Clear(); _concealed.Clear(); _shop.Clear(); _blocks.Clear();
        _nextRef = 0; _choice = 0; _options = []; _optionEntities = []; _choiceOpen = false; _choicePending = false; _picked.Clear();
        _pendingInputGaps = 0;
    }

    /// <summary>
    /// Signals a real loss: a stretch of the game not seen (late attach, rotation, shutdown), a bound reached, or an
    /// observation that could not be typed. Never arbitrary source text, never what the format leaves out on purpose.
    /// </summary>
    public void NoteGap(string reason, int count = 1)
    {
        if (_capture is null || _stopped) return;
        var safe = reason is "late_attach" or "rotation" or "source_missing" or "unsupported" or "limit" or "invalid" or "shutdown" ? reason : "unsupported";
        _gaps += Math.Max(1, count);
        Emit("gap", new { reason = safe, count = Math.Max(1, count) });
    }

    /// <summary>Finishes a partial source on watcher replacement, shutdown or a closed draft screen.</summary>
    public void Stop(string reason = "shutdown")
    {
        if (_capture is null) return;
        _stopped = false; // FINAL_GAMEOVER may have frozen observations before a final STATE/PLAYSTATE arrived.
        NoteGap(reason);
        Finish(null, null, reason is "rotation" or "shutdown" or "unsupported" ? reason : "unknown");
    }

    private bool Ready(GameState game)
    {
        // A game watched in spectator mode is somebody else's: never a source (nothing more of it once it turns out so).
        if (!_enabled || _catchingUp || _stopped || game.IsSpectated || !ReferenceEquals(_game, game) || game.LocalPlayerId == 0) return false;
        if (_capture is not null) return true;
        var mode = Mode(game);
        if (mode is null) return false;
        // The same ambiguity guard as the live replay: never invert visibility when local resolution looks wrong.
        if (game.Controlled(game.OpponentPlayerId, "HAND").Count(e => e.CardId.Length > 0 && e.CreatedTurn == 0) >= 2
            && !game.Controlled(game.LocalPlayerId, "HAND").Any(e => e.CardId.Length > 0)) return false;
        Begin("game", mode, DateOnly.FromDateTime(game.StartedAt).DayNumber - DateOnly.FromDateTime(DateTime.UnixEpoch).DayNumber, game.BuildNumber > 0 ? game.BuildNumber : null,
            Format(game, mode), mode == "arena" ? game.GameType == "GT_UNDERGROUND_ARENA" ? "underground" : "normal" : null);
        _combat = Combat(game);
        if (_late || game.Turn > 1) NoteGap("late_attach");
        if (_pendingInputGaps > 0) { NoteGap("unsupported", _pendingInputGaps); _pendingInputGaps = 0; }
        // A Battlegrounds source is the Power state, which runs ahead of the displayed combat: the format says so for
        // every BG game, so since v4 it is no gap (v2/v3 wrote it as combat_view; docs/source-capture.md, ADR 0013).
        _initializing = true;
        foreach (var player in game.Players.Values)
        {
            if (game.IsBattlegrounds && player.PlayerId != game.LocalPlayerId) continue;
            if (game.Entities.GetValueOrDefault(player.EntityId) is not { } entity) continue;
            foreach (var tag in new[] { "RESOURCES", "RESOURCES_USED", "TEMP_RESOURCES", "OVERLOAD_LOCKED", "OVERLOAD_OWED" })
                if (TryNumber(entity.GetString(tag), out var number)) Emit("tag", new { tag, value = number }, player.PlayerId == game.LocalPlayerId ? "local" : "opponent");
        }
        foreach (var e in game.Entities.Values) Sync(game, e);
        _initializing = false;
        return true;
    }

    private static string? Mode(GameState game) => game.GameType switch
    {
        "GT_RANKED" => "ranked", "GT_CASUAL" => "casual", "GT_ARENA" or "GT_UNDERGROUND_ARENA" => "arena",
        "GT_TAVERNBRAWL" => "brawl", "GT_BATTLEGROUNDS" => "bg",
        "GT_BATTLEGROUNDS_DUO" or "GT_BATTLEGROUNDS_DUOS" => "duos", _ => null,
    };

    // Public format of the game itself; only Ranked/Casual Constructed has one in the v2 contract.
    private static string? Format(GameState game, string mode) => mode is "ranked" or "casual" ? game.FormatType switch
    {
        "FT_STANDARD" => "standard", "FT_WILD" => "wild", "FT_CLASSIC" => "classic", "FT_TWIST" => "twist", _ => null,
    } : null;

    private void Begin(string kind, string mode, int day, int? build, string? format, string? arena)
    {
        _capture = Guid.NewGuid().ToString("N"); _kind = kind; _mode = mode; _day = day; _seq = 0; _gaps = 0; _stopped = false;
        Emit("start", new { captureKind = kind, mode, day, build, format, arena });
    }

    private void Emit(string kind, object data, string side = "none", int entity = 0, string? card = null, int target = 0)
    {
        if (_capture is null || _stopped) return;
        Recorded?.Invoke(new(_capture, _kind, _mode, _day, new(_seq++, kind, kind == "start" || _kind != "game" ? 0 : _game?.Turn ?? 0, side, entity, card, target, data)));
    }

    public void OnEntityCreated(GameState game, Entity entity) { if (Ready(game)) Sync(game, entity); }
    public void OnEntityRevealed(GameState game, Entity entity)
    {
        if (!Ready(game)) return;
        var option = Array.IndexOf(_optionEntities, entity.Id);
        if (_choiceOpen && entity.Zone == "SETASIDE" && !_concealed.Contains(entity.Id)
            && option >= 0 && option < _options.Length && SourceCaptureTokens.Card(entity.CardId) && _options[option] != entity.CardId)
        {
            _options[option] = entity.CardId;
            WriteChoice(false); // A visible hero reroll changes the alternatives at this point, not retroactively.
        }
        if (entity.Zone == "PLAY" || entity.Controller == game.LocalPlayerId && entity.Zone == "HAND") _concealed.Remove(entity.Id);
        Sync(game, entity);
    }
    public void OnEntityHidden(GameState game, Entity entity)
    {
        if (!Ready(game)) return;
        _concealed.Add(entity.Id); _shown.Remove(entity.Id);
        Sync(game, entity);
    }

    private void Sync(GameState game, Entity entity)
    {
        if (entity.Id == game.GameEntityId || game.PlayerByEntity(entity.Id) is not null || entity.CardType is "GAME" or "PLAYER" or "ENCHANTMENT") return;
        var local = entity.Controller == game.LocalPlayerId;
        var side = local ? "local" : "opponent";
        var zone = entity.Zone;
        var face = local && zone is "HAND" or "PLAY" or "GRAVEYARD";
        if (game.IsBattlegrounds)
        {
            // Duos controller swaps and partner shops are deliberately excluded until a displayed-board source exists.
            if (game.IsDuos && (_combat || !local)) { _shown.Remove(entity.Id); return; }
            if (!local)
            {
                if (entity.Controller != game.OpponentPlayerId || zone != "PLAY" || _shop.Contains(entity.Id) && _combat) { _shown.Remove(entity.Id); return; }
                side = _combat ? "opponent" : "shop";
                face = entity.CardType is "MINION" or "BATTLEGROUND_SPELL" or "HERO" or "HERO_POWER" or "WEAPON";
                if (side == "shop") { if (entity.CardType is not ("MINION" or "BATTLEGROUND_SPELL")) return; zone = "SHOP"; _shop.Add(entity.Id); }
            }
        }
        else if (!local) face = zone is "PLAY" or "GRAVEYARD";
        if (entity.Controller == 0) { _shown.Remove(entity.Id); return; }
        var hidden = !face || _concealed.Contains(entity.Id);
        if (entity.Zone is "DECK" or "SETASIDE" or "SECRET" || entity.Zone is not ("HAND" or "PLAY" or "GRAVEYARD"))
        {
            _shown.Remove(entity.Id);
            // No individually trackable deck refs or attributes. SETASIDE is observed only in a local choice callback.
            if (entity.Zone == "DECK")
            {
                if (_nextRef >= 999_999) { NoteGap("limit"); return; }
                // A fresh one-shot structural ref never links this hidden observation to any other zone.
                Emit("entity", new { zone = "DECK", position = 0, tags = new Dictionary<string, int>() }, side, ++_nextRef);
            }
            return;
        }
        _shown.TryGetValue(entity.Id, out var before);
        if (before is not null && (before.Hidden != hidden || hidden && (before.Zone != zone || before.Side != side))) before = null;
        if (_nextRef >= 999_999) { NoteGap("limit"); return; }
        var id = before?.Id ?? ++_nextRef;
        var card = !hidden && SourceCaptureTokens.Card(entity.CardId) ? entity.CardId : null;
        if (hidden && before is not null && before.Zone == zone && before.Side == side && before.Position == Math.Clamp(entity.ZonePosition, 0, 1000)) return;
        if (!hidden && entity.CardId.Length > 0 && card is null) NoteGap("invalid");
        var tags = new Dictionary<string, int>(StringComparer.Ordinal);
        if (!hidden)
        {
            foreach (var tag in SafeTags)
            {
                var raw = entity.GetString(tag);
                if (TryNumber(raw, out var value)) tags[tag] = value;
                else if (raw.Length > 0) NoteGap("unsupported");
            }
        }
        var shown = new Shown(id, side, zone, card, hidden, Math.Clamp(entity.ZonePosition, 0, 1000), tags);
        _shown[entity.Id] = shown;
        Emit("entity", new { zone, position = shown.Position, tags }, side, id, card);
        if (!_initializing && local && before is not null && before.Zone != zone)
        {
            if (before.Zone == "HAND" && zone == "GRAVEYARD" && !_blocks.Any(b => b.Type == "PLAY")) Action("discard", shown);
        }
    }

    private static bool TryNumber(string value, out int number) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
        && number is >= -1_000_000 and <= 1_000_000;
    private static bool Combat(GameState game) => game.GameEntity is { } e && (e.GetInt(GameTag.BaconInCombatPhase) == 1 || e.GetInt(GameTag.BoardVisualState) == 2);
    private Shown? Visible(Entity? entity) => entity is not null && _shown.TryGetValue(entity.Id, out var shown) && !shown.Hidden ? shown : null;
    private void Action(string action, Shown? shown, int target = 0) =>
        Emit("action", new { action, cost = (int?)null }, "local", shown?.Id ?? 0, shown?.Card, target);

    public void OnTagChanged(GameState game, Entity entity, string tag, string previous, string value)
    {
        if (!Ready(game)) return;
        if (entity.Id == game.GameEntityId)
        {
            if (tag == "STEP" && value == "FINAL_GAMEOVER") { _stopped = true; return; }
            if (tag is "BACON_IN_COMBAT_PHASE" or "BOARD_VISUAL_STATE" || tag == "TURN" && game.IsBattlegrounds)
            {
                var combat = Combat(game);
                if (combat != _combat)
                {
                    _combat = combat;
                    foreach (var id in _shown.Where(p => p.Value.Side is "shop" or "opponent" or "partner_public").Select(p => p.Key).ToArray()) _shown.Remove(id);
                    if (!combat) _shop.Clear();
                }
            }
            // Other game tags, PROPOSED_ATTACKER/DEFENDER among them, are outside the format: the attack is its ATTACK block.
            if (SafeTags.Contains(tag)) WriteTag(tag, value);
            return;
        }
        if (game.PlayerByEntity(entity.Id) is { } player)
        {
            if (player.PlayerId == game.LocalPlayerId && tag == "PLAYSTATE" && value is "WON" or "LOST" or "TIED" or "CONCEDED") _stopped = true;
            if (SafeTags.Contains(tag) && (!game.IsBattlegrounds || player.PlayerId == game.LocalPlayerId)) WriteTag(tag, value, player.PlayerId == game.LocalPlayerId ? "local" : "opponent");
            if (tag == "PLAYER_TECH_LEVEL" && player.PlayerId == game.LocalPlayerId) WriteTag("TECH_LEVEL", value, "local");
            return;
        }
        if (tag == "CARD_TARGET" && _blocks.FirstOrDefault(b => b.Source?.Id == entity.Id && b.PendingAction is not null) is { } pending
            && int.TryParse(value, CultureInfo.InvariantCulture, out var targetId) && Visible(game.Entities.GetValueOrDefault(targetId)) is { } target)
        {
            Action(pending.PendingAction!, target, target.Id);
            pending.PendingAction = null;
        }
        if (tag is "ZONE" or "CONTROLLER" or "ZONE_POSITION" or "CARDTYPE") { Sync(game, entity); return; }
        var shown = Visible(entity);
        if (shown is not null && SafeTags.Contains(tag))
        {
            if (TryNumber(value, out var number))
            {
                var tags = new Dictionary<string, int>(shown.Tags, StringComparer.Ordinal) { [tag] = number };
                shown = shown with { Tags = tags };
                _shown[entity.Id] = shown;
            }
            if (shown.Side == "shop")
            {
                if (!TryNumber(value, out _)) NoteGap("unsupported");
                Emit("entity", new { zone = shown.Zone, position = shown.Position, tags = shown.Tags }, shown.Side, shown.Id, shown.Card);
                return;
            }
            WriteTag(tag, value, shown.Side, shown.Id, shown.Card);
        }
    }

    private void WriteTag(string tag, string value, string side = "none", int entity = 0, string? card = null)
    {
        if (TryNumber(value, out var number) && (tag != "TURN" || number is >= 0 and <= 1000))
            Emit("tag", new { tag, value = number }, side, entity, side == "local" ? card : null);
        else NoteGap("unsupported"); // Unknown enum strings never become zero or raw text.
    }

    private static string BlockKind(string type) => type is "PLAY" or "POWER" or "TRIGGER" or "ATTACK" or "DEATHS" or "MOVE_MINION" ? type : "OTHER";

    /// <summary>
    /// A type the format names, or a Power.log block type without a kind of its own (a trade's DECK_ACTION, fatigue...),
    /// whose OTHER block is no loss (source v4). Any other type is grammar the recorder does not know: its OTHER block
    /// comes with an unsupported gap, and <c>Bench capture</c> names it.
    /// </summary>
    internal static bool KnownBlockType(string type) =>
        BlockKind(type) != "OTHER" || type is "DECK_ACTION" or "FATIGUE" or "RITUAL" or "JOUST" or "REVEAL_CARD" or "GAME_RESET";

    public void OnBlockStart(GameState game, string blockType, Entity? source)
    {
        if (!Ready(game)) return;
        var block = BlockKind(blockType);
        if (_blocks.Count >= 64) { NoteGap("limit"); return; }
        var pending = new CaptureBlock(block, source);
        _blocks.Push(pending);
        var shown = Visible(source);
        Emit("block_start", new { block }, shown?.Side ?? "none", shown?.Id ?? 0, shown?.Side == "local" ? shown.Card : null);
        if (!KnownBlockType(blockType)) NoteGap("unsupported");
        if (source?.Controller != game.LocalPlayerId) return;
        var target = Visible(game.Entities.GetValueOrDefault(source.GetInt("CARD_TARGET")));
        if (blockType == "PLAY")
        {
            var action = source.CardId switch
            {
                "TB_BaconShop_DragBuy" or "TB_BaconShop_DragBuy_Spell" => "buy", "TB_BaconShop_DragSell" => "sell",
                "TB_BaconShop_8p_Reroll_Button" => "refresh", "TB_BaconShopLockAll_Button" => "freeze",
                _ when source.CardId.StartsWith("TB_BaconShopTechUp", StringComparison.Ordinal) => "upgrade",
                _ when source.CardType == "HERO_POWER" => "hero_power", _ when source.Zone == "HAND" => "play", _ => null,
            };
            // A source the action list does not name (a location used from play) is recorded by its PLAY block alone.
            if (action is "buy" or "sell" && target is null) pending.PendingAction = action;
            else if (action is not null) Action(action, action is "buy" or "sell" ? target : shown, target?.Id ?? 0);
        }
        else if (blockType == "MOVE_MINION") Action("move", shown);
    }

    public void OnBlockEnd(GameState game)
    {
        if (!Ready(game)) return;
        if (!_blocks.TryPop(out var block)) { NoteGap("invalid"); return; }
        if (block.PendingAction is { } action)
        {
            var target = Visible(game.Entities.GetValueOrDefault(block.Source?.GetInt("CARD_TARGET") ?? 0));
            Action(action, target, target?.Id ?? 0);
            if (target is null) NoteGap("source_missing");
        }
        Emit("block_end", new { block = block.Type });
        if (game.IsBattlegrounds && _blocks.Count == 0)
        {
            var visible = _shown.Values.Where(s => s.Zone is "HAND" or "PLAY" or "SHOP").ToArray();
            // 64 fully tagged cards fit the 64 KiB line budget even with long card tokens and int32 values.
            if (visible.Length > 64) NoteGap("limit");
            Emit("snapshot", new
            {
                gold = game.LocalPlayerEntity is { } player && TryNumber(player.GetString("RESOURCES"), out var resources)
                    && TryNumber(player.GetString("RESOURCES_USED"), out var used) ? (int?)(resources - used + player.GetInt("TEMP_RESOURCES")) : null,
                tier = game.LocalPlayerEntity?.GetInt("PLAYER_TECH_LEVEL") is > 0 and var tier ? (int?)tier : null,
                cards = visible.Take(64).Select(s => new { entity = s.Id, card = s.Card, side = s.Side, zone = s.Zone, position = s.Position, tags = s.Tags }).ToArray(),
            });
        }
    }

    public void OnChoices(GameState game, string choiceType, Entity? source, IReadOnlyList<Entity> options)
    {
        if (choiceType == "MULLIGAN" && options.Any(e => e.Controller != 0 && e.Controller != game.LocalPlayerId)) { Stop("unsupported"); _stopped = true; return; }
        if (!Ready(game)) return;
        SealOpenChoice();
        _choice++; _picked.Clear(); _choiceSource = Visible(source)?.Card;
        _choiceType = choiceType switch { "MULLIGAN" => "mulligan", "GENERAL" => "general", _ => "other" };
        _choiceOpen = true;
        var valid = options.Take(64).Where(e => SourceCaptureTokens.Card(e.CardId)).ToArray();
        _options = valid.Select(e => e.CardId).ToArray();
        _optionEntities = valid.Select(e => e.Id).ToArray();
        if (_options.Length != options.Count) NoteGap("invalid");
        if (_options.Length == 0) { _choiceOpen = false; NoteGap("source_missing"); return; }
        WriteChoice(false);
    }
    public void OnChoiceMade(GameState game) => OnChoiceMade(game, -1);

    /// <summary>
    /// The chosen-entities header precedes its card lines, so it resolves nothing by itself; only a header that names no
    /// card is a completed empty selection (a mulligan's chosen entities are the cards kept: none kept, the whole hand
    /// replaced).
    /// </summary>
    public void OnChoiceMade(GameState game, int count)
    {
        if (Ready(game) && _choice > 0 && _options.Length > 0) { _choiceOpen = false; WriteChoice(count == 0); }
    }
    public void OnChosen(GameState game, Entity chosen)
    {
        if (!Ready(game)) return;
        if (_choice == 0 || _picked.Count >= 16 || !SourceCaptureTokens.Card(chosen.CardId) || !_options.Contains(chosen.CardId, StringComparer.Ordinal)) { NoteGap("invalid"); return; }
        _choiceOpen = false;
        _picked.Add(chosen.CardId); WriteChoice(true);
    }
    private void WriteChoice(bool resolved)
    {
        _choicePending = !resolved;
        Emit("choice", new { choiceId = _choice, sourceCard = _choiceSource, options = _options.ToArray(), picked = _picked.ToArray(), resolved, choiceType = _choiceType }, "local");
    }

    /// <summary>
    /// The current choice is still unresolved when the next one opens or the game ends: its pick never became visible
    /// (the game ended while it was open, the Battlegrounds turn timer chose). A real loss, so the journal ends incomplete;
    /// the pool refuses a complete one with an open choice.
    /// </summary>
    private void SealOpenChoice()
    {
        if (!_choicePending) return;
        _choicePending = false;
        NoteGap("source_missing");
    }

    public void OnGameEnded(GameState game)
    {
        if (!ReferenceEquals(game, _game) || _capture is null || _kind != "game") return;
        // Begun before a spectator marker in the middle of the game: sealed without the watched player's result or fights.
        if (game.IsSpectated) { Stop("unsupported"); return; }
        // _stopped also gates FINAL_GAMEOVER reveals; allow only the final result now.
        _stopped = false;
        var result = game.LocalPlayerEntity?.GetString("PLAYSTATE") switch { "WON" => "W", "LOST" or "CONCEDED" => "L", "TIED" => "T", _ => null };
        if (result is null) NoteGap("source_missing");
        SealOpenChoice();
        if (_blocks.Count > 0) NoteGap("invalid");
        var place = game.IsBattlegrounds && game.LocalHero?.GetInt(GameTag.PlayerLeaderboardPlace) is > 0 and <= 8 and var p ? (int?)p : null;
        if (_mode is "bg" or "duos" && Combats is { } combats)
            foreach (var combat in combats().Take(SourceCaptureCombat.MaxFights)) Emit("bg_combat", combat);
        Finish(result, place, result is null ? "abandoned" : "normal");
    }
    private void Finish(string? result, int? placement, string reason)
    {
        Emit("end", new { status = _gaps == 0 ? "complete" : "incomplete", result, placement, reason });
        _stopped = true; _capture = null;
    }

    /// <summary>Records an independently observed Arena offer/pick, even when no game exists. Draft coverage is partial.</summary>
    public void CaptureDraftChoice(ArenaDraftOffer? offer, int pick, int? picked, DateOnly day)
    {
        if (!_enabled) return;
        Stop("rotation");
        Begin("draft", "arena", day.DayNumber - DateOnly.FromDateTime(DateTime.UnixEpoch).DayNumber, null, null,
            offer?.Underground switch { true => "underground", false => "normal", null => null });
        NoteGap("source_missing"); // Live Arena offers have not been screen-verified; never claim complete draft coverage.
        var valid = offer is { CardIds.Count: > 0 and <= 64 } && offer.CardIds.All(SourceCaptureTokens.Card)
            && Enumerable.Range(0, offer.CardIds.Count).All(i => offer.PackageOf(i).Count <= 64 && offer.PackageOf(i).All(SourceCaptureTokens.Card));
        if (!valid || picked is < 0 || picked >= offer?.CardIds.Count) NoteGap("invalid");
        if (!valid) { Finish(null, null, "normal"); return; }
        Emit("draft_choice", new
        {
            pick = Math.Clamp(pick, 0, 1000),
            options = valid ? offer!.CardIds.Select((card, i) => new { card, package = offer.PackageOf(i).ToArray() }).ToArray() : [],
            picked = valid && picked >= 0 && picked < offer!.CardIds.Count ? picked : null,
            underground = offer?.Underground,
        }, "local");
        Finish(null, null, "normal");
    }

    private sealed record Shown(int Id, string Side, string Zone, string? Card, bool Hidden, int Position, Dictionary<string, int> Tags);
    private sealed class CaptureBlock(string type, Entity? source)
    {
        public string Type { get; } = type;
        public Entity? Source { get; } = source;
        public string? PendingAction { get; set; }
    }
}
