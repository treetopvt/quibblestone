# Story: Share the tale with watermark

**Feature:** Keepsake Gallery  ·  **Status:** Not Started  ·  **Issue:** TBD

## Context
Every saved tale is a chance for word-of-mouth growth (README section 2 - live
cross-device multiplayer is "the differentiator that gets us noticed"). This
story wires the saved image (story 01) into the Web Share API / copy-link
flow, the same pattern already proven in `session-engine/04` and reused in
`the-reveal/01`'s existing "Share the tale" button, and adds a light
watermark ("carved with QuibbleStone") to the exported image so every share
is also a soft, ad-free growth touch. See [feature.md](./feature.md) and
`docs/features/session-engine/04-copy-share-room-code.md`.

## Acceptance Criteria
- [ ] AC-01: Given a saved tale image (story 01), when I tap "Share the tale"
      (extending the Reveal screen's existing share action, or the same
      action on a saved gallery item once story 03 exists), then the
      browser's Web Share API is invoked with the image as the share payload
      (falling back to a text/link share if image sharing is unsupported),
      mirroring the feature-detection approach already used in
      `session-engine/04` and `the-reveal/01` (`typeof navigator.share ===
      'function'`, not gated on `navigator.canShare()`).
- [ ] AC-02: Given the Web Share API is unavailable on the current browser,
      then the share action falls back gracefully (e.g. copy the image or a
      link to clipboard) with no JavaScript error thrown, matching the
      existing fallback behavior in `session-engine/04` and `the-reveal/01`.
- [ ] AC-03: Given the exported image, then it carries a small, legible but
      unobtrusive watermark reading "carved with QuibbleStone" (placement
      should not obscure the story text or the coral words) so every image
      that leaves the app is a passive growth touch, never an ad.
- [ ] AC-04: Given a tale is shared, then only content that already passed
      the safety filter is shareable - if the family-safe toggle is on for a
      session, the shared image reflects family-safe content only (no
      separate un-vetted share path exists).
- [ ] AC-05: Given the shared image, then the only identity on it is the
      in-session nickname(s) + Guardian variant(s), same as story 01's AC-05 -
      no PII is ever included in a shared image, regardless of which share
      target (Messages, WhatsApp, etc.) the player picks.
- [ ] AC-06: Given the share action, then it works from the Reveal screen for
      a just-finished tale (extending the existing "Share the tale" button
      from `the-reveal/01` to share the rendered image rather than plain
      text) without removing or breaking the existing text-share fallback
      path.

## Out of Scope
- Tracking share analytics (opens, click-throughs) - no analytics
  infrastructure exists yet; this story is purely the share mechanic.
- A referral/rewards system tied to sharing (a monetization idea, not in
  scope here - README section 3's entitlement seam is untouched by this
  story).
- Customizing the watermark placement/opacity per user preference - one fixed,
  sensible placement for Slice-1-adjacent scope.
- Sharing directly to a specific social platform's API (this story uses the
  OS-level Web Share sheet only, consistent with the existing approach).

## Technical Notes
- Reuses the exact feature-detection and fallback pattern already documented
  in `session-engine/04-copy-share-room-code.md`'s Technical Notes and already
  implemented in `web/src/pages/Reveal.tsx`'s `handleShare`/`copyTale`
  functions (feature-detect `navigator.share`, do NOT gate on
  `navigator.canShare()`, swallow a user-cancelled `AbortError`, fall back to
  `navigator.clipboard` when Web Share is unavailable). This story extends
  that existing `handleShare` to prefer an image payload
  (`navigator.share({ files: [...] })`, feature-detecting file-share support
  specifically, since not every Web-Share-capable browser supports file
  payloads) with the existing text-only share as the fallback when file
  sharing is unsupported - it does not replace or remove the existing text
  share path (AC-06).
- The watermark is applied at image-render time (story 01's render step), not
  as a separate post-processing pass - add it as part of the same
  canvas/DOM-to-image render so there is only one rendering code path to
  maintain.
- No API/hub change: this is a `web/` -only story, same as story 01.
- Family-safe / filter compliance (AC-04) is inherited for free from story 01
  (the image only ever contains already-vetted `AssembledStory` content) -
  this story does not add a second content path that could bypass it.

## Tests
| AC | Test |
|---|---|
| AC-01 | manual: trigger share on a device/browser supporting Web Share with file payloads; confirm the image is offered |
| AC-02 | manual: trigger share on a browser without Web Share support (e.g. desktop Chrome); confirm graceful clipboard fallback with no console error |
| AC-03 | manual: visually inspect the exported image for a legible, unobtrusive "carved with QuibbleStone" watermark that does not obscure story text |
| AC-04 | manual: with the family-safe toggle on, confirm the shared image reflects only family-safe content |
| AC-05 | manual: inspect shared image for any PII field, confirm only nickname + Guardian variant appear |
| AC-06 | manual: confirm the existing text-share fallback (from `the-reveal/01`) still works when file sharing is unsupported |

## Dependencies
- keepsake-gallery/01-save-reveal-as-image
- session-engine/04-copy-share-room-code (the Web Share pattern this mirrors)
- the-reveal/01-text-reveal (the existing "Share the tale" action this extends)
