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
| 01 | #67 | Anonymous player, forever | Not Started |
| 02 | #68 | Lightweight purchaser account | Not Started |
| 03 | #69 | Sign-in and restore on a new device | Not Started |

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
