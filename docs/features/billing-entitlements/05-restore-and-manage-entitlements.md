# Story: Restore / manage entitlements

**Feature:** Billing & Entitlements  ·  **Status:** Complete  ·  **Issue:** #74

## Context
A purchaser should always be able to see what they own and get it back on a
new device without contacting support or re-buying. This story is the
read-and-restore view on top of billing-entitlements/01's entitlement store,
paired with accounts-identity/03's sign-in - the last link that makes a
purchase durable across the purchaser's devices. See
[feature.md](./feature.md) and README section 3 ("the account hooks go in
early ... retrofitting auth onto an anonymous system later is painful").

## Acceptance Criteria
- [x] AC-01: Given a signed-in purchaser (accounts-identity/03) with one or
      more granted entitlements, when they open the restore/manage view, then
      they see a plain-language list of what is unlocked (e.g. "Family Plan -
      active" or "Holiday Pack - unlocked") sourced from
      billing-entitlements/01's entitlement store for their account.
- [x] AC-02: Given a purchaser signs in on a new device that has never made a
      purchase, when they open the restore view, then their existing
      entitlements are visible there without needing to re-purchase anything,
      and the next session created from that device reflects the same unlocks
      as their original device (per billing-entitlements/01 AC-03's
      session-creation-time read).
- [x] AC-03: Given a purchaser with zero entitlements (e.g. they only ever
      used the tip jar, or never purchased), when they open the restore view,
      then it shows a friendly empty state (not an error) - consistent with
      billing-entitlements/01 AC-07's "day one, nothing granted" default.
- [x] AC-04: Given the restore/manage view, when it is reached, then it is
      only reachable from a purchaser-facing area (the same settings/account
      entry point as accounts-identity/03's sign-in) - it never appears in
      the join code, lobby, word entry, or reveal flow.
- [x] AC-05: Given the restore view displays entitlement state, then it
      displays no data about which players/nicknames used those entitlements
      in past sessions - it shows what the *purchaser* owns, not a play
      history (consistent with accounts-identity/02 AC-03's "who bought this,
      not who played this" scoping).
- [x] AC-06: Given a purchaser is not signed in, when they navigate to the
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
| AC-01 | `tests/QuibbleStone.Api.Tests/Billing/RestoreViewTests.cs::SignedIn_purchaser_sees_active_grants_labeled` (labels via `EntitlementLabels`). Also verified LIVE: signed in as a purchaser, the restore endpoint listed Full Library / Remote Play / Large Groups.` |
| AC-02 | `manual: verified - signed in on a second simulated device/browser profile with an entitled account; the list matched, and a new session created from that device reflected the same unlocks.` |
| AC-03 | `tests/QuibbleStone.Api.Tests/Billing/RestoreViewTests.cs::SignedIn_purchaser_with_no_grants_gets_empty_list` and `::Valid_credential_but_no_account_gets_empty_list` (friendly empty state, no error).` |
| AC-04 | `manual: verified in browser - the restore/manage entry point (Account page) is absent from Join, Lobby, FillBlank, and Reveal.` |
| AC-05 | `tests/QuibbleStone.Api.Tests/Billing/RestoreViewTests.cs::Payload_contains_no_player_or_session_data` |
| AC-06 | `tests/QuibbleStone.Api.Tests/Billing/RestoreViewTests.cs::Unauthenticated_caller_gets_401` and `::Invalid_credential_gets_401` (guarded by `PurchaserCredentialService`), plus `tests/QuibbleStone.Api.Tests/Accounts/PurchaserCredentialServiceTests.cs` (existing suite, re-run as regression).` |

## Dependencies
- billing-entitlements/01 (the entitlement store this view reads).
- billing-entitlements/03 and/or 04 (at least one grant path must exist to
  have anything meaningful to restore, though AC-03's empty state is testable
  without one).
- accounts-identity/03 (the sign-in this view sits behind and restores for).
