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
      a key with no override present shows no stamp.
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
- The action log (ADR 0003 Decision 3) - that is `sysadmin-console`'s job; this story's PUT/DELETE
  responses carry `changedBy`/`changedAt` on the row itself (AC-03), which is sufficient for THIS
  feature's own correctness but is not an append-only audit trail.

## Technical Notes
New folder `api/src/Settings/` (mirrors the shape of `api/src/Billing/` and `api/src/Entitlements/`):

- **`SettingType`** - an enum: `Bool`, `Int`, `Decimal`, `String`. Every settings key declares exactly
  one type; a typed getter that does not match a key's declared type is a coding bug (assert/throw), not
  a runtime branch.
- **`SettingDefinition`** - a record: `Key` (string, dotted namespacing like `EntitlementCatalog`'s
  capability keys, e.g. `moderation.tale.autoHideThreshold`), `Type` (`SettingType`), `CodeDefault`
  (the typed default value), `Description` (a short operator-facing string). Definitions live in a
  static `SettingsCatalog` list (mirrors `EntitlementCatalog`'s static-const-list shape) - story 02 and
  story 03 each APPEND to this list when they add their keys (same file, different waves; the feature's
  Wave Plan schedules them serially so this never conflicts).
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
  - `PUT /api/admin/settings/{key}` - body `{ value }`; writes an override, stamping `changedBy` from
    `User.Identity?.Name` (the operator email claim `OperatorAuthenticationHandler` already sets via
    `ClaimTypes.Name`) and `changedAt` from `DateTimeOffset.UtcNow` (AC-02/AC-03). Rejects (400) a key
    not in the catalog or a value that fails to parse against the key's declared type.
  - `DELETE /api/admin/settings/{key}` - clears the override, reverting to the code default (AC-04).
- **Program.cs wiring**: config-presence split on the SAME `Entitlements:StorageConnectionString` the
  entitlement grant store and the Stripe-mode store already read (no new connection string, no new
  Azure resource) - `TableStorageRuntimeSettingsStore` when present, `InMemoryRuntimeSettingsStore`
  otherwise; `IRuntimeSettingsService` registered as a singleton. This is the story's one edit outside
  `api/src/Settings/` (and, if the controller lands in `api/src/Admin/`, that folder) - per ADR 0003's
  cross-feature note, `Program.cs` is a systemic hotspot; keep this a small, focused diff and expect to
  merge it serially against the other wave-1 stories touching the same file.

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

## Dependencies
none (foundation story for this feature).
