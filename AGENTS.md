# Hearthpulse: where the instructions are

The project instructions are in [CLAUDE.md](CLAUDE.md) and apply to every agent, whatever its name. Read it whole
before any work: ground rules, the default workflow and the skill table live only there.

**Skills.** Each row of that table is a recipe in `.claude/skills/<name>/SKILL.md`. First action on any task: pick the
1-3 rows that match and open those files. Codex also finds them by name through the stubs in `.agents/skills/`; a
stub only sends you to the real file.

**Roles.** A brief that gives you a role (`hearthpulse-core-dev`, `hearthpulse-qa`, ...) means: read
`.claude/agents/<role>.md`, open the skills its frontmatter lists under `skills`, then follow the file.
