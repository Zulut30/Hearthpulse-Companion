"""Builds the stats pool's archetype dictionary and card ids (TASK-2026-11-26-pro-collection, package B1).

The pool classifies game records itself (server/pool/node/records/derive/archetypes.mjs): the player's archetype from
the deck and the opponent's from the cards it showed, by the app's rules (GameRecords.DeckFits, ConstructedMeta.Match,
ArchetypeGuess.FromSeenCards). This script writes what those rules read:

- server/pool/data/archetypes.json: every archetype of HSGuru's catalogue, read through the Hearthpulse data API as
  the app reads it (v1/hsguru/archetypes?format=<f>&min_games=50&limit=500, HearthpulseData.LoadHsguruArchetypesAsync),
  with its class and core cards worked out as ApiConstructedMeta.ClassOf and ApiConstructedMeta.CoreCards do: the most
  common class of its lists, else the class its name spells out; a core card is in lists holding at least 60% of the
  archetype's games, each list weighing its games (at least 1). A printing is a card of its own, as in the app: each
  core card is listed once by card id (coreIds) and by dbf id (coreDbf), in the same order. Archetypes without a class
  or without core cards are left out (the app never matches them); within a class the most played come first. HSGuru's
  game counts stay out of the file: the order is all the app's tie rules need. `heroes` lists, by class, every
  HearthstoneJSON hero card that has a class: the class the app reads off a deck code's hero (DeckFits).
  `rules` names the dictionary, arch-<date>-<the first 8 hex digits of the SHA-256 of `formats` and `heroes`>: any change
  of the content is a new version, and the pool rebuilds every view with it (contract K2).
- server/pool/data/card-ids.json: dbf id -> card id of every collectible card of HearthstoneJSON (v1/latest, enUS) and
  every core card, and HearthstoneJSON's countAsCopyOfDbfId where both printings are listed.

Refresh at a balance patch, from the repository root (HEARTHPULSE_API points at another data API):

    python -B -I tools/archetypes/server_dictionary.py
    HP_REGEN_ARCHETYPES=1 node --test server/pool/test/derive-archetypes.test.mjs
    node --test server/pool/test/*.test.mjs
    dotnet test tests/Hearthpulse.Tests/Hearthpulse.Tests.csproj --filter "FullyQualifiedName~ArchetypeAgreement"

(PowerShell: $env:HP_REGEN_ARCHETYPES = '1' before the second line, Remove-Item Env:HP_REGEN_ARCHETYPES after it.)
The second line makes the agreement cases and their answers for the new dictionary; the .NET test then checks that the
app's classifier gives the same answers. Commit server/pool/data and the two test files together and deploy the pool.
One answer holds at most LIMIT archetypes, so the catalogue is read page by page (&offset=). Nothing is written when an
answer looks wrong (too few archetypes or cards) or the catalogue comes back cut short (fewer archetypes than the
answer counts, or a last page full to the limit without a count); --standard and --wild (an answer or its pages, in
order) and --cards build from saved answers instead of the network.
"""

import argparse
import base64
import binascii
import datetime
import hashlib
import json
import math
import os
import sys
import unicodedata
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "server" / "pool" / "data"
API = "https://api.kolodahearthstone.com/"
CARDS = "https://api.hearthstonejson.com/v1/latest/enUS/cards.json"
FORMATS = ("standard", "wild")
# The archetypes one catalogue answer holds at most: the app reads the first page, the dictionary every page.
LIMIT = 500
SOURCE = "HSGuru via api.kolodahearthstone.com"
CARDS_SOURCE = "HearthstoneJSON v1/latest/enUS/cards.json"

# ApiConstructedMeta.CoreShare and ApiConstructedMeta.Classes.
CORE_SHARE = 0.6
CLASSES = {"DEATHKNIGHT", "DEMONHUNTER", "DRUID", "HUNTER", "MAGE", "PALADIN", "PRIEST", "ROGUE", "SHAMAN", "WARLOCK", "WARRIOR"}

