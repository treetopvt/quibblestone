# Story: Claim and recovery

**Feature:** Keepsake Vault  ·  **Status:** Not Started  ·  **Issue:** #230

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
      in the gallery (a short, typeable code - see the Technical Notes for the
      chosen length/alphabet - distinct from and not reusing the room
      join-code alphabet's live keyspace) so the family can recover the SAME
      vault - and every tale in it - onto a NEW device by entering the code
      there, which re-attaches that device to the vault id WITHOUT requiring
      an account. Recovery via claim code is the account-free path; claiming
      into a family account (AC-01) is the permanent, cross-device path -
      both exist, serving different needs. The code is carried in the POST
      BODY of the redemption request, never as a URL path segment or query
      parameter (the same bearer-credential rule `keepsake-vault/01` AC-02
      establishes for the vault id itself - a claim code is likewise a bearer
      secret and gets the same treatment).
- [ ] AC-03 (anti-brute-force): Given the claim-code redemption endpoint
      (`POST /api/vault/claim-code/redeem`), then it is protected by THREE
      independent controls, not per-IP rate limiting alone:
      1. **Per-IP rate limit** (as before): mirrors the fixed-window pattern
         `CloudGalleryRateLimit`/`PublishTalesRateLimit`/`keepsake-vault/01`'s
         `VaultRateLimit` already establish.
      2. **A GLOBAL redemption ceiling**, independent of source IP: a single,
         IP-agnostic fixed-window limiter on the redemption endpoint (e.g. a
         fixed low cap on total redemption attempts per minute across ALL
         callers combined) - this is what actually bounds brute force against
         an attacker who rotates source IPs to defeat the per-IP limiter
         above.
      3. **A per-CODE failed-attempt burn**: a vault's currently active claim
         code auto-invalidates once it has accumulated a bounded number of
         failed redemption attempts against it (a named constant - see
         Technical Notes), regardless of which IP(s) those attempts came
         from; the vault's owning device(s) see a freshly rotated code the
         next time they open the gallery.
      Together with the entropy floor (Technical Notes) and the validity
      window/rotation (AC-07), these make the claim code's keyspace
      infeasible to brute-force by any single scripted caller OR a
      distributed, IP-rotating one.
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
- [ ] AC-07 (validity window, rotation, explicit revocation): Given a claim
      code, then it carries a bounded **validity window (default 7 days)**
      from minting - past that window it stops working and a fresh code is
      minted automatically the next time the claiming device's gallery screen
      is opened (auto-rotation; the family always sees a live, working code
      when they look, without needing to notice or act on expiry). A code is
      NOT single-use - it may be redeemed by more than one device within its
      window (recovery is a family-wide need, not a one-time transfer) - but
      the family can EXPLICITLY revoke/regenerate the current code on demand
      from the gallery screen (any device already holding/aliased to the
      vault can do this), immediately invalidating the old code, which
      combines with AC-03's per-code failed-attempt burn to bound how long
      any single code value stays exploitable.

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
    `AccountId`, `ClaimCode`, `ClaimCodeExpiresUtc`, `ClaimCodeFailedAttempts`,
    `ClaimedUtc`) mirroring `TaleModeration`'s "tiny companion row keyed by
    the same partition, mutated independently of the immutable content rows"
    pattern (`api/src/PublishedTales/TaleModeration.cs` /
    `TableStoragePublishedTaleStore`'s `ModerationPartitionKey` sentinel
    scheme) - claiming a vault must never rewrite every tale row in it.
  - `IVaultStore.cs` gains `ClaimAsync(vaultId, accountId)` (mints and
    returns a claim code), `RegenerateClaimCodeAsync(vaultId)` (AC-07's
    explicit revoke/regenerate, callable by any device already holding the
    vault id), `RedeemClaimCodeAsync(claimCode, callingDeviceVaultId)` (the
    simplest shape: redeeming a valid, unexpired, non-burned code makes the
    SERVER treat the calling device's vault id as an alias for the claimed
    vault id, so a later fetch under either id resolves the same tales; a
    failed redemption increments `ClaimCodeFailedAttempts` and, once it hits
    the burn threshold - AC-03.3 - invalidates the code and mints a fresh
    one), and `GetClaimAsync(vaultId)` (whether/how a vault is claimed, for
    the TTL check in AC-05 and for surfacing the live code + its expiry to
    the gallery UI).
  - `VaultController.cs` gains `POST /api/vault/claim` (requires the family
    credential - mirror `CloudGalleryController`'s
    `PurchaserCredentialService`-based auth pattern, generalized to whatever
    `accounts-identity/07` calls its family-account credential; the vault id
    is carried in the `X-Vault-Id` request header, mirroring
    `keepsake-vault/01`'s convention - there is no `{vaultId}` route
    parameter), `POST /api/vault/claim-code/regenerate` (requires the vault
    id via the same header; no account required - this is the account-free
    recovery path's own revoke action), and `POST /api/vault/claim-code/redeem`
    (no account required, claim code carried in the JSON body - never a route
    parameter or query string - rate-limited per AC-03, mirroring
    `VaultRateLimit`'s existing policy shape from story 01, PLUS the new
    global ceiling and per-code burn described in AC-03).
  - **File-footprint hazard**: `keepsake-vault/04` (soft delete/restore) also
    touches `api/src/Vault/IVaultStore.cs`, `TableStorageVaultStore.cs`, and
    `VaultController.cs`. Both stories land in the same ADR 0003 wave (wave
    3) - see this feature's `implementation.md` Wave Plan for the
    serialization call.
- **Claim code security model (recorded, not left undecided)**:
  - **Alphabet/length**: reuse `PublishedTales.SlugGenerator`'s unambiguous
    31-glyph alphabet (`ABCDEFGHJKMNPQRSTUVWXYZ23456789`) and its
    `RandomNumberGenerator.GetInt32`-based unbiased-pick primitive, but at
    **length 9** (not the 12-char published-tale slug, which is tuned for a
    URL nobody types by hand) - a keyspace of 31^9 (~2.6e13). Display grouped
    into three 3-character blocks separated by hyphens for human
    typing/reading (e.g. `K5Q-2NX-8CP`). This alone is not brute-force-proof
    at typeable length - AC-03's three controls (per-IP limit, global
    ceiling, per-code burn) plus AC-07's validity window are what make it
    infeasible in practice, not the keyspace alone.
  - **Per-code failed-attempt burn threshold**: 20 cumulative failed
    redemption attempts against a vault's current code auto-invalidates it
    (AC-03.3) - a named constant, `ClaimCodeFailedAttemptBurnThreshold = 20`.
  - **Global redemption ceiling**: a named constant on the global (non-IP)
    fixed-window limiter, e.g. `GlobalClaimRedemptionCeiling = 60` attempts
    per minute across all callers - tuned loose enough that legitimate
    concurrent family recoveries never hit it, tight enough to make
    distributed guessing across the full keyspace impractical within a
    code's 7-day validity window.
  - This is a NEW, sibling generator to `SlugGenerator` (different length,
    same alphabet and RNG primitive) - do not change `SlugGenerator` itself
    or its published-tale-tuned length/alphabet.
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
| AC-02 | manual: claim a vault, read the shown claim code, enter it on a fresh device/browser profile, confirm the same tales appear there; code review: the redemption request carries the code in the body, no route/query parameter exists |
| AC-03 | xUnit: (1) claim-code redemption is rate-limited per client IP (mirrors `PublishTalesRateLimitTests`'s per-IP partitioning test); (2) a global, IP-agnostic redemption ceiling rejects once exceeded regardless of varying source IPs; (3) a code auto-invalidates after `ClaimCodeFailedAttemptBurnThreshold` cumulative failed attempts and a fresh code is minted |
| AC-04 | code review: no code path attaches a vault claim to anything narrower than an `AccountId` (no kid-profile/seat-preset reference anywhere in `api/src/Vault/`) |
| AC-05 | xUnit: a claimed vault's TTL-expiry check (from story 01) never marks its tales expired, regardless of `CreatedUtc` age |
| AC-06 | code review: `VaultClaim`/the redemption request/response carry no field beyond `VaultId`/`ClaimCode`/`AccountId`/`ClaimedUtc` - no email, no raw identity |
| AC-07 | xUnit: a code past its 7-day validity window is rejected on redemption and a fresh one is minted on next `GetClaimAsync`; a manually regenerated code immediately invalidates the prior one; redeeming the same still-valid code from two different devices both succeed (not single-use) |

## Dependencies
- `keepsake-vault/01` (the vault store this story extends).
- `accounts-identity/07` (the free family account - hard gate; must exist
  before this story can start).
- `accounts-identity/05` (the stable `AccountId` spine - transitive hard
  gate: `07`'s family account is built on it).
