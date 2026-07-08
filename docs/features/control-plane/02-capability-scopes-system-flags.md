<!--
  Story: control-plane/02. Depends on control-plane/01. See feature.md and
  docs/adr/0003-admin-platform-and-family-accounts.md, Layer 1.
  Use hyphens/colons/parentheses, never em dashes.
-->

# Story: Capability scopes: system flags in the entitlement evaluation

**Feature:** Control Plane (`docs/features/control-plane/feature.md`)  ·  **Status:** Not Started  ·  **Issue:** #TBD

## Context
`IEntitlementService.EvaluateForSession` (`api/src/Entitlements/IEntitlementService.cs`) today composes
exactly two layers: the default-unlocked baseline, then a resolved purchaser's active grants
(`StoredValueEntitlementService`). ADR 0003 Layer 1 adds a third layer, evaluated FIRST: a system scope
- an app-wide kill switch / not-yet-launched flag, backed by this feature's settings service
(`control-plane/01`), that can force a capability OFF for every session regardless of any account grant.
The order is: system flag -> account grant -> session snapshot. The `IEntitlementService` contract and
the capture-once discipline (evaluated exactly once, at session-creation, never re-evaluated per-request
- README section 3, ADR 0002's retained invariant) do not change; only what feeds the evaluation does.
First system keys (ADR 0003): `publishing.enabled`, `ai.enabled`, `email.enabled` - each defaulting to
the CURRENT config-presence behavior, so shipping this story changes zero observed behavior until an
operator explicitly overrides one. See `feature.md`'s Design notes for the razor and the cross-feature
hazard this story carries.

## Acceptance Criteria
- [ ] AC-01: Given `SettingsCatalog` (story 01) registers `ai.enabled`, `publishing.enabled`, and
      `email.enabled` as boolean settings keys (code default `true`), when GET /api/admin/settings is
      called, then each appears with an EFFECTIVE value computed as (the existing config-presence check
      for that infrastructure - AI endpoint configured, published-tales storage configured, an email
      provider configured) AND (the settings flag) - never `true` when the underlying infrastructure is
      not configured.
- [ ] AC-02: Given `ai.enabled` is at its code default (`true`) and AI is configured, when a room is
      created (`GameHub.CreateRoom`), then the resulting `SessionEntitlements.IsUnlocked("ai.onDemand")`
      is UNCHANGED from today's shipped behavior (still governed solely by the existing default-
      unlocked/grant composition) - zero regression.
- [ ] AC-03: Given an operator overrides `ai.enabled` to `false`, when a NEW room is created after the
      settings cache window (story 01) elapses, then `SessionEntitlements.IsUnlocked("ai.onDemand")` is
      `false` for that session regardless of any active purchaser grant for that capability - the
      system flag is evaluated BEFORE account grants and wins.
- [ ] AC-04: Given an operator overrides `ai.enabled` to `false` AFTER a room already exists, when that
      already-created room's captured `SessionEntitlements` is read again, then it is unaffected (it
      still reflects whatever was captured at its own creation) - the flip only changes sessions
      created after the change, never an in-flight one (capture-once, unchanged).
- [ ] AC-05: Given AI is NOT configured at all (no `Ai:Endpoint`), when `ai.enabled` is left at its
      default or explicitly set to `true`, then the effective system flag still reads `false` - a
      settings override can never enable a capability whose underlying infrastructure is not
      configured (config-presence remains the floor, per ADR 0003).
- [ ] AC-06: Given `IEntitlementService.EvaluateForSession`'s public signature and the capture-once
      discipline, when this story ships, then no consumer (`GameHub.CreateRoom`,
      `CloudGalleryController`) changes its call shape - only the internal composition order inside
      `StoredValueEntitlementService` changes.

