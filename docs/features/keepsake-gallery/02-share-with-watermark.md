# Story: Share the tale with watermark

**Feature:** Keepsake Gallery  ·  **Status:** Complete  ·  **Issue:** #64

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
- [x] AC-01: Given a saved tale image (story 01), when I tap "Share the tale"
      (extending the Reveal screen's existing share action, or the same
      action on a saved gallery item once story 03 exists), then the
      browser's Web Share API is invoked with the image as the share payload
      (falling back to a text/link share if image sharing is unsupported),
      mirroring the feature-detection approach already used in
      `session-engine/04` and `the-reveal/01` (`typeof navigator.share ===
      'function'`, not gated on `navigator.canShare()`).
- [x] AC-02: Given the Web Share API is unavailable on the current browser,
      then the share action falls back gracefully (e.g. copy the image or a
      link to clipboard) with no JavaScript error thrown, matching the
      existing fallback behavior in `session-engine/04` and `the-reveal/01`.
- [x] AC-03: Given the exported image, then it carries a small, legible but
      unobtrusive watermark reading "carved with QuibbleStone" (placement
      should not obscure the story text or the coral words) so every image
      that leaves the app is a passive growth touch, never an ad.
- [x] AC-04: Given a tale is shared, then only content that already passed
      the safety filter is shareable - if the family-safe toggle is on for a
      session, the shared image reflects family-safe content only (no
      separate un-vetted share path exists).
- [x] AC-05: Given the shared image, then the only identity on it is the
      in-session nickname(s) + Guardian variant(s), same as story 01's AC-05 -
      no PII is ever included in a shared image, regardless of which share
      target (Messages, WhatsApp, etc.) the player picks.
- [x] AC-06: Given the share action, then it works from the Reveal screen for
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
- The real back-link that turns a share into a new player lives in story 04
  (shareable tale link): this story shares the watermarked IMAGE; story 04 adds
  a public tale URL to the same share payload (and is the link alone when a
  browser cannot share a file/image - AC-01's fallback). Keep the two share
  outputs in the one `handleShare` path rather than forking a second share flow.

### Implementation record (2026-07-02)
- Watermark (AC-03): added directly to `web/src/gallery/renderTablet.ts`'s
  existing render pass - a fixed "carved with QuibbleStone" string, wrapped
  with the same `wrapPlainTextIntoLines` helper the title/byline already use
  and painted with the same `paintPlainLines` routine, in a small muted font
  (`theme.palette.text.secondary` at 0.5 alpha). Its height is reserved in
  `computeLayout` unconditionally, so it can never overlap the body or byline
  above it, and it is centered below whichever block is last (byline when
  present, otherwise the body).
- Share (AC-01/AC-02/AC-06): `Reveal.tsx`'s `handleShare` now tries
  `shareImage()` first - renders the SAME tablet `handleSaveImage` already
  produces, wraps it in a `File`, and offers it ONLY when
  `navigator.canShare({ files: [file] })` reports support (the one place this
  screen gates on `canShare()`, deliberately, per the story's Technical
  Notes). A user-cancelled `AbortError` is swallowed (matches the existing
  text-share posture); any other outcome (unsupported file share, a render
  failure, or a non-cancellation rejection) falls through unchanged to the
  EXISTING `shareText()` path (`navigator.share({ title, text })`, then
  `copyTale()`) - never removed, never forked. A `sharingImage` flag disables
  the Share button and swaps its label to "Preparing to share..." while the
  image renders/shares, mirroring the existing "Save as image" affordance.
- Byline wiring (completes keepsake-gallery/01's AC-02 for group play): added
  `web/src/gallery/byline.ts` (`joinNamesReadably` / `formatCrewByline`, unit
  tested in `byline.test.ts`) and wired it into `App.tsx`'s `GroupReveal`
  wrapper as `saveImageByline`, built from the SAME `buildCrew(reveal.words)`
  crew list the Round Complete recap already derives - no second data source,
  no hub call. Chosen format: a natural-language list - "carved by Sam" (one
  name), "carved by Sam & Mia" (two), or "carved by Sam, Mia & Bo" (three or
  more) - rather than a literal trailing "& crew" suffix, since the crew
  members ARE named individually; this reads more naturally for a small
  group. Solo.tsx deliberately still omits `saveImageByline`: solo collects no
  nickname at all (no room, no join flow), so there is no faithful string to
  give - see Solo.tsx's own comment at its `<Reveal>` call. Both saved and
  shared images now carry a byline for group play; solo images remain
  byline-free by design, not by omission.
- Known limitation (Gate-1 review CR-W-002, follow-up candidate): `shareImage()`
  awaits the canvas render (fonts + `toBlob`) BEFORE calling `navigator.share`.
  On browsers with strict transient-user-activation rules (some iOS Safari
  versions), that intervening async work can consume the tap's activation, so
  the file-share sheet rejects with `NotAllowedError`. This degrades GRACEFULLY
  - the non-Abort rejection falls through to the text share / clipboard, so no
  error is thrown (AC-02 holds) - but the image happy-path may silently not
  fire there. If it proves common in testing, pre-render the blob before/at tap
  time to preserve the gesture. Not fixed now: it never breaks the button and
  the text fallback is faithful.

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
