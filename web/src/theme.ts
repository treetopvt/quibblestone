// ----------------------------------------------------------------------------
//  theme.ts - the single home for Quibbler's look and feel.
//
//  Per the design brief (README section 10) the target is bright, playful,
//  chunky, high-contrast, big-tap-target: a deliberate departure from
//  restrained professional tooling. We land that as an MUI theme (palette,
//  typography, shape, component defaults) rather than a bespoke design system,
//  so the gap between design and code stays small.
//
//  This is a STARTING POINT, not the final identity. Iterate here as the visual
//  direction (and a possible mascot) firm up. Components should pull colors and
//  spacing from this theme rather than hardcoding them.
// ----------------------------------------------------------------------------

import { createTheme } from '@mui/material/styles';

export const theme = createTheme({
  palette: {
    // Placeholder playful palette - swap once brand colors are chosen.
    primary: { main: '#6C2BD9' }, // grape
    secondary: { main: '#FF7A1A' }, // tangerine
    success: { main: '#2BB673' },
    background: { default: '#FFF8F0', paper: '#FFFFFF' },
  },
  shape: {
    borderRadius: 16, // chunky, rounded corners
  },
  typography: {
    // Big, friendly type. Real font selection comes with the visual identity;
    // these names fall back to the system stack until web fonts are loaded.
    fontFamily: '"Baloo 2", "Nunito", system-ui, -apple-system, sans-serif',
    h1: { fontWeight: 800 },
    button: { fontWeight: 700, textTransform: 'none' },
  },
  components: {
    // Big tap targets by default (kid-and-family, often on phones).
    MuiButton: {
      defaultProps: { variant: 'contained', size: 'large' },
      styleOverrides: {
        root: { borderRadius: 999, paddingInline: 24, minHeight: 48 },
      },
    },
  },
});
