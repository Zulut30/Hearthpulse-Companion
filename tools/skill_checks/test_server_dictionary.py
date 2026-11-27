"""tools/archetypes/server_dictionary.py builds the archetype dictionary the app's rules would build.

The synthetic catalogue of server/pool/test/fixtures/archetype-catalog.json gives its `expected` here; the .NET test
ArchetypeAgreementTests gets the same classes, order and core cards from the app's ApiConstructedMeta for that fixture
and the same hero classes from its CardDb, so the script and the app agree on every rule the fixture holds (class vote
and its tie, the name's class, list weights of at least 1, the 60% edge, unreadable lists, sideboards, unknown cards, a
printing as a card of its own, repeated names, the most played first, heroes with and without a class).

Run from the repository root: python -B -I -m unittest discover -s tools/skill_checks -v
"""
import contextlib
import hashlib
import importlib.util
import io
import json
import tempfile
import unittest
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
FIXTURE = ROOT / "server/pool/test/fixtures/archetype-catalog.json"


def load_script():
    spec = importlib.util.spec_from_file_location("server_dictionary", ROOT / "tools/archetypes/server_dictionary.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class ServerDictionaryTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.script = load_script()
        cls.fixture = json.loads(FIXTURE.read_text(encoding="utf-8"))

    def build(self):
        catalogs = {fmt: [answer] for fmt, answer in self.fixture["catalogs"].items()}
        return self.script.build(catalogs, self.fixture["cards"], self.fixture["built"])

    def saved(self, folder, **answers):
        """The fixture's answers as files (a format given here: these pages instead), and main's arguments for them."""
        args = []
        for name, pages in (("standard", [self.fixture["catalogs"]["standard"]]), ("wild", [self.fixture["catalogs"]["wild"]])):
            paths = []
            for i, page in enumerate(answers.get(name, pages)):
                paths.append(Path(folder) / f"{name}-{i}.json")
                paths[-1].write_text(json.dumps(page), encoding="utf-8")
            args += [f"--{name}", *map(str, paths)]
        cards = Path(folder) / "cards.json"
        cards.write_text(json.dumps(self.fixture["cards"]), encoding="utf-8")
        return args + ["--cards", str(cards), "--date", self.fixture["built"], "--out", str(Path(folder) / "out")]

    def test_the_catalogue_gives_the_expected_dictionary(self):
        dictionary, _ = self.build()
        self.assertEqual(self.fixture["expected"]["archetypes"], dictionary)

    def test_card_ids_hold_collectible_and_core_cards_and_printing_links_inside_the_list(self):
        _, ids = self.build()
        expected = self.fixture["expected"]["cardIds"]
        self.assertEqual(expected["cards"], {str(dbf): card for dbf, card in ids["cards"].items()})
        self.assertEqual(expected["copyOf"], {str(dbf): original for dbf, original in ids["copyOf"].items()})
        self.assertIn("133", expected["cards"], "a core card that is not collectible is listed")
        self.assertNotIn("190", expected["cards"], "a card neither collectible nor core is not")

    def test_rules_name_the_date_and_change_with_the_content(self):
        dictionary, _ = self.build()
        formats, heroes = dictionary["formats"], dictionary["heroes"]
        content = json.dumps({"formats": formats, "heroes": heroes}, ensure_ascii=False, separators=(",", ":"))
        self.assertEqual(f"arch-20261126-{hashlib.sha256(content.encode('utf-8')).hexdigest()[:8]}", dictionary["rules"])
        changed = json.loads(json.dumps(formats))
        changed["standard"]["MAGE"][0]["coreDbf"].append(106)
        self.assertNotEqual(dictionary["rules"], self.script.rules_of(changed, heroes, "2026-11-26"))
        self.assertNotEqual(dictionary["rules"], self.script.rules_of(formats, {**heroes, "MAGE": [637]}, "2026-11-26"))

    def test_hsguru_game_counts_stay_out_but_order_each_class(self):
        dictionary, _ = self.build()
        archetypes = [a for classes in dictionary["formats"].values() for archetypes in classes.values() for a in archetypes]
        self.assertTrue(archetypes)
        self.assertEqual([], [a["key"] for a in archetypes if set(a) != {"key", "coreIds", "coreDbf"}])
        # Gamma 700 games, Alpha and Beta 500 each: the most played first, a tie in the catalogue's order.
        self.assertEqual(["Gamma Rogue", "Alpha Rogue", "Beta Rogue"], [a["key"] for a in dictionary["formats"]["standard"]["ROGUE"]])

    def test_heroes_are_every_hero_card_with_a_class(self):
        dictionary, _ = self.build()
        # A skin that is not collectible and a neutral boss count; a hero without a class and a class spell do not.
        self.assertEqual({"HUNTER": [804], "MAGE": [637, 801], "NEUTRAL": [802], "WARRIOR": [7]}, dictionary["heroes"])
        self.assertEqual("HearthstoneJSON v1/latest/enUS/cards.json", dictionary["heroesSource"])

    def test_deck_codes_decode_as_the_app_reads_them(self):
        harold = self.fixture["catalogs"]["standard"]["data"][4]["decks"][0]["deck_code"]
        self.assertEqual({101: 1, 133: 2, 999: 2}, self.script.decode_deck(harold), "the sideboard is not part of the deck")
        self.assertEqual({101: 1, 104: 1, 106: 1, 107: 3}, self.script.decode_deck(self.fixture["catalogs"]["standard"]["data"][0]["decks"][2]["deck_code"]))
        for bad in ("", "AAAA", "not base64!", harold[:12]):
            self.assertIsNone(self.script.decode_deck(bad), bad)

    def test_class_names_are_read_as_the_app_reads_them(self):
        cases = {"Quest DH": "DEMONHUNTER", "Zoolock": "WARLOCK", "Harold DK": "DEATHKNIGHT", "Pally Aggro": "PALADIN",
                 "C'Thun Priest": "PRIEST", "Hunter's Mark Demon Hunter": "DEMONHUNTER", "Odd Thing": None}
        for name, cls in cases.items():
            self.assertEqual(cls, self.script.class_from_name(name), name)

    def test_the_files_written_are_the_json_built(self):
        dictionary, ids = self.build()
        self.assertEqual(dictionary, json.loads(self.script.dictionary_text(dictionary)))
        written = json.loads(self.script.ids_text(ids))
        self.assertEqual({**ids, "cards": {str(k): v for k, v in ids["cards"].items()}, "copyOf": {str(k): v for k, v in ids["copyOf"].items()}}, written)

    def test_too_little_data_writes_nothing(self):
        with tempfile.TemporaryDirectory() as folder:
            with self.assertRaises(SystemExit) as stop:
                self.script.main(self.saved(folder))
            self.assertIn("Too little data", str(stop.exception))
            self.assertFalse((Path(folder) / "out").exists())

    def test_a_catalogue_cut_at_the_limit_writes_nothing(self):
        # The answer counts more archetypes than it holds: one request, cut at the limit (Wild on 2026-11-26: 500 of 688).
        standard = {**self.fixture["catalogs"]["standard"], "meta": {"count": 20, "limit": 500, "offset": 0}}
        with tempfile.TemporaryDirectory() as folder, mock.patch.multiple(self.script, MIN_ARCHETYPES=1, MIN_CARDS=1):
            with self.assertRaises(SystemExit) as stop:
                self.script.main(self.saved(folder, standard=[standard]))
            self.assertIn("cut short (standard: 13 of 20 archetypes)", str(stop.exception))
            self.assertFalse((Path(folder) / "out").exists())
        # Without a count, a last page full to the limit may hide more.
        with tempfile.TemporaryDirectory() as folder, mock.patch.multiple(self.script, LIMIT=13, MIN_ARCHETYPES=1, MIN_CARDS=1):
            with self.assertRaises(SystemExit) as stop:
                self.script.main(self.saved(folder))
            self.assertIn("standard: 13 archetypes and no count", str(stop.exception))
            self.assertFalse((Path(folder) / "out").exists())

    def test_a_page_given_twice_or_one_archetype_short_writes_nothing(self):
        # QA B1 round 2, Q5: the first saved page twice in place of the whole catalogue, once with offsets, once without.
        data = self.fixture["catalogs"]["standard"]["data"]
        first = {"data": data[:7], "meta": {"count": 13, "limit": 7, "offset": 0}}
        for pages, said in (([first, first], "a page starts at offset 0, the pages before it hold 7"),
                            ([{"data": data[:7], "meta": {"count": 13}}] * 2, "14 archetypes for a count of 13"),
                            ([{"data": data[:12], "meta": {"count": 13}}], "12 of 13 archetypes")):
            with self.subTest(said), tempfile.TemporaryDirectory() as folder, \
                    mock.patch.multiple(self.script, LIMIT=7, MIN_ARCHETYPES=1, MIN_CARDS=1):
                with self.assertRaises(SystemExit) as stop:
                    self.script.main(self.saved(folder, standard=pages))
                self.assertIn(f"standard: {said}", str(stop.exception))
                self.assertFalse((Path(folder) / "out").exists())

    def test_the_pages_of_a_catalogue_make_one_dictionary(self):
        data = self.fixture["catalogs"]["standard"]["data"]
        pages = [{"data": data[:7], "meta": {"count": 13}}, {"data": data[7:], "meta": {"count": 13}}]
        with tempfile.TemporaryDirectory() as folder, mock.patch.multiple(self.script, LIMIT=7, MIN_ARCHETYPES=1, MIN_CARDS=1), \
                contextlib.redirect_stdout(io.StringIO()) as printed:
            self.script.main(self.saved(folder, standard=pages))
            written = json.loads((Path(folder) / "out" / "archetypes.json").read_text(encoding="utf-8"))
        self.assertEqual(self.fixture["expected"]["archetypes"], written)
        self.assertIn(f"{written['rules']}: archetypes standard 8, wild 1", printed.getvalue())

    def test_pages_are_asked_for_until_the_count_is_held(self):
        data = self.fixture["catalogs"]["standard"]["data"]
        asked = []

        def fetch(url):
            asked.append(url)
            offset = int(url.split("&offset=")[1]) if "&offset=" in url else 0
            return {"data": data[offset:offset + 5], "meta": {"count": len(data), "limit": 5, "offset": offset}}

        with mock.patch.multiple(self.script, fetch_json=fetch, LIMIT=5):
            pages = self.script.fetch_catalog("https://api.test/", "wild")
            self.assertIsNone(self.script.cut_short("wild", pages))
        url = "https://api.test/v1/hsguru/archetypes?format=wild&min_games=50&limit=5"
        self.assertEqual([url, url + "&offset=5", url + "&offset=10"], asked)
        self.assertEqual(data, [item for page in pages for item in page["data"]])

    def test_an_api_that_does_not_page_is_refused(self):
        page = {"data": self.fixture["catalogs"]["standard"]["data"][:5], "meta": {"count": 13}}
        with mock.patch.multiple(self.script, fetch_json=lambda url: page, LIMIT=5):
            with self.assertRaises(ValueError):
                self.script.fetch_catalog("https://api.test/", "standard")


if __name__ == "__main__":
    unittest.main()
