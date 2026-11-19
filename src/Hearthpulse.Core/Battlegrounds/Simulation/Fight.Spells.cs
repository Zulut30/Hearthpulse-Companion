using Hearthpulse.Core.Battlegrounds.Effects;

namespace Hearthpulse.Core.Battlegrounds;

/// <summary>What a spell does in combat when it resolves (a static method: no closure per cast).</summary>
public delegate void SpellResolver(Fight fight, in SpellCast spell);

/// <summary>One spell cast in combat.</summary>
/// <param name="Caster">The side whose player casts it: "you cast a spell" for that side's triggers.</param>
/// <param name="CardId">The spell's card id.</param>
/// <param name="Target">The minion it is cast on ("a spell on this / on a Murloc"), or null for a spell without one.</param>
/// <param name="Source">The minion or player-level card whose effect cast it (a Rally, "it casts", a trinket), or null.</param>
/// <param name="Resolve">What the spell does; null when it does nothing that matters in combat.</param>
/// <param name="IsTavernSpell">A Tavern spell (card type BATTLEGROUND_SPELL), for "whenever you cast a Tavern spell".</param>
public readonly record struct SpellCast(Side Caster, string CardId, Unit? Target, Unit? Source, SpellResolver? Resolve, bool IsTavernSpell = true);

/// <summary>
/// Spells cast in combat, cards added to the hand and Blood Gems played in combat, with the hooks that react to them.
/// </summary>
/// <remarks>
/// Order of one cast, from a real Power.log (2026-11-15: a tavern spell played on a Murloc, and Crimson Vindicator's
/// Rally casting Mighty Dragonbreath in combat): "whenever you cast" triggers (<see cref="CombatEffect.OnFriendlySpellCast"/>)
/// run first, then the spell resolves (its POWER block), then "after you cast" triggers
/// (<see cref="CombatEffect.AfterFriendlySpellCast"/>; the player's spell counter fires there too). A spell a minion or
/// trinket casts in combat counts as the player's cast: the log shows the same player-level "spell cast" trigger after it
/// as after a spell played from hand. A Blood Gem a minion plays is not a cast (see <see cref="PlayBloodGem"/>).
/// <para>
/// Every observer list is a snapshot of the side at that moment (board left to right, then player-level cards). Deaths
/// are left to the engine step that started the cast: no spell cast in combat today deals damage. Nested casts (a
/// "whenever" trigger that casts) count towards the engine's depth limit, which also ends endless chains.
/// </para>
/// </remarks>
public sealed partial class Fight
{
    private SpellCast _spell;
    private bool _casting;

    /// <summary>True while a spell's hooks or effect run; <see cref="CurrentSpell"/> is then the innermost cast.</summary>
    public bool IsCasting => _casting;

    /// <summary>The spell being cast (valid while <see cref="IsCasting"/>, e.g. in <see cref="CombatEffect.AfterFriendlySpellCast"/>).</summary>
    public SpellCast CurrentSpell => _spell;

    /// <summary>
    /// A spell cast in combat by <paramref name="caster"/> without a target or source (kept for simple callers): runs
    /// <paramref name="resolve"/> through the full pipeline of <see cref="CastSpell(in SpellCast)"/>.
    /// </summary>
    public void CastSpell(Side caster, string spellCardId, Unit? target, Action<Fight>? resolve = null) =>
        CastSpell(new SpellCast(caster, spellCardId, target, null, resolve is null ? null : (Fight f, in SpellCast _) => resolve(f)));

    /// <summary>
    /// Casts a spell for <see cref="SpellCast.Caster"/>: <see cref="SpellCasts"/> times (1 + the largest
    /// <see cref="CombatEffect.ExtraSpellCasts"/>, e.g. Balinda), each time "whenever you cast" hooks, the spell's effect,
    /// then "after you cast" hooks. A spell on a minion that is no longer alive is not cast (nor its remaining extra casts).
    /// </summary>
    public void CastSpell(in SpellCast spell)
    {
        if (_depth >= MaxDepth)
        {
            return;
        }

        var times = SpellCasts(spell.Caster, spell.CardId, spell.Target);
        for (var i = 0; i < times; i++)
        {
            if (spell.Target is { IsAlive: false })
            {
                break;
            }

            CastOnce(spell);
        }
    }

