# Feature Orchestration Playbook (QuibbleStone)

> The standard process for implementing a **large, already-planned, story-driven feature**: parallel builder
> subagents on isolated worktrees, serial integration onto an umbrella branch, and main-session verification at
> natural stopping points. Goal: **high fidelity to the stories, good code quality, and speed - without losing the
> nights-and-weekends momentum that keeps this build fun** (README section 8).
>
> Companion executable: the `orchestrate-feature` skill (`/orchestrate-feature {slug}`). This doc is the source of
> truth; the skill executes it. The repo `README.md` is the project charter - if anything here conflicts, the
> README wins; flag the discrepancy.
>
> Tracker: **GitHub** (Issues + the Epic/Feature/Story sub-issue hierarchy + Pull Requests + Actions). Wherever
> this doc says "tracker", it means GitHub via `gh`. Status mapping + commands: [`GITHUB_TRACKER.md`](GITHUB_TRACKER.md).
> This is a **single repo** (`api/` + `web/` + `infra/` in one tree), so the cross-repo machinery you may see in
> generic versions of this pattern does not apply here.

## When to use

- A feature that is **already planned**: `docs/features/{slug}/feature.md` + `implementation.md` (with a DAG-ready
  Wave Plan) + the `NN-<slug>.md` story files exist, a GitHub Story issue exists for each story, and the
  **planning-docs PR is merged**.
- **Not** for small/unplanned changes, bug fixes, or refactors. Those don't need orchestration - just build them
  (use `frontend-agent` / `code-review` / `ci-check` directly).

This is a deliberate scope tool. Slice discipline still applies (CLAUDE.md section 7): orchestrate a planned slice,
do not let parallelism become an excuse to build breadth before something is fun.

## Planning is a separate session (do this first)

Planning and orchestration are different jobs, and the seam between them is a checkpoint worth keeping:

- **A wrong plan, parallelized, is the most expensive failure mode** - you pay N builders to faithfully build the
  wrong thing. Making the plan its own reviewed PR puts a human sign-off on scope + ACs before any builder runs.
- **Context hygiene + resumability:** planning loads exploration context; the orchestrator needs clean context for
  DAG scheduling, integration, and verification. With the plan merged to `main`, the orchestrator starts clean and
  can be stopped / resumed / re-run at will.

Run the **`story-agent`** in its own session to produce `docs/features/{slug}/` (`feature.md` + `implementation.md`
+ story files) and to sync the GitHub Epic/Feature/Story issues, open the **planning-docs PR**, review it, and
merge it. Only then start the orchestrator. The orchestrator's **Phase 1 (DAG planning) is not the same as writing
`implementation.md`** - Phase 1 is execution scheduling (file-footprint disjointness, wave concurrency). The
story-agent emits a **DAG-ready Wave Plan inside `implementation.md`**, so Phase 1 validates and adjusts it rather
than deriving it.

## Principles

1. **The story is the spec.** Build to the acceptance criteria; ACs drive the tests. Don't invent scope (CLAUDE.md
   section 7).
2. **One engine, many thin modes** (CLAUDE.md section 2). A mode-related story should read as "the engine,
   configured this way" - if a builder forks the engine per mode, that is a smell to flag at integration.
3. **Parallelism is bounded by the dependency DAG and file-footprint disjointness, not by the wave labels.** Two
   builders editing the same file collide at merge even on separate worktrees.
4. **The umbrella stays green and warning-clean.** Integrate serially; gate before "done".
5. **The main session owns verification.** It holds the long-running dev servers (API + Vite) and drives the
   browser; builder subagents are ephemeral and only build + self-check.
6. **Child safety is non-negotiable** (CLAUDE.md section 5). Any builder touching a free-text surface routes it
   through the safety filter, honors the family-safe toggle, and collects no PII. This is in every builder brief.

## Roles

- **Orchestrator (main session):** plans the DAG, fans out each wave, integrates, runs the gates, runs the app for
  verification, manages PRs + the tracker. Runs at **high** reasoning effort.
