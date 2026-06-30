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

import { createTheme } from '@mui/material/styles';

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
  // App-bar icon-button fill (DESIGN_RULES "Consistent App Bar"): a
  // translucent wash of the warm-dark-brown text color.
  appBarIconFill: 'rgba(43,38,34,.07)',
  appBarIconFillHover: 'rgba(43,38,34,.14)',
} as const;

// Module augmentation: extend MUI's palette so QuibbleStone-specific tokens
// (parchment gradient stops, the gold CTA scale, accent colors) are available
// as `theme.palette.parchment`, `theme.palette.gold`, etc. with full type
// safety - no raw hex literals at call sites.
declare module '@mui/material/styles' {
  interface Palette {
    parchment: { top: string; mid: string; bottom: string; gradient: string };
    sandstone: Palette['primary'];
    card: { main: string };
    stoneSlot: { main: string; alt: string };
    gold: Palette['primary'];
    coral: Palette['primary'];
    teal: Palette['primary'];
    stoneEdge: { main: string };
    appBarIcon: { fill: string; hoverFill: string };
    bottomBarScrim: { main: string };
  }
  interface PaletteOptions {
    parchment?: { top: string; mid: string; bottom: string; gradient: string };
    sandstone?: PaletteOptions['primary'];
    card?: { main: string };
    stoneSlot?: { main: string; alt: string };
    gold?: PaletteOptions['primary'];
    coral?: PaletteOptions['primary'];
    teal?: PaletteOptions['primary'];
    stoneEdge?: { main: string };
    appBarIcon?: { fill: string; hoverFill: string };
    bottomBarScrim?: { main: string };
  }
}

const parchmentGradient = `linear-gradient(180deg, ${tokens.parchment.top} 0%, ${tokens.parchment.mid} 42%, ${tokens.parchment.bottom} 100%)`;

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
    stoneSlot: { main: tokens.stoneSlot, alt: tokens.stoneSlotAlt },
    gold: { main: tokens.goldMain, light: tokens.goldTop, dark: tokens.goldDeep },
    coral: { main: tokens.coral },
    teal: { main: tokens.teal, dark: tokens.tealDeep },
    stoneEdge: { main: tokens.stoneEdge },
    appBarIcon: { fill: tokens.appBarIconFill, hoverFill: tokens.appBarIconFillHover },
    bottomBarScrim: { main: tokens.bottomBarScrim },
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
    // App-bar icon buttons: 42x42, radius 14, translucent warm-brown fill
    // (AC-03). IconButton is also used elsewhere, so this targets the
    // dedicated "appBarIcon" size/color combo via a variant-free override is
    // not possible globally - the <AppBar> component applies these via sx
    // using theme tokens (see AppBar.tsx) to avoid leaking this recipe onto
    // unrelated icon buttons across the app.
    MuiIconButton: {
      styleOverrides: {
        root: {
          borderRadius: 14,
        },
      },
    },
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
        // Filled purple (used for e.g. the Lobby "Share" action per the design
        // pack) - not one of this story's two mandated contracts, but kept
        // here alongside its siblings so any future usage stays theme-driven.
        text: {
          height: 60,
          color: tokens.purple,
        },
      },
    },
  },
});
