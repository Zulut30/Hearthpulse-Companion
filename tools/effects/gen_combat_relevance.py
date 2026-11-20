#!/usr/bin/env python3
"""Generates src/Hearthpulse.Core/Battlegrounds/Effects/CombatRelevance.g.cs.

The table says, per Battlegrounds minion (normal and golden card id), whether its card text changes a
fight after the boards are captured at the first attack. The overlay's "N minion effects not simulated"
note counts minions that are combat-relevant and have no registered effect implementation.

Inputs:
  table        the current generated table. Its hand-labelled rows are carried over as they are: pool minions
               (Origin.Pool, labelled from the card text for patch 37.0.0.255130 by research files that are not kept in
               the repository, plus later additions) and tokens (Origin.Token).
  --data       a snapshot of tools/effects/fetch_bg_data.py: HearthstoneJSON enUS (texts, golden ids) and the
               Hearthpulse data API v1 (the current pool's minions with `in_pool`, the Timewarped library).
  --additions  relevance_additions.json: minions the carried rows lack, labelled by hand from the card text - pool
               minions, and tokens or generated minions seen on boards ("origin": "token"); kept in this folder and always
               worth passing.

Rows, in this order (a card keeps the first label it gets):
  1. the carried hand-labelled rows;
  2. the additions;
  3. pool minions of the snapshot no hand row knows: labelled by the text heuristic below (Origin.Heuristic) and
     listed on stderr - label them by hand in the additions and run again;
  4. Timewarped minions of the snapshot: labelled by the text heuristic from today's text (Origin.Heuristic);
  5. carried heuristic rows of cards the snapshot no longer lists, as they were.

Usage (isolated interpreter; the snapshot folder is data passed as an argument):
  python3 -I tools/effects/fetch_bg_data.py <snapshot>
  python3 -I tools/effects/gen_combat_relevance.py src/Hearthpulse.Core/Battlegrounds/Effects/CombatRelevance.g.cs \
      src/Hearthpulse.Core/Battlegrounds/Effects/CombatRelevance.g.cs --data <snapshot> \
      --additions tools/effects/relevance_additions.json

The text heuristic must stay in sync with CombatRelevance.TextSuggestsCombatEffect (C#), which is the
runtime fallback for cards a new patch adds before this table is regenerated.
"""
import argparse
import json
import pathlib
import re
import sys

COMBAT_VERB = (r"(Summon|summon|Trigger|trigger|Give|give|Gain|gain|Deal|deal|Destroy|destroy|Remove|remove|Cast|cast|"
               r"plays? \d* ?(a )?Blood Gem|Reborn|Divine Shield|Attack|have \+|Blood \|4)")
NOT_COMBAT = (r"^(Get|get|Add|You and your teammate|Your teammate|Reduce|Your Tavern spells|Gain \d+ free|"
              r"Gain .*Refresh|Give minions in the Tavern|After the Tavern)")


def normalize(text):
    text = (text or "").split("@")[0]
    text = re.sub(r"<[^>]+>", "", text)
    text = text.replace("[x]", "").replace("_", " ").replace("\n", " ")
    return re.sub(r"\s+", " ", text).strip()


def clause(text, key):
    m = re.search(key + r"(?: \(\d+\))?: (.*?)(?=(?:Deathrattle|Rally|Avenge|Battlecry)[^:]*:|$)", text)
    return m.group(1).strip() if m else None


def needs_sim(raw):
    """Text heuristic from research/scripts/heuristic_eval.py, plus a few patterns for damage, kills and deaths."""
    t = normalize(raw)
    reasons = []
    for key in ("Deathrattle", "Rally", "Avenge"):
        c = clause(t, key)
        if c and re.search(COMBAT_VERB, c) and not re.match(NOT_COMBAT, c):
            reasons.append(key)
    if re.search(r"(friendly|another|other)[^.]*\battacks\b|After this attacks|Whenever this attacks|is attacked", t):
        reasons.append("on_attack")
    if re.search(r"Also damages|excess damage", t):
        reasons.append("cleave")
    if re.search(r"(Whenever|After) you summon|summon a (Beast|minion) in combat", t):
        reasons.append("on_summon")
    if re.search(r"is Reborn|deals damage, gain|that died this (game|combat)|Deathrattle you've triggered|Deathrattles trigger|"
                 r"while this has Divine Shield|Once this reaches", t):
        reasons.append("dynamic")
    if re.search(r"rest of this combat|When you have space|attack immediately", t):
        reasons.append("persistent_soc")
    # Additions (not in the research heuristic): damage taken, kills and friendly deaths during combat.
    if re.search(r"(Whenever|After) this takes damage|(Whenever|After) this kills|attacks and kills|"
                 r"(Whenever|After) a friendly [A-Za-z ]*dies|loses Divine Shield", t):
        reasons.append("combat_trigger")
    return reasons


RELEVANCE = {
    "y": "Relevant",
    "conditional": "Conditional",
    "resolved": "Resolved",
    "simulated": "Keywords",
    "n": "None",
}

ROW = re.compile(r'new\("([^"]*)", "([^"]*)", Relevance\.(\w+), "([^"]*)", "((?:[^"\\]|\\.)*)", Origin\.(\w+)\),')


def read_table(path):
    """The rows and the hand-label note of a generated table."""
    text = open(path, encoding="utf-8").read()
    # A table this script wrote names its hand labels; an older one only its research source.
    labels = re.search(r"^// Hand labels: (.*)$", text, re.M) or re.search(r"^// Source: (.*)$", text, re.M)
    rows = [(m[1], m[2], m[3], m[4], m[5].replace('\\"', '"').replace("\\\\", "\\"), m[6].lower())
            for m in ROW.finditer(text)]
    if not labels or not rows:
        sys.exit(f"{path} is not a generated relevance table")
    return {"labels": labels.group(1), "rows": rows}


