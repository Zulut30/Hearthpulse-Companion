# Combat effects

How minion effects plug into the Battlegrounds combat simulator (`CombatSimulator`, engine in `../Simulation/`).

Rules for every effect:

- **Clean-room.** Implement from the card text and golden text (HearthstoneJSON / hsdata) and general Battlegrounds rules.
  Do not read, run or copy another simulator's code.
- **No hidden information.** Use only what the player can see: both boards, heroes, hero powers, trinkets, the
  anomaly, and the local player's own hand. The opponent's hand and secrets never reach the simulator. An effect that
  needs a hand overrides `NeedsHand => true`; it then stays "not simulated" on the opponent's side.
- **Add files, don't edit shared ones.** One class per card (or per group of cards with the same behaviour) in its
  own file under `Effects/<Family>/`. Registration is automatic (reflection over `[CardEffect]`); there is no list to edit.
- **AOT-compatible reflection.** New reflection here or in `Simulation/` must not make BGSim's Native AOT server warn
  (IL2xxx); suppress in place with a justification, as `EffectRegistry`/`CombatEffect` do (`tools/bgsim/README.md`).

## Adding an effect

```csharp
namespace Hearthpulse.Core.Battlegrounds.Effects.Deaths;

/// <summary>Harmless Bonehead (BG28_300): "Deathrattle: Summon two 1/1 Skeletons." Golden: four.</summary>
[CardEffect("BG28_300")]
internal sealed class HarmlessBonehead : CombatEffect
{
    public override void Deathrattle(Fight fight, Unit self) =>
        fight.SummonAt(self, Tokens.Skeleton(), self.Pick(2, 4));
}
```

- `[CardEffect("<normal card id>")]` covers normal and golden; read `self.Golden` / `self.Pick(normal, golden)`
  and follow the **golden card's own text** (doubled numbers, "twice", golden tokens, ×3...). For a separate golden
  class use `[CardEffect(id, Golden = GoldenFilter.GoldenOnly)]` (or pass the golden id). Several attributes may sit on
  one class. Enchantments, hero powers, trinkets and the Deity register the same way by their card ids.
