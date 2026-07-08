<!--
  Story: control-plane/01. Foundation story for the feature - see feature.md and
  docs/adr/0003-admin-platform-and-family-accounts.md, Layer 1.
  Use hyphens/colons/parentheses, never em dashes.
-->

# Story: The runtime settings service

**Feature:** Control Plane (`docs/features/control-plane/feature.md`)  ·  **Status:** Not Started  ·  **Issue:** #TBD

## Context
Today every operational knob is one of three ad-hoc mechanisms (ADR 0003 Context): a startup
config-presence switch (redeploy to change), a hardcoded constant (report auto-hide = 3, AI per-IP =
30/min, seat grace, tale TTL = 30 days), or the one bespoke persisted flag done right exactly once - the
Stripe live/test mode store (`api/src/Billing/TableStorageActiveStripeModeStore.cs` +
`IActiveStripeContext`). Each new feature that wants a retunable knob invents its own version of that
last mechanism; that is the piecemeal engine ADR 0003 calls out. This story builds the ONE mechanism,
generalized from the Stripe-mode precedent, so story 02 (system flags) and story 03 (knob migration) -
and every future knob - have a single home instead of a fourth bespoke pattern. See `feature.md`'s
Design notes for the config-presence-vs-settings razor this story enforces.

## Acceptance Criteria
- [ ] AC-01: Given the settings catalog declares a key with a type and a code default, when no override
      has ever been written for that key, then reading it returns the code default (the same
      "safe/known default when nothing is stored" posture as the Stripe-mode precedent).
