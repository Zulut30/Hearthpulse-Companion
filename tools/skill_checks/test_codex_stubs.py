"""The .agents/skills stubs (what Codex scans) mirror the skills in .claude/skills, and AGENTS.md leads to them.

Run from the repository root: python -B -I -m unittest discover -s tools/skill_checks -v
Red here after a skill was added, renamed, deleted or its description edited: python tools/skills/gen_codex_stubs.py
"""
import importlib.util
import tempfile
import unittest
from pathlib import Path

import yaml

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("gen_codex_stubs", ROOT / "tools/skills/gen_codex_stubs.py")
stubs = importlib.util.module_from_spec(spec)
spec.loader.exec_module(stubs)

SKILLS = sorted(path.parent.name for path in (ROOT / ".claude/skills").glob("*/SKILL.md"))


class CodexStubTests(unittest.TestCase):
    def test_every_skill_has_a_stub_with_the_same_name_and_description_and_no_stub_is_extra(self):
        self.assertTrue(SKILLS, "No project skills discovered")
        self.assertEqual(SKILLS, sorted(path.parent.name for path in (ROOT / ".agents/skills").glob("*/SKILL.md")))
        for name in SKILLS:
            with self.subTest(skill=name):
                source = stubs.frontmatter(ROOT / ".claude/skills" / name / "SKILL.md")
                stub = stubs.frontmatter(ROOT / ".agents/skills" / name / "SKILL.md")
                self.assertEqual({"name": name, "description": source["description"]}, stub)

    def test_the_stub_folder_is_exactly_what_the_generator_writes(self):
        self.assertEqual([], stubs.differences(), "Run: python tools/skills/gen_codex_stubs.py")

    def test_every_stub_points_at_its_skill_file(self):
        for name in SKILLS:
            with self.subTest(skill=name):
                body = (ROOT / ".agents/skills" / name / "SKILL.md").read_text(encoding="utf-8").split("---", 2)[2]
                self.assertIn(f"`.claude/skills/{name}/SKILL.md`", body)
                self.assertLess(len(body.strip().splitlines()), 6, "A stub carries a pointer, not the skill's text")

    def test_only_owner_invoked_skills_switch_implicit_invocation_off(self):
        owner_invoked = [name for name in SKILLS if stubs.frontmatter(
            ROOT / ".claude/skills" / name / "SKILL.md").get("disable-model-invocation") is True]
        self.assertTrue(owner_invoked, "No owner-invoked skill discovered")
        policies = sorted((ROOT / ".agents/skills").glob("*/agents/openai.yaml"))
        self.assertEqual(owner_invoked, [path.parents[1].name for path in policies])
        for path in policies:
            with self.subTest(skill=path.parents[1].name):
                self.assertEqual({"policy": {"allow_implicit_invocation": False}},
                                 yaml.safe_load(path.read_text(encoding="utf-8")))

    def test_names_and_descriptions_fit_the_agent_skills_limits(self):
        for name in SKILLS:
            with self.subTest(skill=name):
                front = stubs.frontmatter(ROOT / ".claude/skills" / name / "SKILL.md")
                self.assertLessEqual(len(name), 64)
                self.assertLessEqual(len(front["description"]), 1024)
                self.assertNotIn("\n", front["description"].strip())

    def test_agents_md_sends_the_agent_to_claude_md_and_the_skills(self):
        text = (ROOT / "AGENTS.md").read_text(encoding="utf-8")
        self.assertIn("CLAUDE.md", text)
        self.assertIn(".claude/skills/", text)
        self.assertLess(len(text.splitlines()), 30, "AGENTS.md points at CLAUDE.md, it does not copy it")


class GeneratorTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory(prefix="hp-stubs-test-")
        self.addCleanup(temp.cleanup)
        self.source = Path(temp.name) / "source"
        self.out = Path(temp.name) / "out"

    def skill(self, name, front):
        path = self.source / name / "SKILL.md"
        path.parent.mkdir(parents=True)
        path.write_text(f"---\n{front}---\n\n# Body that must stay out of the stub\n", encoding="utf-8")

    def test_a_description_with_quotes_colons_and_russian_survives_the_round_trip(self):
        description = 'Use when: the owner says "счётчик" - it\'s a trigger, #1.'
        self.skill("alpha", yaml.safe_dump({"name": "alpha", "description": description}, allow_unicode=True))
        stubs.write(self.source, self.out)
        self.assertEqual({"name": "alpha", "description": description},
                         stubs.frontmatter(self.out / "alpha/SKILL.md"))
        self.assertNotIn("Body that must stay out", (self.out / "alpha/SKILL.md").read_text(encoding="utf-8"))
        self.assertEqual([], stubs.differences(self.source, self.out))

    def test_differences_name_a_missing_a_stale_and_an_extra_stub(self):
        self.skill("alpha", "name: alpha\ndescription: First.\n")
        self.skill("beta", "name: beta\ndescription: Second.\ndisable-model-invocation: true\n")
        stubs.write(self.source, self.out)
        self.assertTrue((self.out / "beta/agents/openai.yaml").is_file())
        self.assertFalse((self.out / "alpha/agents").exists())

        (self.out / "alpha/SKILL.md").unlink()
        (self.source / "beta/SKILL.md").write_text("---\nname: beta\ndescription: Changed.\n---\n", encoding="utf-8")
        (self.out / "gone").mkdir()
        (self.out / "gone/SKILL.md").write_text("---\nname: gone\ndescription: Old.\n---\n", encoding="utf-8")
        # beta lost its owner-invoked flag along with the description, so its policy file is now extra.
        self.assertEqual(["missing: alpha/SKILL.md",
                          "not generated from a skill: beta/agents/openai.yaml",
                          "not generated from a skill: gone/SKILL.md",
                          "out of date: beta/SKILL.md"],
                         stubs.differences(self.source, self.out))

        stubs.write(self.source, self.out)
        self.assertEqual([], stubs.differences(self.source, self.out))
        self.assertFalse((self.out / "gone").exists())


if __name__ == "__main__":
    unittest.main()