- Effect objects are shared by all fights and threads: **no mutable fields**. Per-minion state: `Unit.Counter`,
  `Unit.Counter2` (start at the captured `TAG_SCRIPT_DATA_NUM_1/2`), `Unit.AvengeCount`. Per-player state:
  `Side.Counter(key)` / `AddCounter` / `SetCounter` (start at the player entity's captured tag of that name, else 0).
- Only override the hooks you need; the engine skips effects that do not override a hook.

## Hooks (`CombatEffect`)

| Hook | When |
|---|---|
| `OnFightStart(f, self)` | once per simulated fight, before the first attack (not a Start of Combat trigger: those resolved before capture) |
| `OnAttack(f, self, target)` | self attacks, **before damage**: Rally, "whenever this attacks" |
| `OnFriendlyAttack(f, self, attacker, target)` | a friendly minion attacks, before damage and before its own `OnAttack` (also called for self: check `attacker != self` for "another") |
| `OnFriendlyAttacked(f, self, defender, attacker)` | a friendly minion is attacked, before damage |
| `OnAttackDamage(f, self, target)` | during self's combat damage, after the main exchange: extra simultaneous damage (cleave) |
| `AfterAttack(f, self, target, outcome)` | after self attacked, before the death phase (`outcome.Killed`, `outcome.Excess`) |
| `AfterFriendlyAttack(f, self, attacker, target, outcome)` | after a friendly minion (possibly self) attacked |
| `OnDamaged(f, self, source, amount)` | self took damage (not when Divine Shield absorbed it) |
| `AfterFriendlyDealtDamage(f, self, dealer, victim, amount)` | a friendly minion dealt damage |
| `AfterKill(f, self, victim)` | self's damage or destroy killed `victim` |
| `OnFriendlyShieldLost(f, self, unit)` | a friendly minion lost Divine Shield |
| `Deathrattle(f, self)` | self died (already off the board) or `TriggerDeathrattles` was called; runs once per trigger |
| `ExtraDeathrattles(f, self)` | extra triggers for friendly deathrattles while self is alive (Titus: 1, golden 2) |
| `OnFriendlyDeathrattle(f, self, source)` | after each friendly deathrattle trigger |
| `AfterFriendlyDeath(f, self, dead)` / `AfterEnemyDeath` | after a death, once its deathrattles resolved; its Reborn comes after (Avenge: derive from `AvengeEffect`) |
| `AfterDeathPhase(f, self)` | once per side after each death-phase round, after its Reborn copies: "when you have space" waits (Diremuck Forager) |
| `OnSummoned(f, self)` | self was just summoned (token, reborn copy) |
| `WheneverFriendlySummoned` → `AfterFriendlySummoned(f, self, summoned)` | another friendly minion was summoned ("whenever" first) |
| `AfterFriendlyReborn(f, self, reborn)` | a friendly minion came back with Reborn (`reborn.RebornFrom` is the old one) |
| `RebornAs(f, dead, copy) → SimMinion` | what a dying minion with this effect comes back as with Reborn (before summon buffs and hooks); default: the fresh copy |
| `OnFriendlySpellCast(f, self, in spell)` | "whenever you cast a (Tavern) spell (on X)": before the spell resolves (`spell.Target`, `spell.IsTavernSpell`, `spell.Source`) |
| `AfterFriendlySpellCast(f, self, spellId, target)` | "after you cast": the spell resolved; `f.CurrentSpell` holds the whole cast |
| `AfterCardAddedToHand(f, self, cardId)` | "whenever a card is added to your hand" (`Fight.AddToHand`; empty id = a random card) |
| `AfterFriendlyBloodGem(f, self, target, source)` | a Blood Gem was played on a friendly minion (`Fight.PlayBloodGem`; Roogug) |
| `AuraFor(f, self, other) → StatDelta` | static aura onto a friendly minion; recomputed when minions enter or leave. Captured stats already include auras present at capture; the engine only applies changes (a lost Health aura lowers Health down to the new maximum only) |
| `Refresh(f, self)` | re-check state-dependent keywords/thresholds ("Taunt while it has Divine Shield", "once this reaches 6 Attack") after stat changes and board changes |
| `Battlecry(f, self)` | self's Battlecry when an effect triggers it in combat (`Fight.TriggerBattlecry`); once per trigger |
| `ExtraBattlecries(f, self)` | "Your Battlecries trigger twice" while self is alive (Brann: 1, golden 2); copies do not add up, the largest wins |
| `AddedBattlecries(f, self, source)` | extra triggers of `source`'s Battlecry that add up on top of the largest `ExtraBattlecries` (Dragon's Eye: 1 for a Dragon) |
| `AfterFriendlyBattlecry(f, self, source)` | after each friendly Battlecry trigger in combat, multipliers included ("after you trigger a Battlecry") |
| `ExtraSpellCasts(f, self, spellId, target)` | extra casts of a spell the owner casts in combat while self is alive (Balinda: 1 for a friendly target, golden 2); the largest wins |
| `AfterFriendlyGainedStats(f, self, unit, atk, hp)` | a friendly minion gained stats from `Fight.Buff` (the positive part; auras and summon buffs are not reported): Mishmash |

Properties: `NeedsHand` (the effect reads its owner's hand; stays "not simulated" for the opponent),
`ImmuneWhileAttacking` (the engine prevents all damage to the unit while its own attack resolves: Invulnerability),
`HasInputs(side, cardId)` + `CapturedInput` (a player-level card whose value grows over the game reads it from
`side.Info.ExtraTag(cardId, tag)`, the card's own tags at capture; false = "not simulated" with `missing_input`, never a
silent printed value; only `CombatEffect.CapturedTags` of the local player's cards are captured, never the opponent's),
`UncapturedInput` (a value no capture holds: always `missing_input`). A player card's TAG_SCRIPT_DATA_NUM_1/2 hold the
number its text shows now, not a bonus on top of the printed one (`Player.GrowingValue`; real logs), and balance changes on
the server without a new client: read a shown number from the capture, the HearthstoneJSON latest text is the fallback.

`AvengeEffect`: override `Threshold(self)` and `Avenge(f, self)`; it counts friendly deaths while self is alive.
The counter starts at 0 each fight (start-of-combat deaths before capture are not counted: progress is not read
from the log yet). Hero powers and trinkets derive from `Player.PlayerAvengeEffect`: X and the progress come from the
card's SCORE_VALUE_1 / SCORE_VALUE_2 at capture (on an Avenge minion in real logs SCORE_VALUE_2 counts the deaths and
returns to 0 when Avenge fires).

