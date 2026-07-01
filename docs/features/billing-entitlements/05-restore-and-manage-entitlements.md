# Story: Restore / manage entitlements

**Feature:** Billing & Entitlements  ·  **Status:** Not Started  ·  **Issue:** #74

## Context
A purchaser should always be able to see what they own and get it back on a
new device without contacting support or re-buying. This story is the
read-and-restore view on top of billing-entitlements/01's entitlement store,
paired with accounts-identity/03's sign-in - the last link that makes a
purchase durable across the purchaser's devices. See
[feature.md](./feature.md) and README section 3 ("the account hooks go in
early ... retrofitting auth onto an anonymous system later is painful").

## Acceptance Criteria
- [ ] AC-01: Given a signed-in purchaser (accounts-identity/03) with one or
      more granted entitlements, when they open the restore/manage view, then
      they see a plain-language list of what is unlocked (e.g. "Family Plan -
      active" or "Holiday Pack - unlocked") sourced from
      billing-entitlements/01's entitlement store for their account.
- [ ] AC-02: Given a purchaser signs in on a new device that has never made a
      purchase, when they open the restore view, then their existing
      entitlements are visible there without needing to re-purchase anything,
      and the next session created from that device reflects the same unlocks
      as their original device (per billing-entitlements/01 AC-03's
      session-creation-time read).
- [ ] AC-03: Given a purchaser with zero entitlements (e.g. they only ever
      used the tip jar, or never purchased), when they open the restore view,
      then it shows a friendly empty state (not an error) - consistent with
      billing-entitlements/01 AC-07's "day one, nothing granted" default.
- [ ] AC-04: Given the restore/manage view, when it is reached, then it is
      only reachable from a purchaser-facing area (the same settings/account
      entry point as accounts-identity/03's sign-in) - it never appears in
      the join code, lobby, word entry, or reveal flow.
- [ ] AC-05: Given the restore view displays entitlement state, then it
      displays no data about which players/nicknames used those entitlements
      in past sessions - it shows what the *purchaser* owns, not a play
      history (consistent with accounts-identity/02 AC-03's "who bought this,
      not who played this" scoping).
- [ ] AC-06: Given a purchaser is not signed in, when they navigate to the
      restore/manage entry point, then they are directed to sign in
      (accounts-identity/03) first - this view never guesses or shows
      entitlement state for an unauthenticated visitor.

## Out of Scope
- Self-service plan changes (upgrade/downgrade/cancel a subscription) -
  parked in feature.md (Phase 3+); this story is read-only (view + restore),
  not a full Stripe Customer Portal replacement.
- Purchase history / receipts UI - a plain "what's unlocked now" list is
  sufficient for Phase 2; itemized transaction history is a later addition if
  ever needed.
- Any change to how entitlements are granted - this story only reads from the
  store billing-entitlements/01 defines and stories 03-04 write to.
- Multi-purchaser / household management - parked in feature.md.

## Technical Notes
- Web: extend or sit alongside the `Account`/sign-in screen from
  accounts-identity/03 (`web/src/pages/`) rather than building a second,
  separate settings surface - restore/manage is naturally the same
  purchaser-facing area, styled from `web/src/theme.ts` tokens and the shared
  `AppBar`/`Button` contracts.
- API: a read endpoint (REST controller, `api/src/Controllers/`, likely
  alongside or near the accounts controller from accounts-identity/03) that,
  given the signed-in purchaser's credential, calls
  `IEntitlementService`/the Table Storage read path from
  billing-entitlements/01 and returns a simple, human-readable list - no new
  write path is introduced here.
- AC-06's "must be signed in first" requirement means this view's route/API
  call is guarded by the same purchaser-credential check
  accounts-identity/03 introduces - reuse that guard rather than writing a
  second auth check.
- Keep the mapping from capability keys (`pack.<id>`, a subscription product)
  to a friendly display name in one small place (a lookup table or a
  `DisplayName` property alongside the catalog from billing-entitlements/01)
  so adding a new pack later does not require touching this view's rendering
  logic.

## Tests
| AC | Test |
|---|---|
| AC-01 | `api/tests/Billing/RestoreViewTests.cs (to be created): a purchaser with a known grant sees it listed via the read endpoint.` |
| AC-02 | `manual: sign in on a second simulated device/browser profile with an entitled account - confirm the list matches, then create a new session and confirm the unlock is present.` |
| AC-03 | `manual: sign in as a purchaser with zero grants - confirm a friendly empty state, no error.` |
| AC-04 | `manual: UI audit - confirm the restore/manage entry point is absent from Join, Lobby, FillBlank, and Reveal.` |
| AC-05 | `manual: code/response read - confirm the read endpoint's payload contains no player/nickname/session reference.` |
| AC-06 | `manual: attempt to navigate to the restore view while signed out - confirm redirect/prompt to sign in, no entitlement data shown.` |

## Dependencies
- billing-entitlements/01 (the entitlement store this view reads).
- billing-entitlements/03 and/or 04 (at least one grant path must exist to
  have anything meaningful to restore, though AC-03's empty state is testable
  without one).
- accounts-identity/03 (the sign-in this view sits behind and restores for).
