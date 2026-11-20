using System.Globalization;
using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Battlegrounds.Effects.Player;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Memory;
using Hearthpulse.Core.Power;

namespace Hearthpulse.Core.Tracking;

/// <summary>Reading entities: the shop, minions and leaderboard heroes, and the simulator's sides built from them.</summary>
public sealed partial class BattlegroundsTracker
{
    private void NoteShopCard(GameState game, Entity entity)
    {
        if (!game.IsBattlegrounds || entity.Controller != game.OpponentPlayerId || !entity.InPlay
            || (!entity.IsMinion && entity.CardType is not ("SPELL" or "BATTLEGROUND_SPELL")))
        {
            return;
        }

        // A shop card dealt in a later round than the combat start means the fight is over even if the
        // client's BOARD_VISUAL_STATE transition was never logged.
        if (_inCombat && game.BattlegroundsTurn != _combatTurn)
        {
            game.CombatOnScreen = false;
            FinishTranscript(game, complete: false, "cut");
            EndCombat(game);
        }

        if (!_inCombat)
        {
            NoteShopOffer(game, entity);
        }

        // Mid-combat summons are opponent-controlled minions too: only a dealt shop counts for races.
        if (_inCombat || !entity.IsMinion)
        {
            return;
        }

        if (Cards.Get(entity.CardId) is { IsBattlegroundsPoolMinion: true } card)
        {
            foreach (var race in card.Races)
            {
                if (race != "ALL")
                {
                    _shopRaces.Add(race);
                }
            }
        }
    }

    private static bool IsLocal(GameState game, Entity entity) =>
        entity.Id == game.LocalPlayer?.EntityId || entity.Id == game.LocalHero?.Id;

    private static IEnumerable<Entity> Minions(GameState game, int controller) =>
        game.Entities.Values
            .Where(e => e.Controller == controller && e.InPlay && e.IsMinion)
            .OrderBy(e => e.ZonePosition);

    /// <summary>Bob's current offer: shop minions plus tavern spells, in row order.</summary>
    private static IEnumerable<Entity> ShopCards(GameState game)
    {
        var opponent = game.OpponentPlayerId;
        return game.Entities.Values
            .Where(e => e.Controller == opponent && e.InPlay
                        && (e.IsMinion || e.CardType is "SPELL" or "BATTLEGROUND_SPELL"))
            .OrderBy(e => e.ZonePosition);
    }

    /// <summary>Opponents' persistent leaderboard heroes (not combat copies or hero choices).</summary>
    private static IEnumerable<Entity> LeaderboardHeroes(GameState game) =>
        LeaderboardHeroes(game.Entities.Values.Where(e => e.IsHero), game.LocalHero);

    /// <summary>The leaderboard heroes among <paramref name="heroes"/> (every hero entity, in the game's order).</summary>
    private static IEnumerable<Entity> LeaderboardHeroes(IEnumerable<Entity> heroes, Entity? local)
    {
        return heroes
            .Where(e => e.GetInt(GameTag.PlayerId) > 0 && e.GetInt(GameTag.PlayerLeaderboardPlace) > 0
                        && (e.Zone == "SETASIDE" || e == local) && e.CardId != PlaceholderHero)
            .GroupBy(e => e.GetInt(GameTag.PlayerId))
            // Combat copies share PLAYER_ID; the persistent leaderboard entity is the oldest one.
            .Select(g => g.MinBy(e => e.Id)!);
    }

    private static Entity? LeaderboardHero(GameState game, int playerId) =>
        LeaderboardHeroes(game).FirstOrDefault(h => h.GetInt(GameTag.PlayerId) == playerId);

    /// <summary>
    /// A minion left the board during start of combat inside a TRIGGER block of another minion or of a player-level card
    /// (hero power, trinket): remember it for that source (Stitched Salvager's deathrattle summons a copy of the minion it
    /// destroyed; Rapid Reanimation and Soul Fermenter resummon theirs). Inferred from the log's block structure; not yet
    /// checked against a real log.
    /// </summary>
    private void RememberRemoval(GameState game, Entity removed)
    {
        for (var i = game.OpenBlocks.Count - 1; i >= 0; i--)
        {
            var block = game.OpenBlocks[i];
            if (block.Type != "TRIGGER" || block.Source is not { } source || source.Id == removed.Id || !(source.IsMinion || IsPlayerCard(source)))
            {
                continue;
            }

            var noEnchantments = Array.Empty<Entity>().ToLookup(_ => 0);
            var copy = ToSim(removed, removed.Controller != game.LocalPlayerId, noEnchantments) with { HasUnsimulatedEffect = false };
            if (!_removedBy.TryGetValue(source.Id, out var list))
            {
                _removedBy[source.Id] = list = new List<SimMinion>();
            }

            list.Add(copy);
            return;
        }
    }

