namespace Hearthpulse.Core.Battlegrounds.Effects.Timewarped;

// Timewarped minions with "whenever/after" triggers. Their texts carry no {0}/{1} placeholder, so the client does not
// show a number from the entity's script data: the numbers below are the latest card text (HearthstoneJSON enUS,
// build 256589). No game of the owner has one of these cards in a fight.

/// <summary>
/// Timewarped Chimera (BG34_Giant_679): "Whenever this takes damage, give a friendly minion of each type +2/+1
/// permanently." Golden: "…, twice."
/// </summary>
/// <remarks>
/// The selection follows The Last One Standing (same assumption, not on the card): for each type in a fixed order one
/// random living friendly minion of that type, itself included, at most one per minion per trigger, minions with fewer
/// types first (so the all-type Chimera covers a type only when nothing else can). The chosen minions then get +2/+1.
/// Golden: the whole effect twice, with fresh picks. Not when Divine Shield absorbed the hit.
/// </remarks>
[CardEffect("BG34_Giant_679")]
internal sealed class TimewarpedChimera : CombatEffect
{
    private static readonly Tribe[] Types =
    [
        Tribe.Beast, Tribe.Demon, Tribe.Dragon, Tribe.Elemental, Tribe.Mech, Tribe.Murloc,
        Tribe.Naga, Tribe.Pirate, Tribe.Quilboar, Tribe.Undead, Tribe.Aberration,
    ];

    public override void OnDamaged(Fight fight, Unit self, Unit? source, int amount)
    {
        var side = self.Side;
        for (var time = 0; time < self.Pick(1, 2); time++)
        {
            var chosen = 0;
            foreach (var type in Types)
            {
                var index = Pick(fight, self, side, type, chosen);
                if (index >= 0)
                {
                    chosen |= 1 << index;
                }
            }

            for (var i = 0; i < side.Count; i++)
            {
                if ((chosen & (1 << i)) != 0)
                {
                    fight.Buff(side.Minions[i], 2, 1);
                }
            }
        }
    }

    /// <summary>Board index of the random minion chosen for <paramref name="type"/>, or -1.</summary>
    private static int Pick(Fight fight, Unit self, Side side, Tribe type, int chosen)
    {
        var fewest = int.MaxValue;
        var count = 0;
        for (var i = 0; i < side.Count; i++)
        {
            if (Candidate(side.Minions[i], type, chosen, i))
            {
                var types = TypeCount(side.Minions[i]);
                if (types < fewest)
                {
                    (fewest, count) = (types, 1);
                }
                else if (types == fewest)
                {
                    count++;
                }
            }
        }

        if (count == 0)
        {
            return -1;
        }

        var candidates = new List<Unit>(count);
        for (var i = 0; i < side.Count; i++)
        {
            if (Candidate(side.Minions[i], type, chosen, i) && TypeCount(side.Minions[i]) == fewest)
            {
                candidates.Add(side.Minions[i]);
            }
        }

        var pick = count > 1 ? fight.Choose(ChoiceKind.RandomTarget, self, candidates) : 0;
        return side.IndexOf(candidates[pick]);
    }

    private static bool Candidate(Unit unit, Tribe type, int chosen, int index) =>
        (chosen & (1 << index)) == 0 && unit.IsAlive && unit.Is(type);

    private static int TypeCount(Unit unit) => System.Numerics.BitOperations.PopCount((uint)(unit.Tribes & Tribe.All));
}

/// <summary>
/// Timewarped Deadstomper (BG34_Giant_654): "After you summon a minion, give your minions +4 Attack permanently."
/// Golden: +8. Every living friendly minion gets it, the summoned one and the Deadstomper included.
/// </summary>
[CardEffect("BG34_Giant_654")]
internal sealed class TimewarpedDeadstomper : CombatEffect
{
    public override void AfterFriendlySummoned(Fight fight, Unit self, Unit summoned) =>
        TimewarpedKit.BuffAll(fight, self.Side, Tribe.All, self.Pick(4, 8), 0);
}

/// <summary>
/// Timewarped Deathswarmer (BG34_Giant_081): "Whenever this takes damage, your Undead have +1 Attack this game (wherever
/// they are)." Golden: +2. Living friendly Undead (itself included) now, Undead summoned later this fight on arrival; not
/// when Divine Shield absorbed the hit.
/// </summary>
[CardEffect("BG34_Giant_081")]
internal sealed class TimewarpedDeathswarmer : CombatEffect
{
    public override void OnDamaged(Fight fight, Unit self, Unit? source, int amount) =>
        TimewarpedKit.UndeadAttack(fight, self.Side, self.Pick(1, 2));
}

