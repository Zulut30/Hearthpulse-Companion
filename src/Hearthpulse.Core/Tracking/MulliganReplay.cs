using System.Globalization;
using Hearthpulse.Core.Api;
using Hearthpulse.Core.Cards;
using Hearthpulse.Core.Constructed;
using Hearthpulse.Core.Hearthstone;
using Hearthpulse.Core.Power;

namespace Hearthpulse.Core.Tracking;

/// <summary>
/// Replays the newest kept Standard log up to the local player's mulligan and builds the panel for it, for the
/// <c>--render-ui</c> checks of both apps. Reads the logs only; the guide comes from the API (or its disk cache).
/// </summary>
public static class MulliganReplay
{
    /// <summary>The Constructed view at the first mulligan of the newest Standard game whose deck Decks.log names, or null.</summary>
    /// <param name="offered">Only a mulligan with this many offered cards (3 going first, 4 on the coin); null for any.</param>
    public static async Task<ConstructedView?> LatestAsync(string installDirectory, CardDb cards, HearthpulseData? data, ConstructedMeta? meta, int? offered = null, CancellationToken cancellationToken = default)
    {
        foreach (var dir in HearthstoneInstall.AllLogSessions(installDirectory).Reverse())
        {
            var power = Path.Combine(dir, "Power.log");
            var decks = Path.Combine(dir, "Decks.log");
            if (!File.Exists(power) || !File.Exists(decks))
            {
                continue;
            }

            var found = Replay(power, ReadDecks(decks), cards, offered);
            if (found is null)
            {
                continue;
            }

            var (game, deck, tracker) = found.Value;
            var playerClass = cards.Get(game.LocalHero?.CardId)?.CardClass ?? string.Empty;
            var guide = data is null ? null : await MulliganGuideSource.LoadGuideAsync(data, game.FormatType == "FT_WILD" ? "wild" : "standard", playerClass, deck.Cards, cards,
                cards.Get(game.OpponentHero?.CardId)?.CardClass, Mulligan.GoingFirst(game, cards), cancellationToken: cancellationToken).ConfigureAwait(false);
            var view = tracker.BuildView(game, cards);
            return view with { Mulligan = Mulligan.Build(game, cards, deck, meta, playerClass, guide) };
        }

        return null;
    }

    /// <summary>
    /// A fixed opening hand with a collected deck's numbers, for the <c>overlay-mulligan-deck</c> and
    /// <c>-near</c> renders: no logs and no network, so the source line ("By your deck, N games, data from ...") can be
    /// checked on any machine. The rows are a trimmed live answer of 2026-11-15 (C'Thun Warlock, Legend, all games).
    /// </summary>
    /// <param name="near">The list differs from the player's by two cards (<see cref="MulliganScope.NearDeck"/>).</param>
    public static ConstructedView? DeckSample(CardDb cards, bool near)
    {
        (int Dbf, string Id, int Copies, double Keep, double Opening, double Drawn, double Turn)[] rows =
        [
            (129793, "BE_085", 2, 0.971, 0.5672, 0.5439, 4.59),
            (126477, "JAIL_514", 2, 0.8055, 0.5576, 0.5567, 5.68),
            (69519, "CORE_CS2_062", 2, 0.2733, 0.5130, 0.5387, 7.51),
            (127067, "CAP_405", 1, 0.9274, 0.6019, 0.5701, 5.4),
            (125917, "JAIL_399", 2, 0.5723, 0.5049, 0.5357, 6.34),
            (126135, "JAIL_513", 2, 0.3597, 0.4990, 0.5305, 6.58),
        ];
        const int Games = 456569;
        var list = rows.ToDictionary(r => r.Dbf, r => r.Copies);
        var slice = new DeckMulliganSlice("7X8Zqlb1wBPwkkaQVGT8ee", "legend", DeckMulliganSlice.AllGames, MulliganGuide.AnyOpponent, Games, 0.5409, new DateTime(2026, 11, 14, 1, 5, 27, DateTimeKind.Utc), list,
            rows.Select(r =>
            {
                var dealt = (int)(Games * HearthpulseData.DealtShare(r.Copies, 30, DeckMulliganSlice.AllGames));
                return new GuideCard(r.Id, r.Dbf, cards.Get(r.Id)?.Cost ?? 0, dealt, r.Keep, dealt, r.Opening, r.Turn) { Drawn = dealt, DrawnWinRate = r.Drawn };
            }).ToList());
        var guide = new GuideArchetype(895, "C'Thun Warlock", "WARLOCK", slice.Cards, [],
            Localization.Loc.F("Source.MulliganDeck", HearthpulseData.Attribution, Localization.Loc.T("Core.MulliganDeckRank.legend")))
        {
            BaselineWinRate = slice.BaseWinRate,
            Scope = near ? MulliganScope.NearDeck : MulliganScope.Deck,
            DeckId = slice.DeckId,
            DeckDifference = near ? 2 : 0,
            DeckSlices = [slice],
        };
        var hand = new[] { "BE_085", "CORE_CS2_062", "CAP_405" }
            .Select((id, i) => new OfferedCard(100 + i, id, cards.NameOf(id), cards.Get(id)?.Cost ?? 0))
            .ToList();
        var deck = rows.ToDictionary(r => r.Id, r => r.Copies, StringComparer.Ordinal);
        return MulliganGuide.Compute(guide, hand, deck, opponentClass: null, goingFirst: null, guide.Name) is { } view
            ? new ConstructedView(1, "C'Thun Warlock", true, "HERO_07", "WARLOCK", "HERO_08", "MAGE", null, [], 27, 3, [], [], 26, 4, 0, null, null) { Mulligan = view }
            : null;
    }

