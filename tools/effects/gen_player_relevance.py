#!/usr/bin/env python3
"""Generates src/Hearthpulse.Core/Battlegrounds/Effects/PlayerCardRelevance.g.cs.

Every Battlegrounds player-level card the simulator may get as an extra (hero powers, trinkets with their golden
versions, quest rewards, anomalies), every quest and every Tavern spell gets a row: does it act in a fight after the
boards are captured? The hand table PlayerCardTable (PlayerCardRelevance.cs) wins where it has the card; the rest is
labelled here from the English card text. With every known card in a table, a card missing from both is newer than the
data and the simulator counts it as "not simulated" (it cannot know) instead of silently as simulated.

Labels, per sentence of the text:
  Relevant  - a combat trigger (a friendly minion attacks, dies, takes damage, kills, loses a keyword, is summoned or
              Reborn; Avenge; "each combat", "in combat"; an aura "your minions have +X") with an effect that changes
              the fight (give, summon, deal, destroy, trigger, play a Blood Gem, a keyword); not a reward for the Tavern
              (get, Discover, Gold, a Refresh) or a stat for later ("your Blood Gems give an extra...").
  Resolved  - only a Start of Combat effect, done before the boards are captured.
  None      - Tavern only.
Quests (QU): Conditional when their task can progress during a fight (minions attack, die, are destroyed or summoned,
  spells are cast, stats grow): the fight matters only if the quest completes in it and the reward acts at once;
  None for Tavern tasks (buy, sell, spend Gold, Refresh, add cards, lose or tie combats, order the board).
Tavern spells (SP): Relevant when a combat effect of another card in today's game casts it (a Rally, Deathrattle, Avenge
  or combat trigger that says "cast <spell>" or "plays a Blood Gem"): the simulator must resolve the spell in the fight; else the
  sentence labels above (a spell that leaves a combat trigger behind, "When you have space in combat", is Relevant).

Prints how the text labels compare with the hand table, the trinkets the text calls Relevant that the hand research
(meant to hold every combat trinket) misses, and the spells cast in combat with their casters.

Usage (isolated interpreter; the snapshot of tools/effects/fetch_bg_data.py passed as an argument):
  python3 -I tools/effects/gen_player_relevance.py <snapshot> src/Hearthpulse.Core/Battlegrounds/Effects/PlayerCardRelevance.g.cs
"""
import pathlib
import re
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from bgdata import Snapshot  # noqa: E402
from gen_combat_relevance import clause, cs, normalize  # noqa: E402

HAND = pathlib.Path(__file__).resolve().parents[2] / 'src/Hearthpulse.Core/Battlegrounds/Effects/PlayerCardRelevance.cs'
TYPES = {'HERO_POWER': 'HP', 'BATTLEGROUND_TRINKET': 'TR', 'BATTLEGROUND_QUEST_REWARD': 'QR', 'BATTLEGROUND_ANOMALY': 'AN',
         'BATTLEGROUND_SPELL': 'SP'}
BLOOD_GEM = 'BG20_GEM'

TRIGGER = re.compile(
    r"\b(Whenever|After|When)\b[^,.:]*\b(attacks?|dies|die|takes damage|kills|loses|is Reborn|summons?|"
    r"trigger a (Deathrattle|Rally)|awakens|space)\b|\bAvenge \(\d+\)|\b(each|in|this|during) combat\b|"
    r"^Your (minions|[A-Z][a-z]+s) have \+|^Your first Deathrattle|\bYour [A-Za-z' ]*(Deathrattles|Rallies)\b|^Give .* during combat",
    re.IGNORECASE)
EFFECT = re.compile(
    r"\b(give|gives|gain \+|gains \+|double|summon|resummon|deal|deals|destroy|trigger|triggers|"
    r"plays? (a )?(permanent )?Blood Gems?|have \+|attack immediately|an extra time|target|Divine Shield|Reborn)\b", re.IGNORECASE)
