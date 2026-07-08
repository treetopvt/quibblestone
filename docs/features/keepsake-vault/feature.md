# Feature: Keepsake Vault

## Summary
A new anonymous, server-side keepsake vault that auto-saves every completed
reveal so a family's tales survive a cleared browser, a lost device, or a
30-tale local cap - the "where are my saved stories?" support scenario ADR
0003 was written to close. The device-local gallery becomes a durable view
over this vault rather than the only copy; a family can later claim the
vault into a free family account for permanent, cross-device access, or
recover it onto a new device with a human-friendly claim code even without
one; deletions and public-tale takedowns become soft-deletes with a restore
window instead of silent, permanent data loss.

## README reference
[ADR 0003](../../adr/0003-admin-platform-and-family-accounts.md), Decision 2
and "Layer 2 - recovery and support data": this feature IS that layer,
decomposed into build-ready stories. Grounds in README section 4 ("toy, not
a system of record" - but recovery is a real support scenario the ADR's audit
surfaced), section 6 (minimal data on minors - a vault id is a random handle,
never identity), and section 7 (Epic Map, Phase 2+ "Accounts & Identity" /
Phase 4 "saved-story keepsakes", which this feature makes durable).

## Stories
<!-- Status: Not Started | In Progress | Complete | Blocked | Dropped -->
| Story | Issue | Title | Status |
|---|---|---|---|
| 01 | #TBD | The vault: auto-save every completed reveal server-side | Not Started |
| 02 | #TBD | The device gallery becomes a view over the vault | Not Started |
| 03 | #TBD | Claim and recovery | Not Started |
| 04 | #TBD | Soft delete and restore | Not Started |

## Dependencies
- `accounts-identity/05` (the stable `AccountId` spine) and `accounts-identity/07`
  (the free family account) - story 03's hard gate. `05` is the identity
  primitive `07`'s free family account is built on, so it is a transitive
  dependency of story 03 even though story 03 only calls `07` directly.
- `control-plane` (the runtime settings service, ADR 0003 Layer 1) - the vault
  TTL (story 01, default ~90 days), the claim-code rate limit (story 03), and
  the soft-delete restore window (story 04, default ~30 days) are all
  settings-key candidates per the ADR's "knob migration" list. This feature
  does **not** block on `control-plane` existing: every knob ships as a code
  constant default (mirroring `PublishedTalesController.TaleTtl`'s current
  posture) and is migrated to a settings key opportunistically once
  `control-plane/01`'s catalog exists - see each story's Technical Notes.
- `keepsake-gallery` (01 save-reveal-as-image, 03 tales-weve-carved-history,
  05 cloud-synced-gallery) - this feature builds directly on top of story 01's
  rendered tale content and story 03's local IndexedDB gallery, and
  **supersedes story 05's role over time** (see Design notes below). None of
  `keepsake-gallery`'s shipped story files are edited by this feature.
- `child-safety` (the server-side content-safety filter every vault write
  re-vets against, exactly like `CloudGalleryController`/`PublishedTalesController`
  already do).
- `infra` (Azure Table Storage - README section 9 - the vault's datastore;
  no new Azure resource, reusing the storage account the footprint already
  provisions).

## Design notes
- **One engine, many thin modes stays untouched.** This feature is entirely
  a recovery/persistence layer for content the engine already assembled and
  the reveal already filtered and displayed (`the-reveal`, `keepsake-gallery/01`).
  It introduces no new free-text entry point, no new template axis, and no
  change to `assemble()`/`collectWord()`. If a story here seems to need
  engine changes, that is scope creep out of this feature's lane.
- **The vault id mirrors the reconnect-token pattern, but durable.**
  `api/src/Rooms/Room.cs`'s `NewReconnectToken()` mints an opaque,
  cryptographically random, per-seat handle that is never derived from
  identity and never broadcast - the vault id is the same idea (a random
  handle a device holds), except it is durable client-side storage
  (`localStorage`/IndexedDB) rather than an in-memory server seat, because a
  vault must survive across sessions and room lifetimes (a `Room` evaporates
  the moment its last player leaves - `api/src/Rooms/Room.cs`'s header
  comment; the vault is precisely the thing that does not evaporate).
