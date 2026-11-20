namespace Hearthpulse.Tests.Sim;

/// <summary>
/// Why a card the matrix calls no_effect or resolved does not change a fight although its text shows a combat sign
/// (<see cref="RelevanceAudit"/>), checked against its text (HearthstoneJSON enUS build 256589) and the Battlegrounds rules:
/// what a fight after the capture at the first attack does with it. <see cref="KnownGaps"/> are the cards that do change a
/// fight and are not simulated yet, each with its owner; <see cref="Via"/> names the card whose registered effect carries
/// the combat part (a granted enchantment, a summoned token).
/// </summary>
internal static class RelevanceAuditReasons
{
    private const string Tavern = "срабатывает в таверне (розыгрыш, покупка, продажа, Refresh, трата Gold, конец/начало хода): в бою не действует";
    private const string TavernBuff = "бафф существам в таверне / на будущие покупки: на доске боя ничего не меняет";
    private const string TavernResource = "даёт ресурс таверны (Gold, Refresh, Fodder, скидку, Triple Reward): на доску боя не влияет";
    private const string HeroDamage = "урон своему герою в BG бывает только в таверне или после боя (урон по итогам); в бою эффекты по своему герою не бьют";
    private const string AfterCombat = "срабатывает после боя или на следующем ходу (исход боя, прошлый бой, другой герой умер): этот бой не меняет";
    private const string KeepsStats = "сохраняет полученное в бою после боя: внутри боя ничего не меняет";
    private const string InHand = "работает, пока карта в руке, от розыгрыша в таверне";
    private const string Teammate = "Duos: карта или ресурс напарнику в таверну; доска боя не меняется";
    private const string NotThisCombat = "улучшение для следующих боёв: в этом бою Start of Combat уже разрешился, снимок его показывает";
    private const string GoldenTavern = "«Get»: карта в руку из таверны или по итогам: в бою карта не добавляется";
    private const string Ballistics = "заклинание таверны: даёт Battlecruiser зачарование Advanced Ballistics (BG31_HERO_801ptde) — его строка в «Боевых зачарованиях» (эффект читает число зачарования из снимка, формат 2)";
    private const string Caduceus = "заклинание таверны: даёт Battlecruiser зачарование Caduceus Reactor (BG31_HERO_801ptee) — его строка в «Боевых зачарованиях» (эффект читает число зачарования из снимка, формат 2)";
    private const string ProgressReplace = "прогресс на всю игру, награда — замена тринкета или ресурс таверны: в бою не действует";

