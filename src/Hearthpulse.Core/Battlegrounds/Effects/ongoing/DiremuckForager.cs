namespace Hearthpulse.Core.Battlegrounds.Effects.Ongoing;

/// <summary>
/// Diremuck Forager (BG27_556): "Start of Combat: When you have space, summon the highest-Attack Murloc from your hand for
/// this combat only." Golden: "the two highest-Attack Murlocs".
/// <para>
/// The Start of Combat part has resolved when the boards are captured: with space the Murloc is already on the board (and no
/// longer in the hand). What is left is the "when you have space" wait: if the board was full at the start of the fight
/// (7 minions), the summon is still pending, and it happens at the end of the first death phase that leaves room, after
/// that phase's Reborn copies took theirs (<see cref="PendingMurlocs"/>; simreplay #4/11: Bubble Gunner's copy at line
/// 1086990, the Murloc only after the copy died, 1087143), even after the Forager itself died. The Murloc is summoned at
/// the right end of the board, wherever the Forager stands (real log 15.11.2026, build 255790: the delayed summon went to
/// position 7 with the Forager alive at position 4, and with a golden one at position 3; simreplay #2/11-#2/14, lines
/// 349140, 387154, 426591). Needs the hand, which only the local player's is known: the opponent's Forager stays "not
/// simulated". The Murlocs still to summon are the player enchantment's count
/// (<see cref="SimInputs.PendingForagerSummons"/>); without it, with a full board, the sim assumes none was summoned yet.
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

        /// <summary>The Forager whose summon this is: the owner of the tie-break decision (it may have died since).</summary>
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
            if (fight.Summon(side, murloc, side.Count) is null)
            {
                return;
            }

            hand.RemoveAt(best);
            state.Remaining--;
        }
    }

    private static bool IsMurloc(SimMinion minion) => (minion.Tribes & Tribe.Murloc) != 0;
}
