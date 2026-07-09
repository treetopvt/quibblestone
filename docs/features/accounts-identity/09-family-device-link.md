# Story: Family device link

**Feature:** Accounts & Identity  ·  **Status:** In Review (PR open, held for owner sign-off - child-safety seam)  ·  **Issue:** #229

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
creates carries the family's grants.

**MAJOR REVISION (2026-07-08, post adversarial-review finding #1 - owner chose
option a).** The first draft of this story defended the wrong gate. It read
teen-plus content exposure as a problem to fix by flagging a KID's device
(`IsKidDevice`, default `false`) and forcing family-safe content on when that
flag was set - but the review found the teen-plus tier itself is gated ONLY by
the per-round `familySafe` flag the CLIENT sends at `StartRound`
(`TemplateCatalog.cs` / `FamilySafeContentSelector`) and by NO entitlement at
all, and the "lock" rode an optional, client-held, clearable device token: an
unsupervised kid reaches teen-plus for free simply by playing in a fresh or
incognito browser, where no token is presented, no flag is ever set, and
nothing forces anything. A lock that rides a credential its own holder can
shed is not enforcement. **The fix inverts the default posture entirely:
teen-plus content now requires an affirmative adult signal a token-less
session cannot obtain, and is family-safe by default otherwise.** This
supersedes the "force the flag on / override `StartRound`" mechanism described
above and in the earlier draft of AC-07 - see the rewritten AC-07 below, which
is now the load-bearing AC of this whole story.

## Acceptance Criteria
- [x] AC-01: Given a signed-in family account (accounts-identity/07), when the
      account holder taps "Link a device" on the Account page, then the server
      mints a short-lived, human-enterable link code tied to that account's
      `AccountId` (accounts-identity/05), displayed for the parent to hand to
      the kid's device. The code is drawn from a real entropy floor - a
      DISTINCT, larger alphabet/length than a room join code (a room code only
      needs to deter a handful of concurrent rooms from colliding; a link code
      guards actual entitlement access and must resist brute force even under
      its short validity window) - server-minted via a CSPRNG, never a
      predictable or low-entropy generator.
