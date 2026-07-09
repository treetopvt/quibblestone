<!--
  Feature: control-plane. Layer 1 of ADR 0003 (docs/adr/0003-admin-platform-and-family-accounts.md).
  Use hyphens/colons/parentheses, never em dashes.
-->

# Feature: Control Plane

## Summary
A runtime settings service - one typed key catalog with code defaults, persisted overrides, a short
read cache, and a changed-by/at stamp - so an operator can flip a kill switch or retune an operational
knob without a redeploy. This feature generalizes the one bespoke persisted flag the codebase already
has (the Stripe live/test mode store) into a reusable mechanism, wires a system-flag scope into the
existing entitlement evaluation ahead of account grants, and migrates the handful of hardcoded
operational constants scattered across the API onto it.

## README reference
Section 3 (the entitlement seam - system flags sit ahead of it, still evaluated once at
session-creation); section 4 (architecture - config-presence remains the infrastructure-wiring layer,
this feature does not replace it); section 6 (child safety - the moderation auto-hide threshold moves
here); section 9 (infra footprint - no new Azure resource). Primary spec: `docs/adr/0003-admin-platform-
and-family-accounts.md`, "Layer 1 - control plane (`control-plane/01-03`)".

## Stories
| Story | Issue | Title | Status |
|---|---|---|---|
| 01 | #197 | The runtime settings service | Complete |
| 02 | #213 | Capability scopes: system flags in the entitlement evaluation | Not Started |
| 03 | #TBD | Knob migration | Not Started |

## Dependencies
`billing-entitlements` (already shipped): this feature generalizes `TableStorageActiveStripeModeStore` /
`IActiveStripeContext`'s pattern (`api/src/Billing/`) and reuses its `Entitlements:StorageConnectionString`
Table Storage account - no new Azure resource. `IEntitlementService` / `EntitlementCatalog` /
`SessionEntitlements` (already shipped, `api/src/Entitlements/IEntitlementService.cs`) is the contract
story 02 extends without changing its shape. `accounts-identity/05` (ADR 0003 Wave 1, re-keys
`StoredValueEntitlementService.cs` onto `AccountId`) is a HARD dependency of story 02 specifically -
corrected 2026-07-08 per the adversarial review; see the Decisions entry below. `sysadmin-console/06`
(the operator action-log store) is a soft, dependency-tolerant dependency of story 01 (AC-09) - the
write is wired through a seam that no-ops until `06` lands, so this feature does not hard-block on it.
`sysadmin-console` overall remains a downstream CONSUMER (the Operations tab console page reads this
feature's admin endpoints) - not a dependency of this feature.

## Design notes
**The razor:** config-presence stays for infrastructure wiring (connection strings, endpoints - decided
once, at startup, from where a resource is deployed); runtime settings are for operational knobs and
kill switches (decided by an operator, any time, without a redeploy). Anything an operator might
plausibly want to change without a redeploy is a settings key; anything that only changes because a
resource was (or was not) provisioned stays a config-presence switch. A settings-key system flag can
force-disable a configured capability but can never enable one that is not configured - config-presence
remains the floor.

This feature generalizes, rather than replaces, two precedents already in the codebase:
- `TableStorageActiveStripeModeStore` (`api/src/Billing/TableStorageActiveStripeModeStore.cs`): a single
  fixed-key row in Table Storage, a safe default on a missing row, a config-presence-gated real-store vs.
  working-in-memory-store split. Story 01 generalizes the single fixed row into one row per settings key
  in the same table shape.
- `ActiveStripeContext`'s short in-memory cache (`api/src/Billing/IActiveStripeContext.cs`, a few
  seconds): the precedent for "an operator's flip is visible to new reads within a short, bounded window
  without an app restart, and a hot path never pays a storage round-trip per call."

The console PAGE for this (an Operations tab list of settings with an edit affordance) belongs to
`sysadmin-console`, not here - this feature exposes only the admin API endpoints (GET all, PUT one
override, DELETE one override) behind the existing single Operator policy. The seam is deliberately
thin so `sysadmin-console` slots a page in later without any change to this feature's contract.

Story 02 does not change `IEntitlementService`'s public contract or the capture-once discipline
(README section 3, ADR 0002's retained invariant) - only what feeds the evaluation changes: a system
scope, sourced from this feature's settings keys, WINS OVER account grants in precedence (a force-off
cannot be reopened by a grant), implemented as a post-compose FILTER applied after the existing
baseline+grant composition, not as an evaluate-system-first branch (see story 02's Context for why that
distinction matters to a builder). A system flag is a force-disable-only override; the moment nothing
has been overridden, evaluation is bit-for-bit identical to today.

