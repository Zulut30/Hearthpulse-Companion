#!/usr/bin/env python3
"""Generates tests/Hearthpulse.Tests/Fixtures/Effects/effect-card-texts.json.

Battlegrounds balance changes on the server without a new client build, so a number an effect hard-codes (its
fallback when the minion carries no script data) can go stale. EffectCardTextGuardTests compares every such number
with the card text kept in this fixture. Regenerate the fixture after a balance patch, then run the tests: a failing
row names the effect whose numbers no longer match the card.

The fixture holds, for every card id an effect in Effects/attack, Effects/deaths or Effects/ongoing registers
([CardEffect("...")], [CardEffect(Tokens.XId)] or [CardEffect(Id)] with a local const), every token id in Tokens.cs
and the few extra ids below, the enUS name, text, Attack and Health from HearthstoneJSON, plus the golden version
(battlegroundsPremiumDbfId). Text is normalised: markup, [x] and line breaks removed, whitespace collapsed.

Usage (isolated interpreter; the card file is a download of
https://api.hearthstonejson.com/v1/latest/enUS/cards.json, passed as an argument):
  python3 -I tools/effects/gen_effect_texts.py <cards.json> [<repo root>] [--source "<Last-Modified header>"]
"""
import json
import os
import re
import sys

EFFECT_DIRS = ["attack", "deaths", "ongoing"]
# Cards an effect uses without registering them: the Blood Gem and the spell Crimson Vindicator casts.
EXTRA_IDS = ["BG20_GEM", "BG36_246"]
OUTPUT = os.path.join("tests", "Hearthpulse.Tests", "Fixtures", "Effects", "effect-card-texts.json")
EFFECTS = os.path.join("src", "Hearthpulse.Core", "Battlegrounds", "Effects")


def normalise(text):
    if not text:
        return ""
    text = re.sub(r"</?[a-z]+>", "", text)
    text = text.replace("[x]", "").replace(" ", " ").replace("\n", " ")
    return re.sub(r"\s+", " ", text).strip()


def token_ids(root):
    source = open(os.path.join(root, EFFECTS, "Tokens.cs"), encoding="utf-8").read()
    return dict(re.findall(r'public const string (\w+Id) = "([^"]+)";', source))


def effect_ids(root, tokens):
    ids = set()
    for folder in EFFECT_DIRS:
        path = os.path.join(root, EFFECTS, folder)
        for name in sorted(os.listdir(path)):
            if not name.endswith(".cs"):
                continue
            source = open(os.path.join(path, name), encoding="utf-8").read()
            consts = dict(re.findall(r'const string (\w+) = "([^"]+)";', source))
            for arg in re.findall(r"\[CardEffect\(([^,)]+)", source):
                arg = arg.strip()
                if arg.startswith('"'):
                    ids.add(arg.strip('"'))
                elif arg.startswith("Tokens."):
                    ids.add(tokens[arg[len("Tokens."):]])
                elif arg in consts:
                    ids.add(consts[arg])
                else:
                    raise SystemExit(f"{folder}/{name}: cannot resolve [CardEffect({arg})]")
    return ids


def main(argv):
    args = [a for a in argv[1:] if not a.startswith("--")]
    source = ""
    if "--source" in argv:
        source = argv[argv.index("--source") + 1]
        args = [a for a in args if a != source]
    if not args:
        raise SystemExit(__doc__)
    cards_path = args[0]
    root = args[1] if len(args) > 1 else os.getcwd()

    cards = json.load(open(cards_path, encoding="utf-8"))
    by_id = {c["id"]: c for c in cards}
    by_dbf = {c.get("dbfId"): c for c in cards}

    tokens = token_ids(root)
    wanted = effect_ids(root, tokens) | set(tokens.values()) | set(EXTRA_IDS)

    out = {}
    missing = []
    for card_id in sorted(wanted):
        card = by_id.get(card_id)
        if card is None:
            missing.append(card_id)
            continue
        entry = {
            "name": card.get("name", ""),
            "text": normalise(card.get("text")),
            "attack": card.get("attack"),
            "health": card.get("health"),
        }
        golden = by_dbf.get(card.get("battlegroundsPremiumDbfId"))
        if golden is not None and golden["id"] != card_id:
            entry["golden"] = {
                "id": golden["id"],
                "text": normalise(golden.get("text")),
                "attack": golden.get("attack"),
                "health": golden.get("health"),
            }
        out[card_id] = entry
    if missing:
        raise SystemExit("not in the card data: " + ", ".join(missing))

    fixture = {
        "source": "HearthstoneJSON v1/latest/enUS cards.json" + (f", Last-Modified {source}" if source else ""),
        "generator": "tools/effects/gen_effect_texts.py",
        "cards": out,
    }
    path = os.path.join(root, OUTPUT)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(fixture, f, ensure_ascii=False, indent=1, sort_keys=False)
        f.write("\n")
    print(f"{len(out)} cards -> {path}")


if __name__ == "__main__":
    main(sys.argv)
