using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;
using Hearthpulse.Core.Battlegrounds.Effects.Attack;
using Hearthpulse.Core.Battlegrounds.Effects.Ongoing;

namespace Hearthpulse.Core.Tracking;

/// <summary>
/// Inputs a side's effects read that the live capture does not hold, each with an explicit estimate instead of a silent
/// default (<see cref="SimSide.Estimates"/>). The opponent's player-level values are hidden from the player during the
/// game (docs/adr/0005-hidden-information.md): even when the log has them they are not used live; an estimate rests on
/// what the player can see (the opponent's board and player-level cards) and our own values as a prior. An estimate is
/// listed only when the side has a card that reads the value. Lobby-wide facts (anomaly, Deity identity, tribes) and the
/// Start of Combat deaths both players saw are exact.
/// </summary>
internal static class SimEstimates
{
    private const string Ghastcoiler = "BGS_008";
    private const string CrimsonVindicatorId = "BG36_241";

    // Cards whose text shows the player's Beetle ("{0}/{1} Beetle") and Beetle Band, which summons the player's Beetle.
    private static readonly string[] BeetleSummoners = ["BG31_803", "BG31_801", "BG31_809", "BG36_209"];
    private static readonly string[] BeetleBand = ["BG32_MagicItem_860", "BG32_MagicItem_860t"];

    /// <summary>
    /// The estimates of <paramref name="side"/>. Values an effect reads through <see cref="Side.Counter"/> are also written
    /// into <paramref name="tags"/> (the side's <see cref="SimSide.Tags"/>, empty for the opponent). <paramref name="local"/>
    /// is our own side's player values, given when <paramref name="side"/> is the opponent's.
    /// </summary>
    public static IReadOnlyList<SimEstimate> For(SimSide side, bool isOpponent, Dictionary<string, int> tags, SimSide? local)
    {
        var estimates = new List<SimEstimate>();
        if (side.LobbyTribes == Tribe.None && side.Minions.Any(m => m.CardId == Ghastcoiler))
        {
            estimates.Add(new SimEstimate(SimInputs.LobbyTribes, 0, 0, "lobby tribes not read from the client: every tribe of the pool"));
        }

        if (!isOpponent)
        {
            // Our own values are on our screen and our player entity carries every one (a tag never set is 0).
            return estimates;
        }

        DeityStats(side, tags, local, estimates);
        if (side.Minions.Any(m => (m.Tribes & Tribe.Quilboar) != 0))
        {
            estimates.Add(OurValueAsPrior(SimInputs.BloodGemBonus, BloodGems.AttackTag, BloodGems.HealthTag, tags, local, "the printed +1/+1"));
        }

        if (side.Minions.Any(m => m.CardId == CrimsonVindicatorId))
        {
            estimates.Add(OurValueAsPrior(SimInputs.TavernSpellBonus, CrimsonVindicator.SpellAttackTag, CrimsonVindicator.SpellHealthTag, tags, local,
                                          "the printed numbers"));
        }

        BeetleStats(side, local, estimates);
        return estimates;
    }

    /// <summary>A hidden player bonus of the opponent: our own bonus as the prior (0 = the printed card), written into the tags.</summary>
    private static SimEstimate OurValueAsPrior(string input, string attackTag, string healthTag, Dictionary<string, int> tags, SimSide? local, string printed)
    {
        var attack = Math.Max(0, local?.Tags.GetValueOrDefault(attackTag) ?? 0);
        var health = Math.Max(0, local?.Tags.GetValueOrDefault(healthTag) ?? 0);
        tags[attackTag] = attack;
        tags[healthTag] = health;
        var basis = attack > 0 || health > 0 ? "hidden from the player: our own value" : "hidden from the player: " + printed;
        return new SimEstimate(input, attack, health, basis) { Tags = [attackTag, healthTag] };
    }

    /// <summary>
    /// The opponent's sleeping Deity, whose stats the player cannot see, when it can awaken (an Aberration on the board):
    /// our own Deity's stats as the prior, else the printed 1/1. Which Deity it is (lobby-wide) and how many Aberrations
    /// died in Start of Combat (on screen) are exact.
    /// </summary>
    private static void DeityStats(SimSide side, Dictionary<string, int> tags, SimSide? local, List<SimEstimate> estimates)
    {
        if (!side.Extras.Any(id => id == Tokens.CThunId || id == Tokens.YShaarjId) || !side.Minions.Any(m => (m.Tribes & Tribe.Aberration) != 0))
        {
            return;
        }

        var (attack, health, basis) = local?.Tags.GetValueOrDefault(Deity.AttackTag) is > 0 and var ourAttack
            ? (ourAttack, Math.Max(1, local.Tags.GetValueOrDefault(Deity.HealthTag)), "hidden from the player: our own Deity's stats")
            : (1, 1, "hidden from the player: the printed 1/1");
        tags[Deity.AttackTag] = attack;
        tags[Deity.HealthTag] = health;
        estimates.Add(new SimEstimate(SimInputs.DeityStats, attack, health, basis) { Tags = [Deity.AttackTag, Deity.HealthTag, "4914", "4915"] });
    }

    /// <summary>
    /// The opponent's Beetle when nothing on their board shows it (no summoner with its script data): their Beetle bonus
    /// is a player enchantment the player cannot see. Our own Beetle as the prior, else the printed 2/2.
    /// </summary>
    private static void BeetleStats(SimSide side, SimSide? local, List<SimEstimate> estimates)
    {
        var summoners = side.Minions.Where(m => BeetleSummoners.Contains(m.CardId)).ToList();
        if ((summoners.Count == 0 && !side.Extras.Any(BeetleBand.Contains)) || summoners.Exists(m => m.ScriptData1 > 0 || m.ScriptData2 > 0))
        {
            return;
        }

        var (attack, health, basis) = local is not null && SimInputs.BeetleStatsOf(local) is { } ours
            ? (ours.Attack, ours.Health, "our own Beetle")
            : (2, 2, "the printed 2/2");
        estimates.Add(new SimEstimate(SimInputs.BeetleStats, attack, health, basis));
    }
}
