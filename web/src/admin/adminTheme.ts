// ----------------------------------------------------------------------------
//  adminTheme - a SEPARATE, deliberately un-playful MUI theme for operator-only
//  surfaces (currently just AdminBillingMode, billing-entitlements/07).
//
//  WHY THIS EXISTS (a deliberate exception to CLAUDE.md section 4's "one MUI
//  theme" rule): the player app is bright, chunky and parchment-warm - the
//  right voice for a family word game, the wrong voice for a back-office
//  operator console. Operator screens are desktop-first, dense, and read like a
//  tool, not a toy. Rather than bend the player theme (which every player screen
//  depends on), operator routes nest their OWN ThemeProvider with this theme, so
//  the two design languages evolve independently and a change here can never
//  regress a player screen.
//
//  This is a plain, neutral light theme: a system font stack (no Fredoka /
//  Nunito), a slate/blue neutral palette, standard MUI error/success/warning
//  semantics, tighter radii and flat buttons. It intentionally does NOT define
//  the player palette tokens (parchment, gold, teal, coral, stoneEdge, tablet,
//  card, ...): operator components use only the standard MUI slots, so this
//  theme stays a small, conventional surface.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { createTheme } from '@mui/material/styles';

const systemFont = [
  '-apple-system',
  'BlinkMacSystemFont',
  '"Segoe UI"',
  'Roboto',
  '"Helvetica Neue"',
  'Arial',
  'sans-serif',
].join(', ');

export const adminTheme = createTheme({
  palette: {
    mode: 'light',
    primary: { main: '#2563eb' }, // slate-blue: a neutral, tool-like accent
    error: { main: '#dc2626' }, // "go Live" / destructive
    success: { main: '#16a34a' }, // "Test mode" / safe
    warning: { main: '#d97706' }, // interim-surface / caution notices
    background: { default: '#eef1f5', paper: '#ffffff' },
    text: { primary: '#0f172a', secondary: '#475569' },
    divider: 'rgba(15, 23, 42, 0.12)',
  },
  typography: {
    fontFamily: systemFont,
    button: { textTransform: 'none', fontWeight: 600 },
  },
  shape: { borderRadius: 8 },
  components: {
    MuiButton: {
      defaultProps: { disableElevation: true },
      styleOverrides: {
        root: { borderRadius: 8, fontWeight: 600 },
        sizeLarge: { paddingBlock: 10 },
      },
    },
    MuiPaper: {
      styleOverrides: {
        root: { backgroundImage: 'none' },
      },
    },
  },
});
