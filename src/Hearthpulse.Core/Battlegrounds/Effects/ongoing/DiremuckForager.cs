namespace Hearthpulse.Core.Battlegrounds.Effects.Ongoing;

/// <summary>
/// Diremuck Forager (BG27_556): "Start of Combat: When you have space, summon the highest-Attack Murloc from your hand for
/// this combat only." Golden: "the two highest-Attack Murlocs".
/// <para>
/// The Start of Combat part has resolved when the boards are captured: with space the Murloc is already on the board (and no
/// longer in the hand). What is left is the "when you have space" wait: if the board was full at the start of the fight
/// (7 minions), the summon is still pending, and it happens at the end of the first death phase that leaves room, after
/// that phase's Reborn copies took theirs (<see cref="PendingMurlocs"/>), even after the Forager itself died. The Murloc is summoned at the right end of the
/// board, also while the Forager is on it (Power.log 15.11, simreplay #2/11-#2/14: Flighty Scouts made at lines 349140,
/// 387154, 426591 stand right of every friendly minion, the living Forager further left). Needs the hand, which only the local player's is known: the opponent's Forager
/// stays "not simulated". With a golden Forager and a full board the sim assumes none of the two was summoned yet.
/// </para>
/// </summary>
[CardEffect("BG27_556")]
internal sealed class DiremuckForager : CombatEffect
{
    public override bool NeedsHand => true;

    public override void OnFightStart(Fight fight, Unit self)
    {
        if (!self.Side.IsFull)
        {
            return;
        }

        var holder = PlayerEffects.Attach(self.Side, self.CardId, PendingMurlocs.Instance);
        var state = UnitState<PendingMurlocs.State>.Of(holder);
        state.Remaining = SimInputs.PendingForagerSummons(self.Side.Info, self.Spec.EntityId) ?? self.Pick(1, 2);
        state.Source = self;
    }
}

/// <summary>The pending "when you have space" summon of a Diremuck Forager, kept as a player-level effect.</summary>
internal sealed class PendingMurlocs : CombatEffect
{
    public static readonly PendingMurlocs Instance = new();

    public sealed class State
    {
        public int Remaining;
        public Unit? Source;
    }

    public override void AfterDeathPhase(Fight fight, Unit self)
    {
        var state = UnitState<State>.Of(self);
        var side = self.Side;
        while (state.Remaining > 0 && !side.IsFull)
        {
            var hand = side.Hand;
            var best = fight.BestCard(hand, IsMurloc, m => m.Attack, state.Source ?? self);
            if (best < 0)
            {
                return;
            }

            var murloc = hand[best];
            var unit = fight.Summon(side, murloc, side.Count);
            if (unit is null)
            {
                return;
            }

            hand.RemoveAt(best);
            state.Remaining--;
        }
    }

    private static bool IsMurloc(SimMinion minion) => (minion.Tribes & Tribe.Murloc) != 0;
}
