namespace Hearthpulse.Core.Battlegrounds.Effects.Player;

/// <summary>
/// Rapid Reanimation (BG25_HERO_103p, Teron Gorefiend): "Choose a friendly minion. Start of Combat: Destroy it. When you
/// have space, resummon an exact copy."
/// <para>
/// The destroy (and its deathrattle) resolved before the capture. The destroyed minion frees a slot, so the copy usually
/// comes back during Start of Combat too and is already on the captured board: nothing is owed. It is owed only when the
/// board is full at capture (deathrattle summons took the slot), the capture remembered the destroyed minion
/// (<see cref="SimSide.RemovedByExtras"/>, its state when it left the board) and no captured minion was created by the hero
/// power. Then the exact copy (stats and keywords it had, without enchantment effects: the capture keeps none for removed
/// minions) comes once, after the first friendly death that leaves room, at the right end.
/// </para>
/// <para>
/// Assumptions, not checked against a real log (the owner's log of 2026-11-15 has Teron as an opponent who never used the
/// power in a fight): the removal happens inside the hero power's TRIGGER block, the copy's CREATOR is the hero power, and
/// a late copy goes to the right end. Both sides: the destroyed minion stood on the board the player saw.
/// </para>
/// </summary>
[CardEffect("BG25_HERO_103p")]
internal sealed class RapidReanimation : CombatEffect
{
    public override string CapturedInput => "существо, уничтоженное силой в начале боя (SimSide.RemovedByExtras)";

    public override void OnFightStart(Fight fight, Unit self)
    {
        var info = self.Side.Info;
        // Counter: 1 = the copy is still owed.
        self.Counter = info.Minions.Count >= Side.MaxMinions && PlayerCards.Owed(info, self.CardId) is not null ? 1 : 0;
    }

    public override void AfterFriendlyDeath(Fight fight, Unit self, Unit dead)
    {
        if (self.Counter == 0 || self.Side.IsFull || PlayerCards.Owed(self.Side.Info, self.CardId) is not { } removed)
        {
            return;
        }

        self.Counter = 0;
        if (fight.Tracing) fight.Trace($"{(self.Side.IsOpponent ? "their" : "our")} Rapid Reanimation resummons {removed[0].CardId}");
        fight.Summon(self.Side, PlayerCards.CopyOf(removed[0], self.CardId), self.Side.Count);
    }
}
