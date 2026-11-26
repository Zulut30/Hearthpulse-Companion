using System.Globalization;
using System.Text.RegularExpressions;
using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Data;
using Hearthpulse.Core.Hearthstone;
using Hearthpulse.Core.Localization;
using Hearthpulse.Core.Logs;
using Hearthpulse.Core.Memory;
using Hearthpulse.Core.Power;
using Hearthpulse.Core.Replay;

namespace Hearthpulse.Core.Tracking;

public sealed partial class GameSession
{
    // The "Read data from the game client" setting (AppSettings.ReadGameMemory), set from the UI thread.
    private volatile bool _gameMemoryEnabled = true;

    // Whether the collection, cosmetics and achievements watches have a client they may read (tracking thread).
    private bool _clientReadable;

    // How many times reading the client was turned off: a reading asked for before the last time is never applied.
    private int _memoryEpoch;

    /// <summary>
    /// The "Read data from the game client" setting (<see cref="AppSettings.ReadGameMemory"/>, docs/adr/0006). Off: nothing
    /// attaches to the game or reads it any more (<see cref="HearthstoneMemory.Blocked"/>), the memory reader gets no
    /// more requests and lets go of the game process, a scan still under way is never applied, and what was read for
    /// now is forgotten: the Battlegrounds rating and this session's start rating, the tavern buttons' costs, the
    /// lobby names (opponents' MMR), the Arena draft offer, and the wait for a rating after a game. Later games are
    /// recorded without a rating; the game in progress keeps the rating read at its start. What is known of the
    /// collection, cosmetics and achievements stays as saved data, no longer confirmed. The tracking from the logs is
    /// not touched. On again with the client running: it is read as at the client's start.
    /// </summary>
    public void SetGameMemoryEnabled(bool enabled)
    {
        lock (_pipelineGate)
        {
            if (_gameMemoryEnabled == enabled)
            {
                return;
            }

            _gameMemoryEnabled = enabled;
            _memory.Blocked = !enabled;
            if (!enabled)
            {
                // At once, not on the next tick.
                _memoryEpoch++;
                _memoryPoller.Release();
                // The card under the mouse is no longer read, and the one read before is not shown.
                _cardPoolHoverPossible = false;
                _cardPool.SetHovered(null);
                ForgetMemoryValues();
            }

            _dirty = true;
        }

        Diag.Info($"Reading the game client turned {(enabled ? "on" : "off")}");
    }

    /// <summary>What was read from the game for now goes: nothing read before the switch passes for current after it.</summary>
    private void ForgetMemoryValues()
    {
        _rating = null;
        _sessionStartRating = null;
        _awaitingRatingKey = null;
        _battlegrounds.LiveButtonCosts = null;
        _lobbyNames = null;
        _lobbyNamesGeneration = -1;
        ForgetDraftOffers();
    }

    /// <summary>The setting of <see cref="SetGameMemoryEnabled"/>, for tests.</summary>
    internal bool GameMemoryEnabled => _gameMemoryEnabled;

    /// <summary>Memory is read: the player's setting is on and the session may read at all (<see cref="ReadsMemory"/>, false in tests).</summary>
    private bool MemoryOn => ReadsMemory && _gameMemoryEnabled;

    /// <summary>
    /// The game can be read right now: reading the client is on and the memory reader is attached to it (the live view's
    /// "connected", the reads under the mouse). Asking never attaches.
    /// </summary>
    public bool CanReadClient => MemoryOn && _memory.IsAttached;

    /// <summary>
    /// The hero of the leaderboard tile under the mouse, read from the game for the overlay's opponent boards: both hosts
    /// ask here, a few times a second while the pointer is over the leaderboard, outside the memory reader. Null while
    /// reading the client is off or the game is not attached; asking never attaches.
    /// </summary>
    public string? HoveredLeaderboardHero() => CanReadClient ? _memory.GetHoveredLeaderboardHero() : null;

    /// <summary>The memory reader of this session (tests look at what it was asked).</summary>
    internal MemoryPoller MemoryReader => _memoryPoller;

    /// <summary>
    /// The collection, cosmetics and achievements are read only from a client that runs while reading it is on: turning
    /// reading on with the client running starts a client session for them (one read each), turning it off or closing
    /// the client ends one (what is known stays, unconfirmed). Tracking thread.
    /// </summary>
    private void FollowClientReads()
    {
        var readable = _hearthstoneRunning && _gameMemoryEnabled;
        if (readable == _clientReadable)
        {
            return;
        }

        _clientReadable = readable;
        if (readable)
        {
            _collectionWatch.ClientStarted();
            _cosmeticsWatch.ClientStarted();
            _achievementsWatch.ClientStarted();
        }
        else
        {
            _collectionWatch.ClientStopped();
            _cosmeticsWatch.ClientStopped();
            _achievementsWatch.ClientStopped();
        }
    }

