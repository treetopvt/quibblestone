<!--
  Story: control-plane/02. Depends on control-plane/01. See feature.md and
  docs/adr/0003-admin-platform-and-family-accounts.md, Layer 1.
  Use hyphens/colons/parentheses, never em dashes.
-->

# Story: Capability scopes: system flags in the entitlement evaluation

**Feature:** Control Plane (`docs/features/control-plane/feature.md`)  ·  **Status:** Not Started  ·  **Issue:** #213

## Context
`IEntitlementService.EvaluateForSession` (`api/src/Entitlements/IEntitlementService.cs`) today composes
exactly two layers: the default-unlocked baseline, then a resolved purchaser's active grants
(`StoredValueEntitlementService`). ADR 0003 Layer 1 adds a third concern: a system scope - an app-wide
kill switch / not-yet-launched flag, backed by this feature's settings service (`control-plane/01`) -
that can force a capability OFF for every session regardless of any account grant.

**Precedence vs. execution order (these are two different things - do not conflate them):**
PRECEDENCE is "system force-off wins over any account grant" - if a system flag says a capability is
off, no grant can turn it back on, for any session. The simplest, correct IMPLEMENTATION of that
precedence is a **post-compose FILTER**, not an "evaluate system-first, branch early" mechanism: compose
the baseline + account grants exactly as today (unchanged), THEN, as the last step before the result is
captured into `SessionEntitlements`, remove any capability whose owning system flag currently reads
`false`. A builder reading only "system flag wins" might reach for an early-branch/short-circuit
structure that skips grant evaluation entirely when a flag is off - that is unnecessary complexity and
this story explicitly does NOT want it: the existing baseline+grant composition runs unconditionally and
unchanged; the system-flag step only ever SUBTRACTS from its result afterward. The `IEntitlementService`
contract and the capture-once discipline (evaluated exactly once, at session-creation, never
re-evaluated per-request - README section 3, ADR 0002's retained invariant) do not change; only what
feeds the evaluation does. First system keys (ADR 0003): `publishing.enabled`, `ai.enabled`,
`email.enabled` - each defaulting to the CURRENT config-presence behavior, so shipping this story changes
zero observed behavior until an operator explicitly overrides one. See `feature.md`'s Design notes for
the razor and the cross-feature hazard this story carries.

## Acceptance Criteria
- [ ] AC-01: Given `SettingsCatalog` (story 01) registers `ai.enabled`, `publishing.enabled`, and
      `email.enabled` as boolean settings keys (code default `true`), when `SystemFlagEvaluator` computes
      each key's EFFECTIVE value as (the existing config-presence check for that infrastructure - AI
      endpoint configured, published-tales storage configured, an email provider configured) AND (the
      settings flag), then it is never `true` when the underlying infrastructure is not configured, for
      every configured/unconfigured combination of all three keys (surfacing this computed effective
      value in the GET /api/admin/settings response is deferred with the rest of the console UI - see Out
      of Scope - so that endpoint's raw override-or-default response is unchanged by this story).
- [ ] AC-02: Given `ai.enabled` is at its code default (`true`) and AI is configured, when a room is
      created (`GameHub.CreateRoom`), then the resulting `SessionEntitlements.IsUnlocked("ai.onDemand")`
      is UNCHANGED from today's shipped behavior (still governed solely by the existing default-
      unlocked/grant composition) - zero regression.
