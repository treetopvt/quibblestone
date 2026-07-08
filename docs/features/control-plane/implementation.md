<!--
  Implementation plan for the control-plane feature (ADR 0003 Layer 1). Bridges feature.md + stories to
  orchestration. Use hyphens/colons/parentheses, never em dashes.
-->

# Implementation Plan: Control Plane

> The bridge between planning and orchestration. The **Wave Plan** below is DAG-ready: the
> `orchestrate-feature` skill's Phase 1 validates and adjusts it rather than deriving it. See
> [`docs/FEATURE_ORCHESTRATION_PLAYBOOK.md`](../../FEATURE_ORCHESTRATION_PLAYBOOK.md). This feature's
> wave numbers deliberately mirror ADR 0003's cross-feature wave table (`docs/adr/0003-admin-platform-
> and-family-accounts.md`, "Cross-feature build order"): `control-plane/01` is that table's Wave 1,
> `control-plane/02` is Wave 2, `control-plane/03` is Wave 3.

## Reuse map

| Concern | Reuse | Where |
|---|---|---|
| Persisted-runtime-flag pattern this feature generalizes | `TableStorageActiveStripeModeStore` / `IActiveStripeModeStore` (single fixed-key row, `CreateIfNotExists`-once guard, safe default on a missing row) | `api/src/Billing/TableStorageActiveStripeModeStore.cs`, `IActiveStripeModeStore.cs` |
| Short read-cache precedent | `ActiveStripeContext`'s `CacheTtl` (a few seconds; write-through reset on a flip) | `api/src/Billing/IActiveStripeContext.cs` |
| Storage connection to reuse (NO new Azure resource) | `Entitlements:StorageConnectionString` - already backs the entitlement grant store and the Stripe-mode store | `api/src/Program.cs` (config-presence block around the grant store / active-mode store registrations) |
| Config-presence idiom (real store vs. working in-memory store, chosen once at startup) | the `Telemetry:StorageConnectionString`, `PublishedTales:StorageConnectionString`, `Entitlements:StorageConnectionString` branches | `api/src/Program.cs` |
| Entitlement contract this feature extends, NOT replaces | `IEntitlementService`, `EntitlementCatalog`, `SessionEntitlements`, `StoredValueEntitlementService` | `api/src/Entitlements/IEntitlementService.cs`, `StoredValueEntitlementService.cs` |
| Session-creation call sites (unchanged by this feature) | `GameHub.CreateRoom`'s `EvaluateForSession` + `Room.CaptureEntitlements` call; `CloudGalleryController`'s per-request `gallery.cloudSync` check | `api/src/Hubs/GameHub.cs`, `api/src/CloudGallery/CloudGalleryController.cs` |
| Operator authorization boundary (existing, unchanged) | `[Authorize(Policy = OperatorSession.PolicyName)]`; the operator identity claim (`ClaimTypes.Name`) for `changedBy` stamping | `api/src/Admin/OperatorSession.cs`, `OperatorAuthenticationHandler.cs` |
| Admin-endpoint shape precedent (GET/PUT-by-key/verb pattern, operator-only) | `AdminEntitlementsController` | `api/src/Admin/AdminEntitlementsController.cs` |
| Table storage client | `Azure.Data.Tables` (already a project dependency - no new NuGet) | `api/src/Billing/`, `api/src/Entitlements/` (existing `TableStorage*Store` classes) |
| Hardcoded knobs story 03 migrates | see story 03's table | `api/src/PublishedTales/PublishedTalesController.cs`, `api/src/Program.cs`, `api/src/Ai/AiOptions.cs` / `AiQuota.cs` / `AiSpendBreaker.cs`, `api/src/Rooms/SeatGraceService.cs`, `api/src/Admin/OperatorLoginRateLimit.cs` |

New surfaces this feature introduces:
- `api/src/Settings/` (new folder) - `SettingType`, `SettingDefinition`, `SettingsCatalog`,
  `IRuntimeSettingsStore` + `TableStorageRuntimeSettingsStore` / `InMemoryRuntimeSettingsStore`,
  `IRuntimeSettingsService` / `RuntimeSettingsService` - story 01. Story 02 and story 03 each append
  entries to `SettingsCatalog`'s static list (serialized by wave, not concurrent).
- `api/src/Admin/SettingsController.cs` (or `api/src/Settings/SettingsController.cs`) - the three admin
  endpoints - story 01.

## Wave Plan (DAG)

Sizing rule: a builder owns files that are disjoint from its concurrent siblings; this feature has no
concurrency WITHIN itself - each story is scheduled alone in its own wave, because each subsequent story
either depends on the previous one's contract or shares a cross-feature-hazard file with something
outside this feature entirely.

| Story | Issue | Files it owns (footprint) | Depends-on | Can-run-with | Wave | Effort |
|---|---|---|---|---|---|---|
| 01 - runtime settings service | #TBD | NEW `api/src/Settings/` (catalog, store, service); NEW `api/src/Admin/SettingsController.cs`; one small `Program.cs` DI-wiring edit | - | - (outside this feature: other ADR 0003 wave-1 stories also touch `Program.cs` - see hazard (a) below) | 1 | high |
| 02 - capability scopes (system flags) | #TBD | EDITS `api/src/Entitlements/StoredValueEntitlementService.cs` (system-flag composition step); EDITS `api/src/Settings/SettingsCatalog.cs` (appends 3 keys); one small `Program.cs` DI-wiring edit (threads 3 config-presence booleans through) | 01 | - (outside this feature: must serialize with `accounts-identity/06` - see hazard (b) below) | 2 | medium |
| 03 - knob migration | #TBD | EDITS `api/src/PublishedTales/PublishedTalesController.cs`, `api/src/Program.cs` (rate-limiter factories), `api/src/Ai/AiOptions.cs`, `api/src/Ai/AiQuota.cs`, `api/src/Ai/AiSpendBreaker.cs`, `api/src/Rooms/SeatGraceService.cs`, `api/src/Admin/OperatorLoginRateLimit.cs`; EDITS `api/src/Settings/SettingsCatalog.cs` (appends 7 keys); possibly `api/src/Admin/ReportedTalesController.cs` if it also reads `AutoHideThreshold` directly | 01 | - (widest footprint in the feature; ADR 0003 requires it run ALONE in its slot, not just within this feature) | 3 | high |

**Concurrency per wave:** Wave 1 = 01 alone (foundation - everything imports its `IRuntimeSettingsService`
shape). Wave 2 = 02 alone. Wave 3 = 03 alone. There is no wave in this feature with more than one story
running concurrently - both because 02/03 each depend on 01's contract, and because 02 and 03 both touch
`Program.cs` (rate-limiter registrations for 03, the config-presence-boolean wiring for 02), so even
absent the two cross-feature hazards below they would not be safe to run in parallel with each other.

**Cross-feature hazards (ADR 0003), reproduced here for a builder who only reads this file:**
- **(a) `Program.cs` is a systemic hotspot.** Nearly every ADR 0003 wave-1 story
  (`accounts-identity/05`, `keepsake-vault/01`, `control-plane/01`, `sysadmin-console/04`) adds a service
  registration to `Program.cs`. The rule: stories touching `Program.cs` merge one at a time, small
  rebased PRs, never batched - even when everything else about them is otherwise parallel-safe. Story
  01's `Program.cs` edit (wiring `IRuntimeSettingsService`'s config-presence split) must be scheduled
  with this in mind relative to the OTHER wave-1 stories outside this feature, not just the two stories
  inside it.
- **(b) `api/src/Entitlements/` is a two-feature hotspot in ADR 0003's Wave 2.** `accounts-identity/06`
  (Decision F wiring: the hub connection carries the purchaser/family credential, `CreateRoom` resolves
  it to capabilities) and `control-plane/02` (this feature's system-flag composition) BOTH edit
  `StoredValueEntitlementService.cs` / `api/src/Entitlements/` in the same ADR wave. These two stories
  MUST serialize - schedule one to land and merge before the other starts, regardless of which team or
  builder is available first.

## Per-story tech notes

### 01 - The runtime settings service
**Approach:** generalize `TableStorageActiveStripeModeStore`'s single-fixed-row shape into one row per
settings key in a new `RuntimeSettings` table (same `Entitlements:StorageConnectionString` account, no
new resource), with `ActiveStripeContext`'s short cache precedent covering the read path. **Exports:**
`IRuntimeSettingsService` (the typed `GetBoolAsync`/`GetIntAsync`/`GetDecimalAsync`/`GetStringAsync`/
`GetAllAsync` surface every later story - inside and outside this feature - injects) and
`SettingsCatalog` (the static, append-only key registry). **Gotcha:** keep the admin endpoints'
authorization identical to `AdminEntitlementsController`'s existing pattern (`[Authorize(Policy =
OperatorSession.PolicyName)]`) - do not invent a new admin auth scheme for this one surface; scoping
(support/content/ops) is explicitly `sysadmin-console`'s later work, not this story's.

### 02 - Capability scopes: system flags in the entitlement evaluation
**Approach:** insert one composition step into `StoredValueEntitlementService.EvaluateForSession`,
between the existing baseline+grant composition and the final `SessionEntitlements` construction, that
force-removes any capability whose owning system flag reads `false` (settings value AND the existing
config-presence boolean for that infrastructure). Only `ai.enabled` -> `EntitlementCatalog.AiOnDemand`
has a live consumer in this story; `publishing.enabled` / `email.enabled` are registered and
effective-value-correct but unconsumed until a future capability references them. **Exports:** nothing
new for other stories to import - this is an internal composition change behind the unchanged
`IEntitlementService` contract. **Gotcha:** the ONLY files this story touches are inside
`api/src/Entitlements/` (plus the one `Program.cs` wiring line for the three config-presence booleans) -
resist the temptation to also wire `PublishedTalesController` or `IEmailSender` here; that is
deliberately Out of Scope so this story's footprint stays confined to the file that must serialize with
`accounts-identity/06` (hazard (b) above).

### 03 - Knob migration
**Approach:** for each of the seven named knobs, keep the existing hardcoded value as the settings key's
code default and change the READ SITE to ask `IRuntimeSettingsService` for the current effective value,
rather than trusting a value captured once at DI-construction time (`AiQuota`, `AiSpendBreaker`,
`SeatGraceService` all currently capture their tunable once in a constructor - this is the real work of
the story, not just adding catalog entries). Rate-limiter policies (`Program.cs`) read the current value
inside their partition-creation factory lambda, resolving `IRuntimeSettingsService` from
`HttpContext.RequestServices` rather than a value closed over at `AddRateLimiter` registration time.
**Exports:** nothing new for other stories - this is a pure migration of value SOURCE, not a new
capability. **Gotcha:** this is explicitly the widest-footprint story in the whole feature and, per ADR
0003, must be scheduled ALONE in its wave slot - not merely within `control-plane`, but relative to
every other ADR 0003 feature touching `Program.cs`, `api/src/Rooms/`, `api/src/PublishedTales/`,
`api/src/Ai/`, or `api/src/Admin/` at the same time. Do not batch it with anything, even something that
looks file-disjoint on paper.

## Cross-cutting concerns

- **Config-presence stays the floor.** No story in this feature may let a settings-key override enable
  a capability whose underlying infrastructure connection string / endpoint is not configured. A
  settings key can only ever narrow (force-disable), never widen, what config-presence already allows.
- **Capture-once is unchanged.** `IEntitlementService.EvaluateForSession` is still called exactly once,
  at session-creation. Story 02 changes composition INPUTS, never the call-site discipline. No story in
  this feature may add a per-request or per-round settings check into gameplay code paths.
- **No new Azure resource.** Every story reuses `Entitlements:StorageConnectionString`. If a future
  story is tempted to add a dedicated settings storage account, that is a smell to flag against this
  feature's Design notes.
- **Operator-only, no player-facing surface.** Every endpoint in this feature sits behind the existing
  `OperatorSession.PolicyName` policy. Nothing here is ever reachable from the anonymous play plane.
- **No console UI, no action log, no RBAC scopes.** All three are explicitly `sysadmin-console`'s later
  work (parked in `feature.md`); this feature's job is the mechanism and its admin API only.
- **No i18n** (plain strings). **Never em dashes** in any file this feature touches or creates.
