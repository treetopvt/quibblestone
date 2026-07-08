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
| Lazy expiry-on-read (a stored instant, "expired reads as gone", best-effort reclaim delete) | `PublishedTale.IsExpired` / `TableStoragePublishedTaleStore.GetAsync` | `api/src/PublishedTales/PublishedTale.cs`, `TableStoragePublishedTaleStore.cs` |
| Tiny mutable companion-row pattern (a report/claim/deletion signal keyed by the SAME id, never rewriting the immutable content row) | `TaleModeration` + its `ModerationPartitionKey` sentinel scheme | `api/src/PublishedTales/TaleModeration.cs`, `TableStoragePublishedTaleStore.cs` |
| Server-side re-vet of every part + byline before storing (client word/literal classification never trusted) | `CloudGalleryController.Save` / `PublishedTalesController`'s publish path | `api/src/CloudGallery/CloudGalleryController.cs`, `api/src/PublishedTales/PublishedTalesController.cs` |
| Per-IP fixed-window rate limiting on an anonymous write endpoint | `CloudGalleryRateLimit` / `PublishTalesRateLimit` (ASP.NET Core's built-in limiter, no new dependency) | `api/src/CloudGallery/CloudGalleryRateLimit.cs`, `api/src/PublishedTales/PublishTalesRateLimit.cs` |
| Opaque, cryptographically random, never-identity-derived handle minting | `Room.NewReconnectToken()` (`RandomNumberGenerator`) | `api/src/Rooms/Room.cs` |
| Short, human-shareable, unguessable id minting | `SlugGenerator` (unambiguous-glyph alphabet, `RandomNumberGenerator.GetInt32`) | `api/src/PublishedTales/SlugGenerator.cs` |
| Purchaser/family credential auth pattern (bearer header + cookie fallback, resolved once per request) | `PurchaserCredentialService` reused by `CloudGalleryController` | `api/src/Accounts/PurchaserCredentialService.cs` |
| Stable, non-PII owner keying | `AccountIdentity.KeyFor` | `api/src/Accounts/AccountIdentity.cs` |
| Local gallery storage, cap/eviction, and the `cloudTaleId`-style sync-stamp precedent | `localGallery.ts`'s `GalleryAdapter`/`TaleMeta`/`saveTale`/`cloudTaleId` | `web/src/gallery/localGallery.ts` |
| Fire-and-forget save call site on reveal completion | `handleSaveImage`'s existing `saveTale()` call | `web/src/pages/Reveal.tsx` (~line 943-980) |
| Endpoint-call-with-graceful-failure client pattern | `publishTale.ts` (`publishTale`/`revokeTale`, feature-detect + swallow) | `web/src/gallery/publishTale.ts` |
| Child safety | the single server-side safety filter | `api/src/Safety/IContentSafetyFilter.cs` |
| Config | `import.meta.env` (`VITE_*`) for the API base URL | `web/src/vite-env.d.ts` |

What this feature **exports** that others might import later: `IVaultStore` (a stable seam
`sysadmin-console/07`'s support-lookup verbs will call for vault/tale counts and the restore verb), the claim-code
concept (`sysadmin-console/07`'s "lookup by ... claim code" mirrors story 03's redemption logic), and
`web/src/vault/vaultId.ts` (the device-held vault id any future vault-aware surface reads).

## Wave Plan (DAG)

Sizing rule: a builder owns files that are **disjoint** from its concurrent siblings. Story 01 is the foundation
every other story depends on; it also carries the feature's one truly systemic hazard (`Program.cs`).

| Story | Issue | Files it owns (footprint) | Depends-on | Can-run-with | Wave | Effort |
|---|---|---|---|---|---|---|
| 01 (foundation) | #TBD | new `api/src/Vault/` (`VaultTalePart.cs`, `VaultTale.cs`, `IVaultStore.cs`, `TableStorageVaultStore.cs`, `InMemoryVaultStore.cs`, `VaultRateLimit.cs`, `VaultController.cs`); edits `api/src/Program.cs` (service registration); new `web/src/vault/vaultId.ts`, `web/src/vault/vaultClient.ts`; edits `web/src/pages/Reveal.tsx` (adds the fire-and-forget vault save call) | - | none within this feature (external ADR-0003 wave-1 siblings below, no file overlap) | 1 | high |
| 02 | #TBD | new `web/src/vault/vaultGallery.ts` (merge logic); edits `web/src/gallery/localGallery.ts` (adds `vaultTaleId?` to `TaleMeta`, mirroring `cloudTaleId`); edits `web/src/pages/Gallery.tsx` (swaps the data-loading call, no visual change) | 01 | - | 2 | medium |
| 03 | #TBD | edits `api/src/Vault/` (`VaultClaim.cs` new; adds claim methods to `IVaultStore.cs`/`TableStorageVaultStore.cs`/`InMemoryVaultStore.cs`; adds claim + redeem endpoints to `VaultController.cs`); edits `web/src/vault/` (claim + redeem client calls); edits `web/src/pages/Gallery.tsx` (claim/recover affordances) | 01, accounts-identity/07 (external hard gate; transitively accounts-identity/05) | - (serialize with 04, see note below) | 3 | medium-high |
| 04 | #TBD | edits `api/src/Vault/` (adds soft-delete/restore methods to `IVaultStore.cs`/`TableStorageVaultStore.cs`/`InMemoryVaultStore.cs`; adds the soft-delete endpoint to `VaultController.cs`); edits `api/src/PublishedTales/TableStoragePublishedTaleStore.cs` + `IPublishedTaleStore.cs` (`ConfirmHiddenAsync` becomes a soft-delete; adds a distinctly-named restore-from-takedown method) | 01 | - (serialize with 03, see note below) | 3 | medium |

**Concurrency per wave:** Wave 1 = 01 alone (the foundation; also the ADR 0003 wave-1 `Program.cs` hotspot - see
below). Wave 2 = 02 alone (file-disjoint from 01's remaining scope, but there is nothing else in this feature's
wave 2 to run it alongside). Wave 3 = {03, 04} are the ADR's own cross-feature wave 3 pairing for this feature, but
they are **NOT safely parallel within this feature**: both edit `api/src/Vault/IVaultStore.cs`,
`TableStorageVaultStore.cs`, `InMemoryVaultStore.cs`, and `VaultController.cs`. **Serialize them** - land whichever
is ready first as a small PR, rebase the other on top - rather than running them as a true parallel pair, even
though the ADR's cross-feature table lists them in the same wave (that table governs cross-feature ordering, not
this feature's own internal file safety).

**The ADR 0003 wave-1 `Program.cs` hazard (load-bearing, cross-feature):** story 01's `Program.cs` edit
(registering `IVaultStore` connection-string-gated) is one of SIX ADR 0003 wave-1 service registrations landing
around the same time: `accounts-identity/05` (AccountId spine), `keepsake-vault/01` (this story), `control-plane/01`
(settings service), `sysadmin-console/04` (auth unification), and `platform-devops/07`/`08` (key ring / second
environment - `08` does not touch `Program.cs`). Per the ADR: "all but 08 register services in `Program.cs` - land
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
`web/src/vault/vaultId.ts` (the durable device handle). **Gotchas:** the `Program.cs` hazard above - land that edit
as its own small PR. TTL is a code constant (90 days) until `control-plane` exists; do not block on it.

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
`PurchaserCredentialService` - do not invent a second auth scheme); claim-code redemption requires no account and
is rate-limited. **Owns / exports:** the claim + redeem endpoints and their store methods. **Gotchas:** hard-gated
on `accounts-identity/07` (transitively `/05`) - cannot start before that lands. **Serialize with 04** within this
feature (see Wave Plan note) - both touch `IVaultStore.cs`/`TableStorageVaultStore.cs`/`VaultController.cs`. Choose
and record the claim-code alphabet/length here once implemented (shorter/more forgiving than the published-tale
slug, which is tuned for a URL, not a human-typed code).

### 04 - Soft delete and restore
**Approach:** vault tale deletion gets a soft-delete marker (a flipped flag on a rebuilt immutable record, or a
`TaleModeration`-style companion row - choose and record which); the published-tale takedown path
(`TableStoragePublishedTaleStore.ConfirmHiddenAsync`) stops hard-deleting the tale body and instead marks it
soft-deleted within the same restore-window model. Expiry of the restore window is lazy-on-read, mirroring
`PublishedTale.IsExpired`'s existing idiom (the same one story 01 uses for vault-TTL expiry) - no reaper job in this
story. **Owns / exports:** `RestoreAsync`-shaped methods on `IVaultStore` and a distinctly-named restore-from-
takedown method on `IPublishedTaleStore` (do not collide names with the EXISTING moderation `RestoreAsync`, which
un-hides a never-body-deleted tale - a different operation). **Gotchas:** name the two restore concepts (un-hide vs
un-delete) so an operator-console reader cannot confuse them; `CloudGallery`'s hard-delete is explicitly OUT of
scope (an easy story to over-reach into); **serialize with 03** within this feature (see Wave Plan note); also
touches `api/src/PublishedTales/` - check for concurrent work there before starting.

## Cross-cutting concerns

- **This feature persists and recovers; it does not collect, assemble, or moderate-decide.** Every story consumes
  an already-assembled, already-filtered tale (`the-reveal`, `keepsake-gallery/01`). If a story here seems to need
  new engine logic, a new free-text entry point, or a change to the existing moderation report/auto-hide threshold
  logic, that is scope creep out of this feature's lane - flag it rather than build it.
- **No PII, ever.** A vault id and a claim code are both opaque, random handles - never derived from or joined to
  an email, device fingerprint, IP address, or any other identity. Nothing this feature builds lands on `Room` or
  `Player` (`api/src/Rooms/Room.cs`'s "IDENTITY CONTRACT" header comment is the standing guard, unchanged by this
  feature).
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
