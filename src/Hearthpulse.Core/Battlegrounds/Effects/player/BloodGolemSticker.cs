namespace Hearthpulse.Core.Battlegrounds.Effects.Player;

/// <summary>
/// Blood Golem Sticker (BG30_MagicItem_442): "Whenever a friendly Quilboar dies, summon a Golem with stats equal to its
/// Blood Gems."
/// <para>
/// "Its Blood Gems" is what the dying Quilboar gained from Blood Gems: the NUM_1/NUM_2 of its visible "Blood Gems"
/// enchantment (BG20_GEMe2), equal to the minion's tags 4235 / 4236 in the logs (<see cref="Unit.BloodGemAttack"/>, growing
/// with gems played in the fight).
/// Real log of 2026-11-05 (copy of D:\Hearthstone\Logs\Hearthstone_2026_11_05_12_02_48, lines 507997-510277): five
/// Quilboar died, and each time the trinket copied the dying one's 4235/4236 (27/24, 22/16, 111/96, 132/96, 44/32) and
/// summoned a Blood Golem (BG30_MagicItem_442t) with exactly those stats in its slot. (The S1 note "a plain 4/4 Quilboar
/// gave a 67/51 Golem" read the dead minion's stats after the game reset them.) Both sides: the enchantment is visible.
/// </para>
/// <para>Assumption: a Quilboar without Blood Gems (0 Health) makes no Golem; no such death is in the logs.</para>
/// </summary>
[CardEffect("BG30_MagicItem_442")]
internal sealed class BloodGolemSticker : CombatEffect
{
    public const string GolemId = "BG30_MagicItem_442t";

    /// <summary>The minion's visible "Blood Gems" enchantment: TAG_SCRIPT_DATA_NUM_1/2 = the totals (read first).</summary>
    public const string GemsEnchantmentId = "BG20_GEMe2";

    /// <summary>Power.log tag (no name in the client) with the Attack a minion gained from Blood Gems (the fallback).</summary>
    public const string GemAttackTag = "4235";

    /// <summary>Power.log tag with the Health a minion gained from Blood Gems.</summary>
    public const string GemHealthTag = "4236";

    public override void AfterFriendlyDeath(Fight fight, Unit self, Unit dead)
    {
        if (!dead.Is(Tribe.Quilboar) || dead.BloodGemHealth <= 0)
        {
            return;
        }

        fight.SummonAt(dead, Tokens.Make(GolemId, Math.Max(0, dead.BloodGemAttack), dead.BloodGemHealth, Tribe.None));
    }
}
