# Card backs, coins, hero skins, achievements, set completion and "Wrapped"

Core of the task "Collection, achievements and Wrapped" (2026-11-19): what is read from the game client for the
"Collection" and "Achievements" pages, how it is kept, when it is read, and the rules of the counts the pages and the
"Wrapped" page show. The pages themselves get their own documents with their packages (P2: the collection page;
P3: the achievements and "Wrapped" pages). The collection of cards and its read: [collection.md](collection.md). Read only, like every read of the client ([ADR 0006](adr/0006-client-read-only.md)).

## Clean-room statement

The layout below was found with the repository's own probe (`tools/Hearthpulse.Probe`, commands `walk`, `class`,
`cosmetics`) on a running client (ruRU, Windows, 2026-11-19). No code or notes of HDT, HearthMirror, Firestone or the
reference index were read.

## What is read

| What | Where in the client | Reader |
|---|---|---|
| Card backs the player owns, favourite card backs | NetCache `NetCacheCardBacks`: `<CardBacks>k__BackingField`, `<FavoriteCardBacks>k__BackingField`, each a `HashSet<int>` | `HearthstoneMemory.GetCosmetics` (`Memory/HearthstoneMemory.Cosmetics.cs`) |
| Coins the player owns, favourite coins | NetCache `NetCacheCoins`: `<Coins>k__BackingField`, `<FavoriteCoins>k__BackingField` | same |
| Card back catalog | static `GameDbf.CardBack` (`Dbf<CardBackDbfRecord>.m_records`): `m_ID`, `m_name`, `m_description` (`DbfLocValue`), `m_sortCategory`, `m_sortOrder`, `m_enabled`, `m_isRandomCardBack` | same |
| Coin catalog | `GameDbf.CosmeticCoin`: `m_ID`, `m_cardId` (the coin card's dbfId), `m_name`, `m_description`, `m_enabled` | same |
| Achievement catalog | `GameDbf.Achievement` (`m_ID`, `m_achievementSectionId`, `m_sortOrder`, `m_enabled`, `m_retired`, `m_name`, `m_description`, `m_achievementVisibility`, `m_quota`, `m_points`, `m_nextTierId`), `AchievementCategory` (`m_ID`, `m_name`, `m_icon`, `m_sortOrder`), `AchievementSubcategory` (`m_ID`, `m_achievementCategoryId`, `m_name`, `m_sortOrder`), `AchievementSection` (`m_ID`, `m_name`), `AchievementSectionItem` (`m_achievementSubcategoryId`, `m_achievementSectionId`, `m_sortOrder`) | `HearthstoneMemory.GetAchievements` (`Memory/HearthstoneMemory.Achievements.cs`) |
| The player's achievement states | service `Hearthstone.Progression.AchievementManager` → `m_playerState` (`PlayerState<T>`) → `m_playerState` (Blizzard `Map<int, PlayerAchievementState>`: `keySlots`, `valueSlots`, `touchedSlots`, `count`, `generation`); a state's `_AchievementId`, `_Status`, `_Progress`, `_CompletedDate` (unix seconds) | same |

Hero skins need no read: they are collectible `HERO` cards of the set `HERO_SKINS` and arrive in the collection's
stacks ([collection.md](collection.md)); `HeroSkins.Build` takes them from the card data and marks the owned ones.

The shared parts (`Memory/HearthstoneMemory.Dbf.cs`) follow the collection's rules: all or nothing; field names go
through `Field(...)` (respelled names are found again, never another field); each table's records are read one piece
each and must be of the first record's class; a localized value must be a `DbfLocValue` and its text
(`m_currentLocaleValue`, the **client's** language) readable and of sane length; a `HashSet<int>`'s live slots (index
below `_lastIndex`, hash code not negative) must each have hash code `value & 0x7FFFFFFF` and be as many as `_count`;
the state map's live entries (below `touchedSlots`) must be as many as `count`, each key the state's own id. The sets'
`_version` and the map's `generation` are looked at again after the read: a change meanwhile gives nothing. Each read
has its own budget (cosmetics 200 000 reads / 2 s, achievements 600 000 / 3 s) and is asked every 256 rows whether it
is still wanted (a game began). A null result says why in `LastCosmeticsMiss` / `LastAchievementsMiss` and in the log,
once per change of reason. The rules that turn rows into records are shared with `Probe recon-import`:
`CosmeticsCollection.FromClient`, `AchievementBook.FromClient`.

## Models

`Core/Collection/Cosmetics.cs`: `CosmeticsCollection` (catalog of card backs and coins, owned and favourite ids, read
time, `Confirmed`). The catalog keeps only enabled entries and drops the random card back; owned and favourite ids are
kept as the client holds them. Card back ids start at 0 (the "Classic" card back). `m_sortCategory` becomes
`CardBackCategory` by the declaration order of the client's enum (0 NONE … 13 EVENTS; an assumption the distribution of
the read values agrees with); any other number is `Unknown`.

`Core/Progression/AchievementBook.cs`: the book holds only achievements that are enabled, not retired and in a section
that a section item links to a subcategory of a known category (the game shows no other). A subcategory without a name
takes its category's name; a section without a name has no heading. `_Status` 1 is `InProgress`, 2, 3 and 4 are
`Completed` (COMPLETED, REWARD_GRANTED, REWARD_ACKED), anything else `NotStarted` (declaration order, an assumption the
distribution agrees with). `_CompletedDate` 0 is an unknown date; a date outside 2000..2100 makes the read null.
`m_achievementVisibility` other than 0 is a secret achievement.

### Counting rules

- **Chains.** `m_nextTierId` links tiers; a chain is one row whose current tier is the first not completed (the last
  when all are). Progress is the current tier's, at most its quota; a quota of 0 or less counts as done or not (0/1,
  1/1); the "completed" status wins over a progress below the quota (the client holds such records). A secret
  tier not completed is `Hidden`: no description, no progress, never "close to done".