    private static bool IsPlayerCard(Entity e) => ExtraCardTypes.Contains(e.CardType) || IsTrinket(e);

    /// <summary>
    /// What the player-level cards of <paramref name="controller"/> removed during start of combat (see
    /// <see cref="RememberRemoval"/>), by the card's id.
    /// </summary>
    private IReadOnlyDictionary<string, IReadOnlyList<SimMinion>> RemovedByExtras(GameState game, int controller)
    {
        Dictionary<string, IReadOnlyList<SimMinion>>? removed = null;
        foreach (var (sourceId, minions) in _removedBy)
        {
            if (game.Entities.GetValueOrDefault(sourceId) is { CardId.Length: > 0 } source && source.Controller == controller && IsPlayerCard(source))
            {
                (removed ??= new Dictionary<string, IReadOnlyList<SimMinion>>(StringComparer.Ordinal))[source.CardId] = minions.ToArray();
            }
        }

        return removed ?? SimSide.EmptyRemovedByExtras;
    }

    /// <summary>Enchantments in play (with a card id) by the entity they are attached to: a minion, a hand card or a player.</summary>
    private static ILookup<int, Entity> Enchantments(GameState game) =>
        game.Entities.Values
            .Where(e => e.CardType == "ENCHANTMENT" && e.InPlay && e.CardId.Length > 0 && e.GetInt(GameTag.Attached) > 0)
            .OrderBy(e => e.Id)
            .ToLookup(e => e.GetInt(GameTag.Attached));

    private static SimEnchantment ToEnchantment(Entity e) => new(e.CardId)
    {
        ScriptData1 = e.GetInt(GameTag.ScriptData1),
        ScriptData2 = e.GetInt(GameTag.ScriptData2),
        Attack = e.GetInt(GameTag.Atk),
        Health = e.GetInt(GameTag.Health),
        CreatorEntityId = e.GetInt(GameTag.Creator),
    };

    /// <summary>
    /// A minion's Blood Gem total: the NUM_1/NUM_2 of its "Blood Gems" enchantment (real logs: they equal the minion's tags
    /// 4235/4236, 12/12 on a combat copy of 2026-11-15), else those tags.
    /// </summary>
    private static int BloodGemTotal(Entity minion, IEnumerable<Entity> attached, string enchantmentTag, string minionTag)
    {
        foreach (var enchantment in attached)
        {
            if (enchantment.CardId == BloodGolemSticker.GemsEnchantmentId)
            {
                return enchantment.GetInt(enchantmentTag);
            }
        }

        return minion.GetInt(minionTag);
    }

    /// <summary>A minion that died before the capture, as the death count needs it: card, golden, tribes.</summary>
    private SimMinion DeadMinion(Entity e)
    {
        var (cardId, goldenId) = CombatRelevance.Normalize(e.CardId, Cards);
        var tribes = Traits.ParseTribes(Cards.Get(e.CardId)?.Races) | TribeTag(e.GetString(GameTag.CardRace));
        return SimMinion.Create(cardId, e.Attack, e.Health, tribes, golden: goldenId || e.GetInt(GameTag.Premium) > 0) with { EntityId = e.Id };
    }

    /// <summary>A minion entered the board after combat started: deaths before it do not count for it.</summary>
    private void NoteEnteredBeforeCapture(Entity entity) => _enteredAfterDeaths.TryAdd(entity.Id, _deathsBeforeCapture.Count);

    /// <summary>Friendly minions that died during Start of Combat while <paramref name="e"/> was on the board.</summary>
    private int FriendlyDeathsSeenBy(Entity e)
    {
        var from = _enteredAfterDeaths.GetValueOrDefault(e.Id);
        var count = 0;
        for (var i = from; i < _deathsBeforeCapture.Count; i++)
        {
            count += _deathsBeforeCapture[i].Controller == e.Controller ? 1 : 0;
        }

        return count;
    }

