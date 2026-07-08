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
| Operator action-log seam (story 01 AC-09, dependency-tolerant) | `IOperatorActionLog` (`sysadmin-console/06`'s contract - `AppendAsync(operatorEmail, action, target, note, ct)`); if `06` has not landed yet, story 01 declares its own narrow copy of the same shape and a no-op/in-memory implementation, swapped for the real store with no call-site change once `06` merges | `sysadmin-console/06-operator-action-log.md`; the seam is registered in `Program.cs` alongside story 01's other wiring |
| `AccountId` re-key story 02 depends on | `StoredValueEntitlementService.cs`'s constructor and internals after `accounts-identity/05` re-keys it onto `AccountId` (ADR 0003 Wave 1) - story 02's filter step is added on top of the re-keyed class, not the pre-05 shape | `api/src/Entitlements/StoredValueEntitlementService.cs`; `docs/features/accounts-identity/05-*.md` |

New surfaces this feature introduces:
- `api/src/Settings/` (new folder) - `SettingType`, `SettingDefinition` (now carrying `Bounds` and
  `RequiresConfirmation`, per the 2026-07-08 adversarial-review revision), `SettingsCatalog`,
  `IRuntimeSettingsStore` + `TableStorageRuntimeSettingsStore` / `InMemoryRuntimeSettingsStore`,
  `IRuntimeSettingsService` / `RuntimeSettingsService` - story 01. Story 02 and story 03 each append
  entries to `SettingsCatalog`'s static list (serialized by wave, not concurrent), and each numeric entry
  MUST supply `Bounds`.
- `api/src/Admin/SettingsController.cs` (or `api/src/Settings/SettingsController.cs`) - the three admin
  endpoints, now validating bounds and confirmation before write and calling `IOperatorActionLog` on
  every successful write - story 01.
- `IOperatorActionLog` seam (interface + a no-op/in-memory implementation until `sysadmin-console/06`
  lands its Table Storage-backed store) - story 01, registered in `Program.cs`.
- `SystemConfigPresence` (`AiConfigured` / `PublishingConfigured` / `EmailConfigured`) - a small
  record/struct extracted from three previously-inline `Program.cs` conditions (see story 02's tech
  notes) - story 02.

## Wave Plan (DAG)

Sizing rule: a builder owns files that are disjoint from its concurrent siblings; this feature has no
concurrency WITHIN itself - each story is scheduled alone in its own wave, because each subsequent story
either depends on the previous one's contract or shares a cross-feature-hazard file with something
outside this feature entirely.

| Story | Issue | Files it owns (footprint) | Depends-on | Can-run-with | Wave | Effort |
|---|---|---|---|---|---|---|
| 01 - runtime settings service | #TBD | NEW `api/src/Settings/` (catalog with `Bounds`/`RequiresConfirmation`, store, service); NEW `api/src/Admin/SettingsController.cs` (bounds + confirmation validation, action-log write); NEW `IOperatorActionLog` seam (interface + no-op/in-memory impl, swappable for `sysadmin-console/06`'s real store); one small `Program.cs` DI-wiring edit | - (soft: `sysadmin-console/06`'s real action-log store, dependency-tolerant per the seam above) | - (outside this feature: other ADR 0003 wave-1 stories also touch `Program.cs` - see hazard (a) below) | 1 | high |
| 02 - capability scopes (system flags) | #TBD | EDITS `api/src/Entitlements/StoredValueEntitlementService.cs` (post-compose system-flag filter step; ctor gains `IRuntimeSettingsService` + `SystemConfigPresence`); EDITS `api/src/Settings/SettingsCatalog.cs` (appends 3 keys, each with `Bounds`/`RequiresConfirmation` as applicable); EDITS `tests/QuibbleStone.Api.Tests/Entitlements/StoredValueEntitlementServiceTests.cs` (ctor fixture update - not optional); one `Program.cs` edit extracting `SystemConfigPresence` from three previously-inline conditions (~163 AI, ~436 publishing, ~688 email) | 01 (concrete API: `IRuntimeSettingsService.GetBoolAsync`, `SettingsCatalog`); **`accounts-identity/05`** (HARD - re-keys the exact class this story edits; corrected 2026-07-08, was previously missing) | - (does NOT collide with `accounts-identity/06` - corrected 2026-07-08, see hazard (b) below; coordinate, not hard-depend, with `billing-entitlements/08` on the same folder) | 2 | medium |
| 03 - knob migration | #TBD | EDITS `api/src/PublishedTales/PublishedTalesController.cs`, `api/src/Program.cs` (rate-limiter factories, each clamped `[1, sane-max]` in its factory lambda), `api/src/Ai/AiOptions.cs`, `api/src/Ai/AiQuota.cs`, `api/src/Ai/AiSpendBreaker.cs`, `api/src/Rooms/SeatGraceService.cs`, `api/src/Admin/OperatorLoginRateLimit.cs`; EDITS `api/src/Settings/SettingsCatalog.cs` (appends 7 keys, each with `Bounds`); possibly `api/src/Admin/ReportedTalesController.cs` if it also reads `AutoHideThreshold` directly | 01 | - (widest footprint in the feature; ADR 0003 requires it run ALONE in its slot, not just within this feature; coordinate - not hard-depend - with `keepsake-vault/04` on `api/src/PublishedTales/` ordering, see hazard (c) below) | 3 | high |

**Concurrency per wave:** Wave 1 = 01 alone (foundation - everything imports its `IRuntimeSettingsService`
shape). Wave 2 = 02 alone (and gated on `accounts-identity/05` landing first). Wave 3 = 03 alone. There is
no wave in this feature with more than one story running concurrently - both because 02/03 each depend on
01's contract, and because 02 and 03 both touch `Program.cs` (rate-limiter registrations for 03, the
`SystemConfigPresence` extraction for 02), so even absent the cross-feature hazards below they would not
be safe to run in parallel with each other.