- **Follow the `CloudGallery` config-presence idiom, not the `PublishedTales`
  disabled-no-op one.** `ICloudGalleryStore` ships a genuinely WORKING
  in-memory fallback when no Table Storage connection string is configured
  (`InMemoryCloudGalleryStore`), so the whole save -> list -> delete flow is
  exercisable with zero Azure setup - this is the right precedent for the
  vault (an anonymous, always-on surface every player hits), not
  `DisabledPublishedTaleStore`'s "off until provisioned" posture (which fits
  an opt-in growth feature, not a default-on auto-save).
- **Auto-save is fire-and-forget, always.** `Reveal.tsx`'s existing
  `handleSaveImage` (~line 943) already calls `saveTale()` (the local gallery
  write) fire-and-forget alongside the client-side image download, and
  `saveTale`/`localGallery.ts` swallow their own storage failures so a
  gallery-write problem can never undo or block the download that already
  completed. The vault auto-save (story 01) is the same posture one level up:
  a network call to the vault must never delay, block, or visibly degrade the
  reveal screen a family is looking at in the back seat of a car.
- **Supersession, recorded here rather than by editing shipped stories.** Per
  ADR 0003's Consequences: `keepsake-gallery/03` (device-local gallery) and
  `/05` (purchaser cloud gallery) are **not reopened**, but the vault
  supersedes their roles over time:
  - Story 02 turns `keepsake-gallery/03`'s local IndexedDB gallery from the
    only copy into a cache/offline view over the vault - its 30-tale cap
    (`GALLERY_CAP` in `web/src/gallery/localGallery.ts`) and its
    silent-save-failure posture stop being data-loss modes, because the
    server-side copy already exists independent of local storage succeeding.
  - Story 03's vault-claim supersedes `keepsake-gallery/05`'s manual
    purchaser cloud-sync-and-upload flow: once a vault can be claimed into a
    free family account for permanent, cross-device tales, the older
    "sign in as a purchaser and manually upload from the Account area" path
    becomes redundant. This feature does not retire or edit `05`'s story
    file or code - it is recorded here as a planning note for whoever picks
    up that retirement later.
- **The kid-profile boundary applies here too (ADR 0003, Decision 1).** A
  claimed vault's tales are tied to the FAMILY account, never to an
  individual kid seat preset - there is no per-kid gallery, no per-kid
  history, ever. If a future idea wants per-kid anything, that is a new ADR,
  not a slide in this feature.
- **No PII, still.** A vault id is a random handle a device holds - never an
  email, device fingerprint, or IP-derived value. A claim code is likewise an
  opaque, unguessable handle that only ever re-links a vault id to a new
  device - it carries no identity of its own. Nothing new lands on `Room` or
  `Player` (`api/src/Rooms/Room.cs`'s "IDENTITY CONTRACT" header comment is
  the guard here, unchanged).

## Parked - Phase 2+
- A background reaper job for expired/soft-deleted rows (today's precedent,
  `TableStoragePublishedTaleStore`, relies on lazy expiry-on-read only; story
  04 records whether the vault does the same or needs a job, but building a
  scheduled reaper is a separate, later concern if lazy expiry proves
  insufficient at scale).
- Exporting a claimed vault's full tale history as a printable collection
  (already parked in `keepsake-gallery/feature.md`; unaffected by this
  feature).
- Per-kid galleries or history of any kind - explicitly **forbidden**, not
  merely parked, by ADR 0003's kid-profile boundary (Decision 1). Would
  require its own ADR, not a story here.
- Multi-family vault sharing / a public vault directory - out of scope; a
  vault belongs to exactly one family (or sits unclaimed on one device),
  mirroring `keepsake-gallery/05`'s "no public gallery / discovery" stance.

## Decisions
- 2026-07-08: Feature created per ADR 0003 (accepted 2026-07-08), decomposing
  its "Layer 2 - recovery and support data" section into four build-ready
  stories. Scoped to exactly the vault's own data model, auto-save, gallery
  merge, claim/recovery, and soft-delete/restore - the operator console verb
  that calls restore is `sysadmin-console/07`'s job, not this feature's;
  the grant-metadata/reconciliation half of Layer 2 is `billing-entitlements/08`,
  a sibling feature this one does not touch.
