<!--
  Template: docs/features/{slug}/implementation.md
  NET-NEW for QuibbleStone (adopted with the feature-orchestration pattern). Required for every feature.
  This is the bridge between planning (feature.md + stories) and orchestration (the orchestrate-feature skill):
  per-story tech notes, a reuse map, and a DAG-ready Wave Plan. The orchestrator's Phase 1 validates and adjusts
  the Wave Plan rather than deriving it from scratch. Keep it accurate as scope changes.
  Use hyphens/colons/parentheses, never em dashes.
-->

# Implementation Plan: <Feature title>

> The bridge between planning and orchestration. The **Wave Plan** below is DAG-ready: the `orchestrate-feature`
> skill's Phase 1 validates and adjusts it rather than deriving it. See
> [`docs/FEATURE_ORCHESTRATION_PLAYBOOK.md`](../../FEATURE_ORCHESTRATION_PLAYBOOK.md).

## Reuse map

Existing components, hooks, and utilities each story should reuse instead of reinventing - this is what keeps
parallel builders consistent with the codebase (and with "one engine, many thin modes", CLAUDE.md section 2).

| Concern | Reuse | Where |
|---|---|---|
| Styling / theme tokens | the MUI theme (palette, typography, radii, spacing) | `web/src/theme.ts` |
| Shared UI contracts | the single AppBar + Button family (design-system) | `web/src/components/` |
| Icons | FontAwesome, registered once | `web/src/fontawesome.ts` |
| Real-time | the one SignalR connection hook | `web/src/signalr/useGameHub.ts` |
| API hub | the in-app SignalR hub | `api/src/Hubs/GameHub.cs` |
| Child safety | the single server-side safety filter | `api/...` (child-safety feature) |
| Config | `import.meta.env` (`VITE_*`) | `web/src/vite-env.d.ts`, `web/.env.development` |

## Wave Plan (DAG)

Sizing rule: a builder owns files that are **disjoint** from its concurrent siblings. Overlap on a file (e.g. two
stories both editing `theme.ts` or `useGameHub.ts`) means serialize the two, or merge them into one story.
Foundation first; the API/hub -> consuming-web chain is serial (no codegen step - the hub signature is the
contract).

| Story | Issue | Files it owns (footprint) | Depends-on | Can-run-with | Wave | Effort |
|---|---|---|---|---|---|---|
| 01 (foundation) | #<n> | `<shared scaffold/types it exports>` | - | - | 1 | high |
| 02 | #<n> | `<disjoint footprint>` | 01 | 03 | 2 | medium |
| 03 | #<n> | `<disjoint footprint>` | 01 | 02 | 2 | medium |
| 04 (api/hub) | #<n> | `api/src/Hubs/...`, `api/src/...` | 01 | - | 2 | medium |
| 05 | #<n> | `web/src/...` (calls the new hub method) | 04 | - | 3 | medium |

**Concurrency per wave:** Wave 1 = 1 (foundation). Wave 2 = {02, 03, 04} in parallel. Wave 3 = 05 (after the hub
method from 04 lands).

## Per-story tech notes

### 01 - <foundation>
Approach, key files, gotchas. What it exports that others import.

### 02 - <...>
Approach, key files, gotchas. Which reuse-map rows apply.

## Cross-cutting concerns

Anything every story must honor (the "one engine, many thin modes" lens for mode stories; the child-safety filter
on every free-text surface; no PII; no i18n - strings are plain; big tap targets). These also go into each
builder's guardrails brief.
