using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Hearthpulse.Core.Battlegrounds.Effects;

namespace Hearthpulse.Core.Battlegrounds;

/// <summary>
/// Values effects read from the capture besides the boards, exact when the log shows them to the player and estimated
/// (<see cref="SimSide.Estimates"/>) when it does not. Every reader returns null when the capture has no value (a capture
/// saved before the value was recorded), so the effect keeps its own fallback.
/// </summary>
public static class SimInputs
{
    /// <summary>The Deity's stats (player tags BACON_OLD_GOD_ATTACK/HEALTH).</summary>
    public const string DeityStats = "deity_stats";

    /// <summary>A Beetle's stats before in-fight bonuses (2/2 plus the player's Beetle bonus).</summary>
    public const string BeetleStats = "beetle_stats";

    /// <summary>What one Blood Gem adds on top of its printed +1/+1 (player tags BACON_BLOODGEMBUFF*VALUE).</summary>
    public const string BloodGemBonus = "blood_gem_bonus";

    /// <summary>What a tavern spell adds on top of its printed numbers (player tags TAVERN_SPELL_*_INCREASE).</summary>
    public const string TavernSpellBonus = "tavern_spell_bonus";

    /// <summary>A number on the opponent's trinket (its TAG_SCRIPT_DATA_NUM_1/2), which is not captured for them.</summary>
    public const string TrinketValue = "trinket_value";

    /// <summary>The lobby's tribes (random summons draw from them).</summary>
    public const string LobbyTribes = "lobby_tribes";

    /// <summary>Duos: the opponent team's second board, as last seen (or not seen yet: the fight then has none).</summary>
    public const string DuosOpponentTeammate = "duos_opponent_teammate";

    /// <summary>Duos: our teammate's board, when it was not on the log at capture: as last seen (or none).</summary>
    public const string DuosPartnerBoard = "duos_partner_board";

    /// <summary>The crew of the opponent's Timewarped Nellie's Ship: a Discover pick the player does not see.</summary>
    public const string ShipCrew = "ship_crew";

    /// <summary>
    /// The opponent's Secrets (The Great Akazamzarak, the Eleventh Hour anomaly): the player sees how many there are, not
    /// which. <see cref="SimEstimate.Value1"/> = how many; <see cref="SimEstimate.Value2"/> = 1 when the only Secret the
    /// opponent can hold is Eleventh Hour (the anomaly's, and no Akazamzarak), so their fatal hero damage is prevented
    /// (<see cref="DamageRules.HasIceBlock"/>); otherwise their Secrets are left out of the fight. After the game the Secrets
    /// the log showed replace the estimate (<see cref="SimSide.HiddenSecrets"/>).
    /// </summary>
    public const string OpponentSecrets = "opponent_secrets";
    /// What is left of an "after N …, get a random X" count (Lucky Tabby, MechGyver, Timewarped Boar…) of the opponent's,
    /// hidden from the player: N left, as if no progress was made. Basis "&lt;card id&gt;: …".
    /// </summary>
    public const string ProgressLeft = "progress_left";

    /// <summary>Whether a "next combat" hero power (I'll Take That!) was used this turn: not in the capture, assumed used.</summary>
    public const string HeroPowerUsed = "hero_power_used";

    /// <summary>
    /// "A random opponent's warband" (Timewarped Magnanimoose): the fight knows only this fight's opponent, whose board at
    /// the start of combat stands in for the lobby's.
    /// </summary>
    public const string OpponentWarband = "opponent_warband";

    /// <summary>Timewarped Magnanimoose, which summons a minion from a random opponent's warband.</summary>
    public const string Magnanimoose = "BG34_Giant_619";

    /// <summary>Timewarped Nellie's Ship, whose crew the tracker records for the local player (<see cref="SimMinion.Crew"/>).</summary>
    public const string NelliesShip = "BG34_Giant_074t";