- **Summary.** Points and "completed x of y" count tiers, in all and per category. The owner compares the sums with
  the game's journal on a screenshot (task card, step 5b); the numbers stay out of the repository.
- **Close to done:** unfinished, not hidden chains with a quota of at least 2 and some progress, by the share done.
  **Recently completed:** completed tiers with a date, newest first. **By period:** `CompletedBetween` (UTC bounds).
- **Gains** (`AchievementBook.WithGainsSince`, the "tracker"): every read of the client is compared with the book known
  before it (the saved file at start-up, else the last read): a chain that completed tiers gains their number
  (`Tiers`), one that moved on in the same tier gains the difference (`Progress`), both at the read's time; a chain that
  did not move on keeps its last gain. The gains are saved with the book, so they survive a restart. A read in which
  any chain has fewer completed tiers than in the known book is another account's or region's (one account never loses
  tiers; the file names no account): it gains nothing and the earlier gains are dropped
  (`AchievementBook.LostTiersSince`); the log says so in one line without data. The rule catches a switch only towards
  a book with fewer tiers somewhere: going back to an account that is ahead everywhere shows false gains once (see
  "Known limits").
- **Description** (`AchievementText`): `$q` is the quota (a number of the current culture), `|4(one,few,many)` the
  Russian plural form by the quota, `|4(one,other)` the English one, `<i>`/`</i>` are dropped, other markup stays.

### Set completion (`ExpansionCompletion`)

Per collectible set of the card data (not `HERO_SKINS`, `CORE_HIDDEN`): unique cards (owned in any finish / all),
playable copies (Σ min(copies, limit), limit 2, legendary 1), golden copies up to the limit (shown only once the split by
finish is verified, `CollectionFinish.GoldenVerified`), dust to finish the set (missing playable copies of craftable
cards at 40/100/400/1600, `DeckCost.CraftCost`), copies no dust buys (free cards, `CORE`, `EVENT`, `VANILLA`), an
estimate mark when a missing craftable card is also handed out (`howToEarn`), and the same by rarity. A card counts
only under its own card id: a printing in another set fills no slot here, as on the game's collection screen. Groups:
`Standard` = `CardPoolTables.StandardSets`, `Classic` = `VANILLA`, the rest `Wild`.

