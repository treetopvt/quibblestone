// ----------------------------------------------------------------------------
//  theme.ts - the single home for QuibbleStone's look and feel.
//
//  Per the design brief (README section 10) the target is bright, playful,
//  chunky, high-contrast, big-tap-target: a deliberate departure from
//  restrained professional tooling. We land that as an MUI theme (palette,
//  typography, shape, component defaults) rather than a bespoke design system,
//  so the gap between design and code stays small.
//
//  This implements the design-system/01 story: the "playful storybook-fantasy"
//  identity from docs/design/README.md (Global System) and DESIGN_RULES.md -
//  a glowing carved stone-tablet motif on a warm parchment background. Token
//  values here are the AUTHORITATIVE source for every screen; do not
//  re-specify hex colors or pixel spacing in components - extend this file or
//  pull from `theme.palette` / `theme.spacing` / `theme.shape` instead.
//
//  Fonts: Fredoka (headings/buttons/numbers) and Nunito (body/labels/captions)
//  are loaded from Google Fonts via <link> tags in web/index.html - this file
//  only references the family names, it does not load font files itself.
//
//  Custom palette additions (parchmentGradient, gold CTA, coral, teal, stone
//  tones) live under `theme.palette` via TypeScript module augmentation below,
//  so every component can read them in a type-safe way instead of importing
//  raw hex values.
// ----------------------------------------------------------------------------

import { createTheme, alpha } from '@mui/material/styles';

// ----------------------------------------------------------------------------
//  Design tokens - authoritative values from docs/design/README.md (Global
//  System - Palette) and docs/design/DESIGN_RULES.md. Trust these over
//  eyeballing screenshots.
// ----------------------------------------------------------------------------
const tokens = {
  parchment: {
    top: '#F8F1E2',
    mid: '#F6EEDD',
    bottom: '#F0E6D0',
  },
  sandstone: '#E8DCC4',
  card: '#ECE2CC',
  // Stone-tablet motif gradient stops (docs/design/README.md "Tablet gradient",
  // 168deg) - the arched glowing-stone panel used by the Home hero (and later
  // the Lobby/Reveal tablets). Kept as tokens so screens never hardcode them.
  tabletTop: '#EFE3C7',
  tabletMid: '#E3D2AC',
  tabletBottom: '#D6C194',
  // Bottom action bar fade-scrim target color (docs/design/README.md
  // "Bottom action bar pattern") - distinct from the card fill.
  bottomBarScrim: '#F2E8D2',
  stoneSlot: '#DCCFB0',
  stoneSlotAlt: '#DFD2B4',
  textPrimary: '#2B2622',
  textMutedStrong: 'rgba(43,38,34,.66)',
  textMutedSoft: 'rgba(43,38,34,.5)',
  purple: '#6C4BD8',
  goldTop: '#FFC24E',
  goldMain: '#FFB22E',
  goldDeep: '#E89A12',
  goldDeeper: '#B07908',
  coral: '#FF6B57',
  teal: '#2FB8A0',
  tealDeep: '#1F8A78',
  stoneEdge: '#B49B6E',
  // Guardian variant accent colors (docs/design/README.md Shared Component:
  // Guardian; matches the eye/feature color each variant uses in
  // web/src/components/Guardian.tsx). Used as the SOLID accent - tile
  // backgrounds tint these with alpha (see guardianAccent below), so the Join
  // avatar grid (session-engine/05) and the Lobby roster (session-engine/03)
  // stay theme-driven and DRY instead of each re-deriving a tint.
  guardianPurple: '#6C4BD8',
  guardianGold: '#E89A12',
  guardianCoral: '#FF6B57',
  guardianTeal: '#2FB8A0',
  guardianSand: '#7C6442',
  guardianPlum: '#9B7BE0',
  // App-bar icon-button fill (DESIGN_RULES "Consistent App Bar"): a
  // translucent wash of the warm-dark-brown text color.
  appBarIconFill: 'rgba(43,38,34,.07)',
  appBarIconFillHover: 'rgba(43,38,34,.14)',
} as const;

// Module augmentation: extend MUI's palette so QuibbleStone-specific tokens
// (parchment gradient stops, the gold CTA scale, accent colors) are available
// as `theme.palette.parchment`, `theme.palette.gold`, etc. with full type
// safety - no raw hex literals at call sites.
// One Guardian variant's theme-driven accent: a solid `main` (matches the
// Guardian component's eye/feature color for that variant) plus a
// pre-computed `tileTint` - a light alpha wash of that color used as an
// avatar tile's background (Join grid, Lobby roster). Keeping the tint here
// (not re-derived with `alpha()` at each call site) is what keeps those two
// screens DRY and theme-driven per CLAUDE.md section 4.
interface GuardianAccentEntry {
  main: string;
  tileTint: string;
}
type GuardianAccentPalette = Record<
  'purple' | 'gold' | 'coral' | 'teal' | 'sand' | 'plum',
  GuardianAccentEntry