    /// <summary>Card id → why the combat sign does not change a fight.</summary>
    internal static readonly Dictionary<string, string> ByCard = new(StringComparer.Ordinal)
    {
        // ---- Minions of the pool (and Timewarped).
        ["BGS_004"] = Tavern + "; урон герою — в таверне",
        ["BG26_174"] = HeroDamage,
        ["BG31_816"] = "«When you sell this»: продажа в таверне",
        ["BG34_856"] = "Deathrattle баффает существ в таверне после её обновления: " + TavernBuff,
        ["BG36_730"] = "Deathrattle добавляет Fodder в будущие Refresh: " + TavernResource,
        ["BG26_525"] = HeroDamage + "; Battlecry",
        ["BG26_137"] = InHand,
        ["BG36_362"] = "Deathrattle баффает существ в таверне: " + TavernBuff,
        ["BG32_873"] = HeroDamage + "; бафф существам в таверне",
        ["BG24_018"] = "цена продажи после проигранного боя: " + AfterCombat,
        ["BG29_813"] = KeepsStats,
        ["BG36_364"] = NotThisCombat,
        ["BG26_523"] = HeroDamage,
        ["BG31_035"] = Tavern,
        ["BG36_733"] = HeroDamage,
        ["BG27_016"] = "Deathrattle баффает существ в таверне: " + TavernBuff,
        ["BG34_Giant_588"] = "вне пула; Battlecry и Deathrattle дают Pointy Arrow — фидер «card added to hand» (E6a)",
        ["BG34_Giant_082"] = "Avenge увеличивает максимум Gold: " + TavernResource,
        ["BG34_Giant_300"] = HeroDamage,
        ["BG34_Giant_078"] = "вне пула; Deathrattle даёт Blood Gem Barrage — фидер «card added to hand» (E6a)",
        ["BG34_Giant_039"] = AfterCombat,
        ["BG34_Giant_596"] = HeroDamage,
        ["BG34_Giant_801"] = InHand,
        ["BG34_Giant_317"] = "вне пула; передаёт статы картам в руке: в бою рука не участвует, кроме призыва из руки (E6a)",
        ["BG34_Giant_006"] = Tavern,
        ["BG34_PreMadeChamp_067"] = "Deathrattle увеличивает максимум Gold: " + TavernResource,
        ["BG34_Giant_314"] = KeepsStats,
        ["BG34_Giant_326"] = "вне пула; Deathrattle даёт Eyes of the Earth Mother — фидер «card added to hand» (E6a)",

        // ---- Buddies (Bring in the Buddies) and other cards hsbg.cards puts in today's game.
        ["BG22_HERO_200_Buddy"] = Tavern,
        ["BG24_HERO_204_Buddy"] = Tavern,
        ["BG26_HERO_102_Buddy"] = "от силы героя в таверне",
        ["BG36_360t3"] = Tavern,
        ["TB_BaconShop_HERO_01_Buddy"] = Tavern,
        ["TB_BaconShop_HERO_15_Buddy"] = "от силы героя в таверне",
        ["TB_BaconShop_HERO_25_Buddy"] = HeroDamage,
        ["BG36_MagicItem_417t"] = "«outside combat this turn»: только в таверне",
        ["BG31_HERO_801ptd"] = Ballistics,
        ["BG31_HERO_801ptd2"] = Ballistics,
        ["BG31_HERO_801ptd3"] = Ballistics,
        ["BG31_HERO_801ptd4"] = Ballistics,
        ["BG31_HERO_801ptd5"] = Ballistics,
        ["BG31_HERO_801ptd6"] = Ballistics,
        ["BG31_HERO_801ptd7"] = Ballistics,
        ["BG31_HERO_801pte"] = Caduceus,
        ["BG31_HERO_801pte2"] = Caduceus,
        ["BG31_HERO_801pte3"] = Caduceus,
        ["BG31_HERO_801pte4"] = Caduceus,
        ["BG31_HERO_801pte5"] = Caduceus,
        ["BG31_HERO_801pte6"] = Caduceus,
        ["BG31_HERO_801pte7"] = Caduceus,
        ["BG31_HERO_801ptj"] = "в таверне даёт Battlecruiser Reborn и зачарование Ultra-Capacitor (BG31_HERO_801ptje), его эффект UltraCapacitor",
        ["BG31_HERO_801ptj2"] = "в таверне даёт Battlecruiser Reborn и зачарование Ultra-Capacitor (BG31_HERO_801ptje), его эффект UltraCapacitor",

        // ---- Duos-only.
        ["BGDUO_117"] = "«After this is Passed»: передача напарнику в таверне",
        ["BGDUO31_207"] = Teammate,
        ["BGDUO_112"] = Teammate,
        ["BGDUO_110"] = Teammate,
        ["BGDUO31_203"] = Teammate,

        // ---- Hero powers (with variants).
        ["TB_BaconShop_HP_065"] = ProgressReplace,
        ["BG22_HERO_001p_t1"] = "Start of Combat даёт зачарование Element: Earth (BG22_HERO_001p_t1e), его Deathrattle — эффект ElementEarth",
        ["BG21_HERO_000p"] = AfterCombat,
        ["BG33_HERO_001p_ALT"] = ProgressReplace,
        ["TB_BaconShop_HP_081"] = AfterCombat + "; угадывание до боя",
        ["TB_BaconShop_HP_080"] = AfterCombat,
        ["BG23_HERO_303p2"] = AfterCombat + "; угадывание в таверне",
        ["TB_BaconShop_HP_105"] = "даёт Fish of N'Zoth в начале игры; боевая часть — эффект Fish (TB_BaconShop_HP_105t)",
        ["BG22_HERO_007p"] = "вне пула; запуск Naga Conquest по сумме Attack в таверне",
        ["BG23_HERO_306p"] = AfterCombat + "; Discover в таверне",
        ["BG20_HERO_282p"] = "Start of Combat даёт зачарование Fragrant (BG20_HERO_282pe), его Deathrattle — эффект Fragrant",

        // ---- Trinkets of the pool.
        ["BG36_MagicItem_407"] = ProgressReplace,
        ["BG36_MagicItem_410"] = ProgressReplace,
        ["BG30_MagicItem_411"] = "Start of Combat даёт зачарование BG30_MagicItem_411e; его «Deathrattle: Get 2 Blood Gems» — эффект HoggyBankDeathrattle",
        ["BG30_MagicItem_917"] = "Start of Combat даёт зачарование BG30_MagicItem_917e; его «Deathrattle: Get a spell» — эффект RustyTridentDeathrattle",
        ["BG36_MagicItem_200"] = TavernResource + " (free Refresh)",
        ["BG30_MagicItem_952"] = "Start of Combat даёт зачарование Frosty Globe (BG30_MagicItem_952e), его Deathrattle — эффект FrostyGlobe",
        ["BG36_MagicItem_213"] = "Start of Combat даёт Divine Shield существам с Rally: снимок уже его показывает (Rally — признак адресата)",
        ["BG32_MagicItem_205"] = "«outside combat»: только в таверне",
        ["BG36_MagicItem_204"] = "«outside combat»: только в таверне",
        ["BG36_MagicItem_417"] = "«outside combat this turn»: только в таверне",
        ["BG35_MagicItem_740"] = "Start of Combat даёт зачарование Skyward (BG35_MagicItem_740e2), его Deathrattle — эффект Skyward",
        ["BG30_MagicItem_541"] = "«outside combat»; аура на существ в таверне",
        ["BG32_MagicItem_887"] = "вне пула; " + HeroDamage,
        ["BG30_MagicItem_923"] = "вне пула; награда — Gold на следующий ход",
        ["BG30_MagicItem_777"] = "вне пула; награда — Lucky Egg в руку по прогрессу на всю игру",
        ["BG32_MagicItem_230"] = "вне пула; трата Gold в таверне",
        ["BG32_MagicItem_204"] = "вне пула; «outside combat»",
        ["BGDUO_MagicItem_004"] = "вне пула; «after your team Passes»: таверна",
        ["BG32_MagicItem_417"] = "вне пула; " + KeepsStats,
        ["BG35_MagicItem_155"] = "вне пула; урон герою — в таверне/после боя, бонус заклинаний «this turn»",
        ["BG35_MagicItem_714"] = "вне пула; Start of Combat даёт Deathrattle-зачарование без эффекта: маркер DEATHRATTLE остаётся необъяснённым и бой помечается «not simulated» (E6b)",

        // ---- Dark Gifts (the enchantment the gift leaves).
        ["BG36_MidGameEffect_000t50"] = KeepsStats,
        ["BG36_MidGameEffect_000t52"] = "Deathrattle: free Refresh — " + TavernResource,
        ["BG36_MidGameEffect_000t64"] = Tavern,
        ["BG36_MidGameEffect_000t64t"] = Tavern,
        ["BG36_MidGameEffect_000t66"] = "Rally: Fodder в будущие Refresh — " + TavernResource,
        ["BG36_MidGameEffect_000t74"] = Tavern,
        ["BG36_MidGameEffect_000t75"] = Tavern,

        // ---- Quests and quest rewards.
        ["BG24_Quest_125"] = "вне пула; задание на порядок доски в таверне",
        ["BG24_Quest_120"] = "вне пула; задание на итоги боёв (после боя)",
        ["BG24_Reward_138"] = AfterCombat,
        ["BG27_Reward_806"] = AfterCombat,
        ["BG27_Anomaly_555t"] = "вне пула; угадывание в таверне",
        ["BG33_Reward_004"] = "вне пула; Avenge даёт free Refresh — " + TavernResource,

        // ---- Anomalies (out of today's pool).
        ["BG27_Anomaly_714"] = "вне пула; " + AfterCombat,
        ["BG27_Anomaly_558"] = "вне пула; " + AfterCombat,
        ["BG27_Anomaly_715"] = "вне пула; " + AfterCombat,
        ["BG27_Anomaly_805"] = "вне пула; угадывание до боя, Gold после",
        ["BG27_Anomaly_555"] = "вне пула; угадывание в таверне",

        // ---- Tavern spells.
        ["BG36_883"] = AfterCombat,
        ["BG28_884"] = AfterCombat,
        ["BG28_604"] = "аура «Undead have +8 Attack this game» — игровое зачарование игрока; живые Undead в снимке её уже показывают. " +
                       "Undead, призванные в бою, получают её от движка (SimInputs.UndeadAttackBonus: BG25_011pe / BG25_011e2 → Side.AddSummonBuff, E7b)",
        ["BG27_Anomaly_575"] = "аномалия выдаёт каждому Секрет Eleventh Hour (BG27_Anomaly_575t): «prevent fatal damage in combat» делает он, " +
                               "в правиле урона героя (DamageRules.HasIceBlock, E7a)",
    };

