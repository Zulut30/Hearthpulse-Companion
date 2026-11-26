using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Power;

namespace Hearthpulse.Core.Tracking;

/// <summary>
/// Duos team combat input (TASK-2026-11-19 package D): each team's second board for the simulator
/// (<see cref="SimSide.Teammate"/>), which enters when the first one is cleared.
/// </summary>
/// <remarks>
/// <para>Order: the boards on the log at the first regular attack fight first; the other member of each team comes
/// second.</para>
/// <para>How the log shows a team (docs/match-replay.md, "Duos"; not checked on a live Duos log, none among the owner's
/// kept logs on 2026-11-19): while a team member fights, the team's player entity has <c>HERO_ENTITY</c> on that member's
/// hero and their minions come in under the player's controller; a fallback gives our partner a player of their own. So
/// a board is known by the lobby <c>PLAYER_ID</c> of the hero its player shows when it attacks, and teams by
/// <c>BACON_DUO_TEAM_ID</c> on the leaderboard heroes.</para>
/// <para>A second board not on the log at capture is the one the player last saw in a fight (at its first attack), from
/// an earlier combat: an estimate (<see cref="SimInputs.DuosOpponentTeammate"/>, <see cref="SimInputs.DuosPartnerBoard"/>).
/// Never seen, the fight has no second board and says so with the same estimate. Our own board, second when our partner
/// fights first, is the one we left the tavern with this turn (taken when the combat begins), with our hand and values.
/// Player-level values (Blood Gems, Tavern spell bonus...), hand and secrets of a board that is not ours are treated like an
/// opponent's: estimates, none.</para>
/// </remarks>
public sealed partial class BattlegroundsTracker
{
    /// <summary>The last side seen for each lobby PLAYER_ID in a Duos fight, with the turn it was seen on.</summary>
    private readonly Dictionary<int, SeenSide> _duosSides = new();

    /// <summary>Lobby PLAYER_IDs whose board was noted in this combat.</summary>
    private readonly HashSet<int> _duosNoted = new();

    /// <summary>Our lobby PLAYER_ID: our hero's when the combat begins (before a partner's hero can stand in for it).</summary>
    private int _duosOwnId;

    private sealed record SeenSide(int Turn, SimSide Side);

    private void NoteDuosCombatStart(GameState game)
    {
        if (!game.IsDuos)
        {
            return;
        }

        _duosNoted.Clear();
        if (game.LocalHero?.GetInt(GameTag.PlayerId) is > 0 and var id)
        {
            _duosOwnId = id;
            _duosSides[id] = new SeenSide(game.BattlegroundsTurn, OwnTavernBoard(game));
        }
    }

    /// <summary>
    /// Our board as we leave the tavern, with our hand and player values: the team's second board when our partner fights
    /// first. A real Duos log (build 221908, docs/bg-combat-sim.md "Duos") keeps our tavern minions in play when the board
    /// switches to combat; they leave when our partner's board comes in under our controller and come back as they were.
    /// Taken as the combat begins, so nothing of the fight is in it; an empty board means the team has no second board.
    /// </summary>
    private SimSide OwnTavernBoard(GameState game)
    {
        var side = BuildSide(game, game.LocalPlayerId, isOpponent: false, Enchantments(game));
        return side with
        {
            // Nothing was summoned in this fight yet (BuildSide would tell Start of Combat summons by the shop's ids here).
            Minions = [.. side.Minions.Select(m => m with { SummonedInCombat = false })],
            ExtraTags = ExtraTagsOf(game, game.LocalPlayerId, side.Extras),
        };
    }

    /// <summary>
    /// An attack after the capture: a board that entered the fight (a team's second member) is remembered at its first
    /// attack, for the fights where it will be the second board again.
    /// </summary>
    private void NoteDuosBoards(GameState game)
    {
        var turn = game.BattlegroundsTurn;
        ILookup<int, Entity>? enchantments = null;
        foreach (var (controller, isOpponent) in new[] { (game.LocalPlayerId, false), (game.OpponentPlayerId, true) })
        {
            var id = LobbyIdOf(game, controller);
            if (id <= 0 || _duosNoted.Contains(id) || !Minions(game, controller).Any())
            {
                continue;
            }

            _duosNoted.Add(id);
            enchantments ??= Enchantments(game);
            var side = BuildSideKeepingHidden(game, controller, isOpponent, enchantments);
            _duosSides[id] = new SeenSide(turn, isOpponent ? side : AsTeammate(game, side, enchantments));
        }
    }

    private (SimSide Ours, SimSide Theirs) WithTeammates(GameState game, SimSide ours, SimSide theirs, ILookup<int, Entity> enchantments)
    {
        if (!game.IsDuos)
        {
            return (ours, theirs);
        }

        var turn = game.BattlegroundsTurn;
        var theirId = theirs.PlayerId > 0 ? theirs.PlayerId : _combatOpponent;
        if (theirId > 0 && theirs.Minions.Count > 0)
        {
            _duosNoted.Add(theirId);
            _duosSides[theirId] = new SeenSide(turn, theirs);
        }

        // Our partner fights first: our hand, secrets and player values are not on that board.
        if (_duosOwnId > 0 && ours.PlayerId > 0 && ours.PlayerId != _duosOwnId)
        {
            ours = AsTeammate(game, ours, enchantments);
        }

        if (ours.PlayerId > 0 && ours.Minions.Count > 0)
        {
            _duosNoted.Add(ours.PlayerId);
            _duosSides[ours.PlayerId] = new SeenSide(turn, ours.PlayerId == _duosOwnId ? AsTeammate(game, ours, enchantments) : ours);
        }

        var (teammate, known) = OurTeammate(game, ours, enchantments, turn);
        ours = ours with { Teammate = teammate };
        if (!known)
        {
            // Our partner's board shows only while it fights: until then the fight has no second board, and says so.
            ours = ours with
            {
                Estimates = [.. ours.Estimates,
                    new SimEstimate(SimInputs.DuosPartnerBoard, 0, 0, "our partner's board was not seen yet: the fight has no second board")],
            };
        }

        return (ours, OpponentTeammate(game, theirs, theirId));
    }