- [ ] AC-03: Given an operator overrides `ai.enabled` to `false`, when a NEW room is created after the
      settings cache window (story 01) elapses, then `SessionEntitlements.IsUnlocked("ai.onDemand")` is
      `false` for that session regardless of any active purchaser grant for that capability - the
      system flag's force-off PRECEDES account grants in precedence (wins over any grant), implemented
      as a post-compose filter step that runs after the baseline+grant composition, not as a branch that
      skips grant evaluation.
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
- A console UI for these flags (`sysadmin-console`'s Operations tab), and surfacing the AC-01 computed
  effective value in the GET /api/admin/settings response - both deferred together; the admin GET (story
  01's `SettingsController`) keeps returning its raw override-or-default view, unmodified by this story.
- RBAC / scoped operator authorization (parked in `feature.md`).

## Technical Notes
Footprint is deliberately confined to `api/src/Entitlements/` plus one small `Program.cs` wiring edit -
see the cross-feature hazard below.

- Add the three system-scope settings key definitions to `SettingsCatalog` (story 01's list, in
  `api/src/Settings/`) alongside whatever `SettingType` fits (`Bool`, default `true`).
- **Extract `SystemConfigPresence` - these are NOT reusable booleans today.** The three "is this
  infrastructure configured" checks this story needs to AND against each system flag do not exist as
  named, injectable values anywhere in the tree today - they are inline conditions, in three different
  shapes, at three different call sites in `Program.cs`:
  - AI: `if (string.IsNullOrWhiteSpace(aiOptions.Endpoint))` (`Program.cs` ~line 163).
  - Publishing: `if (string.IsNullOrWhiteSpace(talesConnectionString))` (`Program.cs` ~line 436).
  - Email: `if (!emailOptions.IsConfigured)` (`Program.cs` ~line 688).
  This story's real Program.cs work is extracting the boolean each condition already computes into one
  small record/struct, e.g. `SystemConfigPresence(bool AiConfigured, bool PublishingConfigured, bool
  EmailConfigured)`, constructed ONCE where those options are already bound (reusing the same
  `aiOptions.Endpoint` / `talesConnectionString` / `emailOptions.IsConfigured` expressions - do not
  re-derive them a second way), and registered as a singleton alongside the existing config-presence
  branches (do not restructure those branches themselves - only add the extraction). This is more than a
  "thread it through" wiring line: it is a small refactor touching three separate call sites in one file.
- Add a small system-scope composition unit inside `api/src/Entitlements/` (e.g. a
  `SystemFlagEvaluator`) that:
  1. Reads the three settings via `IRuntimeSettingsService` (story 01).
  2. ANDs each with the corresponding field of the injected `SystemConfigPresence` (config-presence
     remains the floor - a settings override can force OFF a configured feature but never force ON an
     unconfigured one).
  3. Runs as a POST-COMPOSE FILTER, not an evaluate-first branch (see Context): after
     `StoredValueEntitlementService.EvaluateForSession` computes its existing baseline+grant result
     exactly as it does today, this step removes `EntitlementCatalog.AiOnDemand` (and any future `ai.*`
     sibling) from that result's unlocked set when the effective `ai.enabled` reads `false`, immediately
     before the result is captured into `SessionEntitlements` (AC-03). `publishing.enabled`/
     `email.enabled` are computed (AC-01) but have no capability key to filter yet (Out of Scope).
- `SessionEntitlements`, `EntitlementCatalog`'s existing keys, `DefaultUnlockedEntitlementService`, and
  the `GameHub.CreateRoom` / `CloudGalleryController` call sites are UNTOUCHED (AC-06).
- **`StoredValueEntitlementService`'s constructor gains dependencies - this ripples into its test
  fixtures.** The class picks up `IRuntimeSettingsService` (story 01) and the new `SystemConfigPresence`
  value as constructor parameters so the filter step has what it needs. This is NOT "one wiring line in
  Program.cs" - every existing call site that constructs `StoredValueEntitlementService` directly (most
  notably `StoredValueEntitlementServiceTests`' test fixtures) must be updated to supply both new
  dependencies (a fake/stub `IRuntimeSettingsService` and a `SystemConfigPresence` value per test's
  configured/unconfigured scenario), alongside the one real `Program.cs` DI registration. Budget for
  touching the existing test file, not just adding a new one.

**Cross-feature hazard and dependency (ADR 0003, corrected 2026-07-08):** the real chain on
`StoredValueEntitlementService.cs` is **`accounts-identity/05` (Wave 1, re-keys the class onto
`AccountId`) -> `control-plane/02` (Wave 2, this story, adds the system-flag filter step)** - this story
has a HARD depends-on `accounts-identity/05`, not merely a same-wave scheduling hazard, because 05
re-keys the exact class this story edits and must land first. `accounts-identity/06` (ADR 0002 Decision
F wiring) does NOT touch `api/src/Entitlements/` - it only edits the `CreateRoom` call site in
`GameHub.cs` (the `EvaluateForSession(purchaserIdentity, ...)` signature already accepts the argument) -
so the earlier "serialize with accounts-identity/06" hazard note was stale and is corrected here: there
is no file collision with `06`. `billing-entitlements/08` (grant metadata + resync) co-occupies
`api/src/Entitlements/` in the same wave (it edits `EntitlementGrant.cs` + the grant store) - sequence
that story's record-shape change before or after this story's edit to the same consumer, not
concurrently, even though it is not a hard code dependency. See `implementation.md`'s Wave Plan.

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
`control-plane/01` (the runtime settings service this story reads through - concrete API:
`IRuntimeSettingsService.GetBoolAsync`, `SettingsCatalog`, `SettingDefinition`) AND
`accounts-identity/05` (Wave 1 - re-keys `StoredValueEntitlementService.cs`, the exact file this story
edits, onto `AccountId`; a HARD depends-on, not a scheduling note, since 05 must land first). Does NOT
collide with `accounts-identity/06` (that story does not touch `api/src/Entitlements/` - corrected
2026-07-08, see the cross-feature hazard note above). Coordinate (not a hard dependency) with
`billing-entitlements/08`, which co-occupies `api/src/Entitlements/` in the same wave.
