namespace Hearthpulse.Core.Battlegrounds;

public sealed partial class Side
{
    /// <summary>
    /// Duos: the teammate whose board entered this fight after this side's first board was cleared; false until then and
    /// in a solo fight.
    /// </summary>
    public bool TeammateEntered { get; private set; }

    /// <summary>
    /// Duos: this side's board is empty and <paramref name="teammate"/> takes over. Player-level state belongs to the player
    /// who fights now: the captured info (hero, hero power, tags, hand), player-level effects, per-fight counters, summon
    /// buffs and extra deathrattle triggers are the teammate's from here; the minions that died stay in
    /// <see cref="DeadThisFight"/> (the team's dead).
    /// </summary>
    internal void EnterTeammate(SimSide teammate)
    {
        Info = teammate;
        TeammateEntered = true;
        _extras.Clear();
        _counters = null;
        _summonBuffs = null;
        _hand = null;
        ExtraDeathrattles = 0;
        Gap = 0;
        LastAttacker = null;
        HasAppliedAuras = false;
        InvalidateHooks();
    }
}
