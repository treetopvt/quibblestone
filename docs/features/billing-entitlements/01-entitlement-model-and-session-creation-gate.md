# Story: Entitlement model + session-creation gate

**Feature:** Billing & Entitlements  ·  **Status:** Not Started  ·  **Issue:** #70

## Context
This was originally scoped as THE seam - a single service/hook answering "is
capability X unlocked for this session?" - but that seam is **already shipped**.
`ai-cost-gate/02` (#121, PR #132) landed `api/src/Entitlements/IEntitlementService.cs`:
the exact `ValueTask<SessionEntitlements> EvaluateForSession(string? purchaserIdentity
= null, ...)` contract, the immutable `SessionEntitlements` capability set, a thin
`EntitlementCatalog` (today reserving only `ai.onDemand`), and a default-unlocked
stand-in (`DefaultUnlockedEntitlementService`). `GameHub.CreateRoom` already calls it
exactly once and captures the result on the room via `Room.CaptureEntitlements(...)`,
which stores **only** the capability set - never a purchaser id (the anonymity
firewall ADR 0002 names as load-bearing). `EntitlementServiceTests` and
`GameHubEntitlementTests` already cover this thin slice.

**What this story now builds - precisely:** (a) replace `DefaultUnlockedEntitlementService`
with the real, stored-value evaluation behind the SAME interface (no consumer/
`GameHub.CreateRoom` change); (b) extend `EntitlementCatalog` to the full catalog -
`library.full`, `play.remote`, `play.largeGroup`, an open-ended `pack.<id>` family,
alongside the already-reserved `ai.*` keys; (c) add the grant store - lease-shaped
`EntitlementGrant` rows (`validThrough` + `source`: subscription / one-time / operator,
ADR 0002 Decision C) in Azure Table Storage, partitioned by purchaser identity for a
single-lookup read; (d) resolve "is there an entitled purchaser" from
accounts-identity's seam (its `IAccountStore`, once accounts-identity/02 lands). This
is a "consume and extend the shipped seam" story, not a "build the seam" story. See
[feature.md](./feature.md) and [ADR 0002](../../adr/0002-accounts-subscriptions-and-admin.md)'s
"State of the tree."

## Acceptance Criteria
- [ ] AC-01: Given the capability-key catalog is extended beyond the already-shipped
      `ai.onDemand` reservation, when it is inspected, then it additionally contains
      at minimum `library.full`, `play.remote`, `play.largeGroup`, and an open-ended
      `pack.<id>` family - still one string-keyed `EntitlementCatalog`, not one-off
      booleans scattered per feature.
- [ ] AC-02: Given `IEntitlementService`, `SessionEntitlements`, and the
      `GameHub.CreateRoom` call site already ship unchanged (ai-cost-gate/02, #121),
      when `DefaultUnlockedEntitlementService` is replaced with the real stored-value
      evaluation, then no change is required to `IEntitlementService`'s public shape,
      `Room.cs`, or any hub method signature - the DI registration swap in
      `Program.cs` is the only integration point touched.
- [ ] AC-03: Given a session with no resolved purchaser identity (anonymous, the
      alpha norm) or a resolved purchaser with no matching grant, when the stored-value
      evaluation runs, then it returns the same default-unlocked set
      `DefaultUnlockedEntitlementService` returns today - zero behavior regression,
      verified against the existing `EntitlementServiceTests` +
      `GameHubEntitlementTests` (extended, not rewritten).
- [ ] AC-04: Given a purchaser has an `EntitlementGrant` row whose `validThrough` is
      null (permanent - e.g. a one-time pack) or in the future, when
      `EvaluateForSession(purchaserIdentity)` runs for that purchaser, then the
      granted capability key(s) are unlocked for the session; given `validThrough`
      has passed, then that grant's capability reads as locked (every other key
      still falls back to the default-unlocked baseline).
- [ ] AC-05: Given an entitlement is granted (stories 03-04, or a future operator
      grant/revoke), when it is persisted, then it is stored as a lease-shaped
      `EntitlementGrant` row (capability key, `validThrough`, `source`: subscription |
      one-time | operator - ADR 0002 Decision C) in Azure Table Storage, partitioned
      by a hash of purchaser identity so the session-creation check resolves ALL of a
      purchaser's grants in a single partition read.
- [ ] AC-06: Given the stored-value evaluation needs "is there an entitled purchaser
      behind this session," then it resolves that from accounts-identity/02's
      `IAccountStore`-backed identity (the value `GameHub.CreateRoom` passes as
      `purchaserIdentity`, resolved from the host's signed-in session per ADR 0002
      Decision F) - it does not duplicate identity or token-verification logic.
- [ ] AC-07: Given a future feature wants to gate a new capability, when it is added,
      then doing so still requires only (a) a catalog key and (b) a grant row
      carrying it - no change to `IEntitlementService`'s signature, `Room.cs` /
      `RoomRegistry.cs`, or any hub method (re-affirms the extensibility promise now
      that the real implementation, not just the stand-in, is in place).

## Out of Scope
- Rebuilding `IEntitlementService`, `SessionEntitlements`, `EntitlementCatalog.AiOnDemand`
  / `AiCapabilities`, or the `GameHub.CreateRoom` call site - ALREADY SHIPPED
  (ai-cost-gate/02, #121, PR #132); this story edits/extends the same file, it does
  not recreate any of it.
- Actually flipping any capability from unlocked-by-default to entitlement-required
  as a live product decision - default-unlocked remains the shipped baseline for
  every key with no grant; this story only makes a real per-key, per-purchaser
  override POSSIBLE via a stored grant. Turning a specific key entitlement-required
  in practice is a later, explicit decision recorded in feature.md's Decisions log.
- The subscription webhook lifecycle (created / renewal / past_due-grace /
  canceled) and the family-plan capability-bundle mapping - those land in stories
  03-04 (ADR 0002 Decisions C/D); this story only defines the grant SHAPE
  (`validThrough` + `source`) those stories write into.
- The mechanics of proving purchaser status to the SignalR hub (ADR 0002 Decision F:
  the access-token-on-connect wiring) - that is accounts-identity's + `GameHub`'s
  connection-auth concern; this story only consumes an already-resolved purchaser
  identity string, the same optional parameter the shipped `EvaluateForSession`
  signature already accepts.
- Rate limiting / quota metering (e.g. "N AI generations per month") - unchanged; a
  session-creation gate answers unlocked/not-unlocked, not "how many are left."
- The tip jar, Stripe integration, gated purchase UI, and restore/manage view -
  stories 02-05 consume this seam; this story only extends the seam and the storage
  it reads from.

## Technical Notes
- **Edit, don't recreate.** `api/src/Entitlements/IEntitlementService.cs` already
  ships `IEntitlementService`, `SessionEntitlements`, `EntitlementCatalog` (today
  reserving only `AiOnDemand` / `AiCapabilities`), and `DefaultUnlockedEntitlementService`
  (ai-cost-gate/02, #121, PR #132). This story extends `EntitlementCatalog` with the
  full set (`library.full`, `play.remote`, `play.largeGroup`, the `pack.<id>` family)
  alongside the existing `ai.*` reservation, and adds a new `EntitlementGrant` record
  + a small grant-store type (e.g. `IEntitlementGrantStore` / a Table-Storage-backed
  implementation) in the SAME folder.
- Replace the DI registration
  `builder.Services.AddSingleton<IEntitlementService, DefaultUnlockedEntitlementService>();`
  (`api/src/Program.cs`) with the new stored-value implementation, registered against
  the SAME `IEntitlementService` interface - a one-line swap, not a new call site.
  `GameHub.CreateRoom`'s call to `EvaluateForSession` is untouched.
- **Regression safety:** have the new stored-value implementation COMPOSE
  `DefaultUnlockedEntitlementService`'s default-unlocked set as its baseline (rather
  than re-deriving it), so "no purchaser / no grant" is guaranteed identical to
  today's shipped behavior - never a re-implementation that could drift (AC-03).
- Table Storage: an `EntitlementGrant` row keyed by a hash of purchaser identity
  (mirrors accounts-identity/02's `AccountStore` keying pattern) for a
  single-partition read at session-creation (AC-05). Reuse the same storage account
  `infra/main.bicep`'s `storage` resource already provisions - no new resource.
- Purchaser identity: `GameHub.CreateRoom` today hardcodes `purchaserIdentity: null`
  (see the call site's ai-cost-gate/02 comment). Resolving a REAL identity there -
  reading it from the host's signed-in purchaser session via SignalR's
  `accessTokenFactory` (ADR 0002 Decision F) - is a small, additive edit to that one
  argument (not a signature or shape change) once accounts-identity's magic-link
  session/credential exists; it is not this story's job to build that token
  verification, only to consume whatever identity string arrives.
- Keep the public shape (`EvaluateForSession`, `SessionEntitlements`) exactly as
  shipped - this is the contract every future paid-feature story (and the
  sysadmin-console operator grant/revoke, #136) already imports.

## Tests
| AC | Test |
|---|---|
| AC-01 | `api/tests/Entitlements/EntitlementServiceTests.cs (extend the existing suite): EntitlementCatalog contains the full key set.` |
| AC-02 | `manual: code review confirming Program.cs's DI line is the only edit at the integration boundary - no diff in IEntitlementService.cs's public members, Room.cs, or GameHub.cs's CreateRoom signature.` |
| AC-03 | `api/tests/Entitlements/EntitlementServiceTests.cs + tests/QuibbleStone.Api.Tests/GameHubEntitlementTests.cs (existing, re-run as regression): no purchaser / no grant still returns the same default-unlocked set.` |
| AC-04 | `api/tests/Entitlements/EntitlementServiceTests.cs (new cases): a grant with a future validThrough unlocks its key; a grant with a past validThrough does not.` |
| AC-05 | `api/tests/Entitlements/... (integration-style, Table Storage emulator or fake): a grant round-trips with its capability key, validThrough, and source intact, resolved via one partition read.` |
| AC-06 | `api/tests/Entitlements/EntitlementServiceTests.cs: purchaser lookup delegates to accounts-identity/02's IAccountStore (mock/fake), no duplicate lookup logic.` |
| AC-07 | `manual: add a throwaway capability key + grant at review time - confirm no Room.cs/hub-signature diff is needed to wire it.` |

## Dependencies
- accounts-identity/02 (#68) - `IAccountStore` / the purchaser identity this story's
  grant lookup keys off; genuinely upstream and not yet built.
- ai-cost-gate/02 (#121, PR #132, shipped) - the `IEntitlementService` contract,
  `SessionEntitlements`, `EntitlementCatalog.AiOnDemand`,
  `DefaultUnlockedEntitlementService`, and the `GameHub.CreateRoom` call site this
  story extends rather than builds.
- infra (Table Storage already provisioned per README section 9).
