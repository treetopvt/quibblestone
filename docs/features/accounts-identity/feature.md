# Feature: Accounts & Identity

## Summary
Formalizes and extends QuibbleStone's tiered identity model: players stay
anonymous forever (join code + nickname + Guardian, no PII), and a lightweight
account exists only for a purchaser, created only at the moment they buy. This
is the auth seam that `billing-entitlements` hangs off of.

## README reference
README section 3 (Monetization - "Identity model (tiered)": players anonymous
forever; only the purchaser gets a lightweight account, and only when they buy;
account hooks go in early even with a minimal UI) and section 7 (Epic Map -
Phase 2, "Accounts & Identity (M) - anonymous players; lightweight purchaser
accounts"). Child-privacy posture: section 6 (minimal data on minors) and
section 3 (COPPA / GDPR-K). CLAUDE.md section 6 (Monetization seam).

## Stories
<!-- Status: Not Started | In Progress | Complete | Blocked | Dropped -->
| Story | Issue | Title | Status |
|---|---|---|---|
| 01 | #67 | Anonymous player, forever | Complete |
| 02 | #68 | Lightweight purchaser account | Complete |
| 03 | #69 | Sign-in and restore on a new device | Complete |
| 04 | #TBD | Magic-link email delivery | Not Started |

## Dependencies
- session-engine (the existing anonymous join contract this feature formalizes:
  nickname + Guardian variant at join, no account).
- child-safety (nicknames remain free text and stay filtered regardless of
  whether the session's purchaser is signed in).
- billing-entitlements (the purchaser account exists to hold entitlements;
  story 03 restore is the read side of billing-entitlements/05).

## Design notes
- **No new identity system for players.** This feature does not touch the
  player join flow (nickname + Guardian, session-engine/02) at all - story 01
  is documentation-and-hardening of a contract that is already true, so the
  session-creation code path can assume "no account" without a special case.
  Adding accounts must be additive, never a prerequisite for play.
- **One account type, one purpose.** The purchaser account exists solely to
  own entitlements and let a purchase survive a device change. It is not a
  player profile, not a friends list, not a leaderboard identity - none of
  that is in scope for Phase 2.
- **Minimum viable account.** An email (the magic-link identity, ADR 0002
  Decision A - resolved 2026-07-03) is the only required field. No display
  name, no avatar, no date of birth, no address, no password, no OAuth SDK.
  The purchaser is assumed to be an adult (they are the one completing a
  checkout), so no age-gating flow is needed for the account itself - the
  age/safety posture that matters is about the **kids playing**, who never
  touch this account.
- **The account belongs to the buyer, not the kids playing.** A parent buys
  the family plan on their own phone; the kids in the back seat still join
  with just a code and a nickname. The purchaser's sign-in state never flows
  down into a player's join experience - a signed-out room plays exactly like
  today.
- **Where it lives:** the account record is a small row keyed by the
  purchaser's email identity (magic-link, ADR 0002 Decision A), stored
  alongside entitlements in Azure Table Storage (README section 4). No new
  datastore is introduced.
- **Session-creation is still the only place identity matters for gameplay.**
  A room does not care who is signed in mid-session; the signed-in state is
  read once, at session-creation, by the entitlement check in
  billing-entitlements/01 - never per-request or per-answer.

## Parked - Phase 3+
- Player-side profiles (remembered nickname/Guardian across devices,
  friends/repeat-crew list) - explicitly out of scope; players stay anonymous.
- Social/OAuth sign-in of any kind (ADR 0002 Decision A resolved the identity
  provider to magic-link email only) - and, within that, multi-provider
  linking or account merge, should the mechanism ever change.
- Household / multi-purchaser management (e.g. co-parents sharing one plan
  with separate logins) - Phase 2 ships one purchaser, one account.
- Any UI beyond the minimum checkout/sign-in/restore flow (account settings
  page, purchase history beyond "what's unlocked" - see billing-entitlements/05).

## Decisions
- 2026-07-01: Scoped to exactly the three stories above (anonymous contract,
  purchaser account, sign-in/restore) rather than a general auth system,
  because the only thing an account needs to do in this product is hold
  entitlements and survive a device change. Anything broader is scope creep
  against README section 3's "lightweight" instruction. Recorded during the
  look-ahead planning pass ahead of Slice 1 shipping.
- 2026-07-03: [ADR 0002](../../adr/0002-accounts-subscriptions-and-admin.md)
  (accounts, subscriptions, sys-admin surface) states the load-bearing invariant
  this feature's purchaser account must uphold - "entitlement travels with the
  session, not identity": the host's purchaser identity is resolved to
  capabilities at `GameHub.CreateRoom` and discarded at that boundary, so only
  the resolved capability set (never a purchaser id) lands on `Room`. The host
  proves purchaser status by supplying the magic-link session token to the hub
  via SignalR's `accessTokenFactory` (ADR 0002 Decision F).
- 2026-07-03: **Identity provider resolved (ADR 0002 Decision A): magic-link
  email.** Story 02's open provider choice is closed - purchasers sign in via an
  emailed one-time link (no password, no OAuth SDK). The same one-time-token
  issue/verify plumbing is reused for the sys-admin back office's operator login
  (`sysadmin-console/01`) against a SEPARATE operator allowlist in config / Key
  Vault; admin authorization is allowlist membership resolved at verify time and
  is never inferred from being a purchaser (`purchaser == admin` is the bug to
  prevent). Consistent with story 02 AC-01 (email or one identity, nothing more).
- 2026-07-03: **Built via `/orchestrate-feature` (all three stories on the
  `claude/orchestrate-accounts-identity-a373p2` umbrella).** Notes from the build:
  - Story 02's magic-link token verifier originally compared DECODED signature
    bytes, which accepted a padding-equivalent final base64url char (~6% of
    mutations) and flaked its tamper test. Fixed to compare the CANONICAL
    base64url strings via constant-time `FixedTimeEquals`, with an exhaustive
    regression test. Live-verified: replaying a consumed token returns
    `link-invalid` (single-use holds).
  - The purchaser session credential (story 03) uses ASP.NET Data Protection
    (`ITimeLimitedDataProtector`, 12h TTL, purpose `QuibbleStone.PurchaserSession`)
    - no new dependency, no hand-rolled crypto. The current key ring is the
    framework default (per-process, not durable); a Key Vault-backed shared key
    ring is a **billing-entitlements deployment follow-up**, not this slice.
  - AC-05 no-enumeration is structural: the request endpoint never reads the
    account store (identical neutral response for any email); the dev-only token
    echo is gated on `IsDevelopment()`. Live-verified end to end.
  - The `signed-in` happy path is not UI-drivable yet (account creation lives in
    the not-yet-built billing purchase flow; no seed endpoint) - it is covered by
    `SignInTests.cs`. Verification drove request -> verify (`no-account`),
    single-use replay, and confirmed free play (hub negotiate + the 2-device
    group-mode e2e) is unaffected by the added `/account` route.
- 2026-07-04: **Email delivery decomposed into story 04 (Not Started).** The
  magic-link transport was carried as a deferred aside in stories 02/03 and
  implementation.md ("later, the email-delivery provider's key"), never its own
  story. It is decomposed now because it is on the critical path: verified on UAT
  (after `sysadmin-console` #158 shipped) that neither purchaser sign-in nor
  operator console login can complete in a deployed environment - the API runs as
  Production (no dev-token echo), no provider is wired, and
  `Accounts:TokenSigningKey` is unset (ephemeral). Token issuance shipped (02);
  delivery is the half that stayed unbuilt (ADR 0002 Decision A named both). OPEN
  decision: provider is Azure Communication Services Email vs SendGrid (story 04
  Technical Notes) - ACS is Azure-native but adds an Email Communication Service +
  a verified domain to the footprint; SendGrid avoids the resource but adds a
  third-party dependency. Story 04 also promotes `Accounts:TokenSigningKey` to a
  durable Key Vault secret so a delivered link survives an app recycle.
