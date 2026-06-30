// ----------------------------------------------------------------------------
//  BottomActionBar - pinned action bar with a fade scrim over scroll content.
//
//  Per docs/design/README.md (Global System - "Bottom action bar pattern"):
//  screens with pinned actions (FillBlank, Reveal, RoundComplete) put their
//  button(s) in an absolutely-positioned bar at the bottom, fading the
//  scrollable content beneath them into the parchment background so nothing
//  looks clipped: `linear-gradient(180deg, transparent 0%, #F2E8D2 ~30%)`.
//
//  This is a thin LAYOUT WRAPPER, not a theme override (per
//  docs/features/design-system/01-mui-theme-and-app-shell.md Technical Notes)
//  - it has no opinion on what buttons it holds, it just:
//    1. renders the actions pinned to the bottom of its nearest positioned
//       ancestor, with the fade scrim and horizontal/safe-area padding, and
//    2. exports a companion `<BottomActionBarSpacer/>` sized to the bar's
//       reserved height (including the iOS safe-area inset) so scrollable
//       content placed above it never hides behind it (AC-06).
//
//  Usage (the nearest ancestor must be `position: relative` or similar):
//    <Box sx={{ position: 'relative', minHeight: '100dvh' }}>
//      <YourScrollableContent />
//      <BottomActionBarSpacer />
//      <BottomActionBar>
//        <Button variant="contained">Next word</Button>
//      </BottomActionBar>
//    </Box>
// ----------------------------------------------------------------------------

import type { ReactNode } from 'react';
import { Box, useTheme } from '@mui/material';

export interface BottomActionBarProps {
  children: ReactNode;
}

/**
 * Vertical room (button height + scrim + padding) the bar occupies. Used by
 * both the bar itself and its companion spacer so content never sits behind
 * it (AC-06). Sized for the tallest button contract (62px gold CTA) plus the
 * pattern's `12px 22px 22px` padding.
 */
const BAR_RESERVED_HEIGHT = 12 + 62 + 22;

/**
 * Reserves vertical room equal to the bar's height (including iOS safe-area
 * inset) so the last bit of scrollable content above a <BottomActionBar>
 * never hides behind it. Place immediately above the bar, inside the same
 * scroll container as the content.
 */
export function BottomActionBarSpacer() {
  return (
    <Box
      aria-hidden
      sx={{
        height: `calc(${BAR_RESERVED_HEIGHT}px + env(safe-area-inset-bottom, 0px))`,
        flexShrink: 0,
      }}
    />
  );
}

export function BottomActionBar({ children }: BottomActionBarProps) {
  const theme = useTheme();

  return (
    <Box
      sx={{
        position: 'absolute',
        left: 0,
        right: 0,
        bottom: 0,
        display: 'flex',
        flexDirection: 'column',
        gap: theme.spacing(3.5), // 14px, matches the design pack's action stack gap
        // Fade scrim: transparent -> bottom-bar scrim tone, per spec.
        background: `linear-gradient(180deg, transparent 0%, ${theme.palette.bottomBarScrim.main} 30%)`,
        pt: theme.spacing(3), // 12px
        pl: 'max(22px, env(safe-area-inset-left))',
        pr: 'max(22px, env(safe-area-inset-right))',
        pb: 'calc(22px + env(safe-area-inset-bottom, 0px))',
      }}
    >
      {children}
    </Box>
  );
}
