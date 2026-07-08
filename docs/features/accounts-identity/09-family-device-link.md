# Story: Family device link

**Feature:** Accounts & Identity  ·  **Status:** Not Started  ·  **Issue:** #TBD

## Context
[ADR 0003](../../adr/0003-admin-platform-and-family-accounts.md)'s "How a
child gets family entitlements" section is explicit: a room created from a
kid's OWN device only unlocks the family's capabilities if that device itself
can prove it belongs to the family - a signed-in parent's own phone already
can (accounts-identity/06), but a kid's tablet never signs in (kids stay
anonymous forever, README section 6). The family device link is the mechanism
that closes that gap: the parent generates a short-lived code from the Account
page, the kid's device redeems it once for a long-lived, individually
revocable token, and `CreateRoom` resolves that token to the family's
capabilities exactly like it resolves a signed-in purchaser. See
[feature.md](./feature.md) and accounts-identity/06 (the resolve-and-discard
pattern this story extends rather than forks).

**The explicit use case (ADR 0003, 2026-07-08):** a parent buys a kid an
add-on pack or capability and then does NOT hand-hold - the kid plays
independently on their own linked device, with no supervising adult present.
The link is set-and-forget: buy once, link once, and every later room the kid
creates carries the family's grants. Independent play opens a content-exposure
gap the ADR calls out explicitly: an unsupervised kid host could flip the
family-safe toggle off and reveal the teen-plus content tier. This story closes
that gap with the kid-device flag (AC-07) in the SAME change that ships the
link itself, so the two never ship separated.

## Acceptance Criteria
- [ ] AC-01: Given a signed-in family account (accounts-identity/07), when the
      account holder taps "Link a device" on the Account page, then the server
      mints a short-lived, human-enterable link code (the same style as a room
      join code - short, unambiguous alphabet) tied to that account's
      `AccountId` (accounts-identity/05), displayed for the parent to hand to
      the kid's device.
