# Collection, "can build", dust

Roadmap `docs/roadmaps/2026-11-15-hsreplay-remediation.md` 2.3. This document covers the Core part (package L1): what
is read from the game client, how it is kept, and how a deck is priced; and the UI part (package L2, "In the
interface" below): the labels in the meta deck lists, the "can build" filter, the missing cards on the own deck page.
Card backs, coins, hero skins, achievements and set completion: [collection-cosmetics.md](collection-cosmetics.md).

## Clean-room statement

The class and field names below were obtained from the running game client with the repository's own tool
`tools/Hearthpulse.Probe` (`class netcache`, `class <Name>`, `collection`) on 2026-11-16: listing the objects of the
client's network cache, then the fields of the classes whose names say "collection" and "dust". The code of HDT,
HearthMirror and Firestone was not read, and nothing was taken from the `reference/` folder. Reading is read-only
(ADR [0006](adr/0006-client-read-only.md)): nothing is written to the game's memory and no input is sent to the game.

## What the client holds (reconnaissance, 2026-11-16)

Client: Hearthstone build 255790 (`BuildNumber` in Power.log), Unity 6000.3.11f1, Windows.

Both objects sit in the network cache: service `NetCache` of the service locator, field `m_netCache` (a map whose
values are the cache objects, `HearthstoneMemory.FindNetCacheObject`).

| Class (nested in `NetCache`) | Field | Offset | Mono type | Meaning |
|---|---|---|---|---|
| `NetCacheCollection` | `<Stacks>k__BackingField` | 0x10 | 0x15 generic instance: `List<CardStack>` (`_items`, `_size`) | one stack per owned card and finish |
| | `TotalCardsOwned` | 0x20 | 0x08 int | equalled the sum of all stack counts at the read; not read by the app |
| | `CoreCardsUnlockedPerClass` | 0x18 | 0x15 generic instance | not read |
| `CardStack` | `<Def>k__BackingField` | 0x10 | 0x12 class `CardDefinition` | the card and its finish |
| | `<Date>k__BackingField` | 0x18 | 0x0A long | not read |
| | `<Count>k__BackingField` | 0x20 | 0x08 int | copies in the stack |
| | `<NumSeen>k__BackingField` | 0x24 | 0x08 int | not read |
| `CardDefinition` | `<Name>k__BackingField` | 0x10 | 0x0E string | the card id (`TOY_100`), the HearthstoneJSON `id` |
| | `<Premium>k__BackingField` | 0x18 | 0x11 value type, enum `TAG_PREMIUM` (int) | the finish |
| `NetCacheArcaneDustBalance` | `<Balance>k__BackingField` | 0x10 | 0x0A long | dust the player can spend |

Walk: `NetCache` service → `m_netCache` → the value of class `NetCacheCollection` → `Stacks` → for each stack `Def.Name`,
`Def.Premium`, `Count`; and the value of class `NetCacheArcaneDustBalance` → `Balance`.

Other facts of the read: a collection of several thousand stacks was read in about 50 ms. The Core set is in the
stacks like any other card (ids starting with `CORE_`). The scene of the collection screen is `COLLECTIONMANAGER`
(the client's enum `SceneMgr.Mode`, and `LoadingScreen.OnSceneLoaded() ... currMode=COLLECTIONMANAGER` in the
developer's kept logs); pack opening is `PACKOPENING` and the login screen `LOGIN` (kept logs).

### The finish is an assumption

`TAG_PREMIUM` declares `NORMAL`, `GOLDEN`, `DIAMOND`, `SIGNATURE`, `MAX` in this order. The numbers of enum constants
are not in readable memory. The stacks held four values, 0 to 3, 0 by far the most common and 2 the rarest, which
fits the declared order, and that is all the evidence there is. The reader counts 0 as plain, 1 as golden and every
other value as "other". **This split was not compared with the game's screen and is not an established fact**: a
view must not show "N golden" until the owner has checked it. Only `OwnedCopies.Total` is used by anything (a copy
of any finish fills a deck slot), so the deck cost does not depend on the assumption.

### Checked and not checked

- Checked on the live client: `Probe collection` reads the collection through `HearthstoneMemory.GetCollection`, walks
  the same stacks a second time field by field through the generic object reader, and compares: no card differed,
  the dust was the same (twice, on 2026-11-16).
- **Not checked**: the numbers against the game's own screen (the dust counter, the copies of a few cards in the
  collection). The session that did the reconnaissance cannot see the screen; the owner checks this.
- Not checked: whether the stacks include cards the account only borrows (a loaned or trial deck, cards of an
  event): if they do, they count as owned; whether `TotalCardsOwned` stays equal to the sum after crafting (the
  reader does not use it); a read during a craft or a pack opening on the live client (covered by a test on a
  built image only); an account without diamond or signature cards; whether a stack stays with a count of zero
  after disenchanting; the Battlegrounds and Mercenaries collections (other classes, not read).

## Reading (`Core/Memory/HearthstoneMemory.Collection.cs`)

`HearthstoneMemory.GetCollection(cancelled)` returns a `CardCollection` or null. Windows only, like every memory
read; on macOS and Linux it is always null (`HearthstoneMemory.IsSupported`).

Null, never a part of a collection, when: either cache object is missing (the client is still signing in), the list is
empty, a stack is null or of another class than the first, a card definition is null or of another class, a count
or the copies of one card are out of bounds, a card id is empty or holds anything but Latin letters, digits and `_`,
a finish value is outside 0..32, the dust cannot be read (it is never taken as 0) or is out of bounds, a read fails,
the read budget runs out, the read is no longer wanted (a game began, see below), every stack is empty, or the
list's size, its array or the dust changed while it was read. A stack with a count of zero is skipped. A collection
with holes would call owned cards missing, which is worse than no answer.

Bounds, the same for the memory read and the saved file (`CollectionLimits`): 100 000 stacks, 100 000 different cards,
10 000 copies of one card (each finish and all together), 1 000 000 000 dust, a card id of at most 64 characters.

The reason for a null goes to `hearthpulse.log` once per change of reason ("Collection not read: …"); the log never
holds the collection itself, only the number of cards of a good read.

Field names go through the same lookup as the other reads (`HearthstoneMemory.Field`): a field respelled by a game
update (`m_stacks`, `_stacks`) is still found; a renamed class is not, and the read gives null.

Cost: the stack pointers in one read, then per stack one read of the stack, one of its definition and two of the card
id; the read has its own budget (600 000 reads, 3 s) because the usual scan budget would cut a collection short.

## Model (`Core/Collection/`)

- `CardCollection(Cards, Dust, ReadAt)` with `Confirmed`: owned copies by card id, the dust balance, the UTC time of
  the read, and whether it was read from the client that is running now (below). `OwnedCopies(Normal, Golden, Other)`,
  `Total`; `Owned(cardId)` gives zeroes for a card that is not owned. `Dust` is always a value that was read: a
  balance that could not be read gives no collection at all. (The record is not called `Collection`: that would be
  the name of its own namespace and of a .NET type.)
- `CollectionStore(path)`: `collection.json` in the data folder (`AppPaths.Collection`). `Load()` never throws: a file
  that is missing, cut short, not JSON, of another `version`, out of the bounds above, empty, or with a key that is
  not a card id (a view may put it into a file name or an address) is "no collection"; the file is left as it is and
  the next read of the client replaces it. What it loads is never `Confirmed`. `Save` writes `collection.json.tmp`
  beside it and renames it; the temp file is removed after every save, failed or not, and at start-up (one left by a
  killed process). Format (version 1):
  `{"version":1,"readAt":"2026-11-16T12:00:00.0000000Z","dust":1200,"cards":{"TOY_100":[1,1,0]}}`, the three numbers
  being the copies as the reader split them. No name, BattleTag, account id or region is in it: the cache objects
  read hold none, and none is added.
- `DeckCost.For(deck, collection, cards)`: a pure function, for a deck as copies by card id or a decoded deck code
  (`DeckDefinition`).

### Confirmed or not (`CardCollection.Confirmed`)

Every `CardCollection` has `ReadAt`, the UTC time its numbers were read from a client (kept in the file, so a loaded
collection has the time of the read that wrote it), and `Confirmed`, which says where it stands now:
`Confirmed == true` means read from the running client, in that client's current run; `false` means anything else,
that is loaded from the file, or read from a client run that has ended. The two cases of `false` are not told
apart by a field of their own: both are "numbers as of `ReadAt`, not vouched for now".

The file does not say whose collection it holds, and the app stores no account identifier to tell.
So a collection is **confirmed** only from a good read of the running client until that client's session ends:

- loaded from the file: not confirmed;
- read from the running client: confirmed;
- the client was closed, started again (the process, or a new log folder), or went to its login screen (`LOGIN`, where
  another account may sign in): not confirmed again, `CollectionChanged` is raised, and a new read is asked for;
- a read that gives nothing changes nothing: the earlier numbers stay, unconfirmed, however long reads keep failing;
- a result that was asked for in an earlier session of the client is dropped.

A view shows an unconfirmed collection as "as of `ReadAt`", not as the present state. Known limit: a switch of
account that the logs do not show as a new client run or a `LOGIN` scene would not be noticed.

### The rule of `DeckCost`

One state per deck (`DeckCost.State`); a view shows it and nothing else decides:

| State | Meaning | `DustNeeded` | `DustShort` |
|---|---|---|---|
| `Unknown` | cannot tell; `Gap` says why: `NoCollection`, `NoCardData`, `EmptyDeck` (no deck, no cards, a count of zero or less), `UnknownCard` (a card or DBF id the card data does not know), `UnknownRarity` (a missing card has no known crafting rarity), `UnreadableSideboard` | null | null |
| `Owned` | every card is in the collection | 0 | 0 |
| `CraftableNow` | cards are missing, all can be crafted, the dust at hand pays | the sum | 0 |
| `NeedsDust` | cards are missing, all can be crafted, the dust is not enough | the sum | the difference |
| `NotCraftable` | a missing card cannot be crafted | null | null |

`DustForCraftable` is the dust for the missing cards that can be crafted: equal to `DustNeeded` when that is known,
and with `NotCraftable` the cost of the rest, which completes nothing. `Unknown` has no missing cards and no dust
numbers: it is never "owned" and never "0 dust". `For` takes a null collection, deck or card data and never throws.
`Estimated` says the answer leans on a guess (below); `Confirmed` repeats the collection's.

- A copy of **any finish** covers a slot.
- A deck needs at most **two copies** of a card and **one of a legendary**; a list that asks for more is counted up
  to that limit.
- Crafting a plain copy costs **40 / 100 / 400 / 1600** dust for a common / rare / epic / legendary.
- A card **cannot be crafted** (`MissingCard.DustEach == null`) when, by the card data (`CardDb`, HearthstoneJSON
  `collectible`, `rarity`, `set`), no printing of it can: it is not `collectible`; its rarity is `FREE`
  (the free basic cards); or its set is `CORE` or `CORE_HIDDEN` (the year's
  Core set and its past rotations), `HERO_SKINS`, `EVENT` (event rewards) or `VANILLA` (the Classic-format mirror of
  the old cards).
- **Maybe not craftable now** (`MissingCard.CraftUncertain`, an estimate): the card has a price by the rule above,
  and the card data has a `howToEarn` text for it (the game hands it out: a free legendary of an expansion, a reward
  track card, an adventure card). Some of these the game does not let be crafted for a time, others it does;
  `howToEarn` does not tell which, so it is a mark on the estimate and never a ban. The price is kept.

### Printings of one card

A deck code names one printing of a card: each entry is the DBF id of that printing (`DeckString`), and the client
keeps a stack per printing (card id). Whether another printing fills the slot is decided **only by an explicit link
in the card data**, never by the name.

The link is HearthstoneJSON `countAsCopyOfDbfId` (checked in the real `cards.json` on 2026-11-16, not assumed): 304
cards have it, 297 of them collectible, each pointing at one other card; every pair has the same type, cost and
class, and no card that is pointed at points on. Examples from the file:

| Printing | `countAsCopyOfDbfId` → | Original |
|---|---|---|
| `CORE_AT_052` (dbf 129849, set `CORE`) | 2610 | `AT_052` (`TGT`) |
| `CORE_AT_123` (120524, `CORE`) | 2682 | `AT_123` (`TGT`) |
| `CORE_SCH_181` (129850, `CORE`) | 59252 | `SCH_181` (`SCHOLOMANCE`) |
| `CORE_RLK_814` (129166, `CORE`) | 84204 | `RLK_814` (`RETURN_OF_THE_LICH_KING`) |
| `CORE_MAW_033` (118043, `CORE_HIDDEN`) | 79780 | `MAW_033` (`REVENDRETH`) |

`CardDb.Printings(card)` joins such cards (collectible, same type, class and cost). For a joined group `DeckCost`
adds up the owned copies of all its printings, applies the limit of 1 or 2 to the group, and prices a missing copy
at the cheapest printing that can be crafted (the Core printing cannot, its original can); the card cannot be
crafted only when none of its printings can.

The link covers a part of the reprints only. Of the 289 collectible cards of the current `CORE` set, 105 have it; the
rest, and most older reprints, share a name with another collectible card without any link in the data (920 such
name groups; `AT_003` and `CORE_AT_003`, `AT_001` and `WON_341` are two). For those the rule stays **exact card id**,
and the result is marked instead of guessed: `MissingCard.MayOwnOtherPrinting` is set when the collection holds
another collectible card with the same name, type, class and cost (`CardDb.LookAlikes`), and `DeckCost.Estimated`
with it. Such a deck may be reported as missing a card the game would accept: a possible understatement, said so.

**Observation, not proof** (2026-11-16): an offline comparison against cached meta lists found missing-card
results where the collection held an unlinked look-alike. This establishes the limitation, not that the game
accepts those printings interchangeably. Personal collection totals and derived proportions are not recorded
here; the result depends on the collection and needs verification against the client before L2 can claim it.

### Sideboards

`DeckString.TryDecode` reads the optional sideboard tail of a code (E.T.C.'s band, the modules of a card built from
parts) into `DeckDefinition.Sideboard`, apart from `Cards` and `CardCount`. `DeckCost` counts a sideboard card that is
collectible like any other card of the deck (the same slot limit, shared with the main deck); one that is not
collectible comes with its owner and is not counted. A code whose sideboard flag is set and whose tail is cut short
is `SideboardUnread` and gives `Unknown` (`UnreadableSideboard`), as does an unsupported tail flag or negative
sideboard count. A deck given as copies by card id has no sideboard:
the caller adds those cards itself.

## When the collection is read (`CollectionWatch`, `GameSession.Collection.cs`, `MemoryPoller`)

The read runs on the memory thread (`MemoryPoller`), never on the log thread or a UI thread. The tracking thread only
hands out a numbered wish for a read (`MemoryRequest.CollectionTicket`) and takes the result:

- once per session of the client (also when the app starts with the client already running);
- when the collection screen opens (`currMode=COLLECTIONMANAGER`), and when it or the pack opening screen closes
  (what was crafted, disenchanted or opened there);
- not asked for while a game is in progress or its scene is loaded (`GAMEPLAY`): the wish waits for the end of the
  game. A read already under way is stopped when the newest request says a game is in progress (its first
  Power.log lines were read) or that the Arena draft screen opened since the read was asked for: the memory thread
  looks at the newest request before the read and every 256 stacks during it. It is the last read of a scan, so the
  values of the same scan are read before it, and its time does not count as "a slow scan" (which would slow the
  following scans down). It can still make other reads wait, for as long as it runs (about 50 ms as measured, at most
  its 3 s budget): the next scan starts only after it, so when the draft screen was already open at the time the
  read was asked for, the next look at the draft offer comes that much later; and when the `GAMEPLAY` scene came
  before the game's first Power.log lines, a read under way is not stopped and may run into the start of the game
  for up to those 3 s. It reads only the player's own collection, so nothing hidden is read in either case;
- at most one read that gave a collection per 30 seconds; a read that gave nothing (the client is signing in) is
  tried again after 10 s, 20 s, … up to once a minute;
- scene lines replayed from the log at the app's start are not visits;
- a result of an older wish (the player went on from the collection to open a pack) is taken, and the newer wish
  stands; a later read never gives way to an earlier one.

A collection that was read replaces the known one and is saved on a pool thread; a save that fails is logged and
tried again a minute later. A process killed during a save leaves the old file whole. At start-up the saved file is
loaded on a pool thread; a read of the client that came first wins over it.

`GameSession.CollectionChanged` carries no collection: a subscriber reads `GameSession.Collection`, the newest known
one, and so cannot end up with an older one than the last. It is raised when the file is loaded, when the client was
read, and when the collection stops being confirmed. A subscriber that throws is logged and does not stop tracking.

## Privacy

The collection is kept in `collection.json` on this machine, and no code in the repository sends it: it is not part
of a game record, a shared game, a source capture, a profile upload, a replay or a crash report, and there is no
switch for it. The app asks the player nothing about it and counts nothing about its use. What supports that
statement:

- reading the sources: the collection's types, file and reads are named only in its own files, in the session and
  memory-poller files that ask for the read, and in the files that show it (the view model partials
  `MainViewModel.Collection.cs`, `CollectionRows.cs`, `DeckDetailsViewModel.Collection.cs`, the hosts'
  `App.Collection.cs` / `AppHost.Collection.cs`, the render scenes);
- `CollectionStoreTests.No_source_file_with_a_sending_call_names_the_collection`, a **static check of names in source
  text**: a source file that contains an HTTP sending call, a crash report, a named pipe or a web socket must not
  contain the collection's names. It does not see a sender without those words, a payload built in another file, or
  a path through a view model, and it is not a check of what goes over the wire. The hosts subscribe to the
  collection in a file of their own for this check (`App.xaml.cs` and `AppHost.cs` hold sending words);
- `CollectionPrivacyTests`, a **check of what goes over the wire** (2026-11-16, with the UI package).
  `AppServices` is built as a host builds it, on an `HttpMessageHandler` that records every request and answers
  "unavailable". The session holds a collection with a marker card id and a marker dust balance (saved to its file,
  loaded at start, then read as from the client), and the main view model shows it (labels, the filter, the order,
  the deck page). Then these senders run, each is checked to have sent a request, and no request has a marker in its
  address, its headers or its body (also percent-encoded, inside gzip, and inside base64 of gzip):

  | Sender | How it is run | Requests seen |
  |---|---|---|
  | Constructed and Battlegrounds stats, archetype names, card pool, update check, usage limits | the loaders on the services' client | `GET` to the data API, GitHub, the site |
  | Game record | a finished game through the session's `GameFinished` handlers, then `GameStatsUploader.PassAsync` (a test pool address) | `POST <pool>/v1/events` |
  | Shared games | `PoolSharingHost` started, a Battlegrounds and a Constructed outcome | `POST <pool>/v1/games`, `/v1/constructed` |
  | Source capture | the services' `SourceCaptureHost`, a game's Power.log lines through the session, `SendPendingAsync` | `POST <pool>/v1/captures/chunks` |
  | Profile upload | `ProfileUploader` with a **made-up signed-in session** on the same client and history (the services' own uploader is signed out: a real sign-in is a device flow against the site) | `POST <site>/tracker/events/batch` |
  | Crash report | `CrashReporter.SendAsync` with an exception | `POST <site>/crash-reports` |

  The files those senders leave in the data folder (queues, spools, the crash folder) are searched for the markers
  too; only `collection.json` holds them. A second test proves the search itself finds a marker in each of those
  places.

  **What this proves, and what it does not.** For these **six flows** (the downloads, the game record, shared games,
  the source capture, the profile upload, the crash report) "the collection does not leave" is shown by behaviour.
  For everything else it rests on the first two points alone (reading the sources, the static check of names), and
  is **not** network proof:

  - the Twitch publisher, the replay upload and the replay share are not run (they need a signed-in account);
  - the account's own calls (`/me`, the sign-in), the mulligan guide's and the leaderboard's downloads, the card
    art downloads;
  - the profile upload and the crash report are run as the same classes on the same client, not as the instances
    a host owns (the services' own uploader is signed out; the reporter is built by the host);
  - the hosts' own wiring (`App.xaml.cs`, `AppHost.cs`) is not executed: the view model of the test is wired by the
    test as the hosts wire it;
  - every answer is 503, so a request a sender makes only after a successful first one is not seen (the source
    capture's `complete` call); a client made beside the `HttpClient` that was handed in would not be seen either;
  - `.db` files are left out of the search on disk; the markers are the card id and the dust balance, so a number
    derived from the collection (a deck's price, the count of cards) is not looked for;
  - anything a later change adds without adding it to the test.

  **The count of cards in the log.** `CollectionWatch` writes "Collection read from the client: N cards" to
  `hearthpulse.log`, and nothing else of the collection. A crash report is made from the exception alone
  (`CrashReport.Build`: the place, the exception's type, message and stack through `CrashReport.Scrub`; it reads no
  log), so that line does not travel with it. The wire test checks this on a made-up count: the line is in the log,
  and neither it nor the count is in the report as sent. Were a later change to attach the log's tail to a report,
  this line would go with it and that test would fail.

Card ids: the views show card names and never put a card id of the collection into an address or a file name (the
missing-card rows carry the id the **deck** names, from the card data; they load no art by it). Ids that come from
the collection are checked against `[A-Za-z0-9_]`, at most 64 characters, when the file is loaded and when the client
is read. Uploading the collection to the site is outside this version (roadmap 2.3): it would be a new data flow
with its own notice.

The texts the player reads about what the app reads from the game (the installer notices) do not name the
collection yet; they are updated by the coordinator before a release that carries this read.

## In the interface (package L2)

Both apps show the same, from shared view models (`Presentation/Main/MainViewModel.Collection.cs`,
`CollectionRows.cs`, `Main/Decks/DeckDetailsViewModel.Collection.cs`) and the twin dictionaries
`Main/CollectionResources.xaml` / `.axaml`; texts are the `Collection.*` keys (`Strings/{en,ru}/collection.json`).
The hosts hand the view model `GameSession.Collection` at start and on every `CollectionChanged`, on the UI thread
(`App.Collection.cs`, `AppHost.Collection.cs`).

**Where.** Standard and Wild, tab "Top decks": over the list the collection's status line, the switch "Can build"
and the order ("By win rate" / "By missing dust"); under each list its build label. The same label under the best
lists of the tier list's side card, with the status line under them. The own deck page: a block "Collection" under
the deck list with the label of the main version, its missing cards and the status line.

**The label** (`DeckBuildView`, made from `DeckCost` and nothing else):

| `DeckCost.State` | Label (en / ru) | Glyph |
|---|---|---|
| `Owned` | All cards owned / Все карты есть | check in a circle |
| `CraftableNow` | Craftable: N dust / Можно создать: N пыли | hammer |
| `NeedsDust` | N dust short / Не хватает N пыли (`DustShort`) | exclamation in a circle |
| `NotCraftable` | Can't be crafted / Нельзя создать | cross in a circle |
| `Unknown` | no label at all | |

The words and the glyph carry the meaning; no colour is used for it. `Estimated` adds the mark "estimate" / «оценка»
and the reason in the label's help text (another printing in the collection; a card the game hands out). The label
is a button: its accessible name is the label with its marks, its help text and tooltip the reason and the missing
cards, and pressing it opens them (WPF: a popup; Avalonia: a flyout). Opened, the list takes focus and is named for
a screen reader; Escape closes it and focus returns to the label (in WPF Enter and Space close it too); a new
collection or a new list, which replaces the row, closes it rather than leave the old numbers on screen. A missing
card shows its name ("2 × Name"), its price ("2 × 400 dust", or "can't be crafted") and what makes it an estimate.
Copies are counted as `OwnedCopies.Total`: the split by finish is not shown anywhere (`CollectionMarkupTests`).

**Three dust numbers, told apart.** The column "Dust" of the list is the price of the whole deck from nothing (it was
there before the collection; its head's tooltip now says so and points at the label). The label of a deck that
lacks dust names what is **lacking** (`DustShort`: "400 dust short"). Its reason puts all of it in one sentence:
"The missing cards cost 2,400 dust and you have 1,000: 1,400 short." The sum under the missing cards is the price of
the missing cards (`DustNeeded`: "Missing cards: 2,400 dust"); for `NotCraftable` it is never a price of the deck
("Dust can't complete this deck", or the cost of the craftable part named as that).

**Never a made-up zero.** A label is built only from numbers the deck cost gives: a state whose label needs a number
that is null (`CraftableNow` without `DustNeeded` or `DustAvailable`, `NeedsDust` without `DustShort`) gets no
label, like `Unknown`, and is ranked with the unknown in the order by dust. Core gives these numbers for these
states today; the views do not depend on it.

**Why a deck has no label** (the deck page says it under the block's title, by `DeckCost.Gap`): no deck code; card
data not loaded; a card the card data does not know; a missing card without a known rarity (so without a price); a
sideboard in the code that cannot be read; and a neutral "couldn't work out what this deck is missing" for anything
else.

**The WPF popup stands by itself.** It is made for each opening, has neither a parent nor a placement target (it is
put at the label's place on the screen and given the page's font and text colour), closes with its window, and is
emptied when it closes (content, template, focus entry, handlers). `CollectionPopupTests` holds this by walking what
a closed popup reaches: neither the view, the window nor the label, whichever way it was closed; the same walk does
find the window from a stock popup that was placed at a label.

### The closed window and UI Automation

`MainWindowLifetimeTests.A_closed_wpf_main_window_is_collected_while_its_view_model_lives_on` failed in a full run of
the Windows suite on this branch. What was found (2026-11-16, this machine, where something listens to UI
Automation: `AutomationPeer.ListenerExists` is true for focus, structure and property events):

- **Series.** 8 full runs on the commit before the interface (`237c5e12`): 0 failures. 8 full runs on `3bac37b4`
  with a hook that looks at the window after the test's five turns of the dispatcher: the closed window was still
  alive in 5 of 8. By the TRX order of each run those are exactly the 5 runs in which the test that opened the
  collection's popup ran **before** the lifetime test; in the 3 runs where it ran after, the window was collected.
- **Not a root that stays.** Left alone (more turns of the dispatcher and collections, no change to the test), the
  window went away after 60 to 68 seconds in 3 of the 5; in the other 2 the extra turns ended sooner than that.
- **The root** (`dotnet-dump`, `gcroot` of the window): ref-counted handles on `MS.Internal.Automation.ElementProxy`,
  each leading through an `ItemsControlItemAutomationPeer` to a list of the window (the rank filter's, in the dump
  read) and from it to the window. UI Automation holds them; no field of the app does.
- **Not the collection's code.** A fresh process that opens a stock context menu, a tool tip or an empty `Popup` and
  then runs the lifetime test's steps leaves the window alive every time (2 of 2 each); without a popup, or with
  only a window, or only a focus change, never (0 of 2 each). The same on `237c5e12`, which has no collection
  interface: 2 of 2 for each kind of popup, 0 of 2 without. So: after the first popup of a process, UI Automation
  keeps elements of later windows for about a minute after they close, on a machine with a listener. For the app
  that means a main window closed by a theme change or to the tray may stay in memory for about a minute while a
  screen reader or a similar tool runs; it is not kept for good, and tool tips have always done this.
- **What was wrong in the branch, and is fixed.** The popup was a child of the view. With that, the peer of its
  popup root led to the view and the window (first dump), and as a parentless popup placed at a label still to the
  window through the values it inherited (second dump): the window a popup had been opened in was reachable from
  the popup itself for as long as UI Automation held the popup's peer. That is the paragraph above.
- **What was wrong in the tests, and is fixed.** A test that opens a real popup changes the process for the tests
  after it. Such tests are now in one collection that runs last (`RealPopups`, `RealPopupsLast` in
  `Hearthpulse.Presentation.Tests`); the lifetime test is not changed.

**A saved collection** (`Confirmed == false`; called "saved", never "from a file"). Rule of the legal review: by a
collection nobody vouches for now, the app does not state "you can build this" as a fact.

- Every label is dimmed (`DeckBuildView.SavedOpacity`) and carries the mark "by a saved collection" / «по сохранённой
  коллекции», in its text, its accessible name and its help text.
- **"Can build" is off limits** (disabled, with the reason in a line beside it and in its tooltip), not "on with a
  note". Decision of this package: a filter hides lists, and hiding by saved numbers states "you cannot build the
  rest" about every hidden list at once, with nowhere to put a mark on what is not shown; the file does not even say
  whose collection it holds. The labels stay, each with its mark. The player's choice is kept and applies again
  once the collection is confirmed. Both UIs do the same (`CollectionContractTests`).
- **The order by missing dust stays, and decides nothing about which lists are shown.** The page shows at most sixty
  lists. By a confirmed collection the order picks those sixty from every list (the cheapest for this player). By a
  saved one the win rate picks the sixty, as without any order, and the saved numbers only arrange them: no list is
  left off the page, or put on it, by numbers nobody vouches for now (`CollectionViewTests`, with 61 lists).

**The status line** (`MainViewModel.CollectionStatus`; the date of `ReadAt` is always in it when there is a collection):

| State | Text (ru) |
|---|---|
| confirmed | «Коллекция прочитана из игры сегодня в 14:05.» (another day: the date and the time) |
| saved, client not running | «Сохранённая коллекция от <дата>: запустите Hearthstone, чтобы обновить.» |
| saved, client running | «Сохранённая коллекция от <дата>: прочитать её из игры пока не удалось. Откройте раздел «Коллекция» в Hearthstone.» |
| none, client not running | «Коллекции пока нет. Запустите Hearthstone: приложение прочитает её из игры.» |
| none, client running (not read yet, or the read fails) | «Прочитать коллекцию из игры пока не удалось. Откройте раздел «Коллекция» в Hearthstone: чтение повторится.» |
| a system without the read (`!HearthstoneMemory.IsSupported`) | «Коллекция читается из клиента игры на Windows. На этой системе меток «могу собрать» нет.» |
| none, reading the client turned off (`AppSettings.ReadGameMemory`) | «Коллекция читается из клиента игры, а чтение выключено (Настройки → Данные): меток «могу собрать» нет.» |
| saved, reading the client turned off | «Сохранённая коллекция от <дата>. Чтение клиента игры выключено: включите его, чтобы обновить (Настройки → Данные).» |

The view model cannot tell "not read yet" from "the read fails": the session gives no reason, so one text covers
both, and it names the step that asks for a new read (opening the collection screen).

**Pricing.** A page's lists are priced when its stats, the collection or the card data change: up to 80 lists at
once, more on a pool thread, coming back on the caller's context (the UI thread: both hosts set the collection and
the stats there); a newer change drops an older result. While a long list is being priced there are no labels yet,
and with "can build" on the card says "Working out which of these decks you can build…" in place of the rows it
waits for. A pricing that fails is logged, ends the wait and shows a line with "Check again"
(`MainViewModel.RetryDeckCosts`); the same inputs are not priced again by themselves. "Can build" keeps `Owned` and
`CraftableNow`. The order by missing dust: what needs no more dust than the player has (by its cost), then by the
dust lacking, then the lists no dust completes, then the unknown.

**The compact row (Avalonia, below 900).** A top-deck row gives the deck's name the room of the games and dust
columns, which hide with their heads, and its Copy button moves to the second line beside the label; the Copy
column of the head and of the rows is one shared width. Before, the name was squeezed to nothing and the button cut
(`CollectionLayoutTests`). WPF has no compact layout.

**Render.** `HEARTHPULSE_RENDER_ONLY=collection` (`DevRender.Collection.cs`, `OffscreenRenderer.Collection.cs`) shows
all of it on `Core/Constructed/CollectionScene.cs`: a made-up collection and made-up lists of real card names. The
player's own collection is never put into a render's data copy. `main-collection-missing` is the opened list of a
label (the popup or flyout itself). The deck frames are taken on the real deck page: the scene opens the made-up
deck through `MainViewModel.OpenSampleDeck`, checks that the page and its collection block are on screen, and fails
the render otherwise. (Until the second round those frames showed the tier list: the scene sets the stats before
every frame, new stats rebuild the deck list from the history, the history has no such deck, and the page closed.
The made-up deck now stays open through a rebuild until its page is closed.)

**Known limits.** The side card of the tier list is as narrow as before: the name of a best list is squeezed and its
Copy button cut there in both UIs (not changed by this package). A screen reader's walk through the page was not
done by hand; names, help texts, tab stops, focus and the 24 px targets are held by tests.

## The Core interface the views use

- `GameSession.Collection` (`AppServices.Session.Collection`): `CardCollection?`, safe from any thread. Show
  `Confirmed == false` as "as of `ReadAt`".
- `GameSession.CollectionChanged`: no argument, raised off the UI thread; dispatch, then read `Collection`.
- `DeckCost.For(...)` → `State` (and `Gap`), `DustNeeded`, `DustShort`, `DustForCraftable`, `DustAvailable`, `Missing`
  (`CardId`, `Copies`, `DustEach`, `Dust`, `Craftable`, `CraftUncertain`, `MayOwnOtherPrinting`), `Uncraftable`,
  `Estimated`, `Confirmed`. A "can build" filter takes `Owned` (and, if wanted, `CraftableNow`) and nothing else;
  `Unknown` is its own empty state.
- Do not show the split of copies by finish (above). A card id from the collection is checked on load and on read
  (`[A-Za-z0-9_]`, at most 64) before a view puts it into a path or an address.
- `HearthstoneMemory.IsSupported` for the empty state on macOS and Linux ("the collection is read from the client on
  Windows").

## Tests

`CollectionMemoryTests` (a hand-built memory image: the stacks come back as the same numbers; a damaged structure,
an unreadable balance, a list or balance that changes during the read, a spent budget, a read no longer wanted and
counts past the bounds give null), `DeckCostTests` (the five states, decks priced by hand, printings, sideboards),
`CollectionStoreTests` (round trip, damaged and out-of-bounds files, version, temp files, when a read is wanted,
confirmed and stale results, a failed save, the static check of sending files). No recorded memory image of a real
collection is in the repository.

The interface: `CollectionViewTests` (the five states, estimates, a saved collection, none, a system without the
read, the filter, the order and its sixty, a new collection, the session's event through a host-style dispatch, a
reason per gap, no label from a missing number, a long list priced off the calling thread and applied on the
caller's context with a superseded pricing dropped, a failed pricing and its retry, one build per change, the
made-up deck staying open, the deck page), `CollectionLayoutTests` (Avalonia, headless: 420 and 1280 wide, ru and
en, the longest texts whole; names, help texts, tab stops, the 24 px target; the real deck page; the opened flyout
with focus in and back; the compact row; the pricing lines), `CollectionMarkupTests` (the two markups side by
side), `CollectionPrivacyTests` (above), and on Windows `Hearthpulse.Presentation.Tests/CollectionContractTests`
(both view models the same in every state; the WPF page, its popup with focus in and back, the real deck page, and
a closed popup with no remaining references to its owner (reference traversal, not a GC collection proof)). The fixture
(`CollectionFixture`) is made up: no real collection or dust balance is in the tests or in this document.

Integration review limits (2026-11-16): theme folders and theme.txt alone did not establish Sands or every other theme. Some supplied PNGs were identical. Contrast across all themes remains unverified. The reference-traversal regression does not prove collection of an owner window by GC after a real popup. Six additional full runs on the corrected author head were reported green by the author; the coordinator independently reruns the merged head.