- **Builder subagent:** one story (or a tight, file-disjoint cluster) on its own worktree; builds to the ACs; runs
  local checks; returns a structured result. Never holds a long-running server. **medium** effort by default;
  **low** for mechanical stories, **high** for novel/stateful ones. Web work uses `frontend-agent`; API work is a
  general builder against the `api/` conventions.

## Phase 0 - Setup

- **Umbrella branch:** `feat/{slug}` off an up-to-date `main` (`git switch -c feat/{slug}`).
- **Where to root the orchestrator:** the repo root (`/home/user/quibblestone`). Repo-specific agents
  (`code-review`, `frontend-agent`, ...) and skills (`ci-check`, `commit`, `orchestrate-feature`) resolve **only
  from the repo root**. Worktree isolation operates on this repo.
- **No generated API client.** The api <-> web contract is the **SignalR hub method signatures** (`api/src/Hubs/`
  consumed by `web/src/signalr/useGameHub.ts`) plus any future REST DTOs - kept in sync by hand. So the
  "regenerate the typed client" step from generic versions of this pattern does not exist here. When an API story
  changes a hub signature, the **consuming web story depends on it** (serialize API -> web), but there is no codegen
  step between them.
- **Web worktrees:** junction the worktree's `node_modules` to the main checkout to skip a full `npm install`.
  **Footgun:** remove the junction **before** `git worktree remove --force`, or the recursive delete follows it and
  wipes the shared install.

## Phase 1 - Plan the parallelization

From `implementation.md` (waves + per-story tech notes + reuse map) build the dependency graph:

- **Foundation first, serialized:** the scaffold and shared types/contracts everyone imports (for QuibbleStone the
  usual roots are `design-system` theme/components, the `template-model` schema, the engine interface, and the
  `child-safety` filter - things many stories call).
- **Then fan out file-disjoint stories.**
- **Serialize the API -> web chain:** an API/hub story lands, then the consuming web story (which calls the new hub
  method via `useGameHub`) can compile.
- **Sizing rule:** a builder = one story or a tight cluster whose files are mostly disjoint from concurrent
  siblings. Overlap on a shared file (e.g. two stories both editing `theme.ts` or `useGameHub.ts`) -> serialize them
  or give them to the **same** builder.

Output a wave plan listing, per story: issue number, files it creates/owns, depends-on, can-run-with. Show the plan
and the per-wave concurrency to the user before fanning out.

## Phase 2 - Fan out per wave (Workflow)

Run **one Workflow per wave** (explicit opt-in: tell the session "use a workflow"). Each file-disjoint story
becomes a **worktree-isolated builder** with a self-contained brief:

- the **story file** (ACs) + its **`implementation.md` per-story note** + the relevant **reuse-map** rows;
- the **builder guardrails** (below);
- a **schema'd return**: `{ summary, filesTouched, ciStatus, openQuestions }`.

Add a **verify stage** to the pipeline: a `code-review` pass per story before it is eligible to integrate (Gate 1).

### Builder guardrails (put these verbatim in every builder brief)

**Web (`web/`):**
- **Style through the MUI theme** (`web/src/theme.ts`): no hex/rgb literals, no raw-px spacing in components; add
  the token to the theme and use it. Land new look-and-feel as theme changes.
- **Reuse the shared design-system contracts** - the single AppBar and Button family; do not re-spec per screen.
- **FontAwesome only**, registered in `web/src/fontawesome.ts`. Never import `@mui/icons-material`.
- **One SignalR connection**, owned by `web/src/signalr/useGameHub.ts`. New real-time work adds `invoke`s/handlers
  to it - never a second `HubConnection`. Hub/API URLs come from `import.meta.env` (`VITE_*`).
- **TypeScript strict:** no `any` (use `unknown` + narrowing/generics); guard instead of non-null `!`; props as
  `interface {Component}Props`.
- **Forms** use react-hook-form with controlled MUI inputs.
- **Big tap targets / family-friendly UX** (README section 10): chunky, high-contrast, kid-readable.

