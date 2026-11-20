using Hearthpulse.Core.Battlegrounds.Effects;

namespace Hearthpulse.Core.Battlegrounds;

/// <summary>Battlecries triggered in combat and their multiplier, and the multiplier of spells cast in combat.</summary>
/// <remarks>
/// "Your Battlecries trigger twice" (Brann Bronzebeard; golden: three times) and "Your spells that target friendly
/// minions cast twice" (Balinda Stonehearth; golden: three times) say how many times in total, so copies do not add up:
/// the largest multiplier among the living minions wins (two Branns still trigger twice, a golden one three times).
/// Titus Rivendare's "an extra time" is different and adds up (<see cref="DeathrattleTriggers"/>), as Dragon's Eye's "Your
/// Dragons' Battlecries trigger an extra time" does for a Dragon's Battlecry (<see cref="BattlecryTriggers(Unit)"/>).
/// </remarks>
public sealed partial class Fight
{
    /// <summary>Per-player counter (<see cref="Side.Counter"/>): Battlecries the side triggered in this fight, multipliers included.</summary>
    public const string BattlecriesTriggeredKey = "sim:BattlecriesTriggered";

    /// <summary>How many times a Battlecry of this side triggers: 1 + the largest extra from its living minions (Brann).</summary>
    public int BattlecryTriggers(Side side) => 1 + LargestExtra(side, Hook.ExtraBattlecries, string.Empty, null);

    /// <summary>
    /// How many times <paramref name="source"/>'s Battlecry triggers: <see cref="BattlecryTriggers(Side)"/> plus the extra
    /// triggers that add up and depend on the source (Dragon's Eye: one more for a Dragon).
    /// </summary>
    public int BattlecryTriggers(Unit source) => BattlecryTriggers(source.Side) + AddedBattlecries(source);

    /// <summary>
    /// How many times a spell <paramref name="caster"/> casts on <paramref name="target"/> (null: no target) is cast:
    /// 1 + the largest extra from its living minions (Balinda).
    /// </summary>
    public int SpellCasts(Side caster, string spellCardId, Unit? target) => 1 + LargestExtra(caster, Hook.ExtraSpellCasts, spellCardId, target);

    /// <summary>A Battlecry printed on the card or given by a registered effect.</summary>
    public static bool HasBattlecry(Unit unit) => (unit.Mechanics & CardMechanic.Battlecry) != 0 || (unit.Hooks & Hook.Battlecry) != 0;

    /// <summary>
    /// Triggers a minion's Battlecry in combat ("trigger your right-most Battlecry"): each of its Battlecries runs
    /// <paramref name="times"/> × the multiplier in a row before the next one (A, A, B, B: the order real logs show for
    /// deathrattles with extra triggers), and after each trigger "after you trigger a Battlecry" hooks run. A Battlecry
    /// with no combat effect (it gets cards, gold...) still counts as triggered. Nothing happens for a minion without a
    /// Battlecry.
    /// </summary>
    /// <param name="withMultiplier">Also apply "Battlecries trigger twice" (Brann).</param>
    public void TriggerBattlecry(Unit unit, int times = 1, bool withMultiplier = true)
    {
        if (!HasBattlecry(unit) || times <= 0 || TooDeep())
        {
            return;
        }

        var total = times * (withMultiplier ? BattlecryTriggers(unit) : 1);
        _depth++;
        try
        {
            if ((unit.Hooks & Hook.Battlecry) == 0)
            {
                // A printed Battlecry the simulator has no combat effect for: it only counts.
                for (var t = 0; t < total; t++)
                {
                    BattlecryTriggered(unit);
                }

                return;
            }

            foreach (var effect in unit.Effects)
            {
                if ((effect.Hooks & Hook.Battlecry) == 0)
                {
                    continue;
                }

                for (var t = 0; t < total; t++)
                {
                    if (Tracing) Trace($"battlecry of {Name(unit)}");
                    effect.Battlecry(this, unit);
                    unit.Side.AddCounter(BattlecriesTriggeredKey, 1);
                    AfterBattlecry(unit.Side, unit);
                }
            }
        }
        finally
        {
            _depth--;
        }
    }

    private void BattlecryTriggered(Unit unit)
    {
        if (Tracing) Trace($"battlecry of {Name(unit)}");
        unit.Side.AddCounter(BattlecriesTriggeredKey, 1);
        AfterBattlecry(unit.Side, unit);
    }

    private void AfterBattlecry(Side side, Unit source)
    {
        if ((side.Hooks & Hook.FriendlyBattlecry) == 0 || TooDeep())
        {
            return;
        }

        _depth++;
        try
        {
            var observers = Snapshot(side, out var count);
            for (var i = 0; i < count; i++)
            {
                var observer = observers[i];
                if ((observer.Hooks & Hook.FriendlyBattlecry) == 0 || (!observer.IsOnBoard && !observer.IsExtra))
                {
                    continue;
                }

                foreach (var effect in observer.Effects)
                {
                    if ((effect.Hooks & Hook.FriendlyBattlecry) != 0)
                    {
                        effect.AfterFriendlyBattlecry(this, observer, source);
                    }
                }
            }
        }
        finally
        {
            _depth--;
        }
    }

    /// <summary>The sum of <see cref="CombatEffect.AddedBattlecries"/> over the living minions and extras of the source's side.</summary>
    private int AddedBattlecries(Unit source)
    {
        var side = source.Side;
        if ((side.Hooks & Hook.AddedBattlecries) == 0)
        {
            return 0;
        }

        var added = 0;
        _depth++;
        try
        {
            var observers = Snapshot(side, out var count);
            for (var i = 0; i < count; i++)
            {
                var unit = observers[i];
                if ((unit.Hooks & Hook.AddedBattlecries) == 0 || (!unit.IsAlive && !unit.IsExtra))
                {
                    continue;
                }

                foreach (var effect in unit.Effects)
                {
                    if ((effect.Hooks & Hook.AddedBattlecries) != 0)
                    {
                        added += Math.Max(0, effect.AddedBattlecries(this, unit, source));
                    }
                }
            }
        }
        finally
        {
            _depth--;
        }

        return added;
    }

    /// <summary>The largest extra count among the living minions and extras of a side that override <paramref name="hook"/>.</summary>
    private int LargestExtra(Side side, Hook hook, string spellCardId, Unit? target)
    {
        if ((side.Hooks & hook) == 0)
        {
            return 0;
        }

        var largest = 0;
        _depth++;
        try
        {
            var observers = Snapshot(side, out var count);
            for (var i = 0; i < count; i++)
            {
                var unit = observers[i];
                if ((unit.Hooks & hook) == 0 || (!unit.IsAlive && !unit.IsExtra))
                {
                    continue;
                }

                foreach (var effect in unit.Effects)
                {
                    if ((effect.Hooks & hook) != 0)
                    {
                        var extra = hook == Hook.ExtraBattlecries
                            ? effect.ExtraBattlecries(this, unit)
                            : effect.ExtraSpellCasts(this, unit, spellCardId, target);
                        largest = Math.Max(largest, extra);
                    }
                }
            }
        }
        finally
        {
            _depth--;
        }

        return largest;
    }
}
