# Story: Lightweight purchaser account

**Feature:** Accounts & Identity  ·  **Status:** Complete  ·  **Issue:** #68

## Context
The first time anyone spends money in QuibbleStone (the tip jar,
billing-entitlements/02, or a gated purchase, billing-entitlements/04), the
app needs somewhere to record who bought it - not to identify a player, but
to let entitlements survive the purchase and, later, a device change
(accounts-identity/03). This story creates that account: minimal, created
only at the moment of purchase, and never required to play. See
[feature.md](./feature.md) and README section 3 ("Only the purchaser gets a
lightweight account, and only when they buy").

## Acceptance Criteria
- [x] AC-01: Given a person is about to complete a purchase (tip jar or a
      gated purchase) and has no purchaser account yet, when the purchase flow
      reaches the point of needing one, then an account is created holding
      only an email address (the magic-link identity, ADR 0002 Decision A) -
      no password, no OAuth identity, no name, birthdate, address, or phone
      number is collected.
- [x] AC-02: Given no one has ever purchased anything in a browser/device,
      when that device is used for free play (single-player or a same-code
      group), then no purchaser account exists and none is created - account
      creation is purchase-triggered only, never a side effect of playing.
- [x] AC-03: Given a purchaser account is created, when it is inspected, then
      it contains no reference to which players/nicknames used the session at
      purchase time - the account is scoped to "who bought this," not "who
      played this."
- [x] AC-04: Given the purchaser account exists, when
      billing-entitlements/01's session-creation gate runs, then it can read
      "is there an entitled purchaser behind this session" from this account
      without touching player/room data (the seam named in
      accounts-identity/01 AC-02).
- [x] AC-05: Given a purchaser account is created, then the email
      identity is treated as adult data, not child data - no age-of-consent
      flow is triggered for the account itself, because completing a checkout
      is itself evidence the account holder is the purchasing adult, per
      feature.md's "belongs to the buyer, not the kids playing" design note.
- [x] AC-06: Given the purchaser account record, then it is persisted in Azure
      Table Storage (README section 4) and no purchaser secret (password, if
      any auth method uses one) is ever logged or stored in plaintext.

## Out of Scope
- The sign-in / restore-on-a-new-device flow (accounts-identity/03).
- Any identity provider other than magic-link email (OAuth, password) -
  explicitly NOT chosen (ADR 0002 Decision A resolved this on 2026-07-03);
  multi-provider linking stays parked (feature.md, Phase 3+).
- Any account settings UI beyond what checkout itself requires (change email,
  delete account, GDPR export/delete request handling) - a later, separate
  pass.
- Linking multiple purchaser accounts into one household (feature.md,
  Phase 3+).
- The actual Stripe checkout UI/flow (billing-entitlements/03-04 own that);
  this story owns the account record the checkout flow creates or attaches
  to.

## Technical Notes
- **Minimal-auth mechanism resolved: magic-link email** (ADR 0002 Decision A,
  2026-07-03). Purchasers sign in via an emailed one-time link - no password
  stored, no OAuth SDK. This satisfies AC-01 ("email address, nothing more").
  The one-time-token issue/verify plumbing built here is deliberately reused
  by the sys-admin back office for operator login (`sysadmin-console/01`)
  against a separate operator allowlist - so keep the token issuer/verifier a
  reusable service, not inlined into the purchase flow. No purchaser session
  ever grants admin scope.
- New `api/src/Accounts/` folder (mirrors the existing `api/src/Rooms/` and
  `api/src/Safety/` project-per-concern layout): an `Account` record type
  (email address, created-at, no PII beyond that) and an
  `IAccountStore` / `AccountStore` service backed by Azure Table Storage,
  registered as a singleton in `Program.cs` following the existing
  `RoomRegistry` / `ContentSafetyFilter` registration pattern (see
  `api/src/Program.cs`).
- Table Storage partition/row key scheme should key by a hash of the email
  address - never store the account's Table key as a guessable sequential ID,
  since it is effectively a purchaser identifier.
- No secrets belong in `VITE_*` web env vars (CLAUDE.md section 4/6); the
  magic-link token-signing key (and, later, the email-delivery provider's key)
  lives in Azure Key Vault, consistent with billing-entitlements/03's Stripe
  key handling.
- This story does **not** touch `api/src/Rooms/` (accounts-identity/01 AC-05
  guarantees that).
- Web-side: a minimal account-creation surface only appears inside the
  purchase flow (tip jar or gated purchase) - it is not a standalone
  "Sign up" entry point reachable from Home, keeping it out of the kid
  play-flow per feature.md's design notes.

## Tests
| AC | Test |
|---|---|
| AC-01 | `tests/QuibbleStone.Api.Tests/Accounts/AccountStoreTests.cs: creating an account from an email identity persists only email + created-at (CreateOrGet_persists_only_email_and_created_at).` |
| AC-02 | `manual: complete a full free single-player and 2-player round with no purchase attempted - confirm zero rows written to the account table (no auto-create path exists near gameplay). Plus AccountStoreTests: GetByIdentity never creates a row on a miss.` |
| AC-03 | `manual: code read of the Account record type (api/src/Accounts/Account.cs) - confirm it holds only Email + CreatedUtc, no nickname/player/room reference field.` |
| AC-04 | `manual: billing-entitlements/01 build-time integration check - the session-creation gate reads this account via IAccountStore without importing Rooms/ (IAccountStore/Account import nothing from api/src/Rooms).` |
| AC-05 | `manual: code/flow read - confirm no age-gate prompt appears in the purchase-triggered account creation path.` |
| AC-06 | `tests/QuibbleStone.Api.Tests/Accounts/MagicLinkTokenServiceTests.cs (single-use, tamper, expiry, malleability regression) + manual: inspect the Table Storage entity + Key Vault config - confirm the token-signing key is config/Key Vault supplied and no plaintext secret is in the account row or logs.` |

## Dependencies
- accounts-identity/01 (the anonymous-forever contract this account sits
  beside, without touching).
- infra (Table Storage must be provisioned - already true per README section
  9's five-resource footprint).
