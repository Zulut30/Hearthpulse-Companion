namespace Hearthpulse.Core.Progression;

/// <summary>
/// A made-up achievement book for the render scenes and view tests: five categories, sections with and without a
/// heading, a three-tier chain part done, secret achievements done and not, one without a quota, one done below its
/// quota, chains close to done, completions spread over the last year, gains, and long names. Names are Russian, as a
/// Russian client gives them. Nothing here is a real player's.
/// </summary>
public static class AchievementsScene
{
    /// <summary>A long achievement name, for the layout.</summary>
    public const string LongName = "Легендарный собиратель тестовых данных с очень длинным названием достижения";

    /// <summary>The head of the three-tier chain (quotas 10/50/100, tier 1 done, 34 of 50 in tier 2, +7 two hours before now).</summary>
    public const int TieredHead = 1001;

    /// <summary>The head of a chain whose last gain is a new tier, a day before now.</summary>
    public const int TierGainHead = 1020;

    /// <summary>The head of a chain whose last gain is +3, five hours before now.</summary>
    public const int ProgressGainHead = 1030;

    /// <summary>The book as of <paramref name="nowUtc"/>; completion dates and gains are counted back from it.</summary>
    public static AchievementBook Build(DateTime nowUtc, bool confirmed)
    {
        var now = nowUtc.Kind == DateTimeKind.Local ? nowUtc.ToUniversalTime() : DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        var scene = new Builder(now);

        AchievementCategory[] categories =
        [
            new(1, "Общие", "general", 1),
            new(2, "Игровой процесс", "gameplay", 2),
            new(3, "Коллекция", "collection", 3),
            new(4, "Приключения", "adventures", 4),
            new(5, "Режимы игры", "game_modes", 5),
        ];
        var subcategories = new List<AchievementSubcategory>();
        var sections = new List<AchievementSection>();
        foreach (var category in categories)
        {
            for (var k = 1; k <= 2; k++)
            {
                var subId = (category.Id * 10) + k;
                subcategories.Add(new AchievementSubcategory(subId, category.Id,
                    k == 1 ? "Прогресс" : category.Id == 3 ? "Подкатегория с очень длинным названием для проверки" : "Особое", k));
                sections.Add(new AchievementSection((subId * 10) + 1, subId, k == 1 ? "Путь героя" : "Испытания", 1));
                sections.Add(new AchievementSection((subId * 10) + 2, subId, string.Empty, 2));
            }
        }

        // General / progress / "Путь героя": every case the chain rules have.
        scene.Chain(TieredHead, 111, "Коллекционер", "Соберите $q |4(разную карту,разные карты,разных карт).", [10, 50, 100], 10, done: 1, progress: 34, daysAgo: [40]);
        scene.Chain(1004, 111, string.Empty, string.Empty, [1], 15, done: 0, progress: 0, hidden: true);
        scene.Chain(1005, 111, string.Empty, string.Empty, [1], 15, done: 1, progress: 1, daysAgo: [1], hidden: true);
        scene.Chain(1006, 111, "Добро пожаловать", "Зайдите в игру.", [0], 5, done: 0, progress: 0);
        scene.Chain(1007, 111, "Выше головы", "<i>Сыграйте</i> $q |4(game,games).", [100], 10, done: 1, progress: 60, daysAgo: [3]);
        scene.Chain(1008, 112, LongName, "Одержите $q |4(победу,победы,побед) в любом режиме, кроме тех, что ещё не вышли.", [500, 1000], 20, done: 0, progress: 480);

        // Gains the tracked block shows.
        scene.Chain(TierGainHead, 121, "Мастер боя", "Победите $q |4(соперника,соперников,соперников).", [5, 25, 100, 250], 10, done: 2, progress: 30, daysAgo: [200, 1]);
        scene.Chain(ProgressGainHead, 211, "Разрушитель", "Нанесите $q урона героям.", [1000, 5000], 10, done: 0, progress: 870);
        scene.Gain(TieredHead, 0, 7, TimeSpan.FromHours(2));
        scene.Gain(TierGainHead, 1, 0, TimeSpan.FromDays(1));
        scene.Gain(ProgressGainHead, 0, 3, TimeSpan.FromHours(5));

        // Close to done, by a different share each, across the categories.
        int[] shares = [95, 92, 88, 85, 80, 76, 70, 64, 58, 50, 42, 35, 20, 10];
        for (var i = 0; i < shares.Length; i++)
        {
            var section = ((((i % 5) + 1) * 10) + 1) * 10 + 2;
            scene.Chain(1100 + i, section, "Почти готово " + N(i + 1), "Сыграйте $q |4(карту,карты,карт) из набора.", [100], 10, done: 0, progress: shares[i]);
        }

        // Completions over the last year, newest first.
        int[] days = [0, 1, 2, 4, 6, 9, 13, 20, 33, 45, 70, 120, 200, 330];
        for (var i = 0; i < days.Length; i++)
        {
            var section = ((((i % 5) + 1) * 10) + 2) * 10 + 1;
            scene.Chain(1200 + i, section, "Выполнено " + N(i + 1), "Победите в $q |4(матче,матчах,матчах).", [3], 5 + (i % 3 * 5), done: 1, progress: 3, daysAgo: [days[i]]);
        }

        // Not started and in progress, to fill the browse view of each category.
        for (var c = 1; c <= 5; c++)
        {
            scene.Chain(1300 + (c * 10), (((c * 10) + 2) * 10) + 2, "Новичок " + N(c), "Начните путь в разделе.", [10, 20], 5, done: 0, progress: 0);
        }

        return new AchievementBook(categories, subcategories, sections, scene.Achievements, scene.States, scene.Gains, now)
        {
            Confirmed = confirmed,
        };
    }

    private static string N(int n) => n.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private sealed class Builder(DateTime now)
    {
        public Dictionary<int, AchievementDef> Achievements { get; } = [];

        public Dictionary<int, AchievementState> States { get; } = [];

        public Dictionary<int, AchievementGain> Gains { get; } = [];

        /// <summary>A chain whose tiers are <paramref name="head"/>, head+1…; <paramref name="done"/> tiers completed <paramref name="daysAgo"/> days before now, the next with <paramref name="progress"/>.</summary>
        public void Chain(int head, int section, string name, string description, int[] quotas, int points, int done, int progress,
                          int[]? daysAgo = null, bool hidden = false)
        {
            for (var t = 0; t < quotas.Length; t++)
            {
                var id = head + t;
                var tierName = quotas.Length > 1 && name.Length > 0 ? $"{name} {Roman(t + 1)}" : name;
                Achievements[id] = new AchievementDef(id, section, id, tierName, description, hidden, quotas[t], points * (t + 1),
                    t + 1 < quotas.Length ? id + 1 : 0);
                if (t < done)
                {
                    var at = daysAgo is { } d && t < d.Length ? now.AddDays(-d[t]).AddMinutes(-30 * (t + 1)) : (DateTime?)null;
                    States[id] = new AchievementState(id, AchievementStatus.Completed, t == quotas.Length - 1 ? progress : quotas[t], at);
                }
                else if (t == done && progress > 0)
                {
                    States[id] = new AchievementState(id, AchievementStatus.InProgress, progress, null);
                }
            }
        }

        public void Gain(int head, int tiers, int progress, TimeSpan before) => Gains[head] = new AchievementGain(tiers, progress, now - before);

        private static string Roman(int n) => n switch { 1 => "I", 2 => "II", 3 => "III", 4 => "IV", _ => N(n) };
    }
}
