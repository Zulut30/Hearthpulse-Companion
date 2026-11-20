using Hearthpulse.Core.Battlegrounds.Effects.Ongoing;

namespace Hearthpulse.Core.Battlegrounds.Effects.Secrets;

// Secrets that trigger "when one of your minions is attacked" (before damage, SecretEffect remarks).

/// <summary>
/// Venomstrike Trap (TB_Bacon_Secrets_01): "Secret: When one of your minions is attacked, summon a 2/3 Poisonous Cobra."
/// Better Venomstrike Trap (TB_Bacon_Secrets_01b): "... summon a 2/3 Poisonous Cobra and give it Reborn." The Cobra is the
/// Battlegrounds Emperor Cobra (BG_EX1_170, a 2/3 Poisonous Beast), summoned at the right end.
/// </summary>
[CardEffect("TB_Bacon_Secrets_01")]
[CardEffect("TB_Bacon_Secrets_01b")]
internal sealed class VenomstrikeTrap : SecretEffect
{
    public const string Id = "TB_Bacon_Secrets_01";
    public const string BetterId = "TB_Bacon_Secrets_01b";
    public const string CobraId = "BG_EX1_170";

    public override void OnFriendlyAttacked(Fight fight, Unit self, Unit defender, Unit attacker)
    {
        if (!Ready(self) || self.Side.IsFull)
        {
            return;
        }

        Use(fight, self, "Venomstrike Trap");
        var keywords = Keyword.Poisonous | (self.CardId == BetterId ? Keyword.Reborn : Keyword.None);
        fight.Summon(self.Side, Tokens.Make(CobraId, 2, 3, Tribe.Beast, keywords: keywords) with { CreatorCardId = self.CardId }, self.Side.Count);
    }
}

/// <summary>
/// Snake Trap (TB_Bacon_Secrets_02): "Secret: When one of your minions is attacked, summon three 1/1 Snakes." The Snakes are
/// the Battlegrounds token (BG_EX1_554t, 1/1 Beasts), at the right end; on a board with less room fewer fit.
/// </summary>
[CardEffect("TB_Bacon_Secrets_02")]
internal sealed class SnakeTrap : SecretEffect
{
    public const string Id = "TB_Bacon_Secrets_02";
    public const string SnakeId = "BG_EX1_554t";

    public override void OnFriendlyAttacked(Fight fight, Unit self, Unit defender, Unit attacker)
    {
        if (!Ready(self) || self.Side.IsFull)
        {
            return;
        }

        Use(fight, self, "Snake Trap");
        for (var i = 0; i < 3 && !self.Side.IsFull; i++)
        {
            fight.Summon(self.Side, Tokens.Make(SnakeId, 1, 1, Tribe.Beast) with { CreatorCardId = self.CardId }, self.Side.Count);
        }
    }
}

/// <summary>
/// Splitting Image (TB_Bacon_Secrets_04): "Secret: When one of your minions is attacked, summon a copy of it." Better Pack
/// Tactics (TB_Bacon_Secrets_15b): "Secret: When a friendly minion is attacked, summon a copy of it." The copy is the
/// minion as it is (stats, damage, keywords, enchantments), to its right.
/// </summary>
[CardEffect("TB_Bacon_Secrets_04")]
[CardEffect("TB_Bacon_Secrets_15b")]
internal sealed class SplittingImage : SecretEffect
{
    public const string Id = "TB_Bacon_Secrets_04";
    public const string BetterPackTacticsId = "TB_Bacon_Secrets_15b";

    public override void OnFriendlyAttacked(Fight fight, Unit self, Unit defender, Unit attacker)
    {
        if (!Ready(self) || self.Side.IsFull || !defender.IsOnBoard)
        {
            return;
        }

        Use(fight, self, self.CardId == Id ? "Splitting Image" : "Better Pack Tactics");
        fight.SummonAt(defender, CopyNow(defender, self.CardId));
    }
}

/// <summary>
/// Pack Tactics (TB_Bacon_Secrets_15): "Secret: When a friendly minion is attacked, summon a 3/3 copy." The copy keeps the
/// minion's keywords and effects with its stats set to 3/3 (its enchantment Packmate, TB_Bacon_Secrets_15e, sets them), to
/// its right; summon bonuses of the side apply on top.
/// </summary>
[CardEffect("TB_Bacon_Secrets_15")]
internal sealed class PackTactics : SecretEffect
{
    public const string Id = "TB_Bacon_Secrets_15";

    public override void OnFriendlyAttacked(Fight fight, Unit self, Unit defender, Unit attacker)
    {
        if (!Ready(self) || self.Side.IsFull || !defender.IsOnBoard)
        {
            return;
        }

        Use(fight, self, "Pack Tactics");
        fight.SummonAt(defender, CopyNow(defender, self.CardId) with { Attack = 3, Health = 3, MaxHealth = 3 });
    }
}

/// <summary>
/// Autodefense Matrix (TB_Bacon_Secrets_07): "Secret: When one of your minions is attacked, give it Divine Shield." Better
/// Autodefense Matrix (TB_Bacon_Secrets_07b): "... It takes 2 hits to break this combat." The better shield comes back once
/// after the first hit breaks it (the holder remembers the minion).
/// </summary>
/// <remarks>
/// Assumption: the Secret triggers even when the minion already has Divine Shield (the condition is only "is attacked"); the
/// "2 hits" apply to that minion's shield for the rest of the fight.
/// </remarks>
[CardEffect("TB_Bacon_Secrets_07")]
[CardEffect("TB_Bacon_Secrets_07b")]
internal sealed class AutodefenseMatrix : SecretEffect
{
    public const string Id = "TB_Bacon_Secrets_07";
    public const string BetterId = "TB_Bacon_Secrets_07b";

    public override void OnFriendlyAttacked(Fight fight, Unit self, Unit defender, Unit attacker)
    {
        if (!Ready(self) || !defender.IsOnBoard)
        {
            return;
        }

        Use(fight, self, "Autodefense Matrix");
        fight.GiveKeywords(defender, Keyword.DivineShield);
        if (self.CardId == BetterId)
        {
            UnitState<Shielded>.Of(self).Minion = defender;
        }
    }

    public override void OnFriendlyShieldLost(Fight fight, Unit self, Unit unit)
    {
        if (self.CardId != BetterId || self.EffectStates is null || UnitState<Shielded>.Of(self) is not { } state || !ReferenceEquals(state.Minion, unit))
        {
            return;
        }

        // The first hit broke it: the second one is still to come.
        state.Minion = null;
        if (unit.IsAlive)
        {
            fight.GiveKeywords(unit, Keyword.DivineShield);
        }
    }

    /// <summary>The minion whose Better Autodefense shield has one more hit to take.</summary>
    private sealed class Shielded
    {
        public Unit? Minion { get; set; }
    }
}
