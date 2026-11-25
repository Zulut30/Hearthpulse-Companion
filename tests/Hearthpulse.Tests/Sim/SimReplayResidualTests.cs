using System.IO.Compression;
using Hearthpulse.Bench;
using Hearthpulse.Core.Cards;

namespace Hearthpulse.Tests.Sim;

/// <summary>
/// Real fights → tests (package ACC sim-replay-residuals, 2026-11-19): the fights of the kept log of 15.11.2026
/// (<c>D:\Hearthstone\Logs\Hearthstone_2026_11_15_20_36_45</c>, build 255790) that Bench <c>simreplay</c> found diverging
/// after the engine rules package, replayed step by step through <see cref="SimReplayKit"/>. They run only where that log
/// is kept (<see cref="RealLogsFactAttribute"/> with the folder name); the rules themselves are pinned without it by
/// <c>VeryHungryWinterfinnerTests</c> and <c>TarecgosaTests</c>.
/// </summary>
/// <remarks>
/// Cards: <c>Fixtures/SimReplay/cards-2026-11-15.json.gz</c> is the subset of HearthstoneJSON enUS cards.json
/// (Last-Modified Tue, 03 Nov 2026 17:22:53 GMT) holding every CardID/cardId the log names plus their golden/normal
/// partners (947 cards): <c>grep -o "CardID=…|cardId=…" Power.log | sort -u</c>, then the cards with those ids, written
/// as compact JSON and gzip -9.
/// </remarks>
public sealed class SimReplayResidualTests
{
    private const string Log = "Hearthstone_2026_11_15_20_36_45";

    private static readonly Lazy<CardDb> Cards = new(() =>
    {
        using var gz = new GZipStream(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "SimReplay", "cards-2026-11-15.json.gz")),
            CompressionMode.Decompress);
        return CardDb.Parse("enUS", gz);
    });

    private static readonly Lazy<IReadOnlyList<ReplayableFight>> Fights = new(() =>
        SimReplayKit.Fights(RealLogTests.SharedLines(Path.Combine(RealLogTests.LogRoot, Log, "Power.log")), new DateOnly(2026, 11, 15), Cards.Value));

    private static ReplayableFight Fight(string key) => Fights.Value.Single(f => f.Key.EndsWith(key, StringComparison.Ordinal));

    [RealLogsFact(Log)]
    public void Winterfinner_buffs_the_flighty_scout_in_hand_before_bassgill_summons_it()
    {
        // Game 2 fight 9: Winterfinner takes 49 and gives Flighty Scout in hand +2/+1 (179/172 → 181/173); Bassgill's
        // deathrattle then summons it as 181/173 (the sim had 179 before the effect existed).
        SimReplayKit.AssertReplays(Fight("#2/9"), Cards.Value);
    }

    [RealLogsFact(Log)]
    public void Winterfinner_buffs_the_dark_paradox_in_hand_before_it_is_summoned()
    {
        // Game 2 fight 12: Winterfinner's +2/+1 goes to Dark Paradox in hand (82/74 → 84/75), summoned later as a Golem.
        SimReplayKit.AssertReplays(Fight("#2/12"), Cards.Value);
    }

    [RealLogsFact(Log)]
    public void Golden_tarecgosa_doubles_eudoras_buff_on_itself()
    {
        // Game 5 fight 8: Ship Master Eudora gives +8/+8 once; golden Tarecgosa doubles its own to +16/+16 (36/34).
        SimReplayKit.AssertReplays(Fight("#5/8"), Cards.Value);
    }

    [RealLogsFact(Log)]
    public void The_last_one_standing_fight_replays_with_the_opponents_deity_awakening_from_its_progress()
    {
        // Game 3 fight 11: both Rallies of The Last One Standing match (+20/+20 then +10/+10), and the opponent's Y'Shaarj
        // awakens after attack 8 because its progress (QUEST_PROGRESS 2 of 4 on their BG_OldGod copy, carried over from
        // earlier fights) is an input of the simulator (SimSide.DeityAwakening).
        SimReplayKit.AssertReplays(Fight("#3/11"), Cards.Value);
    }
}