    private static (GameState Game, DeckDefinition Deck, ConstructedTracker Tracker)? Replay(string log, List<(TimeSpan Time, DeckDefinition Deck)> decks, CardDb cards, int? offered)
    {
        var coin = cards.Get("GAME_005")?.Name;
        if (decks.Count == 0)
        {
            return null;
        }

        var parser = new PowerParser(DateOnly.FromDateTime(File.GetLastWriteTime(log)));
        var tracker = new ConstructedTracker();
        parser.AddListener(tracker);
        var started = TimeSpan.Zero;
        foreach (var line in ReadShared(log))
        {
            if (line.Contains("CREATE_GAME", StringComparison.Ordinal) && Time(line) is { } at)
            {
                started = at;
                tracker.Deck = null;
            }

            parser.Feed(line);
            if (!line.Contains("MULLIGAN_STATE value=INPUT", StringComparison.Ordinal))
            {
                continue;
            }

            parser.Flush();
            if (parser.Game is not { FormatType: "FT_STANDARD", LocalPlayerId: > 0 } game || !game.IsConstructedLike || !Mulligan.InProgress(game)
                || !game.Controlled(game.LocalPlayerId, "HAND").Any(e => e.CardId.Length > 0))
            {
                continue;
            }

            if (offered is { } count && game.Controlled(game.LocalPlayerId, "HAND")
                    .Count(e => e.CardId.Length > 0 && e.CardId != "GAME_005" && (coin is null || cards.Get(e.CardId)?.Name != coin)) != count)
            {
                continue;
            }

            // The deck queued last before this game started is the one being played.
            var deck = decks.LastOrDefault(d => d.Time <= started).Deck;
            if (deck is not null)
            {
                tracker.Deck = deck;
                return (game, deck, tracker);
            }
        }

        return null;
    }

    /// <summary>"Finding Game With Deck:" blocks of Decks.log with their time; the deck code is the block's last line.</summary>
    private static List<(TimeSpan Time, DeckDefinition Deck)> ReadDecks(string path)
    {
        var result = new List<(TimeSpan, DeckDefinition)>();
        TimeSpan? blockTime = null;
        string name = string.Empty;
        foreach (var line in ReadShared(path))
        {
            var content = GameSession.DecksContent(line);
            if (content is null)
            {
                continue;
            }

            if (content.EndsWith("With Deck:", StringComparison.Ordinal))
            {
                blockTime = Time(line);
                name = string.Empty;
            }
            else if (blockTime is not null && content.StartsWith("### ", StringComparison.Ordinal))
            {
                name = content[4..];
            }
            else if (blockTime is { } at && !content.StartsWith('#') && DeckString.TryDecode(content, name) is { } deck)
            {
                result.Add((at, deck));
                blockTime = null;
            }
        }

        return result;
    }

    private static TimeSpan? Time(string line)
    {
        var parts = line.Split(' ', 3);
        return parts.Length > 1 && TimeSpan.TryParse(parts[1], CultureInfo.InvariantCulture, out var time) ? time : null;
    }

    private static IEnumerable<string> ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }
}
