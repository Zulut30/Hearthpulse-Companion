# Card pool panel ("Discover pool")

For a card that Discovers, creates random cards or Excavates, the overlay shows the pool the cards come from in a
compact panel (300 units wide): the card's name, the pool's size and classes, the most played pool cards as tracker rows
(every card of a pool of 12 or fewer) and an "All cards · N" button that opens the pool browser with every card. A card
in hand shows it at the panel's own spot, only while Ctrl is held by default; the player's own Discover shows it beside
the options by itself. Everything is built from game data and card text; no code of other trackers was used. The idea
of a pool panel with a list of options is known from HDT's Outfinder; the layout here follows the spec this project was
given.

Code: `src/Hearthpulse.Core/CardPool/` (catalog, parser, engine, stats, tracker), `Tracking/GameSession.CardPool.cs`,
panels `Overlay/CardPoolPanel.*` in both UIs. Generated table: `CardPoolTables.g.cs` from
`tools/cardpool/gen_pool_tables.py`.

## Data

**Main source: Hearthpulse API** `GET /api/v1/constructed-cards?format=wild&per_page=200&page=N` (33 pages,
~3 MB gzipped, 6501 cards on 2026-10-23). Every collectible Standard/Wild card; `formats[].slug` says whether it is in
Standard, so one Wild download covers both formats (`format=standard` gives the same 1322 cards). Fields used:

| Field | Use |
|---|---|
| `card_id`, `dbf` | identity, join with `CardDb` |
| `card_type.slug` | MINION, SPELL, WEAPON, LOCATION, HERO |
| `class`, `multi_class[]` (CLASS ids 1 DK, 2 Druid, 3 Hunter, 4 Mage, 5 Paladin, 6 Priest, 7 Rogue, 8 Shaman, 9 Warlock, 10 Warrior, 12 Neutral, 14 DH) | class filters; multi-class cards have `class: null` |
| `minion_type` | tribe (one value only, see below) |
| `spell_school` | school filters |
| `mechanics[]`, `referenced_tags[]` | keyword filters and mechanic percentages (identical to HearthstoneJSON) |
| `mana_cost`, `attack`, `health`, `durability`, `rarity`, `card_set` | filters and histograms |
| `name.en`/`name.ru`, `text.en` | names; English text for generator parsing and text rules |

Not in the API and patched from `CardDb` (HearthstoneJSON): **dual minion types** - `minion_type` keeps one of two
types for 59 Standard minions (e.g. Beast + Dragon), so `CardPoolCatalog.WithRaces` takes HearthstoneJSON `races`.

Cached as a compact list in `%APPDATA%\Hearthpulse\cache\api\card-pool.json`, refreshed after 1 day; a stale cache is
used while the API is down. **Fallback**: HearthstoneJSON `cards.collectible.json` (enUS), Standard decided by
`CardPoolTables.StandardSets`.

**Pools first from Hearthstone Wiki** (2026-10-23): `GET /api/v1/constructed-cards/{card_id}/wiki` ->
`data.generated_card_pools[] {pool, cards[{card_id}] | card_ids[], query_url}` (`WikiPools.Parse`, cache
`cache/api/card-wiki-{id}.json`, 6 h). `WikiPoolSource` loads a card on demand in the background (the first hover, play
or choice; the deck's generators are asked for when the deck is known) and the overlay rebuilds the pool when it
arrives. Order in `CardPoolEngine.Build`: wiki pools -> the card text -> "Pool not computed" (no panel, see the
exception table). Wiki pools are Standard
queries for one class (`query_url` `options_format`, `options_heroClasses`): not used in Wild or for another class (a
stolen generator). Several pools (Scion of Insight: Blood 29, Frost 21, Unholy 33) become sections: chips in the
browser, where a click shows one section's cards (the compact panel has none since 2026-11-26, P-6); the stats count
every card once. The page is read now and then, so a wiki list that the card text finds whole and more of is older
(Hematurge: 23 of 29 on 2026-10-12, the newest set missing): the text pool is shown. The compact panel prints neither
the source nor the parsed English phrase: its title is the generator card's name (in the player's language) and the
line under it "51 cards · any class" (since 2026-11-26 without the format: Standard or Wild is the format the player is
in; "from the past" stays, it is part of the pool).

The panel's highlighted card is the pool card played in the most meta-deck games (`ConstructedMeta.Decks`, sum of
`Games` over decks that run it). No per-card win rate is public on the API (`/v1/meta-intelligence/cards` needs a
token), so there is no "strongest card" rule; without meta data no card is highlighted.

### Proposals for the API (server not changed)

1. A light list: `GET /api/v1/constructed-cards?...&fields=card_id,dbf,formats,name,text,card_type,class,multi_class,`
   `minion_types,spell_school,mechanics,referenced_tags,mana_cost,attack,health,durability,rarity,card_set` - the
   current items carry `tile_layout` and image URLs, ~3.7 KB per card for ~0.3 KB the client needs.
2. `minion_types[]` with every type (today `minion_type` drops the second of dual-type minions) and `rune_cost`
   {blood, frost, unholy} (the app takes Death Knight rune costs from HearthstoneJSON `runeCost` meanwhile:
   `CardPoolCatalog.WithRaces` copies them onto the API cards).
3. Optional pool endpoint: `GET /api/v1/card-pool?format=standard&type=MINION&class=MAGE&include_neutral=1&race=BEAST`
   `&school=&mechanic=BATTLECRY&cost=3&cost_min=&cost_max=&rarity=&past=1` returning card ids and the same stats the
   panel draws (histograms, mechanic shares). The app does not need it - the full list is small - but other clients
   (site, bot) could reuse the rules.