    /// <summary>
    /// "Secret Deity [DNT]": the objective in each player's SECRET zone that counts Aberration deaths for the lobby's Deity
    /// (QUEST_PROGRESS / QUEST_PROGRESS_TOTAL, TAG_SCRIPT_DATA_NUM_1 = deaths still needed, BACON_DEITY_SIGIL = 1 marks it;
    /// Power.log 2026-11-15 L746400, the opponent's copy revealed at the combat's start).
    /// </summary>
    public const string DeityObjective = "BG_OldGod";

    /// <summary>A quest's progress so far (its QUEST_PROGRESS tag).</summary>
    public const string QuestProgress = "QUEST_PROGRESS";

    /// <summary>A quest's target (QUEST_PROGRESS_TOTAL).</summary>
    public const string QuestTotal = "QUEST_PROGRESS_TOTAL";

    /// <summary>The dbfId of a quest's reward card (QUEST_REWARD_DATABASE_ID; Power.log 2026-11-15 L500730).</summary>
    public const string QuestReward = "QUEST_REWARD_DATABASE_ID";

    /// <summary>
    /// The tags kept of the local player's quests and lasting Tavern spells in the SECRET zone (<see cref="SimSide.ExtraTags"/>):
    /// their script numbers (Boon of Beetles: the Beetle's stats and how many are left) and a quest's task and reward. Kept in
    /// the local capture file; not part of the pool's card values (<see cref="CombatEffect.CapturedTags"/>).
    /// </summary>
    public static IReadOnlyList<string> ObjectiveTags { get; } =
        ["TAG_SCRIPT_DATA_NUM_1", "TAG_SCRIPT_DATA_NUM_2", "TAG_SCRIPT_DATA_NUM_3", QuestProgress, QuestTotal, QuestReward];

    /// <summary>"Unplayable" (BG26_350e2): a hand card summoned "for this combat only" by Start of Combat.</summary>
    public const string UnplayableEnchantment = "BG26_350e2";

    /// <summary>Diremuck Forager's player enchantment: TAG_SCRIPT_DATA_NUM_1 = summons still pending.</summary>
    public const string DiremuckEnchantment = "BG27_556pe";

    /// <summary>"Beetle Army Player Enchant": TAG_SCRIPT_DATA_NUM_1/2 = the Beetle bonus over 2/2.</summary>
    public const string BeetleArmyEnchantment = "BG31_808pe";

    /// <summary>
    /// "Undead Bonus Attack Player Enchant [DNT]" (BG25_011pe, "Give Attack to Undead."): the player's "Your Undead have +X
    /// Attack this game" (Nerubian Deathswarmer, Anub'arak, Butchering, Champion of the Primus...), X in its ATK tag or
    /// TAG_SCRIPT_DATA_NUM_1 (no log has it yet: either is read).
    /// </summary>
    public const string UndeadArmyPlayerEnchantment = "BG25_011pe";

    /// <summary>"Undead Army" (BG25_011e2, "+0/+0."): the same bonus as the enchantment each Undead minion carries.</summary>
    public const string UndeadArmyEnchantment = "BG25_011e2";

    /// <summary>The printed Beetle.</summary>
    private const int BeetleBase = 2;

    /// <summary>
    /// The player's "Your Undead have +X Attack this game" at capture, which Undead summoned in the fight get as well (a
    /// captured Undead already shows it in its Attack): the local player's enchantment <see cref="UndeadArmyPlayerEnchantment"/>,
    /// else the largest "Undead Army" enchantment on the side's Undead (<see cref="UndeadArmyEnchantment"/>, on the board, so
    /// visible for the opponent too), else 0 (no Undead with the bonus on the board).
    /// </summary>
    public static int UndeadAttackBonus(SimSide side)
    {
        if (side.PlayerEnchantment(UndeadArmyPlayerEnchantment) is { } player && Number(player) > 0)
        {
            return Number(player);
        }

        var best = 0;
        foreach (var minion in side.Minions)
        {
            foreach (var enchantment in minion.EnchantmentValues)
            {
                if (enchantment.CardId == UndeadArmyEnchantment)
                {
                    best = Math.Max(best, Number(enchantment));
                }
            }
        }

        return best;

        static int Number(SimEnchantment e) => Math.Max(0, e.Attack > 0 ? e.Attack : e.ScriptData1);
    }

