# Shared UI - the component library

This folder is QuibbleStone's **single shared-component surface**. The goal is to
build each piece once and reuse it on every screen, so the app keeps one
consistent, family-friendly look (README section 10) without re-specifying
styling per screen.

Import shared UI from the barrel, not from deep paths:

```ts
import { AppBar, BottomActionBar, Guardian, HeroGuardian } from '../components';
```

## What lives here

| Component | Use | Notes |
|---|---|---|
| `AppBar` | top app bar on every screen | centered Fredoka title, 42x42 icon slots; pass `leftAction` / `rightAction` (FontAwesome icon names) |
| `BottomActionBar` (+ `BottomActionBarSpacer`) | pinned bottom actions | reserves room + fade scrim so content never hides behind it |
| `Guardian` | the small stone-guardian avatar | 6 variants (`purple/gold/coral/teal/sand/plum`) + `size`; inline SVG |
| `ConnectionStatus` | real-time connection readout | presentational |
| `HeroGuardian` | the full-size hero mascot | lives in `../assets` (illustrative art), re-exported from the barrel |

The two stone-guardian assets (`Guardian` avatar, `HeroGuardian` hero) hardcode
their SVG colors **on purpose** - illustrative art is not theme chrome. That is
the one documented exception to the rule below.

## The reuse rules (how the look stays consistent)

1. **Look-and-feel lives in the MUI theme** (`web/src/theme.ts`), never inline in
   a component. No hex literals, no raw-px spacing in a component - add the token
   to the theme and pull it from there. The gold CTA and outlined-purple buttons
   are `MuiButton` theme overrides, so every `<Button variant="contained">` is the
   gold CTA and every `<Button variant="outlined">` is the purple secondary,
   app-wide, with no per-screen styling.
2. **Reuse these contracts; never re-spec per screen.** A screen needs a new
   look-and-feel? That is a theme change here, not an inline style there.
3. **FontAwesome only** for icons, registered once in `web/src/fontawesome.ts`.
4. **Add a new shared component** under `web/src/components/`, give it a verbose
   header comment, type its props as `interface {Component}Props`, and re-export
   it from `index.ts` so it joins the shared surface.

## Where the rest of the shared surface lives

- `web/src/theme.ts` - the styling contract (palette, Fredoka/Nunito type, radii,
  AppBar/Button overrides).
- `web/src/fontawesome.ts` - the single icon registry.
- `web/src/signalr/useGameHub.ts` - the one shared real-time connection.
- `web/src/engine/` - the pure game engine (template, assemble, mode).
