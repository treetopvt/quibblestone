# Feature: Design System & UI Foundation

## Summary
The MUI theme built from the design-pack brand tokens, the shared AppBar and
Button contracts used on every screen, the reusable `Guardian` mascot component
(6 variants), and the hero mascot asset. Without this, no screen can be built
faithfully; it is the Slice-1 prerequisite for all web UI work.

## README reference
README section 10 (Design Brief: "Land the look-and-feel as an MUI theme -
palette, typography, shape/radius, component overrides - so the gap between
design and code stays small"). CLAUDE.md section 4: "Web styling lives in the
MUI theme. No hardcoded colors or pixel spacing in components."

## Stories
- [ ] 01 - MUI theme, AppBar, and Button shell
- [ ] 02 - Guardian avatar component (6 variants)
- [ ] 03 - Orientation: prefer portrait, stay readable in landscape

## Dependencies
None (this is the UI foundation everything else renders on top of).

## Design notes
- All token values come from `docs/design/README.md` (Global System) and
  `docs/design/DESIGN_RULES.md`. Those documents are authoritative; do not
  invent values.
- The MUI theme is the single place colors, typography, radii, and shadows live.
  Every screen imports components, not inline styles. This is the contract
  enforced by CLAUDE.md section 4.
- The AppBar and primary/secondary Button are **fixed contracts** (design pack
  Gotchas: "reuse a single Button and AppBar component; don't re-spec per
  screen"). They must be implemented once and used everywhere.
- The `Guardian` component (story 02) is a shared avatar consumed by Join,
  Lobby, Waiting, and Round Complete screens. It must be built before those
  screens can be built.
- The hero mascot (Home, Waiting) is a larger illustrated variant of the same
  character. It can ship as an optimized SVG asset or illustrated component;
  it is in scope for story 01 as an asset, not a parameterised component.

## Parked - Phase 2+
- **Client routing with react-router** (#59): replace the single-`view`-state
  navigation in `web/src/App.tsx` with real routes - URLs, browser back/forward,
  refresh-to-current-screen, and a deep-link join URL (`/join/:code`). Decided (the
  overhead is worth it). Keep `useGameHub` mounted once ABOVE the router so the one
  SignalR connection is never remounted/duplicated; hub/API URLs still from
  `import.meta.env`. Pairs with the parked reconnect hardening (session-engine).

## Parked - Phase 4
- Dyslexia-friendly font option and reduced-motion variants of all animations
  (design pack Expansion Area 7). Record here; do not pull into Slice 1.