## Helpers (`Fight`)

- Board: `Alive(side, tribe, except)`, `Neighbours(unit)`, `unit.Position`, `side.Minions` (left to right, dying
  minions included until the death phase), `side.IsFull`, `side.DeadThisFight`, `side.DeathrattlesTriggered`.
- Decisions: every random pick goes through the fight's `ICombatChooser` (`Simulation/CombatChooser.cs`) with a
  `ChoiceKind`, the deciding unit and the candidates: seeded `RandomChooser` for the odds, the log's answers for a replay
  (Bench `simreplay`). Never touch `System.Random` in an effect (`SimReplayTests` fails on it); use
  `RandomFriendly(self, tribe, includeSelf)`, `RandomEnemy(self, tribe)`, `RandomOf(list, self)`,
  `RandomDistinct(list, n, self)`, `Best(list, key, self)` (tie-break), `BestCard(cards, eligible, key, self)` (hand
  tie-break), or `Choose(kind, self, units|cards)` for a pool (`RandomSummon`, `RandomCard`, `RandomHandCard`). Pass
  `self` as the source: a replay finds the answer in that source's triggered effect.
- Summons: `SummonAt(place, minion)` – in a dead minion's old slot (deathrattles), or right of a living one;
  `SummonAt(place, minion, count)`; `Summon(side, minion, index)`; `Summon(side, cardId, atk, hp, keywords, slot)`.
  All return null once the side has 7 minions (extra summons are lost). Token stats: `Tokens.*`
  (`Tokens.Make(id, atk, hp, tribe, golden, keywords)` for others).
- Stats and keywords: `Buff(unit, atk, hp)`, `SetStats`, `GiveKeywords`, `RemoveKeywords`; for "your X have +a/+b
  (including ones summoned later this combat)" also call `side.AddSummonBuff(tribe, atk, hp)`.
- Damage: `DealDamage(source, target, amount)` (Divine Shield, Poisonous and Venomous apply; the target dies at the
  next death phase), `Destroy(target, source)`, `unit.Killer`.
- Attacks: `GiveImmuneForThisAttack(attacker)` ("Immune while Attacking" for one attack only: Eclipsion Illidari),
  `AttackNow(attacker, target)` (synchronous, out of turn: "summon X to attack the target first"),
  `AttackSoon(attacker)` ("attacks immediately": right after the current attack).