4. `updated_since` already exists on `constructed-cards`; the app could refresh incrementally with it.

## Which cards are generators

`PoolTextParser` reads the normalised English text (`PoolText.Normalize`: no markup, `[x]`, `$`, `#`, `@`, `&`).
Collectible generators bring their text with the catalog; non-collectible ones (hero powers, tokens, Dark Gifts)
are in `CardPoolTables.GeneratorTexts` (348 texts; solo adventure, Book of Heroes and boss cards are left out by id, and
Taverns of Time by set, since their pools are the adventure's own). Two anchors:

- **Discover**: `Discover [a|an|two|any|N] <descriptor>` - not after "you", "you've", "to", "Quest:" (those *count*
  Discovers), not "Discovered".
- **random**: `<verb> ... [count] random <descriptor>` where the nearest verb before "random" in the clause is a
  generation verb (Get, Add, Summon, Cast, Fill, Transform, Shuffle, Equip, Replace, Look at, Become). A nearer other
  verb (deal, destroy, give, return, draw, discard, trigger...) or a target word right after "random" (enemy,
  friendly, player's, adjacent, character...) means it is not a generator.

Descriptor = adjectives, a noun, then postfix clauses; every word must be known, or the card is **Unrecognized**: no
panel, and `hearthpulse.log` gets `Card pool: <name> (<id>) not understood: <rest of text> | <full text>`.

| Part | Accepted | Filter |
|---|---|---|
| filler | different, Temporary, new, random, additional, other, another | - |
| owner | friendly, enemy, your, their, your opponent's | not a pool (NotPool) |
| class | Death Knight ... Warrior, Neutral (`Mage spells`) | named classes |
| rarity | Legendary, Epic, Rare, Common | rarity |
| cost | `3-Cost`, `6, 4, and 2-Cost`, `10 and 1-Cost` | exact costs (a list = union) |
| parity | `odd-Attack`, `even-Cost`... | parity of the stat |
| school | Arcane, Fel, Fire, Frost, Holy, Nature, Shadow before "spell(s)"; `Holy and Shadow spell` | school (union) |
| keyword | `PoolKeywords.All` (Battlecry, Deathrattle, Taunt, Rush, Divine Shield, Choose One, Combo, Stealth...); lists `Combo, Battlecry, or Stealth` | one of the tags in mechanics (Imbue/Herald also in referencedTags) |
| tribe | Beast, Demon, Dragon, Elemental, Mech, Murloc, Pirate, Naga, Undead, Totem, Draenei, Quilboar (+plural); also a noun | minion of that type (or "ALL") |
| noun | card(s), minion(s), spell(s)/Spells, weapon(s), location(s), Secret(s), Hero card(s) | card type; Secret = spell + SECRET |
| postfix | `that costs (N) or more/less`, `that cost (N)`, `costing (N) or less` | cost bounds |
| postfix | `from another class` / `from a different class` / `from other classes` | class cards not of your class (no neutrals) |
| postfix | `from any class` (any case), `from your class`, `from your opponent's class` | scope |
| postfix | `from the past` | cards not in Standard (Wild-only sets), in any format |
| postfix | `with Taunt` (any keyword), `with N or more Attack`, `with multiple minion types` | keyword / attack / 2+ types |
| postfix | `with a Dark Gift` | ignored (a bonus, not a filter) |
| postfix | `in/from your hand/deck`, `that died`, `you've ...`, `copy of` | NotPool (not from the catalog) |
| end | end of text, `. , ; : ! ? ( )`, and, to, into, for, then, instead, if, when, after, until, it, they, at, on, plus, each, as, but | - |

### Class and format rules

- **Game type** (`PoolContext.SupportedGameTypes`): Ranked, Casual and Friendly only. Arena, Tavern Brawl, Practice and
  solo adventures (`GT_VS_AI`), Duels and other modes have pools of their own: no panel, and game memory is not read.
- **Format**: `GameState.FormatType` FT_STANDARD -> Standard cards, FT_WILD -> all cards. Classic and Twist have no
  known pool: no panel.
- **Discover without a class**: your class + neutral cards (your class = local hero's card class; outside a game the
  generator's own class). Class cards are offered more often than neutrals in the game; the panel lists the pool
  unweighted.
- **Random without a class**: every class and neutral.
- Named classes (`Mage spells`) use the card's `multi_class` too: a Mage/Priest card is in a Mage pool.
- "from another class": class cards whose classes do not include yours; neutral cards excluded.

### Exception table

`tools/cardpool/gen_pool_tables.py` `OVERRIDES` maps text patterns to actions for every Constructed card carrying
them (ids written to `CardPoolTables.Overrides`):

- `skip` - generates, but the catalog cannot answer: cost from the game state (`with Cost equal to`, `of that Cost`,
  `playable spell`), pool from another card (`of the same Cost/Rarity`), named token groups (Animal Companions,
  Dormant Dreadseeds, Dream cards, Leylines, Shatter, Masks, Rewind cards, Wild Gods, Old Gods, contraband, Mimiron's
  tech card). Status `Uncomputed`: no panel (since 2026-11-26, P-8: the player could do nothing with the reason, and
  such a card in hand is no reason to read the card under the mouse); logged once as `pool not computed (no panel)`.
  `CardPoolTracker.Computed` drops these views for the Discover, the request and the hover alike.
  Rune pools ("Discover a Blood Rune card", "a Blood, Frost or Unholy card") are read by the parser since
  2026-10-23: `PoolQuery.Runes`, rune costs from HearthstoneJSON, any class (only Death Knight cards have runes).
- `sub` / `text` - parse a corrected text (`2-Cost minion that is Dormant for 2 turns` -> `2-Cost minion`,
  `into random Druid ones` -> `random Druid cards`, Fyrakk's `Fire spells at random enemies` -> `random Fire spells`).

Regenerate after each patch:

```
python3 -I tools/cardpool/gen_pool_tables.py cards.enUS.json src/Hearthpulse.Core/CardPool/CardPoolTables.g.cs
```

## Mechanic percentages

Share of pool cards per mechanic, highest first (`PoolStats.Mechanics`, at most `PoolStats.MaxMechanics` = 8). The
compact panel shows no shares since 2026-11-26 (P-2: a share of dozens of cards does not tell what will come); the same
rules decide the browser's filter chips (`CardPoolView.Filters`). A mechanic counts when it is in the card's
`mechanics` or `referencedTags` (so "Give a minion Taunt" counts as Taunt). Shown mechanics and labels:
`PoolKeywords.Shown`; technical tags (TRIGGER_VISUAL, AURA, ImmuneToSpellpower...) are not shown. Mechanics every pool
card has (the filter itself, e.g. Battlecry in a Battlecry pool) and those no card has are left out.

Text rules (`PoolStats`, over the English text):

| Tile | Rule |
|---|---|
| Card Draw | "draw(s)" followed by a count or "cards" (Draw a card, draw 2 Undead, Each player draws 2 cards); not triggers on someone else's draw (after "you", "opponent", "they", "enemy": Whenever you draw a card, your opponent draws) and not Casts When Drawn |
| Deal Face Damage | `Deal N damage.` (any target), or N damage to a character / the enemy hero / your opponent / all enemies / all characters / both heroes / (random) enemies (not "enemy minion"), or damage split among enemies |
| Heal | restore(s) / heal(s) |

## Histograms

Cost 0..6 and 7+ (all pool cards); Attack 0..6 and 7+ (minions and weapons); Health 1..9 and 10+ (minions). The
median is the usual median of the cards that have the stat (halves shown as 3.5); its bar is highlighted.

The histograms stay in the view (`CardPoolView.Cost/Attack/Health`) but the compact panel no longer draws them
(2026-10-28: the chart with tabs was too busy for a tooltip, and rebuilding it on every hovered column cost about
25 ms of the UI thread each). The one line of averages that replaced them ("Average Mana 6 Attack 4.2 Health 5.1",
`PoolStats.Average`) and the mechanic shares went on 2026-11-26 (P-1, P-2): an average over 40-936 cards does not say
what will come. The panel lists cards instead (`OverlayViewModel.CardPoolRows`):

- a pool of 12 cards or fewer: every card as a tracker row (cost gem, the whole name over a strip of card art), by cost
  then name, under "Every card in the pool", and no button - the list is the answer;
- a larger pool: the four pool cards played most in meta decks (`PoolStats.TopCards`; none without meta data), in that
  order and without a share (P-3), under "Most played in meta decks", then the "All cards · N" button.

The full lists are in the pool browser. Everything is built when the pool changes.

The panel fades in (opacity only, 120 ms, cubic ease-out) when it appears or shows another pool, in both UIs, only
with Windows' animation setting on (WPF `SystemParameters.ClientAreaAnimation`, Avalonia `PoolInput.AnimationsOn`, so
not on macOS/Linux). Beside a Discover its position is set before the first frame (WPF measures it on becoming
visible), so it never shows at the previous spot and never slides. `--render-ui` turns the Avalonia fade off
(`CardPoolPanel.Motion`); the WPF render waits long enough for it to finish.

## When the panel shows

The owner's word of 2026-11-26 on the old behaviour - "pops up oddly, inconveniently and out of control" - set the rule:
nothing appears without the player asking, nothing follows the hand around (TASK-2026-11-26 AC4, design variant A).
`CardPoolTracker` (an `IPowerListener`) picks the pool, in priority order:

1. **Discover choice open** (`OnChoices`, GENERAL choice with a known source card) until the pick (`OnChoiceMade`), when
   the setting "During a Discover" is on (`AppSettings.CardPoolOnDiscover`, on by default; `CardPoolTracker.OnDiscover`).
   Off, the Discover shows no pool (and no pick hint). It comes from the log: nothing is read from the game for it.
2. **Requested**: `GameSession.ShowCardPool(cardId)` / `ShowCardPool(null)` opens the browser for a card. It is the hook
   for deck rows; today only `--render-ui` calls it. `GameSession.BuildCardPool(cardId, context)` builds a view outside
   the live game.
3. **A card in hand**: the card under the mouse (game memory, below) when it is in your hand (`GameState` HAND zone) and
   has a pool, an Excavate pool or a "this game" list. Related cards alone open nothing (P-9).

Settings → Tracker, under the main switch "Card pool" (`AppSettings.ShowCardPool`: off hides everything and reads
nothing):

| Setting | `AppSettings` | Default | A card in hand |
|---|---|---|---|
| "When you point at a card in hand": With Ctrl · Always · Off | `CardPoolHover` (`Ctrl`, `Always`, `Off`) | `Ctrl` | Ctrl: only while Ctrl is held over it; Always: on any hover; Off: never |
| "During a Discover" | `CardPoolOnDiscover` | on | - |

Both rows are disabled (the choice row dimmed) while "Card pool" is off. With "Read data from the game client" off
(Settings → Data, `AppSettings.ReadGameMemory`, docs/adr/0006) the card under the mouse is never read: the hover row is
off too, with the line "The card under the mouse is read from the game client, and reading it is off…"; the Discover
switch stays, a Discover comes from the logs. The hover row is not shown where game memory is not read at all (macOS,
Linux). A settings file from before has neither key and reads as `Ctrl` and on, the same as a new install. Ctrl, not Shift (five Shift presses turn on Windows' Sticky Keys) or
Alt (it moves the game window's focus); Ctrl already arms the overlay's panels for moving, and no panel stands over the
hand.

**What keeps a hand card's pool up** (`OverlayViewModel.CardPool`, the same in both UIs):

| The player | The panel |
|---|---|
| Points at a card of the hand without Ctrl ("With Ctrl") | Nothing; the game is not read |
| Holds Ctrl over a card with a pool, a "this game" list or an Excavate pool | Shows at its own spot at once (the fade only) |
| Moves along the hand with Ctrl held | The content changes in place; a card with nothing to show keeps the last pool, so the way to the panel keeps it too |
| Lets go of Ctrl, the pointer not on the panel | Goes 150 ms later (`OverlayViewModel.CardPoolCtrlLinger`) |
| Rests the pointer on the panel | It stays while the pointer is on it, Ctrl or not (`CardPoolHeld`); its linger counts from leaving |
| "Always": leaves the card without Ctrl, the pointer not on the panel | Goes 300 ms later (`CardPoolHoverLinger`) |
| Presses "All cards · N" | The browser opens on the left; the compact panel hides while it is open |
| Opens a Discover | The panel beside the options by itself (setting on); it goes with the pick, never held |
| Holds Ctrl over the panel `card-pool` | It arms and moves like every panel; its button still clicks (`MovablePanel.OnTool`) |
| Esc, a new turn, another pool | The browser closes |

The overlay's tick (WPF `OverlayWindow.Tick`, 33 ms; Avalonia `Desktop/OverlayWindow.Tick` through
`IOverlayPlatform.ControlDown`: Windows, macOS, X11) passes Ctrl to `OverlayViewModel.UpdateCardPoolKeys`, which is also
the clock of the lingers above; while the overlay is hidden (another window in front) it passes false. The view model
raises `CardPoolControlChanged` on each change and the host hands it to `GameSession.SetCardPoolControl`. A hand card's
pool that the session still has just after Ctrl came up is not shown again (the session forgets the card at once).

**Game memory** is read only while a card in hand could show something and the mode allows it now
(`GameSession.HoverReadsWanted`): reading the client on, setting on, catalog loaded, a supported mode (Ranked, Casual,
Friendly) and format, a card with a pool or a "this game" list in your hand, and the hover mode - with Ctrl only while
Ctrl is held, never when "Off"; the read itself goes through `GameSession.CanReadClient`. The read runs on its own
thread every 250 ms (`GameSession.HoverInterval`); Ctrl going down wakes it, so the first read does not wait out the
interval. Turning reading off forgets the card read before. The thread ends after 10 s without a read. Its read errors do not change the memory
status shown in the app. Cards on the board and the opponent's cards show nothing.

The compact panel lets clicks through to the game on every desktop but for its "All cards · N" button, the one part
marked `OverlayHit.Interactive` (the same plate as the panels' lock button: WPF `LockButton`, Avalonia
`Button.lockButton`). The pointer is read globally - WPF polls it every 40 ms while the panel is shown
(`CardPoolPanel.Poll`); Avalonia gets it from the overlay platform layer (`IOverlayPlatform.PointerPosition`) through
`OverlayView.UpdateCardPoolPointer` - for the hold and for the card previews of its rows. No right-click: until
2026-11-26 a right-click on the panel opened the browser, and the game got the same click (a cancelled target).

### Where the panel stands

- **A card in hand, and a request:** the movable panel `card-pool`, top left by default (`Margin="248,76,0,0"`, next to
  the opponent panel, above "Opponent archetype"), clear of the Discover options and the hand on 16:9, 16:10 and 4:3
  screens (render scenes `overlay-card-pool-hover*`). The player moves and resizes it in layout mode or with Ctrl over
  it; it grows downwards and `MovablePanel.KeepOnScreen` keeps it on the screen. The panel names the card, since it
  does not stand beside it.
- **Discover:** a second `CardPoolPanel` in a Canvas over the surface (`Anchored`), right of the row of options (left
  of it when there is no room), placed by `PoolPlacement` (`CardPoolTracker.AnchorOf` -> `ConstructedView.CardPoolAnchor`,
  no game memory: only the option count the log gives). The geometry is measured on 16:9 screenshots in units of a
  1080-high screen centred horizontally, the way Hearthstone lays out its board; the popup undoes the player's overlay
  scale, is drawn no smaller than 0.85 of its size at overlay scale 1 (`PoolPlacement.MinPopupScale`) and is kept on
  screen. Its fill does not fade with the overlay opacity.

Until 2026-11-26 a hand card's pool stood beside the game's enlarged preview of the card (`PoolPlacement` hand
geometry, `CardPoolTracker.HandAnchor`), lingered 0.8 s after the mouse left and was kept by a neighbour without a
pool. The panel slid along the fan and jumped sides at the middle of the screen; the geometry is kept in
`PoolPlacement` (tested), but the overlay no longer uses it.

## Excavate treasures

A card that Excavates (`EXCAVATE` in its mechanics), counts Excavates (`EXCAVATE` in its referenced tags, or
"Excavate" in its English text) or is a treasure shows the Excavate pool instead of nothing: the card text has no
pool the parser could read, and treasures are not collectible, so the catalog never had them. A treasure or counter
card with a pool of its own text (Azerite Gem: "Discover a 5-Cost card") keeps that pool.

- Treasures: `CardPool/Excavate.cs`, the Hearthstone Wiki list of 2026-10-28 (six per tier for tiers 1-3, common,
  rare, epic; the tier-4 legendary of Death Knight, Mage, Paladin, Rogue, Shaman, Warlock, Warrior). Names come from
  the card data (localized), English from the table when the card data has no entry.
- Tier: Power.log keeps it on the player entity. `MAX_EXCAVATE_TIER` is set from the start and 0-based (3 = four
  tiers, the seven classes; 2 = three tiers, everyone else; seen in the client's logs of 2026-10-23..28).
  `CURRENT_EXCAVATE_TIER` appears after the first Excavate; until then (and if a build does not log it) the treasures
  the player got this game are counted. Either value is taken modulo the cycle: the next tier is `value % tiers + 1`.
  Only the local player's tier is read (the player's own cycle; the opponent's is never shown).
- Panel: the card's name, the line "Excavate: tier 2 of 4", then "Next treasure · rare" over that tier's treasures
  as tracker rows. The other tiers are the browser's (P-10: until 2026-11-26 one quiet line each, cut short). Outside a
  game (a deck row) the line is "Excavate: 4 tiers" and there are no rows; "All cards · N" opens every tier.
- Browser: the same "Next treasure" line, the tiers as section chips, the grid in cycle order from the next tier.
- The view depends on the tier (`PoolContext.Excavate`); other pools drop it from the cache key, so they stay the same
  object through the game.

## This game's cards (hovered card in hand)

A card in hand whose text looks back at this game - "Trigger the Deathrattle of a random friendly minion that died this
game" (Undeath Sentence), Resurrect, "spells you've cast this game", "minions you've summoned / played this game" -
shows, when hovered, the cards of this game that fit, as tracker rows (cost gem, name over the art strip, "×2" for
repeats; 8 rows at most, "+N more"; "None this game yet" when empty) in the pool panel at its own spot, with Ctrl as any
hand card's pool: on its own (the card's name as the title, the section "Your minions that died · Deathrattle: 3") or
above the card's pool under that section. It never covers another card's pool (a Discover wins).

`MatchLists.Parse` reads the rule from the English card text (the pool catalog's; the card data's in an English
client): the clause before "died this game" / "you've cast|played|summoned this game", or after Resurrect, names the
side (friendly/your, enemy, or both), a mechanic (Deathrattle, Taunt, Divine Shield, Rush, Lifesteal, Reborn,
Battlecry), a minion type, a spell school and an "N-Cost". A clause that ends in a name ("each time Ysondre has died",
"each friendly Treant") or adds a condition the list cannot check ("that didn't start in your deck") has no list.
`MatchLists.Build` takes the cards from `CounterTracker` (deaths on the board for both players, the local player's
plays and summons) and the card data (mechanics, types, cost, localized names): only what the player saw. Rules are
cached per card id; a card with a rule in hand also turns on the hover reads.

One card has its rule by id, not by text: Ectoplasm (`CAP_805t`, `MatchLists.RuleOf`). It is uncollectible, so the
pool catalog has no English text for it. Its list, "Your minions that died to Slime 'em!", comes from the tags, not
from the death events. Slime 'em! (`CAP_805`) sets aside a copy of each minion on the board (`ZONE=SETASIDE`,
`CREATOR` = the cast, `COPIED_FROM_ENTITY_ID` = the minion) before destroying it, and gives each player an Ectoplasm
with the same `CREATOR`. Played, the Ectoplasm summons its player's copies in that order while the board has room.
The list is the local player's copies (`CONTROLLER`, minions only, with a card id: the opponent's are logged without
one) of the cast of the leftmost Ectoplasm in hand. The hover gives only the card id, and the panel stands beside the
leftmost copy. Checked on the four Ectoplasms played in the owner's logs of October 2026: each brought back the start
of its list, cut only by a full board.

## Related cards (card preview)

A card's related cards (tokens, Choose One options, Old God abilities, the cards it names) show in the card preview
beside the big card (below). Until 2026-11-26 a hovered card in hand also showed them in the pool panel, and a card
with only related cards opened it: the panel came up on half the hand, while the game itself shows most of them on its
own hover (P-9). The pool panel no longer shows them, and they are no reason to read the card under the mouse.
`RelatedCards.Of`, from the card data only (no hand-made table):

- **Tokens:** a card that is not collectible, of a drawable type (minion, spell, weapon, location, hero), in the same
  set, whose id is the card's id with a suffix (`t`, `t2`, `ta`, `a`, `b`, `A`, `B`: `CS2_101` → `CS2_101t`, `BE_017` →
  `BE_017t3`, `EDR_872` → `EDR_872A`); a `CORE_` or `Core_` reprint takes the original's tokens (`Core_LOE_115` →
  `LOE_115a`). An enchantment (`BE_017t6e`) is no related card, and a hero skin (set `HERO_SKINS`) has no tokens: its
  suffixed ids are other skins (`HERO_01b` → `HERO_01ba`). Two related cards with the same name and the same text are
  one row (C'Thun's two identical Eyes `BE_013t3`/`BE_013t6`); the same name with another text shows both.
