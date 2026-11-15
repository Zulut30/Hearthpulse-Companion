---
name: hearthpulse-ci-security
description: Use when editing or auditing a GitHub Actions workflow of Hearthpulse (.github/workflows/ci.yml, release.yml, codeql.yml, mutation.yml, .github/ci/code-changed.sh) for security - triggers and who can fire them, GITHUB_TOKEN permissions, pinning actions to commit SHAs, script injection through ${{ }}, signing secrets and where they are visible, artifacts passed between jobs, release signing. Not for cutting a release or versions (hearthpulse-release). Triggers - "workflow", "GitHub Actions", "закрепить actions по SHA", "pin actions", "permissions", "секреты в CI", "безопасность CI", "pull_request_target", "supply chain", "Dependabot".
---

> Source: [github/awesome-copilot](https://github.com/github/awesome-copilot) `skills/github-actions-hardening/`
> (`SKILL.md`, `references/triggers-and-privilege.md`, `injection.md`, `permissions-and-tokens.md`, `supply-chain.md`)
> at `8ecd5b1`, MIT, © GitHub, Inc., see [THIRD-PARTY-NOTICES](../THIRD-PARTY-NOTICES.md). Hearthpulse changes:
> the threat model and the trigger, injection, permission and pinning rules condensed into this file; the step
> order reordered by this repository's risk; "never apply, only report" and the emoji report replaced by the
> project's workflow and B/M/m severity; OIDC cloud recipes and self-hosted runner sections dropped (unused here).

# Workflow security in Hearthpulse

What the four workflows do today, with the facts an audit starts from: [reference/workflows.md](reference/workflows.md).
Building and publishing a release: [hearthpulse-release](../hearthpulse-release/SKILL.md).

## Authority

- Editing a workflow file is a code change like any other: it follows the session's normal workflow
  (`hearthpulse-ship` in the coordinating session, a `claude/*` commit in a worktree). No extra approval step.
- A change that makes a workflow **publish, tag, or spend the signing secrets** is a release action and waits for
  the owner's word: pushing a `v*` tag, running `release.yml` with `publish` on, changing what the `release` job
  uploads. A trial run (`publish` off) publishes nothing; whether to start one is `hearthpulse-release`'s rule.
- Repository settings (secrets, default token permissions, branch or tag protection, Dependabot alerts) are not in
  the checkout. Recommend them with the exact setting; the owner applies them.

## The model

Two facts make workflow risk different from code risk:

1. `${{ expr }}` is pasted into the script **as text before the shell runs**. An expression holding text a
   contributor controls (PR title or body, branch name `github.head_ref`, commit message, issue or comment body) is
   command injection. The fix is always the same: bind it under `env:` and use the quoted shell variable.
2. The trigger decides the token. `pull_request` from a fork gets a read-only token and no secrets, so untrusted
   code there can steal nothing. `pull_request_target`, `workflow_run`, `issue_comment` and `issues` run with the
   base repository's token and secrets while anyone can fire them; a job on those triggers must never check out and
   execute the contributor's code.

## Audit or edit, in this repository's order of risk

1. **`release.yml` first.** It holds every secret and the only `contents: write`. Check: the build jobs stay
   read-only; signing secrets reach only the Windows matrix entries; `UPDATE_SIGNING_KEY` only the `release` job; no
   step that sees a secret runs code that is not in the repository at the tagged commit; the action that uploads the
   release is pinned to a full commit SHA.
2. **Triggers of all four files.** A new `pull_request_target`, `workflow_run`, `issue_comment` or `issues`
   trigger is a Blocker unless the job only reads metadata and runs no checked-out code. Prefer `pull_request`.
3. **`${{ }}` inside every `run:` and `script:`.** List them. Server-set or repository-set values (`matrix.*` from
   the file itself, `steps.*.outputs` computed from validated input, `github.sha`) are acceptable; anything from
   `github.event.*` text fields or `github.head_ref` moves under `env:`. A `workflow_dispatch` input is typed by a
   collaborator: validate its shape before use, as the `Version` step does with its regex.
4. **`permissions:`.** Every workflow keeps a read-only top level and raises a scope on the one job that needs it.
   A new job that needs `write` says why in a comment. Never `write-all`.
5. **Action references.** Third-party actions (anything outside `actions/*` and `github/*`) are pinned to a
   40-character commit SHA with the version in a trailing comment (`@<sha> # v2.3.1`). First-party actions: pin
   the ones in jobs that see secrets or write (release, CodeQL upload) before the ones in read-only CI. `@main` or
   another branch is never acceptable. Resolve a tag yourself:
   `git ls-remote https://github.com/<owner>/<repo> "refs/tags/<tag>*"` and take the peeled (`^{}`) line when the
   tag is annotated; do not copy a SHA from memory or from a web page.
6. **Pins go stale.** SHA pins without an updater rot: add `github-actions` to a Dependabot config in the same
   change that introduces the first pins, or record in the PR who updates them and when.
7. **Artifacts between jobs are data.** `release` publishes what `build` uploaded: the file-count and checksum
   checks are the integrity gate, keep them when editing. A job that downloads an artifact produced from a pull
   request never executes it.
8. **Secrets in steps.** No `echo` of a secret, no `set -x` in a step that has one, no secret passed to an action
   that is not pinned. `actions/checkout` leaves the token in `.git/config`: set `persist-credentials: false` in a
   job that afterwards runs code from outside the repository.
9. **Dependencies installed in CI** (`npm ci`, `pip install`, `dotnet restore`, downloaded tools such as the signer)
   run with the job's token. In a job with secrets, fetch by exact version and verify what the script already
   verifies (checksums); do not add an unpinned download there.

## Severity

The project's scale (`hearthpulse-review`): **B** - a path for someone outside the collaborators to reach a secret,
the write token or the published files; **M** - a mutable reference or an unneeded write scope in a job with
secrets or write access, or an injection sink only a collaborator can reach; **m** - a hardening gap in a read-only
job (first-party action on a tag, default `persist-credentials`). A fork `pull_request` running untrusted code is
by design safe and is not a finding.

## Report or change

For an audit, list findings as `B1`/`M1`/`m1` with file and line, the exact YAML, the risk in one sentence and the
corrected YAML, then what was checked and found clean. For an edit, make the change and verify it:

- the YAML still parses and the job graph is unchanged except where intended (read the diff of `needs:`, `if:`,
  `permissions:`);
- every pinned SHA resolves to the tag in its comment (the `git ls-remote` line above);
- CI on the branch is green. A change to `release.yml` cannot be proved by CI: say so, and name the trial run that
  would prove it instead of claiming it works.