Story 03 (knob migration) is explicitly the highest-file-footprint story in this feature and, per ADR
0003's cross-feature build order, must be scheduled ALONE in its wave slot - not just within this
feature but across the whole ADR 0003 body of work - because it touches files several other in-flight
features also touch (`Program.cs`, `api/src/Rooms/SeatGraceService.cs`, `api/src/PublishedTales/`,
`api/src/Ai/`, `api/src/Admin/`).

## Parked - Phase 2+
- **Per-session or per-account setting overrides.** This feature's settings are app-wide (one value per
  key for the whole deployment) - never scoped to a session, room, purchaser, or family account. A
  per-account override is a distinct, much larger feature (closer to a real experimentation platform)
  and is not in scope here.
- **Scheduled flips / timed rollbacks.** A setting change takes effect the moment it is written (within
  the cache window); there is no "apply this at midnight" or "auto-revert after an hour" mechanism.
- **A/B rollouts or percentage-based flags.** Every settings key is a single app-wide value, not a
  cohort-split experiment. If a future feature wants gradual rollout, that is a new ADR.
- **The settings console page itself.** Belongs to `sysadmin-console` (Operations tab) once this
  feature's endpoints exist.
- **Broader RBAC / scoped operator authorization** (a `support`/`content`/`ops` scope model). This
  feature's endpoints sit behind the existing single Operator policy, same as every other admin
  endpoint today; `sysadmin-console`'s Layer 3 work is the one that introduces scopes.

## Decisions
- **2026-07-08** - ADR 0003 accepted; this feature folder created as its Layer 1 decomposition
  (`control-plane/01-03`). See `docs/adr/0003-admin-platform-and-family-accounts.md`.
- **2026-07-08** - Two ADR 0003 cross-feature hazards recorded (and mirrored into `implementation.md`'s
  Wave Plan): (a) story 01 registers services in `Program.cs`, a systemic hotspot every wave-1 story
  across the whole ADR also touches - merge serially, small PRs, never batched; (b) story 02 edits
  `api/src/Entitlements/` and must serialize with `accounts-identity/06` (ADR 0002 Decision F wiring),
  which touches the same folder in the same ADR wave.
- **2026-07-08 (adversarial review resolutions)** - a five-lens adversarial review of ADR 0003 (run the
  same day, before any code) named this feature in its "Security posture" section and its corrected
  Cross-feature build order table. Stories 01-03 are revised to fold in the review's findings:
  - **Story 01** gains: per-key numeric `Bounds` enforced on PUT (a type-only check let an operator
    uncap AI spend, mass-expire the tale/vault store via `tales.ttlDays=0`, or disable a rate limiter
    via a huge permit count - AC-08); a requirement to append an operator action-log row on every
    settings PUT/DELETE via the `IOperatorActionLog` seam, satisfiable before `sysadmin-console/06`
    lands (AC-09) - this resolves the prior contradiction between this feature's Out of Scope and ADR
    0003 Amendment 2; and confirmation-gating (`RequiresConfirmation`) for the `*.enabled` kill switches
    and the AI monthly spend ceiling, so a flip of a load-bearing switch cannot be an accidental
    one-field PUT (AC-10).
  - **Story 02** now states PRECEDENCE ("system force-off wins over grants") and IMPLEMENTATION
    ("a post-compose filter, not an evaluate-first branch") as two separate things, correcting
    ambiguous ordering language that could have led a builder to an unnecessary early-branch structure.
    Its dependency is corrected: a HARD depends-on `accounts-identity/05` (which re-keys
    `StoredValueEntitlementService.cs`, the file this story edits) is added; the prior "serialize with
    `accounts-identity/06`" hazard is retracted as stale (06 does not touch `api/src/Entitlements/`).
    The three config-presence conditions this story ANDs against are documented as NOT already
    reusable booleans (they are inline `if` conditions at three different `Program.cs` call sites) -
    the story now calls for extracting them into a `SystemConfigPresence` value, and notes that
    `StoredValueEntitlementService`'s constructor gaining dependencies ripples into its existing test
    fixtures, not just a wiring line.
  - **Story 03** gains a read-site CLAMP (`[1, sane-max]`) on every rate-limit-permit knob, inside the
    partition-creation factory lambda, independent of story 01's catalog-level bounds check - because
    `FixedWindowRateLimiterOptions.PermitLimit <= 0` throws inside that lambda, which would otherwise
    turn a bad settings value into a 500 on every request (AC-08). It also records a `PublishedTales/`
    ordering hazard with `keepsake-vault/04` (both touch that folder in Wave 3) and restates that
    `Program.cs` is a four-story Wave 3 hotspot, not a two-story one.
  See `docs/adr/0003-admin-platform-and-family-accounts.md`, "Security posture (from the 2026-07-08
  adversarial review)" for the source findings, and `implementation.md`'s Wave Plan / per-story tech
  notes for where each resolution lands.