- **Named cards:** a collectible card whose English name (two words or six letters at least) stands as whole words in
  the English text (the pool catalog's), outside bold keywords, and is no keyword itself (a mechanic or a referenced
  tag of some card, or one of Rewind, Resurrect, Silence, Freeze, Discover, Recruit, Corrupt, Overload, Shatter). The
  card data has no field that links a card to the cards it names, so this is a reading of the text: a heuristic that
  prefers no row to a doubtful one, not a proven link (2026-11-16, after Dark Deacon `BE_012` showed the Old Gods
  C'Thun). Its steps:
  - *The longest name wins its place:* "Add a Dragonbane Shot" is not Dragonbane, and a card's own name is never a
    reference (Zilliax Deluxe 3000).
  - *The text leads into the name as a card:* before it a lowercase word ("summon a", "your"), a count, stats or a
    cost written whole ("3", "1/3", "2-Cost"), a list comma, or the keywords Herald and Temporary; or the name is the
    subject after a clause's first word ("If Feugen also died"). A name that opens the text or a clause is grammar's
    capital letter ("Convert all but 1 of your Hero's Health", "Tradeable Convert a friendly Divine Shield", after a
    keyword's number "Spell Damage +1 Duplicate your hand", after "; or"), and a capitalised word on either side makes
    it part of a longer name the catalog does not have ("a 1/3 Risen Footman", "Reward: Gorishi Colossus").
  - *What the text says of the card comes first:* a candidate whose cost differs from the "N-Cost" before the name,
    or whose attack (and, for a minion, health) differs from the "A/H", is not that card, whatever its set. Stickybomb
    Saboteur `CATA_186`, "a 2-Cost Sabotage", makes its token `CATA_186t`, not the 4-Cost `GVG_047`. A weapon's second
    number is its durability, which the catalog does not have (Truesilver Champion is 4/0 there): only its attack is
    compared. Stats before a spell's name fit no card.
  - *The name means one card:* the candidates are the cards of that name plus the cards it is the short name of: the
    name before ", " of any card, before " the " of a minion or hero ("C'Thun" → `OG_280`/`WON_135` C'Thun, `DMF_254`
    C'Thun, the Shattered, `BE_013` C'Thun, Eye of the Empire; a spell's "Infest the Scullery" is a phrase, not
    another Infest). Reprints (the same type, cost, stats and text) are one card: the printing in the text's own set,
    else the lowest id, whatever the catalog's order. Of several different cards only the one in the text's own set,
    when it is the only one there (`BE_012` → `BE_013`, Old Gods cards → `OG_280`, their Wonders reprints →
    `WON_135`, "Herald Cho'gall" → `CATA_726`); with none or two there, nothing. A card whose own name starts with
    the short name means itself (Neptulon the Tidehunter, `BE_013`).
  - *The card's own token comes before a card of another set:* a named card of another set is not shown when the
    card has a token of that type (Dreaded Mount `SW_087`, "an endless Dreadsteed", makes `SW_087t`, not `AT_019`;
    Jan'alai's Ragnaros is `TRL_316t`). A named card of the text's own set stays beside the tokens (Stalagg: Feugen
    and Thaddius).
  - **Limits of the heuristic.** "The same set" is how the sets of the 2026-11-15 catalog write their own cards, not
    a rule of the game: a text that means an older card of the name while its set has its own would show the set's
    card ("Give your Deathwing +2 Attack" in Cataclysm → `CATA_190h`). An epithet before the name is no candidate:
    "Herald Onyxia" (`CATA_156`, `CATA_780`) means that set's Arisen Onyxia `CATA_155` and shows nothing, because
    the candidates are `EX1_562` and Onyxia the Broodmother of other sets; by the same gap a name with one older
    card and a new "Arisen X" in the text's set would show the older card (no such text in this catalog). A text
    with no cost or stats and no token says nothing to check a single candidate against. A name under six letters
    in one word is never found (Hex, Swipe, Brawl). A card that speaks of another card's tokens shows the card, not
    its tokens (`BE_012` shows `BE_013`; the abilities are `BE_013`'s own related cards).
    CORE reprints share a generic set: the same-set exception can still be ambiguous when both a source and a
    namesake are reprinted into CORE (no such token/name collision was found in this snapshot).
  - A text that explicitly replaces the hero or Hero Power requires that target type, before same-set selection.
    Majordomo (`BRM_027`) therefore does not show the minion Ragnaros; Exarch Naielle (`GDB_846`) does not show the
    spell Tracking. If the replacement itself is absent from the collectible catalog, the named row is omitted.
    This does not infer new token links or show Hero Powers, which remain outside the preview's allowed types.
  - **Audit of `c4ad4cc0`, 2026-11-16** (catalog of 2026-11-15, 6 501 cards; tokens from HearthstoneJSON; before the
    later replacement-type filter). The name rule gives 121
    named cards on 100 cards. By what backs each: 20 have stats or a cost in the text equal to the card's; 60 are
    cards of the text's own set under their full name; 8 are the same-set short name (C'Thun, Cho'gall); 32 are the
    only card of that name, in another set, with nothing in the data to check them against beyond the name (Gift
    spells, Animal Companion, Ragnaros the Firelord, Leper Gnome...); 1 is also the card's own token (King Krush).
    The token step then takes 5 of them (`SW_087`, `TRL_316`, and three whose token is the same card: `DRG_232`,
    `ICC_833`, `ULD_156`). The audit is mechanical where the data allows (stats, costs, tokens, sets) and a reading
    of the text otherwise; it found `CATA_186` and `SW_087` only when the tokens were compared, so it is no proof
    that the rest is right.
- Names in the client's language (`CardDb`); more than 12 related cards is no list. Without the catalog the tokens still
  show (they come from `CardDb`), the named cards do not. Cached per card for the card data, catalog and interface
  language. `GameSession.RelatedCardsOf(cardId)` gives any card's rows by id alone (no game state, thread-safe; empty
  for none or more than 12) for card previews.

### Card preview: related cards

The hover card (`CardPreviewHost`, both UIs) shows the previewed card's related cards beside it, wherever a row carries
`CardPreview.CardId`: deck tracker, pool rows, graveyard, the main window's deck lists and match page.
`CardPreview.Related` is set once by the app host to `GameSession.RelatedCardsOf`. The grid stands on the side of the big
card away from the row (either side, right first, for `Below`), top-aligned with it, else bottom-aligned, else pulled
inside; a "Related cards" plate above it. 1-3 cards in one row, 4 as 2x2, 5-6 as 3x2 at 0.75 of the big card; 7-8 as
4x2 and 9-12 as 4x3 at 0.5. Short of room it shrinks from 0.75 to 0.5, then shows no grid; 0.5 is never used below
overlay scale 0.9 (`CardPreviewLayout.PlaceRelated`). No grid for Battlegrounds cards, for more than 12 related cards,
without `CardDb`, and where `CardPreview.ShowRelated="False"`: the opponent's hand marks and hand rows, whose preview
is the card that created a hidden card (its related cards would be a guess). The pool panel's rows carry
`CardPreview.CardId` too, so N'Zoth's row in his "this game" list shows his four abilities beside his card.

## What players take (pick hint)

While the player's own Discover, Adapt, Choose One or similar choice is open, the panel beside the options adds a
block above the pool's cards, two lines per offered card: its name on its own line, then the share of offers in which
players took it (bar and percent) and the share of games they won after taking it; the name in gold with a star when
one option leads the next by 5 points or more. Numbers only, never advice or a click for the player.

- **Data: the Hearthpulse data API** (`api.kolodahearthstone.com/v1/hsreplay/card-choices`, Legend). The legal check
  (`reference/reviews/LEGAL-HeartLens-card-choices.md`, 2026-10-26) marked it a Blocker (L-B1: HSReplay Premium data
  collected without HearthSim's written permission, ToS §8). **The owner accepted that risk on 2026-10-27** and chose
  the API over the stats pool, which is not deployed; the UI names the source "Hearthpulse data" like every other API
  number.
- **Collected**: `ConstructedPicksTracker` keeps the local player's picks of a Ranked/Casual Standard/Wild game
  (source card id as the log names it, 2 to 8 offered card ids, the taken ones; the opponent's choices never reach
  listeners, and the source must be the player's). At the game's end `ConstructedPoolGame.From` turns them into
  `{id, day, format, won, picks}` - no deck, class, rank or opponent - and both apps post it to
  `POST /v1/constructed` through `Core/Hosting/PoolSharingHost.cs` while `PoolClient.Sharing` holds (on by default,
  off by the Settings → Data switch; `docs/game-stats.md`).
- **Read**: `DiscoverChoiceSource` loads `v1/hsreplay/card-choices/cards` once (which cards have numbers, in which
  formats), then, the first time a choice from a listed card opens, that card's `v1/hsreplay/card-choices?card=&format=
  &rank=legend` in the background - never per frame. Both answers are kept in memory and in `cache/api/` (6 h, trimmed
  to the fields read); a failed load is retried after 5 minutes. Until the answer is in, and for cards the API has no
  numbers for, the block does not show. The stats pool's `v1/choices` is no longer read by the app.
- **Matching** (`DiscoverPicks.Build`): an offered card id matches an API option by card id, else by its dbfId (a
  reprint under another id). `popularity` and `win_rate` are percents; an option the API does not list gets a dash,
  and "No Hearthpulse data for these cards yet" shows when no offered card has a number. Options marked `low_sample`
  are drawn faded (bar, share, win rate), and they never get the star; the note "Faded: few games" shows only when a
  faded row is on the panel (`DiscoverPicksView.HasLowSample` counts the same rows).
- Panel and rows: `OverlayViewModel.UpdateCardPoolPicks` (`CardPoolPicks`, `CardPoolPicksNote`), both `CardPoolPanel`s.
  Render scene `overlay-card-pool-picks` uses made-up numbers (`DiscoverPicks.RenderSample`, one option on few games).

## Pool browser ("all options")

A full-height side panel (`CardPoolBrowser`, MovablePanel `card-pool-browser`, default 350 units wide; drag its right
edge to resize, stored in `AppSettings.CardPoolBrowserWidth`). It starts at the **left** edge: on the right it would
cover the End Turn button (it can be moved in layout mode). Opened by the compact panel's "All cards · N" button
(`OverlayViewModel.OpenCardPoolBrowser`, a plain left click; a pool of 12 cards or fewer has no button, the panel lists
it whole) and by `GameSession.ShowCardPool(cardId)`; closed by the × button, Esc, and automatically when the turn
changes or another generator's pool comes up. No right-click opens or closes it since 2026-11-26 (P-16: the game got
the same click). Esc: the overlay never takes keyboard focus, so it is read from the keyboard state while the browser is
open and Hearthstone or Hearthpulse is the foreground window (the game sees the key too).

- Header "<card> Pool: N cards", the pool description, the highlighted card.
- Filters (collapsible, a gold dot when any is on; sentence case since 2026-11-26): chips for every mechanic some but
  not all pool cards have (`CardPoolView.Filters`, same rules as the percentages, no 12-tile limit); selected chips
  combine with AND (`PoolEntry.Mechanics`).
- Grid of full card renders, three per row, by cost then name; rows are virtualized, wheel scrolling glides to its
  target only when the Windows animation setting is on (both UIs). Hover shows the card larger.
- Card renders: HearthstoneJSON `v1/render/latest/{locale}/256x/{id}.png`, `Infrastructure/CardRender.cs` in both UIs
  (disk cache `images/render-{locale}`, newest 2000 files kept, trimmed once per session; a card without a render is
  not asked for again in the session; the transparent margin cropped to the 215x303 card; 120 images in memory). Kept
  apart from `CardArt.cs` on purpose; it can be merged into a `CardArt` kind later.
- The engine caches each built view per (card, game context, trigger, wiki answer), so a pool that stays up is the same
  object on every overlay update: the panels rebuild nothing and the grid keeps its scroll position. "No pool" is cached
  too, and the key takes the wiki answer itself, not the wiki lists: a generator whose pool matches no catalog card walks
  the catalog once, and a built view does not match its wiki lists again (2026-11-09; before, every publish of a hand
  with such a card walked the 6 501 cards again). Another trigger of the same pool reuses its lists.

An open browser keeps its pool until the player closes it (×, Esc) or the turn changes: the same card's pool getting a
new count (the wiki list) updates it in place, while Ctrl let go, the pointer crossing another hand card and even a
Discover of another card started meanwhile leave it as it is; the Discover's own panel shows once the browser is closed
(2026-11-09). Only a new "show the pool" request from a deck or hand row replaces it. Closed, a hand card's pool comes
back only while something still keeps it (Ctrl held), with no linger.

### Card under the mouse (game memory)

Found by reading the client's classes with `tools/Hearthpulse.Probe` (`class`, `hover`), no other tracker's code:
`InputManager.s_instance` -> `m_mousedOverCard` (Card) -> `m_entity` -> `m_cardIdInternal`
(`HearthstoneMemory.GetHoveredCardId`). Checked live in a Constructed match for ~6 minutes: hand cards (CATA_467,
JAIL_444, BE_571, CATA_161) always matched the `m_mousedOver` flag of the same card in `m_myHandZone.m_cards`; it also
returns board minions, hero, hero power and weapon, and null when nothing is hovered. While dragging it is the card
under the cursor; opponent's hidden hand cards have no id (null). `InputManager.s_instance` is rebuilt every match, so
it is read fresh each time. Not checked: outside a match (expected null), during a Discover choice. When it is read at
all is the hover mode's ("When the panel shows"): with Ctrl, only while Ctrl is held.

## Verification (2026-10-23)

- **Parser over the whole Standard pool** (1322 cards): 177 generators with a pool, 65 generators without a catalog
  pool (from the deck/hand, copies, game-state costs, named token groups - see the exception table), 0 not understood.
  After the rune fix (same day): 182 with a pool (+5 rune generators), 39 "Pool not computed" panels, 21 not from the
  catalog (deck, hand, copies), 0 not understood.
- **Tests** `tests/Hearthpulse.Tests/CardPoolTests.cs` on the fixture `Fixtures/card-pool.json.gz` (the real catalog of
  this date, Wild + HearthstoneJSON minion types, English text for Standard cards): 26 real Standard generators with
  pool sizes counted by a separate script from the raw API pages (one hand-written filter per card), parser cases
  (not a generator / not a pool / unrecognized / counts, costs, keyword lists, schools, class clauses), histograms,
  text rules, the view (filter mechanic left out, highlighted card) and the tracker on a synthetic Power.log (hover in
  hand -> play, no pool -> choice -> request -> hover outside the hand; a board generator gives no pool and no memory
  reads, `CardPoolPanelTests`), the supported game types, `PoolText.Normalize` and
  `ParseApiPage`. After a rotation: `tools/cardpool/pool_fixture.py fetch|fixture|check` rebuilds the fixture and
  recounts the expected sizes with its own hand-written filters (the committed fixture is reproduced exactly).
- **Real logs**: replaying 11 Constructed games (10 Standard, 1 Wild; Power.logs of 2026-10-21..23) met 12 generator
  plays/choices: 4 pools shown on play (Void Soul 5, Jade Guardians 44, Cryofrozen Champion 196, Drakeadon Mongrel
  144), 8 correctly without a pool (Cultist Map/Nightmare Fuel - from the deck or a copy; Grim Harvest, Time Machine -
  token groups). The other choices in those games (C'Thun pieces, Fleshshape, Aya's coins) are not generators.
- **Render**: `--render-ui` writes `overlay-card-pool.png` (Blazing Invocation as a Shaman),
  `overlay-card-pool-browser.png` (Thief's Tools, filters open) and `overlay-card-pool-browser-filtered.png` (one
  filter on) in both apps.

## Verification (2026-11-26, TASK-2026-11-26 AC4)

- **Tests**: `CardPoolHoverModeTests` (a `GameSession` with a fake reader of the card under the mouse: without Ctrl
  nothing is read and no pool shows; with Ctrl the hovered card's pool at the panel's own spot, gone when Ctrl is let go;
  "Always" reads without Ctrl, "Off" never; the Discover with its switch on and off; related cards alone read nothing),
  `CardPoolPopupTests` (both compiled view models: Ctrl, the 150 ms and 300 ms lingers, the pointer on the panel, the
  Discover popup not held, "All cards" and small pools, the header, Excavate, the browser; the WPF panel's layout and
  its one interactive button), `LocBindingTests.CardPool` (the Avalonia panel and Settings rows), `AppSettingsTests`
  (an old file reads as Ctrl and on), `SessionSettingsTests`, `CardPoolHoverTests`, `CardPoolTests`.
- **Render**: `overlay-card-pool*` in both apps (the Discover popup with and without the pick hint and at overlay scale
  0.8; a hand card's pool at the panel's own spot on 16:9, 16:10 and 4:3; a large pool; rune sections; a small pool; an
  Excavate treasure; N'Zoth's list and its row's preview; the browser).
- Not checked: a live game (Ctrl over the hand, the button pressed through the overlay window).