**API (`api/`):**
- REST (controllers) and real-time (hubs) stay in their folders; shared logic in services - not duplicated.
- Async all the way; no blocking `.Result`/`.Wait()`. Nullable reference types respected.
- No secrets in committed config (appsettings/env/Key Vault, not literals).

**Cross-cutting (every builder):**
- **Child safety:** any surface that submits or displays free text routes it through the safety filter before
  anyone sees it; honor the family-safe toggle; collect no PII (anonymous: nickname + Guardian variant only).
- **No new deps** from the deliberately-excluded list: Azure Functions, Redux/Zustand, an i18n framework,
  AG Grid/Syncfusion, MSAL. There is **no i18n** here - user-facing strings are plain (no translation hook).
- **Verbose header comment** on any new key file (CLAUDE.md section 4).
- **Prose/commits:** hyphens, colons, parentheses - **never em dashes**.
- Don't hold a long-running dev server (that's the main session's job). Don't bypass a gate (`--no-verify`, etc.).

## Phase 3 - Integrate + gate

- Main session **merges each builder branch onto the umbrella serially**, resolving conflicts where footprints
  overlapped.
- Re-run **`ci-check`** on the merged result and **`code-review`** on the integrated delta (Gate 2 below).
- Keep the umbrella **green and warning-clean**.

Do not merge a builder branch that is not `code-review`-clean and `ci-check`-green.

## Phase 4 - Verification checkpoint (the "natural stopping points" = wave boundaries)

The main session boots the app against the **latest umbrella code** and walks the wave's journeys with the user.

- **Prereqs:** none beyond the repo. Rooms are **ephemeral in-memory** (in-process SignalR hub - CLAUDE.md
  section 10), so there is no DB to seed and no local infra to start. Storage + Key Vault are provisioned but
  unused by the skeleton; the free FontAwesome packs need no registry token.
- **Run** (the main session holds these):
  ```bash
  dotnet run --project api/QuibbleStone.Api.csproj      # http://localhost:5180
  cd web && npm install && npm run dev                   # http://localhost:5173 (URLs from web/.env.development)
  ```
  Use the `run` / `verify` skills if present. The pre-installed Playwright Chromium lives at
  `/opt/pw-browsers/chromium`.
- **Drive it:** walk the wave's user journeys via the browser MCP. For the multiplayer / real-time stories use
  **two browser contexts** (the 2-player sync check that README section 8 calls out as the scary part to de-risk
  early): one creates a room, the other joins by code; confirm roster, round, and the **shared reveal** sync
  without a refresh.
