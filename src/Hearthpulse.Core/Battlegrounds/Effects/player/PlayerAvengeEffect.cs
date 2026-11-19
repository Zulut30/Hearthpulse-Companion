namespace Hearthpulse.Core.Battlegrounds.Effects.Player;

/// <summary>
/// Avenge (X) of a player-level card (hero power, trinket), with X and the progress from the card's own tags at capture:
/// SCORE_VALUE_1 = X and SCORE_VALUE_2 = deaths counted so far. Real logs (2026-11-15, Relentless Deflector, Avenge (3)):
/// SCORE_VALUE_1 is 3 and SCORE_VALUE_2 counts 1, 2, 3, then returns to 0 when Avenge fires. That is a minion; for hero
/// powers and trinkets the same tags are assumed. Only our own cards are captured: the opponent's count from 0 with the
/// X of the card text (HearthstoneJSON latest), as before.
/// </summary>
internal abstract class PlayerAvengeEffect : AvengeEffect
{
    public const string ThresholdTag = "SCORE_VALUE_1";
    public const string ProgressTag = "SCORE_VALUE_2";

    /// <summary>The X of the card text (HearthstoneJSON latest), used when the capture has none.</summary>
    protected abstract int PrintedThreshold(Unit self);

    protected sealed override int Threshold(Unit self) =>
        self.Side.Info.ExtraTag(self.CardId, ThresholdTag) is >= 1 and <= 20 and var captured ? captured : PrintedThreshold(self);

    /// <summary>Starts the count at the captured progress (deaths at Start of Combat and whatever the game carried over).</summary>
    public override void OnFightStart(Fight fight, Unit self) =>
        self.AvengeCount = Math.Clamp(self.Side.Info.ExtraTag(self.CardId, ProgressTag) ?? 0, 0, Threshold(self) - 1);
}