    private static readonly ConditionalWeakTable<IReadOnlyList<SimMinion>, ConcurrentDictionary<Tribe, IReadOnlyList<SimMinion>>> LobbyPools = new();

    /// <summary>
    /// The player tags effects read (<see cref="Side.Counter"/>) that the client does not show for the opponent: Blood Gem
    /// and tavern spell values, the Deity's stats (by name and by the number the log prints without one). During the game
    /// the opponent's never reach <see cref="SimSide.Tags"/>; they are kept in <see cref="SimSide.HiddenTags"/> for after it.
    /// The exception is the Deity's stats while the client draws the opponent's revealed Deity with them (the tracker's
    /// DeityStatsShown, docs/bg-combat-sim.md "Входы симулятора").
    /// </summary>
    public static IReadOnlyCollection<string> HiddenPlayerTags { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "BACON_BLOODGEMBUFFATKVALUE", "BACON_BLOODGEMBUFFHEALTHVALUE",
        "TAVERN_SPELL_ATTACK_INCREASE", "TAVERN_SPELL_HEALTH_INCREASE",
        "BACON_OLD_GOD_ATTACK", "BACON_OLD_GOD_HEALTH", "4914", "4915",
    };

    /// <summary>
    /// <paramref name="side"/> with the values of <see cref="SimSide.HiddenTags"/> in place of the estimates that stood in
    /// for them: for consumers after the game only (<see cref="SimOptions.PostGame"/>). An estimate stays when the log had no
    /// value for it either.
    /// </summary>
    public static SimSide PostGame(SimSide side)
    {
        if (side.HiddenSecrets.Count > 0)
        {
            // The opponent's Secrets as the log showed them by the end of the game: exact, in place of the estimate.
            side = side with
            {
                Secrets = side.HiddenSecrets,
                Estimates = side.Estimates.Where(e => e.Input != OpponentSecrets).ToArray(),
            };
        }

        if (side.HiddenTags.Count == 0)
        {
            return side;
        }

        var hidden = side.HiddenTags;
        bool Known(string tag) => hidden.TryGetValue(tag, out var value) && value > 0;
        var tags = new Dictionary<string, int>(side.Tags, StringComparer.Ordinal);
        var kept = new List<SimEstimate>();
        foreach (var estimate in side.Estimates)
        {
            if (estimate.Tags.Count > 0 && estimate.Tags.Any(Known))
            {
                // The log has the value: the estimate and everything it wrote go.
                foreach (var tag in estimate.Tags)
                {
                    tags.Remove(tag);
                }
            }
            else
            {
                kept.Add(estimate);
            }
        }

        // A value the log had where no estimate still stands in for it.
        foreach (var (tag, value) in hidden)
        {
            tags.TryAdd(tag, value);
        }

        return side with { Tags = tags, Estimates = kept };
    }

    /// <summary>The estimate of <paramref name="input"/> on <paramref name="side"/>, or null when it was read (or not needed).</summary>
    public static SimEstimate? Estimate(SimSide side, string input)
    {
        foreach (var estimate in side.Estimates)
        {
            if (estimate.Input == input)
            {
                return estimate;
            }
        }

        return null;
    }

    /// <summary>
    /// Murlocs Diremuck Forager <paramref name="foragerEntityId"/> still has to summon "when you have space": its player
    /// enchantment's TAG_SCRIPT_DATA_NUM_1 (G1 L348841: 1 while the board is full, 0 once summoned). Null when not captured.
    /// </summary>
    public static int? PendingForagerSummons(SimSide side, int foragerEntityId) =>
        foragerEntityId > 0 && side.PlayerEnchantment(DiremuckEnchantment, foragerEntityId) is { } pending ? Math.Max(0, pending.ScriptData1) : null;