# ArchetypeNames.ClassWords, in its order: "Quest DH" is a Demon Hunter before it is a Hunter.
CLASS_WORDS = (
    ("death knight", "DEATHKNIGHT"), ("demon hunter", "DEMONHUNTER"), ("druid", "DRUID"), ("hunter", "HUNTER"),
    ("mage", "MAGE"), ("paladin", "PALADIN"), ("priest", "PRIEST"), ("rogue", "ROGUE"), ("shaman", "SHAMAN"),
    ("warlock", "WARLOCK"), ("warrior", "WARRIOR"),
)

# An answer with fewer is taken for a broken one: the files are left as they are.
MIN_ARCHETYPES = 30
MIN_CARDS = 5000


def fetch_json(url):
    request = urllib.request.Request(url, headers={"User-Agent": "Hearthpulse archetype dictionary", "Accept": "application/json"})
    with urllib.request.urlopen(request, timeout=120) as response:
        return json.loads(response.read().decode("utf-8"))


def catalog_url(api, fmt, offset):
    """The app's catalogue request (HearthpulseData.LoadHsguruArchetypesAsync), then the pages after it."""
    url = f"{api}v1/hsguru/archetypes?format={fmt}&min_games=50&limit={LIMIT}"
    return f"{url}&offset={offset}" if offset else url


def data_of(page):
    data = page.get("data") if isinstance(page, dict) else None
    return data if isinstance(data, list) else []


def count_of(page):
    """The number of archetypes the answer says the catalogue has (meta.count), or None."""
    meta = page.get("meta") if isinstance(page, dict) else None
    count = meta.get("count") if isinstance(meta, dict) else None
    return count if isinstance(count, int) and not isinstance(count, bool) else None


def fetch_catalog(api, fmt):
    """Every page of a format's catalogue: until the answer's count is held, else until a page comes back short."""
    pages = []
    held = 0
    while True:
        page = fetch_json(catalog_url(api, fmt, held))
        data = data_of(page)
        if pages and data and data[:1] == data_of(pages[-1])[:1]:
            raise ValueError(f"{fmt}: the page at offset {held} repeats the one before it (the API does not page)")
        pages.append(page)
        held += len(data)
        count = count_of(pages[0])
        if not data or (held >= count if count is not None else len(data) < LIMIT):
            return pages


def offset_of(page):
    """The answer's meta.offset, or None when it gives none."""
    meta = page.get("meta") if isinstance(page, dict) else None
    offset = meta.get("offset") if isinstance(meta, dict) else None
    return offset if isinstance(offset, int) and not isinstance(offset, bool) else None


def cut_short(fmt, pages):
    """Why the pages are not the whole catalogue, or None."""
    held = 0
    for page in pages:
        # A saved page given twice, or one left out, starts where the pages before it do not end.
        offset = offset_of(page)
        if offset is not None and offset != held:
            return f"{fmt}: a page starts at offset {offset}, the pages before it hold {held}"
        held += len(data_of(page))
    count = count_of(pages[0]) if pages else None
    if count is not None and held < count:
        return f"{fmt}: {held} of {count} archetypes"
    if count is not None and held > count:
        return f"{fmt}: {held} archetypes for a count of {count} (a page given twice)"
    if count is None and pages and len(data_of(pages[-1])) >= LIMIT:
        return f"{fmt}: {held} archetypes and no count, the last page full at the limit of {LIMIT}"
    return None


def read_json(path):
    with open(path, encoding="utf-8") as file:
        return json.load(file)


def number(value):
    """HearthpulseData.Num cast to int: a JSON number truncated toward zero, anything else 0."""
    if isinstance(value, bool) or not isinstance(value, (int, float)) or (isinstance(value, float) and not math.isfinite(value)):
        return 0
    return int(value)


def text(value):
    """HearthpulseData.Str: a JSON string, else None."""
    return value if isinstance(value, str) else None