REWARD = re.compile(r"\b(get|gets|Discover|Gold|Refresh|replace this|keep|Blood Gems give|Tavern spells give)\b", re.IGNORECASE)
START_OF_COMBAT = re.compile(r"^Start of Combat:\s*", re.IGNORECASE)
TAVERN_QUEST = re.compile(r"^Quest: (Buy|Spend|Sell|Add|Play|End your turn|Have the Tavern Refreshed|Lose or tie|Order your)",
                          re.IGNORECASE)
CAST = re.compile(r"\b[Cc]asts? ((?:[A-Z][A-Za-z'-]*)(?: (?:[A-Z][A-Za-z'-]*|of|the|a|in))*)")
PLAYS_GEM = re.compile(r"\bplays? (\d+ |a |an |two |three )?(extra )?Blood Gems?\b", re.IGNORECASE)
SPELL_ON_THIS = re.compile(r"^Whenever you cast a spell on (this|a [A-Za-z]+), it casts", re.IGNORECASE)


def sentences(text):
    """The text's sentences, without the "(Improved by...)" and "(N left!)" notes."""
    notes = r"\((\d+ left!|Improved by[^)]*|[^)]*times per combat\.?|[^)]*\bgame!?)\)"
    return [s.strip() for s in re.split(r"(?<=[.!])\s+", re.sub(notes, "", text)) if s.strip()]


def acts_in_combat(sentence):
    if not TRIGGER.search(sentence):
        return False
    effect = re.split(r"[,:]", sentence, maxsplit=1)[-1] if re.search(r"[,:]", sentence) else sentence
    return bool(EFFECT.search(effect)) and not REWARD.search(effect)


def label(card):
    text = normalize(card.get('text', ''))
    # a sentence about Start of Combat effects acts before the boards are captured
    after_capture = [s for s in sentences(text) if 'Start of Combat' not in s]
    if any(acts_in_combat(s) for s in after_capture):
        return 'Relevant'
    if START_OF_COMBAT.search(text) or 'Start of Combat' in text:
        return 'Resolved'
    return 'None'


def combat_parts(card):
    """The parts of a card's text that run during a fight: Deathrattle, Rally and Avenge clauses, combat-trigger
    sentences, and "whenever you cast a spell on this, it casts..." (a spell cast in combat sets it off)."""
    text = normalize(card.get('text', ''))
    parts = [c for c in (clause(text, key) for key in ('Deathrattle', 'Rally', 'Avenge')) if c]
    for s in sentences(text):
        if 'Start of Combat' not in s and (TRIGGER.search(s) or SPELL_ON_THIS.search(s)):
            parts.append(s)
    return parts


def combat_casts(data):
    """Spell card id -> ids of the cards in today's game whose combat text casts it (by the spell's name), Blood Gems
    included. A caster out of the pool no longer casts anything in a fight."""
    in_game = data.in_game()
    spells_by_name = {}
    for card in data.cards:
        if card.get('type') == 'BATTLEGROUND_SPELL' and card.get('set') == 'BATTLEGROUNDS' and not card.get('battlegroundsNormalDbfId'):
            # the pool's printing first when several spells share a name
            spells_by_name.setdefault(card.get('name', ''), []).append(card)
    for cards in spells_by_name.values():
        cards.sort(key=lambda c: (not c.get('isBattlegroundsPoolSpell'), c['id']))
    casts = {}
    for card in data.cards:
        if card.get('set') != 'BATTLEGROUNDS' or card.get('type') == 'ENCHANTMENT' or (in_game is not None and card['id'] not in in_game):
            continue
        for part in combat_parts(card):
            named = [m.group(1) for m in CAST.finditer(part)]
            for name in named:
                # "Cast Mighty Dragonbreath" -> the longest prefix of the words that names a spell
                words = name.split()
                hit = next((spells_by_name[' '.join(words[:n])][0] for n in range(len(words), 0, -1)
                            if ' '.join(words[:n]) in spells_by_name), None)
                if hit:
                    casts.setdefault(hit['id'], set()).add(card['id'])
            if PLAYS_GEM.search(part):
                casts.setdefault(BLOOD_GEM, set()).add(card['id'])
    return casts


