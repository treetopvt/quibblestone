# Story: The device gallery becomes a view over the vault

**Feature:** Keepsake Vault  ·  **Status:** Complete  ·  **Issue:** #212

## Context
`keepsake-gallery/03`'s "Tales we've carved" screen reads ONLY the device's
local IndexedDB (`web/src/gallery/localGallery.ts`): a 30-tale cap
(`GALLERY_CAP`) with silent oldest-first eviction, and a `saveTale()` that
swallows its own storage failures (both documented, deliberate trade-offs at
the time - device-local, account-free storage has no other option). Now that
`keepsake-vault/01` auto-saves every completed reveal server-side
independent of the local write, both of those trade-offs stop being genuine
data loss - the tale still exists in the vault even if the local copy was
evicted or never wrote. This story makes the gallery read BOTH sources and
present one merged list, with local IndexedDB becoming a cache/offline copy
rather than the only copy. See [feature.md](./feature.md) and
[ADR 0003](../../adr/0003-admin-platform-and-family-accounts.md) (Layer 2:
"the device IndexedDB gallery becomes a cache/offline view over the vault").

## Acceptance Criteria
- [x] AC-01: Given a device holding a vault id, when the "Tales we've carved"
      gallery loads, then it fetches the vault's tales (`GET`, story 01) and
      merges them with the device's local IndexedDB list into a single,
      deduplicated, recency-ordered list - a tale saved on this device never
      appears twice even though it exists in both places.
- [x] AC-02: Given the vault is unreachable (offline, a network failure, a
      slow response), then the gallery still renders the local IndexedDB copy
      immediately - a vault fetch failure degrades gracefully (the gallery
      simply shows what it has locally) and never blocks, errors, or delays
      the screen beyond a normal loading state.
- [x] AC-03: Given the local gallery's 30-tale cap (`GALLERY_CAP` in
      `localGallery.ts`) would otherwise evict a tale, then that eviction is
      no longer data loss for a vault-backed device: the evicted tale remains
      listed (it is re-populated into the merged view from the vault, the
      server-side source of truth, the next time the gallery is opened online).
- [x] AC-04: Given `saveTale()`'s existing silent-failure posture (a local
      IndexedDB write that fails for any reason - quota, blocked, private
      browsing), then that failure also stops being data loss for a
      vault-backed device, because the SAME tale already landed server-side
      via story 01's independent auto-save call.
- [x] AC-05 (child-safety): Given a tale merged into the gallery view from the
      vault, then it is the exact already-filtered content story 01 stored
      (title, parts, byline nicknames) - this story introduces no new
      free-text entry point and no new PII surface; only the merge/read logic
      changes.
- [x] AC-06: Given the "Tales we've carved" gallery screen, then there is no
      visual redesign - the same grid/list, cards, and full-image/detail view
      from `keepsake-gallery/03` render unchanged; only the data source
      feeding them becomes vault-backed with local IndexedDB as a cache.

## Out of Scope
- Claiming the vault into a family account, or the claim-code recovery flow -
  `keepsake-vault/03`.
- Soft-delete / restore UI or behavior - `keepsake-vault/04`.
- Any change to `keepsake-gallery/05`'s purchaser cloud-sync gallery (a
  separate, purchaser-account-scoped surface this feature supersedes over
  time per `feature.md`'s Design notes, but does not edit or remove here).
- Redesigning `Gallery.tsx`'s visual language, card styling, or navigation
  entry point - this story is a data-source swap only.
- Server-side rendering of vault-only tales that were never saved as a local
  image (a tale synced from a DIFFERENT device, if such a scenario ever
  arises before claim/recovery exists) - within THIS story's scope, a device
  merges its OWN vault id's tales with its own local gallery; cross-device
  visibility of the same vault only becomes meaningful once story 03 ships
  claim/recovery (a device with a vault id already sees its own vault's
  tales regardless of which device originally saved them, since the vault is
  keyed by vault id, not by device - but two DIFFERENT devices only share a
  vault id once story 03's recovery flow re-attaches one).

## Technical Notes
- New `web/src/vault/vaultGallery.ts` (or an extension of
  `web/src/gallery/localGallery.ts`, whichever keeps the merge logic
  cleanest): composes `listTales()` (existing local read) with a vault
  `GET` (`web/src/vault/vaultClient.ts` from story 01), dedupes by a stored
  identifier, and returns one merged, recency-sorted list. Keep the merge a
  PURE function over two already-fetched arrays (mirroring `talesToEvict`'s
  "no IndexedDB, no async, directly unit-testable" precedent in
  `localGallery.ts`), so the offline-degrade behavior (AC-02) is trivially
  testable without mocking IndexedDB or fetch together.
- Extend `TaleMeta` (`localGallery.ts`) with a new optional
  `vaultTaleId?: string` field - mirroring the EXISTING `cloudTaleId` stamp
  pattern exactly (set after a successful vault round-trip, used to dedupe a
  local record against its vault counterpart so the merge never double-lists
  the same tale). This is an additive, backward-compatible field, same
  posture as `cloudTaleId`'s own addition.
- `Gallery.tsx` swaps its data-loading call from `listTales()` alone to the
  new merged loader; the render tree (cards, detail view, re-share action)
  is untouched (AC-06) - this story owns exactly the data-fetching layer,
  not the presentation layer.
- Offline-first ordering (AC-02): render the local list FIRST (synchronous,
  fast), then reconcile with the vault fetch when it resolves (or fails) -
  never block the initial paint on the network call.

## Tests
| AC | Test |
|---|---|
| AC-01 | unit (Vitest): merging a local list and a vault list with overlapping ids produces one deduplicated, recency-ordered list |
| AC-02 | unit (Vitest, mocked failing fetch): the merge function/gallery loader returns the local-only list without throwing when the vault fetch rejects |
| AC-03 | unit (Vitest): a tale evicted from the local list (past `GALLERY_CAP`) but present in the vault-fetch result still appears in the merged output |
| AC-04 | unit (Vitest): a tale present only in the vault-fetch result (simulating a local write that failed) still appears in the merged output |
| AC-05 | manual + code review: merged vault-sourced tales carry only the story-01 shape (title, parts, byline names, createdUtc), no new field |
| AC-06 | manual: visually compare the gallery screen before/after - no layout, card, or navigation change |

## Dependencies
- `keepsake-vault/01` (the vault store + auto-save + the `GET` fetch this
  story consumes).
