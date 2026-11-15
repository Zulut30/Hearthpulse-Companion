using Hearthpulse.Core.Replay;

namespace Hearthpulse.Bench;

/// <summary>
/// One combat of a recorded Battlegrounds replay: the minions on each table when the first minion attacked (after the
/// start-of-combat effects, the moment the simulator's boards are captured), left to right.
/// </summary>
/// <param name="FirstAttack">A minion attack was recorded; without one the boards are those of the combat's last step.</param>
/// <param name="PartnerSeen">Duos: minions of our partner's table were shown at some step of the combat.</param>
/// <param name="OursSeen">Minions of our own table were shown at some step of the combat.</param>
internal sealed record ReplayFight(
    int Turn,
    IReadOnlyList<BgReplayCard> Ours,
    IReadOnlyList<BgReplayCard> Theirs,
    bool FirstAttack,
    bool OursSeen,
    bool PartnerSeen,
    string? Result);

/// <summary>What the replays of a folder hold for the simulator's measurement.</summary>
/// <param name="DuosBothTables">Duos fights that showed both our table and our partner's.</param>
internal sealed record ReplayInventory(int Games, int Fights, int FightsWithFirstAttack, int DuosGames, int DuosFights, int DuosBothTables);

/// <summary>
/// Reads the combats of local replays (.hpr) for the simulator's measurement. A replay holds each table as the player
/// saw it: card, Attack, Health, damage, golden, keywords, tavern tier, the heroes, hero powers and trinkets. It does
/// not hold what else a captured fight is made of (<see cref="Missing"/>), so its fights cannot be simulated the way
/// the app simulated them; they are counted, and Duos fights are checked for both tables of the team.
/// </summary>
internal static class ReplayFights
{
    /// <summary>
    /// What <c>BattlegroundsTracker</c> hands the simulator at capture and no replay line carries (formats 4 and 5,
    /// <c>BattlegroundsReplayGrammar</c>): adding any of it is a new format (ADR 0004).
    /// </summary>
    public static IReadOnlyList<string> Missing { get; } =
    [
        "enchantments attached to a minion (Dark Gifts, granted deathrattles): SimMinion.Enchantments",
        "DEATHRATTLE / AVENGE / RALLY tags a minion gained: SimMinion.Mechanics",
        "TAG_SCRIPT_DATA_NUM_1/2 of a minion: SimMinion.ScriptData1/2",
        "the card that created a minion (CREATOR): SimMinion.CreatorCardId",
        "minions a start-of-combat trigger removed: SimMinion.Remembered",
        "the tribe a minion was given (CARDRACE): SimMinion.Tribes",
        "the players' numeric tags: SimSide.Tags",
        "values of our hero power and trinkets (TAG_SCRIPT_DATA_NUM_1/2): SimSide.ExtraTags",
        "the lobby's combat damage cap (BACON_COMBAT_DAMAGE_CAP): SimOptions.DamageCap",
        "the Deity of the lobby (BACON_GLOBAL_OLD_GOD_DBID): SimSide.Extras",
    ];

    /// <summary>The combats of a replay, in order.</summary>
    public static IReadOnlyList<ReplayFight> Of(BattlegroundsMatchReplay replay)
    {
        var fights = new List<ReplayFight>();
        BgReplayFrame? previous = null;
        BgReplayFrame? boards = null;
        var open = false;
        bool firstAttack = false, ours = false, partner = false;
        var turn = 0;

        void Close(string? result)
        {
            if (open && (boards ?? previous) is { } frame)
            {
                fights.Add(new ReplayFight(turn, Table(frame, 'L'), Table(frame, 'O'), firstAttack, ours, partner, result));
            }

            open = false;
        }

        foreach (var frame in replay.Frames)
        {
            if (frame.Kind == MatchStepKind.CombatStart)
            {
                Close(null);
                open = true;
                turn = frame.Turn;
                boards = null;
                firstAttack = ours = partner = false;
            }
            else if (open && frame.Phase != MatchPhase.Combat)
            {
                // A combat without its end (the recording stopped, or a combat cut at its step limit).
                Close(null);
            }

            if (open)
            {
                if (!firstAttack && frame is { Kind: MatchStepKind.Attack, Actor.Type: "MINION" } && previous is not null)
                {
                    // The step before the first minion attack is the table that attack started from.
                    firstAttack = true;
                    boards = previous;
                }

                ours |= Table(frame, 'L').Count > 0;
                partner |= Table(frame, 'P').Count > 0;
                if (frame.Kind == MatchStepKind.CombatEnd)
                {
                    Close(frame.Result);
                }
            }

            previous = frame;
        }

        Close(null);
        return fights;
    }

    /// <summary>Counts the Battlegrounds replays of <paramref name="replays"/> (file texts); other files are passed over.</summary>
    public static ReplayInventory Inventory(IEnumerable<string> replays)
    {
        int games = 0, fights = 0, attacked = 0, duosGames = 0, duosFights = 0, both = 0;
        foreach (var text in replays)
        {
            if (BattlegroundsMatchReplay.Parse(text) is not { } replay)
            {
                continue;
            }

            var list = Of(replay);
            games++;
            fights += list.Count;
            attacked += list.Count(f => f.FirstAttack);
            if (replay.Header.Summary?.Mode == "Duos")
            {
                duosGames++;
                duosFights += list.Count;
                both += list.Count(f => f.OursSeen && f.PartnerSeen);
            }
        }

        return new ReplayInventory(games, fights, attacked, duosGames, duosFights, both);
    }

    private static List<BgReplayCard> Table(BgReplayFrame frame, char side) =>
        frame.Cards.Where(c => c.Side == side && c is { Zone: "PLAY", Type: "MINION", Dying: false }).OrderBy(c => c.Position).ToList();
}