def hand_rows():
    rows = {}
    for kind, cid in re.findall(r'(Relevant|Tavern)\("([^"]+)"', HAND.read_text(encoding='utf-8')):
        rows[cid] = 'Relevant' if kind == 'Relevant' else 'None'
    return rows


def player_cards(cards):
    for card in cards:
        kind = TYPES.get(card.get('type'))
        if card.get('type') == 'SPELL' and card.get('set') == 'BATTLEGROUNDS' and 'QUEST' in (card.get('mechanics') or []):
            kind = 'QU'
        if kind and (card.get('set') == 'BATTLEGROUNDS' or kind not in ('HP', 'SP')):
            yield kind, card


def main():
    data = Snapshot(sys.argv[1])
    hand = hand_rows()
    casts = combat_casts(data)
    cards = list(player_cards(data.cards))
    if BLOOD_GEM in data.by_id and not any(c['id'] == BLOOD_GEM for _, c in cards):
        cards.append(('SP', data.by_id[BLOOD_GEM]))
    rows, disagree, agree = [], [], 0
    for kind, card in sorted(cards, key=lambda kc: kc[1]['id']):
        cid = card['id']
        guess = label(card)
        category = f'{kind}_{guess.upper()}'
        if kind == 'QU':
            guess = 'None' if TAVERN_QUEST.search(normalize(card.get('text', ''))) else 'Conditional'
            category = 'QU_TAVERN' if guess == 'None' else 'QU_COMBAT_PROGRESS'
        elif kind == 'SP' and cid in casts:
            # normal casters only (a golden one casts the same spell); a long list is cut, the count stays
            casters = sorted(c for c in casts[cid] if not data.by_id[c].get('battlegroundsNormalDbfId'))
            shown = ','.join(casters[:4]) + (f',+{len(casters) - 4}' if len(casters) > 4 else '')
            guess, category = 'Relevant', f'SP_COMBAT_CAST({shown})'
        if cid in hand:
            if hand[cid] == guess:
                agree += 1
            else:
                disagree.append((cid, card.get('name', ''), f'hand {hand[cid]}, text {guess}'))
            continue
        if kind == 'TR' and guess == 'Relevant':
            disagree.append((cid, card.get('name', ''), 'text Relevant, not in the hand table'))
        rows.append((cid, guess, category, card.get('name', '')))
    out = ['// <auto-generated> tools/effects/gen_player_relevance.py from a card data snapshot (tools/effects/fetch_bg_data.py); do not edit.',
           f'// {data.source}.',
           '// Player-level cards, quests and Tavern spells the hand table (PlayerCardRelevance.cs) does not hold, labelled from their',
           '// English text.',
           'namespace Hearthpulse.Core.Battlegrounds.Effects;', '',
           'internal static class PlayerCardGenerated', '{',
           '    internal static readonly CardRelevance[] Entries =', '    [']
    for cid, rel, cat, name in rows:
        out.append(f'        new({cs(cid)}, "", Relevance.{rel}, {cs(cat)}, {cs(name)}, Origin.Player),')
    out += ['    ];', '}', '']
    pathlib.Path(sys.argv[2]).write_bytes('\r\n'.join(out).encode('utf-8'))
    counts = {}
    for _, _, cat, _ in rows:
        key = cat.split('(')[0]
        counts[key] = counts.get(key, 0) + 1
    print(f'{len(rows)} generated rows; by kind: {dict(sorted(counts.items()))}')
    print(f'hand table: {len(hand)} rows, {agree} labelled the same from the text')
    print(f'{len(disagree)} differences:')
    for cid, name, why in disagree:
        print(f'  {cid:32} {name:32} {why}')
    print('spells cast in combat:')
    for cid, casters in sorted(casts.items()):
        print(f'  {cid:16} {data.by_id[cid].get("name", ""):28} by {", ".join(sorted(casters))}')


if __name__ == '__main__':
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    main()
