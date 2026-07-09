# Story: Stable account id spine

**Feature:** Accounts & Identity  ·  **Status:** Complete  ·  **Issue:** #195

## Context
An audit ahead of [ADR 0003](../../adr/0003-admin-platform-and-family-accounts.md)
found that `PurchaserAccounts`, `EntitlementGrants`, and `CloudGalleryTales` are
all keyed by a SHA-256 hash of the purchaser's email (`AccountIdentity.KeyFor`,
accounts-identity/02). That means an email change orphans a purchaser's own
grants and gallery, and there is no stable id an operator (or a future support
tool) can hand back to a customer. ADR 0003's Layer 0 ("identity spine") fixes
this first, before anything else in the platform build lands on top of it: mint
a durable `AccountId` (GUID) once, at account creation, and make email a
mutable login attribute rather than the account's identity. This is the
FOUNDATION story for `accounts-identity/06-09` and for `keepsake-vault` and
`control-plane` (ADR 0003, "Cross-feature build order", Wave 1). See
[feature.md](./feature.md).

## Acceptance Criteria
- [x] AC-01: Given an account is created (the existing `IAccountStore.CreateOrGetAsync`
      purchase path, or accounts-identity/07's new free sign-up path), then it is
      assigned a stable, randomly generated `AccountId` (a GUID) at creation time,
      in addition to its email - the `AccountId` never changes for the life of the
      account, regardless of any later email change.
- [x] AC-02: Given the `Account` record, then `Email` is treated as a MUTABLE
      login attribute (a value that can be updated in place without disturbing
      anything else), not the account's identity - `AccountId` is the durable key
      everything else keys off. This story does not build an actual
      change-email endpoint (Out of Scope); it only makes the record's shape
      support one without a future re-key.
- [x] AC-03: Given `EntitlementGrant` rows (billing-entitlements/01) and
      `CloudTale` rows (keepsake-gallery/05), when they are written or read, then
      they are keyed by `AccountId` - never by a hash of email. A lookup that
      starts from an email (a magic-link verify, a purchaser credential) resolves
      to the `Account` FIRST via the existing email-hash index, then reads/writes
      grants and gallery rows by that account's `AccountId`.
- [x] AC-04: Given `IAccountStore`, then it exposes a lookup BY `AccountId`
      (in addition to the existing by-email lookup) so a caller that already
      holds an `AccountId` (a resolved family-device token, story 09; a future
      support/claim lookup, `keepsake-vault`) never needs to round-trip through
      an email to find the account.
- [x] AC-05: Given UAT holds a small number of pre-ADR-0003 rows keyed under the
      OLD email-hash scheme, when this story ships, then those rows are NOT
      required to survive - a one-time reset (dropping/recreating the three
      tables, or a short manual re-seed) is an accepted migration path, because
      near-zero real data exists there today (a toy, not a system of record,
      README section 4). This is a deliberate, documented choice, not silent
      data loss of anything that matters.
- [x] AC-06: Given `StoredValueEntitlementService`, `CloudGalleryController`, and
      any other consumer that resolves "which account does this identity belong
      to", then they all resolve the SAME `AccountId` for the same email - there
      is no code path left that still partitions by the old email-hash scheme
      while another uses `AccountId` (no split-brain keying).
- [x] AC-07 (no new PII): Given the new `AccountId`, then it is a GUID carrying
      no PII by itself and introduces no new identifying field beyond what
      accounts-identity/02 already established (email + created-at, now plus
      this id) - README section 6's minimal-data posture is unchanged.

## Out of Scope
- An actual "change your email" endpoint/UI - this story only makes the record
  shape support one later (AC-02); building it is a separate, future story.
- A real, general-purpose data-migration tool or dual-write/dual-read
  compatibility shim for old rows - AC-05 explicitly accepts a one-time reset
  given the near-zero real UAT data.
- Any change to `Room`/`Player` or the hub - this story is entirely within
  `api/src/Accounts/`, `api/src/Entitlements/`, and `api/src/CloudGallery/`.
- `accounts-identity/06`'s hub-connection wiring, `07`'s free-account sign-up
  flow, `08`'s presets, or `09`'s device link - this story only lays the keying
  foundation those build on.
- Any operator/support-facing lookup UI (that is `sysadmin-console`'s job,
  consuming AC-04's by-id lookup later).

## Technical Notes
- **`Account` record:** add `Guid Id` to `api/src/Accounts/Account.cs`
  (`Account(Guid Id, string Email, DateTimeOffset CreatedUtc)`). Every existing
  construction site (`TableStorageAccountStore`, `InMemoryAccountStore`, their
  tests) needs the new argument.
- **Store shape (a primary row + a slim index row):** `TableStorageAccountStore`
  moves from "PartitionKey = RowKey = hash(email)" to a two-row pattern:
  - A PRIMARY entity keyed `PartitionKey = "account"`, `RowKey = accountId`
    (the GUID as a string), holding `Email` + `CreatedUtc`.
  - A slim INDEX entity keyed `PartitionKey = "emailIndex"`,
    `RowKey = AccountIdentity.KeyFor(email)` (the existing hash), holding only
    `AccountId`.
  `CreateOrGetAsync` reads the index first (a miss means a new account: mint a
  `Guid`, write the primary row, then the index row); `GetByIdentityAsync`
  reads the index, then point-reads the primary row by the resolved id. Handle
  the same "lost the create race" 409 recovery the current implementation
  already does, now on the index row. `InMemoryAccountStore` mirrors this with
  two dictionaries (or one dictionary keyed by `AccountId` plus an email->id
  index) so both implementations behave identically.
- **`IEntitlementGrantStore` / `TableStorageEntitlementGrantStore` /
  `InMemoryEntitlementGrantStore`:** change the partition key from
  `AccountIdentity.KeyFor(purchaserIdentity)` to the account's `Id.ToString()`.
  Since an `AccountId` is already a random, unguessable GUID, it can be the
  partition key directly - no further hashing needed (unlike a raw email).
  `GetGrantsAsync`/`PutGrantAsync`'s parameter can stay a `string` (pass
  `account.Id.ToString()`) or be strengthened to a `Guid accountId` - prefer the
  `Guid` overload since it is now the true identity type; update
  `StoredValueEntitlementService` to resolve the `Account` first (as it already
  does) and pass `account.Id` instead of `account.Email`.
- **`CloudTale.OwnerKey` / `TableStorageCloudGalleryStore` /
  `InMemoryCloudGalleryStore` / `CloudGalleryController`:** the SAME change -
  `OwnerKey` becomes `account.Id.ToString()` rather than
  `AccountIdentity.KeyFor(account.Email)`. Update the four call sites in
  `CloudGalleryController.cs` (list, save, delete, delete-all) that currently
  compute `ownerKey` from the hash.
- **`AccountIdentity.cs`** stays: `KeyFor`/`Normalize` are still exactly the
  right tool for the EMAIL INDEX row's key - just no longer the primary key for
  grants/gallery. Update its header comment to say so explicitly, matching the
  verbose-header-comment convention already in the file.
- **Program.cs:** no NEW service registrations are expected (the same
  interfaces are registered); this is a keying/shape change inside existing
  implementations, not a new seam. Confirm no registration needs to change.
- **Gotcha:** every existing unit test that constructs an `Account(...)` with
  the old 2-argument shape will need updating for the new 3-argument record -
  flag this for the testing-agent rather than silently working around it with
  optional-parameter defaults that would hide the shape change.

## Tests
| AC | Test |
|---|---|
| AC-01 | `tests/QuibbleStone.Api.Tests/Accounts/AccountStoreTests.cs: creating an account assigns a non-empty, stable AccountId; creating twice for the same email returns the SAME AccountId (idempotent).` |
| AC-02 | `manual: code read of Account.cs - confirm Email is a plain settable-in-spirit field with no key derivation baked into equality/identity beyond AccountId.` |
| AC-03 | `tests/QuibbleStone.Api.Tests/EntitlementGrantStoreTests.cs (Two_accounts_never_collide) + tests/QuibbleStone.Api.Tests/CloudGalleryControllerTests.cs (end-to-end owner-key keying through the controller): writing a grant/tale for an account and reading it back resolves by AccountId; two accounts never collide even if a future email were reused after a hypothetical change.` |
| AC-04 | `tests/QuibbleStone.Api.Tests/Accounts/AccountStoreTests.cs (GetById_ResolvesTheSameAccountAsGetByIdentity, GetById_ReturnsNullOnMiss_AndNeverCreates): GetByIdAsync(accountId) returns the same account GetByIdentityAsync(email) does.` |
| AC-05 | `manual: docs/runbooks/reset-account-tables-for-account-id-spine.md records the one-time UAT reset for this story, confirming no expectation of preserved rows.` |
| AC-06 | `manual: code read across StoredValueEntitlementService, CloudGalleryController - confirm every keying call site uses account.Id, none still calls AccountIdentity.KeyFor(account.Email) for a grant/gallery partition.` |
| AC-07 | `manual: inspect the Account record and the Table Storage schema - confirm AccountId is the only new field and it carries no PII.` |

## Dependencies
- none new. This story re-keys the already-Complete accounts-identity/02-04,
  billing-entitlements/01, and keepsake-gallery/05 stores in place; it needs no
  new infrastructure beyond what they already provisioned (Azure Table
  Storage).