- [ ] AC-02: Given an operator PUTs a value for a settings key via the admin endpoint, when any caller
      reads that key afterward, then it returns the override value once the short read cache
      (a few seconds, mirroring `ActiveStripeContext`'s `CacheTtl`) has elapsed - immediately on the
      node that wrote it, within the cache window everywhere else.
- [ ] AC-03: Given an override is written (PUT) or cleared (DELETE), when that override is read back
      (individually or via the GET-all endpoint), then it carries a `changedBy` (the operator's
      identity, from the existing operator session credential) and a `changedAt` (UTC timestamp) stamp;
      a key with no override present shows no stamp. This row-level stamp is a display convenience,
      NOT the audit trail (see AC-08 - the two are different mechanisms with different failure modes:
      the stamp is overwritable by the next PUT, the log row is append-only history).
- [ ] AC-04: Given an operator DELETEs an override for a key, when the key is next read (after the
      cache window), then it returns to the code default, and the GET-all response no longer lists an
      override for that key.
- [ ] AC-05: Given no storage connection string is configured (local dev, CI, a fresh clone), when the
      app starts, then the settings service runs on a working in-memory store (not a no-op) - AC-01
      through AC-04 all still hold; only durability across a process restart is lost, mirroring the
      `InMemoryActiveStripeModeStore` / `InMemoryEntitlementGrantStore` config-presence split (no new
      Azure resource: it reuses the same `Entitlements:StorageConnectionString` the entitlement grant
      store and the Stripe-mode store already use).
- [ ] AC-06: Given a caller without a valid operator session credential, when they call GET, PUT, or
      DELETE on any settings endpoint, then they receive 401/403 - every settings endpoint in this
      story is operator-only (mirrors `AdminEntitlementsController`'s `[Authorize(Policy =
      OperatorSession.PolicyName)]` boundary; there is no anonymous or player-facing read).
- [ ] AC-07: Given a stored override row is missing, or a stored value fails to parse against its
      declared type (schema drift, a hand-edited row), when the service reads that key, then it
      degrades to the code default rather than throwing - the same "a storage hiccup never crashes the
      app" posture as `TableStorageActiveStripeModeStore.GetAsync`'s 404-and-unparseable handling.
- [ ] AC-08 (bounds, not just type - closes the adversarial-review finding): Given a `SettingDefinition`
      declares a numeric `Min`/`Max` (see Technical Notes' `Bounds`), when a PUT value type-parses
      correctly but falls outside `[Min, Max]`, then the endpoint rejects it with 400 and writes NO
      override - a type-only check is not sufficient. Boolean kill switches use the separate
      `RequiresConfirmation` gate (AC-10), not a numeric bound. Concretely: an operator cannot
      set `ai.spend.monthlyCeilingUsd` above its declared `Max` (uncapping AI spend), cannot set
      `tales.ttlDays` to `0` or below its declared `Min` (mass-expiring the vault/tale store), and
      cannot set a rate-limit-permit key (e.g. `admin.operatorLogin.rateLimitPermitPerMinute`, and any
      key story 03 migrates) to a value that would defeat the limiter (an absurdly large permit count) -
      each such key's `Max` is chosen to keep the limiter meaningful, not merely non-crashing.
- [ ] AC-09 (every settings change is logged NOW, not deferred): Given a successful PUT (write or
      change an override) or DELETE (clear an override), when the call completes, then the service
      appends exactly one row to the operator action log via `IOperatorActionLog` (the seam
      `sysadmin-console/06` owns), recording the operator, the action (`settings.put` /
      `settings.delete`), the target (the settings key), and a note carrying the old and new value (or
      `"reverted to default"` for a DELETE) - a failed PUT (rejected by AC-08's bounds check, or a
      malformed value) writes NO row, mirroring `sysadmin-console/06`'s AC-05 "only completed, effectful
      actions are logged." This requirement is THIS story's (it resolves the ADR-flagged contradiction
      between this feature and Amendment 2); the STORE is `sysadmin-console/06`'s. Wire this through a
      thin internal seam (see Technical Notes) so building this story does not hard-block on
      `sysadmin-console/06` landing first.
- [ ] AC-10 (a flipped kill switch or ceiling change is confirmation-gated): Given a PUT targets a key
      marked `RequiresConfirmation` in its `SettingDefinition` (the `*.enabled` system flags this
      feature and story 02 register, and `ai.spend.monthlyCeilingUsd`), when the request body omits an
      explicit `confirm: true` field, then the endpoint rejects it with 400 and writes no override and
      no log row; when `confirm: true` is present AND the value passes AC-08's bounds check, the write
      proceeds normally (AC-02/AC-09). This is a request-shape gate (an explicit, deliberate flag on the
      same call), not a second approval workflow or a second operator - it exists so a flip of one of
      these load-bearing switches can never be an accidental one-field PUT.

## Cross-feature dependency note
This story's action-log write (AC-09) depends on `IOperatorActionLog`, the seam
`sysadmin-console/06` defines and backs with `TableStorageOperatorActionLog`. Per that story's own
Technical Notes, the WRITE seam has no technical dependency on `sysadmin-console/05`'s shell and can
land independently of the console UI. This story does NOT hard-depend on `sysadmin-console/06`
merging first: register a local `IOperatorActionLog` seam (an interface this story defines or a
narrow reference to the one `06` defines, whichever lands first) with a no-op/in-memory
implementation until `06` lands, then swap in the real store with zero call-site change - see
Technical Notes. If `06` lands first, this story simply depends on and calls its concrete
`IOperatorActionLog`.

## Out of Scope
- The settings console PAGE (an Operations tab list + edit affordance) - that is `sysadmin-console`'s
  job, consuming this story's endpoints. This story ships the endpoints only.
- Scoped operator authorization (a `support`/`content`/`ops` scope model). These endpoints sit behind
  the existing single `OperatorSession.PolicyName` policy, same as every other admin endpoint today;
  introducing scopes is `sysadmin-console`'s Layer 3 work.
- Per-session or per-account setting values, scheduled/timed flips, and A/B or percentage rollouts (all
  parked in `feature.md`).
- Seeding the actual business keys this feature will eventually carry (`publishing.enabled`,
  `ai.quota.perSession`, etc.) - those are story 02's and story 03's job. This story may register one or
  two scaffolding/example keys purely to prove the mechanism end to end, but owns no production knob.
- The action log's STORE and STORAGE mechanics (`IOperatorActionLog`'s Table Storage implementation,
  its retention cap, and its console view) - that is `sysadmin-console/06`'s job. What IS in scope for
  this story is the REQUIREMENT to call that seam on every successful settings change (AC-09) - this
  story does not build the log, but it is one of the log's writers, on day one, same as the other four
  money/moderation call sites `sysadmin-console/06` names. The row-level `changedBy`/`changedAt` stamp
  (AC-03) remains a separate, overwritable display convenience on the settings row itself - it is not a
  substitute for the append-only log entry.
- A second approval workflow, a distinct approver identity, or any multi-operator sign-off for
  confirmation-gated keys (AC-10). Confirmation is a same-request, same-operator explicit flag, not a
  maker-checker process - this feature has exactly one operator identity today (ADR 0003 Layer 3 notes
  RBAC as later work).

## Technical Notes
New folder `api/src/Settings/` (mirrors the shape of `api/src/Billing/` and `api/src/Entitlements/`):

- **`SettingType`** - an enum: `Bool`, `Int`, `Decimal`, `String`. Every settings key declares exactly
  one type; a typed getter that does not match a key's declared type is a coding bug (assert/throw), not
  a runtime branch.
- **`SettingDefinition`** - a record: `Key` (string, dotted namespacing like `EntitlementCatalog`'s
  capability keys, e.g. `moderation.tale.autoHideThreshold`), `Type` (`SettingType`), `CodeDefault`
  (the typed default value), `Description` (a short operator-facing string), plus two new fields that
  close the adversarial-review gap:
  - `Bounds` (nullable, numeric keys only: `Min`/`Max` of the same underlying type as `Int`/`Decimal`
    keys) - `null` for `Bool`/`String` keys that have no natural range. Every numeric key story 02 or
    story 03 registers MUST supply a `Bounds` that keeps the knob meaningful (e.g.
    `ai.spend.monthlyCeilingUsd` gets a `Max` an operator cannot exceed to uncap spend; `tales.ttlDays`
    gets a `Min` of `1` so it can never mass-expire the store; a rate-limit-permit key gets a `Max` that
    keeps the limiter meaningful) - a definition that omits `Bounds` for a numeric key is a review
    blocker in story 02/03, not this story's job to police at runtime beyond enforcing whatever
    `Bounds` is declared.
  - `RequiresConfirmation` (bool, default `false`) - set `true` on `*.enabled` system-flag keys (story
    02) and on `ai.spend.monthlyCeilingUsd` (story 03). A confirmation-gated PUT (AC-10) is otherwise
    identical to any other PUT.
  Definitions live in a static `SettingsCatalog` list (mirrors `EntitlementCatalog`'s static-const-list
  shape) - story 02 and story 03 each APPEND to this list when they add their keys (same file, different
  waves; the feature's Wave Plan schedules them serially so this never conflicts).
- **`IRuntimeSettingsStore`** - the storage seam, generalizing `IActiveStripeModeStore`'s single-fixed-
  row shape into one row per key: `GetAllOverridesAsync()`, `GetOverrideAsync(key)`,
  `SetOverrideAsync(key, value, changedBy, changedAtUtc)`, `DeleteOverrideAsync(key, changedBy,
  changedAtUtc)`.
  - `TableStorageRuntimeSettingsStore` - mirrors `TableStorageActiveStripeModeStore` closely: one table
    (e.g. `RuntimeSettings`), `PartitionKey` a fixed constant (e.g. `"setting"`), `RowKey` the settings
    key itself (so a read is a point lookup, same as the Stripe-mode row); columns for the typed value
    (stored as its wire-string form plus the declared type, or one column per type - keep it simple:
    a single string column plus the catalog's own declared type is enough to parse it back), `ChangedBy`,
    `ChangedAtUtc`. Same `CreateIfNotExists`-once guard, same "404 -> no override, not an error" handling
    (AC-07). Uses `Azure.Data.Tables` - already a project dependency, no new NuGet.
  - `InMemoryRuntimeSettingsStore` - the config-presence "absent" half: a WORKING store (a concurrent
    dictionary), not a no-op, so every AC is exercisable with zero Azure setup (AC-05).
- **`IRuntimeSettingsService`** / **`RuntimeSettingsService`** - composes `SettingsCatalog` (defaults) +
  `IRuntimeSettingsStore` (overrides), with the SAME short in-memory cache precedent as
  `ActiveStripeContext` (cache the full resolved override set for a few seconds; a write-through reset on
  every PUT/DELETE so the flipping node sees its own change immediately, same as
  `ActiveStripeContext.SetModeAsync`). Typed getters: `GetBoolAsync(key)`, `GetIntAsync(key)`,
  `GetDecimalAsync(key)`, `GetStringAsync(key)`; a `GetAllAsync()` returning every catalog key with its
  type, description, code default, current override (if any) with its stamp, and the effective value -
  the shape the admin GET endpoint serializes directly.
- **`SettingsController`** (`api/src/Settings/SettingsController.cs` or `api/src/Admin/SettingsController.cs`
  - place it alongside `AdminEntitlementsController.cs` in `api/src/Admin/` since it is an admin-only
  surface, consistent with that controller's location): three actions, all
  `[Authorize(Policy = OperatorSession.PolicyName)]` (AC-06):
  - `GET /api/admin/settings` - the full catalog with defaults + overrides (AC-01/AC-03).
  - `PUT /api/admin/settings/{key}` - body `{ value, confirm? }`; validates in order: (1) key exists in
    the catalog, (2) value parses against the key's declared `SettingType`, (3) if `Bounds` is declared,
    value falls within `[Min, Max]` (AC-08), (4) if `RequiresConfirmation` is `true`, `confirm === true`
    is present (AC-10) - any failure is a 400 with no write and no log row. On success: writes the
    override, stamps `changedBy` from `User.Identity?.Name` (the operator email claim
    `OperatorAuthenticationHandler` already sets via `ClaimTypes.Name`) and `changedAt` from
    `DateTimeOffset.UtcNow` (AC-02/AC-03), then calls `IOperatorActionLog.AppendAsync` (AC-09) with
    action `settings.put`, target the key, and a note of `"{oldValue} -> {newValue}"`.
  - `DELETE /api/admin/settings/{key}` - clears the override, reverting to the code default (AC-04),
    then calls `IOperatorActionLog.AppendAsync` with action `settings.delete`, target the key, and a
    note of `"reverted to default"` (AC-09). A DELETE against a key with no existing override is a
    no-op (mirrors the log's own "no row on a no-op" rule) - no write, no log row.
- **The action-log seam (AC-09), buildable before `sysadmin-console/06` lands:** declare a narrow
  `IOperatorActionLog` interface (`AppendAsync(operatorEmail, action, target, note, ct)`) in this
  story's own `api/src/Settings/` folder (or reference `sysadmin-console/06`'s if it has already landed
  - same shape, one interface, no duplicate contract). Register a working no-op/in-memory
  implementation in `Program.cs` alongside this story's other wiring so `SettingsController` always has
  something to call; when `sysadmin-console/06` merges its `TableStorageOperatorActionLog`, the DI
  registration swaps to the real store with no change to `SettingsController`'s call sites. This keeps
  AC-09 satisfiable on this story's own schedule without a hard build-blocking dependency on `06`.
- **Program.cs wiring**: config-presence split on the SAME `Entitlements:StorageConnectionString` the
  entitlement grant store and the Stripe-mode store already read (no new connection string, no new
  Azure resource) - `TableStorageRuntimeSettingsStore` when present, `InMemoryRuntimeSettingsStore`
  otherwise; `IRuntimeSettingsService` registered as a singleton; the `IOperatorActionLog` seam
  registered per the bullet above. This is the story's one edit outside `api/src/Settings/` (and, if the
  controller lands in `api/src/Admin/`, that folder) - per ADR 0003's cross-feature note, `Program.cs`
  is a systemic hotspot; keep this a small, focused diff and expect to merge it serially against the
  other wave-1 stories touching the same file.

## Tests
| AC | Test |
|---|---|
| AC-01 | `tests/QuibbleStone.Api.Tests/Settings/RuntimeSettingsServiceTests.cs` - default read with no override |
| AC-02 | same file - override read after a PUT, asserting the cache-window behavior |
| AC-03 | same file / `SettingsControllerTests.cs` - changedBy/changedAt present on PUT and absent on a never-overridden key |
| AC-04 | same file - DELETE reverts to code default; GET-all omits the cleared override |
| AC-05 | same file, constructed over `InMemoryRuntimeSettingsStore` - identical assertions with no storage configured |
| AC-06 | `SettingsControllerTests.cs` - unauthenticated/non-operator caller gets 401/403 on all three verbs |
| AC-07 | `TableStorageRuntimeSettingsStoreTests.cs` - missing row and an unparseable stored value both degrade to the code default |
| AC-08 | `SettingsControllerTests.cs` - a value type-parses but falls outside `Bounds` on a scaffolding numeric key gets 400, writes no override |
| AC-09 | `SettingsControllerTests.cs` - a fake `IOperatorActionLog` captures exactly one row per successful PUT/DELETE; zero rows on a rejected PUT (AC-08/AC-10) |
| AC-10 | `SettingsControllerTests.cs` - a `RequiresConfirmation` key without `confirm: true` gets 400, no write, no log row; with `confirm: true` and an in-bounds value, the write and log proceed |

## Dependencies
none (foundation story for this feature) for build purposes; the settings mechanism itself has no
prerequisite. AC-09's action-log write depends on the `IOperatorActionLog` seam - see "Cross-feature
dependency note" above for why that is not a hard build-blocking dependency on `sysadmin-console/06`.