- Deathrattles: `TriggerDeathrattles(unit, times, withMultiplier)`; `DeathrattleTriggers(side)`;
  `unit.HasDeathrattle`, `unit.IsRally`, `unit.AddEffect(effect)` (gain another minion's deathrattle).
- Battlecries in combat: `TriggerBattlecry(unit, times, withMultiplier)` (a Battlecry with no combat effect still counts
  as triggered), `BattlecryTriggers(side)`, `Fight.HasBattlecry(unit)`, side counter `Fight.BattlecriesTriggeredKey`.
- Spells in combat (`Simulation/Fight.Spells.cs`; spells and their reactions in `spells/`): `CastSpell(new SpellCast(side,
  cardId, target, source, resolver))` — `SpellCasts(side, spellId, target)` times (Balinda), each: "whenever" hooks, the
  resolver, "after" hooks. The simple `CastSpell(side, spellId, target, resolve)` goes the same way. A spell a minion or
  trinket casts is the player's cast (real log); a Tavern spell's "+X/+Y" adds the player's Tavern spell bonus
  (`TavernSpells.Stats`). `AddToHand(side, cardId, minion)` for "Get a card"; `PlayBloodGem(source, target, atk, hp)` for
  each gem (`BloodGems.Play` calls it) — a gem a minion plays is not a spell cast (real log: a separate card,
  BG20_GEM_No_Impact, without the player's "spell cast" trigger).
- Player context: `side.Info` (`HeroCardId`, `HeroPowerCardId`, `Extras`, `Tags`, `TavernTier`), `side.Hand` (local
  player only; always empty for the opponent), `side.ExtraDeathrattles`.
- Captured data on `unit.Spec`: `CardId` (normal id), `Golden`, `Tribes`, `BaseKeywords`, `RebornAttack`,
  `BaseHealth`, `ScriptData1/2`, `Enchantments`, `Remembered` (minions its start-of-combat trigger removed, e.g.
  Stitched Salvager's victim), `BloodGemAttack/Health` ("its Blood Gems", tags 4235/4236; `unit.BloodGemAttack` also
  counts gems played in the fight). `side.Info.RemovedByExtras`: minions a hero power or trinket removed at Start of
  Combat, by its card id (Rapid Reanimation, Soul Fermenter). `ScriptData3/4`: a second pair the text shows
  (Skitterer/Scorpid's "+X/+Y"). `Crew`: Timewarped Nellie's Ship's crew, the local player's own Discover pick (the
  opponent's is estimated, `SimInputs.ShipCrew`).
- Secrets (`secrets/`): The Great Akazamzarak's TB_Bacon_Secrets_* and the Eleventh Hour anomaly's Secret come from
  `SimSide.Secrets` (the local player's own) as player-level holders after the extras; each triggers once (`SecretEffect`,
  assumptions in its remarks). Ice Block and Eleventh Hour act only in the hero damage rule (`DamageRules.HasIceBlock`). The
  opponent's Secrets: during the game only how many (estimate `SimInputs.OpponentSecrets`), after it the ids the log
  showed (`SimSide.HiddenSecrets`, `SimOptions.PostGame`).
- Quests and waiting Tavern spells: the local player's cards in the SECRET zone (a quest, Boon of Beetles) are extras
  with `SimInputs.ObjectiveTags` in `ExtraTags` (script numbers, `QUEST_PROGRESS`, `QUEST_PROGRESS_TOTAL`,
  `QUEST_REWARD_DATABASE_ID`); quests in `quests/` count progress in the fight and attach their reward's effect when done.
- "Make Golden" in combat: `CombatBattlecry.BecomeGolden` (`ongoing/CombatBattlecries.cs`) replaces `unit.Spec`.

## Engine order (see `Fight` remarks)

attack declared → `OnFriendlyAttack` / `OnAttack` / `OnFriendlyAttacked` → death phase (attack cancelled if the
attacker or target died) → simultaneous combat damage + `OnAttackDamage` → damage hooks → `AfterAttack` /
`AfterFriendlyAttack` → death phase → queued "attacks immediately".

Death phase: remove every dead minion at once, in order of play (the starting minions of the side with the lower
lobby PLAYER_ID, then the other side's, each left to right, then minions summoned in combat in summon order) → per
dead minion: each deathrattle × (1 + extra triggers) before the next one (A, A, B, B) → "after a friendly/enemy minion
dies" hooks per death → each Reborn → `AfterDeathPhase` → auras → repeat while minions keep dying. Without both PLAYER_IDs the runs mix
both side orders evenly; a single traced fight takes the attacking side first.

Attack order: each side's next attacker is the first minion with Attack at or right of the slot just right of its
last attacker. The deathrattle tokens and reborn copy of that last attacker take its slot and attack next; other
minions summoned into a slot left of that point wait for the next round; minions summoned right of it attack in turn.

These rules are checked against 55 real fights (`CombatRulesEvidenceTests`, with the Power.log lines). A trigger chain
deeper than the engine follows, or endless death phases, is not dropped silently: the fight counts as
`CombatOdds.Truncated` and as one more "not simulated" item.

## Testing an effect

Tests live in `tests/Hearthpulse.Tests/Sim/`. `SimTestKit` builds boards and runs one deterministic fight:

```csharp
using static Hearthpulse.Tests.Sim.SimTestKit;

[Fact]
public void Bonehead_summons_two_skeletons()
{
    // Their 5/50 attacks first and kills the Bonehead.
    var result = Fight(
        Side(Minion(1, 1, cardId: "BG28_300", tribes: Tribe.Undead)),
        Side(Minion(5, 50)),
        oursFirst: false,
        picks: [0]);                      // scripted picks, one per decision in call order (then 0)
    Assert.Equal(2, result.Log.Count(l => l.StartsWith("summoned our BG_ICC_026t", StringComparison.Ordinal)));
}
```

Golden minions: `Minion(2, 2, cardId: "BG28_300", golden: true)` (the normal card id plus `golden: true`). Buffed
minions: `Minion(printedAtk, printedHp, ...) with { Attack = 10, Health = 10, MaxHealth = 10 }` keeps the printed
values as base stats (what Reborn brings back).

- `Fight(...)` uses `EffectRegistry.Default` (all effects in Hearthpulse.Core); pass
  `Effects(typeof(MyEffect))` to isolate one.
- `result.Ours` / `result.Theirs` are the survivors as `SimMinion`s; `result.Log` is a readable trace
  ("our X 1/1 attacks their Y 5/5", "… dies", "summoned …", "reborn …", "deathrattle of …").
- For odds, `CombatSimulator.Run(side, side, firstAttackerIsOurs, new SimOptions { Runs = …, Effects = … })`.
- `RelevanceTests.Built_in_effects_register_cleanly` fails on duplicate registrations.

## "Not simulated"

`CombatSimulator.Explain(side, isOpponent)` lists what a side counts, with the reason: `missing_effect`,
`missing_input` (the effect needs a value the capture lacks, the opponent's hidden hand, or a card in
`SimCoverage.InputGaps`), `unknown` (no table knows the card). `SimCoverage.Classify` gives every card one of six
statuses with the same rules; `Hearthpulse.Bench simmatrix` writes them for the current pool to docs/bg-sim-matrix.md.

A captured minion is counted in "N minion effects not simulated" when its card is combat-relevant
(`CombatRelevance`) and `EffectRegistry.Implements(cardId, golden, isOpponent)` is false; when an enchantment gave it
a deathrattle/Avenge/Rally and none of its enchantments explains it (a registered effect, or a `PlayerCardTable` row that
acts only in the Tavern); or when it carries a `PlayerCardTable` enchantment marked `Relevant` without an effect.
Player-level extras (hero powers, trinkets) count when their `PlayerCardTable` row is `Relevant` and has no effect.
Registering an effect therefore removes the flag; `NeedsHand` effects keep it for the opponent.

`PlayerCardRelevance.cs` (`PlayerCardTable`) is the hand-labelled table for hero powers, trinkets and Dark Gift
enchantments (English card text, patch 37.0); their effects live in `player/` and `deaths/DarkGifts.cs`. Coverage and
accuracy on real logs: `Hearthpulse.Bench simcover`, results in docs/bg-combat-sim.md.

`CombatRelevance.g.cs` is generated by `tools/effects/gen_combat_relevance.py`: the hand-labelled rows it already holds
(302 pool minions labelled by research for patch 37.0.0.255130, tokens) are carried over, plus
`tools/effects/relevance_additions.json` (minions research lacks, labelled by hand from the card text: Murcules, Bassgill,
Rot Hide Gnoll), plus Timewarped minions classified by the text heuristic from today's text. Today's data comes from a
snapshot of `tools/effects/fetch_bg_data.py` (HearthstoneJSON enUS and the Hearthpulse data API v1 with `in_pool`); the
same snapshot feeds `PlayerCardRelevance.g.cs` and the Ghastcoiler and Undead pools. After a patch: fetch a snapshot,
rerun the generators (commands in their headers); a pool minion no hand row knows is printed - label it in the JSON and
run again. Cards the table does not know fall back to `TextSuggestsCombatEffect` (English card text) or to their
mechanics (other locales), which over-report on purpose.
