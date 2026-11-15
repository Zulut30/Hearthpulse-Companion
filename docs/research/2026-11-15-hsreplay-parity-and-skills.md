# Hearthpulse против HSReplay / HDT / Tier7: что готово, чего не хватает, какие скиллы нужны

Дата исследования: 2026-11-15. Код: `main` @ `fff06cd5` (ветка `t3/1dff49e7`), версия в проектах 0.15.0
(последний опубликованный релиз 0.14.0 от 2026-10-30). Код, установка скиллов и релиз не менялись.

## 1. Короткий ответ

**Как трекер и оверлей Hearthpulse уже сопоставим с HDT**: колода, рука соперника, секреты, счётчики, пул
Discover со статистикой выбора, гайд по муллигану, вероятная колода соперника, боевой симулятор BG с уроном и
леталом, выбор героя и тринкета, помощник драфта Арены, история, реплеи, личная статистика. Почти всё это есть в
обоих интерфейсах (WPF и Avalonia).

**Как сервис статистики Hearthpulse пока не аналог HSReplay, а клиент его данных.** Каждое общее число в
приложении (мета, матчапы, муллиган, герои и тринкеты BG, оценки Арены, выбор Discover) приходит с
`api.kolodahearthstone.com`, который собирает HSReplay и HSGuru. Разрешение на эти данные владелец подтвердил
2026-11-15 (`docs/data-permissions.md`); это закрывает юридический вопрос, но не делает данные своими.
Собственный контур «игра → загрузка → агрегат → число на экране» написан и покрыт тестами, но в работающей
системе пуст: пул статистики развёрнут и отвечает `games: 0`, адрес пула в приложении не задан, маршрут
собственной аналитики `v2` на сервере отвечает 404.

**Три вещи, которые мешают честно назвать продукт полноценным аналогом:**

1. Нет собственной выборки. Пока она пуста, нельзя дать то, чего источник не публикует: синергии пар карт в
   муллигане, муллиган по конкретной колоде, статистику героя под трайбы лобби, срез по регионам.
2. Нет коллекции игрока (ни чтения из клиента, ни «могу собрать», ни пыли до колоды).
3. Часть заявленного не доведена до пользователя: 0.15.0 не выпущен, Twitch-расширение ждёт серверной части,
   помощник Арены выключен по умолчанию (бета), установщик не подписан.

Общий процент готовности не считаю: строки матрицы несравнимы по весу. Ниже матрица по задачам пользователя.

## 2. Что и как проверено

| Способ | Что именно |
|---|---|
| Статически (чтение кода) | `src/Hearthpulse.Core`, `Presentation`, виды обоих UI, `server/pool`, `twitch-extension`, `docs/`; локальные checkout сайта и API данных (см. 2.1) |
| Реально запускалось | `dotnet test tests/Hearthpulse.Tests` на Windows, Debug: 3553 пройдено, 2 пропущено, 1 не пройден. Упавший `UpdateSignatureTests.The_shipped_key_file_parses` ищет корень репозитория от папки сборки, а я вынес артефакты в `%TEMP%` (`--artifacts-path`): это следствие моего способа запуска, не дефект продукта. `Hearthpulse.Presentation.Tests` (WPF + Avalonia вместе) и само приложение не запускались |
| Живые GET-запросы без авторизации | около 20 публичных адресов `api.kolodahearthstone.com`, `hearthpulse.net`, `bg.hs-manacost.ru` (таблица в 4.2). Ничего не отправлялось и не менялось |
| Онлайн | Страницы hsreplay.net (15.11.2026), строковый ресурс его интерфейса, CHANGELOG HDT до v1.59.8, релизы `HearthSim/HDT-Releases` до v1.60.1; каталог skills.sh и `SKILL.md` 25 кандидатов в GitHub (описание, структура и начало текста каждого; целиком не вычитывались) |
| Недоступно | Разделы HSReplay за входом и подпиской (My Replays, My Decks, My Stats, коллекция, личная статистика Tier7): видел только редирект на вход. Цены подписок (рендерятся скриптом). Содержимое базы сайта и число игр в профилях игроков. Сервер по SSH не открывался: карта `reference/server-151.md` снята 2026-10-23 |

### 2.1. Свежесть локальных источников