>;

declare module '@mui/material/styles' {
  interface Palette {
    parchment: { top: string; mid: string; bottom: string; gradient: string };
    sandstone: Palette['primary'];
    card: { main: string };
    tablet: { top: string; mid: string; bottom: string; gradient: string };
    stoneSlot: { main: string; alt: string };
    gold: Palette['primary'];
    coral: Palette['primary'];
    teal: Palette['primary'];
    stoneEdge: { main: string };
    appBarIcon: { fill: string; hoverFill: string };
    bottomBarScrim: { main: string };
    /** Guardian variant accent colors + tile tints (session-engine/05). */
    guardianAccent: GuardianAccentPalette;
  }
  interface PaletteOptions {
    parchment?: { top: string; mid: string; bottom: string; gradient: string };
    sandstone?: PaletteOptions['primary'];
    card?: { main: string };
    tablet?: { top: string; mid: string; bottom: string; gradient: string };
    stoneSlot?: { main: string; alt: string };
    gold?: PaletteOptions['primary'];
    coral?: PaletteOptions['primary'];
    teal?: PaletteOptions['primary'];
    stoneEdge?: { main: string };
    appBarIcon?: { fill: string; hoverFill: string };
    bottomBarScrim?: { main: string };
    guardianAccent?: GuardianAccentPalette;
  }
}

const parchmentGradient = `linear-gradient(180deg, ${tokens.parchment.top} 0%, ${tokens.parchment.mid} 42%, ${tokens.parchment.bottom} 100%)`;
const tabletGradient = `linear-gradient(168deg, ${tokens.tabletTop} 0%, ${tokens.tabletMid} 52%, ${tokens.tabletBottom} 100%)`;

