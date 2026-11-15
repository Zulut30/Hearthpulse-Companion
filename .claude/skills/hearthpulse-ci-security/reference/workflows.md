# The workflows as they stand

Read from the files on 2026-11-15 at `f2dbdca6`. This is a starting map, not a verdict: re-derive each line from the
workflow you are editing, and update this file in the same change when a fact here stops being true.

## Inventory

| File | Triggers | Top-level token | Raised scope | Secrets |
|---|---|---|---|---|
| `ci.yml` | `push` to `main`, `pull_request`, `workflow_dispatch` | `contents: read`, `actions: read` | none | none (`github.token` as `GH_TOKEN` for `.github/ci/code-changed.sh`) |
| `release.yml` | `push` of a `v*` tag, `workflow_dispatch` with `version`, `publish`, `simulate_previous`, `sign_selftest` | `contents: read`, `actions: read` | `release` job: `contents: write` | Windows build entries: `WINDOWS_SIGN_PFX`, `WINDOWS_SIGN_PFX_PASSWORD`, `AZURE_SIGNING_*`, `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`; `release` job: `UPDATE_SIGNING_KEY` |
| `codeql.yml` | `push` to `main`, weekly `schedule`, `workflow_dispatch` | `contents: read` | `analyze` job: `security-events: write` | none |
| `mutation.yml` | `workflow_dispatch` with a `choice` input | `contents: read` | none | none |

No workflow uses `pull_request_target`, `workflow_run`, `issue_comment` or `issues`. All runners are GitHub-hosted.

## Action references

40 `uses:` lines, all on version tags, none on a commit SHA, none on a branch:

| Action | Count | Where it matters |
|---|---|---|
| `actions/checkout@v4` | 13 | every job; in `release.yml` it runs in jobs that later see secrets |
| `actions/setup-dotnet@v4` | 11 | same |
| `actions/upload-artifact@v4`, `actions/download-artifact@v4` | 8, 3 | carry the installers from `build` to `release` |
| `actions/setup-node@v4`, `actions/setup-python@v5` | 1, 1 | `ci.yml` scripts job |
| `github/codeql-action/init@v4`, `analyze@v4` | 1, 1 | the job with `security-events: write` |
| `softprops/action-gh-release@v2` | 1 | **the only third-party action**, in the only job with `contents: write`; it receives the release files |

There is no `.github/dependabot.yml`.

## `${{ }}` reaching a shell

Checked in every `run:` block. What is interpolated directly:

- `ci.yml`: `matrix.project`, `matrix.args` (defined in the file), `steps.code.outputs.run` (written by
  `.github/ci/code-changed.sh`);
- `mutation.yml`: `matrix.target`, built from the `choice` input in the `targets` job;
- `release.yml`: `matrix.build` (a command defined in the file).

Event data (`github.event.before`, `github.event.pull_request.base.sha`, `github.base_ref`) and the dispatch inputs
are passed through `env:`. The release version is checked against `^[0-9]+\.[0-9]+\.[0-9]+$` before it is used. No
PR title, body, branch name or commit message is interpolated anywhere.

## Release integrity that already exists

- Build jobs are read-only; the comment in `release.yml` states the reason (they see the signing secrets).
- The `release` job checks that every expected installer exists, counts the files (23, or 24 with a delta, plus the
  signature) and writes `SHA256SUMS.txt`.
- `installer/sign-sums.sh` signs the checksums with Ed25519 (`UPDATE_SIGNING_KEY`) and verifies against the keys in
  `src/Hearthpulse.Core/Updates/update-signing-key.pub`; a publishing run with an embedded key and no secret fails.
- Authenticode signing of the Windows packages runs only when its secrets exist; without them the build warns and
  ships unsigned. Details and the state of the keys: [hearthpulse-release](../../hearthpulse-release/SKILL.md).

## Open hardening, by risk

1. Pin `softprops/action-gh-release` to a commit SHA (third-party code with the write token and the release files).
2. Pin the first-party actions in `release.yml`, then in `codeql.yml`.
3. Add a Dependabot configuration for `github-actions` together with the first pins.
4. Then the first-party actions in `ci.yml` and `mutation.yml`.

Not verified from the checkout: the repository's default token permissions, tag protection for `v*`, who may run
`workflow_dispatch`, and whether the signing secrets are set.