def varint(data, position):
    result = 0
    shift = 0
    while True:
        byte = data[position]
        position += 1
        result |= (byte & 0x7F) << shift
        if not byte & 0x80:
            return result, position
        shift += 7


def decode_deck(code):
    """DeckString.TryDecode(code).Cards: dbf id -> copies in the order the code lists them (no sideboard), or None."""
    try:
        data = base64.b64decode("".join(code.split()), validate=True)
        position = 0
        reserved, position = varint(data, position)
        version, position = varint(data, position)
        if reserved != 0 or version != 1:
            return None
        _, position = varint(data, position)  # the format
        heroes, position = varint(data, position)
        for _ in range(heroes):
            _, position = varint(data, position)
        cards = {}
        for copies in (1, 2, 3):
            count, position = varint(data, position)
            for _ in range(count):
                dbf, position = varint(data, position)
                n = copies
                if copies == 3:
                    n, position = varint(data, position)
                cards[dbf] = cards.get(dbf, 0) + n
        return cards
    except (binascii.Error, ValueError, IndexError):
        return None


def words(name):
    """ArchetypeNames.Words: lower-case letters and digits; an apostrophe joins, anything else splits."""
    word = []
    for ch in name.lower():
        category = unicodedata.category(ch)
        if category[0] == "L" or category == "Nd":
            word.append(ch)
        elif ch not in ("'", "’") and word:
            yield "".join(word)
            word = []
    if word:
        yield "".join(word)


def normalize(name):
    """ArchetypeNames.Normalize: class abbreviations spelled out, "Harold" read as "Herald"."""
    out = []
    for word in words(name):
        if word in ("dh", "demonhunter"):
            out += ["demon", "hunter"]
        elif word in ("dk", "deathknight"):
            out += ["death", "knight"]
        elif word == "pally":
            out.append("paladin")
        elif word == "harold":
            out.append("herald")
        elif len(word) > 4 and word.endswith("lock") and word != "warlock":
            out += [word[:-4], "warlock"]
        else:
            out.append(word)
    return " ".join(out)


def class_from_name(name):
    """ArchetypeNames.ClassOf: the class the name spells out, or None."""
    padded = " " + normalize(name) + " "
    return next((cls for word, cls in CLASS_WORDS if " " + word + " " in padded), None)


def class_of(name, decks):
    """ApiConstructedMeta.ClassOf: the most common real class of the lists (the first seen on a tie), else the name's."""
    counts = {}
    for deck in decks:
        cls = deck["class"].replace(" ", "").upper()
        if cls in CLASSES:
            counts[cls] = counts.get(cls, 0) + 1
    if counts:
        best = max(counts.values())
        return next(cls for cls, count in counts.items() if count == best)
    return class_from_name(name)


def core_cards(lists, by_dbf):
    """ApiConstructedMeta.CoreCards: dbf ids in lists holding >= CORE_SHARE of the games, first seen first."""
    total = sum(max(1, games) for games, _ in lists)
    if total == 0:
        return []
    weights = {}
    for games, cards in lists:
        for dbf in cards:
            weights[dbf] = weights.get(dbf, 0) + max(1, games)
    return [dbf for dbf, weight in weights.items() if weight >= CORE_SHARE * total and dbf in by_dbf]


def fold(name):
    """StringComparer.OrdinalIgnoreCase's key: each character upper-cased on its own."""
    return "".join(ch.upper() if len(ch.upper()) == 1 else ch for ch in name)


def parse_catalog(pages):
    """HearthpulseData.ParseHsguruArchetypes over the pages' data in order, then the first of each name (any case)."""
    if not pages or not all(isinstance(page, dict) and isinstance(page.get("data"), list) for page in pages):
        raise ValueError("the answer has no data list")
    catalog = []
    seen = set()
    for item in (item for page in pages for item in page["data"]):
        if not isinstance(item, dict) or not text(item.get("archetype")):
            continue
        decks = []
        for deck in item.get("decks") if isinstance(item.get("decks"), list) else []:
            if isinstance(deck, dict) and text(deck.get("deck_code")):
                decks.append({"code": deck["deck_code"], "class": text(deck.get("class")) or "", "games": number(deck.get("games"))})
        key = fold(item["archetype"])
        if key not in seen:
            seen.add(key)
            catalog.append({"name": item["archetype"], "games": number(item.get("games")), "decks": decks})
    return catalog


