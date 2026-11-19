namespace Hearthpulse.Core.Battlegrounds.Effects.Spells;

// Trinkets that react to the spells a player casts in combat (TASK-2026-11-19 E5), beside SpellTrinkets.cs. In combat a
// spell is cast only by another effect (Crimson Vindicator, Gatekeeper Amalgam, Seafloor Recruiter, Lovely Locket...), and
// such a cast counts as the player's (E1, real log of 2026-11-15). Card text from HearthstoneJSON enUS, build 256589. No
// kept log has one of these trinkets in a fight (2026-11-19).

/// <summary>Replica Cathedral (BG30_MagicItem_434): "Your first spell each turn casts an extra time."</summary>
/// <remarks>
/// A Battlegrounds combat is a game turn of its own (Power.log 2026-11-15: the game's TURN goes from 1 to 2 when the first
/// shop phase ends and the first combat starts), so the first spell the player casts in the fight is cast once more. The
/// extra cast goes through the engine's spell rule, where the largest extra wins (<see cref="Fight.SpellCasts"/>): with
/// Balinda Stonehearth or Temporal Tampering the first spell is still cast twice, and the Cathedral's turn is used.
/// </remarks>
[CardEffect("BG30_MagicItem_434")]
internal sealed class ReplicaCathedral : CombatEffect
{
    private const string UsedKey = "e5_replica_cathedral_used";

    public override int ExtraSpellCasts(Fight fight, Unit self, string spellCardId, Unit? target)
    {
        var side = self.Side;
        if (side.Counter(UsedKey) > 0)
        {
            return 0;
        }

        side.SetCounter(UsedKey, 1);
        return 1;
    }
}

/// <summary>
/// Honeycomb Ring (BG36_MagicItem_371): "Your Tavern spells give an extra +1/+1. After you cast a spell on a minion,
/// improve for this turn only."
/// </summary>
/// <remarks>
/// The extra +1/+1 is part of the player's Tavern spell bonus (player tags TAVERN_SPELL_ATTACK_INCREASE / _HEALTH_INCREASE,
/// <see cref="TavernSpells.Stats"/>), which the game keeps for every Tavern spell bonus of the player; this turn's
/// improvements from the shop ended with it. In the fight each spell cast on a minion raises the bonus by +1/+1 for the
/// rest of the combat (its turn). Assumption (no log): the step is the printed +1/+1 and the tags already hold the extra.
/// </remarks>
[CardEffect("BG36_MagicItem_371")]
internal sealed class HoneycombRing : CombatEffect
{
    public override void AfterFriendlySpellCast(Fight fight, Unit self, string spellCardId, Unit? target)
    {
        if (target is null)
        {
            return;
        }

        var side = self.Side;
        side.AddCounter(TavernSpells.SpellAttackTag, 1);
        side.AddCounter(TavernSpells.SpellHealthTag, 1);
    }
}

/// <summary>
/// Bloodbound Earrings (BG32_MagicItem_808): "After you cast 4 spells, this plays a Blood Gem on all your minions. (4
/// left!)" Greater version (BG32_MagicItem_808t): "After you cast 5 spells, this plays two Blood Gems on all your minions.
/// (5 left!)".
/// </summary>
/// <remarks>
/// The spells still needed are the trinket's TAG_SCRIPT_DATA_NUM_1 (the "(N left!)" its text shows) for the local player,
/// else the printed count: the opponent's trinket number is hidden, so for them it is an estimate listed with the side
/// (<see cref="SimInputs.TrinketValue"/>). Each spell cast in the fight counts one; at zero the gems are played on every
/// living friendly minion (left to right, each gem on its own: <see cref="Fight.PlayBloodGem"/>) and the count starts over.
/// </remarks>
[CardEffect("BG32_MagicItem_808")]
[CardEffect("BG32_MagicItem_808t")]
internal sealed class BloodboundEarrings : CombatEffect
{
    public override void OnFightStart(Fight fight, Unit self) => self.Counter = SpellTriggers.TrinketShown(self, 1, Printed(self));

    public override void AfterFriendlySpellCast(Fight fight, Unit self, string spellCardId, Unit? target)
    {
        if (--self.Counter > 0)
        {
            return;
        }

        self.Counter = Printed(self);
        var gems = self.CardId.EndsWith('t') ? 2 : 1;
        foreach (var unit in fight.Alive(self.Side))
        {
            Attack.BloodGems.Play(fight, self, unit, gems);
        }
    }

    private static int Printed(Unit self) => self.CardId.EndsWith('t') ? 5 : 4;
}
