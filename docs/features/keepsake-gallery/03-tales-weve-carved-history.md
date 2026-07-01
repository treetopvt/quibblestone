# Story: "Tales we've carved" local history

**Feature:** Keepsake Gallery  ·  **Status:** Not Started  ·  **Issue:** #65

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
- [ ] AC-01: Given I have saved at least one tale as an image (story 01), then
      it appears in a "Tales we've carved" gallery screen accessible from
      somewhere in the app's navigation (e.g. from Home), showing a list/grid
      of saved tales with title, date, and a thumbnail or the saved image
      itself.
- [ ] AC-02: Given the gallery, when I tap a saved tale, then I see the full
      saved image (or a read-only re-render of its content) and can re-share
      it using the same share action from story 02.
- [ ] AC-03: Given the gallery, then it is stored entirely device-local
      (`localStorage` or `IndexedDB`) with no server round-trip and no
      account required - it works identically to how every other player
      identity in this app already works: anonymous, tied only to the device
      that played.
- [ ] AC-04: Given the gallery grows over time, then a cap and eviction policy
      keeps device storage from growing unbounded (e.g. a maximum count of
      saved tales, or a maximum total storage budget, evicting the oldest
      entries first once the cap is reached) - a family that plays for months
      does not silently fill up their device's storage.
- [ ] AC-05: Given a saved tale in the gallery, then only content that already
      passed the safety filter and no PII beyond in-session nickname +
      Guardian variant is ever present - the gallery introduces no new
      free-text entry point and stores nothing beyond what was already saved
      as the story-01 image plus its small metadata record (title, date,
      byline names).
- [ ] AC-06: Given I clear my browser data/storage (a normal device action,
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
- Storage: `IndexedDB` is the better fit for storing image blobs at any
  meaningful scale (localStorage's ~5-10MB quota and string-only storage make
  it a poor fit for multiple images); use `localStorage` only for small
  metadata if a hybrid approach is chosen (e.g. `localStorage` for the
  ordered list of tale metadata, `IndexedDB` for the image blobs themselves).
  Whichever is chosen, keep the storage access behind a small, testable
  module (e.g. `web/src/gallery/localGallery.ts`) rather than scattering raw
  `indexedDB`/`localStorage` calls through the gallery screen.
- Cap/eviction policy (AC-04): a concrete starting rule to specify during
  implementation - for example, a maximum of N saved tales (e.g. 20-30) with
  oldest-first eviction once the cap is hit, or a maximum total storage
  footprint (e.g. a few MB) with the same oldest-first eviction. Record the
  chosen concrete numbers in this story once decided (update this Technical
  Notes section) rather than leaving the cap unspecified at build time.
- Each gallery entry's metadata record: title, saved-at date, byline names
  (already-filtered display names), and a reference/key to the stored image
  blob. This mirrors story 01's output exactly - no new data is derived from
  the engine.
- Gallery screen: a simple grid/list reusing the app's existing stone-tablet
  card styling and theme tokens (consistent visual language with Home,
  Lobby, Reveal) - no new visual system for this screen.
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
