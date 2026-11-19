"""The card data snapshot of tools/effects/fetch_bg_data.py, shared by the effect table generators.

A snapshot folder holds HearthstoneJSON enUS (cards.enUS.json: texts, stats, mechanics, golden links, hero powers) and
the Hearthpulse data API v1 libraries (api-<library>.json, with the current pool flag `in_pool`), plus snapshot.json
(build, patch, dates). The generators record `Snapshot.source` in their headers, so a table says which data it is.
"""
import json
import pathlib

TRIBES = {
    "BEAST": "Tribe.Beast",
    "DEMON": "Tribe.Demon",
    "DRAGON": "Tribe.Dragon",
    "ELEMENTAL": "Tribe.Elemental",
    "MECHANICAL": "Tribe.Mech",
    "MURLOC": "Tribe.Murloc",
    "NAGA": "Tribe.Naga",
    "PIRATE": "Tribe.Pirate",
    "QUILBOAR": "Tribe.Quilboar",
    "UNDEAD": "Tribe.Undead",
    "ABERRATION": "Tribe.Aberration",
    "ALL": "Tribe.All",
}

KEYWORDS = {
    "TAUNT": "Keyword.Taunt",
    "DIVINE_SHIELD": "Keyword.DivineShield",
    "POISONOUS": "Keyword.Poisonous",
    "VENOMOUS": "Keyword.Venomous",
    "REBORN": "Keyword.Reborn",
    "WINDFURY": "Keyword.Windfury",
    "MEGA_WINDFURY": "Keyword.MegaWindfury",
    "STEALTH": "Keyword.Stealth",
}

MECHANICS = {
    "DEATHRATTLE": "CardMechanic.Deathrattle",
    "BACON_RALLY": "CardMechanic.Rally",
    "AVENGE": "CardMechanic.Avenge",
    "BATTLECRY": "CardMechanic.Battlecry",
    "START_OF_COMBAT": "CardMechanic.StartOfCombat",
    "MAGNETIC": "CardMechanic.Magnetic",
}


def flags(values, table, none):
    parts = [table[v] for v in values if v in table]
    return " | ".join(parts) if parts else none


class Snapshot:
    def __init__(self, folder):
        self.folder = pathlib.Path(folder)
        self.meta = json.loads((self.folder / "snapshot.json").read_text(encoding="utf-8"))
        self.cards = json.loads((self.folder / "cards.enUS.json").read_text(encoding="utf-8"))
        self.by_id = {c["id"]: c for c in self.cards}
        self.by_dbf = {c["dbfId"]: c for c in self.cards if "dbfId" in c}
        self._libraries = {}

    @property
    def build(self):
        return self.meta["build"]

    @property
    def patch(self):
        return self.meta.get("patch") or str(self.build)

    @property
    def source(self):
        """One line for a generated header: which data, which patch, from when."""
        return (f"HearthstoneJSON enUS build {self.build} ({self.meta.get('hsjson_date', '')}) and the Hearthpulse data API v1 "
                f"(in_pool, fetched {self.meta['fetched_at']}); patch {self.patch}")

    def library(self, name):
        if name not in self._libraries:
            path = self.folder / f"api-{name}.json"
            self._libraries[name] = json.loads(path.read_text(encoding="utf-8"))["data"] if path.exists() else []
        return self._libraries[name]

    def golden_id(self, card_id):
        card = self.by_id.get(card_id) or {}
        golden = self.by_dbf.get(card.get("battlegroundsPremiumDbfId"))
        return golden["id"] if golden else ""

    def pool_minions(self):
        """The Tavern pool's minions (normal cards) with their Duos-only flag: API `in_pool`, else HearthstoneJSON."""
        api = [a for a in self.library("cards")
               if a.get("in_pool") and (a.get("card_type") or {}).get("slug") == "minion"
               and (a.get("variant") or {}).get("kind", "base") == "base"]
        if api:
            return [(self.by_id[a["card_id"]], bool(a.get("duos_only"))) for a in api if a["card_id"] in self.by_id]
        return [(c, bool(c.get("isBattlegroundsDuosExclusive"))) for c in self.cards
                if c.get("isBattlegroundsPoolMinion") and c.get("type") == "MINION" and not c.get("battlegroundsNormalDbfId")]

    def in_game(self):
        """Card ids in today's game by the API (pool cards, Timewarped cards, minions seen on boards this patch, library
        items with `in_pool`, every hero and its hero power; golden versions too), or None when the snapshot has no API
        libraries."""
        if not self.library("cards"):
            return None
        ids = {a["card_id"] for a in self.library("cards") if a.get("in_pool")}
        ids |= {t["card_id"] for t in self.library("timewarped-cards")}
        ids |= {m["card_id"] for m in self.library("minions-seen") if m.get("card_id")}
        for name in ("trinkets", "rewards", "anomalies", "quests", "dark-gifts"):
            ids |= {x["card_id"] for x in self.library(name) if x.get("in_pool")}
        for hero in self.library("heroes"):
            power = self.by_dbf.get((self.by_id.get(hero["card_id"]) or {}).get("heroPowerDbfId"))
            ids |= {hero["card_id"]} | ({power["id"]} if power else set())
        return ids | {g for g in (self.golden_id(i) for i in ids) if g}

    def timewarped_minions(self):
        """Timewarped minions (normal cards): the API's Timewarped library, else HearthstoneJSON's flag."""
        api = [t["card_id"] for t in self.library("timewarped-cards") if (t.get("card_type") or {}).get("slug") == "minion"]
        if not api:
            api = [c["id"] for c in self.cards if c.get("battlegroundsTimewarpCard") and c.get("type") == "MINION"
                   and not c.get("battlegroundsNormalDbfId")]
        return [self.by_id[i] for i in api if i in self.by_id]
