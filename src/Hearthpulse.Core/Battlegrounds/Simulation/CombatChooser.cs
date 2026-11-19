namespace Hearthpulse.Core.Battlegrounds;

/// <summary>What a decision of a fight is about. Every random pick of the engine and the effects names one.</summary>
public enum ChoiceKind
{
    /// <summary>Which side attacks first when the boards are the same size: index 0 = ours, 1 = theirs (no candidates).</summary>
    FirstAttacker,

    /// <summary>The minion an attack hits (source: the attacker; candidates: the minions it may attack).</summary>
    AttackTarget,

    /// <summary>A random minion an effect damages, buffs, destroys or changes (source: the effect's holder).</summary>
    RandomTarget,

    /// <summary>One of the minions tied on a value ("the highest Health"): candidates are the tied ones, in board order.</summary>
    TieBreak,

    /// <summary>One point of stats split at random among minions (C'Thun), one decision per point.</summary>
    StatSplit,

    /// <summary>A random card from a public pool to summon (Ghastcoiler): card candidates.</summary>
    RandomSummon,

    /// <summary>A random card from a public pool added to a hand (Deathly Striker): card candidates.</summary>
    RandomCard,

    /// <summary>A random card of the hand (Murcules): card candidates, the hand's cards in order.</summary>
    RandomHandCard,

    /// <summary>One of the hand's cards tied on a value ("the highest-Health Murloc in your hand"): card candidates.</summary>
    HandTieBreak,
}

/// <summary>
/// One decision of a fight: its kind, who makes it and the candidates, either minions (<see cref="Units"/>) or cards
/// (<see cref="Cards"/>), or neither for <see cref="ChoiceKind.FirstAttacker"/>. The lists are valid only during the call.
/// </summary>
/// <param name="Source">The minion (or player-level holder: hero power, trinket) whose rule or effect decides; null for the coin.</param>
/// <param name="Count">How many candidates there are (at least 1); the answer is an index below it.</param>
public readonly record struct CombatChoice(ChoiceKind Kind, Unit? Source, int Count, IReadOnlyList<Unit>? Units, IReadOnlyList<SimMinion>? Cards);

/// <summary>
/// Makes every random decision of a fight (<see cref="Fight.Choose(ChoiceKind, Unit?, IReadOnlyList{Unit})"/>) and may
/// watch its steps. The odds use <see cref="RandomChooser"/>; a replay of a real fight answers each decision with what the
/// log did (Bench <c>simreplay</c>).
/// </summary>
public interface ICombatChooser
{
    /// <summary>The index of the picked candidate, in [0, <see cref="CombatChoice.Count"/>).</summary>
    int Choose(Fight fight, in CombatChoice choice);

    /// <summary>An attack is about to happen (target known, nothing resolved); <paramref name="nested"/> = inside another attack.</summary>
    void OnAttack(Fight fight, Unit attacker, Unit target, bool nested)
    {
    }

    /// <summary>A minion was just put on the board (a token, a summon from a pool or a hand, a reborn copy).</summary>
    void OnSummon(Fight fight, Unit unit)
    {
    }
}

/// <summary>
/// The default chooser: a uniform pick from a seeded <see cref="Random"/>, one <see cref="Random.Next(int)"/> per decision
/// (tests script the picks by overriding it). A tie-break makes the draws of a running "keep the newest tie with
/// probability 1/n" pick: one draw per tied candidate after the first.
/// </summary>
public sealed class RandomChooser(Random random) : ICombatChooser
{
    public int Choose(Fight fight, in CombatChoice choice)
    {
        if (choice.Kind is ChoiceKind.TieBreak or ChoiceKind.HandTieBreak)
        {
            var pick = 0;
            for (var i = 1; i < choice.Count; i++)
            {
                if (random.Next(i + 1) == 0)
                {
                    pick = i;
                }
            }

            return pick;
        }

        return random.Next(choice.Count);
    }
}
