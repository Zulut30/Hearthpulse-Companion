namespace Hearthpulse.Core.Battlegrounds;

/// <summary>
/// Duos team combat (TASK-2026-11-19 package D). Each side is a team: the board that starts the fight, then its
/// teammate's board (<see cref="SimSide.Teammate"/>), which enters once the first board has no minions left. The fight ends
/// when a team has no minions and no teammate left to bring in.
/// </summary>
/// <remarks>
/// Assumptions from the general Duos rules, not yet checked against a Duos Power.log (none among the owner's kept logs
/// on 2026-11-19; docs/bg-combat-sim.md "Duos"):
/// <list type="bullet">
/// <item>The teammate enters between attacks, after the death phase that cleared the first board: an attack in progress
/// (including the rest of a Windfury attacker's swings against the cleared board) ends there.</item>
/// <item>Sides keep alternating: the side whose turn comes next attacks next, whichever board it has; the new board
/// attacks from its left-most minion.</item>
/// <item>The teammate's Start of Combat effects are not simulated (as for the first boards, whose Start of Combat comes
/// from the log); the board enters as last seen, with its auras already counted in its stats.</item>
/// </list>
/// </remarks>
public sealed partial class Fight
{
    /// <summary>
    /// Both sides have minions on the board. A side left without any first brings in its teammate's board (Duos), so the
    /// fight goes on while a team still has minions.
    /// </summary>
    private bool BoardsStanding()
    {
        // Each side brings its teammate in at most once; an entering board's own deaths can clear the other side, so look
        // again after each entry.
        while (true)
        {
            if (Ours.Count == 0 && CanEnterTeammate(Ours))
            {
                EnterTeammate(Ours);
            }
            else if (Theirs.Count == 0 && CanEnterTeammate(Theirs))
            {
                EnterTeammate(Theirs);
            }
            else
            {
                return Ours.Count > 0 && Theirs.Count > 0;
            }
        }
    }

    private static bool CanEnterTeammate(Side side) => !side.TeammateEntered && side.Info.Teammate is not null;

    private void EnterTeammate(Side side)
    {
        var teammate = side.Info.Teammate!;
        side.EnterTeammate(teammate);
        foreach (var spec in teammate.Minions)
        {
            if (side.IsFull)
            {
                break;
            }

            side.AddInitial(CreateUnit(spec, side));
        }

        foreach (var id in teammate.Extras)
        {
            if (_registry.Get(id, golden: false) is { } effect)
            {
                side.AddExtra(new Unit(SimMinion.Create(id, 0, 0), side, [effect], extra: true));
            }
        }

        if (Tracing)
        {
            Trace($"{(side.IsOpponent ? "theirs" : "ours")}: teammate enters with {side}");
        }

        StartSide(side);
        BaselineAuras(side);
        RefreshAuras();
        ResolveDeaths();
        RunQueuedAttacks();
    }
}
