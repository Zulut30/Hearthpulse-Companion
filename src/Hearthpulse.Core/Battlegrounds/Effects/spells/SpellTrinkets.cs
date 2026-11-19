namespace Hearthpulse.Core.Battlegrounds.Effects.Spells;

/// <summary>
/// Lovely Locket (BG36_MagicItem_211): "After you cast a spell on a friendly minion, this casts it on another friendly
/// minion."
/// </summary>
/// <remarks>
/// After a spell on a friendly minion resolved, the same spell is cast again (a full cast: "whenever/after you cast"
/// hooks run again) on a random other living friendly minion. Assumption: the Locket does not react to its own casts
/// (otherwise every cast would chain until no target is left); unverified, the card is not in a real log yet.
/// </remarks>
[CardEffect("BG36_MagicItem_211")]
internal sealed class LovelyLocket : CombatEffect
{
    public override void AfterFriendlySpellCast(Fight fight, Unit self, string spellCardId, Unit? target)
    {
        if (!fight.IsCasting || target is null || target.Side != self.Side)
        {
            return;
        }

        var spell = fight.CurrentSpell;
        if (spell.Source == self || PickOther(fight, self, target) is not { } other)
        {
            return;
        }

        fight.CastSpell(spell with { Target = other, Source = self });
    }

    /// <summary>The random "another friendly minion".</summary>
    private static Unit? PickOther(Fight fight, Unit self, Unit target) => fight.RandomOf(fight.Alive(target.Side, except: target), self);
}

/// <summary>
/// Corrupted Baton (BG36_MagicItem_404): "After you cast a Tavern spell, give your Deity +4/+4." Greater version
/// (BG36_MagicItem_404t): "+10/+10".
/// </summary>
[CardEffect("BG36_MagicItem_404")]
[CardEffect("BG36_MagicItem_404t")]
internal sealed class CorruptedBaton : CombatEffect
{
    public override void AfterFriendlySpellCast(Fight fight, Unit self, string spellCardId, Unit? target)
    {
        if (SpellTriggers.AfterTavernSpell(fight))
        {
            var printed = self.CardId.EndsWith('t') ? 10 : 4;
            SpellTriggers.GiveDeity(fight, self.Side, SpellTriggers.TrinketShown(self, 1, printed), SpellTriggers.TrinketShown(self, 2, printed));
        }
    }
}

/// <summary>Miniature Ship (BG35_MagicItem_710): "After you cast a Tavern spell, give your Pirates +4/+4."</summary>
[CardEffect("BG35_MagicItem_710")]
internal sealed class MiniatureShip : CombatEffect
{
    public override void AfterFriendlySpellCast(Fight fight, Unit self, string spellCardId, Unit? target)
    {
        if (SpellTriggers.AfterTavernSpell(fight))
        {
            SpellTriggers.BuffAll(fight, self.Side, SpellTriggers.TrinketShown(self, 1, 4), SpellTriggers.TrinketShown(self, 2, 4), Tribe.Pirate);
        }
    }
}

/// <summary>
/// Comfy Coffin (BG30_MagicItem_547): "Whenever you cast a Tavern spell, your Undead have +1 Attack this game (wherever
/// they are)." Greater version (BG30_MagicItem_547t): "+2 Attack". Living Undead now, Undead summoned later this fight
/// when they arrive.
/// </summary>
[CardEffect("BG30_MagicItem_547")]
[CardEffect("BG30_MagicItem_547t")]
internal sealed class ComfyCoffin : CombatEffect
{
    public override void OnFriendlySpellCast(Fight fight, Unit self, in SpellCast spell)
    {
        if (spell.IsTavernSpell)
        {
            SpellTriggers.BuffThisGame(fight, self.Side, Tribe.Undead, SpellTriggers.TrinketShown(self, 1, self.CardId.EndsWith('t') ? 2 : 1), 0);
        }
    }
}

/// <summary>
/// Bluegill Flippers (BG32_MagicItem_893): "Whenever you cast a Tavern spell, give the left-most minion in your hand and
/// warband +3/+3."
/// </summary>
/// <remarks>
/// The left-most living minion of the board gets +3/+3. The hand part is left out: the left-most card in hand may be a
/// spell, which the capture does not hold, so the hand list cannot say which card it is.
/// </remarks>
[CardEffect("BG32_MagicItem_893")]
internal sealed class BluegillFlippers : CombatEffect
{
    public override void OnFriendlySpellCast(Fight fight, Unit self, in SpellCast spell)
    {
        if (!spell.IsTavernSpell)
        {
            return;
        }

        foreach (var unit in self.Side.Minions)
        {
            if (unit.IsAlive)
            {
                fight.Buff(unit, SpellTriggers.TrinketShown(self, 1, 3), SpellTriggers.TrinketShown(self, 2, 3));
                return;
            }
        }
    }
}

/// <summary>
/// Flighty Portrait (BG36_MagicItem_820): "Get a Flighty Scout. After you cast a Tavern spell, give Flighty Scouts in your
/// hand and warband +3/+3." Flighty Scouts (BG32_330) on the board and in the hand list get +3/+3.
/// </summary>
[CardEffect("BG36_MagicItem_820")]
internal sealed class FlightyPortrait : CombatEffect
{
    public const string FlightyScoutId = "BG32_330";

    public override void AfterFriendlySpellCast(Fight fight, Unit self, string spellCardId, Unit? target)
    {
        if (!SpellTriggers.AfterTavernSpell(fight))
        {
            return;
        }

        var (attack, health) = (SpellTriggers.TrinketShown(self, 1, 3), SpellTriggers.TrinketShown(self, 2, 3));
        foreach (var unit in fight.Alive(self.Side))
        {
            if (unit.CardId == FlightyScoutId)
            {
                fight.Buff(unit, attack, health);
            }
        }

        SpellTriggers.BuffHand(self.Side, attack, health, static m => m.CardId == FlightyScoutId);
    }
}