def archetypes_of(catalog, by_dbf):
    """One format's classes: archetypes with a class and core cards, the most played first (stable), without the counts."""
    classes = {}
    for archetype in catalog:
        cls = class_of(archetype["name"], archetype["decks"])
        if cls is None:
            continue
        lists = [(deck["games"], cards) for deck in archetype["decks"] if (cards := decode_deck(deck["code"])) is not None]
        core = core_cards(lists, by_dbf)
        if not core:
            continue
        classes.setdefault(cls, []).append((archetype["games"], {
            "key": archetype["name"],
            "coreIds": [by_dbf[dbf]["id"] for dbf in core],
            "coreDbf": core,
        }))
    return {cls: [a for _, a in sorted(classes[cls], key=lambda entry: -entry[0])] for cls in sorted(classes)}


def heroes_of(by_dbf):
    """Class -> dbf ids of every hero card with a class, as the app's CardDb reads a deck code's hero (cardClass)."""
    heroes = {}
    for dbf, card in by_dbf.items():
        if card.get("type") == "HERO" and isinstance(card.get("cardClass"), str) and card["cardClass"]:
            heroes.setdefault(card["cardClass"], []).append(dbf)
    return {cls: sorted(heroes[cls]) for cls in sorted(heroes)}


def rules_of(formats, heroes, built):
    content = json.dumps({"formats": formats, "heroes": heroes}, ensure_ascii=False, separators=(",", ":"))
    return f"arch-{built.replace('-', '')}-{hashlib.sha256(content.encode('utf-8')).hexdigest()[:8]}"


def build(catalogs, cards, built):
    """The two files' contents from the catalogue (format -> its answer's pages) and HearthstoneJSON's card list."""
    by_dbf = {card["dbfId"]: card for card in cards if isinstance(card, dict) and isinstance(card.get("dbfId"), int)
              and card["dbfId"] > 0 and isinstance(card.get("id"), str)}
    formats = {fmt: archetypes_of(parse_catalog(catalogs[fmt]), by_dbf) for fmt in FORMATS}
    heroes = heroes_of(by_dbf)
    dictionary = {"v": 1, "rules": rules_of(formats, heroes, built), "built": built, "source": SOURCE,
                  "heroesSource": CARDS_SOURCE, "formats": formats, "heroes": heroes}

    listed = {dbf for dbf, card in by_dbf.items() if card.get("collectible") is True}
    listed.update(dbf for classes in formats.values() for archetypes in classes.values() for a in archetypes for dbf in a["coreDbf"])
    copy_of = {dbf: by_dbf[dbf].get("countAsCopyOfDbfId") for dbf in listed}
    ids = {
        "v": 1,
        "built": built,
        "source": CARDS_SOURCE,
        "cards": {dbf: by_dbf[dbf]["id"] for dbf in sorted(listed)},
        "copyOf": {dbf: target for dbf, target in sorted(copy_of.items()) if isinstance(target, int) and target in listed and target != dbf},
    }
    return dictionary, ids


def problems(dictionary, ids):
    found = [f"{fmt}: {sum(map(len, classes.values()))} archetypes" for fmt, classes in dictionary["formats"].items()
             if sum(map(len, classes.values())) < MIN_ARCHETYPES]
    if len(ids["cards"]) < MIN_CARDS:
        found.append(f"{len(ids['cards'])} cards")
    return found


