# Story: Purchaser proof at CreateRoom (ADR 0002 Decision F, finally wired)

**Feature:** Accounts & Identity  ·  **Status:** Not Started  ·  **Issue:** #210

## Context
[ADR 0002](../../adr/0002-accounts-subscriptions-and-admin.md) Decision F named
the mechanism for a purchaser to prove their status to `GameHub.CreateRoom`
(the SignalR hub's `accessTokenFactory`) back on 2026-07-03, but it was never
built: `GameHub.CreateRoom` still calls
`_entitlements.EvaluateForSession(purchaserIdentity: null, ...)` unconditionally
(`GameHub.cs:536`). That means a family-plan subscription cannot unlock a
session today no matter how the rest of billing-entitlements is built. ADR
0003 calls this out explicitly as still-unbuilt and puts it in Layer 0 - this
story wires it, finally, using plumbing that already exists
(`PurchaserCredentialService`, `IEntitlementService`). See
[feature.md](./feature.md) and ADR 0002's "the load-bearing invariant" section,
which this story's ACs are reviewed against.

## Acceptance Criteria
- [ ] AC-01: Given a signed-in purchaser (accounts-identity/03's `PurchaserSession`,
      held in-memory on their device) creates or rejoins a room, when the web
      client establishes its SignalR hub connection, then it supplies that
      EXISTING session credential to the hub via SignalR's standard
      `accessTokenFactory` option on `HubConnectionBuilder` - no new credential
      type is minted for this purpose.
- [ ] AC-02: Given that credential arrives on the connection, when
      `GameHub.OnConnectedAsync` runs (a NEW override this story adds - `GameHub`
      has no `OnConnectedAsync` today, only `OnDisconnectedAsync` at
      `GameHub.cs:~827`), then the server validates the token using the
      ALREADY-REGISTERED `PurchaserCredentialService.ResolvePurchaserEmail` (the
      same resolver billing-entitlements/05's restore endpoint already uses) and,
      if it resolves, IMMEDIATELY calls `IEntitlementService.EvaluateForSession`
      with that identity, right there in `OnConnectedAsync` - there is no second
      credential-verification implementation, and the identity string is never
      carried past this one call (see AC-04).
- [ ] AC-03: Given the `SessionEntitlements` capability set produced by that one
      `OnConnectedAsync`-time call, then it - and ONLY it, plus an `AdultUnlocked`
      boolean this story reserves a slot for and always sets `false` (story 09
      populates the real adult-signal logic; see accounts-identity/09) - is
      stored in a NEW per-connection singleton service keyed by
      `Context.ConnectionId`, cleared in `OnDisconnectedAsync`. When
      `GameHub.CreateRoom` runs, it reads that ALREADY-RESOLVED capability set
      from the singleton and calls `room.CaptureEntitlements(...)` with it
      directly - `CreateRoom` itself no longer calls `EvaluateForSession` - in
      place of today's hardcoded `EvaluateForSession(purchaserIdentity: null)`,
      so a purchaser holding an active family-plan grant (accounts-identity/05's
      re-keyed grant store, billing-entitlements/01) actually unlocks the room's
      session entitlements for the first time.
- [ ] AC-04 (the invariant, non-negotiable): Given the resolved purchaser
      identity, then the identity string itself (email or any purchaser/
      account/device identifier) is NEVER stored anywhere past the single
      `EvaluateForSession` call that consumes it in `OnConnectedAsync` - not on
      `Room`, not on `Player`, not in the new per-connection singleton, not in
      any DTO, broadcast, or log line alongside a room/player/connection
      identifier. A code-level check confirms (a) `Room.cs` gains no new field
      from this story, and (b) the per-connection singleton's value type carries
      no identity-shaped field at all - only a `SessionEntitlements` and a
      `bool`, exactly as `Room.Entitlements` already enforces the same discipline
      on `Room` (accounts-identity/01 AC-02).
- [ ] AC-05: Given a connection that supplies NO credential (every anonymous
      player, and a signed-out or never-signed-in host), when it connects and
      creates or joins a room, then it behaves exactly as today -
      `EvaluateForSession(null)` returns the default-unlocked baseline, with
      zero observable change to free play.
- [ ] AC-06: Given a credential that is malformed, expired, or tampered, when
      the hub attempts to resolve it, then the connection is treated as if no
      credential were supplied (falls back to the default-unlocked baseline)
      rather than rejecting the connection or throwing - a stale or corrupted
      purchaser token must never break a family's ability to play.
- [ ] AC-07 (no PII travels to other players): Given the purchaser credential
      is exchanged only between the purchaser's own device and the server, then
      it is never relayed to, or visible from, any other connection in the same
      room - no roster field, no broadcast payload references it, matching
      README section 6's minimal-PII-on-the-play-plane posture.
- [ ] AC-08 (no identity keyed by ConnectionId, alongside the roster): Given the
      new per-connection singleton, then at no point is an identity string
      (email, `AccountId`, device-token id) ever a value keyed by
      `Context.ConnectionId` - the singleton's map holds only the resolved
      capability set and the `AdultUnlocked` boolean, matching the same
      no-PII-alongside-the-roster shape `RoomRegistry` already holds for
      `Room`/`Player`.

## Out of Scope
- A new credential type or a REST session-mint endpoint - ADR 0002 explicitly
  set aside the "structurally-enforced REST mint-session" alternative as more
  moving parts than a solo alpha needs; this story reuses the EXISTING
  `PurchaserCredentialService` bearer exactly as accounts-identity/03 minted it.
- Any change to what a family-plan grant unlocks (billing-entitlements/01's
  capability catalog/bundle is untouched) - this story only wires the plumbing
  that carries an already-resolved identity into `CreateRoom`.
- Mid-session entitlement refresh - capture-once at `CreateRoom` stands (ADR
  0003 Decision 5); a grant/revoke takes effect at the NEXT room, never live.
- The family device link (accounts-identity/09) - a separate credential shape
  this story does not build, though 09 explicitly reuses the resolver this
  story creates rather than forking a second one.
- Kid seat presets (accounts-identity/08) - an unrelated surface.

## Technical Notes
- **web (`web/src/signalr/useGameHub.ts`):** the `HubConnectionBuilder()
  .withUrl(HUB_URL)` call gains an `accessTokenFactory` option reading the
  purchaser's live in-memory credential from accounts-identity/03's
  `usePurchaserSession()` (`web/src/account/PurchaserSession.tsx`). Pass a
  GETTER FUNCTION (`accessTokenFactory: () => session.credential ?? ''`), not a
  snapshot value - SignalR calls this before every (re)connection attempt, so a
  purchaser who signs in mid-app-life does not need to force a hub reconnect.
  Keep `useGameHub` itself generic (it must not become a purchaser-facing
  surface) - it only reads the session's current credential, it does not import
  or render any purchaser UI.
- **`GameHub` gains its FIRST `OnConnectedAsync` override.** `GameHub` has no
  `OnConnectedAsync` today - only `OnDisconnectedAsync` (`GameHub.cs:~827`).
  This story adds the override; it does not extend an existing one. Do not
  build a full ASP.NET Core authentication scheme/JWT bearer pipeline -
  `PurchaserCredentialService`'s credential is a Data-Protection payload, not a
  JWT, and ADR 0002 Decision F explicitly rejected the heavier alternative. In
  the new `OnConnectedAsync`, read the incoming access token from
  `Context.GetHttpContext()?.Request.Query["access_token"]` (SignalR's carrier
  for `accessTokenFactory` when the transport cannot use a header), resolve it
  via the ALREADY-REGISTERED singleton `PurchaserCredentialService.
  ResolvePurchaserEmail(...)`, and - if it resolves - call `_entitlements.
  EvaluateForSession(identity, Context.ConnectionAborted)` right there. The
  resolved identity string lives only in a local variable for the duration of
  that one call; it is discarded the moment `EvaluateForSession` returns.
- **The new per-connection singleton (cold-builder-critical - read this
  before sizing the story).** SignalR builds a FRESH `GameHub` instance per
  invocation (`Program.cs:~491`'s own comment: "every transient GameHub
  instance (SignalR builds a fresh hub per invocation) shares the SAME set of
  active rooms" - the same reasoning `RoomRegistry` is a singleton for). A hub
  INSTANCE field cannot bridge `OnConnectedAsync` and a later `CreateRoom`
  call, because they run on different `GameHub` instances. The per-connection
  map therefore MUST be a new singleton service (e.g.
  `IConnectionEntitlementStore` / `ConnectionEntitlementStore`, a
  `ConcurrentDictionary<string, ResolvedConnectionIdentity>` keyed by
  `Context.ConnectionId`, where `ResolvedConnectionIdentity` is
  `(SessionEntitlements Capabilities, bool AdultUnlocked)` - no identity-shaped
  field, per AC-04/AC-08), injected into `GameHub`'s constructor alongside
  `RoomRegistry` et al., and registered with `builder.Services.
  AddSingleton<...>()` in `Program.cs` next to `RoomRegistry`'s registration
  (`Program.cs:~496`). Cleared (removed by key) in `OnDisconnectedAsync`.
- **`GameHub.CreateRoom`:** replace the hardcoded
  `EvaluateForSession(purchaserIdentity: null)` call with a read of the
  ALREADY-COMPUTED capability set from the new singleton by
  `Context.ConnectionId` (a miss - no token supplied, or the connection never
  went through `OnConnectedAsync`'s resolve path - falls back to the
  default-unlocked baseline, matching AC-05), then `room.
  CaptureEntitlements(...)` with that value directly. `CreateRoom` itself no
  longer calls `EvaluateForSession` - that call now happens exactly once, in
  `OnConnectedAsync`. No signature change to `IEntitlementService` or `Room`.
- **Reuse (do not re-implement):** `PurchaserCredentialService`
  (accounts-identity/03), `IEntitlementService`/`StoredValueEntitlementService`
  (billing-entitlements/01, accounts-identity/05's re-keyed grant store),
  `Room.CaptureEntitlements` (ai-cost-gate/02) - none of these are modified,
  only newly CALLED with a real, non-null value (and now called from
  `OnConnectedAsync` instead of `CreateRoom`).
- **Test-fixture ripple (cold-builder-critical).** `GameHub`'s constructor
  gains a new dependency (the singleton above). Six existing hub test files
  construct `GameHub` directly with the current 11-argument constructor and
  must be extended to supply a twelfth argument: `GameHubEntitlementTests.cs`,
  `GameHubJoinTests.cs`, `GameHubStartRoundTests.cs`, `GameHubRejoinTests.cs`,
  `GameHubSubmitWordTests.cs`, `GameHubDisconnectTests.cs` - a fake/in-memory
  implementation of the new store is enough (the same pattern
  `DefaultUnlockedEntitlementService` already follows for
  `IEntitlementService` in these same files). Separately, exercising AC-02's
  `OnConnectedAsync` resolve path requires the incoming access token to be
  readable from `Context.GetHttpContext()?.Request.Query["access_token"]` -
  today's shared `FakeHubCallerContext` (defined once in
  `GameHubEntitlementTests.cs`, reused by the others) returns an EMPTY
  `FeatureCollection` for `Features`, so `Context.GetHttpContext()` resolves to
  `null` and cannot simulate a query-string access token. The test-extension
  plan for this story must add a fake `IHttpContextFeature` (wrapping a
  `DefaultHttpContext` whose `Request.QueryString`/`Query` carries the token)
  to `FakeHubCallerContext.Features` before any `OnConnectedAsync` test can
  supply a token - this is new test-fixture work, not a trivial constructor-arg
  addition. Flag this ripple to the testing-agent explicitly.
- **Files:** `api/src/Hubs/GameHub.cs` (the new `OnConnectedAsync` override +
  the `CreateRoom` read + the `OnDisconnectedAsync` cleanup), the new
  `api/src/Accounts/ConnectionEntitlementStore.cs` (or similar) + its
  interface, `api/src/Program.cs` (ONE new singleton registration - this story
  DOES touch `Program.cs`; a prior draft of this story said it did not, which
  was wrong), `web/src/signalr/useGameHub.ts` (the `accessTokenFactory`
  option). No `api/src/Rooms/` field changes (AC-04 guard).
- **Cross-feature hazard (ADR 0003, corrected 2026-07-08).** This story does
  NOT touch `api/src/Entitlements/` - `EvaluateForSession`'s signature already
  accepts a `purchaserIdentity` argument (no change needed there), so this
  story's only edits are the `GameHub.cs` call site, `useGameHub.ts`, and the
  new singleton's registration in `Program.cs`. The real cross-feature
  serialization concern is therefore `Program.cs` (ADR 0003 Wave 1's rule:
  every story touching `Program.cs` merges as a separate, small, serially
  rebased PR) - not `api/src/Entitlements/`, which is `accounts-identity/05`
  (Wave 1 re-key) -> `control-plane/02` (Wave 2 system-flag composition)'s
  hazard, a chain this story is not part of. See this feature's
  implementation.md and ADR 0003's "Cross-feature build order" table.

## Tests
| AC | Test |
|---|---|
| AC-01 | `manual: sign in as a purchaser (accounts-identity/03 flow), create a room, and confirm (via browser devtools' WS/negotiate request) the access token is present on the hub connection.` |
| AC-02 | `tests/QuibbleStone.Api.Tests/GameHubEntitlementTests.cs (extended, plus a fake IHttpContextFeature added to FakeHubCallerContext): a connection carrying a valid purchaser credential, when OnConnectedAsync runs, resolves the identity and calls EvaluateForSession exactly once.` |
| AC-03 | `tests/QuibbleStone.Api.Tests/GameHubEntitlementTests.cs (extended): a purchaser with an active EntitlementGrant sees the pre-resolved capability set on Room.Entitlements after CreateRoom (CreateRoom itself makes no EvaluateForSession call - verify via a spy/counting fake); the null-identity path (existing test) still passes and falls back to the default-unlocked baseline.` |
| AC-04 | `manual: code-level check - Room.cs gains no new field; the per-connection singleton's value type (ResolvedConnectionIdentity or similar) is inspected to confirm it holds only SessionEntitlements + a bool, no identity-shaped field; grep the diff for any purchaser/account/email identifier reaching a DTO or a Clients.Group broadcast.` |
| AC-05 | `tests/QuibbleStone.Api.Tests/GameHubEntitlementTests.cs (existing, re-run as regression): CreateRoom with no access token (no entry in the per-connection singleton) still captures the default-unlocked baseline.` |
| AC-06 | `tests/QuibbleStone.Api.Tests/GameHubEntitlementTests.cs (new case): a malformed/expired/tampered token resolves to null identity in OnConnectedAsync (falls back to default-unlocked, stored as such in the singleton) rather than throwing or rejecting the connection.` |
| AC-07 | `manual: a 2-device group-play round with one purchaser-signed-in host - confirm no other player's client ever receives the credential or any purchaser-identifying field.` |
| AC-08 | `manual: code read of the new per-connection singleton's value type and every write site - confirm no write ever stores an email/AccountId/device-token id keyed by ConnectionId.` |

## Dependencies
- accounts-identity/05 (the re-keyed `IAccountStore`/grant store
  `EvaluateForSession` ultimately reads through).
- accounts-identity/03 (`PurchaserCredentialService`, already Complete).
- billing-entitlements/01 (`IEntitlementService`/`StoredValueEntitlementService`,
  already built).