**Cross-feature hazards (ADR 0003), reproduced here for a builder who only reads this file (corrected
2026-07-08 per the adversarial review):**
- **(a) `Program.cs` is a systemic hotspot - FOUR stories touch it in Wave 3 alone.** Nearly every ADR
  0003 wave-1 story (`accounts-identity/05`, `keepsake-vault/01`, `control-plane/01`, `sysadmin-
  console/04`) adds a service registration to `Program.cs` in Wave 1; in Wave 3, `accounts-identity/08`,
  `accounts-identity/09`, `control-plane/03`, and `sysadmin-console/06` all touch it too. The rule:
  stories touching `Program.cs` merge one at a time, small rebased PRs, never batched - even when
  everything else about them is otherwise parallel-safe. Story 01's Wave-1 `Program.cs` edit (wiring
  `IRuntimeSettingsService`'s config-presence split plus the `IOperatorActionLog` seam registration) and
  story 03's Wave-3 edit (rate-limiter factories) must each be scheduled with this in mind relative to
  the OTHER stories outside this feature touching `Program.cs` in the same wave.
- **(b) `api/src/Entitlements/` hazard, CORRECTED 2026-07-08: it is `accounts-identity/05`, not `06`.**
  The prior version of this table named `accounts-identity/06` (Decision F wiring) as the story
  `control-plane/02` must serialize with on `StoredValueEntitlementService.cs`. That was stale: `06` only
  edits the `CreateRoom` call site in `GameHub.cs` (the `EvaluateForSession(purchaserIdentity, ...)`
  signature already accepts the argument) and never touches `api/src/Entitlements/`. The real chain is
  **`accounts-identity/05` (Wave 1, re-keys the class onto `AccountId`) -> `control-plane/02` (Wave 2,
  adds the system-flag filter step)** - a hard depends-on, reflected in the Wave Plan table above.
  `billing-entitlements/08` (grant metadata + resync) also co-occupies `api/src/Entitlements/` in Wave 2
  (it edits `EntitlementGrant.cs` + the grant store) - sequence its record-shape change relative to
  story 02's edit, though neither is a hard code dependency on the other.
- **(c) `api/src/PublishedTales/` hazard, NEW 2026-07-08: shared with `keepsake-vault/04` in Wave 3.**
  `keepsake-vault/04` changes `ConfirmHiddenAsync` to a soft-delete; `control-plane/03` migrates
  `AutoHideThreshold`/`TaleTtl` onto settings keys and may touch `ReportedTalesController.cs`, the
  caller. Decide and record ordering between the two before either starts building - see story 03's
  Technical Notes.

## Per-story tech notes

### 01 - The runtime settings service
**Approach:** generalize `TableStorageActiveStripeModeStore`'s single-fixed-row shape into one row per
settings key in a new `RuntimeSettings` table (same `Entitlements:StorageConnectionString` account, no
new resource), with `ActiveStripeContext`'s short cache precedent covering the read path. **Exports:**
`IRuntimeSettingsService` (the typed `GetBoolAsync`/`GetIntAsync`/`GetDecimalAsync`/`GetStringAsync`/
`GetAllAsync` surface every later story - inside and outside this feature - injects), `SettingsCatalog`
(the static, append-only key registry, now carrying `Bounds` and `RequiresConfirmation` per definition),
and the `IOperatorActionLog` seam (a narrow interface plus a no-op/in-memory implementation, superseded
in-place once `sysadmin-console/06` merges its Table Storage-backed store). **Gotcha (revised
2026-07-08):** keep the admin endpoints' authorization identical to `AdminEntitlementsController`'s
existing pattern (`[Authorize(Policy = OperatorSession.PolicyName)]`) - do not invent a new admin auth
scheme for this one surface; scoping (support/content/ops) is explicitly `sysadmin-console`'s later
work, not this story's. Two NEW gotchas from the adversarial review: (1) a type-parse-only PUT check is
not enough - every numeric key needs a `Bounds` check and every `*.enabled`/spend-ceiling key needs
`RequiresConfirmation`, both enforced BEFORE the write, not after; (2) the action-log write (AC-09) must
happen inside the same PUT/DELETE handler, immediately after a successful write and never on a rejected
one - do not defer it to `sysadmin-console` as "someone else's problem," even though the STORE itself is
`06`'s to build.