    /// <summary>The lobby's tribes, from client memory (the only complete source); <see cref="Tribe.None"/> when unknown.</summary>
    private Tribe LobbyTribes() => MemoryRaces is { Count: > 0 } races ? Traits.ParseTribes(races) : Tribe.None;

    /// <summary>
    /// A hand card summoned "for this combat only" by a Start of Combat effect stays in the hand marked "Unplayable"
    /// (BG26_350e2, with LITERALLY_UNPLAYABLE): no hand summon can take it again this combat (G1 L349409, L350734).
    /// </summary>
    private static bool SummonedFromHandThisCombat(Entity card, ILookup<int, Entity> enchantments) =>
        enchantments[card.Id].Any(e => e.CardId == SimInputs.UnplayableEnchantment);

    /// <summary>
    /// The player's quests and lasting Tavern spells waiting in the SECRET zone (Boon of Beetles: "When you have space in
    /// combat..."): a cast Tavern spell with a Start of Combat or later effect moves from the hand to SECRET and leaves the
    /// game once done (Power.log 2026-11-15 L801212, Brood of Nozdormu); a quest sits there with its progress
    /// (L504885). The local player's only: they are on the player's screen, the opponent's secret zone is hidden.
    /// </summary>
    private static IEnumerable<Entity> Objectives(GameState game, int controller) =>
        game.Controlled(controller, "SECRET")
            .Where(e => e.CardId.Length > 0 && (e.CardType == "BATTLEGROUND_SPELL" || e.Has("QUEST")))
            .OrderBy(e => e.Id);

    /// <summary>
    /// The player's "Secret Deity" objective in their SECRET zone (<see cref="SimInputs.DeityObjective"/>), or null. The
    /// opponent's is a copy the server reveals at the combat's start (SHOW_ENTITY with its card id, Power.log 2026-11-15
    /// L746400) and removes after it (L752070).
    /// </summary>
    private static Entity? DeityObjective(GameState game, int controller) =>
        game.Controlled(controller, GameTag.Secret).FirstOrDefault(e => e.CardId == SimInputs.DeityObjective);

    /// <summary>
    /// Whether the client draws <paramref name="objective"/>'s Deity with the owner's own stats: BACON_EVOLUTION_CARD_OVERWRITE_ATK
    /// on a revealed objective tells it to show the Deity card (BACON_EVOLUTION_CARD_ID) with the player's
    /// BACON_OLD_GOD_ATTACK/HEALTH, which arrive in the same block, before the first attack (L746501-746504; 2026-11-15
    /// L387546/L387630, 722/788; L1277814, 123/107).
    /// </summary>
    private static bool DeityStatsShown(Entity? objective) =>
        objective is not null && objective.GetInt(GameTag.BaconEvolutionOverwriteAttack) > 0;

    /// <summary>A local Discover offer opened: remember it when it is a Timewarped Nellie's Ship crewing itself.</summary>
    private void NoteCrewOffer(Entity? source) =>
        _crewShip = source is { CardId.Length: > 0 } ship && CombatRelevance.Normalize(ship.CardId, Cards).CardId == SimInputs.NelliesShip ? source.Id : 0;

    /// <summary>
    /// The local player picked <paramref name="chosen"/> in the ship's Discover ("At the start of each turn, Discover a
    /// Pirate to crew the ship"): it is the crew until a later turn's pick replaces it (golden: two picks a turn). The
    /// player made the pick, so it is on their screen; the opponent's picks are never seen.
    /// </summary>
    private void NoteCrewPick(GameState game, Entity chosen)
    {
        if (_crewShip == 0 || chosen.CardId.Length == 0)
        {
            return;
        }

        var turn = game.BattlegroundsTurn;
        if (!_crew.TryGetValue(_crewShip, out var crew) || crew.Turn != turn)
        {
            _crew[_crewShip] = crew = (turn, new List<string>());
        }

        crew.Pirates.Add(chosen.CardId);
    }

    /// <summary>The crew of ship <paramref name="e"/> as fresh cards (printed stats and keywords), or none.</summary>
    private IReadOnlyList<SimMinion> CrewOf(Entity e) =>
        _crew.TryGetValue(e.Id, out var crew) ? crew.Pirates.Select(FreshCard).ToArray() : Array.Empty<SimMinion>();