def dictionary_text(dictionary):
    """One archetype a line and one class of heroes a line, so a refresh shows as changed archetypes."""
    compact = lambda value: json.dumps(value, ensure_ascii=False, separators=(",", ":"))
    lines = ["{"] + [f"  {compact(key)}: {compact(dictionary[key])}," for key in ("v", "rules", "built", "source", "heroesSource")]
    lines.append('  "formats": {')
    for f, (fmt, classes) in enumerate(dictionary["formats"].items()):
        lines.append(f"    {compact(fmt)}: {{")
        for c, (cls, archetypes) in enumerate(classes.items()):
            lines.append(f"      {compact(cls)}: [")
            lines += [f"        {compact(a)}{',' if i < len(archetypes) - 1 else ''}" for i, a in enumerate(archetypes)]
            lines.append(f"      ]{',' if c < len(classes) - 1 else ''}")
        lines.append(f"    }}{',' if f < len(dictionary['formats']) - 1 else ''}")
    heroes = list(dictionary["heroes"].items())
    lines += ["  },", '  "heroes": {']
    lines += [f"    {compact(cls)}: {compact(dbfs)}{',' if i < len(heroes) - 1 else ''}" for i, (cls, dbfs) in enumerate(heroes)]
    lines += ["  }", "}"]
    return "\n".join(lines) + "\n"


def ids_text(ids):
    """One card a line, by dbf id."""
    compact = lambda value: json.dumps(value, ensure_ascii=False, separators=(",", ":"))
    lines = ["{"] + [f"  {compact(key)}: {compact(ids[key])}," for key in ("v", "built", "source")]
    for name in ("cards", "copyOf"):
        rows = list(ids[name].items())
        lines.append(f"  {compact(name)}: {{")
        lines += [f"    {compact(str(dbf))}: {compact(value)}{',' if i < len(rows) - 1 else ''}" for i, (dbf, value) in enumerate(rows)]
        lines.append("  }," if name == "cards" else "  }")
    lines.append("}")
    return "\n".join(lines) + "\n"


def write(path, content):
    temp = path.with_name(path.name + ".tmp")
    with open(temp, "w", encoding="utf-8", newline="\n") as file:
        file.write(content)
    os.replace(temp, path)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n", 1)[0])
    parser.add_argument("--standard", nargs="+", help="a saved v1/hsguru/archetypes answer for Standard, or its pages in order")
    parser.add_argument("--wild", nargs="+", help="a saved v1/hsguru/archetypes answer for Wild, or its pages in order")
    parser.add_argument("--cards", help="a saved HearthstoneJSON cards.json")
    parser.add_argument("--out", default=str(OUT), help="the folder for archetypes.json and card-ids.json")
    parser.add_argument("--date", default=datetime.date.today().isoformat(), help="the build date, YYYY-MM-DD")
    args = parser.parse_args(argv)
    datetime.date.fromisoformat(args.date)

    api = os.environ.get("HEARTHPULSE_API") or API
    api = api if api.endswith("/") else api + "/"
    saved = {"standard": args.standard, "wild": args.wild}
    try:
        catalogs = {fmt: [read_json(path) for path in saved[fmt]] if saved[fmt] else fetch_catalog(api, fmt) for fmt in FORMATS}
        if cut := [reason for fmt in FORMATS if (reason := cut_short(fmt, catalogs[fmt]))]:
            sys.exit("The catalogue answer is cut short (" + "; ".join(cut) + "): the files are left as they are.")
        cards = read_json(args.cards) if args.cards else fetch_json(CARDS)
        dictionary, ids = build(catalogs, cards if isinstance(cards, list) else [], args.date)
    except (OSError, ValueError) as error:
        sys.exit(f"Could not read the catalogue or the cards ({error}): the files are left as they are.")
    if found := problems(dictionary, ids):
        sys.exit("Too little data (" + ", ".join(found) + "): the files are left as they are.")

    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    write(out / "archetypes.json", dictionary_text(dictionary))
    write(out / "card-ids.json", ids_text(ids))
    counts = ", ".join(f"{fmt} {sum(map(len, classes.values()))}" for fmt, classes in dictionary["formats"].items())
    print(f"{dictionary['rules']}: archetypes {counts}; {len(ids['cards'])} cards -> {out}")


if __name__ == "__main__":
    main()