### 02 - Capability scopes: system flags in the entitlement evaluation
**Approach:** insert one composition step into `StoredValueEntitlementService.EvaluateForSession`, AFTER
the existing baseline+grant composition completes unchanged and BEFORE the final `SessionEntitlements`
construction, that force-removes any capability whose owning system flag reads `false` (settings value
AND the corresponding field of the injected `SystemConfigPresence`). This is a POST-COMPOSE FILTER, not
an evaluate-system-first branch - precedence ("system wins") and implementation ("filter after, not
branch before") are deliberately stated as two different things in the story so a builder does not reach
for an unnecessary early-branch structure (see the story's Context). Only `ai.enabled` ->
`EntitlementCatalog.AiOnDemand` has a live consumer in this story; `publishing.enabled` /
`email.enabled` are registered and effective-value-correct but unconsumed until a future capability
references them. **Exports:** nothing new for other stories to import - this is an internal composition
change behind the unchanged `IEntitlementService` contract. **Gotchas (revised 2026-07-08):** (1) the
three config-presence checks this story needs are NOT reusable booleans today - they are inline
`string.IsNullOrWhiteSpace(...)` conditions at `Program.cs` ~163 (AI) and ~436 (publishing), and
`emailOptions.IsConfigured` at ~688 (email); extracting these into one `SystemConfigPresence` value is
real work in this story, not a wiring line; (2) `StoredValueEntitlementService`'s constructor gains
`IRuntimeSettingsService` and `SystemConfigPresence` as new parameters, which means
`StoredValueEntitlementServiceTests`' existing fixtures need updating too - budget for that, not just a
new test file; (3) the ONLY files this story touches are inside `api/src/Entitlements/` (plus the
`Program.cs` extraction) - resist the temptation to also wire `PublishedTalesController` or
`IEmailSender` here; that is deliberately Out of Scope; (4) this story hard-depends on
`accounts-identity/05` landing first (it re-keys the exact class this story edits) - it does NOT
serialize with `accounts-identity/06` (that story never touches `api/src/Entitlements/` - the earlier
hazard note naming `06` was stale, see hazard (b) above).

### 03 - Knob migration
**Approach:** for each of the seven named knobs, keep the existing hardcoded value as the settings key's
code default and change the READ SITE to ask `IRuntimeSettingsService` for the current effective value,
rather than trusting a value captured once at DI-construction time (`AiQuota`, `AiSpendBreaker`,
`SeatGraceService` all currently capture their tunable once in a constructor - this is the real work of
the story, not just adding catalog entries). Rate-limiter policies (`Program.cs`) read the current value
inside their partition-creation factory lambda, resolving `IRuntimeSettingsService` from
`HttpContext.RequestServices` rather than a value closed over at `AddRateLimiter` registration time.
**Exports:** nothing new for other stories - this is a pure migration of value SOURCE, not a new
capability. **Gotchas (revised 2026-07-08):** (1) every rate-limit-permit knob's read site must CLAMP
into `[1, sane-max]` (e.g. `Math.Clamp`) immediately before constructing `FixedWindowRateLimiterOptions`,
independent of story 01's catalog `Bounds` check - `PermitLimit <= 0` throws inside the factory lambda,
which the rate-limiter middleware turns into a 500 on every request against that partition, not a
graceful degrade; (2) `api/src/PublishedTales/` is shared with `keepsake-vault/04` in Wave 3 (that story
changes `ConfirmHiddenAsync` to a soft-delete) - decide ordering between the two before either starts,
see hazard (c) above; (3) this is explicitly the widest-footprint story in the whole feature and, per
ADR 0003, must be scheduled ALONE in its wave slot - not merely within `control-plane`, but relative to
every other ADR 0003 feature touching `Program.cs` (a four-story Wave-3 hotspot, see hazard (a)),
`api/src/Rooms/`, `api/src/PublishedTales/`, `api/src/Ai/`, or `api/src/Admin/` at the same time. Do not
batch it with anything, even something that looks file-disjoint on paper.

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
- **No console UI, no RBAC scopes.** Both are explicitly `sysadmin-console`'s later work (parked in
  `feature.md`); this feature's job is the mechanism and its admin API only. The ACTION LOG is a
  different story: this feature is a REQUIRED WRITER of it (story 01, AC-09) from day one - only the
  log's storage/retention/console-view mechanics belong to `sysadmin-console/06` (revised 2026-07-08;
  do not read the older "no action log" framing as license to skip the write).
- **The control plane cannot disable its own safety rails (2026-07-08 adversarial-review finding, binding
  on stories 01 and 03).** Every numeric setting has a min/max `Bounds` enforced on PUT - a type-parse
  alone is not sufficient. Rate-limit permits are additionally clamped to a sane floor/ceiling at the
  read site (story 03) so a bad value can neither disable nor zero a limiter even if it somehow reaches
  the read path. The AI monthly spend ceiling and the `*.enabled` kill switches are bounded AND
  confirmation-gated (`RequiresConfirmation`) - never freely settable to an arbitrary value by a single
  PUT.
- **No i18n** (plain strings). **Never em dashes** in any file this feature touches or creates.
