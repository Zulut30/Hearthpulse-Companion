namespace Hearthpulse.Core.Battlegrounds.Effects.Secrets;

// Secrets that trigger when a friendly minion dies (after its deathrattles, before its Reborn: SecretEffect remarks).

/// <summary>
/// Avenge (TB_Bacon_Secrets_08): "Secret: When one of your minions dies, give a random friendly minion +3/+2." It needs a
/// living friendly minion to buff; without one it waits.
/// </summary>
[CardEffect("TB_Bacon_Secrets_08")]
internal sealed class AvengeSecret : SecretEffect
{
    public const string Id = "TB_Bacon_Secrets_08";

    public override void AfterFriendlyDeath(Fight fight, Unit self, Unit dead)
    {
        if (!Ready(self) || fight.RandomOf(fight.Alive(self.Side), self) is not { } target)
        {
            return;
        }

        Use(fight, self, "Avenge");
        fight.Buff(target, 3, 2);
    }
}

/// <summary>
/// Redemption (TB_Bacon_Secrets_10): "Secret: When a friendly minion dies, return it to life with 1 Health." A fresh copy
/// of its card (printed Attack and keywords) with 1 Health, in its slot. Better Redemption (TB_Bacon_Secrets_10b): "...
/// return it to life with full Health and enchantments": the minion as it died (Attack, maximum Health, keywords and
/// enchantments of the card) at full Health.
/// </summary>
[CardEffect("TB_Bacon_Secrets_10")]
[CardEffect("TB_Bacon_Secrets_10b")]
internal sealed class Redemption : SecretEffect
{
    public const string Id = "TB_Bacon_Secrets_10";
    public const string BetterId = "TB_Bacon_Secrets_10b";

    public override void AfterFriendlyDeath(Fight fight, Unit self, Unit dead)
    {
        if (!Ready(self) || self.Side.IsFull)
        {
            return;
        }

        Use(fight, self, self.CardId == BetterId ? "Better Redemption" : "Redemption");
        fight.SummonAt(dead, self.CardId == BetterId ? WithEnchantments(dead, self.CardId) : FreshCopy(dead, self.CardId, health: 1));
    }

    /// <summary>The dead minion with its stats and its card's keywords (Divine Shield or Reborn it used come back), at full Health.</summary>
    private static SimMinion WithEnchantments(Unit dead, string creator)
    {
        var copy = CopyNow(dead, creator);
        return copy with
        {
            Health = copy.MaxHealth,
            DivineShield = dead.Spec.DivineShield,
            Reborn = dead.Spec.Reborn,
            Venomous = dead.Spec.Venomous,
            Taunt = dead.Spec.Taunt,
        };
    }
}

/// <summary>
/// Hand of Salvation (TB_Bacon_Secrets_11): "Secret: When your second minion dies in a turn, return it to life." Combat is
/// one turn: the deaths of Start of Combat (before the capture, <see cref="SimSide.DeathsBeforeCapture"/>) count, so the
/// second friendly death of the combat comes back as a fresh copy of its card at full Health, in its slot.
/// </summary>
[CardEffect("TB_Bacon_Secrets_11")]
internal sealed class HandOfSalvation : SecretEffect
{
    public const string Id = "TB_Bacon_Secrets_11";

    public override void OnFightStart(Fight fight, Unit self) => self.Counter2 = self.Side.Info.DeathsBeforeCapture.Count;

    public override void AfterFriendlyDeath(Fight fight, Unit self, Unit dead)
    {
        if (!Ready(self) || ++self.Counter2 != 2 || self.Side.IsFull)
        {
            return;
        }

        Use(fight, self, "Hand of Salvation");
        fight.SummonAt(dead, FreshCopy(dead, self.CardId));
    }
}

/// <summary>
/// Effigy (TB_Bacon_Secrets_05): "Secret: When a friendly minion dies, summon a random minion with the same Cost." In
/// Battlegrounds every minion costs the same 3 Gold and the card data gives pool minions Cost 0, so "the same Cost" is read as
/// the same Tavern tier (tokens: tier 1): a random minion of the lobby's pool of that tier (<see cref="MinionPool"/>), in the
/// dead minion's slot. Assumption, not checked against a log.
/// </summary>
[CardEffect("TB_Bacon_Secrets_05")]
internal sealed class Effigy : SecretEffect
{
    public const string Id = "TB_Bacon_Secrets_05";

    public override void AfterFriendlyDeath(Fight fight, Unit self, Unit dead)
    {
        if (!Ready(self) || self.Side.IsFull || MinionPool.Draw(fight, self, Math.Max(1, dead.Spec.TechLevel)) is not { } minion)
        {
            return;
        }

        Use(fight, self, "Effigy");
        fight.SummonAt(dead, minion with { CreatorCardId = self.CardId });
    }
}

/// <summary>
/// Reckoning (TB_Bacon_Secrets_14): "Secret: After an enemy minion deals 3 or more damage, destroy it." In combat an enemy
/// minion deals damage to a friendly minion (Divine Shield absorbs it: no damage dealt); the whole amount counts, not the
/// Health it took away.
/// </summary>
[CardEffect("TB_Bacon_Secrets_14")]
internal sealed class Reckoning : SecretEffect
{
    public const string Id = "TB_Bacon_Secrets_14";

    public override void AfterFriendlyDamaged(Fight fight, Unit self, Unit damaged, Unit? source, int amount)
    {
        if (!Ready(self) || amount < 3 || source is null || source.IsExtra || source.Side == self.Side || !source.IsOnBoard || source.IsDying)
        {
            return;
        }

        Use(fight, self, "Reckoning");
        fight.Destroy(source, self);
    }
}

/// <summary>
/// Ice Block (TB_Bacon_Secrets_12): "Secret: When your hero takes fatal damage, prevent it and become Immune this turn."
/// Eleventh Hour (BG27_Anomaly_575t, the anomaly's Secret): "When your hero takes fatal damage in combat, prevent it and gain
/// 11 Gold next turn instead." Both act only on the hero damage after the fight, which <see cref="DamageRules.Taken"/> applies
/// (<see cref="DamageRules.HasIceBlock"/>): nothing happens on the board, so the effect has no hooks. One prevention covers the
/// hit however many such Secrets the side has: the rule is a yes or no, never counted twice.
/// </summary>
[CardEffect("TB_Bacon_Secrets_12")]
[CardEffect("BG27_Anomaly_575t")]
internal sealed class IceBlock : SecretEffect
{
    public const string Id = "TB_Bacon_Secrets_12";
    public const string EleventhHourId = "BG27_Anomaly_575t";
}