    /// <summary>Cards that change a fight and are not simulated yet: why, and who owns the gap.</summary>
    internal static readonly Dictionary<string, string> KnownGaps = new(StringComparer.Ordinal)
    {
        // Out of today's pool: real combat effects labelled "no effect" (owners: E6a Timewarped, E6b trinkets).
        ["BG34_Giant_376"] = "вне пула; «Your Battlecries, Deathrattles, and Rallies trigger twice» — боевая аура, размечена None (E6a)",
        ["BG30_MagicItem_989"] = "вне пула; аура «Your Undead have +3 Attack» на призванных в бою, как у BG30_MagicItem_989t (E6b: добавить id к ArtisanalUrn)",
        ["BG30_MagicItem_843t"] = "вне пула; аура «minions from Tier 3 or lower have +7/+5» на призванных в бою (E6b)",
        ["BG30_MagicItem_921"] = "вне пула; аура «Your Sky Pirates have +6 Attack» на призванных в бою (E6b)",
        ["BG35_MagicItem_431t"] = "вне пула; после Deathrattle в бою растут Blood Gems, разыгрываемые позже в этом бою (E6b)",
        ["BG32_MagicItem_171"] = "вне пула; Divine Shield потерян в бою → карта в руку (фидер «card added to hand», E6b)",
        ["BG32_MagicItem_270"] = "вне пула; Avenge (3) поднимает бонус заклинаний, как у BG32_MagicItem_270t (E6b: добавить id к WickedTome)",
        ["BG35_MagicItem_922"] = "вне пула; заклинание в бою → копия в руку (фидер «card added to hand», E6b)",
        ["BG28_843"] = "вне пула; заклинание Titus' Tribute: «Deathrattles trigger twice until next turn» (зачарование игрока BG28_843e) — в бою удваивает Deathrattle; ни за кем",
        ["BG33_112"] = "вне пула; заклинание Haunted Carapace: «Your minions have +3/+1 until next turn» — аура и на призванных в бою; ни за кем",
        ["BG33_112t"] = "вне пула; Haunted Carapace (вариант): та же аура; ни за кем",
    };

    /// <summary>A card whose combat part is another card's registered effect: card id → that card.</summary>
    internal static readonly Dictionary<string, string> Via = new(StringComparer.Ordinal)
    {
        ["BG27_Anomaly_575"] = "BG27_Anomaly_575t",
        ["BG22_HERO_001p_t1"] = "BG22_HERO_001p_t1e",
        ["BG20_HERO_282p"] = "BG20_HERO_282pe",
        ["BG30_MagicItem_411"] = "BG30_MagicItem_411e",
        ["BG30_MagicItem_917"] = "BG30_MagicItem_917e",
        ["BG30_MagicItem_952"] = "BG30_MagicItem_952e",
        ["BG35_MagicItem_740"] = "BG35_MagicItem_740e2",
        ["TB_BaconShop_HP_105"] = "TB_BaconShop_HP_105t",
    };
}
