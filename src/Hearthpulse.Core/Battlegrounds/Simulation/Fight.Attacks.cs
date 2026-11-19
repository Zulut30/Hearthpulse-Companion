using Hearthpulse.Core.Battlegrounds.Effects;

namespace Hearthpulse.Core.Battlegrounds;

/// <summary>Attacks: who attacks, the target (taunt, stealth, forced), windfury and queued attacks.</summary>
public sealed partial class Fight
{
    // The candidates of one attack's target pick, reused (a pick never runs inside another).
    private List<Unit>? _targets;

    private void Attack(Unit attacker, Unit? forcedTarget)
    {
        var defenders = attacker.Enemy;
        var target = forcedTarget is { IsAlive: true } && forcedTarget.Side == defenders ? forcedTarget : PickTarget(attacker, defenders);
        if (target is null || TooDeep())
        {
            return;
        }

        _chooser.OnAttack(this, attacker, target, nested: _attacksInProgress > 0);
        var previous = Attacking;
        Attacking = attacker.Side;
        // Counted, not a single "current attacker": a nested attack (Rally, "attack immediately") keeps the outer
        // attacker attacking.
        attacker.AttackDepth++;
        _attacksInProgress++;
        _depth++;
        _attacks++;
        try
        {
            attacker.Stealth = false;
            if (Tracing) Trace($"{Name(attacker)} attacks {Name(target)}");

            // Attack declared, before damage: "whenever a friendly minion attacks" first, then the attacker's own Rally /
            // "whenever this attacks" (effects-audit E1: 15.11 G1 Prodigious Tusker's gem on Bonker at line 236025, Bonker's
            // Rally at 236080; G4 Faerie Dragon Scale 1331318, Rally 1331351), then the defending side's triggers.
            Raise(attacker.Side, Hook.FriendlyAttack, attacker, target);
            if ((attacker.Hooks & Hook.Attack) != 0)
            {
                foreach (var effect in attacker.Effects)
                {
                    if ((effect.Hooks & Hook.Attack) != 0)
                    {
                        effect.OnAttack(this, attacker, target);
                    }
                }
            }

            Raise(defenders, Hook.FriendlyAttacked, target, attacker);
            RefreshAuras();
            ResolveDeaths();
            if (!attacker.IsAlive || !target.IsAlive)
            {
                // The attacker or its target died before damage (e.g. to a Rally): the attack does not happen.
                if (Tracing) Trace("attack cancelled");
                return;
            }

            // Combat damage, simultaneous: both amounts are read before either is dealt.
            var toTarget = attacker.Attack;
            var toAttacker = target.Attack;
            var targetHealth = target.Health;
            BeginDamageBatch();
            var dealt = DamageCore(attacker, target, toTarget);
            DamageCore(target, attacker, toAttacker);
            if ((attacker.Hooks & Hook.AttackDamage) != 0)
            {
                foreach (var effect in attacker.Effects)
                {
                    if ((effect.Hooks & Hook.AttackDamage) != 0)
                    {
                        effect.OnAttackDamage(this, attacker, target);
                    }
                }
            }

            EndDamageBatch();

            var killed = target.IsDying;
            var outcome = new AttackOutcome(dealt, killed, killed ? Math.Max(0, dealt - Math.Max(0, targetHealth)) : 0);
            if ((attacker.Hooks & Hook.AfterAttack) != 0 && attacker.IsOnBoard)
            {
                foreach (var effect in attacker.Effects)
                {
                    if ((effect.Hooks & Hook.AfterAttack) != 0)
                    {
                        effect.AfterAttack(this, attacker, target, outcome);
                    }
                }
            }

            Raise(attacker.Side, Hook.AfterFriendlyAttack, attacker, target, outcome: outcome);
            RefreshAuras();
            ResolveDeaths();
        }
        finally
        {
            _depth--;
            _attacksInProgress--;
            Attacking = previous;
            attacker.AttackDepth--;
        }
    }

    /// <summary>Random target: Taunt minions first; Stealth minions only when nothing else can be attacked.</summary>
    private Unit? PickTarget(Unit attacker, Side side)
    {
        int visible = 0, visibleTaunts = 0, alive = 0, taunts = 0;
        foreach (var unit in side.Minions)
        {
            if (!unit.IsAlive)
            {
                continue;
            }

            alive++;
            if (unit.Taunt) taunts++;
            if (!unit.Stealth)
            {
                visible++;
                if (unit.Taunt) visibleTaunts++;
            }
        }

        if (alive == 0)
        {
            return null;
        }

        // 0: visible taunts, 1: visible, 2: any taunt, 3: any.
        var mode = visible > 0 ? (visibleTaunts > 0 ? 0 : 1) : (taunts > 0 ? 2 : 3);
        var targets = _targets ??= new List<Unit>(Side.MaxMinions);
        targets.Clear();
        foreach (var unit in side.Minions)
        {
            if (!unit.IsAlive)
            {
                continue;
            }

            var eligible = mode switch
            {
                0 => !unit.Stealth && unit.Taunt,
                1 => !unit.Stealth,
                2 => unit.Taunt,
                _ => true,
            };
            if (eligible)
            {
                targets.Add(unit);
            }
        }

        var target = targets[Choose(ChoiceKind.AttackTarget, attacker, targets)];
        targets.Clear();
        return target;
    }

    /// <summary>Makes <paramref name="attacker"/> attack right now, out of turn order (e.g. a Golem that "attacks the target first").</summary>
    /// <param name="target">The target, or null for a normal random target.</param>
    public void AttackNow(Unit attacker, Unit? target = null)
    {
        if (attacker.IsAlive && _attacks < MaxAttacks)
        {
            Attack(attacker, target);
        }
    }

    /// <summary>"Attacks immediately": queues an out-of-turn attack that happens right after the current attack finishes.</summary>
    public void AttackSoon(Unit attacker) => (_queuedAttacks ??= new Queue<Unit>()).Enqueue(attacker);

    private void RunQueuedAttacks()
    {
        while (_queuedAttacks is { Count: > 0 } && Ours.Count > 0 && Theirs.Count > 0 && _attacks < MaxAttacks)
        {
            var unit = _queuedAttacks.Dequeue();
            if (unit.IsAlive)
            {
                Attack(unit, null);
            }
        }

        _queuedAttacks?.Clear();
    }
}