    /// <summary>What the next memory scan should read (internal for the tests of when long reads are asked for).</summary>
    internal MemoryRequest BuildMemoryRequest()
    {
        var game = _parser?.Game;
        var inGame = game is { IsComplete: false };
        var battlegrounds = inGame && game!.IsBattlegrounds;
        return new MemoryRequest(
            _gameGeneration,
            inGame,
            battlegrounds,
            ReadRating: !inGame || battlegrounds || _scene == "BACON",
            // Lobby tribes are set before hero selection, which is when the hero advisor needs them.
            WantRaces: battlegrounds && !_racesRead,
            WantRanks: inGame && game!.IsConstructedLike && !_ranksRead && game.Turn >= 1,
            _awaitingRatingKey,
            WantDraftChoices: WantDraftChoices,
            WantLobbyNames: battlegrounds && WantLobbyNames,
            CollectionTicket: CollectionTicket(inGame),
            CosmeticsTicket: CosmeticsTicket(inGame),
            AchievementsTicket: AchievementsTicket(inGame),
            AchievementsCatalog: _achievementsWatch.SessionCatalog,
            Epoch: _memoryEpoch);
    }

    /// <summary>
    /// Whether a reading may be applied: reading the client is on and the reading was asked for after it was last turned
    /// off (a scan under way at the switch, or one asked for before an off and on, is dropped).
    /// </summary>
    private bool MayApply(MemoryReading reading) => MemoryOn && reading.Request.Epoch == _memoryEpoch;

    /// <summary>Applies a memory reading on the tracking thread. Values read for an earlier game are dropped.</summary>
    private void ApplyMemory(MemoryReading reading)
    {
        ApplyArenaMemory(reading);
        ApplyCollectionMemory(reading);
        ApplyCosmeticsMemory(reading);
        ApplyAchievementsMemory(reading);
        var request = reading.Request;
        if (_awaitingRatingKey is not null && request.AwaitingRatingKey == _awaitingRatingKey)
        {
            ApplyPostGameRating(reading.PostGameRating, reading.Rating);
        }

        if (request.ReadRating && reading.Rating is { } rating && rating != _rating)
        {
            _rating = rating;
            _sessionStartRating ??= rating.Solo > 0 ? rating.Solo : null;
            _dirty = true;
        }

        var game = _parser?.Game;
        if (game is null || request.Generation != _gameGeneration)
        {
            return;
        }

        ApplyLobbyMemory(reading);
        if (request.WantRaces && !_racesRead)
        {
            var raceIds = reading.Races;
            if (raceIds is { Count: >= 4 })
            {
                _racesRead = true;
                _battlegrounds.MemoryRaces = raceIds
                    .Select(id => Races.ById.GetValueOrDefault(id, $"RACE_{id}"))
                    .Order(StringComparer.Ordinal)
                    .ToList();
                _dirty = true;
            }
        }

        // The buttons are the authority for shop costs: they carry hero-power and trinket discounts.
        if (request.InGame && request.Battlegrounds && reading.ButtonCosts != _battlegrounds.LiveButtonCosts)
        {
            _battlegrounds.LiveButtonCosts = reading.ButtonCosts;
            _dirty = true;
        }

        if (request.WantRanks && !_ranksRead)
        {
            if (reading.Players is { } p)
            {
                _ranksRead = true;
                _constructed.LocalRank = PickRank(p.Local, game.FormatType);
                _constructed.OpponentRank = PickRank(p.Opponent, game.FormatType);
                _dirty = true;
            }
        }
    }

    private void ApplyPostGameRating(int? postGameRating, BattlegroundsRating? current)
    {
        var before = _ratingAtGameStart is { } r ? (_awaitingDuos ? r.Duos : r.Solo) : 0;
        int? after = postGameRating;
        if (after is null)
        {
            var value = current is null ? 0 : _awaitingDuos ? current.Duos : current.Solo;
            if (value > 0 && value != before)
            {
                after = value;
            }
        }

        if (after is > 0 && after != before)
        {
            try
            {
                _history.UpdateRatingAfter(_awaitingRatingKey!, after.Value);
            }
            catch (Exception ex)
            {
                Diag.Error("Saving rating failed", ex);
            }

            if (_lastOutcome is not null)
            {
                _lastOutcome = _lastOutcome with { RatingAfter = after };
                GameFinished?.Invoke(_lastOutcome);
            }

            _rating = _awaitingDuos
                ? (_rating ?? new BattlegroundsRating(0, 0)) with { Duos = after.Value }
                : (_rating ?? new BattlegroundsRating(0, 0)) with { Solo = after.Value };
            _awaitingRatingKey = null;
            _dirty = true;
        }
        else if (Clock() > _awaitingRatingUntil)
        {
            _awaitingRatingKey = null;
        }
    }

    private static RankInfo? PickRank(MatchPlayerInfo? player, string format) => format switch
    {
        "FT_STANDARD" => player?.Standard,
        "FT_WILD" => player?.Wild,
        "FT_CLASSIC" => player?.Classic,
        "FT_TWIST" => player?.Twist,
        _ => null,
    };

    private HeroStats? LookupHeroStats(string baseHeroId)
    {
        lock (_statsGate)
        {
            if (_heroStats is null)
            {
                try
                {
                    var cards = _cards;
                    _heroStats = _history.HeroStats(cards.BaseHeroId)
                        .ToDictionary(h => h.HeroCardId, h => new HeroStats(h.Games, h.AveragePlace, h.Top4Rate, h.WinRate));
                }
                catch (Exception ex)
                {
                    Diag.Error("Hero stats query failed", ex);
                    _heroStats = new Dictionary<string, HeroStats>();
                }
            }

            return _heroStats.GetValueOrDefault(baseHeroId);
        }
    }
}