    /// <summary>
    /// The player's Beetle before in-fight bonuses: 2/2 plus the Beetle Army enchantment's NUM_1/NUM_2 (G4 L1389006: 75/73
    /// with Beetles 77/75), else the estimate; null when neither is in the capture.
    /// </summary>
    public static (int Attack, int Health)? BeetleStatsOf(SimSide side) =>
        side.PlayerEnchantment(BeetleArmyEnchantment) is { } army
            ? (BeetleBase + Math.Max(0, army.ScriptData1), BeetleBase + Math.Max(0, army.ScriptData2))
            : Estimate(side, BeetleStats) is { } estimate ? (estimate.Value1, Math.Max(1, estimate.Value2)) : null;

    /// <summary>
    /// Whether a space-copy hero power (Stormpike Strength, Frostwolf Fervor) still owes this combat's copy: its
    /// TAG_SCRIPT_DATA_NUM_1, 1 until the copy is summoned and 0 after (G1 L205932, reset to 1 after the combat at
    /// L211324). Null when not captured (the opponent's, or an older capture).
    /// </summary>
    public static bool? SpaceCopyOwed(SimSide side, string heroPowerCardId) =>
        side.ExtraTag(heroPowerCardId, "TAG_SCRIPT_DATA_NUM_1") is { } flag ? flag > 0 : null;

    /// <summary>
    /// Friendly Aberration deaths still needed to awaken the Deity <paramref name="deityCardId"/> in this fight: from the
    /// side's objective when the capture has it (<see cref="SimSide.DeityAwakening"/>: its target and what it counted,
    /// Start of Combat included), else <paramref name="deathsToAwaken"/> less the Aberrations of this side that died during
    /// Start of Combat. <see cref="int.MaxValue"/> when it awakened before the capture (once per combat).
    /// </summary>
    public static int DeityDeathsStillNeeded(SimSide side, string deityCardId, int deathsToAwaken)
    {
        if (side.DeityAwakening is { Total: > 0 } awakening)
        {
            return DeityAwake(side, deityCardId) ? int.MaxValue : Math.Max(1, awakening.Total - Math.Max(0, awakening.Progress));
        }

        var died = 0;
        foreach (var dead in side.DeathsBeforeCapture)
        {
            if (dead.CardId == deityCardId)
            {
                return int.MaxValue;
            }

            if ((dead.Tribes & Tribe.Aberration) != 0 && !IsDeity(dead.CardId))
            {
                died++;
            }
        }

        foreach (var minion in side.Minions)
        {
            if (minion.CardId == deityCardId)
            {
                return int.MaxValue;
            }
        }

        // Four died and it is not on the board: the board was full; the next death tries again.
        return Math.Max(1, deathsToAwaken - died);
    }

    /// <summary>
    /// The minions of <paramref name="pool"/> a random summon can give in a lobby of <paramref name="lobby"/>: those with a
    /// lobby tribe and those without a tribe (BG rule: random minions come from the game's pool). The whole pool when the
    /// tribes are unknown. Cached per pool and lobby: no allocation per draw.
    /// </summary>
    public static IReadOnlyList<SimMinion> InLobby(IReadOnlyList<SimMinion> pool, Tribe lobby)
    {
        if (lobby == Tribe.None || (lobby & Tribe.All) == Tribe.All)
        {
            return pool;
        }

        return LobbyPools.GetValue(pool, _ => new ConcurrentDictionary<Tribe, IReadOnlyList<SimMinion>>())
            .GetOrAdd(lobby, static (tribes, source) => source.Where(m => m.Tribes == Tribe.None || (m.Tribes & tribes) != 0).ToArray(), pool);
    }

    private static bool IsDeity(string cardId) => cardId == Tokens.CThunId || cardId == Tokens.YShaarjId;

    /// <summary>The Deity <paramref name="deityCardId"/> is on the board or died during Start of Combat: it awoke already.</summary>
    private static bool DeityAwake(SimSide side, string deityCardId)
    {
        // Loops, not LINQ: read on every Aberration death of a simulated fight.
        foreach (var minion in side.Minions)
        {
            if (minion.CardId == deityCardId)
            {
                return true;
            }
        }

        foreach (var dead in side.DeathsBeforeCapture)
        {
            if (dead.CardId == deityCardId)
            {
                return true;
            }
        }

        return false;
    }
}