- Сайт: `D:\HeartLens-main\reference\repos\HeartPulse` — `main` @ `d1448f0` от 2026-10-23, **устарел**. На GitHub
  `Manacost-Labs/HeartPulse` `main` @ `37f0b795` от 2026-11-11. Нужный серверный код читал в worktree
  `HeartPulse-wt-crash-reports` (ветка слита как PR #352 2026-11-06) и сверял со списком PR через `gh`.
- API данных: `reference/repos/api.kolodahearthstone.com` @ `0e78c71` от 2026-10-21; на GitHub последний коммит
  2026-11-09. Локальная копия отстаёт, живые ответы сняты напрямую.
- `reference/index/not-implemented-2026-10.md` (срез 0.12.0) устарел сильно: секреты, сессия ладдера,
  вероятная колода, урон и летал в бою, помощник Арены, шаринг реплеев, Потасовка, картинки для шаринга с тех пор
  сделаны. Использовал его только как список того, что перепроверить.
- `.claude/skills/hearthpulse-data-sources/reference/endpoints.md` называет матрицу матчапов
  `v1/hsguru/matchups` «не вызывается». Это уже неверно: `HearthpulseData.LoadHsguruMatchupsAsync`
  (`src/Hearthpulse.Core/Api/HearthpulseData.cs:212`) её читает, вкладка «Matchups» есть в обоих UI.

## 3. Что реально умеют HSReplay, HDT и Tier7 (на 15.11.2026)

Источники в разделе 9. Строковый ресурс сайта содержит и старые строки (Mercenaries, Duels, Classic), поэтому
как подтверждение беру только то, что есть в навигации сайта, в заголовках живых страниц или в CHANGELOG HDT.

### 3.1. Веб-продукт (hsreplay.net)

| Область | Что есть | Доступ |
|---|---|---|
| Мета Constructed | Тир-лист архетипов (`/meta/`), колоды (`/decks/`), растущие колоды (`/decks/trending/`), матчапы архетипов, популярность по рангам | Базовое бесплатно; часть фильтров Premium |
| Форматы | Standard подтверждён навигацией. Wild есть в фильтрах и в клиенте («Standard / Wild / Arena» для муллигана, «Not enough support for Wild» в интерфейсе: данных меньше). Twist в интерфейсе помечен «Locked». Classic, Duels, Mercenaries встречаются только в старых строках — актуальная поддержка не подтверждена, в сравнение не включены | — |
| Фильтры | Ранг (Bronze–Legend, Diamond–Legend, Diamond 4–1, Legend, Top Legend), период (1/3/7 дней, текущий патч, «Last 30 days» для бесплатных), регион (NA, EU, APAC, CN), класс соперника, опыт игрока, кто ходит первым | Ранг, регион, класс соперника и ряд периодов — Premium |
| Карты | Статистика всех карт (`/cards/`), в колоде: винрейт при взятии и при розыгрыше, ход розыгрыша по матчапам | Часть колонок Premium |
| Муллиган | Гайд по колоде и архетипу, отдельно по монетке и классу соперника, «Opponent Mulligan Guide», личный гайд по своим играм | Premium |
| Пулы | Discover Pools и Assemble Pools: что предлагают, как часто берут, винрейт после выбора (`/cards/discover-pools/`, `/cards/assemble-pools/`) | Бесплатно |
| Арена | Arena Guide: тир-лист классов, оценки карт Arenasmith, группы легендарок, 12-победные колоды Underground с перевыбором | Оценки Arenasmith — Premium |
| Battlegrounds | Герои (тир, pick rate, лучшая композиция, среднее место, распределение мест), композиции с гайдами Jeef и «perfect games», существа (влияние на место, боевой винрейт, фильтры по тиру, типу, механике), тринкеты, тёмные дары; фильтры MMR и периода; Duos-тир-лист героев | Полные списки и фильтры — Tier7 |
| Личное | My Replays, My Decks, My Stats, My Collection («могу собрать», пыль до колоды, публичная ссылка), личная статистика BG (MMR во времени, по героям и композициям) | Вход; личная статистика по колодам — Premium, BG — Tier7. **Содержимое не проверял** |
| Реплеи | Загрузка из HDT, плеер Joust, настройка видимости, удаление | Бесплатно |
| Прочее | 14 языков интерфейса, расширение Twitch, промокоды, связка аккаунтов Blizzard/Discord/Twitch | — |

### 3.2. Клиент (Hearthstone Deck Tracker, v1.60.1 от 2026-11-12; HSTracker для Mac)

- Бесплатно: трекер колоды, рука соперника, секреты, счётчики (с 1.55.0 настраиваются по одному), связанные
  карты, активные эффекты, Max Resources, порядок выхода на стол, The OutFinder (пулы Discover, более 700
  эффектов, с 1.54.0), окно для OBS, расширение Twitch, загрузка реплеев и коллекции.
- BG бесплатно: Bob's Buddy (заявлено 10 000+ симуляций, шансы победы, ничьей, поражения, летал), браузер
  существ с фильтрами и закреплением, виджет сессии с MMR и последними досками, последняя известная доска
  соперника, Duos.
- Premium в клиенте: Mulligan G-V2 (оценка карт стартовой руки с учётом синергий и антисинергий внутри руки),
  Arenasmith (тиры героев, оценки карт с поправкой на уже взятое, синергии, перевыбор в Underground), фильтры и
  разбивка по ключевым словам в OutFinder. Пробный доступ: 5 игр в неделю для муллигана, 1 драфт в неделю.
- Tier7 в клиенте: статистика героев на выборе под трайбы лобби, статистика и тир-лист композиций под лобби,
  гайды по героям, тринкетам, аномалиям, оценки тринкетов в магазине, тиры силы героя сэра Финли (1.58.10).
  Бесплатно 2 матча в неделю.

### 3.3. Данные и инфраструктура

- Собственная выборка: реплеи, которые загружает HDT («backed by millions of games» на странице Premium).
  Отсюда всё, что недоступно сборщику чужих страниц: личные числа, пары карт, срез под лобби.
- Пороги публикации: «Too few contributors at this rank», «Only games from players who uploaded at least this
  many replays», серые фильтры при малой выборке.
- Серверные функции Tier7 считаются на сервере под конкретное лобби, поэтому и лимитируются по матчам.

## 4. Hearthpulse по коду и по работающим сервисам

Статусы: **готово** — код есть, подключён в обоих UI; **частично**; **не подключено** — код есть, пользователь
не получает; **план**; **нет**; **не проверено**.

### 4.1. Приложение

| Функция | Статус | Доказательство |
|---|---|---|
| Страницы главного окна | готово | `Presentation/Main/MainViewModel.Navigation.cs:16-23`: Overview, Battlegrounds, Standard, Wild, Arena, Brawl, History, Settings; вкладки BG `Mine, Heroes, Comps, Trinkets, Cards`, Constructed `Decks, Tiers, Top, Matchups`. Виды парные: `App/Main/Pages|Meta|Stats` и `UI/Main/Pages|Meta|Stats` |
| Мета Standard и Wild | готово (чужие данные) | `Core/Constructed/ConstructedMeta.cs:61` форматы, `:67-77` восемь срезов ранга (All … Top 100), `:83` периоды; загрузка `ApiConstructedMeta`; оба `ConstructedMetaView` связывают одни и те же свойства (`MetaRanks`, `MetaPeriods`, `MetaClasses`, `MetaTierRows`, `MatchupMatrix`, `Mulligan`, `ArchetypeDecks`) |
| Матрица матчапов | готово | `HearthpulseData.cs:212` `LoadHsguruMatchupsAsync`, `Core/Constructed/MetaMatchups.cs`, `MainViewModel.Meta.cs:120,136` |
| Страница архетипа: колоды, муллиган, матчапы | готово | `HearthpulseData.cs:81,88,113`, `MainViewModel.MetaDetail.cs` |
| Фильтр региона | нет | в `Core/Constructed` и `HearthpulseData.cs` региона нет; сервер собирает один регион (`region: REGION_EU` в ответе `api/db/archetypes`) |
| Статистика отдельной карты (страница) | нет | страницы карты нет; числа карты видны только в муллигане архетипа и на странице своей колоды |
| Гайд по муллигану в игре | частично | Срезы по монетке, классу соперника, рангу: `HearthpulseData.cs:164,184`, `Core/Tracking/MulliganGuide.cs`. **Синергий пар нет**: `Core/Tracking/Mulligan.cs:82-83` `HasPairData` — «no server endpoint publishes card pairs». Муллиган по конкретной колоде не реализован; для Арены нет |
| Трекер колоды, рука соперника, секреты, счётчики, кладбище, таймер хода | готово | `Core/Tracking`, `Core/Secrets/SecretTracker.cs`, панели в `App/Overlay` и `UI/Overlay` парные (по 23 файла панелей в каждой папке, имена совпадают) |
| Вероятная колода и архетип соперника | готово | `Core/Constructed/LikelyDeck.cs:53`, `Stats/ArchetypeGuess.cs`, панели `LikelyDeckPanel`, `OpponentArchetypePanel` в обоих UI |
| Пул Discover и статистика выбора | готово (чужие данные) | `Core/CardPool/*`, `DiscoverPicks.cs:47`, `HearthpulseData.CardChoices.cs` |
| Коллекция | нет | нет `Core/Collection`, чтения коллекции из памяти нет |
| Личная статистика | готово | `Core/Stats/ConstructedStats.cs:35`, `BattlegroundsStats.cs:71`, `ArenaStats.cs`, `DeckDetails.cs:43` (сводка, матчапы, муллиган и розыгрыш по картам), семьи версий колоды `DeckFamilies.cs`; периоды `Stats/StatsPeriod.cs` (всё, неделя, месяц, сезон). Экспорта в CSV нет |
| Реплеи локально | готово | `Core/Replay/*` (рекордер, `MatchPage`, `MatchAnalysis`, BG-раунды), виды `Main/Match` в обоих UI |
| Реплей по ссылке | готово в клиенте, сервер слит; вживую не проверено | `Core/Replay/ReplayUpload.cs:41`, `Accounts/AccountClient.cs:190-197` (`POST tracker/replays`); на сайте PR #352 слит 2026-11-06. Проверить загрузкой нельзя без аккаунта |
| Перенос из HDT и Firestone | готово | `Core/Migration/HdtImport.cs`, `FirestoneImport.cs`, виды `Main/Migration` в обоих UI |
| BG: вкладки героев, композиций, тринкетов, карт | готово (чужие данные) | `Core/Battlegrounds/Site/BgSiteClient.cs:42`, срезы `:50-56` (соло 50/20/5/1 %, Duos 50/20 %, тринкеты 4 среза); список допущенных источников в `BgSiteParser` |
| BG: совет на выборе героя и тринкета, магазин, тёмный дар | частично | `Core/Battlegrounds/BattlegroundsAdvisor*.cs`. Композиции, тринкеты со статистикой, влияние трайбов лобби и пороги MMR приходят только из пула (`docs/bg-meta-sources.md` §1), а пул пуст — см. 4.2 |
| BG: гайды по героям и композициям, квесты, аномалии | нет | в `Core/Battlegrounds` и `Core/Pool` нет типов гайдов; статистики аномалий и квестов нет |
| BG: боевой симулятор | частично | `Core/Battlegrounds/CombatSimulator.cs:163` 4000 прогонов, `:192` `CombatOdds`, `:206-209` летал в обе стороны; в игре бюджет 40 мс (`Tracking/BattlegroundsTracker.Combat.cs:217`). 104 id карт с `[CardEffect]` в 74 файлах. Точность измерена на **6 играх, 76 боях**: исход угадан в 87 %, Brier 0,191 (`docs/bg-combat-sim.md`). Летал в Duos выключен (`:183`) |
| BG: сессия, лобби, попап соперника, бейджи, MMR соперников | готово | панели `SessionPanel`, `BgLobbyPanel`, `LobbyPopupPanel`, `BgBadgesLayer`; MMR соперников выключен по умолчанию (`docs/bg-opponent-mmr.md`) |
| Арена: помощник драфта | частично | `Core/Arena/ArenaDraftTracker.cs:59`, оценки `ArenaMeta`, чтение предложения из памяти `Memory/HearthstoneMemory.Arena.cs:15`. Выключен по умолчанию: `AppSettings.cs:124` `ShowArenaDraft`. Одна оценка на все классы, срез Underground за 4 дня (endpoints.md); синергий с уже взятым и оценок по классам нет |
| Потасовка | готово | `Stats/BrawlStats.cs`, `Api/HearthpulseData.Brawl.cs`. Источник `hsguru_brawl` сейчас в состоянии «hard-failed» на сервере (4.2) |
| Аккаунт hearthpulse.net | готово | `Core/Accounts/*` (device flow, `TokenStore`, `/me`); `GET /api/v1/me` без токена отвечает 401 с ожидаемой ошибкой |
| Платный доступ | почти не применяется | `Entitlements.Has` вызывается только в `Accounts/UsageGate.cs:258` и `MainViewModel.AccountCenter.cs:218`; лимиты на сайте выключены (`usage/limits` → `enabled: false`) |
| Загрузка игр в профиль | готово в клиенте, сервер слит; содержимое не проверено | `Accounts/ProfileUploader.cs:25`, включено по умолчанию `AppSettings.cs:178`; PR #352 и #356 на сайте слиты |
| Twitch-расширение | не подключено | клиент `Core/Twitch/TwitchPublisher.cs:55`, страницы `twitch-extension/`; серверная часть — PR #353 **открыт**, `GET /api/v1/twitch/ext/snapshot` отвечает 404 |
| Отчёты о сбоях | готово | `Core/Diagnostics/CrashReporter.cs:13`, выключено по умолчанию `AppSettings.cs:193` |
| Обновления | частично | `Core/Updates/UpdateService.cs:20`; репозиторий релизов публичный. Ключа подписи обновлений в сборке нет (`Updates/update-signing-key.pub` — только комментарии, «transition mode»), установщик Windows не подписан, macOS не нотаризован (`installer/RELEASE_NOTES.md`) |

**Зрелость WPF и Avalonia.** Наборы видов и панелей совпадают по файлам, модели представления общие
(`Hearthpulse.Presentation`). Различия, найденные в коде:

- Чтение памяти игры работает только на Windows (`Core/Memory/HearthstoneMemory.cs:52`): рейтинг BG, ранги,
  трайбы лобби, драфт Арены на macOS и Linux недоступны в любом UI.
- Отправка игр в пул написана только в WPF-хосте: `App/App.xaml.cs:676,707,714` (`PoolClient.SubmitAsync`,
  `SubmitConstructedAsync`); в `UI` и `Desktop` вызовов нет. Записи игр (`GameStatsUploader`) и захват источника
  (`SourceCaptureHost`) подключены общим `Core/Hosting/AppServices.cs:70,73`.
- Установщик поставляет WPF; Avalonia помечена как preview. Совместные контракты проверяет
  `tests/Hearthpulse.Presentation.Tests` — в этом исследовании не запускался.

### 4.2. Сервер, сайт и данные (живые ответы 2026-11-15, около 16:40 UTC)

| Адрес | Ответ | Что это значит |
|---|---|---|
| `api.kolodahearthstone.com/v1/hsguru/meta?format=standard&rank=legend&period=past_day` | 200, 51 архетип, `fetched_at` 11:00 сегодня | мета HSGuru свежая |
| `…/api/db/archetypes?game_type=RANKED_STANDARD&rank_range=LEGEND` | 200, 46 архетипов, прогон `completed_at` **2026-11-12**, `region: REGION_EU` | архетипы и муллиган HSReplay трёхдневной давности, один регион |
| `…/api/bg/heroes?mode=solo&mmr=TOP_50_PERCENT` | 200, 116 героев, `fetched_at` 2026-11-12, 2,7 МБ | герои BG трёхдневной давности |
| `…/v1/battlegrounds/minions/stats` | 200, 273 существа, сегодня 15:20 | свежо |
| `…/v1/hsguru/matchups?format=standard&rank=legend` | 200, 98 архетипов, 5393 ячейки, 3,69 млн игр | матрица есть и свежая |
| `…/api/bg/trinkets` | 200, 247 тринкетов, сегодня 06:01 | есть, приложение для совета не читает |
| `…/v1/arena/classes` | 200, 11 классов, сегодня 06:03 | свежо |
| `…/v1/hsreplay/card-choices/cards` | 200, 533 карты, сегодня 03:35 | свежо |
| **`…/stats/v1/stats?mode=solo`** | 200, **`games: 0`**, все списки пусты | пул развёрнут, но игр в нём нет |
| `…/stats/v1/events/summary` | 200, `games: 0` | записей игр нет |
| **`…/stats/v2/analytics/bg/heroes?mode=solo`** | **404 от nginx** | маршрут собственной аналитики не развёрнут |
| `hearthpulse.net/api/v1/usage/limits` | 200, `enabled: false` | лимиты выключены |
| `hearthpulse.net/health/ready` | 200, `dataStatus: "degraded"`: каталоги карт отдаются из last-known-good, у API данных «stale: vicious_syndicate_radars; hard-failed: hsguru_brawl» | сайт работает на запасных копиях части данных |
| `bg.hs-manacost.ru/api/bg/heroes`, `/api/bg-comps` | 200, сегодня 06:00; у композиций источник «HSReplay + Firestone» | сервис живой; Firestone-строки клиент отсекает по списку допущенных источников |

Выводы по контуру данных:

- **Собственная выборка равна нулю.** `Core/Pool/PoolClient.cs:19` `DefaultEndpoint = ""`, адрес берётся только
  из `settings.json` (`:54-57`), в интерфейсе поля нет. Поэтому `GameStatsUploader` (`GameStats/GameStatsUploader.cs:137`)
  и захват источника (`SourceCapture/SourceCaptureQueue.cs:63`) ничего не отправляют, а совет BG не получает
  композиций и влияния трайбов.
- **Собственная аналитика не дошла ни до сервера, ни до экрана.** Метрика `bg.hero.offer_pick_placement`
  описана и протестирована (`docs/analytics-api.md`, `server/pool/node/analytics.mjs`, миграция `0009`),
  клиент `Core/Pool/AnalyticsClient.cs:36` используется только в тестах; маршрут в production отвечает 404.
- **Собственные замеры малы.** Симулятор проверен на 6 играх владельца; в `history.db` владельца на 01.11 было
  2 BG-игры (`docs/bg-combat-sim.md`).
- **Чужие данные собираются скрейпингом** (FastAPI, Playwright, прокси: `requirements.txt` API данных). Свежесть
  от 1 часа до 3 дней, зависит от доступности источника; один регион; нет того, чего источник не показывает.
- **Сайт заметно шире приложения** (worktree сайта, `apps/public-web/app`): мета, матчапы, архетипы, карты,
  конструктор колод, fun-decks, Vicious Gold, Арена (классы, тир-лист, легендарки), библиотека и стратегии BG,
  лидерборды, стримеры, профили `/id`, `/u`, страница реплея, публичный API v1 статистики
  (`docs/specs/public-api-statistics-coverage.md`). Стек: Next.js 16, React 19, Express, SQLite.
- **Наблюдаемость.** Сайт: Sentry (`server/index.ts:4,9597`), workflow `production-monitor`,
  `production-observer`, CodeQL, gitleaks, trivy, osv-scanner; на сервере uptime-kuma, beszel, Plausible
  (карта от 23.10). Пул: `server/pool/deploy/synthetic-smoke.mjs`, оповещений по нему не нашёл. Приложение:
  локальный `hearthpulse.log` и отчёты о сбоях по согласию; метрик использования функций нет, и это осознанное
  ограничение приватности. Оповещения о старении данных (архетипы от 12.11) в коде не найдено.

## 5. Матрица parity

«Источник»: **Ч** — разрешённые чужие данные HSReplay/HSGuru, **С** — собственные данные или расчёт,
**—** — данные не нужны.

| Задача пользователя | HSReplay / HDT / Tier7 | Hearthpulse | Источник | Разрыв |
|---|---|---|---|---|
| Какой колодой играть на моём ранге | тир-лист, фильтры ранга, периода, региона | тир-лист Standard и Wild, 8 срезов ранга, периоды, бесплатно | Ч | нет региона; данные HSReplay до 3 дней |
| Против чего моя колода плоха | матчапы архетипа, матрица | матчапы и матрица | Ч | — |
| Что оставлять в стартовой руке | G-V2: карты + синергии руки, монетка, класс соперника; Standard, Wild, Arena | карты, монетка, класс соперника, ранг | Ч | нет синергий пар, нет Арены, нет гайда по своей колоде |
| Что даёт Discover и что брать | OutFinder + Discover/Assemble Pools | пул, шансы, статистика выбора | Ч | Assemble-пулы не проверял отдельно |
| Что у соперника | колода, секреты, связанные карты, счётчики | то же плюс вероятная колода и архетип | — / Ч | на уровне |
| Мои результаты | My Stats, My Decks, личный муллиган (Premium) | My Stats, страница колоды с картами, семьи версий; профиль на сайте | С | нет экспорта; веб-профиль вживую не проверен |
| Пересмотреть и показать игру | реплей на сайте, плеер Joust | локальный реплей с разбором, ссылка на сайт | С | веб-плеер вживую не проверен |
| Что могу собрать из своей коллекции | коллекция, пыль, «могу собрать» | нет | — | **полный разрыв** |
| Какого героя BG взять | Tier7: статистика под трайбы лобби, гайд | тир и места по срезу MMR | Ч | нет поправки на лобби и гайда |
| Какую композицию собирать | Tier7: статистика и гайды под лобби | список композиций на вкладке; в игре — только из пула | Ч / С | в игре пусто, пока пул пуст |
| Какой тринкет взять | оценки под лобби | тир-лист на вкладке; в игре без статистики | Ч / С | то же |
| Выиграю ли бой | Bob's Buddy, 10 000+ симуляций, летал, Duos | свой симулятор, 4000 прогонов или 40 мс, урон и летал | С | точность на малой выборке; летал в Duos выключен |
| Что брать в драфте Арены | Arenasmith: оценки под колоду, синергии, перевыбор | оценки карт и тир классов, бета, выключено | Ч | нет поправки на колоду, перевыбора, групп легендарок |
| Показать зрителям | расширение Twitch, окно OBS | окна OBS, режим стримера; расширение не работает | — | серверная часть не слита |
| Потасовка | трекинг колод | страница с классами недели | Ч | у нас больше, но источник сейчас сбоит |
| Языки | 14 | русский и английский | — | — |
| Платформы | Windows (HDT), Mac (HSTracker) | Windows; Avalonia preview для macOS и Linux без чтения памяти | — | — |

## 6. Что закрываем, что мешает, где мы сильнее

**Уже закрываем** (пользователь получает результат сегодня, в 0.14.0 или в собранном 0.15.0):
выбор колоды и чтение меты; матчапы; базовый муллиган; игру с трекером, секретами и счётчиками; оценку
соперника; Discover; бои BG с шансами; чтение тир-листов BG; личную статистику и разбор своих игр; перенос
истории из HDT и Firestone.

**Не позволяет называться полноценным аналогом:**

1. Нулевая собственная выборка и неразвёрнутая аналитика (4.2). Это корень разрывов в муллигане, BG под лобби и
   регионах.
2. Нет коллекции.
3. Зависимость от скрейпинга: давность до 3 дней, один регион, деградация источников видна прямо сейчас.
4. Недоведённые части: Twitch, Арена в бете, релиз 0.15.0, подпись установщика и обновлений.
5. Не проверено вживую то, что HSReplay показывает как основу сервиса: веб-профиль игрока и веб-плеер реплея.

**Собственные преимущества:**

- Русский интерфейс и русские названия архетипов (`Core/Constructed/ArchetypeTranslations.cs`,
  `SiteArchetypeLabels.json`), русские статьи Потасовки.
- Срезы ранга, матчапы и муллиган по классу соперника бесплатны; у HSReplay это Premium.
- Реплей хранит только то, что игрок видел, а после матча показывает всё, что раскрыл лог; публичная ссылка
  без послематчевых данных соперника (`docs/web-replays.md`).
- Симулятор написан с нуля по текстам карт и помечает бои с неучтёнными эффектами, а не выдаёт 0 % и 100 %.
- Правила публикации собственной аналитики строже и записаны: не меньше 5 установок за числом, интервалы
  Уилсона, отзыв снимка при удалении (`docs/analytics-api.md`). Они начнут работать, когда появятся данные.
- Перенос из HDT и Firestone, картинки для шаринга, таймкоды YouTube, итоги года, 13 тем оформления, сборка для
  Linux и macOS.

## 7. План

Трудоёмкость относительная: S — дни, M — одна-две недели, L — больше.

### P0. Свой контур данных и доставка

| # | Что | Зависит от | Приёмка | Объём |
|---|---|---|---|---|
| P0.1 | Включить пул: развернуть Node-сервис пула с миграцией `0009`, маршрут nginx для `/stats/v2/`, затем `SourceWorker` v2, затем `DefaultEndpoint` в клиенте (порядок из `docs/analytics-api.md`, «Rollout order») | решение владельца о деплое | `GET /stats/v2/analytics/bg/heroes?mode=solo` отвечает JSON, не 404; `/stats/v1/stats` показывает `games > 0`; первая строка героя публикуется при 5 установках | M |
| P0.2 | Перенести отправку игр в пул из `App.xaml.cs` в общий `AppServices`, чтобы работала и в Avalonia | — | тест контракта обоих хостов; в `UI`/`Desktop` игра уходит в пул | S |
| P0.3 | Вывести первую собственную метрику на экран: `AnalyticsClient` во вкладке героев BG, с подписью источника и размером выборки | P0.1 | в обоих UI строка «свои данные» с интервалом; пустое состояние при `not_published` | S |
| P0.4 | Контроль свежести чужих данных: показывать дату среза рядом с числами, оповещение при возрасте прогона больше суток, разбор сбоя `hsguru_brawl` | — | возраст каждого источника виден в приложении; оповещение приходит при остановленном сборщике | S–M |
| P0.5 | Выпустить 0.15.0 и проверить на живом сайте загрузку игр, профиль и реплей по ссылке | слово владельца на релиз | игра видна в профиле; ссылка на реплей открывается; запись в отчёте | S |

### P1. Разрывы, которые видит игрок

| # | Что | Зависит от | Приёмка | Объём |
|---|---|---|---|---|
| P1.1 | Коллекция: чтение из памяти, «могу собрать», пыль до колоды в мете и на странице колоды | проверка `hearthpulse-legal` (чтение памяти) | колоды меты помечены «есть / не хватает N пыли»; на macOS и Linux честное пустое состояние | L |
| P1.2 | Синергии в муллигане из своих игр (пары карт стартовой руки и исход) | P0.1, объём выборки | `HasPairData` становится true для архетипов с ≥ 200 игр на пару (`MulliganGuide.MinPairGames`); иначе панель как сейчас | M–L |
| P1.3 | BG под лобби: влияние трайбов на героя, композиции и тринкеты в игре. Сначала серверные ручки из `docs/bg-meta-sources.md` §3 (чужие данные), затем свои | P0.1 для собственной части | совет на выборе героя меняется при смене трайбов лобби; у тринкета в магазине число и выборка | M |
| P1.4 | Арена из беты: оценки по классам, поправка на уже взятое, перевыбор, группы легендарок | ручки API данных | включено по умолчанию; тесты на перевыбор | M |
| P1.5 | Twitch: слить PR #353 сайта, включить публикацию | сайт | зритель видит колоду; `ext/snapshot` отвечает 401 без токена, не 404 | S–M |
| P1.6 | Точность симулятора на большой отложенной выборке (боевые доски из своих захватов) | P0.1 | замер на ≥ 500 боях записан в `docs/bg-combat-sim.md`; решение по леталу в Duos | M |

### P2. Доведение

Фильтр региона (нужен сбор по регионам или свои данные); страница карты; гайды героев и композиций на русском
(контент); подпись кода Windows, нотаризация macOS, ключ подписи обновлений; экспорт истории в CSV; новые
языки. Mercenaries не планировать (ADR проекта).

### Первый практический этап

P0.1 на стенде без релиза: поднять пул с миграцией `0009` по `server/pool/deploy/LINUX-RUNBOOK.md`, указать его
в `PoolEndpoint` файла `settings.json` в копии папки данных (`HEARTHPULSE_DATA`), сыграть или проиграть из лога
несколько BG-игр и убедиться, что `/stats/v1/stats` показывает игры, а `/v2/analytics/bg/heroes` отдаёт
`not_published` вместо 404. Это проверяет весь путь от игры до снимка до того, как менять `DefaultEndpoint`.

## 8. Скиллы из skills.sh

Пользователь назвал `skill.sh`: этот домен — заглушка с редиректом на `/lander`. Каталог находится на
`https://skills.sh/` (отвечает 308 на `https://www.skills.sh/`). Поиск шёл через его же
`/api/search` по 35 темам; у 25 кандидатов просмотрены `SKILL.md` в исходных репозиториях (описание,
структура, начало текста) и проверены лицензия и дата последнего изменения.

### 8.1. Что уже есть

- Проект: 30 скиллов в `.claude/skills`, те же 30 заглушек в `.agents/skills`. Покрыты карта проекта, дизайн,
  UI-ревью (8 скиллов), оверлей, страницы, трекинг, счётчики, источники данных, тесты, релиз, ревью, параллельная
  работа, команда, проектирование модулей, глоссарий и ADR, письмо для агентов.
- Глобально (`~/.claude/skills`): `diagnosing-bugs`, `systematic-debugging`, `verification-before-completion`,
  `requesting-code-review`, `writing-plans`, `handoff`, `wordpress-plugin-dev`, `manacost-ship`, `blender`.
  Встроенные: `code-review`, `security-review`, `simplify`.
- В `~/.codex/skills` для этого проекта полезного нет (видео, GSAP, Playwright).
- Сайт (`HeartPulse`) уже ведёт `skills-lock.json` (три скилла `sofa*`) и 13 своих скиллов.

Пробелы, под которые искал: производительность .NET, покрытие тестами, Avalonia 12, статистика для своей
аналитики, приватность данных игроков, безопасность CI, наблюдаемость серверной части.

### 8.2. Рекомендуемые

Все восемь страниц skills.sh и репозитории отвечали 200 на момент проверки. Формат `SKILL.md` общий для Claude
Code, Codex и T3; в проект скилл попадает вместе с записью в `.claude/skills/THIRD-PARTY-NOTICES.md` и
заглушкой от `tools/skills/gen_codex_stubs.py`.

| # | Скилл и источник | Какой пробел закрывает | Ожидаемый результат | Дублирование | Лицензия | Решение |
|---|---|---|---|---|---|---|
| 1 | `analyzing-dotnet-performance` — [skills.sh](https://skills.sh/dotnet/skills/analyzing-dotnet-performance), [исходник](https://github.com/dotnet/skills/blob/main/plugins/dotnet-diag/skills/analyzing-dotnet-performance/SKILL.md) (`dotnet/skills`, 9 файлов) | Поиск ~50 антипаттернов в горячих путях: поток логов, симулятор, сборка снимка. У нас есть `tools/Hearthpulse.Bench` и аудит, но нет чек-листа для агента | Отчёт с приоритетами перед P1.6 и при правке `GameSession` | нет | MIT, обновлён 15.11.2026 | **Установить как есть** |
| 2 | `dotnet-trace-collect` — [skills.sh](https://skills.sh/dotnet/skills/dotnet-trace-collect), [исходник](https://github.com/dotnet/skills/blob/main/plugins/dotnet-diag/skills/dotnet-trace-collect/SKILL.md) | Выбор инструмента и команд для снятия трассы и дампа: память в трее, зависания оверлея | Воспроизводимый сбор диагностики на Windows | частично с `diagnosing-bugs` (тот про цикл, а не про инструменты .NET) | MIT | **Установить как есть**, глобально: нужен редко |
| 3 | `test-gap-analysis` — [skills.sh](https://skills.sh/dotnet/skills/test-gap-analysis), [исходник](https://github.com/dotnet/skills/blob/main/plugins/dotnet-test/skills/test-gap-analysis/SKILL.md) | «Какие изменения поведения тесты не заметят» для модулей вне Stryker (`docs/mutation-testing.md` покрывает четыре области) | Список непокрытых исходов для `hearthpulse-qa` | частично с `hearthpulse-testing` (тот про запуск и приёмы) | MIT | **Установить как есть**, назвать в файле роли QA |
| 4 | `avalonia` — [skills.sh](https://skills.sh/codewithmukesh/dotnet-claude-kit/avalonia), [исходник](https://github.com/codewithmukesh/dotnet-claude-kit/blob/main/skills/avalonia/SKILL.md) | Единственный найденный скилл, сверенный с Avalonia 12.1.x (у нас 12.1.4, compiled bindings включены) | Правила `x:DataType`, `StyledProperty`/`DirectProperty`, потоки, антипаттерны | наши UI-скиллы про вид, а не про механику фреймворка | MIT | **Адаптировать**: взять привязки, свойства, потоки, антипаттерны; убрать Generic Host, DI и `ViewLocator` — у нас свой корень композиции (`AppHost`) и общие модели представления |
| 5 | `statistical-analysis` — [skills.sh](https://skills.sh/anthropics/knowledge-work-plugins/statistical-analysis), [исходник](https://github.com/anthropics/knowledge-work-plugins/blob/main/data/skills/statistical-analysis/SKILL.md) | Методика для своей аналитики: распределения, выбросы, значимость, когда числу нельзя верить | Основа проектного скилла про метрики: правила каталога `docs/analytics-metrics.json`, порог 5 установок, интервалы Уилсона, усадка малых выборок | нет | Apache-2.0 | **Адаптировать** в проектный скилл к P0.3 и P1.2 |
| 6 | `gdpr-compliant` — [skills.sh](https://skills.sh/github/awesome-copilot/gdpr-compliant), [исходник](https://github.com/github/awesome-copilot/blob/main/skills/gdpr-compliant/SKILL.md) | Инженерный чек-лист для загрузки игр, реплеев, отчётов о сбоях: минимизация, сроки хранения, удаление по запросу | Проверка контракта пула и сайта до включения P0.1 | агент `hearthpulse-legal` оценивает риск, а не проектирует хранение | MIT | **Адаптировать** как справочник для `hearthpulse-legal` и работ по `server/pool` |
| 7 | `github-actions-hardening` — [skills.sh](https://skills.sh/github/awesome-copilot/github-actions-hardening), [исходник](https://github.com/github/awesome-copilot/blob/main/skills/github-actions-hardening/SKILL.md) | В наших workflow 40 строк `uses:` и ни одной привязки к SHA; `release.yml` собирает и публикует установщики | Аудит `ci.yml`, `release.yml`, `codeql.yml`, `mutation.yml` с правками | нет | MIT | **Установить как есть**, разовый аудит до подписи кода |
| 8 | `observability-and-instrumentation` — [skills.sh](https://skills.sh/addyosmani/agent-skills/observability-and-instrumentation), [исходник](https://github.com/addyosmani/agent-skills/blob/main/skills/observability-and-instrumentation/SKILL.md) | Метод «сначала вопросы дежурного, потом сигналы» для пула и контроля свежести (P0.4) | Список сигналов и оповещений для пула и сборщика | нет | MIT | **Адаптировать**: только серверная часть; в приложении телеметрии без согласия нет |

Под вопросом: `security-threat-model` ([skills.sh](https://skills.sh/openai/skills/security-threat-model),
[исходник](https://github.com/openai/skills/blob/main/skills/.curated/security-threat-model/SKILL.md), Apache-2.0
в папке скилла) — модель угроз по репозиторию для цепочки «приложение → пул → nginx → воркер» и обновлений.
Полезен один раз перед P0.1; ставить глобально, а не в проект.

### 8.3. Что не нужно

| Что | Почему |
|---|---|
| React, Next.js, Vercel (`vercel-react-best-practices` и подобные) | В этом репозитории нет веб-фронтенда. Сайт действительно на Next.js 16 и React 19, но у него свой репозиторий и свои 13 скиллов; решать там |
| `wpf-best-practices` (jcurbelo), `dotnet-wpf-modern` (wshaddix), `avalonia` (markpitt) | В репозиториях нет лицензии: брать текст нельзя. По содержанию — общий MVVM, расходится с нашей общей Presentation |
| `development-plugin-for-avalonia` (wieslawsoltes) | Закреплён на Avalonia 11.3.12, версия 12 там отдельная «миграционная» ветка; у нас 12.1.4 |
| Скиллы Syncfusion WPF | Библиотека не используется |
| `data-quality-frameworks` (wshobson) | Great Expectations и dbt; у нас SQLite и Node, таких конвейеров нет |
| `statistical-analysis` (k-dense-ai) | Требует отдельной Python-среды с научным стеком и нацелен на отчёты в формате APA |
| `security-review` (getsentry) | Дублирует встроенный `security-review`; справочники в основном под веб-стек |
| `dotnet-webapi`, `optimizing-ef-core-queries`, `configuring-opentelemetry-dotnet` | Нет ASP.NET, EF Core и приёмника телеметрии |
| `fastapi` (официальный) | Полезен в репозитории API данных, не здесь |
| `competitive-intelligence`, `competitive-landscape`, скиллы «product research» | Про продажи, CRM и маркетплейсы; для сравнения продуктов хватает этого отчёта и `grilling` |
| `test-anti-patterns`, `msbuild-antipatterns` (dotnet/skills) | Качественные, но сейчас пробела нет: сборка без предупреждений с анализаторами, ревью тестов ведёт `hearthpulse-review` |

## 9. Источники

HSReplay, HDT (все обращения 2026-11-15):

- https://hsreplay.net/premium/ — навигация, описание Premium
- https://hsreplay.net/battlegrounds/tier7/ , https://hsreplay.net/battlegrounds/overlay/ , https://hsreplay.net/mulligan-gv2/ , https://hsreplay.net/arenasmith/
- https://hsreplay.net/meta/ , https://hsreplay.net/decks/ , https://hsreplay.net/decks/trending/ , https://hsreplay.net/cards/ , https://hsreplay.net/cards/discover-pools/ , https://hsreplay.net/cards/assemble-pools/ , https://hsreplay.net/arena/
- https://hsreplay.net/battlegrounds/heroes/ («Season 14»), `/comps/`, `/minions/`, `/trinkets/`, `/dark-gifts/`
- `/games/mine/`, `/decks/mine/`, `/collection/mine/`, `/battlegrounds/mine/` — редирект на вход
- https://static.hsreplay.net/static/webpack/i18n_en.7053569de09007243cbd.js — строки интерфейса (лимиты пробного доступа, фильтры, описания функций)
- https://github.com/HearthSim/Hearthstone-Deck-Tracker/blob/master/CHANGELOG.md — до v1.59.8 (2026-11-03)
- https://github.com/HearthSim/HDT-Releases/releases — v1.60.1 (2026-11-12), без текста изменений
- https://help.hearthsim.net/en/collections/1921599-hsreplay-net — справочный центр (ссылка со страницы Premium, не читал)

Hearthpulse: файлы и строки указаны в таблицах; живые адреса — в 4.2; сайт
https://github.com/Manacost-Labs/HeartPulse (PR #352, #353, #356), API данных
https://github.com/Manacost-Labs/api.kolodahearthstone.com .

skills.sh: https://skills.sh/ , поиск `https://www.skills.sh/api/search?q=…`; ссылки на скиллы — в 8.2.