- **Regression:** confirm unrelated surfaces (Home, Join) are unchanged.
- Capture feedback -> fold into fix items / the next wave (and into the feature's Decisions log).
  **Do not proceed without the user's sign-off.**

## Phase 5 - PRs + tracker (GitHub)

> Command cheat sheet + status mapping: [`GITHUB_TRACKER.md`](GITHUB_TRACKER.md).

- **Draft PR** for the umbrella: `feat/{slug}` -> `main`, opened with `--draft`. Promote to ready only after Gate 3
  passes.
- **Link every Story issue to the PR.** Put `Closes #<issue>` for each story in the PR body so merging the umbrella
  auto-closes them.
- **Issue status** as stories build: flip the body-text `**Status:**` line **and** swap the `status:*` label
  (`status:todo` -> `status:in-progress` -> `status:in-review`), then close on merge. The markdown story file is
  canonical; the issue mirrors it.

## The gate model (code review + CI before PRs)

Three gate points. The first two are **local** (fast, mirror the CI workflow); the third is the **remote** GitHub
Actions run on the pushed branch. Nothing becomes a ready-for-review PR until all three pass.

```
  plan DAG -> fan out builders (Workflow) -> [GATE 1: per-builder]
                each builder on its own          - local CI (ci-check): API build + web build + Bicep validate
                worktree, builds to ACs          - code-review pass (Workflow verify stage)
                                                    v only clean branches are eligible to integrate
            integrate onto umbrella (serial) -> [GATE 2: post-integration]
                resolve overlap conflicts         - re-run full ci-check on the MERGED result
                                                  - code-review the integrated delta
                                                    v umbrella stays green + warning-clean
            main-session app verification -> user sign-off
                                                    v
            open / update DRAFT PR          -> [GATE 3: pre-ready]
                                                  - gh pr checks --watch  (Actions green)
                                                  - final code-review clean
                                                  - verification signed off
                                                    v only now: gh pr ready  (draft -> ready)
  <- next wave repeats -+
```

Why **two** CI runs: a builder can be green in isolation and still break the umbrella, because the merge introduces
interactions its isolated worktree never saw (a renamed export another story consumes, a shared type that drifted,
a `theme.ts` two stories both touched). Gate 1 keeps junk out of the merge cheaply; Gate 2 is the run that actually
protects the umbrella.

Gate 3 wiring (GitHub):

```bash
# After pushing the umbrella branch and opening the draft PR:
gh pr checks --watch                 # blocks until all required Actions checks finish
gh pr view --json statusCheckRollup  # confirm conclusion == SUCCESS
# ... only after Gate 3 is green AND verification is signed off:
gh pr ready                          # draft -> ready-for-review
```

## Guardrails / known traps (also in builder briefs)

- Worktree isolation prevents working-tree collisions, **not merge conflicts** - size by file-disjointness.
- A builder never holds a long-running dev server - verification is the main session's job.
- In prose/docs/commits use hyphens/colons/parens, **not em dashes**.
- Don't bypass any gate (`--no-verify`, `gh pr ready` before checks pass) without telling the user.
- Don't pull parked Phase 2-4 ideas into a Slice 1 wave (CLAUDE.md section 7).

## The loop (summary)

`plan DAG` -> for each wave: `Workflow fan-out to worktree builders` -> `code-review (Gate 1)` ->
`integrate to umbrella` -> `ci-check + code-review (Gate 2)` -> **main-session app verification + your feedback** ->
`update draft PR` -> `Actions green + gh pr ready (Gate 3)` -> next wave.

## Effort

- **Orchestrator (main session): high** - DAG planning, builder sizing, integration/conflict resolution, gate
  adjudication, reading verification results are the costly decisions.
- **Builder subagents: medium by default**; **low** for trivial/mechanical, **high** for novel/stateful.
- **Code-review / verify stage: high.**

## The per-wave Workflow skeleton

When `/orchestrate-feature` reaches a wave, it runs one Workflow like this (fan out builders -> Gate 1 per story).
Integration, Gate 2, and verification stay in the main session.

```js
export const meta = {
  name: 'wave-builder',
  description: 'Fan out file-disjoint stories to worktree builders; gate each with ci-check + code-review',
  phases: [{ title: 'Build' }, { title: 'Verify' }],
}

const STORIES = args.stories   // [{ id, brief }] from the DAG plan

const results = await pipeline(
  STORIES,
  // Stage 1: build to ACs on an isolated worktree, self-run local ci-check
  s => agent(
    `${s.brief}\n\nWhen done, run the local ci-check commands (API build + web build + Bicep validate) and report ciStatus.`,
    { label: `build:${s.id}`, phase: 'Build', isolation: 'worktree',
      schema: { type:'object', required:['summary','filesTouched','ciStatus','openQuestions'],
        properties:{ summary:{type:'string'}, filesTouched:{type:'array',items:{type:'string'}},
          ciStatus:{type:'string'}, openQuestions:{type:'array',items:{type:'string'}} } } }
  ),
  // Stage 2: adversarial code-review of that builder's diff (Gate 1)
  (built, s) => built && agent(
    `Review the diff for story ${s.id} against the QuibbleStone checklist. Return the clean verdict + findings.`,
    { label: `review:${s.id}`, phase: 'Verify', agentType: 'code-review',
      schema: { type:'object', required:['clean','findings'],
        properties:{ clean:{type:'boolean'}, findings:{type:'array',items:{type:'object'}} } } }
  ).then(review => ({ story: s, built, review }))
)

return results.filter(Boolean)
// Orchestrator then integrates only review.clean===true && green ciStatus branches, serially,
// re-runs ci-check on the umbrella (Gate 2), boots the app for verification, then updates the draft PR.
```