- [x] AC-02: Given a kid's device (running QuibbleStone, never signed in), when
      it enters that link code within its short validity window, then the
      server verifies the code, mints a long-lived, opaque, single-device
      family-device token, and the device stores it. The code itself expires
      after a short window (minutes, mirroring a magic link's lifetime) if
      never redeemed, and is single-use once redeemed - it cannot mint a
      second device's token, whether reused immediately or after expiry. The
      newly minted device row defaults to the SAFE state (AC-07's
      `IsAdultConfirmedDevice = false`) - a freshly linked device unlocks
      nothing beyond the family's paid capabilities until an adult explicitly
      opts it into teen-plus (AC-07).
- [x] AC-03: Given a device holding a valid family-device token, when its
      SignalR hub connection is established and it creates a room, then
      `CreateRoom` resolves that token to the family's capabilities the SAME
      way accounts-identity/06 resolves a purchaser credential -
      `EvaluateForSession` is called with the family's resolved identity, the
      result is captured on `Room`, and the token/identity is discarded at that
      boundary (no new field on `Room`/`Player`). This is orthogonal to AC-07:
      a family-device token always resolves the family's PAID capabilities
      (entitlements), independent of whether that same device also carries an
      adult-unlock signal for CONTENT safety - the two are separate axes
      captured together but never conflated.
- [x] AC-04: Given the Account page, when the account holder views it, then it
      lists every linked device with enough NON-PII context to make revocation
      an actionable decision - a short, random, non-identifying label (e.g. a
      two-word adjective-noun tag minted at redeem time, not a device
      fingerprint) and a relative last-seen time ("used 2 hours ago", "never
      used since linking") - and NOTHING device-identifying like an IP address
      or user agent - with a one-tap Revoke per device; revoking immediately
      invalidates that device's token (a room created from it after revocation
      falls back to the default-unlocked, family-safe baseline, not an error).
- [x] AC-05 (no PII, handled as a secret): Given the family-device token, then
      it carries no PII beyond an opaque device-token id and the `AccountId`
      it resolves to - no kid nickname, birthdate, or any identifying field is
      ever collected or stored as part of linking a device. The server stores
      only a HASH of the token value (never the raw secret), the same
      "handle as a secret" discipline the ADR's security posture applies to
      vault ids and claim codes - a stored-data leak cannot be replayed
      directly.
- [x] AC-06: Given a device with no family-device token and no signed-in
      purchaser session, when it creates or joins a room, then nothing changes
      - the default-unlocked-for-paid-capabilities, family-safe-for-content
      baseline applies exactly as today (and exactly as AC-07 restates as the
      content-safety default for every session with no adult signal, whether
      or not a device token is present).
- [x] AC-07 (REDESIGNED 2026-07-08 - the teen-plus gate, child safety,
      server-enforced, the load-bearing AC of this story): Given a room's
      session, then the teen-plus content tier is served ONLY when that
      session carries an explicit, affirmative ADULT-UNLOCK signal, resolved
      SERVER-SIDE at `CreateRoom` and captured onto `Room` exactly like an
      entitlement (capture-once, alongside `SessionEntitlements`) - NEVER
      inferred from, or gate-able by, any value the client submits at
      `StartRound`. The adult-unlock signal is `true` when, and ONLY when, the
      connection that created the room resolved to:
      (a) a signed-in family/purchaser session (accounts-identity/03's
          `PurchaserSession` - adult-by-construction, since only an adult
          completes a magic-link sign-in, ADR 0002 Decision A), OR
      (b) a family-device token whose row has `IsAdultConfirmedDevice = true`
          - an explicit toggle an adult flips from the Account page's
          linked-devices list (AC-04), defaulting to `false` on every newly
          redeemed device (AC-02).
      A room with NEITHER signal - anonymous play, a signed-out host, an
      incognito/private window, a cleared-storage device, or a linked device
      an adult never confirmed - is FAMILY-SAFE, full stop, REGARDLESS of
      whatever `familySafe` value the client sends to `StartRound`. Reaching
      teen-plus is an adult OPT-IN an adult performs from the Account page;
      it is never a toggle a kid can flip, skip, or bypass by clearing local
      state. Enforced entirely server-side: `GameHub.StartRound` computes the
      EFFECTIVE `familySafe` value passed into `FamilySafeContentSelector`/
      `TemplateCatalog` as `room.AdultUnlocked ? clientRequestedFamilySafe :
      true` - the client's own value is honored only once an adult has
      already unlocked the room, and is otherwise ignored outright, including
      via a modified/raw hub call that bypasses any client UI. A client-visible
      family-safe toggle, if shown at all for a session with no adult-unlock
      signal, is informational/disabled - never the source of truth.
- [x] AC-08 (host-migration cannot open the gate): Given a room whose captured
      `Room.AdultUnlocked` is `false` (the family-safe default), when host
      migration occurs (`EnsureHostLocked`/`PassHost` promotes a different
      player - possibly a kid - to host), then `Room.AdultUnlocked` is
      UNCHANGED - it is a property of the room's captured session from
      `CreateRoom`, never of whoever currently holds the host role, so
      promoting a new host never flips teen-plus on. A code-level check
      confirms no host-migration code path reads or writes
      `Room.AdultUnlocked`.

## Out of Scope
- Kid seat presets on a linked device beyond making the CREDENTIAL resolve
  (this story's AC-03 is the resolution half; the picker UI itself is
  accounts-identity/08's job, which this story only unblocks per its AC-05).
- Any device-management beyond list + revoke + the adult-confirmed toggle
  (renaming a device, per-device usage history/analytics beyond the coarse
  last-seen relative time in AC-04).
- A general multi-device sync of anything OTHER than entitlement resolution -
  no shared game state, no cross-device rejoin, no shared gallery sync beyond
  what `keepsake-vault` separately builds.
- QR-code scanning - the code is short and human-enterable; a scannable QR is
  a nice-to-have parked for later, not required here.
- Any change to `FamilySafeContentSelector`'s/`TemplateCatalog`'s own internal
  filtering logic - the fix in AC-07 is entirely at the `GameHub.StartRound`
  CALL SITE (computing the effective `familySafe` value before the selector is
  invoked), not inside the selector itself.
- **The superseded "force the flag on for a kid-flagged device" mechanism.**
  The earlier draft's `Room.FamilySafeForced` / `IsKidDevice`-forces-safe-on
  design is REPLACED by AC-07's inverted default (`Room.AdultUnlocked`,
  default `false`, opt-in only) - do not build both; there is exactly one
  content-safety capture-once boolean on `Room` for this story.
- **Solo play's teen-plus gate (SCOPED OUT here - needs its own follow-up).**
  AC-07 is enforced at `GameHub.StartRound`, which is GROUP play. Solo play is
  client-driven with no server session today (`GameHub.cs`'s own comment: "Solo
  play has no server session ... it is client-driven"), so solo template
  selection - including the teen-plus tier - is gated client-side and is NOT
  closed by this story: a kid on a family tablet could still reach teen-plus in
  SOLO mode by clearing storage or using a modified client, the same root cause
  finding #1 named. Closing it is a genuine design question this story does not
  answer (does solo move content selection server-side, gate the library
  download behind the adult signal, or mint a lightweight solo session?) and so
  is deliberately deferred to its own follow-up story, tracked in feature.md's
  Parked/Decisions. AC-07's "can never" guarantee is therefore SCOPED TO GROUP
  PLAY until that follow-up lands - do not read it as covering solo.
- **Per-device capability scoping (PARKED - ADR 0003, recorded in
  feature.md's Parked section).** Letting a parent choose WHICH paid grants a
  linked device carries is explicitly rejected for now: the free tier is
  generous, packs apply family-wide at no extra cost, and the AI cost gate
  bounds spend per session/month regardless of who plays. A linked device
  always carries the WHOLE family grant set (AC-03), never a subset - only the
  content-safety state (AC-07) is ever device/adult-signal-scoped, because it
  is a content-safety concern, not an entitlement.

## Technical Notes
- **api:** extend `api/src/Accounts/` with a link-code minter (a NEW small
  short-code generator, entropy-floored per AC-01 - do NOT reuse
  `RoomRegistry`'s room-code alphabet/length as-is; size the code so its
  keyspace resists brute force across its short validity window even under
  the redeem endpoint's rate limits, e.g. a longer code and/or a larger
  alphabet than a room code, generated via `RandomNumberGenerator`, never
  `System.Random` or a `Guid` treated as "random enough" without confirming
  its generation path is CSPRNG-backed) and a `FamilyDeviceToken` store (Table
  Storage: `PartitionKey = accountId`, `RowKey = deviceTokenId`; properties:
  a HASH of the token value (AC-05 - never the raw secret), created-at,
  last-used-at (AC-04's relative last-seen), a short random non-identifying
  label (AC-04, minted at redeem time), revoked flag, rolling `ExpiresUtc`,
  and `IsAdultConfirmedDevice` - the redesigned adult-unlock flag (AC-07),
  defaulting to `false` for every newly redeemed device; the parent toggles it
  from the linked-devices list, a plain property update on this SAME row, not
  a new record). The device token itself is an opaque random string -
  deliberately NOT a JWT and NOT wrapped by `PurchaserCredentialService`'s Data
  Protection, because it must be revocable server-side BY ROW (a
  Data-Protection payload can only expire, never be individually revoked
  before its TTL).
- **Redeem endpoint:** `POST /api/accounts/devices/redeem { code }` - the code
  travels in the request BODY, never a URL path segment (the security
  posture's "handles are secrets" rule: a path segment leaks to App Insights,
  access logs, `Referer`, and browser history, while the existing
  `PiiScrubbingTelemetryInitializer` only scrubs the query string). The
  endpoint resolves the code to an `AccountId`, mints + persists a new
  `FamilyDeviceToken` row (with `IsAdultConfirmedDevice = false`, AC-02),
  returns the raw token to the device ONCE (only the hash is retained
  server-side, AC-05). The device PERSISTS this token client-side (e.g.
  `localStorage`) - a DELIBERATELY less restrictive persistence than
  accounts-identity/03's in-memory-only `PurchaserSession`, because the whole
  point is surviving app restarts/reloads on a kid's own device; the
  mitigation for that relaxed persistence is exactly this story's per-device,
  individually-revocable shape (AC-04) plus the rolling-TTL/rotation and rate
  limiting below.
- **Redeem endpoint rate limiting (security posture, non-negotiable):** the
  redeem endpoint carries BOTH a per-IP rate limit AND a global ceiling (an
  IP-only limiter is defeated by IP rotation) PLUS a per-CODE attempt burn (a
  code is invalidated after a small number of failed redeem attempts against
  it, independent of the IP making them) - mirror the partition-on-remote-IP
  precedent already used for the AI gate (`Program.cs`, "Partition on the
  remote IP") but add the missing global + per-code layers this story
  requires that the AI gate's simpler case did not.
- **Rolling TTL + silent re-issue on use (security posture):** the token is
  long-lived but not indefinite - `ExpiresUtc` slides forward on each
  successful use (a resolve at `CreateRoom`, or an explicit refresh) rather
  than being fixed at redeem time, so a device in regular use never has to
  re-link. Because SignalR's `accessTokenFactory` has no return channel to
  hand a rotated value back to the client over the hub connection itself,
  add one small companion REST endpoint - e.g. `POST
  /api/accounts/devices/refresh { token }` (token in the BODY, never the URL) -
  that the web client calls once per app launch (not per hub reconnect): it
  verifies the current token by hash, mints a REPLACEMENT opaque value on the
  SAME row, invalidates the old value immediately, updates `last-used-at`, and
  returns the new value for the client to persist. This bounds how long a
  copied/stolen token stays valid if the legitimate device keeps using its
  own (freshly rotated) copy.
- **List/revoke endpoints:** live behind the SAME `PurchaserCredentialService`-
  resolved auth accounts-identity/08's preset management uses - only the
  signed-in account holder can list/revoke their OWN account's devices, and
  the `IsAdultConfirmedDevice` toggle (AC-07) is edited through this SAME
  authenticated surface, never from an unauthenticated device.
- **`GameHub`'s connect-time resolver (accounts-identity/06):** extend the
  SAME per-connection singleton story 06 built (`ResolvedConnectionIdentity`
  - `SessionEntitlements` + a bool) to also recognize a family-device token
  (in addition to a purchaser session credential) for the `SessionEntitlements`
  half, AND to compute the REAL value of the `AdultUnlocked` bool this story
  is the one that actually populates (story 06 always set it `false`, as a
  reserved slot). At `OnConnectedAsync`: resolve a purchaser session first (if
  present, `AdultUnlocked = true` unconditionally, adult-by-construction);
  else resolve a family-device token and set `AdultUnlocked` to that token
  row's `IsAdultConfirmedDevice` value; else (no credential at all)
  `AdultUnlocked = false`. Extend that one resolver rather than adding a
  second, parallel one inside `CreateRoom`.
- **`IEntitlementService.EvaluateForSession(purchaserIdentity)`:** the identity
  string passed for a family-device-token-resolved session is the family's
  resolved account identity (whatever `StoredValueEntitlementService`/
  `IAccountStore` already expect post-accounts-identity/05's re-keying, e.g.
  the account's email or `AccountId.ToString()`, chosen for consistency with
  story 06/05) - NOT a new identity type; the entitlement seam's public
  contract does not change. This identity string is, per story 06's AC-04/
  AC-08 discipline, used only for the single `EvaluateForSession` call and
  never persisted - the same rule applies to a device-token-resolved identity
  as to a purchaser-resolved one.
- **`Room.CaptureAdultUnlocked(bool)` (AC-07):** a SMALL, separate,
  capture-once field on `Room`, set once at `CreateRoom` from the resolved
  connection's `AdultUnlocked` bool, alongside (not folded into)
  `Room.CaptureEntitlements` - keeping content-safety state and capability
  state as two distinct capture-once fields, since they are different axes
  (AC-03's note). `GameHub.StartRound`'s EXISTING `familySafe` parameter
  (today host-supplied per round) is combined with `room.AdultUnlocked` at the
  TOP of `StartRound`, BEFORE `FamilySafeContentSelector`/`TemplateCatalog`
  ever see a value: `var effectiveFamilySafe = room.AdultUnlocked ?
  familySafe : true;` - this one-line call-site change is the entire "small
  child-safety touch to the content selector" the ADR references; the
  selector's own internal filtering logic is untouched (Out of Scope). The web
  client SHOULD also disable/hide its family-safe toggle when it knows the
  room has no adult-unlock signal (a UX nicety, not a security boundary) - the
  server check alone is authoritative and must hold even if the client is
  compromised, stale, modified, or simply wrong (verify via a raw/modified hub
  call in testing, not just the normal UI path).
- **Host-migration guard (AC-08):** confirm the host-migration path
  (`EnsureHostLocked`/`PassHost`, `session-engine`) never reads or writes
  `Room.AdultUnlocked` - it is set exactly once, at `CreateRoom`, from the
  ORIGINAL creating connection, and is otherwise as immutable as
  `Room.Entitlements` already is.
- **web:** the Account page gains "Linked devices" (list + revoke + the
  adult-confirm toggle, AC-04/AC-07) and "Link a device" (generates +
  displays the code). A NEW minimal redeem surface, reachable WITHOUT being
  signed in (a kid's device is never signed in) - a small route/screen where a
  code can be typed, calling the redeem endpoint and storing the returned
  token; reuse theme tokens + the existing `AppBar`, do not build a second
  visual language. This redeem screen is registered as a new route in
  `web/src/App.tsx` (see the Files list below - this is a cross-feature
  merge-order hazard, not just an internal one: `sysadmin-console/04` deletes
  the unrelated `/admin/billing-mode` route from this SAME file; whichever of
  the two lands second resolves the small diff overlap). `useGameHub.ts`'s
  `accessTokenFactory` (story 06) is extended to prefer a live purchaser
  session credential if signed in, else a stored family-device token, else
  supply nothing; the web client also calls the new refresh endpoint once per
  app launch when a device token is present, to keep the rolling TTL alive.
- **Files:** `api/src/Accounts/` (new `FamilyDeviceToken.cs`,
  `IFamilyDeviceTokenStore.cs`/implementations, the link-code minter),
  `api/src/Controllers/AccountsController.cs` (generate/redeem/refresh/list/
  revoke/adult-confirm-toggle endpoints), `api/src/Hubs/GameHub.cs` (extend
  story 06's per-connection resolver + `Room.CaptureAdultUnlocked` +
  `StartRound`'s effective-`familySafe` computation), `api/src/Rooms/Room.cs`
  (the new `AdultUnlocked` capture-once field), `web/src/pages/Account.tsx`
  (linked-devices UI), `web/src/App.tsx` (the new redeem route - see the
  cross-feature note above), a new small web redeem screen/component,
  `web/src/signalr/useGameHub.ts` (the `accessTokenFactory` also considers a
  stored device token, plus the periodic refresh call).

## Tests
| AC | Test |
|---|---|
| AC-01 | `tests/QuibbleStone.Api.Tests/Accounts/FamilyDeviceLinkTests.cs (new): generating a link code ties it to the correct AccountId, is displayable, and is measurably longer/higher-entropy than a room join code (assert code length/alphabet size).` |
| AC-02 | `tests/QuibbleStone.Api.Tests/Accounts/FamilyDeviceLinkTests.cs: redeeming a valid code once mints a token with IsAdultConfirmedDevice=false; redeeming the SAME code again fails (single-use); an unredeemed code past its expiry window fails to redeem.` |
| AC-03 | `tests/QuibbleStone.Api.Tests/GameHubEntitlementTests.cs (extended): a connection carrying a valid family-device token resolves to that family's entitlements on CreateRoom, discarding the token at the boundary (no Room/Player field); confirm this happens whether or not that token's IsAdultConfirmedDevice is true.` |
| AC-04 | `manual: link two devices from the Account page, confirm both are listed with a short non-identifying label and a relative last-seen time (not an IP/user agent); revoke one and confirm a subsequent CreateRoom from that device falls back to default-unlocked, family-safe.` |
| AC-05 | `manual: inspect the FamilyDeviceToken table schema - confirm only AccountId + a token HASH (not the raw value) + created-at/last-used-at/label/revoked/IsAdultConfirmedDevice, no PII.` |
| AC-06 | `manual/Playwright (tests/*.spec.ts, not in CI): a fresh device with no session and no device token plays a full free round with the default-unlocked, family-safe baseline, unaffected by this story.` |
| AC-07 | `tests/QuibbleStone.Api.Tests/GameHubEntitlementTests.cs (extended): CreateRoom from a connection with no credential, or a device token with IsAdultConfirmedDevice=false, captures Room.AdultUnlocked=false; a subsequent StartRound called with the client's familySafe=false STILL applies family-safe content. A signed-in PurchaserSession, or a token with IsAdultConfirmedDevice=true, captures AdultUnlocked=true and StartRound then honors the client's familySafe value. Plus manual: attempt the bypass via a modified/raw hub call (not just the normal client UI) to confirm the server, not the client, is the boundary.` |
| AC-08 | `tests/QuibbleStone.Api.Tests/GameHubEntitlementTests.cs or a host-migration test fixture (extended): a room with AdultUnlocked=false undergoes a host migration (EnsureHostLocked/PassHost promotes a different, possibly kid, player); confirm Room.AdultUnlocked is still false afterward and StartRound still forces family-safe content.` |

## Dependencies
- accounts-identity/06 (the `CreateRoom`-resolves-and-discards wiring and the
  per-connection singleton this story extends rather than forks).
- accounts-identity/07 (the free family account a device links to).
- accounts-identity/05 (the `AccountId` spine the token resolves to).
- child-safety (the family-safe content selector, `FamilySafeContentSelector`/
  `TemplateCatalog`, whose CALL SITE this story's AC-07 changes - the
  selector's own filtering logic is not modified, per Out of Scope).
- session-engine (`EnsureHostLocked`/`PassHost`, host migration - AC-08's
  guard is reviewed against this existing code path).
