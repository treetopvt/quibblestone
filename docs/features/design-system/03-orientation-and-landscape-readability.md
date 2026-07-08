# Story: Orientation - prefer portrait, stay readable in landscape

**Feature:** Design System & UI Foundation  ·  **Status:** Complete  <!-- Not Started | In Progress | Complete | Blocked | Dropped -->  ·  **Issue:** TBD

## Context
Every screen is built portrait-first (a centered `maxWidth: 430` column). That is
the right default - QuibbleStone is a phone game - but real play breaks the
assumption: a kid hands the phone to a parent, it auto-rotates to landscape, and the
finished tale on the Reveal screen becomes unreadable. The culprit is concrete: the
Reveal's story panel is hard-capped at `maxHeight: '48vh'` (`web/src/pages/Reveal.tsx`),
so on a short landscape viewport the tale collapses into a thin sliver squeezed
between the fixed narration bar and the pinned action bar, in a narrow column with
empty gutters on both sides. There is also no PWA manifest, so nothing even expresses
a portrait preference. This story lands the app-wide orientation posture: PREFER
portrait (via the web app manifest) AND degrade gracefully so a rotated phone stays
usable - with the Reveal (the payoff moment a phone is literally handed around,
README section 10) as the acceptance-critical screen. See [feature.md](./feature.md)
and `the-reveal/01` (the screen this fixes).

## Acceptance Criteria
- [x] AC-01: Given the app is installed as a PWA, then a web app manifest
      (`manifest.webmanifest`, linked from `index.html`) declares
      `"orientation": "portrait"` so an installed instance opens and stays portrait -
      the "prefer portrait" layer.
- [x] AC-02: Given a browser that ignores the manifest orientation (a NON-installed
      tab, and notably iOS Safari, which does not honor manifest orientation), when
      the device rotates to landscape, then the app does not break - it degrades
      gracefully: content stays readable, primary CTAs stay reachable, and nothing is
      trapped in an unreadable sliver. (This is why the story is "both", not a
      portrait lock: a handed-off phone WILL sometimes land in landscape regardless.)
- [x] AC-03: Given the Reveal screen in landscape, when the completed tale is shown,
      then the story panel is NOT hard-capped to a tiny fraction of the short
      viewport - it reflows to use the available width and height so the tale reads
      and scrolls normally without pinch-zoom, and the two primary CTAs (Play again /
      Share) remain reachable. The `48vh` cap that caused the sliver is replaced with
      an orientation-aware height.
- [x] AC-04: Given the app in PORTRAIT, then this story changes nothing visible -
      portrait rendering is identical to today. This is additive, orientation-scoped
      responsive handling (CSS `@media (orientation: landscape)` or MUI equivalents),
      not a portrait redesign; no portrait pixel moves.
- [x] AC-05: Given the landscape adaptations, then they are theme-driven and honor
      the existing contracts: no hardcoded hex or ad-hoc raw-px colors (CLAUDE.md
      section 4), big tap targets preserved (README section 10), FontAwesome-only
      icons, and any motion still respects `prefers-reduced-motion`.
- [x] AC-06 (child-safety / privacy): Given this is layout-only, then it introduces
      no new free-text surface and collects no data - the manifest carries only public
      app metadata (name, icons, theme color), never PII (README sections 3 and 6).

## Out of Scope
- A full landscape/tablet/desktop REDESIGN (multi-column dashboards, side-by-side
  panes for every screen). The bar here is "portrait unchanged, landscape usable and
  the Reveal readable", not a bespoke landscape art direction.
