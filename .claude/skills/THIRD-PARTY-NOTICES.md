# Third-party notices for project skills

Skills in this folder that were copied from other projects, with their source, revision, path, what was changed for
Hearthpulse, and the licence text. Each copied file also carries a one-line source note under its title or
frontmatter. Everything else in `.claude/skills/` and `.claude/agents/` is Hearthpulse's own.

Imported 2026-10-29 (task `TASK-2026-10-29-skills-import`). Only Markdown was taken: no scripts, hooks, settings,
plugins or MCP servers.

## mattpocock/skills

- Source: https://github.com/mattpocock/skills, commit `a496f5513d4a4cf889bc48e189e579f4b0bdf6aa`
- Licence: MIT, Copyright (c) 2026 Matt Pocock (text below)

| Project path | Source path | Changes |
|---|---|---|
| `codebase-design/SKILL.md` | `skills/engineering/codebase-design/SKILL.md` | TypeScript examples rewritten in C#; Russian triggers in the description |
| `codebase-design/DEEPENING.md` | `skills/engineering/codebase-design/DEEPENING.md` | local stand-ins named for this project (temp SQLite, data-folder copy, fake `HttpMessageHandler`) |
| `codebase-design/DESIGN-IT-TWICE.md` | `skills/engineering/codebase-design/DESIGN-IT-TWICE.md` | glossary sources (`docs/GLOSSARY.md`, `hearthstone-context`); sub-agents only design |
| `domain-modeling/SKILL.md` | `skills/engineering/domain-modeling/SKILL.md` | "File structure" rewritten: one context, `docs/GLOSSARY.md`, `docs/adr/`, task-card decisions, `docs/README.md` row; Russian triggers in the description |
| `domain-modeling/GLOSSARY-FORMAT.md` | `skills/engineering/domain-modeling/GLOSSARY-FORMAT.md` | multi-context section replaced by the single `docs/GLOSSARY.md` |
| `domain-modeling/ADR-FORMAT.md` | `skills/engineering/domain-modeling/ADR-FORMAT.md` | none |
| `grilling/SKILL.md` | `skills/productivity/grilling/SKILL.md` | Russian trigger phrases; "In Hearthpulse" paragraph (Russian, only on the owner's request, results into the task card, a session without sub-agents) |
| `writing-for-agents/SKILL.md` | `skills/productivity/writing-for-agents/SKILL.md` | environment examples for this repo; role files, briefs and Russian triggers in the description |
| `writing-for-agents/SKILL-MECHANICS.md` | `skills/productivity/writing-for-agents/SKILL-MECHANICS.md` | "In Hearthpulse" section (CLAUDE.md table, catalogue test, Codex stubs, role preloads, notices) |

```
MIT License

Copyright (c) 2026 Matt Pocock

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## jakubkrehel/skills

Imported 2026-10-30 (task `TASK-2026-10-30-skills-ui`). Only Markdown was taken and every file was rewritten from web
(CSS, Tailwind, ARIA, browser, URL pickers) to WPF `.xaml` + Avalonia `.axaml`, the `hearthpulse-design` tokens and
theme presets, Loc ru/en and `hearthpulse-visual-check` renders. Each rewritten skill carries the owner's rules (movable
and resizable panels, every theme, no render while `Hearthstone.exe` runs, both UIs, only what the player sees
during a game) and the project's B/M/m/n severity. Not imported: `teardown` (JS/CJS drawing tool), `explain-interface`
(reads other sites by URL), `build-design` (Figma MCP; designs arrive as `design.md`), `agents/openai.yaml`,
`.claude-plugin/*`, `AGENTS.md`, `CLAUDE.md`, `opencode.json`, `README.md`; `better-typography/choosing-fonts.md`,
`spacing-and-sizing.md`, `details-and-accessibility.md` and `variable-fonts-and-opentype.md` (fonts and ramp are
`hearthpulse-design`'s, the rest is web: `@font-face`, iOS zoom, `text-box`); `state-machine/switcher.md` and
`variant/picker.md` (URL switchers).

- Source: https://github.com/jakubkrehel/skills, commit `592aedc44d7d5b3e29740984252902f3e5402a1c` (plugin version 1.17.0)
- Licence: MIT, Copyright (c) 2026 Jakub Krehel (text below)

| Project path | Source path | Changes |
|---|---|---|
| `better-interface/SKILL.md` | `skills/better-interface/SKILL.md` | WPF/Avalonia recon; B/M/m/n scale and escalation triggers incl. hidden information, one-UI features, `MovablePanel`, hex in views, Loc keys; hands branches to `interface-review` directly; project test and render checks |
| `better-interface/review-format.md` | `skills/better-interface/review-format.md` | Russian headings, ids `B1`/`M1`/`m1`, both-UI locations, verdict «можно / нельзя сливать» |
| `better-accessibility/SKILL.md` | `skills/better-accessibility/SKILL.md` | ARIA → UI Automation (`AutomationProperties`), built-in controls, `FocusRing*` / Fluent focus, `ClientAreaAnimation`, overlay has no keyboard walk; autocomplete, iOS zoom, viewport, skip links, SPA routes removed |
| `better-accessibility/keyboard-and-focus.md` | `skills/better-accessibility/focus-and-keyboard.md` | focus visuals, tab order, `RadioButton`/`ListBox` groups, modal windows and popups, page switches, shortcuts, the overlay exception |
| `better-accessibility/names-and-announcements.md` | `semantics-and-aria.md`, `screen-readers.md`, `forms.md` | merged; names, `Label Target`/`LabeledBy`, images and glyphs, disabled tooltips, validation, `LiveSetting` |
| `better-accessibility/hit-areas.md` | `skills/better-accessibility/hit-areas.md` | transparent backgrounds for hit testing, `IsHitTestVisible`, `OverlayHit.Interactive`, `MovablePanel` drag alternatives |
| `better-accessibility/motion-and-scale.md` | `skills/better-accessibility/motion-and-zoom.md` | Windows animation setting, timed toasts, window minimum, Avalonia compact, overlay scale instead of 200% zoom and 320 px |
| `better-layout/SKILL.md` | `skills/better-layout/SKILL.md` | project page grid, reserved zones (live bar, game UI), compact, Russian growth; logical properties, RTL, safe areas, container queries removed |
| `better-layout/grouping-and-alignment.md` | `skills/better-layout/grouping-and-alignment.md` | recipes in XAML with the project's styles; RTL removed |
| `better-layout/spacing-and-adaptivity.md` | `skills/better-layout/spacing-and-adaptivity.md` | `WrapPanel`, `*` columns, horizontal `StackPanel` trap, compact rules, overlay scale and `KeepOnScreen` |
| `better-writing/SKILL.md` | `skills/better-writing/SKILL.md` | `Loc` keys in both string files, Russian conventions from existing strings (infinitive buttons, «вы», «ёлочки»), game terms from the client |
| `better-writing/patterns.md` | `skills/better-writing/patterns.md` | ru/en templates; ICU and `Intl` → `Loc.F`, `Loc.P`, `Loc.DateCulture` |
| `better-typography/SKILL.md` | `skills/better-typography/SKILL.md` | the project's three fonts and ramp, shipped Montserrat weights, WPF/Avalonia trimming and tabular figures, overlay size floor; font loading, `rem`, iOS, `text-box`, bidi removed |
| `better-typography/xaml-cheat-sheet.md` | `skills/better-typography/css-cheat-sheet.md` | replaced by a WPF/Avalonia property table |
| `better-typography/wrapping-and-punctuation.md` | `skills/better-typography/wrapping-and-punctuation.md` | Russian and English characters, JSON/XAML entry; bidi removed |
| `better-colors/SKILL.md` | `skills/better-colors/SKILL.md` | theme files, `ThemePalette.Map` and fixed meaning prefixes, presets as `ThemeSpec`, `ThemeContrastTests`; OKLCH ramps, gamut, light/dark removed |
| `better-colors/contrast.md` | `skills/better-colors/contrast.md` | measuring with `ThemeContrastTests` and from PNG pixels; APCA and CSS fixes removed |
| `better-colors/palette-and-tokens.md` | `palette-structure.md`, `token-naming.md`, `color-usage.md`, `palette-generation.md`, `color-formats.md` | merged and rewritten for the project's roles, the engine's recolour table, `#AARRGGBB` |
| `better-ui/SKILL.md` | `skills/better-ui/SKILL.md` | theme radii and depth, no new effects (overlay-safe), MDI glyph states, WPF storyboards and Avalonia transitions with the theme's values (press 0.96 / 80 ms, hover 0.12 / 0.22 s); blur, theme-switch, `will-change`, touch hover, RTL removed |
| `better-ui/surfaces-and-icons.md` | `surfaces.md`, `icons.md` | merged; nested radius, optical nudges, depth tokens, neutral art edge, MDI icons |
| `better-ui/motion.md` | `animations.md`, `enter-exit.md`, `icon-transitions.md`, `performance.md` | merged; animation guard, reversible storyboards, press, entrances/exits, glyph swaps, overlay performance |
| `interface-review/SKILL.md` | `skills/interface-review/SKILL.md` | model-invoked (the reviewer role runs it), `main...claude/*` scope, task card as intent, XAML-specific incomplete-change list, B/M/m |
| `interface-review/scope-resolution.md` | `skills/interface-review/scope-resolution.md` | local `main` base, UI paths instead of web exclusions, consumers through resource keys and bindings |
| `interface-review/removed-signals.md` | `skills/interface-review/removed-signals.md` | XAML, theme, Loc and overlay signals |
| `ui-break/SKILL.md` | `skills/break/SKILL.md` | renamed `ui-break`; harness page → scratch render scene with PNGs in both UIs |
| `ui-break/scenarios.md` | `skills/break/scenarios.md` | card/deck/player names, Russian text, game modes, overlay scale; RTL and emoji removed |
| `ui-break/scratch-scene.md` | throwaway-route paragraphs of `break`, `state-machine`, `variant` | new shared file: the scratch `--render-ui` scene, data boundary, never committed |
| `ui-states/SKILL.md` | `skills/state-machine/SKILL.md` | renamed `ui-states`; states from view models, game scenes, account and settings; one PNG per state; `switcher.md` not imported |
| `ui-variant/SKILL.md` | `skills/variant/SKILL.md` | renamed `ui-variant`; one PNG per variant in the real surface; floor = `better-interface` triggers; promotion through the page/panel recipes in both UIs; `picker.md` not imported |

```
MIT License

Copyright (c) 2026 Jakub Krehel

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Ideas only, no text copied

These shaped our own wording in `hearthpulse-team/reference/task-template.md`, `.claude/agents/hearthpulse-lead.md`,
`.claude/agents/hearthpulse-qa.md` and the `hearthpulse-team` skill (which names them in its "Ideas borrowed" line).
Nothing was copied from them.

| Project | Revision | Licence | Idea |
|---|---|---|---|
| [github/spec-kit](https://github.com/github/spec-kit) | `a397de4f00d11729de7297b6681d91d0667ff62f` | MIT, Copyright GitHub, Inc. | Given/When/Then acceptance scenarios, `[NEEDS CLARIFICATION]` markers, a requirements-quality checklist, a project constitution |
| [gsd-build/get-shit-done](https://github.com/gsd-build/get-shit-done) | `bdcaab2c752d9a33a1a1ca9acf3a3c81fb991815` | MIT, Copyright (c) 2025 Lex Christopherson | an explicit check after each phase, goal-backward verification (exists, real, wired), a state log |
| [obra/superpowers](https://github.com/obra/superpowers) | `2ce8aa15d05bd57213e6f746f953aafb7ded7a8b` | MIT, Copyright (c) 2025 Jesse Vincent | `brainstorming`'s builder check: a fresh reader asks what they would still need before building; earlier, `subagent-driven-development` for `hearthpulse-team` (fresh implementer per task, review against the spec before quality, rulings instead of stalls, a ledger on disk) |
| [vijaythecoder/awesome-claude-agents](https://github.com/vijaythecoder/awesome-claude-agents) | not recorded (borrowed before this import) | MIT, Copyright (c) 2024 Awesome Claude Agents Contributors | `hearthpulse-team`: role split and a fixed assignment format (`tech-lead-orchestrator`) |
| [bmad-code-org/BMAD-METHOD](https://github.com/bmad-code-org/BMAD-METHOD) | not recorded (borrowed before this import) | MIT, Copyright (c) 2025 BMad Code, LLC (its trademark notice covers the BMad names, not used here) | `hearthpulse-team`: the task card as the developer's whole context |
| [lepoco/wpfui](https://github.com/lepoco/wpfui) | `ffebacd61058170cf63864b7d5aa730cffff848a` | MIT, Copyright (c) 2021-2025 Leszek Pomianowski and WPF UI Contributors | assessed only, not used: `docs/wpfui-assessment.md` |
