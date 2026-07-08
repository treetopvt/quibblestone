# Story: Claim and recovery

**Feature:** Keepsake Vault  ·  **Status:** Not Started  ·  **Issue:** #TBD

## Context
A vault (`keepsake-vault/01`) is anonymous and device-bound by default: it
survives a cleared browser or the local gallery's cap, but it still lives or
dies with one device and, unclaimed, expires on its TTL. ADR 0003's Decision
2 names the durability upgrade: claiming a vault into a free family account
(`accounts-identity/07`) makes its tales permanent and visible from any
device signed into that account, and a human-friendly claim code lets a
family recover the SAME vault onto a new device even without an account.
This story's claim also **supersedes `keepsake-gallery/05`'s manual
purchaser cloud-sync-and-upload flow over time** (recorded in
`feature.md`'s Design notes; `keepsake-gallery/05`'s story file is not
reopened or edited by this story). Per ADR 0003's kid-profile boundary
(Decision 1), a claimed vault's tales are tied to the FAMILY, never to an
individual kid seat preset. See [feature.md](./feature.md) and
[ADR 0003](../../adr/0003-admin-platform-and-family-accounts.md) (Decision 2,
Decision 1's kid-profile boundary).

This story is **GATED**: it cannot start until `accounts-identity/07` (the
free family account) exists, which itself depends on `accounts-identity/05`
(the stable `AccountId` spine) - so `05` is a transitive hard gate here even
though this story only calls `07` directly.

## Acceptance Criteria
- [ ] AC-01: Given a signed-in family account (`accounts-identity/07`) on a
      device holding a vault id, when they claim that vault, then the vault's
      tales become permanently associated with the family account (no TTL -
      see AC-05) and are visible from ANY device subsequently signed into that
      same family account, not just the claiming device.
- [ ] AC-02: Given a claimed vault, then a human-friendly claim code is shown
      in the gallery (a short, typeable alphanumeric code, distinct from and
      not reusing the room join-code alphabet's live keyspace) so the family
      can recover the SAME vault - and every tale in it - onto a NEW device by
      entering the code there, which re-attaches that device to the vault id
      WITHOUT requiring an account. Recovery via claim code is the account-free
      path; claiming into a family account (AC-01) is the permanent,
      cross-device path - both exist, serving different needs.
- [ ] AC-03 (anti-abuse / rate limit): Given the claim-code redemption
      endpoint, then attempts are rate-limited per client IP - mirroring the
      per-IP fixed-window pattern `CloudGalleryRateLimit`/`PublishTalesRateLimit`/
      `keepsake-vault/01`'s `VaultRateLimit` already establish - so the claim
      code's keyspace cannot be brute-forced by a scripted caller.
- [ ] AC-04 (child-safety / kid-profile boundary): Given a claimed vault's
      tales, then they are tied to the FAMILY account only, NEVER to an
      individual kid profile or seat preset - there is no per-kid gallery and
      no per-kid tale history, ever (ADR 0003 Decision 1, the firm,
      non-negotiable edge). If a future idea wants per-kid anything, that
      requires its own ADR, not a slide in this story.
- [ ] AC-05: Given an unclaimed vault approaching or past its TTL
      (`keepsake-vault/01`, default 90 days), when it is claimed into a
      family account, then that TTL no longer applies - a claimed vault's
      tales do not expire; claiming is the durability upgrade path the whole
      feature exists to offer.
- [ ] AC-06 (no PII): Given a claim code, then it is an opaque, unguessable
      handle that carries no identity of its own - redeeming it only ever
      re-links a vault id to whichever device enters it; it is never an
      email, an account id, or any other PII, and the redemption endpoint
      itself requires no account.

## Out of Scope
- Building the free family account (`accounts-identity/07`) or the stable
  `AccountId` spine (`accounts-identity/05`) - this story only consumes both
  once they exist (hard gate, see the Context note above).
- The family device link mechanism (`accounts-identity/09`) - a separate
  mechanism for a kid's OWN device to carry family entitlements into
  `CreateRoom`; unrelated to vault claiming, which is about keepsake content,
  not session entitlements.
- Per-kid galleries or history of any kind - explicitly forbidden by ADR 0003
  Decision 1 (see AC-04); not a "later" item, a firm boundary.
- Editing `keepsake-gallery/05`'s manual purchaser cloud-sync story or code -
  this story's claim supersedes its ROLE over time (recorded in
  `feature.md`), but does not touch its file or its shipped implementation.
- Un-claiming / transferring a claimed vault to a different family account -
  a reasonable follow-up, not required for this story; note as a candidate
  small addition if it comes up.
- Merging TWO vaults (e.g. two devices that each independently accumulated
  tales before ever being linked) into one on claim - out of scope; claiming
  attaches a family account to ONE vault id, it does not consolidate
  multiple.

## Technical Notes
- Extend `api/src/Vault/` (from story 01) rather than inventing a second
  vault concept:
  - Claim state is a property of the VAULT as a whole, not of individual
    tales - add a small companion record (e.g. `VaultClaim.cs`: `VaultId`,
    `AccountId`, `ClaimCode`, `ClaimedUtc`) mirroring `TaleModeration`'s
    "tiny companion row keyed by the same partition, mutated independently
    of the immutable content rows" pattern
    (`api/src/PublishedTales/TaleModeration.cs` /
    `TableStoragePublishedTaleStore`'s `ModerationPartitionKey` sentinel
    scheme) - claiming a vault must never rewrite every tale row in it.
  - `IVaultStore.cs` gains `ClaimAsync(vaultId, accountId)` (mints and
    returns a claim code), `RedeemClaimCodeAsync(claimCode, newVaultId)` (or
    however the "re-attach this device's vault id" mechanic is expressed -
    the simplest shape is: redeeming a code makes the SERVER treat the
    calling device's vault id as an alias for the claimed vault id, so a
    later fetch under either id resolves the same tales), and
    `GetClaimAsync(vaultId)` (whether/how a vault is claimed, for the TTL
    check in AC-05).
  - `VaultController.cs` gains `POST /api/vault/{vaultId}/claim` (requires
    the family credential - mirror `CloudGalleryController`'s
    `PurchaserCredentialService`-based auth pattern, generalized to whatever
    `accounts-identity/07` calls its family-account credential) and
    `POST /api/vault/claim-code/redeem` (no account required, rate-limited
    per AC-03, mirroring `VaultRateLimit`'s existing policy shape from
    story 01).
  - **File-footprint hazard**: `keepsake-vault/04` (soft delete/restore) also
    touches `api/src/Vault/IVaultStore.cs`, `TableStorageVaultStore.cs`, and
    `VaultController.cs`. Both stories land in the same ADR 0003 wave (wave
    3) - see this feature's `implementation.md` Wave Plan for the
    serialization call.
- **Claim code generation**: reuse the SHAPE of `PublishedTales.SlugGenerator`'s
  approach (an unambiguous-glyph alphabet, `RandomNumberGenerator`-minted,
  never sequential) but at a length/alphabet tuned for a HUMAN TO TYPE from a
  screen onto a different device (shorter and more forgiving than the
  12-char, 31-glyph published-tale slug, which is tuned for a URL nobody
  types by hand) - record the chosen length/alphabet here once decided,
  following the same "chosen values, recorded per the story process"
  precedent `keepsake-gallery/04`'s Technical Notes set.
- **Family credential**: this story depends on whatever credential shape
  `accounts-identity/07` mints for a signed-in family account - reuse it
  exactly (mirroring how `CloudGalleryController` reuses
  `PurchaserCredentialService` rather than inventing a second auth scheme).
  Do not build a parallel family-identity concept here.
- **Web**: the gallery screen (extended by `keepsake-vault/02`) gains a
  "claim this vault" affordance (shown only when signed into a family
  account) and displays the claim code plus a "recover a vault" code-entry
  affordance (shown to any device, signed in or not) - both purchaser/family-
  facing surfaces, never shown or hinted at to a session that never touches
  a family credential (mirrors the auth-boundary invariant
  `keepsake-gallery/05` already established: the child-facing reveal never
  touches a purchaser/family credential).

## Tests
| AC | Test |
|---|---|
| AC-01 | xUnit + manual: claim a vault under a family account; fetch its tales from a second device signed into the same account; confirm they appear |
| AC-02 | manual: claim a vault, read the shown claim code, enter it on a fresh device/browser profile, confirm the same tales appear there |
| AC-03 | xUnit: claim-code redemption is rate-limited per client IP (mirrors `PublishTalesRateLimitTests`'s per-IP partitioning test) |
| AC-04 | code review: no code path attaches a vault claim to anything narrower than an `AccountId` (no kid-profile/seat-preset reference anywhere in `api/src/Vault/`) |
| AC-05 | xUnit: a claimed vault's TTL-expiry check (from story 01) never marks its tales expired, regardless of `CreatedUtc` age |
| AC-06 | code review: `VaultClaim`/the redemption request/response carry no field beyond `VaultId`/`ClaimCode`/`AccountId`/`ClaimedUtc` - no email, no raw identity |

## Dependencies
- `keepsake-vault/01` (the vault store this story extends).
- `accounts-identity/07` (the free family account - hard gate; must exist
  before this story can start).
- `accounts-identity/05` (the stable `AccountId` spine - transitive hard
  gate: `07`'s family account is built on it).
