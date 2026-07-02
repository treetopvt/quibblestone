# Story: Guardian avatar component (6 variants)

**Feature:** Design System & UI Foundation  ·  **Status:** Complete

## Context
The stone-guardian mascot appears as a small avatar on four screens: the Join
avatar grid, the Lobby player tiles, the Waiting progress row, and the Round
Complete crew recap. It is a single reusable component parameterised by a
`variant` prop (6 values); building it once means each subsequent screen story
can import it without repeating SVG. The design spec names the six variants and
their distinguishing features. See [feature.md](./feature.md) and
`docs/design/README.md` Shared Component: Guardian.

## Acceptance Criteria
- [x] AC-01: Given `<Guardian variant="purple" />` (and each of `gold`, `coral`,
      `teal`, `sand`, `plum`), then it renders the correct distinguishing feature
      for that variant: purple - small square block on head; gold - gold zig-zag
      crown; coral - two small horns; teal - leaf sprout; sand - round stone ears;
      plum - single antenna with glowing dot.
      Implemented in `GuardianFeature` in `web/src/components/Guardian.tsx`,
      one case per variant matching the spec's distinguishing feature.
- [x] AC-02: Given any variant, then the common body renders: a sandstone
      rounded-square head (`#E0CDA0`, outline `#B49B6E`), two rounded-rect eyes
      in the variant's eye color, and a curved carved smile (`#7C6442`).
      Implemented in `GuardianBody` (`HEAD_FILL`/`HEAD_STROKE`/`SMILE_STROKE`
      constants match the spec hexes exactly).
- [x] AC-03: Given a `size` prop, then the component scales uniformly (the SVG
      viewBox is `0 0 56 56` and the component fills its box); the caller controls
      display size via a numeric pixel value or a CSS size string.
      `size` prop (default 56) is applied directly to the `<svg>` width/height,
      viewBox is `0 0 56 56`.
- [x] AC-04: Given the component is used for a player tile, then names and
      Guardian variants are kept consistent across screens per the canonical set
      (Pip=teal/host, Maple=gold, Bramble=coral, Wren=plum, Flint=sand,
      Juniper=purple); this consistency is enforced by using the same variant
      value the player chose at join, not by hard-coding the canonical names.
      `Guardian` takes `variant` as a prop with no hard-coded name mapping,
      satisfying the enforcement mechanism; consuming screens (session-engine)
      own the actual name-to-variant assignment.
- [x] AC-05: Given the component, then it renders as inline SVG (no external
      image request) so it works offline and at any resolution.
      Confirmed - `Guardian` returns a plain inline `<svg>` tree, no `<img>` or
      external asset reference.

## Out of Scope
- The full-size hero mascot (that is in story 01 - it is a separate, more
  detailed illustrated asset, not a `Guardian` variant).
- Animation on the Guardian itself (idle bob, reactions, etc.) - animations
  belong to the screens that use it, not the component (keeps it composable).
- More than 6 variants in Slice 1.

## Technical Notes
- Implement as `web/src/components/Guardian.tsx` (or
  `web/src/components/guardian/Guardian.tsx`). Export a named component.
- Use the SVG viewBox `0 0 56 56` from the spec. The caller wraps it in a
  sized container (e.g. a 74px circle tile on Lobby). The component should
  accept a `size` prop and apply it as `width`/`height` directly.
- Reference `docs/design/Guardian.dc.html` for the SVG body geometry; the
  `.dc.html` is a design reference, not production code - recreate using React
  SVG idioms (no inline HTML, no `dangerouslySetInnerHTML`).
- Eye color per variant: purple `#6C4BD8`, gold `#E89A12`, coral `#FF6B57`,
  teal `#2FB8A0`, sand `#7C6442`, plum `#9B7BE0`.
- Distinguishing features are purely visual; SVG path/shape details are in
  `docs/design/Guardian.dc.html` and `docs/design/README.md`.
- Keep the SVG clean (no inline styles that duplicate theme tokens). Colors can
  be hardcoded SVG `fill`/`stroke` attributes since the SVG is illustrative, not
  theme-driven UI chrome.
- See also `docs/design/screens/02-join.png` (avatar grid), `03-lobby.png`
  (player tiles), `05-waiting.png` (progress row), `07-roundcomplete.png`
  (crew recap).

## Dependencies
- design-system/01-mui-theme-and-app-shell (project must be set up before
  adding components into it).

## Tests
No dedicated spec exists for `web/src/components/Guardian.tsx` (no
`Guardian.test.tsx`/`.test.ts`). All 6 variants and the common body are
implemented and read as complete by inspection, but marking this In Review
rather than Complete until a render/snapshot test (or at least a per-variant
assertion) backs AC-01/AC-02. Gap: add a Vitest + Testing Library (or
Playwright) spec asserting each variant renders its distinguishing feature
and the shared body colors.
