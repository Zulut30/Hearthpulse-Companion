# Source captures for Hearthpulse statistics

Source capture preserves a safe, ordered projection of the observed game protocol so the server can decode it
again when its parser improves. It supplements the compact records in [game-stats.md](game-stats.md); it does not
upload arbitrary personal log files. Source format, decoder version and derived-statistics version are separate.

The Linux deployment targets the existing Hearthpulse server, with its own process, private archive and database.
It does not use the site's account database. The API and the .NET decoder are independent processes; a successfully
stored source remains available when decoding fails. Deployment settings and limits live with `server/pool`.

## What is captured

The v1 JSONL protocol retains ordered visible entity observations, selected numeric tags, blocks, local choices
with ordered alternatives, actions, visible board/shop snapshots, draft choices and explicit gaps. Each record has
a contiguous sequence number, turn, scoped references and versioned payload. Repeated alternatives remain repeated;
a card instance is distinct from its card definition. Draft captures are independent of completed matches.

Source v2 adds only public context and labels: the game's format for Ranked/Casual
(`standard|wild|classic|twist`), the Arena kind (`normal|underground`, also on draft captures), the client's choice
type (`mulligan|general|other`) and a named `combat_view` gap that marks every Battlegrounds game source as not a
replay of the displayed combat. All records of a capture share one version, recorded as `manifest.sourceVersion`; a
capture spooled by an older app is finished and sent as v1 unchanged. Contract details: the
[CLI contract](../tools/Hearthpulse.SourceWorker/README.md#source-v2-and-result-v2).

Source v3 (2026-11-19) is v2 plus one record kind, `bg_combat`: the fights of a Battlegrounds game (below). A v1/v2
line of that kind is invalid; everything else reads as v2.

Source v4 (current recorder, 2026-11-26) is v3 in which a gap is only a real loss: the same record kinds and fields,
far fewer gap records, and no `combat_view` gap (["Source v4"](#source-v4-a-gap-is-a-real-loss) below).

There is no player name, BattleTag, account id, deck name, chat, authentication/network log or machine path in the
wire format. Other players' hidden cards, shop or future post-game reveals are not a source. References across
opponent conceal/reveal boundaries must be renewed so an entity cannot track a hidden shuffled card.

Capture is a typed safety boundary. Unknown or unavailable observations produce a gap/partial state rather than
an arbitrary string payload or an invented zero; known log grammar the format leaves out on purpose is neither (v4). A newly supported semantic interpretation can reuse saved source;
a new capture field or source sometimes needs a client update. The v1 allowlist does not imply coverage of every
Hearthstone mechanic or a guarantee of complete data from every installation.

## Battlegrounds fights (`bg_combat`, source v3)

Purpose: measure the combat simulator on at least 500 fights it was not tuned on (owner, 2026-11-19; the task card
TASK-2026-11-19-sim-all-mechanics, packages U and U2): score its odds (Bench `simcover --pool`) and replay each fight
step by step after the game (`simreplay --pool`). Same switch as the rest of the journal: on by default, off with
"Send my games to Hearthpulse statistics"; no consent question and no notice.

- **When.** At the end of a Battlegrounds or Duos game the recorder asks the tracker for the game's finished fights
  and writes one `bg_combat` record per fight just before `end` (`SourceCaptureRecorder.Combats`,
  `GameSession.SourceCombats`). The journal is sent only after `end` is spooled, so no fight leaves during the game. A
  fight whose combat never ended is not one. A historical catch-up replay records nothing, as before.
- **From where.** An allowlisted copy of the fights the tracker holds in memory (`SourceCaptureCombat.FromGame`), the
  same ones the local `combat-captures/` file keeps; that file itself is not read or sent.
- **What (the allowlist).** Envelope `side:"none"`, `entity:0`, `card:null`, `target:0`; data, every field always
  present: `fight` (the combat's number in the game, 0–63, growing), `turn`, `oursFirst` (true/false/null),
  `oursEnteredFirst` (true/false/null, the order of the sides: whether the player's side entered play first, i.e. its
  hero has the lower lobby `PLAYER_ID`; read only by the death order, `Fight.EnteredFirst`, rules-evidence 4a: the side
  that entered first loses its starting minions first in a death phase; null when either number is unknown),
  `firstAttacker`, `damageCap`, `countDamage` (false in Duos), `ours`/`theirs`, `facts`, `result` (`W|T|L`),
  `damageTaken`, `damageDealt`, `odds` (null or `win, tie, loss, lethalTaken, lethalDealt` rounded to 4 decimals,
  `unsimulated`, `runs`: what the app showed when the game ended) and `transcript`.
  A side: `hero`, `heroPower`, `extras` (player-level cards: hero powers, trinkets, quest rewards, anomaly, Deity), `tier`,
  `health` (hero health with armor), `turn`, `tags` (only the player counters effects read: `BACON_BLOODGEMBUFFATKVALUE`,
  `BACON_BLOODGEMBUFFHEALTHVALUE`, `TAVERN_SPELL_ATTACK_INCREASE`, `TAVERN_SPELL_HEALTH_INCREASE`,
  `BACON_OLD_GOD_ATTACK`, `BACON_OLD_GOD_HEALTH`, `4914`, `4915`), `cardTags` (the local player's player-level card
  values `TAG_SCRIPT_DATA_NUM_1/2` and Avenge (X) and its progress `SCORE_VALUE_1/2`; always `{}` for the opponent),
  `playerEnchantments` (enchantments on the local player with their numbers, e.g. Diremuck Forager's pending summons;
  always `[]` for the opponent), `lobbyTribes` (the lobby's tribes as tribe bits, 0 when not read), `deaths` (the side's
  minions that died at Start of Combat, before the capture: the player saw them die; minions without `remembered`),
  `minions`. A minion: `entity`, `card`, `golden`, `attack`, `health`, `maxHealth`, `baseHealth`, `rebornAttack`,
  `keywords` (Taunt, Divine Shield, Poisonous, Venomous, Reborn, Stealth bits), `attacks` (Windfury), `baseKeywords`,
  `tribes`, `mechanics`, `tier`, `script1`, `script2`, `creator` (card id), `enchantments` (card ids),
  `enchantmentValues` (each enchantment with its numbers: `card`, `script1`, `script2`, `attack`, `health`,
  `creatorEntity` = the reference of what made it when the record holds that entity, else 0), `friendlyDeaths` (friendly
  minions that died at Start of Combat while it stood on the board), `bloodGemAttack`/`bloodGemHealth` (what its Blood
  Gems gave it, tags 4235/4236), `remembered` (minions a start-of-combat trigger removed, one level), `unsimulated`. A
  fact: `entity`, `card` (as logged), `markers` (`DEATHRATTLE TRIGGER_VISUAL AURA AVENGE OVERKILL BACON_RALLY`).
- **Transcript** (`SourceCaptureCombat.Transcript.cs`, version 1; `null` when the fight has none or it is outside the
  limits). The tracker's record of the fight from the first attack to its end (`BattlegroundsTracker.Transcript.cs`), cut
  down to the steps of the boards: `{"version":1,"complete","ending","events","boards"}`, `ending` one of `hero restore
  cleanup end cut`. A step is a compact array: `["attack",attacker,defender,nested]`, `["damage",minion,amount]`
  (negative: healed), `["death",minion]`, `["summon",minion,card,position,opponent,golden]`, `["tag",minion,tag,value]`
  with `tag` one of `ATK HEALTH TAUNT DIVINE_SHIELD POISONOUS VENOMOUS REBORN WINDFURY MEGA_WINDFURY STEALTH`,
  `["trigger",source,host,card,opponent]` (a triggered effect of a board minion, of an enchantment on one (`host`) or of
  a hero power, trinket or other player-level card its side names) and `["end"]` (closes the innermost trigger). A
  board: `[steps before it,[[minion,card,attack,health,keyword bits],..ours],[..theirs]]`, before each regular attack and
  at the end. Every step names a board minion: one of the capture or one an earlier step summoned. Entity ids are the
  record's references (one numbering with the boards).
- **The order of the sides, not a seat number (U2 decision, legal review LEGAL-sim-pool-combats-v3 L-M1).** The
  simulator compares the two heroes' lobby `PLAYER_ID`s and reads nothing else of them (`Fight.cs`, `EnteredFirst`), so
  the record carries only that order (`oursEnteredFirst`); the reader gives the sides the numbers 1 and 2 in that order.
  The numbers themselves never leave: no seat, nothing next to it in Power.log (`GameAccountId=[hi=… lo=…]`, the
  Battle.net account). `oursFirst`/`firstAttacker` cannot stand in for it: who attacks first is a different rule.
- **Never.** Either hand (the player's own included; in the transcript no card into the hand, no hand card's stats or
  triggered effect), the opponent's card values or player enchantments, other player tags (ratings, ids), lobby
  `PLAYER_ID` numbers, `GameAccountId` (`hi`/`lo`), the player's own secrets (the capture keeps them for Ice Block; the
  record does not), names, BattleTags, account ids, paths or text. Entity ids are fight-scoped references 1.. in Power.log order
  (the simulator only compares them), not the game's ids.
- **Caveat to "only what the player saw" (owner's decision, 2026-11-19).** The fights are uploaded to measure the
  simulator. Simulator inputs the client may not show (the opponent's player counters such as Blood Gem values, the
  `script1/2` values of his minions, the numbers on their enchantments (`enchantmentValues`), their Blood Gem buffs
  (`bloodGemAttack`/`bloodGemHealth`, tags 4235/4236), the friendly deaths they saw before the capture
  (`friendlyDeaths`) and the card that created them, `creator`) are sent after the game, only for this measurement, and
  never published. Both boards and the course
  of the combat (the transcript) are on the player's screen during the combat. `creator` stays on the opponent's side
  because the simulator needs it there: his hero powers that copy or summon (Vanndar, Drek'Thar, the Tentacle power)
  check which minion they already made, and it is part of the simulation seed.
- **Limits** (client, decoder and pool alike): 64 fights a game, 7 minions a side, 7 remembered per minion (none
  nested), 32 enchantments (and 32 enchantment values) a minion and 32 player enchantments a side, 32 deaths before the
  capture a side, 16 player-level cards and values a side, 14 facts, references up to 1000, stats 0–1 000 000 (a
  remembered or dead minion's from −1 000 000: damage past its health), hero health −10 000–10 000 (below 0 when the
  capture saw a hero already beaten), data at most 56 KiB (inside the 64 KiB line limit). A fight outside them is left out whole and counted in the log
  (`Source capture: N fight(s) left out of the journal`), never cut, so the journal is never refused for it. The
  transcript: at most 512 steps, 513 boards and 16 KiB of compact JSON; a transcript outside them, or one whose fight
  would then be over the data limit, is left out alone and the fight goes without it.
- **Rules in code.** `SourceCaptureCombat` (app and decoder; the transcript in `SourceCaptureCombat.Transcript.cs`) and
  `validCombat`/`validTranscript` in `server/pool/node/validation.mjs`; `server/pool/test/bg-combat.fixture.json` is
  checked by both test suites (`SourceCaptureCombatTests`, `SourceCaptureCombatTranscriptTests`, `combats.test.mjs`). A
  new tribe, keyword or mechanic bit, marker, card value, player counter or transcript step changes all three
  (`The_wire_lists_are_the_ones_the_simulator_reads` fails first).
- **Decoder.** Reads v3 and v4; a `bg_combat` adds no fact and no gap, its event keeps no data. v3 kept the parser
  version (`source-v2.0`); v4 brought `source-v2.1`, which decodes v1–v3 sources as before.
- **Pool.** On the manifest commit the fights go to table `bg_combats` (migration `0010_bg_combats.sql`) in the same
  transaction, or the commit answers 503 to retry under storage pressure. They leave with their capture: expiry (the
  archive retention, 180 days), forget of the generation, deletion. Nothing is published from them; `/healthz` shows a
  count (`bgCombats`).
- **Use.** Fights are used only to measure the simulator: never joined with hearthpulse.net profiles, with other
  installations' captures of the same lobby, or with any outside replay or video (legal review
  LEGAL-sim-pool-combats-v3, L-m3).
- **Export (owner only).** On the pool host, with the token from the service's env file, never typed on a command line
  (it would stay in the shell history):

  ```sh
  set -a; . /etc/hearthpulse-pool/pool.env; set +a   # or: read -rs POOL_OPERATOR_TOKEN; export POOL_OPERATOR_TOKEN
  node server/pool/node/export-combats.mjs --out fights.jsonl
  ```

  The loopback route `POST /v1/captures/combats/export` takes the operator token like reprocess; nginx refuses it. One
  line per fight: `{"v":1,"game":"<16 hex of a hash of the capture key>","day","build","mode","fight"}`, no
  installation, generation or capture key. Read with `Hearthpulse.Bench simcover --pool fights.jsonl` (odds) and
  `Hearthpulse.Bench simreplay --pool fights.jsonl` (step by step: the record has no hand, so an effect that uses the
  hand plays without it and such a fight can leave the log, e.g. a summon from the hand the sim cannot make).
- **Rule for an export file** (legal review L-M2). Pool retention, forget and deletion do not reach a copy, so:
  - it is a working copy only on the pool host or the owner's PC, moved between them over SSH (`scp`/`sftp`);
  - never committed, never put on a cloud drive, never given to anyone else or to an outside service;
  - deleted after the measurement and no later than 180 days from the `day` of its rows; a new measurement takes a
    fresh export;
  - `simcover --pool` and `simreplay --pool` leave out any row older than 180 days (`PoolFights.MaxAgeDays`), so a stale
    copy gives nothing.
- **Order of deployment.** Deploy `server/pool` before releasing an app that writes v3. A pool without v3 refuses a v3
  journal as an unknown record (`400 invalid_record`); the app then sends that journal and the following ones as v2
  without their fights until it restarts (`SourceCaptureQueue.Downgrade.cs`, log line `Source capture: the pool does
  not take source v3 yet...`), so the queue never waits on it, but those fights are lost.
- **Before releasing v3: update the site's privacy policy, section 9** (legal review L-M1; the site is another
  repository). The ready paragraph from the review, with the card that created a minion, the numbers on enchantments,
  the deaths before the first attack, the lobby's tribes, the order of the sides and the course of the fight added (U2,
  legal review LEGAL-sim-pool-combats-v3), for the owner to paste:

  > «После игры в Полях сражений журнал содержит и каждый бой так, как его получил симулятор боёв приложения: оба
  > стола на момент первой атаки (карты, статы, ключевые слова, усиления и их значения, усиления от Кровавых самоцветов,
  > карта, создавшая существо), существа, погибшие до первой атаки, героев, силы героя, аксессуары и награды и их
  > значения, ваши усиления игрока, племена лобби, порядок сторон (чья сторона вошла в игру первой — без номеров мест
  > в матче), уровень таверны, здоровье, счётчики игроков обеих сторон (например, усиление Кровавых самоцветов), ход
  > боя по шагам (атаки, урон, гибель и призыв существ, изменения их характеристик и сработавшие эффекты на столах),
  > показанные шансы, исход и урон. У соперника это и то, что игра может не показывать: карта, создавшая существо,
  > его усиления и их значения, его счётчики. Никогда — руки (ни карт, полученных в бою, ни их характеристик), имена,
  > BattleTag, аккаунты. Цель — проверять точность симулятора: бои не публикуются и не сопоставляются с профилями
  > hearthpulse.net, с боями других установок из того же лобби или с чужими записями и видео. Хранятся и удаляются
  > вместе с журналом (до 180 дней, при выключении отправки).»

## Source v4: a gap is a real loss

Up to v3 the recorder counted every Power.log line and value it did not type as an `unsupported` gap. On the kept logs
of 24–25.11.2026 (10 Ranked games, `Hearthpulse.Bench capture`) that was 12 245 gap records, 46 % of the records,
about 1 200 a game, and every game ended `incomplete`; the pool saw the same (63 captures, all `partial`, 111 580
`unsupported` gaps, 2026-11-26). Nine tenths were lines the format never meant to carry. Since v4
(TASK-2026-11-26-pro-collection, P1) a game capture is `partial` only when something was lost: a stretch of the game
not seen (late start, log rotation, shutdown), a bound reached (`limit`), an observation that could not be typed
(`unsupported`, `invalid`) or one that never became visible (`source_missing`).

One exception, on purpose: a draft capture is always `partial`. It starts with a `source_missing` gap because the
Arena offers it holds are read from the client and have not been checked against the screen
(`SourceCaptureRecorder.CaptureDraftChoice`), so no draft claims complete coverage. Draft metrics therefore never read
`parsed` or `completeForAggregation`; each needs its own fitness ([ADR 0013](adr/0013-per-metric-eligibility.md)).

Left out on purpose, neither recorded nor counted (`SourceCaptureRecorder.Input.cs`, `SourceCaptureRecorder.cs`):

| In Power.log | Why the format does not need it |
|---|---|
| `GameState.DebugPrintOptions` (the option lists), `SendOption`, `SendChoices`, `OnEntityChoices ... queued` | what the player could do or sent; what happened arrives as Power lines and chosen entities |
| `DebugPrintPowerList` (`Count=`) | the header of a batch of Power lines |
| `META_DATA` with its `Info[n]` lines; `SUB_SPELL_START` with `Source`/`Targets[n]`, `SUB_SPELL_END` | display metadata and effects; the state they show arrives as tag changes |
| `SHUFFLE_DECK`, `CACHED_TAG_FOR_DORMANT_CHANGE` | the order of a hidden deck; tags the client keeps for a dormant minion |
| tag `STEP` (no longer in the recorder's allowlist) | the phase is logged as a name and no metric reads it; `TURN` carries the turn |
| `PROPOSED_ATTACKER`/`PROPOSED_DEFENDER` | the attack is its `ATTACK` block; v1–v3 wrote an `unknown` action and a gap for each |
| a `PLAY` block from a card the action list does not name (a location used from play) | the block with its source is the observation; v1–v3 added an `unknown` action |
| a Power.log block type without a kind of its own: `DECK_ACTION` (a trade), `FATIGUE`, `RITUAL`, `JOUST`, `REVEAL_CARD`, `GAME_RESET` | written as `OTHER` with its contents, which are kept; only the type is left out |

Unknown Power.log grammar stays an `unsupported` gap: the recorder cannot claim a game complete when the client wrote
something it does not know. A block type outside both lists is such grammar: its block is written as `OTHER` with its
contents and comes with an `unsupported` gap (`SourceCaptureRecorder.KnownBlockType`), and `Bench capture` names the
type.

A Battlegrounds game has no `combat_view` gap in v4. Every Battlegrounds source is the Power state, which runs ahead of
the displayed combat; this section says so for all of them instead of counting a loss in each. A Battlegrounds game
without a loss is `parsed` and complete for aggregation; the per-metric eligibility of [ADR 0013](adr/0013-per-metric-eligibility.md)
is unchanged (its amendment of 2026-11-26).

For `choiceType: mulligan`, `picked` is the cards kept: `DebugPrintEntitiesChosen` lists the cards the player keeps,
and the others go back to the deck (checked on the 10 kept mulligans of 24–25.11.2026). A local choice resolved with no
card (`EntitiesCount=0`, a mulligan that replaces every card) is written as resolved with an empty `picked`; before, it
stayed open and made the game `partial` without a gap. A header with a count above zero resolves nothing by itself: the
choice is resolved by its card lines.

A local choice whose pick never became visible is a real loss: the game ended while it was open (a concede during the
mulligan or a Discover) or the Battlegrounds turn timer chose and no chosen line came. The recorder writes one
`source_missing` gap for it when the next choice opens or the game ends, so the journal ends `incomplete`. Sealed
`complete` with a choice open, the journal is refused by the pool (`400 incomplete_stream`), also as v3.

On the same 10 games v4 writes 1 gap record (a game cut by the end of its log: `shutdown`) and 13 928 records instead
of 26 395; 9 games are `parsed`, the cut one `partial`.

- **Decoder** (`source-v2.1`): reads v1–v4 and decodes v1–v3 exactly as `source-v2.0` did. In v4 an `OTHER` block is
  no unsupported observation and a `combat_view` gap is invalid. An `unknown` action and an unknown record kind stay
  unsupported in every version.
- **Pool**: takes v4 lines and manifests with the same two rules (`validation.mjs`). The worker-result rule (`partial`
  when there are gaps or unsupported observations, `archive.mjs`) is unchanged.
- **Old captures** keep their status: the 63 v2 captures of 2026-11-26 stay `partial`, since their gaps cannot be told
  apart after the fact; reprocessing them with `source-v2.1` gives the same result.
- **Order of deployment**: pool and worker before an app writing v4. A pool without v4 refuses a v4 line (`400
  invalid_record`); the app then sends that journal one version older, and once the pool takes it, the next ones too
  until it restarts (`SourceCaptureQueue.Downgrade.cs`, log line `Source capture: the pool does not take source v4 yet,
  journals go as v3 until the app restarts`): as v3 with the `combat_view` gap put back and the end sealed incomplete
  where v3 counts a loss (that gap, an `OTHER` block), fights kept; as v2 without fights when the pool does not take v3
  either. Only the first chunk of a journal is tried older. A pool that took the refused version earlier in the session
  (rolled back since) gets that journal older on its own: the session keeps its version, and each journal costs one
  request more. A journal refused in v2 too is refused for good (below).
- **Refused for good**: a 400 whose code says the pool will never take the journal as it is, its records, their order
  or the manifest against them (`invalid_record`, `invalid_sequence`, `incomplete_stream`, `manifest_source_mismatch`
  and the others in `SourceCaptureClient.JournalRefusals`), and a `410 deleted_capture` (the pool deleted the
  journal's upload, unfinished past `POOL_UNFINISHED_HOURS`, and refuses every later request of it), drop that journal,
  and the pass goes on to the next one. The log line `Source capture: the pool refused a <mode> journal (<code>),
  dropped; source v<n>, sent as v<m>` is its record; it is not counted with the journals that could not be saved
  (Settings → Data). `SourceCaptureClientTests` holds the client's lists against every 400 and 410 code of the pool. A
  temporary answer (`429`, `503`, no network) or another refusal (an envelope, a limit, a conflict, a revoked
  generation) keeps the journal and ends the pass, as before.
- **Nothing new leaves the machine**: v4 has the record kinds and fields of v3, in fewer records.
- **Measure**: `Hearthpulse.Bench capture [--logs <dir>]` replays kept logs through `GameSession` and the worker's
  parser: per capture the gaps by reason, the decoder's gaps, unsupported count and status, the choices left open, the
  journals the pool would refuse as complete with a choice open, and the block types the recorder does not know.
  `--cost <n>`: what the journal costs the log thread, every log through `GameSession.OnLines` n times with the journal
  off and on after a warm-up round (time and bytes allocated on that thread).
- Tests: `SourceCaptureV4Tests` (a whole Ranked game per left-out kind and block type and a whole Battlegrounds game
  through `GameSession`, `parsed`; unknown grammar still a gap; a pick that never showed, one gap, and none carried
  into the next game; a mulligan's pick is the cards kept; the decoder's rules; the fallback to v3 and v2, also from a
  pool rolled back after it took v4, never from a journal spooled in v3; a journal refused for good or with its upload
  deleted dropped, the next ones sent), `SourceCaptureClientTests` (the final codes against the pool's),
  `capture-validation.test.mjs` and `combats.test.mjs` (the pool's rules, a journal with a choice left open taken as
  incomplete in v4 and v3).

## Delivery and lifecycle

Sources are written to a separate durable spool, compressed and sent in chunks with SHA-256 over the exact
uncompressed bytes. Receipts must match the submitted index and hash before a chunk is acknowledged locally.
The manifest is committed after all chunks; `stored`, `processing`, `parsed`, `partial` and `failed` are distinct
states. Missing parts, conflicting hashes, rejected data and revoked generations are not successful delivery.

Source sending follows the existing game-statistics switch (the table in [game-stats.md](game-stats.md), "Sending
rule"); since 2026-11-17 no in-app notice gates it. The Settings → Data and installer texts describe the expanded
fields, the Hearthpulse server and retention. The production endpoint is configured by default since 2026-11-16, after publication of the policy and the owner pilot
(see source-capture-verification.md). Public release remains gated on
server disk capacity and intake quotas; the pilot does not establish published analytics.

Turning sending off stops capture, clears pending source and persists a forget request. The server revokes the
installation generation, removes its archive/derived contributions and prevents late uploads or jobs from restoring
them. An HTTP 202 deleting response is not final confirmation. A new generation is created only after an explicit
subsequent enabling action. Source identities are unrelated to site accounts and are never put in request URLs.

### Queue destination

A spool belongs to one pool address, the endpoint its generation was made for (`state.json`), because the install key
is the same for every pool: sources and deletion requests of a test pool must not reach another host when
`PoolEndpoint` changes or is removed. The address is `PoolDestination.Normalize` (`https://host[:port]/path`, lower
case host, no trailing slash; credentials, a query or a fragment make it no address). It is local metadata and never
part of a payload or a log line.

- **Layout.** `source-capture/<16 hex of the address hash>/` is the spool of one address (`SourceCaptureQueue.Folder`):
  its `state.json` (install key, generation, endpoint, deletion requests) and its sources. `SourceCaptureHost` opens
  the spool of the address the settings name now; a new address gets a spool and a generation of its own. When the
  address changes, also without a settings event, the old spool is flushed and closed untouched.
- **Sending.** Before every chunk, manifest and deletion request the sender compares the pool the settings name at
  that moment with the endpoint in the spool's state, and sends nothing while they differ or no pool is set up. An
  answer that arrives after the address changed settles nothing locally: the chunk or manifest is sent again when its
  pool is back (the server stores a repeated chunk once).
- **Other addresses.** Their spools keep sources and deletion requests and are sent when the settings name that
  address again, also after a restart. The 14 days are applied to their sources on every delivery pass (every 30 s
  while the app runs) and whenever settings are applied; an open spool that turns out to be another pool's applies
  them too. The 2 GB budget is per spool. A deletion request waits for its own address without a time limit.
- **"Something was sent" mark.** `state.json` carries `sent` for the current generation: `false` from its creation,
  `true` from just before its first chunk is handed to the pool (before, because an answer that never arrives may
  still have stored it). A state written before the mark existed has none and counts as sent. Local metadata only.
- **Switching off** revokes the current spool's generation as before, and opens every other address's spool to delete
  its sources and revoke its generation. A deletion request is persisted only for a generation that may have reached
  its pool (`sent` not `false`), and waits for that address; a refusal before the first chunk left makes no request.
  The host makes no install secret for a refusal (`SourceCaptureHost.ApplySettingsAsync`). A
  spool set aside without an address is deleted, and so are the sources of a spool whose state cannot be read and
  whatever an earlier version left in the root that could not be moved; no deletion request can be made for those.
- **Returning** to an address that was left by an opt-out does not resume capture by itself (a revoked generation is
  never resumed): it captures again after the next explicit enabling, as on one address.
- **Spool of an earlier version** (`state.json` and sources directly in `source-capture/`), moved before a spool is
  opened; a move that fails is tried again whenever settings are applied, and nothing in the root is sent meanwhile. Its state names its pool, so the sources go to that address's folder whatever the settings say
  now, and each deletion request goes to the folder of the address it carries (a request of another address keeps
  the install key there, so that address needs an explicit enable). A request without a valid address is dropped
  with a count in the log. Unreadable state means an unknown pool: the spool is renamed to
  `source-capture/unknown-<8 hex>/`, never opened or sent, dropped after 14 days or on switching off.
- **Unreadable or contradicting state of an address's spool.** Unreadable `state.json`, also one that parses but
  lacks its list of deletion requests (`{}`): the host reports `unavailable`, logs `Source capture: unavailable`,
  sends nothing and does not throw; the delivery loop keeps running. A state that names another
  pool than its folder: status `other_pool`, sources kept, nothing sent.
- **Seen from outside.** `SourceCaptureState.Status == "other_pool"` and the counts `HeldCaptures` / `HeldDeletions`
  are apart from `disabled`, `unavailable` and `offline`; Settings → Data shows them in both apps
  (`MainViewModel.SourceCaptureStatusText`, strings `Settings.Data.HeldData` / `HeldDeletions`). The host publishes
  its state under one lock with a growing `Revision`; the view model drops a report older than the one it shows. Log lines, one per
  event, with counts and no address: `Source capture: queues for other pools kept, N capture(s), M deletion
  request(s)` (when the counts change), `Source capture: queue for another pool kept, …` (an open spool stopped for
  the address), `Source capture: earlier queue kept for its own pool, …`, `Source capture: earlier queue has
  unreadable state, set aside, …`, `Source capture: dropped N expired capture(s) kept for another pool`,
  `Source capture: sharing turned off, cleared N queue(s) kept for other pools`,
  `Source capture: nothing was sent for this generation, no deletion request`.
- Tests: `SourceCaptureDestinationTests` (chunk, manifest and forget across an address change, removed override,
  held answers, restart, both earlier-version cases, unreadable and incomplete state, opt-out with and without a
  send, first refusal, a failed move, report order, expiry on a delivery pass).

The target retention is 180 days, subject to the configured service/archive budget and a verified capacity plan.
Disk pressure rejects new intake for retry; it must not acknowledge data it discarded. Pilot deployment has an
explicit storage ceiling. Server backups and restore procedures must preserve revocation/deletion semantics.

## Decoder and aggregate eligibility

`tools/Hearthpulse.SourceWorker` is a standalone .NET 10 command with a self-contained Linux build. It validates
the JSONL envelope, numeric/card/tag restrictions, visibility, boundaries, sequence, source/line/record budgets,
choices and state, then writes an atomic versioned derived result. Error output contains fixed technical codes,
never source snippets or private paths. See [the CLI contract](../tools/Hearthpulse.SourceWorker/README.md).

The decoder hash identifies exact source bytes; `parserVersion` identifies its interpretation. Reprocessing
replaces the contribution for a capture rather than adding a second game. Publication must recheck that the source
generation has not been revoked, even when the worker has already finished.

Structural completeness and completed-game eligibility are separate. `completeForAggregation` requires a normal
complete game without unresolved observations and the appropriate outcome: W/L/T for Ranked/Casual/Arena/Brawl,
placement 1–8 for solo Battlegrounds, or team placement 1–4 for Duos. A complete source without that outcome can
still preserve actions but cannot enter completed-match result aggregates. A draft is never a completed match.

Result v2 (`source-v2.0`) keeps that flag unchanged and adds per-metric eligibility ([ADR 0013](adr/0013-per-metric-eligibility.md)):
`eligibility.outcome` and `eligibility.offers` with fixed reasons, plus the `facts` they admit (the visible result or
place; the Battlegrounds hero offer at the moment of the pick). A v2/v3 Battlegrounds game can be fit for the place and
the hero offer while remaining `partial` (its `combat_view` gap); a v4 one without a loss is `parsed`. v1 Battlegrounds
sources are never fit (`source_v1`). Metric definitions:
[analytics-metrics.md](analytics-metrics.md).

## Verification boundaries

Tests cover synthetic sources, malformed/bounded input, hidden-reference rejection, receipts/retry/deletion and
deterministic reprocessing. Separate acceptance requires real recorder input, both UI hosts and Linux/HTTPS
deployment. A cross-publish is not proof of Linux execution, and synthetic fixtures are not live-game coverage.