## Out of Scope
- Wiring `publishing.enabled` or `email.enabled` into an actual consuming capability key or call site
  (`PublishedTalesController`, `IEmailSender`). This story REGISTERS the three system-scope settings
  keys and proves the system-flag-before-account-grant composition mechanism using `ai.enabled` ->
  `ai.onDemand` as the one concrete, currently-consumable example. `publishing.enabled` and
  `email.enabled` are reserved (visible and settable via story 01's endpoints, effective value provably
  correct per AC-01) exactly the way `EntitlementCatalog.AiOnDemand` itself was originally reserved
  before anything consumed it - a future story wires an actual capability key against them when a
  session-scoped publish or email capability exists.
- Any change to `EntitlementCatalog`'s existing capability keys or their default-unlocked/grant
  behavior beyond inserting the system-flag composition step.
- A console UI for these flags (`sysadmin-console`'s Operations tab).
- RBAC / scoped operator authorization (parked in `feature.md`).

## Technical Notes
Footprint is deliberately confined to `api/src/Entitlements/` plus one small `Program.cs` wiring edit -
see the cross-feature hazard below.

- Add the three system-scope settings key definitions to `SettingsCatalog` (story 01's list, in
  `api/src/Settings/`) alongside whatever `SettingType` fits (`Bool`, default `true`).
- Add a small system-scope composition unit inside `api/src/Entitlements/` (e.g. a
  `SystemFlagEvaluator` or an inline step in `StoredValueEntitlementService.EvaluateForSession`) that:
  1. Reads the three settings via `IRuntimeSettingsService` (story 01).
  2. ANDs each with the corresponding ALREADY-EXISTING config-presence boolean (AI endpoint configured,
     published-tales storage configured, email provider configured) - these booleans already exist as
     the branch conditions in `Program.cs`'s config-presence registrations; thread them through as a
     small record/struct (e.g. `SystemConfigPresence(bool AiConfigured, bool PublishingConfigured, bool
     EmailConfigured)`) constructed once in `Program.cs` and injected into `StoredValueEntitlementService`.
     This is the ONE piece of this story that touches `Program.cs` - a small, mechanical wiring line, not
     a change to the `Ai`/`PublishedTales`/`Email` domain files themselves.
  3. When the effective `ai.enabled` is `false`, removes `EntitlementCatalog.AiOnDemand` (and any future
     `ai.*` sibling) from the composed unlocked set, AFTER the baseline + grant composition but before
     the result is captured into `SessionEntitlements` (AC-03). `publishing.enabled`/`email.enabled` are
     computed (AC-01) but have no capability key to filter yet (Out of Scope).
- `SessionEntitlements`, `EntitlementCatalog`'s existing keys, `DefaultUnlockedEntitlementService`, and
  the `GameHub.CreateRoom` / `CloudGalleryController` call sites are UNTOUCHED (AC-06).

**Cross-feature hazard (ADR 0003):** `accounts-identity/06` (ADR 0002 Decision F wiring: the hub
connection carries the purchaser/family credential, `CreateRoom` resolves it) ALSO edits
`StoredValueEntitlementService.cs` / `api/src/Entitlements/` in the same ADR 0003 wave. These two
stories MUST serialize - do not build or merge them in parallel, even though neither has a hard code
dependency on the other's output. See `implementation.md`'s Wave Plan.

## Tests
| AC | Test |
|---|---|
| AC-01 | `tests/QuibbleStone.Api.Tests/Settings/SystemFlagEffectiveValueTests.cs` - effective value under configured/unconfigured infra combinations |
| AC-02 | `tests/QuibbleStone.Api.Tests/Entitlements/StoredValueEntitlementServiceTests.cs` - default (`ai.enabled=true`, AI configured) baseline unchanged |
| AC-03 | same file - override `ai.enabled=false`, new session excludes `ai.onDemand` even with an active grant |
| AC-04 | same file - a room captured before the flip is unaffected when read again after |
| AC-05 | same file - AI unconfigured + `ai.enabled=true` still evaluates `false` |
| AC-06 | manual: diff review confirms `GameHub.CreateRoom` / `CloudGalleryController` call sites are unchanged |

## Dependencies
`control-plane/01` (the runtime settings service this story reads through). Must be scheduled to
serialize with `accounts-identity/06` per the cross-feature hazard above (not a build dependency, a
scheduling constraint - both touch `api/src/Entitlements/`).