/// <summary>Timewarped Duskmaw (BG34_PreMadeChamp_020): "Avenge (1): Give your Dragons +6/+4." Golden: +12/+8 (itself included).</summary>
[CardEffect("BG34_PreMadeChamp_020")]
internal sealed class TimewarpedDuskmaw : AvengeEffect
{
    protected override int Threshold(Unit self) => 1;

    protected override void Avenge(Fight fight, Unit self) =>
        TimewarpedKit.BuffAll(fight, self.Side, Tribe.Dragon, self.Pick(6, 12), self.Pick(4, 8));
}

/// <summary>
/// Timewarped Gemsplitter (BG34_Giant_644): "Divine Shield. After a friendly minion loses Divine Shield, your Blood Gems
/// give an extra +1 Attack this game." Golden: +2. Changes the fight only through Blood Gems played later in it; Divine
/// Shield is a keyword the engine handles.
/// </summary>
[CardEffect("BG34_Giant_644")]
internal sealed class TimewarpedGemsplitter : CombatEffect
{
    public override void OnFriendlyShieldLost(Fight fight, Unit self, Unit unit) =>
        TimewarpedKit.ImproveBloodGems(self.Side, self.Pick(1, 2), 0);
}

/// <summary>Timewarped Hyena (BG34_Giant_581): "Whenever a friendly Beast dies, gain +2/+2 permanently." Golden: +4/+4.</summary>
[CardEffect("BG34_Giant_581")]
internal sealed class TimewarpedHyena : CombatEffect
{
    public override void AfterFriendlyDeath(Fight fight, Unit self, Unit dead)
    {
        if (self.IsAlive && dead.Is(Tribe.Beast))
        {
            var bonus = self.Pick(2, 4);
            fight.Buff(self, bonus, bonus);
        }
    }
}

/// <summary>
/// Timewarped Jungle King (BG34_PreMadeChamp_004): "Stealth. After you summon a Beast, give it +4/+3. Improves after you
/// cast a spell." Golden: +8/+6.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The bonus grown from the spells cast this game is the King's captured TAG_SCRIPT_DATA_NUM_1/2: cards that
/// "improve" keep their current value there even when HearthstoneJSON prints a literal number (The Last One Standing,
/// BG34_320: "+10/+10" in the text, NUM_1/2 10/20/30 in real logs of 2026-11-15). Without script data (a King made in
/// combat, or a log that sent none) the printed +4/+3 (golden +8/+6) is used.</item>
/// <item>"Improves after you cast a spell" during the fight: the step of one improvement is not in the text or a kept log,
/// so a spell cast in combat leaves the bonus unchanged for the rest of the fight (a lower bound).</item>
/// <item>No real log with the King was available (2026-11-19). Stealth is a keyword the engine handles.</item>
/// </list>
/// </remarks>
[CardEffect("BG34_PreMadeChamp_004")]
internal sealed class TimewarpedJungleKing : CombatEffect
{
    public override void AfterFriendlySummoned(Fight fight, Unit self, Unit summoned)
    {
        if (summoned.Is(Tribe.Beast))
        {
            var (attack, health) = Deaths.DeathKit.Shown(self, (4, 3), (8, 6));
            fight.Buff(summoned, attack, health);
        }
    }
}

/// <summary>
/// Timewarped Piper (BG34_Giant_069): "Whenever this takes damage, your Blood Gems give an extra +1 Attack this game.
/// (3 times per combat.)" Golden: +2. Changes the fight only through Blood Gems played later in it.
/// </summary>
/// <remarks>The three uses are counted from the start of the simulated fight (<see cref="Unit.Counter"/>, reset there).</remarks>
[CardEffect("BG34_Giant_069")]
internal sealed class TimewarpedPiper : CombatEffect
{
    public override void OnFightStart(Fight fight, Unit self) => self.Counter = 0;

    public override void OnDamaged(Fight fight, Unit self, Unit? source, int amount)
    {
        if (self.Counter < 3)
        {
            self.Counter++;
            TimewarpedKit.ImproveBloodGems(self.Side, self.Pick(1, 2), 0);
        }
    }
}

/// <summary>
/// Timewarped Tamuzo (BG34_Giant_595): "After you summon a minion in combat, double its stats." Golden: "triple its
/// stats". The summoned minion's Attack and Health (current and maximum) are multiplied as it has them after its summon
/// buffs; effects that react to the same summon earlier in board order act first.
/// </summary>
[CardEffect("BG34_Giant_595")]
internal sealed class TimewarpedTamuzo : CombatEffect
{
    public override void AfterFriendlySummoned(Fight fight, Unit self, Unit summoned)
    {
        if (!summoned.IsOnBoard)
        {
            return;
        }

        var extra = self.Pick(1, 2);
        fight.Buff(summoned, Times(summoned.Attack, extra), Times(Math.Max(0, summoned.Health), extra));
    }

    private static int Times(int value, int factor) => (int)Math.Min(int.MaxValue, (long)value * factor);
}
