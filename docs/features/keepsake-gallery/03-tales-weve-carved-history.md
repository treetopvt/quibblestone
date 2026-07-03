# Story: "Tales we've carved" local history

**Feature:** Keepsake Gallery  ·  **Status:** In Progress  ·  **Issue:** #65

## Context
A family plays several rounds over a session (or several sessions over weeks)
and will want to revisit their funniest tales without re-playing them. This
story adds a small, device-local gallery of past saved reveals - anonymous, no
account, no server sync - so the memories persist on the device that played
them. This idea was explicitly parked in `session-engine/feature.md`'s "Parked
- Phase 2+" list ("Tales we've carved local history (design pack Expansion
5)") and in `the-reveal/feature.md`'s "Parked - Phase 3" ("Saving and sharing
the finished tale as an image of the tablet"); this story gives it a home. See
[feature.md](./feature.md).

## Acceptance Criteria
- [x] AC-01: Given I have saved at least one tale as an image (story 01), then
      it appears in a "Tales we've carved" gallery screen accessible from
      somewhere in the app's navigation (e.g. from Home), showing a list/grid
      of saved tales with title, date, and a thumbnail or the saved image
      itself.
- [x] AC-02: Given the gallery, when I tap a saved tale, then I see the full
      saved image (or a read-only re-render of its content) and can re-share
      it using the same share action from story 02.
- [x] AC-03: Given the gallery, then it is stored entirely device-local
      (`localStorage` or `IndexedDB`) with no server round-trip and no
      account required - it works identically to how every other player
      identity in this app already works: anonymous, tied only to the device
      that played.
- [x] AC-04: Given the gallery grows over time, then a cap and eviction policy
      keeps device storage from growing unbounded (e.g. a maximum count of
      saved tales, or a maximum total storage budget, evicting the oldest
      entries first once the cap is reached) - a family that plays for months
      does not silently fill up their device's storage.
- [x] AC-05: Given a saved tale in the gallery, then only content that already
      passed the safety filter and no PII beyond in-session nickname +
      Guardian variant is ever present - the gallery introduces no new
      free-text entry point and stores nothing beyond what was already saved
      as the story-01 image plus its small metadata record (title, date,
      byline names).
- [x] AC-06: Given I clear my browser data/storage (a normal device action,
      not an in-app feature), then the gallery is gone, exactly as expected
      for device-local, account-free storage - this is documented behavior,
      not a bug, and the story does not attempt to work around it (no hidden
      server backup).

## Out of Scope
- Cloud sync of the gallery across devices (parked in `feature.md` - requires
  a purchaser account, which does not exist yet per README section 7 Phase 2).
- Search or filtering within the gallery beyond a simple recency-ordered list
  (parked in `feature.md`).
- Deleting individual tales from the gallery (a reasonable follow-up, but not
  required for the initial local-history feature - note as a candidate small
  addition, not core scope here).
- Exporting the whole gallery as a printable collection (parked in
  `feature.md`).
- Re-rendering a saved tale's LIVE data (i.e. re-running `assemble()` against
  stored `AssembledStory`/`Template` data) - per `feature.md`'s Decisions log,
  this story stores and displays the rendered IMAGE (plus small metadata),
  never the raw engine data, keeping it disjoint from `the-reveal` and the
  engine entirely.

## Technical Notes
- Storage (AS BUILT): a single IndexedDB database (`qs-gallery`, one object
  store `tales` keyed by `id`) stores EACH tale's metadata AND its image blob
  together as one record - no hybrid `localStorage` split was needed. All raw
  IndexedDB access lives behind `web/src/gallery/localGallery.ts`'s
  `GalleryAdapter` interface (`readAllMeta`/`putTale`/`readImage`/`deleteTale`),
  implemented by a lazily-constructed IndexedDB adapter; the gallery screen
  (`Gallery.tsx`) never touches `indexedDB` directly.