def cs(s):
    return '"' + (s or "").replace("\\", "\\\\").replace('"', '\\"') + '"'


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("table")
    ap.add_argument("output")
    ap.add_argument("--data", required=True)
    ap.add_argument("--additions")
    args = ap.parse_args()

    sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
    from bgdata import Snapshot  # noqa: E402

    data = Snapshot(args.data)
    carried = read_table(args.table)
    additions = json.load(open(args.additions, encoding="utf-8")) if args.additions else {"minions": []}

    entries = []  # (normal, golden, relevance, category, name, origin)
    seen = set()

    def add(normal, golden, relevance, category, name, origin):
        if normal in seen or (golden and golden in seen):
            return False
        seen.add(normal)
        if golden:
            seen.add(golden)
        entries.append((normal, golden or "", relevance, category, name, origin))
        return True

    hand = [r for r in carried["rows"] if r[5] in ("pool", "token")]
    heuristic = {r[0]: r for r in carried["rows"] if r[5] == "heuristic"}
    for row in hand:
        add(*row)
    for r in additions["minions"]:
        add(r["id"], r["golden_id"], RELEVANCE[r["combat_relevant"]], r["category"], r["name"], r.get("origin", "pool"))

    unlabelled = []
    for card, _ in sorted(data.pool_minions(), key=lambda cd: cd[0]["id"]):
        reasons = needs_sim(card.get("text"))
        if add(card["id"], data.golden_id(card["id"]), "Relevant" if reasons else "None",
               "UNLABELLED_" + (reasons[0].upper() if reasons else "OTHER"), card.get("name", ""), "heuristic"):
            unlabelled.append(card)

    relabelled = []
    for card in sorted(data.timewarped_minions(), key=lambda c: c["id"]):
        reasons = needs_sim(card.get("text"))
        # A Start of Combat text without a later combat trigger resolved before the capture (relevance audit, 2026-11-19:
        # 13 such Timewarped minions were "None").
        start_of_combat = not reasons and "Start of Combat" in normalize(card.get("text"))
        relevance = "Relevant" if reasons else "Resolved" if start_of_combat else "None"
        old = heuristic.get(card["id"])
        category = old[3] if old and old[2] == relevance else "TIMEWARPED_" + (
            reasons[0].upper() if reasons else "START_OF_COMBAT" if start_of_combat else "OTHER")
        if old and old[2] != relevance:
            relabelled.append((card["id"], card.get("name", ""), old[2], relevance))
        add(card["id"], data.golden_id(card["id"]) or (old[1] if old else ""), relevance, category, card.get("name", ""), "heuristic")
    for row in heuristic.values():
        add(*row)

    # Heuristic quality on the hand-labelled rows of today's pool, printed for the record.
    pool_ids = {c["id"] for c, _ in data.pool_minions()}
    tp = fp = fn = 0
    misses = []
    for normal, _, relevance, _, name, origin in entries:
        if origin != "pool" or normal not in pool_ids:
            continue
        pred = bool(needs_sim(data.by_id[normal].get("text")))
        truth = relevance == "Relevant"
        tp += pred and truth
        fp += pred and not truth
        fn += truth and not pred
        if pred != truth:
            misses.append(f"{'missed' if truth else 'false alarm'}: {normal} {name} ({relevance})")
    print(f"heuristic on the hand-labelled pool: precision={tp / max(1, tp + fp):.2f} recall={tp / max(1, tp + fn):.2f} "
          f"(tp={tp} fp={fp} fn={fn})", file=sys.stderr)
    for line in misses:
        print("  " + line, file=sys.stderr)
    for card in unlabelled:
        print(f"UNLABELLED pool minion {card['id']} {card.get('name', '')}: label it in relevance_additions.json", file=sys.stderr)
    for cid, name, old, new in relabelled:
        print(f"relabelled Timewarped {cid} {name}: {old} -> {new}", file=sys.stderr)

    lines = [
        "// <auto-generated>",
        "// Generated by tools/effects/gen_combat_relevance.py; do not edit by hand. Regenerate after a patch (see the",
        "// script header): fetch_bg_data.py <snapshot>, then gen_combat_relevance.py <this file> <this file> --data <snapshot>",
        "//     --additions tools/effects/relevance_additions.json",
        f"// Source: {data.source}",
        f"// Hand labels: {carried['labels']}",
        "// Pool rows are labelled by hand from card text; Timewarped rows come from the text heuristic.",
        *([f"// Additions: {additions['source']}"] if additions["minions"] else []),
        "// </auto-generated>",
        "",
        "namespace Hearthpulse.Core.Battlegrounds.Effects;",
        "",
        "internal static partial class CombatRelevanceTable",
        "{",
        f"    internal const string Patch = {cs(data.patch)};",
        "",
        "    internal static readonly CardRelevance[] Entries =",
        "    [",
    ]
    for normal, golden, relevance, category, name, origin in entries:
        lines.append(f"        new({cs(normal)}, {cs(golden)}, Relevance.{relevance}, {cs(category)}, {cs(name)}, Origin.{origin.capitalize()}),")
    lines += ["    ];", "}", ""]
    with open(args.output, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines))
    counts = {}
    for e in entries:
        counts[e[2]] = counts.get(e[2], 0) + 1
    print(f"wrote {len(entries)} cards ({counts}) to {args.output}", file=sys.stderr)


if __name__ == "__main__":
    main()