    private SimMinion FreshCard(string id)
    {
        var card = Cards.Get(id);
        var (cardId, golden) = CombatRelevance.Normalize(id, Cards);
        var mechanics = card?.Mechanics ?? Array.Empty<string>();
        return SimMinion.Create(cardId, card?.Attack ?? 0, Math.Max(1, card?.Health ?? 1), Traits.ParseTribes(card?.Races),
            Traits.KeywordsFromMechanics(mechanics), golden, Traits.MechanicsFrom(mechanics)) with { TechLevel = card?.TechLevel ?? 0 };
    }

    /// <summary>
    /// One side for the simulator: board minions plus what the player can see about that player. The opponent's hand
    /// and secrets are hidden information and are never read.
    /// </summary>
    private SimSide BuildSide(GameState game, int controller, bool isOpponent, ILookup<int, Entity> enchantments)
    {
        // The opponent's board is copied when the combat starts, so our minions created after its first copy were
        // summoned during Start of Combat (15.11 g2c4: copies from 2071, our Flighty Scout 2145). The opponent's own
        // Start of Combat summons cannot be told from its copies this way and count as starting minions.
        var combatStart = isOpponent ? 0 : Minions(game, game.OpponentPlayerId).Select(e => e.Id).DefaultIfEmpty(0).Min();
        var minions = Minions(game, controller)
            .Select(e => ToSim(e, isOpponent, enchantments) with
            {
                CreatorCardId = e.GetInt(GameTag.Creator) is > 0 and var creator ? game.Entities.GetValueOrDefault(creator)?.CardId ?? string.Empty : string.Empty,
                SummonedInCombat = combatStart > 0 && e.Id > combatStart,
                FriendlyDeathsBeforeCapture = FriendlyDeathsSeenBy(e),
            })
            .ToList();
        var player = game.Players.TryGetValue(controller, out var slot) ? game.Entities.GetValueOrDefault(slot.EntityId) : null;
        var hero = game.HeroOf(player);
        var played = game.Controlled(controller, "PLAY").Where(e => e.CardId.Length > 0).OrderBy(e => e.Id).ToList();
        var heroPower = played.FirstOrDefault(e => e.CardType == "HERO_POWER");
        var extras = played.Where(e => ExtraCardTypes.Contains(e.CardType) || IsTrinket(e)).Select(e => e.CardId).ToList();
        if (!isOpponent)
        {
            extras.AddRange(Objectives(game, controller).Select(e => e.CardId));
        }

        // Lobby-wide cards: the anomaly and this season's Deity (tag name as hsdata names it; unverified in a real log).
        foreach (var tag in GlobalCardTags)
        {
            var dbf = game.GameEntity?.GetInt(tag) ?? 0;
            if (dbf > 0 && Cards.Get(dbf) is { } global)
            {
                extras.Add(global.Id);
            }
        }

        var hand = isOpponent
            ? Array.Empty<SimMinion>()
            : game.Controlled(controller, "HAND")
                .Where(e => e.IsMinion && e.CardId.Length > 0 && !SummonedFromHandThisCombat(e, enchantments))
                .OrderBy(e => e.ZonePosition)
                .Select(e => ToSim(e, isOpponent: false, enchantments) with { HasUnsimulatedEffect = false })
                .ToArray();
        // The opponent's player tags (Blood Gem and tavern spell values...) are not on the player's screen: the live side gets
        // none of them, only estimates; the values the log had wait for the end of the game. The one exception is the Deity's
        // stats while the client shows its revealed objective (DeityStatsShown).
        var playerTags = NumericTags(player);
        var tags = isOpponent ? new Dictionary<string, int>(StringComparer.Ordinal) : playerTags;
        var deity = DeityObjective(game, controller);
        if (isOpponent)
        {
            _hiddenOpponentTags[_combats.Count] = playerTags
                .Where(t => SimInputs.HiddenPlayerTags.Contains(t.Key))
                .ToDictionary(t => t.Key, t => t.Value, StringComparer.Ordinal);
            if (DeityStatsShown(deity) && playerTags.GetValueOrDefault(GameTag.BaconOldGodAttack) > 0)
            {
                // The client draws the opponent's revealed Deity with these stats: on the player's screen, so exact.
                tags[GameTag.BaconOldGodAttack] = playerTags[GameTag.BaconOldGodAttack];
                tags[GameTag.BaconOldGodHealth] = Math.Max(1, playerTags.GetValueOrDefault(GameTag.BaconOldGodHealth));
            }
        }

        var side = new SimSide(minions)
        {
            HeroCardId = hero?.CardId ?? string.Empty,
            HeroPowerCardId = heroPower?.CardId ?? string.Empty,
            Extras = extras.Distinct().ToList(),
            // Only the local player's own hand: it is on their screen. The opponent's is hidden.
            Hand = hand,
            Tags = tags,
            RemovedByExtras = RemovedByExtras(game, controller),
            TavernTier = hero?.GetInt(GameTag.PlayerTechLevel) is > 0 and var tier ? tier : player?.GetInt(GameTag.PlayerTechLevel) ?? 0,
            // Lobby PLAYER_ID: on our hero and on the opponent's combat hero copy (decides the death order).
            PlayerId = hero?.GetInt(GameTag.PlayerId) ?? 0,
            // Our own secrets are on our screen; the opponent's are hidden and never read.
            Secrets = isOpponent
                ? Array.Empty<string>()
                : game.Controlled(controller, "SECRET").Where(e => e.CardId.Length > 0).OrderBy(e => e.Id).Select(e => e.CardId).ToArray(),
            // Enchantments on the player entity: only our own (whether the client shows the opponent's is not established).
            PlayerEnchantments = isOpponent || player is null ? Array.Empty<SimEnchantment>() : enchantments[player.Id].Select(ToEnchantment).ToArray(),
            DeathsBeforeCapture = _deathsBeforeCapture.Where(d => d.Controller == controller).Select(d => d.Dead).ToArray(),
            LobbyTribes = LobbyTribes(),
            DeityAwakening = deity?.GetInt(SimInputs.QuestTotal) is > 0 and var total ? new DeityAwakening(deity.GetInt(SimInputs.QuestProgress), total) : null,
        };
        var estimates = SimEstimates.For(side, isOpponent, tags, LocalPlayerSide(game, enchantments, isOpponent));
        if (isOpponent)
        {
            // The opponent's Secrets: how many is on the player's screen, which ones is not (an estimate); the entities are kept
            // so that the ids the log showed by the end of the game reach the post-game captures.
            var secrets = OpponentSecretEntities(game, controller);
            _opponentSecrets[_combats.Count] = secrets;
            if (SimEstimates.OpponentSecrets(side, secrets.Count) is { } estimate)
            {
                estimates = [.. estimates, estimate];
            }
        }

        return side with { Estimates = estimates };
    }