- A hard, JS-enforced orientation LOCK or a "please rotate your device" blocker
  overlay - that fights the user (README section 10's friendly posture) and is exactly
  the dead end the "both" decision rejected. Prefer portrait, tolerate landscape.
- A service worker / full installability + offline pass - the manifest here only
  expresses metadata + orientation preference; the offline PWA is a separate concern.
- Reworking every other screen's landscape layout beyond "not broken" - the Reveal is
  the acceptance-critical screen (where the bug was hit); other screens must remain
  usable but are not individually redesigned here.

## Technical Notes
- **Manifest (AC-01):** add `web/public/manifest.webmanifest` (`name`, `short_name`,
  `start_url`, `display: standalone`, `orientation: portrait`, `theme_color` =
  `#6C4BD8` to match the existing `<meta name="theme-color">`, `background_color`, and
  `icons` reusing the existing `favicon.svg` / `apple-touch-icon.png` in `public/`).
  Link it from `index.html` with `<link rel="manifest" href="/manifest.webmanifest">`.
- **Why "both" and not a lock (AC-02):** manifest `orientation` only binds an INSTALLED
  PWA, and iOS Safari ignores it outright, so the graceful-landscape work is not
  optional - it is the real fix for the handed-off-phone case. Do both layers.
- **Reveal reflow (AC-03):** in `web/src/pages/Reveal.tsx`, replace the fixed
  `maxHeight: '48vh'` story-scroll cap with an orientation-aware height (larger share
  of the viewport in landscape), widen the outer `maxWidth: 430` container in landscape,
  and compact the celebratory header + narration bar vertical padding in landscape to
  reclaim space - all behind `@media (orientation: landscape)` so portrait is untouched
  (AC-04). Mind the `BottomActionBar`/`BottomActionBarSpacer` reserved-height interplay
  so CTAs are never covered.
- **Reuse, don't fork:** the portrait column pattern (`maxWidth: 430, mx: 'auto'`) is
  repeated across every page (`Home`, `Join`, `Lobby`, `FillBlank`, `Reveal`,
  `RoundComplete`, `Waiting`, `GroupRound`, `Solo`). This story fixes the acute case
  (Reveal); if a shared responsive container emerges, factor it in `web/src/components/`
  rather than copy-pasting media queries - but do not gold-plate every screen here.
- **No new dependency** - MUI's `sx` media queries (or `useMediaQuery('(orientation:
  landscape)')`) cover this; no new library on a PWA bundle.

## Tests
| AC | Test |
|---|---|
| AC-01 | manual: `manifest.webmanifest` is served and linked; installed PWA opens portrait; DevTools Application panel shows `orientation: portrait` |
| AC-02 | manual: rotate a NON-installed tab (and iOS Safari) to landscape; confirm the app stays usable, nothing trapped in a sliver |
| AC-03 | manual (landscape, phone or emulated): the Reveal story reads and scrolls without pinch-zoom; Play again / Share reachable |
| AC-04 | manual + code review: portrait rendering is unchanged; all landscape rules are behind `@media (orientation: landscape)` |
| AC-05 | code review: no hardcoded hex, big tap targets intact, reduced-motion still honored |
| AC-06 | code review: manifest holds only public metadata (no PII); no new text input introduced |

## Dependencies
- design-system/01-mui-theme-and-app-shell (the app shell, `index.html`, `BottomActionBar`, theme this builds on)
- the-reveal/01-text-reveal (the Reveal screen whose landscape readability is the acceptance-critical case)

## Delivered
- 2026-07 (commit `ead9ae4`, "fix(reveal): keep the completed tale readable in
  landscape + prefer portrait"): the PWA manifest (`web/public/manifest.webmanifest`,
  linked from `index.html`) declares `orientation: portrait` with the theme/background
  colors and existing icons (AC-01), and `web/src/pages/Reveal.tsx` replaces the
  hard `48vh` story-panel cap with orientation-aware `@media (orientation: landscape)`
  heights so a handed-off phone in landscape stays readable with the CTAs reachable
  (AC-02, AC-03); portrait rendering is unchanged (AC-04). Status trued up 2026-07-08
  (the story/roadmap had lagged the code). A manual landscape spot-check on a real
  phone is still worth doing before the beta.
