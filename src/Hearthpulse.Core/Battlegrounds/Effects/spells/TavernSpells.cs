namespace Hearthpulse.Core.Battlegrounds.Effects.Spells;

/// <summary>
/// The Tavern spells that minions and trinkets cast in combat today, what each does there, and the player's Tavern spell
/// bonus. Card text from HearthstoneJSON (enUS, 2026-11-19).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Tavern spell bonus: the player tags <c>TAVERN_SPELL_ATTACK_INCREASE</c> / <c>TAVERN_SPELL_HEALTH_INCREASE</c> are
/// added to every "+X/+Y" part of a Tavern spell. Checked in a real log (2026-11-15): the opponent's player entity gets
/// both tags (10/10) at the start of combat, and each part of an in-combat Mighty Dragonbreath gave a non-shielded Dragon
/// +13/+12 (= 3+10 / 2+10) twice. A missing tag reads as 0, which is what the game sends for no bonus.</item>
/// <item>"Your minions" are the side's living minions when the part resolves.</item>
/// </list>
/// </remarks>
internal static class TavernSpells
{
    public const string MightyDragonbreath = "BG36_246";
    public const string ChefsChoice = "BG28_518";
    public const string MisplacedTeaSet = "BG28_888";
    public const string SpellAttackTag = "TAVERN_SPELL_ATTACK_INCREASE";
    public const string SpellHealthTag = "TAVERN_SPELL_HEALTH_INCREASE";

    /// <summary>The tribes in the order <see cref="Tribe"/> lists them (Misplaced Tea Set goes through them).</summary>
    private static readonly Tribe[] Types =
    [
        Tribe.Beast, Tribe.Demon, Tribe.Dragon, Tribe.Elemental, Tribe.Mech, Tribe.Murloc, Tribe.Naga, Tribe.Pirate,
        Tribe.Quilboar, Tribe.Undead, Tribe.Aberration,
    ];

    /// <summary>Mighty Dragonbreath's effect.</summary>
    public static readonly SpellResolver MightyDragonbreathSpell = ResolveMightyDragonbreath;

    /// <summary>Chef's Choice's effect.</summary>
    public static readonly SpellResolver ChefsChoiceSpell = ResolveChefsChoice;

    /// <summary>Misplaced Tea Set's effect.</summary>
    public static readonly SpellResolver MisplacedTeaSetSpell = ResolveMisplacedTeaSet;

    /// <summary>What a Tavern spell's printed "+attack/+health" gives for this side, with its Tavern spell bonus.</summary>
    public static (int Attack, int Health) Stats(Side side, int attack, int health) =>
        (attack + Math.Max(0, side.Counter(SpellAttackTag)), health + Math.Max(0, side.Counter(SpellHealthTag)));

    /// <summary>Casts <paramref name="cardId"/> for <paramref name="source"/>'s side (a Tavern spell).</summary>
    public static void Cast(Fight fight, Unit source, string cardId, Unit? target, SpellResolver resolve) =>
        fight.CastSpell(new SpellCast(source.Side, cardId, target, source, resolve));

    /// <summary>
    /// Mighty Dragonbreath (BG36_246): "Give your minions +3/+2. Repeat for your Dragons. Repeat for your minions with
    /// Divine Shield." Each part adds the Tavern spell bonus; "with Divine Shield" is checked when that part resolves.
    /// </summary>
    private static void ResolveMightyDragonbreath(Fight fight, in SpellCast spell)
    {
        var side = spell.Caster;
        var (attack, health) = Stats(side, 3, 2);
        BuffAll(fight, side, attack, health, static _ => true);
        BuffAll(fight, side, attack, health, static u => u.Is(Tribe.Dragon));
        BuffAll(fight, side, attack, health, static u => u.DivineShield);
    }

    /// <summary>
    /// Chef's Choice (BG28_518): "Choose a minion. Get a different minion of the same type." In combat the chosen minion
    /// is the spell's target; the caster gets a random minion of one of its types. Which minion that is (and its stats)
    /// is not modelled: it counts as a card added to the hand and is not put in the hand list. A target without a type
    /// gives nothing (assumption: no "same type" exists).
    /// </summary>
    private static void ResolveChefsChoice(Fight fight, in SpellCast spell)
    {
        if (spell.Target is { Tribes: not Tribe.None })
        {
            fight.AddToHand(spell.Caster, string.Empty);
        }
    }

    /// <summary>
    /// Misplaced Tea Set (BG28_888): "Give a friendly minion of each type +3/+3."
    /// </summary>
    /// <remarks>
    /// Each minion is buffed at most once and stands for one type. Real log (2026-11-15, two casts on one board of six
    /// Dragons, a Demon/Dragon and a Par-tea Guest of every type): three minions got +3/+3 (plus the bonus) once each:
    /// the all-type Guest, a Dragon and the Demon/Dragon; on a board of four Dragons and two Demon/Dragons: one
    /// Demon/Dragon and one Dragon. So the sim gives each type present one distinct minion, as many types as can be
    /// covered: the type with the fewest candidates left goes first (ties in <see cref="Tribe"/> order), and among its
    /// candidates those with the fewest other open types are preferred (an all-type minion covers what no one else can);
    /// the pick among them is random. Which minion the game picks among equals is unverified.
    /// </remarks>
    private static void ResolveMisplacedTeaSet(Fight fight, in SpellCast spell)
    {
        var side = spell.Caster;
        var alive = fight.Alive(side);
        var open = Tribe.None;
        foreach (var unit in alive)
        {
            open |= unit.Tribes;
        }

        var (attack, health) = Stats(side, 3, 3);
        var candidates = new List<Unit>(alive.Count);
        while (open != Tribe.None)
        {
            // The open type with the fewest candidates left.
            var type = Tribe.None;
            var fewest = int.MaxValue;
            foreach (var t in Types)
            {
                if ((open & t) == 0)
                {
                    continue;
                }

                var n = 0;
                foreach (var unit in alive)
                {
                    if (unit.Is(t))
                    {
                        n++;
                    }
                }

                if (n == 0)
                {
                    open &= ~t;
                }
                else if (n < fewest)
                {
                    (type, fewest) = (t, n);
                }
            }

            if (type == Tribe.None)
            {
                break;
            }

            // Its candidates covering the fewest other open types.
            candidates.Clear();
            var least = int.MaxValue;
            foreach (var unit in alive)
            {
                if (!unit.Is(type))
                {
                    continue;
                }

                var others = OpenTypes(unit.Tribes & open & ~type);
                if (others < least)
                {
                    candidates.Clear();
                    least = others;
                }

                if (others == least)
                {
                    candidates.Add(unit);
                }
            }

            var picked = PickForType(fight, spell.Source, candidates);
            open &= ~type;
            if (picked is null)
            {
                continue;
            }

            alive.Remove(picked);
            fight.Buff(picked, attack, health);
        }
    }

    private static int OpenTypes(Tribe tribes) => System.Numerics.BitOperations.PopCount((uint)tribes);

    /// <summary>The random pick of Misplaced Tea Set among equal candidates for one type.</summary>
    private static Unit? PickForType(Fight fight, Unit? source, List<Unit> candidates) => fight.RandomOf(candidates, source);

    private static void BuffAll(Fight fight, Side side, int attack, int health, Func<Unit, bool> which)
    {
        foreach (var unit in fight.Alive(side))
        {
            if (which(unit))
            {
                fight.Buff(unit, attack, health);
            }
        }
    }
}
