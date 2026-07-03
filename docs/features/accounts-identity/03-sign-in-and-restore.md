# Story: Sign-in and restore on a new device

**Feature:** Accounts & Identity  ·  **Status:** In Review  ·  **Issue:** #69

## Context
A purchaser who bought the family plan on their phone should not have to
re-buy it on a tablet, a laptop, or after reinstalling the PWA. This story
lets a returning purchaser sign back into their lightweight account
(accounts-identity/02) on a new device, which is the read-side trigger for
billing-entitlements/05's restore of what they own. Anonymous play is
untouched - no player is ever asked to sign in to join a room. See
[feature.md](./feature.md) and README section 3 ("the account hooks go in
early even if the UI is minimal").

## Acceptance Criteria
- [ ] AC-01: Given a purchaser has an existing account (accounts-identity/02)
      and opens QuibbleStone on a new device, when they enter their email and
      follow the emailed magic link (ADR 0002 Decision A), then their existing
      account is recognized and no duplicate account is created.
- [ ] AC-02: Given a purchaser signs in successfully, when
      billing-entitlements/05's restore view is opened, then it can look up
      that purchaser's entitlements without any device-specific state - the
      new device now behaves identically to the original purchasing device
      for entitlement purposes.
- [ ] AC-03: Given a device where no one has signed in, when a player opens
      the app and plays (single-player or joins a group by code), then they
      are never prompted to sign in, and declining/ignoring any visible
      sign-in affordance has zero effect on their ability to play the free
      tier.
- [ ] AC-04: Given the sign-in surface, when it is placed in the app, then it
      lives in a purchaser-facing area (Home's settings/account entry point or
      the restore/manage screen) - never inside the join code, lobby, word
      entry, or reveal flow a child would be using.
- [ ] AC-05: Given a magic-link request for an email address that has no
      matching account, then the system does not leak whether an account
      exists for that email beyond what is functionally necessary (no
      account is silently created as a side effect of a failed sign-in
      attempt; the user is guided to purchase, not left in an ambiguous
      state).
- [ ] AC-06: Given no accounts or purchases exist anywhere yet (day one of
      this feature shipping), then this story's sign-in surface is inert
      (nothing to restore) without erroring - graceful for the common case of
      a family that has never paid for anything.

## Out of Scope
- Any password-reset / account-recovery flow beyond what magic-link already
  provides natively - requesting a fresh link IS the recovery flow, since
  there is no password to reset or forget (ADR 0002 Decision A).
- Multi-device *simultaneous* session management (seeing "signed in on 2
  devices," remote sign-out) - a single sign-in-and-restore is enough for
  Phase 2.
- The entitlement list UI itself and the "what's unlocked" rendering
  (billing-entitlements/05 owns that view; this story owns getting the
  purchaser signed in so that view has an identity to query).
- Any change to how players join rooms - AC-03 is a guard, not new join-flow
  work.

## Technical Notes
- Web: a small `SignIn`/`Account` surface (new `web/src/pages/` component,
  named to avoid clashing with existing screens) reachable only from a
  clearly purchaser-labeled entry point (e.g. a small "Account" affordance in
  the AppBar or Home's settings area - reuse `web/src/components/AppBar.tsx`,
  do not fork a second app-bar variant per CLAUDE.md section 4). Styled from
  `web/src/theme.ts` tokens only, consistent with the stone-tablet/Guardian
  visual language - no separate "corporate account page" look.
- API: a sign-in endpoint (REST controller, `api/src/Controllers/`) that
  issues a fresh magic-link token to an entered email (REUSING
  accounts-identity/02's one-time-token issuer/verifier - the same reusable
  service `sysadmin-console/01`'s operator login also calls, not a second
  implementation), and, once the link is followed, resolves the verified
  email to the existing `Account` record via `IAccountStore` - it does not
  create a new record on a match (AC-01) and does not create one on a miss
  (AC-05), and its response shape/timing does not reveal whether a given
  email has an account.
- Session/token handling for "signed in as this purchaser" should be a
  short-lived, purchaser-scoped credential (e.g. a signed cookie or bearer
  token) - it must never be required by, or checked in, the SignalR game hub
  or any player-facing endpoint (AC-03/AC-04). Keep it fully separate from
  room/player state (`api/src/Rooms/`).
- No secrets in `VITE_*` (CLAUDE.md section 4/6); any signing key for the
  purchaser credential lives in Azure Key Vault alongside the Stripe keys from
  billing-entitlements/03.

## Enhancement (2026-07-03, PR #157)
The purchaser credential from a successful verify used to live in `Account.tsx`
local state, so it was discarded on navigation away - a return to Account (or the
keepsake cloud gallery, keepsake-gallery/05) forced a fresh sign-in every visit.
Added an app-wide, in-memory `PurchaserSession` context (`web/src/account/
PurchaserSession.tsx`, mounted above the router in `main.tsx`) so sign-in persists
across client-side navigation for the SPA's lifetime, plus a Sign out control.
In-memory ONLY (never persisted) - a short-lived bearer must not linger on a shared
or child's device across reloads (the credential is gone on a full reload / new
tab, an accepted trade-off). Auth boundary unchanged: only the Account surface
consumes it; the game/reveal/lobby flow never imports it. A durable, persisted
sign-in remains a separate, deliberate decision (not done here).

## Tests
| AC | Test |
|---|---|
| AC-01 | `tests/QuibbleStone.Api.Tests/Accounts/SignInTests.cs: verify with a known identity resolves the same account twice (created-at stable), no duplicate created.` |
| AC-02 | `manual: sign in on a second simulated device/browser profile - confirm billing-entitlements/05's restore view shows the same entitlements (consumes the purchaser credential this story issues).` |
| AC-03 | `manual/Playwright (tests/*.spec.ts, not in CI): full free-play round with the sign-in affordance visible but untouched. Also web/src/account/signInClient.test.ts pins the client fails graceful and never gates play.` |
| AC-04 | `manual: UI audit - confirm the /account entry point renders only from Home, never on Join, Lobby, GroupRound (word entry), or Reveal.` |
| AC-05 | `tests/QuibbleStone.Api.Tests/Accounts/SignInTests.cs: verify with an unknown identity creates no account row; the request endpoint returns a neutral shape (dev token echo only, gated on IsDevelopment) and never reads or writes the store.` |
| AC-06 | `manual: fresh environment with zero accounts/entitlements - open /account, confirm an empty-but-friendly inert state, no error.` |

## Dependencies
- accounts-identity/02 (the account this story signs back into).
- billing-entitlements/01 (the entitlement model story 03 restores).
- billing-entitlements/05 (the restore/manage view this sign-in feeds).
