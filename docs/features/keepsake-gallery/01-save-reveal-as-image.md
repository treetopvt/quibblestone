# Story: Save the reveal as a stone-tablet image

**Feature:** Keepsake Gallery  ·  **Status:** In Progress  ·  **Issue:** #63

## Context
The finished tale on the Reveal screen - confetti, "Your tale is carved!"
header, byline, coral player-words in the glowing stone tablet - is worth
keeping. This story renders that same tablet to a single shareable image, so a
family can save the moment instead of it only living in the session. It is the
foundation both the share story (02) and the local-history gallery (03) build
on. See [feature.md](./feature.md) and `docs/features/the-reveal/01-text-reveal.md`.

## Acceptance Criteria
- [x] AC-01: Given a completed reveal on the Reveal screen, then I see an
      action to save it as an image (e.g. a "Save as image" affordance,
      secondary weight, not competing with the existing gold "Play another
      round" CTA).
- [x] AC-02: Given I trigger the save action, then an image is rendered
      containing: the story title, the story body with every filled-in word
      shown in coral (matching the Reveal screen's existing coral treatment),
      a byline in the form "carved by [names] & crew" (using the same
      attribution the Reveal screen already shows, when present), and the
      stone-tablet visual treatment (gradient, carved rim) - the image reads
      as a recognizable snapshot of the Reveal screen, not a plain text dump.
      Byline mechanism is built and renders correctly when supplied (renders
      nothing when omitted, which is valid per "when present"); no caller
      currently supplies one - see Technical Notes.
- [x] AC-03: Given the image is rendered, then it resolves in a reasonable
      time on a mid-range mobile device (target: under ~2 seconds) so the
      save action does not feel broken or hung; a loading state is shown
      while it renders if it takes longer than a moment.
- [x] AC-04: Given the rendered image, then only content that has already
      passed the safety filter appears on it - the image introduces no new
      free-text surface and renders nothing that was not already vetted and
      shown on the live Reveal screen.
- [x] AC-05: Given the rendered image, then the only identity shown is the
      in-session nickname(s) + Guardian variant(s) already present in the
      byline - no PII (no real name, no email, no device identifier) is ever
      rendered onto the image.
- [x] AC-06: Given the rendering approach, then it is client-side (canvas or
      DOM-to-image) with no new server round-trip required to produce the
      image; if client-side fidelity proves insufient during implementation,
      the story's Technical Notes record the fallback decision (a server-side
      render) rather than silently shipping a degraded image.

## Out of Scope
- Sharing the image (that is story 02 - this story only produces it).
- Saving it to a persistent local gallery (that is story 03 - this story
  produces a single image for the current session's use).
- Server-side rendering as the default path (client-side first; see AC-06 and
  feature.md Design notes - only fall back if needed, and record the decision
  if it happens).
- Animating the image (confetti, glow pulse) - a saved image is a static
  snapshot; the live Reveal screen keeps the animation.
- Editing or annotating the image before saving.

## Technical Notes
- Client-side approach: render the existing Reveal tablet DOM/canvas region to
  a bitmap. This likely means either (a) a lightweight DOM-to-image library
  (evaluate bundle-size cost before adding - this is a PWA, so a new heavy
  dependency needs to be flagged, per `feature.md` Design notes) or (b) a
  hand-built canvas render that mirrors the tablet's layout using the same
  theme tokens (`theme.palette.tablet.gradient`, `theme.palette.coral.main`,
  Fredoka/Nunito font stacks) so the image visually matches the live screen
  without duplicating its React component tree.
- Reuses `web/src/pages/Reveal.tsx`'s already-computed `buildRevealParts()`
  output (`web/src/pages/revealParts.ts`) for the interleaved text/coral-word
  parts, rather than re-deriving attribution from `AssembledStory` a second
  way - the image's text content should come from the SAME pure function the
  live screen already renders from.
- Byline: reuse whatever `attribution` content the caller already passes to
  `Reveal` (per `RevealProps.attribution` - group play's "carved by [names] &
  crew", solo's personal summary) rather than inventing a second byline
  format for the image.
- No API/hub change: this is a `web/` -only story. If a server-side fallback
  is later needed (see AC-06), that would be a new story, not a silent
  addition to this one.
- Consider device-pixel-ratio scaling for a crisp image on high-DPI phones
  (the most common share target), but do not over-engineer resolution options
  for Slice-1-adjacent scope - one sensible fixed output size is enough.

### Implementation record (2026-07-02)
- Shipped a hand-built `<canvas>` render (`web/src/gallery/renderTablet.ts`):
  no new dependency was added or needed. Public surface:
  `renderTabletImage(input: RenderTabletInput): Promise<Blob>` and a
  `renderTabletDataUrl(input): Promise<string>` convenience variant for a
  future consumer (story 03) that may prefer a data URL over an object-URL
  lifecycle. `RenderTabletInput = { assembled, template, theme, byline? }`.
- Word-wrap layout was extracted into a separate PURE module
  (`web/src/gallery/tabletLayout.ts`, `wrapRevealPartsIntoLines` /
  `wrapPlainTextIntoLines`) that takes a measurement function as a dependency,
  so it is Vitest-unit-tested (`web/src/gallery/tabletLayout.test.ts`) without
  needing a real `<canvas>` (Vitest's `node` environment has none).
  `renderTablet.ts` supplies the real `CanvasRenderingContext2D.measureText`
  measurer; only the actual paint calls live outside test coverage.
- **Byline-wiring decision:** `RevealProps.saveImageByline?: string` was added
  to `Reveal.tsx` as the minimal seam, but it is NOT wired through any caller
  in this story. `attribution` (the existing slot) is a `ReactNode`, not a
  string, and group play's transient reveal (`App.tsx`'s `GroupReveal`
  wrapper) does not pass `attribution` at all today - only solo's
  `PersonalSummary` does, and it has no "carved by ... & crew" text to give
  (solo has no crew). Threading a real plain-text byline through Solo.tsx
  and/or App.tsx would touch files outside this story's `Reveal.tsx` +
  `web/src/gallery/` footprint for behavior neither caller currently has a
  faithful string to supply. Per this story's brief, the saved image
  presently renders the title + coral story faithfully with NO byline (a
  valid image per AC-02's "when present" wording); wiring a real byline into
  Solo.tsx/App.tsx is left as a small, disjoint follow-up (a natural fit for
  story 02 or its own tiny follow-up, since it only needs a one-line prop
  addition once a caller has plain-text byline content to give).

## Tests
| AC | Test |
|---|---|
| AC-01 | manual: Reveal screen shows the save action as secondary, not competing with "Play another round" |
| AC-02 | manual: rendered image visually matches title, coral words, byline, and tablet styling against the live screen |
| AC-03 | manual: timed render on a throttled mobile device profile; loading state shown if render exceeds a moment |
| AC-04 | manual: confirm no rejected/unfiltered word ever appears (image only ever renders already-vetted `AssembledStory` content) |
| AC-05 | manual: inspect rendered image for any PII field; confirm only nickname + Guardian variant appear |
| AC-06 | manual: confirm the render happens client-side with no network call; if a fallback was needed, confirm it is documented here |

## Dependencies
- the-reveal/01-text-reveal
- child-safety/01-profanity-filter