    /// <summary>
    /// The opponent's Battlegrounds Secrets in their SECRET zone: hidden cards (no id) and revealed ones, without the Deity
    /// objective and the quests and lasting Tavern spells the client shows (<see cref="Objectives"/>).
    /// </summary>
    private static List<Entity> OpponentSecretEntities(GameState game, int controller) =>
        game.Controlled(controller, GameTag.Secret)
            .Where(e => e.CardId.Length == 0 || SimEstimates.IsBattlegroundsSecret(e.CardId))
            .OrderBy(e => e.Id)
            .ToList();

    /// <summary>Post-game: the ids the log showed for the opponent's Secrets of a capture (<see cref="SimSide.HiddenSecrets"/>).</summary>
    private IReadOnlyList<string> RevealedOpponentSecrets(int combatIndex) =>
        _opponentSecrets.TryGetValue(combatIndex, out var secrets)
            ? secrets.Where(e => SimEstimates.IsBattlegroundsSecret(e.CardId)).Select(e => e.CardId).ToArray()
            : Array.Empty<string>();

    /// <summary>
    /// What the opponent's estimates may lean on from our own side (our player tags and enchantments: on our screen);
    /// null for our own side.
    /// </summary>
    private static SimSide? LocalPlayerSide(GameState game, ILookup<int, Entity> enchantments, bool isOpponent)
    {
        if (!isOpponent || game.LocalPlayerEntity is not { } local)
        {
            return null;
        }

        return new SimSide([])
        {
            Tags = NumericTags(local),
            PlayerEnchantments = enchantments[local.Id].Select(ToEnchantment).ToArray(),
        };
    }