- Cap/eviction policy (AC-04, RECORDED numbers): **`GALLERY_CAP = 30`** saved
  tales, **oldest-`savedAt`-first** eviction once a save would push the count
  past the cap. The eviction decision is the exported PURE function
  `talesToEvict(existing, cap): string[]` (no IndexedDB, no async) -
  `saveTale()` calls it before every write to decide which id(s), if any, to
  delete first. Unit-tested directly (`talesToEvict`'s own describe block)
  and end-to-end against an in-memory fake adapter (saving `GALLERY_CAP + 2`
  tales leaves exactly `GALLERY_CAP`, with the two oldest evicted) in
  `web/src/gallery/localGallery.test.ts`.
- Testability without a new dependency: every `localGallery.ts` function
  (`saveTale`/`listTales`/`getTaleImage`) accepts an optional `GalleryAdapter`
  parameter (defaulting to the real IndexedDB adapter, constructed lazily so
  merely importing the module never touches IndexedDB). Tests inject a small
  in-memory `Map`-backed fake implementing the same interface - no
  `fake-indexeddb` or any other new runtime/test dependency was added.
- Each gallery entry's metadata record: `{ id, title, savedAt (epoch ms),
  bylineNames? }`, stored alongside the image blob. This mirrors story 01's
  output exactly - no new data is derived from the engine.
- Gallery screen (`Gallery.tsx`): a simple 2-column grid of teal-bordered
  cards (mirroring `Favorites.tsx`'s card language) reusing the shared
  `<AppBar>` and theme tokens - no new visual system for this screen. Tapping
  a card swaps to a full-image detail view (same screen, no new dialog/router
  dependency) with a "Share this tale" re-share button. Object URLs
  (`URL.createObjectURL`) are tracked and revoked on unmount/reload so the
  screen never leaks blob URLs.
- Re-share (AC-02): `web/src/gallery/shareImageFile.ts` extracts the
  "feature-detect `navigator.canShare({ files })`, share, swallow
  AbortError, never throw" logic that `Reveal.tsx`'s `handleShare` already
  implemented (story 02) into one shared helper. Both `Reveal.tsx`'s image
  share path and the Gallery's re-share action call it - no duplicated
  share-with-fallback logic. The gallery re-share shares the STORED blob
  directly (re-fetched via `getTaleImage`); it never re-renders from engine
  data, per this feature's Decisions log.
- Capture-on-save (AC-01): `Reveal.tsx`'s existing `handleSaveImage` renders
  the tablet blob ONCE, then both triggers the existing client-side download
  AND calls `saveTale({ title, image: blob, bylineNames: saveImageByline })`.
  `saveTale` swallows its own storage failures internally (mirroring
  `../content/favorites.ts`'s posture), so a gallery-write failure can never
  break the pre-existing download.
- No API/hub change: this is a `web/` -only story, same as stories 01 and 02.
- Because storage is entirely device-local and anonymous, there is no
  per-player quota enforcement question across devices - the cap applies per
  device/browser storage instance.

## Tests
| AC | Test |
|---|---|
| AC-01 | manual: save two or more tales, confirm they appear in the gallery with title/date/thumbnail |
| AC-02 | manual: tap a saved tale, confirm full image view and a working re-share action |
| AC-03 | unit (Vitest): `localGallery.ts`'s save/list/evict functions operate purely against a storage abstraction, no network calls |
| AC-04 | unit (Vitest): saving beyond the configured cap evicts the oldest entry first; storage never exceeds the configured limit |
| AC-05 | manual: inspect stored metadata for any PII field beyond nickname + Guardian variant; confirm no unfiltered content path exists |
| AC-06 | manual: clear browser storage, confirm the gallery empties with no error and no attempted recovery call |

## Dependencies
- keepsake-gallery/01-save-reveal-as-image
- keepsake-gallery/02-share-with-watermark (the re-share action a gallery item reuses)