Names: `CardSets` with Loc keys `CardSet.<CODE>` (`Localization/Strings/{en,ru}/cardsets.json`; neither the card data nor
the client's set table has names). Release order: the sets' order of release, told apart by their cards' dbfIds where
needed; `CORE` is placed where the current year begins, `VANILLA` where the Classic format came out. An unknown code
reads as words ("Brand New Set") and is the newest; it is logged once.

### Wrapped (`Stats/Wrapped.cs`)

A month or a year in local time (Monday first) from the history: games without abandoned ones, time with at most three
hours a game, wins and losses outside Battlegrounds and Mercenaries, modes, favourite class, deck (`DeckName.Display`)
and Battlegrounds hero (base hero), best win streak, the longest game of at most three hours, games by weekday and
hour, Battlegrounds solo (firsts, top 4, average place), the previous period's totals, and achievements completed in
the period by their local completion date (an unknown date is in no period). A year agrees with `YearInReview` on the
same games (`WrappedTests`). No opponent names.

## Files

Both in the data folder, written through a temp file and a move, loaded without exceptions, never repaired (a damaged,
cut short, out of bounds or other-version file is "none yet" and the next read replaces it), never `Confirmed` when
loaded, the temp file removed after every save and at start-up. Letters of any language are written as they are
(UTF-8); the few characters JSON escapes for HTML (`<`, `>`, `&`, `'`, `+`) stay escaped (`<i>` is `\u003Ci\u003E` in
the file).

- `cosmetics.json` (`CosmeticsStore`, `version: 1`): `readAt`, `cardBacks` (`id`, `name`, `description`, `category`
  by name, `sortOrder`), `ownedCardBacks`, `favoriteCardBacks`, `coins` (`id`, `cardDbfId`, `name`, `description`),
  `ownedCoins`, `favoriteCoins`.
- `achievements.json` (`AchievementsStore`, `version: 1`): `readAt`, `categories`, `subcategories`, `sections`,
  `achievements` (`id`, `sectionId`, `sortOrder`, `name`, `description`, `hidden`, `quota`, `points`, `nextTierId`),
  `states` (`id`, `status` by name, `progress`, `completedAt` or null), `gains` (`headId`, `tiers`, `progress`, `at`).

No BattleTag, account name, account id or region is in either file.

## When it is read (`ClientReadWatch`, `CosmeticsWatch`, `AchievementsWatch`, `MemoryPoller`)

- Once when a session of the client starts: the client started, was found running at the app's start, began a new
  log folder, or went back to its login screen. The achievement catalog is read with the states once per session
  (`AchievementsWatch.SessionCatalog`); later reads take only the states and set them into the session's catalog.
- Again when a game ended (the scene left `GAMEPLAY`) or the collection or pack opening screen closed (rewards come
  after games and from the shop and the collection), at most once a minute.
- Never during a game or in the `GAMEPLAY` scene; a read under way stops when a game begins. A read that gave nothing
  is tried again after 10 s, 20 s… up to a minute; it never erases what is known.
- One long read per memory scan: the collection first, then the cosmetics, then the achievements; one not made waits
  for the next scan (3 s) and is no miss. Their time does not count as a slow scan.
- Scene lines replayed from the log at start-up are no visits.
- What is read is confirmed only until the client is closed, restarted or sent to its login screen; then it is shown
  as of its read time, and a result asked for in an earlier session is dropped.

`GameSession.Cosmetics` / `CosmeticsChanged` and `GameSession.Achievements` / `AchievementsChanged` carry this to the
hosts (raised off the UI thread; the host dispatches).

## Privacy

Local only, as the collection: nothing sends the card backs, coins or achievements. Checked by the static check of
names (`CollectionStoreTests.No_source_file_with_a_sending_call_names_the_collection`, extended with the new types,
files and events; the Collection files and `Core/Progression/` name no network type) and on the wire
(`CollectionPrivacyTests`: the session holds a card back and an achievement with marker names; no request of the usual
senders carries them). The log names a read by its counts only.

## What was checked

- `Probe cosmetics-check <folder>` on the running client (2026-11-19, ruRU): the app's reads and the generic walk
  differ in 0 records (card backs, coins, owned and favourite ids, categories, subcategories, sections, achievements,
  states, also for a read of states only); reads took 36 ms (cosmetics), 77 ms (achievements with the catalog) and
  10 ms (states only).
- `Probe recon-import <cosmetics-dump.json> <folder>` turns a file of `Probe cosmetics` into the two files by the same
  rules (for a copy of the data folder made for renders).
- Not checked against the game's screens yet: the card back groups (the enum numbers), the status numbers, the set
  names and the golden split; the owner compares them on screenshots.

## Known limits

- Gains after a switch of account or region: the files name no account, so a switch is seen only when some chain has
  fewer completed tiers than in the known book; switching back to an account that is ahead in every chain counts the
  difference as a gain of that read (known, accepted; the file format stays as it is).

## Tests

`CosmeticsMemoryTests`, `AchievementsMemoryTests` (reads on a hand-built image, `ClientImage`), `CosmeticsStoreTests`,
`AchievementsStoreTests`, `CosmeticsWatchTests`, `AchievementsWatchTests`, `ExpansionCompletionTests`,
`CardSetsTests`, `HeroSkinsTests`, `AchievementTextTests`, `AchievementBookTests`, `WrappedTests`, and the privacy checks
above. All made-up data.
