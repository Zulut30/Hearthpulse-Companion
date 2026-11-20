using Hearthpulse.Core.Battlegrounds.Effects.Ongoing;
using Hearthpulse.Core.Battlegrounds.Effects.Spells;

namespace Hearthpulse.Core.Battlegrounds.Effects.Timewarped;

/// <summary>
/// Timewarped Boar (BG34_Giant_201): "Whenever every third friendly Timewarped Boar dies, get a random Golden Beast. (N
/// left!)" Golden: "get 2 random Golden Beasts". The card matters to a fight through "whenever a card is added to your hand".
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The count is the player's: "Timewarped Boar Player Ench [DNT]" (BG34_Giant_201pe, "Tracks number of dead Boars"),
/// its TAG_SCRIPT_DATA_NUM_1 (else ATK) the Boars dead so far, for the local player; else a captured Boar's
/// TAG_SCRIPT_DATA_NUM_1 when it is 1..3 (the "(N left!)" its text shows, on the board for both players); else 3 left —
/// for the opponent an estimate (<see cref="SimInputs.ProgressLeft"/>). No log of the owner has the card: which tag holds
/// what is the pattern of the other "(N left!)" cards (Effects/player/ProgressCards).</item>
/// <item>Every living friendly Boar triggers on a Boar death that completes three (literal "whenever": two living Boars
/// give two rewards); each death steps the count once. The dying Boar has left the board and does not trigger itself;
/// a Boar death while no Boar is alive is not counted (an approximation).</item>
/// </list>
/// </remarks>
[CardEffect("BG34_Giant_201")]
internal sealed class TimewarpedBoar : CombatEffect
{
    public const string Id = "BG34_Giant_201";
    public const string DeadBoarsEnchantment = "BG34_Giant_201pe";
    public const int Every = 3;

    private const string LeftKey = "sim:TimewarpedBoarsLeft";
    private const string StartedKey = "sim:TimewarpedBoarsStarted";

    private sealed class Death
    {
        public bool Counted;
        public bool Completes;
    }

    /// <summary>Boar deaths left until the next reward at the capture (<see cref="Every"/> when nothing says otherwise).</summary>
    public static int LeftAtCapture(SimSide side)
    {
        if (side.PlayerEnchantment(DeadBoarsEnchantment) is { } dead)
        {
            var count = Math.Max(0, dead.ScriptData1 > 0 ? dead.ScriptData1 : dead.Attack);
            return Every - count % Every;
        }

        foreach (var minion in side.Minions)
        {
            if (minion.CardId == Id && minion.ScriptData1 is > 0 and <= Every)
            {
                return minion.ScriptData1;
            }
        }

        return Every;
    }

    /// <summary>True when the side shows its count (our enchantment, or a Boar's "(N left!)"); else an estimate stands in.</summary>
    public static bool Shown(SimSide side) =>
        side.PlayerEnchantment(DeadBoarsEnchantment) is not null || side.Minions.Any(m => m.CardId == Id && m.ScriptData1 is > 0 and <= Every);

    public override void AfterFriendlyDeath(Fight fight, Unit self, Unit dead)
    {
        if (!self.IsAlive || dead.CardId != Id)
        {
            return;
        }

        var death = UnitState<Death>.Of(dead);
        if (!death.Counted)
        {
            death.Counted = true;
            var side = self.Side;
            if (side.Counter(StartedKey) == 0)
            {
                side.SetCounter(StartedKey, 1);
                side.SetCounter(LeftKey, LeftAtCapture(side.Info));
            }

            var left = side.AddCounter(LeftKey, -1);
            death.Completes = left <= 0;
            if (death.Completes)
            {
                side.SetCounter(LeftKey, Every);
            }
        }

        if (death.Completes)
        {
            for (var n = self.Pick(1, 2); n > 0; n--)
            {
                fight.AddToHand(self.Side, CardToHand.AnyCard);
            }
        }
    }
}
