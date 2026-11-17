---
name: hearthpulse-team
description: Use when the owner hands the coordinating session a Hearthpulse task to run as a team - the lead writes a task card and splits it into packages, the designer writes design.md, core and UI developers build in their own sessions and worktrees, QA and the reviewer check each package, then hearthpulse-ship merges, pushes, builds and restarts. Triggers - "/hearthpulse-team", "командой", "раздай задачу", "оформи в задание", "поставь задачу разработчикам и дизайнеру". Default front half of hearthpulse-ship for M/L tasks; a small fix goes straight to hearthpulse-ship.
---

# Hearthpulse dev team

The owner (2026-10-29): "один который будет получать задачу оформлять её в задание и раздавать разработчикам,
дизайнером и так далее".

The coordinating session is the team's dispatcher. It does not write feature code: it hands the request to the lead,
gives the packages to the roles, checks what comes back and ships per [hearthpulse-ship](../hearthpulse-ship/SKILL.md).
That skill's standing permission and its stop cases apply unchanged.

Ideas borrowed (not code): role split and a fixed assignment format from vijaythecoder/awesome-claude-agents
(`tech-lead-orchestrator`, MIT); fresh implementer per task, review against the spec before quality, rulings instead
of stalls and a ledger on disk from obra/superpowers (`subagent-driven-development`, MIT); the task card as the
developer's whole context from BMAD-METHOD; дано/когда/тогда criteria, `[NEEDS CLARIFICATION]` markers and the card
check from github/spec-kit (MIT); a check that closes each phase and the "Сейчас" state line from
gsd-build/get-shit-done (MIT) - see [notices](../THIRD-PARTY-NOTICES.md).

## Roster

| Role | Agent file | Runs as | Writes |
|---|---|---|---|
| Lead: task card, split | [hearthpulse-lead](../../agents/hearthpulse-lead.md) | subagent | `task.md` |
| Designer | [hearthpulse-designer](../../agents/hearthpulse-designer.md) | subagent | `design.md` |
| Core developer | [hearthpulse-core-dev](../../agents/hearthpulse-core-dev.md) | session in `wt-*` | code on `claude/*`, `handover-P<n>.md` |
| UI developer | [hearthpulse-ui-dev](../../agents/hearthpulse-ui-dev.md) | session in `wt-*` | code on `claude/*`, `handover-P<n>.md` |
| QA | [hearthpulse-qa](../../agents/hearthpulse-qa.md) | subagent | `qa-P<n>.md` |
| Reviewer | [hearthpulse-reviewer](../../agents/hearthpulse-reviewer.md) | subagent | `reference/reviews/HeartLens-claude-*.md` |
| Legal, when triggered | [hearthpulse-legal](../../agents/hearthpulse-legal.md) | subagent | `reference/reviews/LEGAL-*.md` |

- **Subagent:** run the role as a subagent ([tools](../hearthpulse-ship/SKILL.md#session-tools-by-environment)).
  The session lists the role as an agent type: use it, the role's skills come preloaded. Otherwise a general
  subagent with "Твоя роль: прочитай `.claude/agents/<role>.md` от корня checkout, открой скиллы из
  его списка `skills` и выполняй" plus the inputs.
- **Session:** the brief in step 3. In T3 use an isolated `delegate_task`; a separate top-level chat is created
  only when the owner requests one. Elsewhere use the environment's supported isolated session.
- **Task folder** `$task` = the folder given in the brief, otherwise `<repo>/../reference/tasks/TASK-<yyyy-mm-dd>-<slug>/`
  if the sibling exists, or `<repo>/dist/tasks/TASK-<yyyy-mm-dd>-<slug>/`: card, design, handovers,
  QA. It is the team's ledger. After a compaction or in a new conversation, read the "Журнал" in `task.md` before
  starting or re-sending anything.

## 1. Card

- **S** (one area, a few files, no UI decision): no lead. Write `$task\task.md` yourself from
  [the template](reference/task-template.md), only goal, AC, files and one package. Go to step 3.
- **M / L:** lead subagent with the owner's request verbatim, today's date and `$task`. Read the card it writes.
  "Вопросы владельцу" not empty: ask the owner those questions only, write the answers into the card in
  place of their `[NEEDS CLARIFICATION]` markers. Otherwise ask nothing. Packages go out only when its "Проверка
  карточки" is fully ticked.
- The card's "Журнал" opens with a "Сейчас" line (phase, next step, what we wait for): update it at every step
  below, and close a phase only after the check named in its table.

## 2. Design

A package changes what the player sees: designer subagent with `$task\task.md`. Core packages that do not wait for
the design start at the same time (step 3).

## 3. Developers

Per package, when its inputs exist (design.md, a contract from another package):

- obtain the worktree through the environment's task API (see the tools table linked below); outside such an
  environment, create a sibling checkout with `git worktree add`;
- start a session in that worktree ([tools](../hearthpulse-ship/SKILL.md#session-tools-by-environment)): fresh
  context, title `<slug> · P<n> <role>`;
- brief:

  ```
  Твоя роль — <role>: прочитай и выполняй .claude/agents/<role>.md.
  Первым делом загрузи скиллы: <список `skills` из файла роли + 1-2 скилла области пакета по таблице CLAUDE.md>.
  Задача: <$task>\task.md, пакет P<n>. Дизайн: <$task>\design.md (если есть).
  Worktree <path>, ветка claude/<slug>-<pkg>. Коммить только в неё, без push и merge.
  Рендер — через hearthpulse-visual-check на копии данных, оба UI, нужные темы, никогда при запущенном
  Hearthstone.exe. <Или «Без рендера», если задача его запрещает.>
  Локальная проверка пакета через tools/dev/check.ps1. Укажи, какие тесты проверили критерии приёмки.
  В конце: <$task>\handover-P<n>.md и отчёт — коммиты, счёт тестов, закрытые AC, что не проверено.
  ```

- a "Журнал" row: package, session id, branch, "в работе".

Developer sessions share the local check queue in CLAUDE.md.

## 4. Check each package

When a session reports (its note is not proof):

- QA and reviewer subagents in parallel on its worktree and branch. Legal as well when the diff touches game memory,
  input, third-party code/data, paid features or user data.
- QA "вернуть разработчику" or a reviewer B/M: write to the session with the ids, wait for its report, re-run only
  the side that failed (the reviewer checks the new commits and the closed ids). A third failed round on the same
  finding: stop and tell the owner.
- Minor findings: the session may fix them in the same round; they never block the merge.
- "Журнал": "принят", with the QA and review report paths.

## 5. Ship

All packages accepted: [hearthpulse-ship](../hearthpulse-ship/SKILL.md) §3-6 - merge one branch at a time (fewest
conflicts first), build after each, tests green, push, CI, restart the app, delete sessions, worktrees and branches.

## 6. Report

Once, in Russian: what now works in the app, the card path, packages with their commits, build and test counts, CI,
QA and review verdicts, what was not checked (render, live game), what is left for later.
