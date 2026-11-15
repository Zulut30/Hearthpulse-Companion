"""Keep the checked-in skill catalog discoverable and its local recipe links usable."""
import re
import unittest
from pathlib import Path
from urllib.parse import unquote, urlsplit

import yaml

ROOT = Path(__file__).resolve().parents[2]
SKILLS = sorted((ROOT / ".claude/skills").glob("*/SKILL.md"))


class SkillCatalogTests(unittest.TestCase):
    def test_every_skill_has_matching_name_and_a_nonempty_description(self):
        self.assertTrue(SKILLS, "No project skills discovered")
        names = set()
        for skill in SKILLS:
            with self.subTest(skill=skill.parent.name):
                source = skill.read_text(encoding="utf-8")
                self.assertTrue(source.startswith("---\n"), "Missing YAML frontmatter")
                front = yaml.safe_load(source.split("---", 2)[1])
                self.assertIsInstance(front, dict)
                self.assertEqual(skill.parent.name, front.get("name"))
                self.assertNotIn(front["name"], names)
                names.add(front["name"])
                self.assertIsInstance(front.get("description"), str)
                self.assertTrue(front["description"].strip())

    def test_relative_recipe_links_resolve_from_their_own_skill(self):
        checked = 0
        for skill in SKILLS:
            for href in re.findall(r"\[[^\]]*\]\(([^\s)]+)\)", skill.read_text(encoding="utf-8")):
                target = urlsplit(href)
                if target.scheme or target.netloc or not target.path:
                    continue
                with self.subTest(skill=skill.parent.name, link=href):
                    self.assertTrue((skill.parent / unquote(target.path)).is_file(), "Broken recipe link")
                checked += 1
        self.assertGreater(checked, 0, "No local recipe links discovered")

    def test_the_claude_md_table_lists_exactly_the_skill_folders(self):
        rows = re.findall(r"^\|[^|\n]+\|([^|\n]+)\|\s*$", (ROOT / "CLAUDE.md").read_text(encoding="utf-8"), re.M)
        listed = sorted(name for cell in rows for name in re.findall(r"`([a-z0-9-]+)`", cell))
        self.assertEqual(sorted(skill.parent.name for skill in SKILLS), listed)

    def test_agent_files_preload_only_existing_model_invoked_skills(self):
        fronts = {skill.parent.name: yaml.safe_load(skill.read_text(encoding="utf-8").split("---", 2)[1])
                  for skill in SKILLS}
        agents = sorted((ROOT / ".claude/agents").glob("*.md"))
        self.assertTrue(agents, "No agent files discovered")
        for agent in agents:
            with self.subTest(agent=agent.stem):
                preload = yaml.safe_load(agent.read_text(encoding="utf-8").split("---", 2)[1]).get("skills")
                self.assertIsInstance(preload, list, "Every role names the skills it starts with")
                self.assertTrue(1 <= len(preload) <= 3, "Preloading puts the whole skill text into context")
                for name in preload:
                    self.assertIn(name, fronts)
                    # Claude Code skips a preload of a skill that sets disable-model-invocation.
                    self.assertIsNot(fronts[name].get("disable-model-invocation"), True)


if __name__ == "__main__":
    unittest.main()