    private static Dictionary<string, int> NumericTags(Entity? entity)
    {
        var tags = new Dictionary<string, int>(StringComparer.Ordinal);
        if (entity is null)
        {
            return tags;
        }

        foreach (var (tag, value) in entity.Tags)
        {
            if (int.TryParse(value, CultureInfo.InvariantCulture, out var number))
            {
                tags[tag] = number;
            }
        }

        return tags;
    }

    private SimMinion ToSim(Entity e, bool isOpponent, ILookup<int, Entity> enchantments)
    {
        // Golden minions have their own card id (golden stats and text); effects are keyed by the normal id + Golden.
        var card = Cards.Get(e.CardId);
        var (cardId, goldenId) = CombatRelevance.Normalize(e.CardId, Cards);
        var golden = goldenId || e.GetInt(GameTag.Premium) > 0;
        var mechanics = card?.Mechanics ?? Array.Empty<string>();
        var attachedEntities = enchantments[e.Id].ToList();
        var attached = attachedEntities.Select(x => x.CardId).ToList();
        var facts = new MinionFacts(e.Id, e.CardId, CombatRelevance.EffectMarkers(e.Has));
        _minionFacts[e.Id] = facts;
        var unsimulated = IsUnsimulated(facts, cardId, golden, attached, isOpponent, Cards, Effects);

        var keywords = KeywordsOf(e);
        // A reborn copy is a fresh copy of the card. Without card data, keep what the minion has minus one-shot keywords.
        var baseKeywords = card is not null
            ? Traits.KeywordsFromMechanics(mechanics)
            : keywords & (Keyword.Taunt | Keyword.Poisonous | Keyword.Windfury | Keyword.MegaWindfury);
        return new SimMinion(
            e.Attack,
            e.Health,
            e.Has(GameTag.Taunt),
            e.Has(GameTag.DivineShield),
            e.Has(GameTag.Poisonous),
            e.Has(GameTag.Venomous),
            e.Has(GameTag.Reborn),
            Traits.AttacksPerTurn(keywords),
            e.Has(GameTag.Stealth),
            card is not null ? card.Attack : e.Attack,
            unsimulated)
        {
            CardId = cardId,
            Golden = golden,
            EntityId = e.Id,
            Tribes = Traits.ParseTribes(card?.Races) | TribeTag(e.GetString(GameTag.CardRace)),
            MaxHealth = e.GetInt(GameTag.Health),
            BaseHealth = card?.Health ?? 0,
            BaseKeywords = baseKeywords,
            Mechanics = Traits.MechanicsFrom(mechanics) | Traits.MechanicsFrom(GrantableTags.Where(e.Has)),
            TechLevel = e.GetInt(GameTag.TechLevel) is > 0 and var tier ? tier : card?.TechLevel ?? 0,
            ScriptData1 = e.GetInt(GameTag.ScriptData1),
            ScriptData2 = e.GetInt(GameTag.ScriptData2),
            ScriptData3 = e.GetInt(GameTag.ScriptData3),
            ScriptData4 = e.GetInt(GameTag.ScriptData4),
            Crew = isOpponent ? Array.Empty<SimMinion>() : CrewOf(e),
            Enchantments = attached,
            EnchantmentValues = attachedEntities.Select(ToEnchantment).ToArray(),
            Remembered = _removedBy.TryGetValue(e.Id, out var removed) ? removed.ToArray() : Array.Empty<SimMinion>(),
            // "Its Blood Gems" (Blood Golem Sticker): the visible "Blood Gems" enchantment BG20_GEMe2, else tags 4235/4236.
            BloodGemAttack = BloodGemTotal(e, attachedEntities, GameTag.ScriptData1, BloodGolemSticker.GemAttackTag),
            BloodGemHealth = BloodGemTotal(e, attachedEntities, GameTag.ScriptData2, BloodGolemSticker.GemHealthTag),
        };
    }

    /// <summary>
    /// The "not simulated" flag of a captured minion, from what the log said about it (<paramref name="facts"/>) and the
    /// card data and effects of now. A saved capture is flagged again with it, so effects added later count.
    /// </summary>
    internal static bool IsUnsimulated(MinionFacts facts, string cardId, bool golden, IReadOnlyList<string> enchantments, bool isOpponent,
                                       CardDb cards, EffectRegistry effects)
    {
        var card = cards.Get(facts.CardId);
        // Known card: the generated table (or the text fallback for a newer card). Unknown card: its effect tags.
        var relevant = card is null && CombatRelevance.Find(facts.CardId) is null
            ? facts.Markers.Count > 0
            : CombatRelevance.IsCombatRelevant(facts.CardId, card, cards.Locale);
        // A deathrattle, Avenge or Rally the card does not print came from an enchantment (Dark Gift, Tamsin...).
        var granted = card is not null ? CombatRelevance.GrantedMechanics(facts.Markers.Contains, card.Mechanics) : [];
        return CombatRelevance.IsUnsimulated(cardId, golden, relevant, granted, enchantments, isOpponent, effects);
    }