- [ ] AC-02: Given a kid's device (running QuibbleStone, never signed in), when
      it enters that link code within its short validity window, then the
      server verifies the code, mints a long-lived, opaque, single-device
      family-device token, and the device stores it. The code itself expires
      after a short window (minutes, mirroring a magic link's lifetime) if
      never redeemed, and is single-use once redeemed - it cannot mint a
      second device's token, whether reused immediately or after expiry.
- [ ] AC-03: Given a device holding a valid family-device token, when its
      SignalR hub connection is established and it creates a room, then
      `CreateRoom` resolves that token to the family's capabilities the SAME
      way accounts-identity/06 resolves a purchaser credential -
      `EvaluateForSession` is called with the family's resolved identity, the
      result is captured on `Room`, and the token/identity is discarded at that
      boundary (no new field on `Room`/`Player`).
- [ ] AC-04: Given the Account page, when the account holder views it, then it
      lists every linked device (a label - e.g. "Linked [date]" - and NOTHING
      device-identifying like an IP address or user agent) with a one-tap
      Revoke per device; revoking immediately invalidates that device's token
      (a room created from it after revocation falls back to the
      default-unlocked baseline, not an error).
- [ ] AC-05 (no PII): Given the family-device token, then it carries no PII
      beyond an opaque device-token id and the `AccountId` it resolves to - no
      kid nickname, birthdate, or any identifying field is ever collected or
      stored as part of linking a device.
- [ ] AC-06: Given a device with no family-device token and no signed-in
      purchaser session, when it creates or joins a room, then nothing changes
      - the default-unlocked baseline applies exactly as today.
- [ ] AC-07 (the kid-device flag - child safety, server-enforced): Given a
      linked device the parent has marked as a "kid device" (a toggle on that
      device's row in the Account page's linked-devices list - AC-04 - an
      attribute of the LINK, never of a player), when a room is created from
      that device, then `CreateRoom` captures a forced-family-safe state on
      `Room` (once, mirroring `Room.CaptureEntitlements`'s capture-once
      pattern), and EVERY round subsequently started in that room applies
      family-safe content REGARDLESS of whatever `familySafe` value the
      client submits to `StartRound` - a kid-flagged device can never reveal
      the teen-plus content tier, whether by a modified client, a stale
      toggle, or an unsupervised host. This is enforced server-side; a
      client-visible family-safe toggle, if shown at all for a session created
      this way, is informational/disabled, never the source of truth.

## Out of Scope
- Kid seat presets on a linked device beyond making the CREDENTIAL resolve
  (this story's AC-03 is the resolution half; the picker UI itself is
  accounts-identity/08's job, which this story only unblocks per its AC-05).
- Any device-management beyond list + revoke + the kid-device flag (renaming a
  device, per-device usage history/analytics).
- A general multi-device sync of anything OTHER than entitlement resolution -
  no shared game state, no cross-device rejoin, no shared gallery sync beyond
  what `keepsake-vault` separately builds.
- QR-code scanning - the code is short and human-enterable, matching the
  existing room-join-code pattern; a scannable QR is a nice-to-have parked for
  later, not required here.
- **Per-device capability scoping (PARKED - ADR 0003, recorded in
  feature.md's Parked section).** Letting a parent choose WHICH grants a
  linked device carries is explicitly rejected for now: the free tier is
  generous, packs apply family-wide at no extra cost, and the AI cost gate
  bounds spend per session/month regardless of who plays. A linked device
  always carries the WHOLE family grant set (AC-03), never a subset - only the
  family-safe state (AC-07) is device-scoped, because it is a content-safety
  concern, not an entitlement.

## Technical Notes
- **api:** extend `api/src/Accounts/` with a link-code minter (a NEW small
  short-code generator, matching the alphabet/shape convention `RoomRegistry`
  already uses for room codes - do not repurpose `IMagicLinkTokenService`,
  since a link code is short and human-typed while a magic-link token is a
  long signed string built for a different purpose) and a `FamilyDeviceToken`
  store (Table Storage: `PartitionKey = accountId`, `RowKey = deviceTokenId`;
  properties: created-at, revoked flag, and `IsKidDevice` - the kid-device flag
  (AC-07), defaulting to `false` for a newly redeemed device; the parent
  toggles it from the linked-devices list, a plain property update on this
  SAME row, not a new record). The device token itself is an opaque
  random string - deliberately NOT a JWT and NOT wrapped by
  `PurchaserCredentialService`'s Data Protection, because it must be revocable
  server-side BY ROW (a Data-Protection payload can only expire, never be
  individually revoked before its TTL).
- **Redeem endpoint:** `POST /api/accounts/devices/redeem { code }` -> resolves
  the code to an `AccountId`, mints + persists a new `FamilyDeviceToken` row,
  returns the raw token to the device. The device PERSISTS this token
  client-side (e.g. `localStorage`) - a DELIBERATELY less restrictive
  persistence than accounts-identity/03's in-memory-only `PurchaserSession`,
  because the whole point is surviving app restarts/reloads on a kid's own
  device; the mitigation for that relaxed persistence is exactly this story's
  per-device, individually-revocable shape (AC-04), unlike the purchaser
  session which relies on short TTL + in-memory-only for its safety margin.
- **List/revoke endpoints:** live behind the SAME `PurchaserCredentialService`-
  resolved auth accounts-identity/08's preset management uses - only the
  signed-in account holder can list/revoke their OWN account's devices.
- **`GameHub`'s connect-time resolver (accounts-identity/06):** extend the
  SAME per-connection resolution step story 06 built to also recognize a
  family-device token (in addition to a purchaser session credential) - both
  resolve to "an identity `EvaluateForSession` can use." Extend that one
  resolver rather than adding a second, parallel one inside `CreateRoom`.
- **`IEntitlementService.EvaluateForSession(purchaserIdentity)`:** the identity
  string passed for a family-device-token-resolved session is the family's
  resolved account identity (whatever `StoredValueEntitlementService`/
  `IAccountStore` already expect post-accounts-identity/05's re-keying, e.g.
  the account's email or `AccountId.ToString()`, chosen for consistency with
  story 06/05) - NOT a new identity type; the entitlement seam's public
  contract does not change.
- **The kid-device flag, AC-07 (child safety, server-enforced):** when
  `GameHub`'s connect-time resolver (extended above) resolves a family-device
  token whose stored row has `IsKidDevice = true`, `CreateRoom` captures a
  SMALL, separate boolean on `Room` - e.g. `Room.CaptureFamilySafeForced(bool)`,
  called once alongside (not folded into) `Room.CaptureEntitlements`, keeping
  content-safety state and capability state as two distinct capture-once
  fields rather than overloading `SessionEntitlements` with a non-capability
  concern. `GameHub.StartRound`'s EXISTING `familySafe` parameter (today
  host-supplied per round - `api/src/Hubs/GameHub.cs`, `StartRound`) must then
  be forced to `true` server-side whenever `room.FamilySafeForced` is set,
  IGNORING whatever value the client actually sent - this is the real
  enforcement point, since `familySafe` is chosen per round at `StartRound`,
  not fixed once at `CreateRoom`. The web client SHOULD also disable/hide its
  family-safe toggle when it knows the room came from a kid-flagged device (a
  UX nicety, not a security boundary) - the server check alone is
  authoritative and must hold even if the client is compromised, stale, or
  simply wrong. The flag lives on the device-link row, never on `Room` or any
  `Player` (only the single derived `FamilySafeForced` boolean crosses onto
  `Room`, exactly as only `SessionEntitlements` crosses today) - the no-PII/
  no-identity-on-Room invariant (accounts-identity/06 AC-04) is untouched.
- **web:** the Account page gains "Linked devices" (list + revoke) and "Link a
  device" (generates + displays the code). A NEW minimal redeem surface,
  reachable WITHOUT being signed in (a kid's device is never signed in) - a
  small route/screen where a code can be typed, calling the redeem endpoint
  and storing the returned token; reuse theme tokens + the existing `AppBar`,
  do not build a second visual language. `useGameHub.ts`'s
  `accessTokenFactory` (story 06) is extended to prefer a live purchaser
  session credential if signed in, else a stored family-device token, else
  supply nothing.
- **Files:** `api/src/Accounts/` (new `FamilyDeviceToken.cs`,
  `IFamilyDeviceTokenStore.cs`/implementations, the link-code minter),
  `api/src/Controllers/AccountsController.cs` (generate/redeem/list/revoke
  endpoints), `api/src/Hubs/GameHub.cs` (extend story 06's per-connection
  resolver), `web/src/pages/Account.tsx` (linked-devices UI), a new small web
  redeem screen/route, `web/src/signalr/useGameHub.ts` (the
  `accessTokenFactory` also considers a stored device token).

## Tests
| AC | Test |
|---|---|
| AC-01 | `tests/QuibbleStone.Api.Tests/Accounts/FamilyDeviceLinkTests.cs (new): generating a link code ties it to the correct AccountId and it is displayable.` |
| AC-02 | `tests/QuibbleStone.Api.Tests/Accounts/FamilyDeviceLinkTests.cs: redeeming a valid code once mints a token; redeeming the SAME code again fails (single-use); an unredeemed code past its expiry window fails to redeem.` |
| AC-03 | `tests/QuibbleStone.Api.Tests/GameHubEntitlementTests.cs (extended): a connection carrying a valid family-device token resolves to that family's entitlements on CreateRoom, discarding the token at the boundary (no Room/Player field).` |
| AC-04 | `manual: link two devices from the Account page, confirm both are listed with a non-identifying label; revoke one and confirm a subsequent CreateRoom from that device falls back to default-unlocked.` |
| AC-05 | `manual: inspect the FamilyDeviceToken table schema - confirm only AccountId + a token id + created-at/revoked/IsKidDevice, no PII.` |
| AC-06 | `manual/Playwright (tests/*.spec.ts, not in CI): a fresh device with no session and no device token plays a full free round with the default-unlocked baseline, unaffected by this story.` |
| AC-07 | `tests/QuibbleStone.Api.Tests/GameHubEntitlementTests.cs (extended): CreateRoom from a token with IsKidDevice=true captures FamilySafeForced; a subsequent StartRound called with familySafe=false still applies family-safe content. Plus manual: attempt the same via a modified/raw hub call (bypassing any client-side disabled toggle) to confirm the server, not the client, is the boundary.` |

## Dependencies
- accounts-identity/06 (the `CreateRoom`-resolves-and-discards wiring this
  story extends rather than forks).
- accounts-identity/07 (the free family account a device links to).
- accounts-identity/05 (the `AccountId` spine the token resolves to).
- child-safety (the family-safe content selector, `FamilySafeContentSelector`,
  this story's AC-07 forces on rather than reimplements).
