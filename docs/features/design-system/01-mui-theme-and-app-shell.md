# Story: MUI theme, AppBar, and Button shell

**Feature:** Design System & UI Foundation  ·  **Status:** Complete

## Context
Every screen in the design pack shares the same palette, typefaces, radii,
shadows, AppBar recipe, and two button contracts (gold CTA + outlined-purple
secondary). CLAUDE.md section 4 mandates that styling lives in the MUI theme,
not in per-component inline styles. This story installs that foundation so every
subsequent UI story can import components instead of re-specifying tokens. Without
it, no screen matches the design. See [feature.md](./feature.md).

## Acceptance Criteria
- [x] AC-01: Given the app loads, then the background renders the parchment
      gradient (`#F8F1E2` to `#F6EEDD` to `#F0E6D0`, top to bottom) from the
      MUI theme palette, with no hardcoded color in any component.
      Implemented as the `tokens.parchment` scale and `theme.palette.parchment`
      module augmentation in `web/src/theme.ts`.
- [x] AC-02: Given any heading or button text, then it renders in Fredoka
      (weights 500/600/700); given any body or label text, then it renders in
      Nunito (weights 400/600/700/800). Both fonts load from Google Fonts via the
      app's font-loading mechanism (not inline in component files).
      Confirmed via the Google Fonts `<link>` in `web/index.html` and the
      typography section of `web/src/theme.ts`.
- [x] AC-03: Given a screen uses the shared `<AppBar>` component, then it
      renders exactly: `height` per design spec, icon buttons at 42x42 with
      `border-radius:14px` and `background:rgba(43,38,34,.07)`, centered Fredoka
      600 21px title, and a balancing 42px spacer on the side that has no action
      button - all sourced from theme overrides, not per-instance styles. See
      `docs/design/README.md` Global System - Consistent App Bar.
      Implemented in `web/src/components/AppBar.tsx` (`ICON_SLOT_SIZE`/
      `ICON_SLOT_RADIUS`, `theme.palette.appBarIcon`) plus the `MuiAppBar`
      theme override.
- [x] AC-04: Given a screen uses the gold primary CTA button, then it renders
      with `height:62px`, `border-radius:20px`, the gold gradient
      (`#FFC24E` to `#FFB22E`), warm-dark-brown text, Fredoka 600 20px label, and
      the specified box-shadow and inset highlight - all from a single reusable
      component. See `docs/design/DESIGN_RULES.md` Consistent Buttons.
      Implemented via the `MuiButton` `contained` variant override in
      `web/src/theme.ts` (`height: 62`, gold gradient tokens).
- [x] AC-05: Given a screen uses the outlined-purple secondary button, then it
      renders with `height:60px`, `border:2.5px solid #6C4BD8`,
      `border-radius:20px`, purple text, and Fredoka 600 20px label - from the
      same reusable component family, not re-specified per screen.
      Implemented via the `MuiButton` `outlined` variant override
      (`height: 60`) in `web/src/theme.ts`.
- [x] AC-06: Given the bottom action bar pattern (pinned actions), then scrollable
      content above it never hides behind it (the bar reserves vertical room and
      applies the fade scrim `transparent to #F2E8D2`). See `docs/design/README.md`
      Bottom action bar pattern.
      Implemented as `web/src/components/BottomActionBar.tsx` +
      `BottomActionBarSpacer` (shared `BAR_RESERVED_HEIGHT`).
- [x] AC-07: Given the app runs on a 390px-wide portrait viewport, then content
      uses 22px horizontal padding (20px on the Reveal screen) and respects iOS
      safe-area insets; no layout breaks at the target viewport. See
      `docs/design/README.md` Device frame.
      `env(safe-area-inset-*)` is applied in `BottomActionBar.tsx`; no
      automated viewport check exists (see Tests below).
- [x] AC-08: Given the hero mascot asset (the full-size, posed stone-guardian
      used on the Home and Waiting screens), then it is present in the repo as an
      optimized SVG or component and renders at its intended size without
      pixelation. See `docs/design/screens/01-home.png` and
      `docs/design/screens/05-waiting.png`.
      Implemented as `web/src/assets/HeroGuardian.tsx` (inline SVG component).

## Out of Scope
- The `Guardian` avatar component (6 variants) - that is story 02.
- Per-screen layout and content (each screen is its own story).
- Dyslexia-friendly / reduced-motion a11y passes (Phase 4, parked in
  feature.md).
- FontAwesome icon registration changes (already handled in
  `web/src/fontawesome.ts`; this story is about the MUI theme, not icon packs).

## Technical Notes
- All work is in `web/`. Theme lives in `web/src/theme.ts` (already exists per
  CLAUDE.md section 4 - extend it, do not create a parallel file).
- Token values: `docs/design/README.md` Global System palette table and
  typography section; `docs/design/DESIGN_RULES.md`. Trust the documented hex
  values over eyeballing screenshots.
- MUI component overrides for `MuiAppBar`, `MuiButton` (variant `contained` for
  gold CTA; variant `outlined` for secondary purple) live in the theme's
  `components` key so every usage inherits them automatically.
- The bottom action bar can be a thin layout wrapper component that applies the
  gradient scrim and reserves height; it is not a theme override.
- Google Fonts: add Fredoka and Nunito `<link>` tags to `web/index.html` or use
  `@fontsource` packages - whichever approach the project already uses. Do not
  bundle the font files; let the CDN serve them.
- Hero mascot: port from `docs/design/Home.dc.html` (inline SVG) into
  `web/src/assets/` or a dedicated component. The `.dc.html` files are design
  references only - do not copy production code from them; recreate in the
  project's idiom.
- FontAwesome icon style for app-bar icon buttons: `stroke:#2B2622;
  stroke-width:2.4` per design spec; use the appropriate FA icon variant.
- Ambient glow (most screens): `radial-gradient(circle, rgba(108,75,216,.2) 0%,
  rgba(255,178,46,.1) 45%, transparent 70%)` - suitable as a pseudo-element or
  decorative `<Box>` in per-screen layouts, not a global style.
- See `docs/design/README.md` Implementation Gotchas for animation rules
  (drive entrance pops with `transform:scale` only, never opacity keyframes on
  list items).

## Dependencies
- platform-devops/01-test-harness (nice to have, not blocking - theme can ship
  without tests wired, but visual regression is useful if available).

## Tests
No automated specs cover this story directly - the theme, AppBar, and button
contracts are visual/CSS concerns with no dedicated Vitest or Playwright spec.
`tests/smoke.spec.ts` (Playwright) exercises the app shell indirectly (it loads
the app and asserts it reaches "Connected"), but does not assert on theme
tokens, fonts, or the AppBar/button contracts. Gap: no visual regression or
theme-token assertions exist yet; consider a lightweight Vitest render check
or Playwright visual snapshot if this drifts.