    private static Keyword KeywordsOf(Entity e) =>
        (e.Has(GameTag.Taunt) ? Keyword.Taunt : 0)
        | (e.Has(GameTag.DivineShield) ? Keyword.DivineShield : 0)
        | (e.Has(GameTag.Poisonous) ? Keyword.Poisonous : 0)
        | (e.Has(GameTag.Venomous) ? Keyword.Venomous : 0)
        | (e.Has(GameTag.Reborn) ? Keyword.Reborn : 0)
        | (e.Has(GameTag.Windfury) ? Keyword.Windfury : 0)
        | (e.Has(GameTag.MegaWindfury) ? Keyword.MegaWindfury : 0)
        | (e.Has(GameTag.Stealth) ? Keyword.Stealth : 0);

    /// <summary>CARDRACE as Power.log prints it: a name ("BEAST") or, for races the client does not name, a number.</summary>
    private static Tribe TribeTag(string value) =>
        int.TryParse(value, CultureInfo.InvariantCulture, out var id) ? Traits.ParseTribe(Races.ById.GetValueOrDefault(id)) : Traits.ParseTribe(value);

    private BgMinion ToMinion(Entity e)
    {
        var keywords = KeywordTags.Where(k => e.Has(k.Tag)).Select(k => k.Label).ToList();
        var card = Cards.Get(e.CardId);
        var cost = e.GetInt(GameTag.Cost);
        if (cost <= 0 && card?.Cost is > 0)
        {
            cost = card.Cost;
        }

        return new BgMinion(
            e.CardId,
            Cards.NameOr(e.CardId, e.Name),
            e.Attack,
            e.Health,
            e.GetInt(GameTag.Premium) > 0,
            e.GetInt(GameTag.TechLevel),
            keywords)
        {
            CardType = e.CardType is { Length: > 0 } type ? type : card?.Type ?? "MINION",
            Cost = cost,
        };
    }

    /// <summary>What the last combat simulation was given (for tests and diagnostics).</summary>
    internal CombatCapture? LastCapture { get; private set; }

    /// <summary>
    /// The options <see cref="LastCapture"/> was simulated with (damage cap, Duos, first attacker, budget), so a
    /// measurement can run the same fight again.
    /// </summary>
    internal SimOptions? LastCaptureOptions { get; private set; }

    /// <summary>
    /// Every fight of this game the simulator was given, in order, with the index of its combat in <see cref="Combats"/>
    /// (the combat is added when the fight ends). Kept for the local capture file (<see cref="CombatCaptureStore"/>).
    /// </summary>
    /// <remarks>
    /// During the game these are the fights as the live simulator got them. Once the game is over for the player, the
    /// opponent's player values the client did not show (<see cref="SimSide.HiddenTags"/>) are added and the options say
    /// <see cref="SimOptions.PostGame"/>: the post-game reveal, like the match page's (docs/adr/0005-hidden-information.md).
    /// </remarks>
    internal IReadOnlyList<CapturedFight> Captures => LocalFinished || _gameEnded ? PostGameCaptures() : _captures;

    private List<CapturedFight> PostGameCaptures() =>
        _captures.Select(c =>
            {
                var hasTags = _hiddenOpponentTags.TryGetValue(c.CombatIndex, out var hidden);
                var secrets = RevealedOpponentSecrets(c.CombatIndex);
                if (!hasTags && secrets.Count == 0)
                {
                    return c;
                }

                var theirs = c.Capture.Theirs;
                return c with
                {
                    Capture = c.Capture with
                    {
                        Theirs = theirs with { HiddenTags = hasTags ? hidden! : theirs.HiddenTags, HiddenSecrets = secrets.Count > 0 ? secrets : theirs.HiddenSecrets },
                    },
                    Options = c.Options with { PostGame = true },
                };
            })
            .ToList();
}
