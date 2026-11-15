# Hearthpulse: where the instructions are

The project instructions are in [CLAUDE.md](CLAUDE.md) and apply to every agent, whatever its name. Read it whole
before any work: ground rules, the default workflow and the skill table live only there.

**Skills.** Each row of that table is a recipe in `.claude/skills/<name>/SKILL.md`. First action on any task: pick the
1-3 rows that match and open those files. Codex also finds them by name through the stubs in `.agents/skills/`; a
stub only sends you to the real file.

**Another machine or agent.** Everything a skill needs is in this checkout: skills and their `reference/` files link
to each other and to `docs/` by relative paths, and third-party sources with their licences are in
`.claude/skills/THIRD-PARTY-NOTICES.md`. Claude Code reads `.claude/skills/`, Codex reads the `.agents/skills/` stubs
(`python tools/skills/gen_codex_stubs.py --check` shows whether they match), T3 uses the loader of the provider it
runs. When a skill does not load by itself, open its `SKILL.md` from the table in CLAUDE.md: that always works. No
global plugin or install step is required; paths outside the checkout named in a skill are optional and marked so.

**Roles.** A brief that gives you a role (`hearthpulse-core-dev`, `hearthpulse-qa`, ...) means: read
`.claude/agents/<role>.md`, open the skills its frontmatter lists under `skills`, then follow the file.
