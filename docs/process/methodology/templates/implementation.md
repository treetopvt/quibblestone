<!--
  TEMPLATE: implementation.md - the planning-to-orchestration bridge, generalized.
  Copy into docs/features/{slug}/ and fill in. Sized by TWO axes (file-disjointness AND semantic
  coupling), carries an explicit integration-seam row and a `stack:` field per story so no stack is
  orphaned. DISPOSABLE after merge - do not maintain or backfill it onto shipped features.
  Prose style: hyphens, colons, parentheses - never em dashes.
-->

# Implementation plan: <feature>

> The bridge between planning and orchestration. Disposable after merge. The wave plan is DAG-ready:
> the orchestrator validates and adjusts it rather than deriving it.

## Reuse map

Existing components / modules / utilities each story must reuse instead of reinventing (this is what
keeps parallel builders consistent and faithful to the architectural bet).

| Concern | Reuse | Where |
|---|---|---|
| <shared UI / theme> | <the shared thing> | <path> |
| <real-time / transport> | <the one connection/client> | <path> |
| <domain / safety / auth> | <the one service> | <path> |
| <config> | <env mechanism> | <path> |

## The integration seam (owner: the orchestrator ONLY)

The composition root - the file that is **disjoint from nothing** and therefore can never be a wave
story. List it explicitly so no builder edits it in parallel:

| Integration seam | What it wires | Edited by |
|---|---|---|
| `<route/provider tree, DI container, module registry, main entrypoint>` | routes, providers, DI registrations | **orchestrator only, serially, between waves** |

**Seam-hardening schedule** (not "freeze once"): seed the seam at v0 in Wave 1; reserve extension
fields `<name them>`; budget **one hardening pass after the first consumer wave** (Wave `<n>`) for the
highest-coupling seams `<name them>`.

## Wave plan (DAG)

Sized by **two axes**: file-footprint disjointness (no working-tree collision) AND semantic coupling
(shared types, protocols, migrations, config, DI). Disjoint files can still be coupled through a
contract - that coupling is what breaks at integration. `stack:` tags which builder role + which gate
owns each story (every stack gets one; none orphaned).

| Story | Issue | Stack | Files it owns | Coupling surface | Depends-on | Can-run-with | Wave | Effort |
|---|---|---|---|---|---|---|---|---|
| 01 (foundation seam) | #<n> | <stack> | `<shared types/scaffold>` | `<who imports it: N importers>` | - | - | 1 | high |
| 02 | #<n> | <stack> | `<disjoint footprint>` | `<shared contract, if any>` | 01 | 03 | 2 | medium |
| 03 | #<n> | <stack> | `<disjoint footprint>` | `<...>` | 01 | 02 | 2 | medium |
| 04 (producer) | #<n> | <stack:api> | `<contract/signature>` | `<the contract 05 consumes>` | 01 | - | 2 | medium |
| 05 (consumer) | #<n> | <stack:web> | `<calls 04's contract>` | `<coupled to 04 via contract>` | 04 | - | 3 | medium |

**Concurrency per wave:** Wave 1 = 1 (foundation seam). Wave 2 = {02, 03, 04}. Wave 3 = 05. On a
coupled repo expect this to collapse toward serial - say so here if it does, and name the seam-creation
work the early waves must do before fan-out pays off.

## Per-story tech notes

### 01 - <foundation seam>
Approach, key files, what it exports that others import, which extension fields it reserves.

### 02 - <...>
Approach, key files, gotchas, which reuse-map rows apply, which stack's gate must pass.

## Cross-cutting

Non-negotiables every story honors (the architectural bet; the project's safety/security invariants;
no orphaned stack - each story's `stack:` has a builder and a gate). These also go into each builder's
guardrails brief.
