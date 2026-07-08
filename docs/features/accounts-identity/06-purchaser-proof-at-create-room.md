# Story: Purchaser proof at CreateRoom (ADR 0002 Decision F, finally wired)

**Feature:** Accounts & Identity  ·  **Status:** Not Started  ·  **Issue:** #TBD

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
- [ ] AC-02: Given that credential arrives on the connection, then the server
      validates it using the ALREADY-REGISTERED `PurchaserCredentialService.
      ResolvePurchaserEmail` (the same resolver billing-entitlements/05's
      restore endpoint already uses) - there is no second credential-
      verification implementation.
- [ ] AC-03: Given a validated purchaser identity for the connection, when
      `GameHub.CreateRoom` runs, then it passes that resolved identity to
      `IEntitlementService.EvaluateForSession(purchaserIdentity: ...)` in place
      of today's hardcoded `null` - so a purchaser holding an active family-plan
      grant (accounts-identity/05's re-keyed grant store, billing-entitlements/01)
      actually unlocks the room's session entitlements for the first time.
- [ ] AC-04 (the invariant, non-negotiable): Given the resolved purchaser
      identity, when `CreateRoom` finishes, then NO purchaser/account/email/
      device field is ever assigned to `Room` or to any `Player` record,
      returned in any DTO, broadcast to the room's group, or logged alongside a
      room/player identifier - the identity is read once to compute
      `SessionEntitlements` and discarded at that boundary, exactly as
      `Room.Entitlements` already enforces (accounts-identity/01 AC-02). A
      code-level check confirms `Room.cs` gains no new field from this story.
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
- **api (`api/src/Hubs/GameHub.cs`):** add a small connect-time resolution step,
  NOT a full ASP.NET Core authentication scheme/JWT bearer pipeline -
  `PurchaserCredentialService`'s credential is a Data-Protection payload, not a
  JWT, and ADR 0002 Decision F explicitly rejected the heavier alternative. In
  `OnConnectedAsync`, read the incoming access token from
  `Context.GetHttpContext()?.Request.Query["access_token"]` (SignalR's carrier
  for `accessTokenFactory` when the transport cannot use a header) and resolve
  it via the ALREADY-REGISTERED singleton `PurchaserCredentialService.
  ResolvePurchaserEmail(...)`. Stash the (possibly null) resolved identity in a
  small per-connection map (e.g. `ConcurrentDictionary<string, string?>` keyed
  by `Context.ConnectionId`), cleared in `OnDisconnectedAsync`; only
  `CreateRoom` reads it, and it never becomes a field on anything broadcast.
- **`GameHub.CreateRoom`:** replace the hardcoded `purchaserIdentity: null` with
  a read from that per-connection map, then call `_entitlements.
  EvaluateForSession(purchaserIdentity, Context.ConnectionAborted)` and
  `room.CaptureEntitlements(...)` exactly as today - no signature change to
  `IEntitlementService` or `Room`.
- **Reuse (do not re-implement):** `PurchaserCredentialService`
  (accounts-identity/03), `IEntitlementService`/`StoredValueEntitlementService`
  (billing-entitlements/01, accounts-identity/05's re-keyed grant store),
  `Room.CaptureEntitlements` (ai-cost-gate/02) - none of these are modified,
  only newly CALLED with a real, non-null value.
- **Files:** `api/src/Hubs/GameHub.cs` (connect-time resolve + the `CreateRoom`
  argument), `web/src/signalr/useGameHub.ts` (the `accessTokenFactory` option).
  No `api/src/Rooms/` field changes (AC-04 guard).
- **Cross-feature hazard (ADR 0003):** `control-plane/02` (capability scopes)
  also touches `api/src/Entitlements/` in the same cross-feature wave -
  serialize the two small PRs rather than landing them in parallel (see this
  feature's implementation.md).

## Tests
| AC | Test |
|---|---|
| AC-01 | `manual: sign in as a purchaser (accounts-identity/03 flow), create a room, and confirm (via browser devtools' WS/negotiate request) the access token is present on the hub connection.` |
| AC-02 | `tests/QuibbleStone.Api.Tests/GameHubEntitlementTests.cs (extended): a connection carrying a valid purchaser credential resolves to that purchaser's email inside CreateRoom.` |
| AC-03 | `tests/QuibbleStone.Api.Tests/GameHubEntitlementTests.cs (extended): a purchaser with an active EntitlementGrant sees that capability unlocked on Room.Entitlements after CreateRoom; the null-identity path (existing test) still passes.` |
| AC-04 | `manual: code-level check - Room.cs / RoomRegistry.cs gain no new field; grep the diff for any purchaser/account/email identifier reaching a DTO or a Clients.Group broadcast.` |
| AC-05 | `tests/QuibbleStone.Api.Tests/GameHubEntitlementTests.cs (existing, re-run as regression): CreateRoom with no access token still captures the default-unlocked baseline.` |
| AC-06 | `tests/QuibbleStone.Api.Tests/GameHubEntitlementTests.cs (new case): a malformed/expired/tampered token resolves to null identity (falls back to default-unlocked) rather than throwing or rejecting the connection.` |
| AC-07 | `manual: a 2-device group-play round with one purchaser-signed-in host - confirm no other player's client ever receives the credential or any purchaser-identifying field.` |

## Dependencies
- accounts-identity/05 (the re-keyed `IAccountStore`/grant store
  `EvaluateForSession` ultimately reads through).
- accounts-identity/03 (`PurchaserCredentialService`, already Complete).
- billing-entitlements/01 (`IEntitlementService`/`StoredValueEntitlementService`,
  already built).
