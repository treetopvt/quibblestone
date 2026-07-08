# Implementation Plan: Keepsake Vault

> The bridge between planning and orchestration. The **Wave Plan** below is DAG-ready: the `orchestrate-feature`
> skill's Phase 1 validates and adjusts it rather than deriving it. See
> [`docs/FEATURE_ORCHESTRATION_PLAYBOOK.md`](../../FEATURE_ORCHESTRATION_PLAYBOOK.md).

ADR 0003 Layer 2, decomposed. Story 01 is the foundation everything else in this feature (and story 03's
family-account claim, once its external gate lands) builds on: a new, isolated `api/src/Vault/` namespace mirroring
`api/src/CloudGallery/`'s shape and config-presence idiom, plus a device-held vault id and a fire-and-forget web
save call. Stories 02-04 then extend it in file-disjoint-where-possible directions - see the per-story footprints
and the flagged hazards below.

## Reuse map

| Concern | Reuse | Where |
|---|---|---|
| Owner-keyed Table Storage store shape (`PartitionKey`/`RowKey`, `PartsJson` blob, ensure-table-once guard) | `TableStorageCloudGalleryStore`'s pattern | `api/src/CloudGallery/TableStorageCloudGalleryStore.cs` |
| Config-presence store split (real store when a connection string is configured, a genuinely WORKING in-memory fallback otherwise - not a no-op) | `ICloudGalleryStore` / `InMemoryCloudGalleryStore` | `api/src/CloudGallery/ICloudGalleryStore.cs`, `InMemoryCloudGalleryStore.cs` |
| The general SHAPE of lazy expiry-on-read (an expired row reads as gone, best-effort reclaim delete) - reused as a CONCEPT only, NOT as an exact mirror | `PublishedTale.IsExpired` / `TableStoragePublishedTaleStore.GetAsync` | `api/src/PublishedTales/PublishedTale.cs`, `TableStoragePublishedTaleStore.cs` - **note the vault's own expiry shape differs**: `GetAsync` stores `ExpiresUtc` and checks one row by slug; the vault has no single-slug read, so it computes `CreatedUtc + TtlDays` at read time over a `ListAsync` partition query instead (see story 01) |
| Tiny mutable companion-row pattern (a report/claim/deletion signal keyed by the SAME id, never rewriting the immutable content row) | `TaleModeration` + its `ModerationPartitionKey` sentinel scheme | `api/src/PublishedTales/TaleModeration.cs`, `TableStoragePublishedTaleStore.cs` |
| Server-side re-vet of every part + byline before storing (client word/literal classification never trusted); server-STAMPED `CreatedUtc` (never accepted from the client) | `CloudGalleryController.Save` / `PublishedTalesController`'s publish path - note `SaveCloudTaleRequest` has no `CreatedUtc` field; the controller stamps `DateTimeOffset.UtcNow` itself | `api/src/CloudGallery/CloudGalleryController.cs`, `api/src/PublishedTales/PublishedTalesController.cs` |
| Per-IP fixed-window rate limiting on an anonymous endpoint - applied to BOTH reads and writes in this feature (the existing precedents only rate-limit their write endpoint; the vault's read endpoint is anonymous and bearer-credential-gated too, so it needs the same protection - see story 01 AC-06) | `CloudGalleryRateLimit` / `PublishTalesRateLimit` (ASP.NET Core's built-in limiter, no new dependency) | `api/src/CloudGallery/CloudGalleryRateLimit.cs`, `api/src/PublishedTales/PublishTalesRateLimit.cs` |
| Opaque, cryptographically random, never-identity-derived handle minting - NO weak/`Math.random` fallback when the handle is itself a bearer credential | `Room.NewReconnectToken()` (`RandomNumberGenerator`) | `api/src/Rooms/Room.cs` |
| Short, human-shareable, unguessable id minting | `SlugGenerator` (unambiguous-glyph alphabet, `RandomNumberGenerator.GetInt32`) - the claim code (story 03) is a NEW, shorter-length sibling generator over the same alphabet/RNG primitive, not a `SlugGenerator` edit | `api/src/PublishedTales/SlugGenerator.cs` |
| Purchaser/family credential auth pattern (bearer header + cookie fallback, resolved once per request) | `PurchaserCredentialService` reused by `CloudGalleryController` | `api/src/Accounts/PurchaserCredentialService.cs` |
| Stable, non-PII owner keying | `AccountIdentity.KeyFor` | `api/src/Accounts/AccountIdentity.cs` |
| Local gallery storage, cap/eviction, and the `cloudTaleId`-style sync-stamp precedent - **the `generateTaleId()` `Math.random` fallback is explicitly NOT reused for the vault id** (a local tale row id is not a credential; a vault id is) | `localGallery.ts`'s `GalleryAdapter`/`TaleMeta`/`saveTale`/`cloudTaleId` | `web/src/gallery/localGallery.ts` |
| Fire-and-forget save call site on reveal completion | `handleSaveImage`'s existing `saveTale()` call | `web/src/pages/Reveal.tsx` (~line 943-980) |
| Endpoint-call-with-graceful-failure client pattern | `publishTale.ts` (`publishTale`/`revokeTale`, feature-detect + swallow) | `web/src/gallery/publishTale.ts` |
| Child safety | the single server-side safety filter | `api/src/Safety/IContentSafetyFilter.cs` |
| Config | `import.meta.env` (`VITE_*`) for the API base URL | `web/src/vite-env.d.ts` |
| Sensitive-key scrubbing allowlist (telemetry must know the vault id/claim code are secrets) | `PiiScrubbingTelemetryInitializer`'s `SensitivePropertyKeys` - add `vaultId`/`claimCode` per ADR 0003's "Security posture" telemetry finding | `api/src/Telemetry/PiiScrubbingTelemetryInitializer.cs` (exact path per that class - a small, additive edit any story here may need to make) |

