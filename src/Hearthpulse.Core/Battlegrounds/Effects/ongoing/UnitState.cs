namespace Hearthpulse.Core.Battlegrounds.Effects.Ongoing;

/// <summary>
/// Per-minion state that does not fit in <see cref="Unit.Counter"/>/<see cref="Unit.Counter2"/> (a list, a reference to
/// another minion, or a counter on a minion whose own card already uses them). Effects are shared singletons, so the
/// state lives on the <see cref="Unit"/> of one fight (<see cref="Unit.EffectStates"/>, one object per state type): it is
/// never shared between fights, and it is freed with the unit. A unit belongs to one fight on one thread, so no lock is
/// needed (a ConditionalWeakTable took a lock and a GC handle per unit).
/// </summary>
internal static class UnitState<T> where T : class, new()
{
    public static T Of(Unit unit)
    {
        var states = unit.EffectStates;
        if (states is not null)
        {
            foreach (var state in states)
            {
                if (state.GetType() == typeof(T))
                {
                    return (T)state;
                }
            }
        }

        var created = new T();
        unit.EffectStates = states is null ? [created] : [.. states, created];
        return created;
    }
}