    private void CastOnce(in SpellCast spell)
    {
        var caster = spell.Caster;
        if (Tracing) Trace($"{(caster.IsOpponent ? "they" : "we")} cast {spell.CardId}{(spell.Target is { } t ? " on " + Name(t) : string.Empty)}");
        var outer = _spell;
        var outerCasting = _casting;
        _spell = spell;
        _casting = true;
        _depth++;
        try
        {
            if ((caster.Hooks & Hook.SpellCasting) != 0)
            {
                var observers = Snapshot(caster, out var count);
                for (var i = 0; i < count; i++)
                {
                    var unit = observers[i];
                    if ((unit.Hooks & Hook.SpellCasting) == 0 || (!unit.IsOnBoard && !unit.IsExtra))
                    {
                        continue;
                    }

                    foreach (var effect in unit.Effects)
                    {
                        if ((effect.Hooks & Hook.SpellCasting) != 0)
                        {
                            effect.OnFriendlySpellCast(this, unit, spell);
                        }
                    }
                }
            }

            spell.Resolve?.Invoke(this, spell);
            Raise(caster, Hook.SpellCast, spell.Target, text: spell.CardId);
        }
        finally
        {
            _depth--;
            _spell = outer;
            _casting = outerCasting;
        }
    }

    // ================================================================================================ hand

    /// <summary>
    /// "Get a card" in combat: adds <paramref name="minion"/> (when the card is a minion the sim knows) to the side's hand
    /// list and fires "whenever a card is added to your hand" (<see cref="CombatEffect.AfterCardAddedToHand"/>).
    /// </summary>
    /// <param name="side">Whose hand.</param>
    /// <param name="cardId">The card's id; empty for a random card the simulator does not pick (it still counts as a card).</param>
    /// <param name="minion">The card as a minion, for effects that later summon from the hand; null for spells and unknown cards.</param>
    /// <remarks>
    /// The hand is assumed to have room: the capture holds only the local player's minions in hand (not its spells), and
    /// the opponent's hand is hidden, so a full hand cannot be told. The opponent's list starts empty and holds only
    /// cards got in this fight.
    /// </remarks>
    public void AddToHand(Side side, string cardId, SimMinion? minion = null)
    {
        if (minion is not null)
        {
            side.Hand.Add(minion);
        }

        if (Tracing) Trace($"{(side.IsOpponent ? "they" : "we")} get {(cardId.Length > 0 ? cardId : "a random card")}");
        if ((side.Hooks & Hook.CardToHand) == 0 || _depth >= MaxDepth)
        {
            return;
        }

        _depth++;
        try
        {
            var observers = Snapshot(side, out var count);
            for (var i = 0; i < count; i++)
            {
                var unit = observers[i];
                if ((unit.Hooks & Hook.CardToHand) == 0 || (!unit.IsOnBoard && !unit.IsExtra))
                {
                    continue;
                }

                foreach (var effect in unit.Effects)
                {
                    if ((effect.Hooks & Hook.CardToHand) != 0)
                    {
                        effect.AfterCardAddedToHand(this, unit, cardId);
                    }
                }
            }
        }
        finally
        {
            _depth--;
        }
    }

    // ================================================================================================ Blood Gems

    /// <summary>
    /// One Blood Gem played on <paramref name="target"/> by <paramref name="source"/>: +<paramref name="attack"/>/+<paramref name="health"/>,
    /// then "whenever a Blood Gem is played on X" hooks of the target's side (<see cref="CombatEffect.AfterFriendlyBloodGem"/>).
    /// </summary>
    /// <remarks>
    /// Not a spell cast: in a real log (2026-11-15) the Blood Gems minions play in combat (Bonker, Prodigious Tusker) are a
    /// separate card, BG20_GEM_No_Impact, and unlike every spell played from hand or cast by a minion (Blood Gems played
    /// from hand included) they are not followed by the player's "spell cast" trigger. So they do not reach "whenever you
    /// cast a spell" hooks.
    /// </remarks>
    public void PlayBloodGem(Unit? source, Unit target, int attack, int health)
    {
        if (!target.IsOnBoard)
        {
            return;
        }

        Buff(target, attack, health);
        // "Its Blood Gems" (Blood Golem Sticker) grows with every gem, as tags 4235/4236 do in real logs.
        target.BloodGemAttack += attack;
        target.BloodGemHealth += health;
        if (Tracing) Trace($"{Name(target)} gets a Blood Gem");
        var side = target.Side;
        if ((side.Hooks & Hook.BloodGem) == 0 || _depth >= MaxDepth)
        {
            return;
        }

        _depth++;
        try
        {
            var observers = Snapshot(side, out var count);
            for (var i = 0; i < count; i++)
            {
                var unit = observers[i];
                if ((unit.Hooks & Hook.BloodGem) == 0 || (!unit.IsOnBoard && !unit.IsExtra))
                {
                    continue;
                }

                foreach (var effect in unit.Effects)
                {
                    if ((effect.Hooks & Hook.BloodGem) != 0)
                    {
                        effect.AfterFriendlyBloodGem(this, unit, target, source);
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