    /// <summary>Our team's second board, and whether it is known at all (never seen: no board, an estimate).</summary>
    private (SimSide? Board, bool Known) OurTeammate(GameState game, SimSide ours, ILookup<int, Entity> enchantments, int turn)
    {
        // The fallback model: our partner as a player of the game, their minions under their own controller.
        var slot = game.PartnerPlayerId;
        if (slot > 0 && Minions(game, slot).Any())
        {
            var side = AsTeammate(game, BuildSide(game, slot, isOpponent: false, enchantments), enchantments) with { Turn = turn };
            if (side.PlayerId > 0)
            {
                _duosSides[side.PlayerId] = new SeenSide(turn, side);
            }

            return (side, true);
        }

        var mateId = ours.PlayerId == _duosOwnId || _duosOwnId == 0 ? TeammateOf(game, ours.PlayerId) : _duosOwnId;
        if (mateId <= 0 || !_duosSides.TryGetValue(mateId, out var seen))
        {
            return (null, false);
        }

        // Our own board left the tavern empty (OwnTavernBoard): the team has no second board, and that is no estimate.
        return (seen.Side.Minions.Count == 0 ? null : LastSeenSide(seen, SimInputs.DuosPartnerBoard, turn), true);
    }

    private SimSide OpponentTeammate(GameState game, SimSide theirs, int theirId)
    {
        var mateId = TeammateOf(game, theirId);
        if (mateId > 0 && _duosSides.TryGetValue(mateId, out var seen))
        {
            return theirs with { Teammate = LastSeenSide(seen, SimInputs.DuosOpponentTeammate, theirs.Turn) };
        }

        return theirs with
        {
            Estimates = [.. theirs.Estimates,
                new SimEstimate(SimInputs.DuosOpponentTeammate, 0, 0, "the opponent's teammate's board was not seen yet: the fight has no second board")],
        };
    }

    private static SimSide LastSeenSide(SeenSide seen, string input, int turn) => seen.Side with
    {
        Teammate = null,
        Turn = turn,
        Estimates = [.. seen.Side.Estimates, new SimEstimate(input, seen.Turn, seen.Side.Minions.Count, $"the board as last seen on turn {seen.Turn}")],
    };

    /// <summary>A board of our team that is not ours: no hand, no secrets, its player values estimated as an opponent's.</summary>
    private static SimSide AsTeammate(GameState game, SimSide side, ILookup<int, Entity> enchantments)
    {
        var tags = new Dictionary<string, int>(StringComparer.Ordinal);
        var stripped = side with
        {
            Hand = Array.Empty<SimMinion>(),
            Secrets = Array.Empty<string>(),
            PlayerEnchantments = Array.Empty<SimEnchantment>(),
            Tags = tags,
            ExtraTags = SimSide.EmptyExtraTags,
        };
        return stripped with { Estimates = SimEstimates.For(stripped, isOpponent: true, tags, LocalPlayerSide(game, enchantments, isOpponent: true)) };
    }

    /// <summary><see cref="BuildSide"/> for a board seen after the capture: the capture's hidden opponent tags stay as they were.</summary>
    private SimSide BuildSideKeepingHidden(GameState game, int controller, bool isOpponent, ILookup<int, Entity> enchantments)
    {
        var had = _hiddenOpponentTags.TryGetValue(_combats.Count, out var hidden);
        var hadSecrets = _opponentSecrets.TryGetValue(_combats.Count, out var secrets);
        var side = BuildSide(game, controller, isOpponent, enchantments);
        if (had)
        {
            _hiddenOpponentTags[_combats.Count] = hidden!;
        }

        if (hadSecrets)
        {
            _opponentSecrets[_combats.Count] = secrets!;
        }

        return side;
    }

    /// <summary>The lobby PLAYER_ID of the hero a player of the game shows now, else 0.</summary>
    private static int LobbyIdOf(GameState game, int controller) =>
        game.Players.TryGetValue(controller, out var slot) && game.HeroOf(game.Entities.GetValueOrDefault(slot.EntityId)) is { } hero
            ? hero.GetInt(GameTag.PlayerId)
            : 0;

    /// <summary>The lobby PLAYER_ID of the other member of <paramref name="playerId"/>'s Duos team, else 0.</summary>
    private int TeammateOf(GameState game, int playerId)
    {
        if (playerId <= 0)
        {
            return 0;
        }

        var heroes = LeaderboardHeroes(game).ToList();
        var team = heroes.FirstOrDefault(h => h.GetInt(GameTag.PlayerId) == playerId)?.GetInt(GameTag.BaconDuoTeamId) ?? 0;
        if (team <= 0 && playerId == _duosOwnId)
        {
            // Our hero may not carry the team: our player entity does.
            team = game.TeamOf(game.LocalPlayerId);
        }

        if (team <= 0)
        {
            return 0;
        }

        return heroes.FirstOrDefault(h => h.GetInt(GameTag.PlayerId) != playerId && h.GetInt(GameTag.BaconDuoTeamId) == team)?.GetInt(GameTag.PlayerId) ?? 0;
    }
}