What this feature **exports** that others might import later: `IVaultStore` (a stable seam
`sysadmin-console/07`'s support-lookup verbs will call for vault/tale counts and the restore verb), the claim-code
concept (`sysadmin-console/07`'s "lookup by ... claim code" mirrors story 03's redemption logic), and
`web/src/vault/vaultId.ts` (the device-held vault id any future vault-aware surface reads).

## Wave Plan (DAG)

Sizing rule: a builder owns files that are **disjoint** from its concurrent siblings. Story 01 is the foundation
every other story depends on; it also carries the feature's one truly systemic hazard (`Program.cs`).

| Story | Issue | Files it owns (footprint) | Depends-on | Can-run-with | Wave | Effort |
|---|---|---|---|---|---|---|
| 01 (foundation) | #TBD | new `api/src/Vault/` (`VaultTalePart.cs`, `VaultTale.cs`, `IVaultStore.cs`, `TableStorageVaultStore.cs`, `InMemoryVaultStore.cs`, `VaultRateLimit.cs`, `VaultController.cs` with `POST /api/vault/tales`, `GET /api/vault/tales`, `POST /api/vault/mint` - all vault-id-bearing calls read `X-Vault-Id` from a HEADER, never a route segment); edits `api/src/Program.cs` (service registration); new `web/src/vault/vaultId.ts` (crypto-only mint, no `Math.random` fallback), `web/src/vault/vaultClient.ts`; edits `web/src/pages/Reveal.tsx` (adds the fire-and-forget vault save call) | - | none within this feature (external ADR-0003 wave-1 siblings below, no file overlap) | 1 | high |
| 02 | #TBD | new `web/src/vault/vaultGallery.ts` (merge logic); edits `web/src/gallery/localGallery.ts` (adds `vaultTaleId?` to `TaleMeta`, mirroring `cloudTaleId`); edits `web/src/pages/Gallery.tsx` (swaps the data-loading call, no visual change) | 01 | - | 2 | medium |
| 03 | #TBD | edits `api/src/Vault/` (`VaultClaim.cs` new; new `ClaimCodeGenerator.cs` - a sibling to `SlugGenerator`, length 9 over its alphabet; adds claim methods to `IVaultStore.cs`/`TableStorageVaultStore.cs`/`InMemoryVaultStore.cs`; adds `POST /api/vault/claim`, `POST /api/vault/claim-code/regenerate`, `POST /api/vault/claim-code/redeem` to `VaultController.cs` - vault id via `X-Vault-Id` header, claim code via JSON body, never a route segment); edits `web/src/vault/` (claim + redeem client calls); edits `web/src/pages/Gallery.tsx` (claim/recover affordances) | 01, accounts-identity/07 (external hard gate; transitively accounts-identity/05) | - (serialize with 04, see note below) | 3 | medium-high |
| 04 | #TBD | edits `api/src/Vault/` (adds soft-delete/restore methods to `IVaultStore.cs`/`TableStorageVaultStore.cs`/`InMemoryVaultStore.cs`; adds `DELETE /api/vault/tales/{taleId}` to `VaultController.cs`, vault id via `X-Vault-Id` header); edits `api/src/PublishedTales/TableStoragePublishedTaleStore.cs` + `IPublishedTaleStore.cs` (`ConfirmHiddenAsync` becomes a soft-delete; adds a distinctly-named, confirmation-gated `RestoreFromTakedownAsync`) | 01 | - (serialize with 03, see note below) | 3 | medium |

**Concurrency per wave (Wave numbers here are the ADR 0003 canonical numbers - 01=Wave 1, 02=Wave 2, 03+04=Wave 3 -
do not renumber locally):** Wave 1 = 01 alone (the foundation; also the ADR 0003 wave-1 `Program.cs` hotspot - see
below). Wave 2 = 02 alone (file-disjoint from 01's remaining scope, but there is nothing else in this feature's
wave 2 to run it alongside). Wave 3 = {03, 04} are the ADR's own cross-feature wave 3 pairing for this feature, but
they are **NOT safely parallel within this feature**: both edit `api/src/Vault/IVaultStore.cs`,
`TableStorageVaultStore.cs`, `InMemoryVaultStore.cs`, and `VaultController.cs`. **Serialize them** - land whichever
is ready first as a small PR, rebase the other on top - rather than running them as a true parallel pair, even
though the ADR's cross-feature table lists them in the same wave (that table governs cross-feature ordering, not
this feature's own internal file safety).

**Cross-feature Wave-3 hazard, `keepsake-vault/04` vs `control-plane/03` (from ADR 0003's Wave 3 row):** both stories
touch `api/src/PublishedTales/` - `04` changes `TableStoragePublishedTaleStore.ConfirmHiddenAsync` from a hard
delete to a soft-delete (a semantic behavior change to an existing method); `control-plane/03` migrates
`AutoHideThreshold` to a settings key and may touch `ReportedTalesController.cs` (a surface, constant-swap change).
**Order: land `keepsake-vault/04` first.** Its change is the larger, semantic one; rebasing the smaller
settings-key swap (`control-plane/03`) on top of it is far cheaper than the reverse (which would require
`control-plane/03` to be re-validated against a store method whose delete behavior changed out from under it). This
is a cross-FEATURE ordering call - `control-plane`'s own implementation.md should carry the mirror image of this
note once that feature is decomposed.

**The ADR 0003 wave-1 `Program.cs` hazard (load-bearing, cross-feature):** story 01's `Program.cs` edit
(registering `IVaultStore` connection-string-gated) is one of FIVE ADR 0003 wave-1 service registrations landing
around the same time: `accounts-identity/05` (AccountId spine), `keepsake-vault/01` (this story), `control-plane/01`
(settings service), `sysadmin-console/04` (auth unification), and `platform-devops/08` (durable key ring). (ADR
0003 Decision 4's second environment is NOT a wave-1 story - `main`'s shipped `platform-devops/07` QA lane delivers
it.) Per the ADR: "all register services in `Program.cs` - land
as separate small PRs, rebase serially; do not batch." This story's `Program.cs` edit must be its own small,
reviewable diff, merged and rebased serially against whichever of those other four lands around the same time -
never batched into one larger `Program.cs` change alongside them.

## Per-story tech notes

### 01 - The vault: auto-save every completed reveal server-side
**Approach:** a new, isolated `api/src/Vault/` namespace mirroring `api/src/CloudGallery/`'s shape almost exactly
(owner-key -> vault-id, `CloudTale` -> `VaultTale`), but with the `InMemoryCloudGalleryStore` config-presence idiom
(a genuinely working fallback, not `PublishedTales`' disabled-no-op) since the vault is a default-on, anonymous,
every-player surface rather than an opt-in growth feature. `Reveal.tsx`'s existing fire-and-forget `saveTale()` call
site (~line 943-980) gains a sibling fire-and-forget vault-save call - same posture, same place, never blocking the
download. **Owns / exports:** `IVaultStore` (the seam stories 02-04 and, later, `sysadmin-console/07` build on),
`web/src/vault/vaultId.ts` (the durable device handle). **Gotchas (per the 2026-07-08 adversarial review):** the
vault id is a bearer credential - it travels on the `X-Vault-Id` HEADER on every endpoint, never a route segment
(App Insights/access-log/`Referer`/history leakage otherwise); BOTH the read and write endpoints are rate-limited,
not just the write; the client mints it with `crypto.randomUUID()` ONLY (no `Math.random` fallback - a
crypto-unavailable device calls the new `POST /api/vault/mint` instead) and the server independently rejects any
client-presented id below a length/format floor; a per-vault `MaxTalesPerVault` cap (500) bounds storage growth
independent of the per-IP limiter (which IP rotation alone defeats); `CreatedUtc` is server-stamped, never accepted
from the client (the TTL keys off it on an anonymous, abusable endpoint); the TTL is a COMPUTED
`CreatedUtc + TtlDays` check applied on the `ListAsync` partition-query path (not a stored `ExpiresUtc`, and not an
exact mirror of `TableStoragePublishedTaleStore.GetAsync`'s single-slug shape). The `Program.cs` hazard above - land
that edit as its own small PR. TTL (90 days) and `MaxTalesPerVault` (500) are code constants until `control-plane`
exists; do not block on it.

### 02 - The device gallery becomes a view over the vault
**Approach:** a pure merge function over two already-fetched arrays (local `TaleMeta[]` + vault `GET` result),
mirroring `talesToEvict`'s "no IndexedDB, no async, directly unit-testable" precedent in `localGallery.ts`. Render
the local list first (fast, synchronous), reconcile with the vault fetch when it resolves or fails - never block
initial paint on the network. **Owns / exports:** the merge module; a `vaultTaleId?` stamp on `TaleMeta`. **Gotchas:**
no visual change to `Gallery.tsx` - this is a data-source swap only. Out of scope: claim/recovery UI (03),
soft-delete/restore UI (04).

### 03 - Claim and recovery
**Approach:** claim state lives in a tiny companion record (`VaultClaim`) keyed like `TaleModeration`'s sentinel-
partition pattern, never rewriting every tale row on claim. Claim requires the family credential
`accounts-identity/07` mints (reused exactly, mirroring `CloudGalleryController`'s reuse of
`PurchaserCredentialService` - do not invent a second auth scheme); claim-code redemption requires no account.
**Owns / exports:** the claim + redeem endpoints and their store methods; `ClaimCodeGenerator` (a new, sibling
generator to `SlugGenerator` - length 9, same 31-glyph alphabet and RNG primitive, NOT an edit to `SlugGenerator`
itself). **Gotchas (per the 2026-07-08 adversarial review):** hard-gated on `accounts-identity/07` (transitively
`/05`) - cannot start before that lands. **Serialize with 04** within this feature (see Wave Plan note) - both touch
`IVaultStore.cs`/`TableStorageVaultStore.cs`/`VaultController.cs`. The claim code is a bearer secret exactly like
the vault id: carried in the request BODY on redemption (never a route segment or query string), protected by
THREE anti-brute-force controls (a per-IP limiter, a GLOBAL IP-agnostic redemption ceiling, and a per-code
failed-attempt burn/auto-rotate), and given a 7-day validity window with auto-rotation plus an explicit
regenerate/revoke action - not single-use (recovery is a family-wide, repeatable need). Alphabet/length (9 chars,
`SlugGenerator`'s alphabet) and every numeric threshold (burn = 20, global ceiling = 60/min, window = 7 days) are
recorded in the story file, not left as open questions for the builder.

### 04 - Soft delete and restore
**Approach:** vault tale deletion gets a soft-delete marker (a flipped flag on a rebuilt immutable record, or a
`TaleModeration`-style companion row - choose and record which); the published-tale takedown path
(`TableStoragePublishedTaleStore.ConfirmHiddenAsync`) stops hard-deleting the tale body and instead marks it
soft-deleted within the same restore-window model. Expiry of the restore window is lazy-on-read, mirroring
`PublishedTale.IsExpired`'s existing idiom (the same one story 01 uses for vault-TTL expiry) - no reaper job in this
story. **Owns / exports:** `RestoreAsync`-shaped methods on `IVaultStore` and a distinctly-named, confirmation-gated
`RestoreFromTakedownAsync` method on `IPublishedTaleStore` (do not collide names with the EXISTING moderation
`RestoreAsync`, which un-hides a never-body-deleted tale - a different operation). **Gotchas (per the 2026-07-08
adversarial review):** name the two restore concepts (un-hide vs un-delete) so an operator-console reader cannot
confuse them; a takedown restore carries materially higher risk than a player's own delete restore (it re-exposes
previously reported/hidden content), so `RestoreFromTakedownAsync`'s SIGNATURE requires an explicit confirmation
argument that `IVaultStore.RestoreAsync` has no equivalent of - a structural, not documentation-only, distinction
(`sysadmin-console/07` supplies it after its own confirmation UX); `CloudGallery`'s hard-delete is explicitly OUT of
scope (an easy story to over-reach into); **serialize with 03** within this feature (see Wave Plan note); also
touches `api/src/PublishedTales/` where `control-plane/03` (knob migration) lands in the SAME cross-feature wave -
**land 04 first** (its change to `ConfirmHiddenAsync` is the larger, semantic one; `control-plane/03`'s
constant-to-settings-key swap rebases on top far more cheaply than the reverse) - see the Wave Plan's cross-feature
hazard note above.

## Cross-cutting concerns

- **This feature persists and recovers; it does not collect, assemble, or moderate-decide.** Every story consumes
  an already-assembled, already-filtered tale (`the-reveal`, `keepsake-gallery/01`). If a story here seems to need
  new engine logic, a new free-text entry point, or a change to the existing moderation report/auto-hide threshold
  logic, that is scope creep out of this feature's lane - flag it rather than build it.
- **No PII, ever.** A vault id and a claim code are both opaque, random handles - never derived from or joined to
  an email, device fingerprint, IP address, or any other identity. Nothing this feature builds lands on `Room` or
  `Player` (`api/src/Rooms/Room.cs`'s "IDENTITY CONTRACT" header comment is the standing guard, unchanged by this
  feature).
- **Handles are bearer credentials, treated as secrets (added 2026-07-08 per the adversarial review).** The vault id
  and the claim code both grant possession-based access (read/write for the vault id; claim/alias for the claim
  code). Every endpoint in this feature therefore: (a) carries the handle in a request HEADER or BODY, never a URL
  path segment or query parameter (`PiiScrubbingTelemetryInitializer` only strips the query string - a path segment
  leaks to App Insights, access logs, `Referer`, and browser history); (b) rate-limits reads exactly like writes,
  not writes alone; (c) mints with a real crypto entropy floor (`crypto.randomUUID()` client-side, no `Math.random`
  fallback, with a server-side length/format floor as a backstop) or, for the claim code, a recorded
  length/alphabet plus a validity window, rotation, and multi-layered anti-brute-force controls (per-IP limit,
  global ceiling, per-code failed-attempt burn) - never an undecided "shortish code" left to the builder.
- **Byline nicknames cross ADR 0003's play/account-plane carve-out only once claimed.** A vault's byline content is
  play-plane (no account linkage) until story 03's claim attaches an `AccountId` to it - only from that point does
  it become account-plane/household data under the carve-out. No story here surfaces byline content back onto
  `Room`/`Player`, and the support console (`sysadmin-console/07`, a sibling feature) must never project it either
  - see ADR 0003's "Security posture" section.
- **The kid-profile boundary (ADR 0003, Decision 1) is absolute.** A claimed vault's tales are tied to the FAMILY
  account, never to an individual kid seat preset - no per-kid gallery, no per-kid history, in any story here. A
  future idea that wants per-kid anything needs a new ADR, not a story-level slide (every builder's guardrail brief
  should carry this verbatim).
- **Fire-and-forget, always, on the write path a player experiences.** Story 01's auto-save (and any future write
  a child-facing screen triggers) must never block, delay, or visibly degrade gameplay or the reveal - mirror
  `saveTale()`'s existing swallow-failures posture exactly.
- **Config-presence, not a hard Azure requirement.** Every store in this feature ships a genuinely working
  in-memory fallback (mirroring `ICloudGalleryStore`'s split, not `IPublishedTaleStore`'s disabled-no-op) - the
  vault is default-on for every anonymous player, so it must work with zero Azure setup in local dev/CI.
- **Settings-key candidates degrade to code constants.** The vault TTL (story 01), the claim-code rate limit
  (story 03), and the soft-delete restore window (story 04) are all named, recorded constants until
  `control-plane/01`'s catalog exists - never leave one an unrecorded magic number, and never block a story on
  `control-plane` landing first.
- **No i18n** (plain strings), **big tap targets** on any new web affordance (claim/recover buttons, the claim-code
  display), **no em dashes**.
