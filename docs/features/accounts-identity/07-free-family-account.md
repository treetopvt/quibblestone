# Story: The free family account

**Feature:** Accounts & Identity  ·  **Status:** Complete  ·  **Issue:** #211

## Context
[ADR 0003](../../adr/0003-admin-platform-and-family-accounts.md) Amendment 1
decouples account creation from purchase: an account becomes "an adult who
wants things to persist," and a purchaser is an account that ALSO holds paid
grants. Today `IAccountStore.CreateOrGetAsync` is called only from a purchase
flow (accounts-identity/02, AC-02) - there is no way to create an account
without paying, so a family that never buys anything has nowhere to claim a
keepsake vault (`keepsake-vault`) or link a kid's device (accounts-identity/09).
This story adds that free entry point, reusing the exact same magic-link
plumbing, and reframes the existing Account surface as a "Family account" (not
a purchaser-only page). See [feature.md](./feature.md) and ADR 0003's
Amendment 1: "purchase-only accounts - not player anonymity - are what made
keepsakes unrecoverable and support blind. Player anonymity is untouched."

## Acceptance Criteria
- [x] AC-01: Given a person who has never purchased anything, when they use a
      new "Create your family account" sign-up flow (email only), then a new
      `Account` (accounts-identity/05's `AccountId`-keyed record) is created
      holding email + created-at and NO entitlement grants - exactly the same
      minimal shape story 02 already established, just reachable WITHOUT a
      purchase.
- [x] AC-02: Given the sign-up flow, when it issues or verifies a magic link,
      then it reuses the EXACT SAME `IMagicLinkTokenService` issue/verify
      plumbing and the SAME neutral, no-enumeration response contract
      accounts-identity/02 AC and accounts-identity/03 AC-05 already
      established (identical response shape/timing whether or not an account
      already exists for that email) - no second token or response
      implementation.
- [x] AC-03: Given an existing purchaser account (created via a purchase,
      story 02) or a free family account created here, when the SAME email
      later goes through the OTHER path (a free sign-up for an existing
      purchaser, or a purchase for an existing free account), then exactly ONE
      account exists for that email - `CreateOrGetAsync`'s existing idempotency
      is exercised as create-or-attach, never a duplicate.
- [x] AC-04: Given the Account page (`web/src/pages/Account.tsx`), when it is
      viewed, then it reframes as a "Family account" surface: it offers
      create/sign-in for anyone (not just a returning purchaser), and for an
      account with no purchase it shows "no paid features unlocked yet, here's
      how to get them" rather than presenting as purchase-only or
      purchase-required.
- [x] AC-05 (entitlement AC): Given a brand-new free family account (no
      purchase), when a session is created from a device signed into it (via
      accounts-identity/06's wiring), then `EvaluateForSession` resolves it to
      EXACTLY the default-unlocked baseline - identical to an anonymous session
      - because it holds zero `EntitlementGrant` rows; creating a free account
      grants NOTHING by itself, and this story does not touch
      billing-entitlements/01's grant store.
- [x] AC-06 (child safety / no PII): Given the sign-up flow, then it collects
      ONLY an email - no name, birthdate, address, or any other field is ever
      requested or stored, identical to story 02's minimal-data posture
      (README section 6).
- [x] AC-07: Given free play (single-player or a same-code group), when it is
      played with no family account anywhere, then it is completely unaffected
      - declining/ignoring the new "Create a family account" affordance has
      zero effect on play (mirrors story 03 AC-03's existing guard).

## Out of Scope
- Kid seat presets (accounts-identity/08) - a later story built ON TOP of the
  free family account this story creates.
- The family device link (accounts-identity/09).
- Any change to the purchase flow's OWN account-creation call site
  (billing-entitlements/02-04 still call `CreateOrGetAsync` at purchase time
  exactly as today) - this story adds a SECOND entry point into the same
  idempotent create-or-get; it does not touch the purchase path's code.
- A distinct "free account" vs "purchaser account" data model - there remains
  ONE `Account` type (accounts-identity/05); "family account" is a UI/product
  framing, not a new record shape.
- Any entitlement bundle, trial grant, or free-tier bonus for signing up free -
  a free account holds zero grants (AC-05).

## Technical Notes
- **api:** the cleanest reuse is to extend the EXISTING `AccountsController`
  rather than fork a parallel controller. Add a `purpose` distinction
  (`"signin"` vs `"signup"`, or reuse/extend `MagicLinkPurpose` -
  `api/src/Accounts/IEmailSender.cs` already has `PurchaserSignIn` /
  `OperatorLogin`; add a `FamilySignUp` value so the delivered email copy reads
  correctly - "create your account" vs "sign in") to the request/verify calls.
  `Verify`'s existing "no-account" branch (today: "guided to purchase") becomes,
  on the sign-up path, a call to `IAccountStore.CreateOrGetAsync` instead -
  reusing the SAME idempotent create-or-get accounts-identity/02 already built,
  never a second creation path. On the EXISTING sign-in path this behavior is
  UNCHANGED (still "no-account, guided to purchase" - or, once this story
  ships, guided to create a free account instead, per AC-04's reframing).
- **web:** `Account.tsx` gains a "Create a family account" affordance alongside
  "Sign in" (or unifies them into one email-entry flow, since the server now
  handles create-or-get transparently based on the purpose) - reframe copy
  from "purchaser" to "family account" throughout, per AC-04. Reuse the
  existing sign-in form/component rather than forking a second screen.
- **Reuse map:** `IMagicLinkTokenService` (accounts-identity/02),
  `IAccountStore.CreateOrGetAsync` (accounts-identity/02, already idempotent),
  `PurchaserCredentialService` (accounts-identity/03), `IEmailSender`
  (accounts-identity/04). None of these are reimplemented - this story only
  adds a second CALLER of `CreateOrGetAsync` and a copy-selecting purpose.
- **Files:** `api/src/Controllers/AccountsController.cs` (extend `Verify`/the
  request body with a sign-up purpose), `api/src/Accounts/IEmailSender.cs`
  (`MagicLinkPurpose.FamilySignUp` + the matching copy in `AcsEmailSender.cs`;
  `NoOpEmailSender.cs` has no copy to update - it logs the purpose only),
  `web/src/pages/Account.tsx` (reframe + a create-account affordance). No hub or
  `Room` changes.
- **Gotcha (no enumeration regression):** a sign-up request must remain
  neutral in the SAME way sign-in already is - a constant response regardless
  of whether the email already has an account. The only place existence is
  ever revealed is AFTER a token is followed (the one case where the holder
  has already proven control of the inbox) - do not add a branch anywhere in
  the REQUEST path that reveals whether an account exists.

## Tests
| AC | Test |
|---|---|
| AC-01 | `tests/QuibbleStone.Api.Tests/Accounts/SignUpTests.cs (new): a sign-up verify for a brand-new email creates exactly one Account with zero grants.` |
| AC-02 | `manual: code read - the sign-up request/verify endpoints call the SAME IMagicLinkTokenService and return the SAME response shape/timing as the existing sign-in endpoints.` |
| AC-03 | `tests/QuibbleStone.Api.Tests/Accounts/SignUpTests.cs: a sign-up verify for an email that already has a purchaser account resolves to that SAME account (no duplicate); a purchase for an email that already has a free account attaches to it.` |
| AC-04 | `manual: UI walkthrough of /account with no purchase - confirm "Family account" framing and a "no paid features yet" message, not a purchase wall.` |
| AC-05 | `tests/QuibbleStone.Api.Tests/StoredValueEntitlementServiceTests.cs (existing, re-run as regression): a resolved identity with an account but zero grants returns exactly the default-unlocked baseline.` |
| AC-06 | `manual: code read of the sign-up request body/Account record - confirm only email is ever collected.` |
| AC-07 | `manual/Playwright (tests/*.spec.ts, not in CI): a full free-play round with the "Create a family account" affordance visible but untouched.` |

## Dependencies
- accounts-identity/05 (the `AccountId`-keyed account record this story
  creates instances of).
- accounts-identity/02, /03, /04 (the magic-link issuer/verifier, the
  purchaser credential, and email delivery - all already Complete and reused
  unchanged).