export const theme = createTheme({
  palette: {
    primary: { main: tokens.purple },
    secondary: { main: tokens.goldMain },
    success: { main: tokens.teal },
    error: { main: tokens.coral },
    text: {
      primary: tokens.textPrimary,
      secondary: tokens.textMutedStrong,
    },
    background: { default: tokens.parchment.mid, paper: tokens.card },
    parchment: { ...tokens.parchment, gradient: parchmentGradient },
    sandstone: { main: tokens.sandstone },
    card: { main: tokens.card },
    tablet: {
      top: tokens.tabletTop,
      mid: tokens.tabletMid,
      bottom: tokens.tabletBottom,
      gradient: tabletGradient,
    },
    stoneSlot: { main: tokens.stoneSlot, alt: tokens.stoneSlotAlt },
    gold: { main: tokens.goldMain, light: tokens.goldTop, dark: tokens.goldDeep },
    coral: { main: tokens.coral },
    teal: { main: tokens.teal, dark: tokens.tealDeep },
    stoneEdge: { main: tokens.stoneEdge },
    appBarIcon: { fill: tokens.appBarIconFill, hoverFill: tokens.appBarIconFillHover },
    bottomBarScrim: { main: tokens.bottomBarScrim },
    // Guardian variant accents (session-engine/05, docs/design/Join.dc.html
    // avatar grid): each tile's background is a light alpha wash of the
    // variant's accent color. Alpha values match the design reference exactly
    // (purple .12, gold .14, coral .13, teal .14, sand .10, plum .14).
    guardianAccent: {
      purple: { main: tokens.guardianPurple, tileTint: alpha(tokens.guardianPurple, 0.12) },
      gold: { main: tokens.guardianGold, tileTint: alpha(tokens.goldMain, 0.14) },
      coral: { main: tokens.guardianCoral, tileTint: alpha(tokens.guardianCoral, 0.13) },
      teal: { main: tokens.guardianTeal, tileTint: alpha(tokens.guardianTeal, 0.14) },
      sand: { main: tokens.guardianSand, tileTint: alpha(tokens.guardianSand, 0.1) },
      plum: { main: tokens.guardianPlum, tileTint: alpha(tokens.guardianPlum, 0.14) },
    },
  },
  shape: {
    borderRadius: 20, // large rounded buttons/cards default; see DESIGN_RULES card radius (24px) for cards specifically
  },
  spacing: 4, // MUI default scale (1 = 4px); screen horizontal padding (22px/20px) is applied per-screen via this scale
  typography: {
    fontFamily: '"Nunito", system-ui, -apple-system, sans-serif',
    h1: { fontFamily: '"Fredoka", sans-serif', fontWeight: 700 },
    h2: { fontFamily: '"Fredoka", sans-serif', fontWeight: 700 },
    h3: { fontFamily: '"Fredoka", sans-serif', fontWeight: 600 },
    h4: { fontFamily: '"Fredoka", sans-serif', fontWeight: 600 },
    h5: { fontFamily: '"Fredoka", sans-serif', fontWeight: 600 },
    h6: { fontFamily: '"Fredoka", sans-serif', fontWeight: 600 },
    subtitle1: { fontFamily: '"Nunito", sans-serif', fontWeight: 700 },
    subtitle2: { fontFamily: '"Nunito", sans-serif', fontWeight: 600 },
    body1: { fontFamily: '"Nunito", sans-serif', fontWeight: 600 },
    body2: { fontFamily: '"Nunito", sans-serif', fontWeight: 600 },
    button: { fontFamily: '"Fredoka", sans-serif', fontWeight: 600, textTransform: 'none', fontSize: 20 },
    caption: { fontFamily: '"Nunito", sans-serif', fontWeight: 700 },
    overline: { fontFamily: '"Nunito", sans-serif', fontWeight: 800, letterSpacing: 1.4 },
  },
  components: {
    // Parchment gradient background (AC-01): set once on body via CssBaseline
    // so every screen inherits it without a per-page wrapper.
    MuiCssBaseline: {
      styleOverrides: {
        body: {
          background: parchmentGradient,
          backgroundAttachment: 'fixed',
          minHeight: '100vh',
        },
      },
    },
    // Shared AppBar recipe (AC-03, DESIGN_RULES "Consistent App Bar"). The
    // <AppBar> component (web/src/components/AppBar.tsx) renders MUI's
    // AppBar; all visual tokens live here so every usage inherits them.
    MuiAppBar: {
      defaultProps: {
        elevation: 0,
        color: 'transparent',
        position: 'static',
      },
      styleOverrides: {
        root: {
          backgroundColor: 'transparent',
          backgroundImage: 'none',
          boxShadow: 'none',
          color: tokens.textPrimary,
        },
      },
    },
    MuiToolbar: {
      styleOverrides: {
        root: {
          display: 'flex',
          alignItems: 'center',
          gap: 10,
          padding: '6px 16px 8px',
          minHeight: 'unset',
          '@media (min-width: 0px)': {
            minHeight: 'unset',
          },
        },
      },
    },
    // App-bar icon buttons (42x42, radius 14, translucent warm-brown fill,
    // AC-03) are styled locally in <AppBar> via sx + theme tokens, NOT a global
    // MuiIconButton override - that would leak the app-bar recipe onto every
    // IconButton across the app. Keep the recipe where it belongs (AppBar.tsx).
    // Button family (AC-04 gold CTA / AC-05 outlined purple secondary).
    // DESIGN_RULES "Consistent Buttons" - fixed contracts, never re-specified
    // per screen. Every <Button variant="contained"> is the gold primary CTA;
    // every <Button variant="outlined"> is the purple secondary.
    MuiButton: {
      defaultProps: {
        disableElevation: true,
      },
      styleOverrides: {
        root: {
          borderRadius: 20,
          gap: 11,
          fontFamily: '"Fredoka", sans-serif',
          fontWeight: 600,
          fontSize: 20,
          letterSpacing: 0.3,
          textTransform: 'none',
          minWidth: 0,
        },
        // Gold primary CTA (AC-04).
        contained: {
          height: 62,
          border: 'none',
          background: `linear-gradient(180deg, ${tokens.goldTop} 0%, ${tokens.goldMain} 100%)`,
          color: tokens.textPrimary,
          boxShadow: '0 12px 22px -8px rgba(255,178,46,.85), inset 0 2px 0 rgba(255,255,255,.5)',
          '&:hover': {
            background: `linear-gradient(180deg, ${tokens.goldTop} 0%, ${tokens.goldMain} 100%)`,
            boxShadow: '0 14px 26px -8px rgba(255,178,46,.95), inset 0 2px 0 rgba(255,255,255,.5)',
          },
          '&:active': {
            boxShadow: '0 8px 16px -8px rgba(255,178,46,.85), inset 0 2px 0 rgba(255,255,255,.5)',
          },
          '&.Mui-disabled': {
            background: tokens.stoneSlotAlt,
            color: tokens.textMutedSoft,
            boxShadow: 'none',
          },
        },
        // Outlined purple secondary (AC-05).
        outlined: {
          height: 60,
          border: `2.5px solid ${tokens.purple}`,
          background: 'rgba(108,75,216,.06)',
          color: tokens.purple,
          '&:hover': {
            border: `2.5px solid ${tokens.purple}`,
            background: 'rgba(108,75,216,.12)',
          },
          '&.Mui-disabled': {
            border: `2.5px solid ${tokens.textMutedSoft}`,
            color: tokens.textMutedSoft,
          },
        },
      },
    },
  },
});
