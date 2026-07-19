<!--
  PULSE_CI_KICKOFF.md - a ready-to-paste prompt for a session running INSIDE the pulse repo, to generate
  a real minimal PR-gating CI workflow and close pulse's #1 review finding (quality gates ran only
  post-merge inside deploy; the backend / ~155 tests were ungated). This is generalization 3 of the
  methodology applied to the project that most needs it. Self-contained: it embeds the target structure,
  so the pulse session does not need this repo checked out. Canonical template: methodology/templates/ci-minimal.yml.
  Prose style: hyphens, colons, parentheses - never em dashes.
-->

# Pulse gating-CI kickoff

Paste everything below the line into a session **inside the pulse repository**. It has the session
generate a real `.github/workflows/ci.yml` from pulse's actual build files - not the template's
placeholders - and make it the required check, closing pulse's top finding (no gating CI; an ungated
backend stack). It will output the file for you to review; it will not push or open a PR unless you say
so.

---

You are a release engineer working in the **pulse** repository. Your task: create a **minimal but real
pull-request-gating CI workflow** and make it the required status check - closing pulse's top process
gap (quality gates currently run only post-merge inside the deploy workflow, and at least one stack, the
backend with roughly 155 tests, is ungated). Work from pulse's ACTUAL build files, never assumptions.

**Step 1 - Inventory every stack in the repo and its real commands.**
- Read the manifests and lockfiles: `package.json` (scripts, workspaces), `*.csproj` / `*.sln` /
  `global.json`, `pyproject.toml` / `requirements.txt`, `go.mod`, Dockerfiles, any infra (Bicep /
  Terraform).
- For each independently-buildable stack, record: the toolchain and its version (from the lockfile /
  manifest / `global.json`), and the **real** commands for install, lint, typecheck, build, and test. If
  a stack genuinely lacks one (for example no lint is configured), note that - do not invent a command.
- Record the directory paths each stack owns (for path filters).

**Step 2 - Write `.github/workflows/ci.yml`** with exactly this shape:
- Triggers: `pull_request` to the default branch AND `push` to the default branch (so the trunk's
  status is always known).
- `concurrency: { group: ci-${{ github.ref }}, cancel-in-progress: true }` - pulse is an agent-fleet
  repo, so cancel superseded runs.
- **One job per stack**, each running that stack's real install / lint / typecheck / build / test with
  the correct setup action and pinned toolchain version. **Every stack gets a job - the backend is NOT
  exempt** (that omission is the finding). No stack orphaned.
- Optional per-stack path filters so a job runs only when its paths changed - add these only after the
  job set is correct.
- A final aggregate job `gate` with `needs: [<every stack job>]` that simply succeeds. **This aggregate
  is the one status check branch protection will require**, so adding a stack later cannot silently
  un-gate the branch.
- Do NOT put quality gates inside the deploy workflow. Deploy assumes-green off already-gated trunk; if
  pulse's deploy currently runs the tests, note that they should move to (or be duplicated onto) this PR
  gate.

**Step 3 - Report back:**
- The full `ci.yml`, ready to commit.
- A short "what changed" note: which stacks are now gated that were not (call out the backend
  explicitly), and confirm no stack is left orphaned.
- The exact branch-protection change to apply, as steps I can follow in GitHub settings: require the
  `gate` check on the default branch, and require a pull request before merging.
- Anything you could NOT determine from the repo (a missing test script, an ambiguous toolchain
  version) listed as explicit TODOs, not guesses.

Constraints: match pulse's real toolchains and script names exactly; keep it minimal (build + lint +
typecheck + test - no deploy, no coverage thresholds yet); every command must actually exist in the
repo. Output the file for me to review first - do not push or open a PR unless I ask.

---

## After it runs

Review the generated `ci.yml`, apply the branch-protection change it describes, then merge it **before**
adding the next contributor or unattended agent run - a gate that exists but is not required is not a
gate. This single change moves pulse from "gates are an honor-system claim" to "the machine enforces the
floor on every stack," which is the precondition the methodology (`methodology/METHODOLOGY.md`,
generalization 3) puts ahead of everything else.
