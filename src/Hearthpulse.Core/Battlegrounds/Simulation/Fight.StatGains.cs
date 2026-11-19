using Hearthpulse.Core.Battlegrounds.Effects;

namespace Hearthpulse.Core.Battlegrounds;

/// <summary>"Whenever a friendly minion gains stats" hooks (<see cref="CombatEffect.AfterFriendlyGainedStats"/>).</summary>
public sealed partial class Fight
{
    private void StatsGained(Unit unit, int attack, int health)
    {
        var side = unit.Side;
        if (TooDeep())
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
                if ((observer.Hooks & Hook.FriendlyGainedStats) == 0 || (!observer.IsOnBoard && !observer.IsExtra))
                {
                    continue;
                }

                foreach (var effect in observer.Effects)
                {
                    if ((effect.Hooks & Hook.FriendlyGainedStats) != 0)
                    {
                        effect.AfterFriendlyGainedStats(this, observer, unit, attack, health);
                    }
                }
            }
        }
        finally
        {
            _depth--;
        }
    }
}
