// ----------------------------------------------------------------------------
//  GameSettingsSheet - the Lobby's collapsed "Game settings" bottom sheet
//  (screen de-clutter / fit-to-viewport redesign, 2026-07).
//
//  Why this exists: the Waiting room (Lobby.tsx) must fit ONE phone viewport
//  with NO page scroll. Before this component, the host's family-safe toggle,
//  story-length choice, mode picker, and "Play a favorite" panel were stacked
//  inline in a scrollable area - four chunky controls that pushed the pinned
//  Start CTA well past the fold on a real phone. This component moves all of
//  that into a slide-up sheet that only opens on demand (tapping the new
//  collapsed "Game settings" row on Lobby), so the main screen stays a fixed-
//  height, non-scrolling layout while the tall controls still have all the
//  room they need INSIDE the sheet (which may scroll internally).
//
//  This is a pure LAYOUT/CHROME wrapper, not a new settings system: every
//  control it renders is an EXISTING shared component used exactly as it was
//  inline on Lobby - <FamilySafeToggle>, <StoryLengthChoice>, <ModePicker>,
//  and the host's "Play a favorite" <FavoritesList> panel - passed through as
//  props/children so Lobby keeps owning all of the actual state (familySafe,
//  lengthPref, mode, showFavoritePicker, favoriteError). This component never
//  reads or writes that state itself; it only decides WHEN those controls are
//  visible (sheet open) and provides the slide-up chrome + scrim + "Done" CTA.
//
//  Built on MUI's <Drawer anchor="bottom"> (not a bespoke sheet): Drawer
//  already gives us the dim scrim, the slide transition, and tap-scrim-to-
//  close for free, matching the ~0.28s ease the design calls for via MUI's
//  default transition duration. Content inside can scroll independently of
//  the page (its own `overflowY: auto`), so a tall stack of controls never
//  forces the PAGE to scroll - only the sheet's own body does, and only when
//  it does not fit the sheet's own max height.
//
//  Styling: theme tokens only (no hex/raw-px literals) - the sheet surface
//  uses `card.main` + the shared `stoneEdge`-tinted border language already
//  established on Lobby's collapsed settings row, so the sheet reads as a
//  continuation of that row rather than a new visual system. FontAwesome
//  icons only, already registered in web/src/fontawesome.ts. Big tap targets:
//  the gold "Done" button is a full `variant="contained"` CTA, matching the
//  weight of every other primary action in the app.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import type { ReactNode } from 'react';
import { useEffect, useRef, useState } from 'react';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { Box, Drawer, Typography, Button } from '@mui/material';
import { alpha, useTheme } from '@mui/material/styles';

export interface GameSettingsSheetProps {
  /** Whether the sheet is open (Lobby owns this as local state). */
  open: boolean;
  /** Close the sheet - wired to both the scrim tap and the "Done" button. */
  onClose: () => void;
  /**
   * The settings controls to render inside the sheet, in order (the
   * family-safe toggle, the story-length choice, the mode picker, and the
   * host's "Play a favorite" panel) - all EXISTING components, passed through
   * as children so this component has no opinion on which controls exist.
   */
  children: ReactNode;
}

/**
 * The Lobby's host-only settings bottom sheet: a slide-up panel over a dim
 * scrim holding the round-setup controls that used to live inline on the
 * main screen. Tapping the scrim or the gold "Done" button closes it; the
 * controls themselves are supplied by the caller as `children` so Lobby keeps
 * full ownership of their state and behavior.
 *
 * When the controls are taller than the sheet, the region scrolls with the gold
 * "Done" button PINNED below it, and a "more below" scroll cue (a soft fade + a
 * downward chevron) appears at the foot of the scroll area so a player discovers
 * the mode options past Classic; the cue hides once they scroll to the bottom.
 */
export function GameSettingsSheet({ open, onClose, children }: GameSettingsSheetProps) {
  const theme = useTheme();

  // Scroll cue (discoverability): when the controls overflow the sheet, a player
  // might not realize there are more MODE options below Classic. `hasMoreBelow`
  // tracks whether the scroll area still has content past the fold, so a soft
  // fade + a downward chevron can hint "scroll for more" - and hide once the
  // player reaches the bottom.
  const scrollRef = useRef<HTMLDivElement>(null);
  const [hasMoreBelow, setHasMoreBelow] = useState(false);

  const updateScrollCue = () => {
    const el = scrollRef.current;
    if (!el) return;
    setHasMoreBelow(el.scrollTop + el.clientHeight < el.scrollHeight - 8);
  };

  // Re-check right after the sheet opens (its content has just laid out), so the
  // cue appears immediately when the controls overflow - before the player has
  // touched anything. A rAF waits one frame for the Drawer's slide-in layout.
  useEffect(() => {
    if (!open) return;
    const raf = requestAnimationFrame(updateScrollCue);
    return () => cancelAnimationFrame(raf);
  }, [open]);

  return (
    <Drawer
      anchor="bottom"
      open={open}
      onClose={onClose}
      transitionDuration={280}
      PaperProps={{
        sx: {
          borderTopLeftRadius: '24px',
          borderTopRightRadius: '24px',
          bgcolor: 'card.main',
          border: `1.5px solid ${alpha(theme.palette.stoneEdge.main, 0.22)}`,
          borderBottom: 'none',
          maxWidth: 430,
          mx: 'auto',
          maxHeight: '86dvh',
          display: 'flex',
          flexDirection: 'column',
        },
      }}
    >
      {/* A small drag-handle affordance so the sheet reads as swipeable chrome
          even though the close gesture here is tap-scrim / tap-Done. */}
      <Box sx={{ display: 'flex', justifyContent: 'center', pt: 2.5, pb: 1, flexShrink: 0 }}>
        <Box
          aria-hidden
          sx={{
            width: 44,
            height: 5,
            borderRadius: 999,
            bgcolor: alpha(theme.palette.stoneEdge.main, 0.35),
          }}
        />
      </Box>

      {/* Scrollable controls region, wrapped in a positioned container so the
          scroll cue can sit at its bottom edge. flex:1 so it only starts
          scrolling once the sheet hits its max height (a tall stack on a short
          phone); the pinned "Done" footer below always stays put. */}
      <Box sx={{ position: 'relative', flex: 1, minHeight: 0, display: 'flex' }}>
        <Box
          ref={scrollRef}
          onScroll={updateScrollCue}
          sx={{
            flex: 1,
            minHeight: 0,
            overflowY: 'auto',
            px: 5.5,
            pb: 3,
            display: 'flex',
            flexDirection: 'column',
            gap: 4,
          }}
        >
          <Typography
            variant="h6"
            component="h2"
            sx={{ fontFamily: '"Fredoka", sans-serif', fontWeight: 600, fontSize: 19, textAlign: 'center' }}
          >
            Game settings
          </Typography>

          {children}
        </Box>

        {/* "More below" scroll cue: a soft fade into the sheet color plus a
            downward chevron, shown ONLY while content sits past the fold, so
            players discover the mode options after Classic. pointerEvents:none
            so it never intercepts a tap on the card beneath it. */}
        {hasMoreBelow && (
          <Box
            aria-hidden
            sx={{
              position: 'absolute',
              left: 0,
              right: 0,
              bottom: 0,
              height: 44,
              pointerEvents: 'none',
              display: 'flex',
              alignItems: 'flex-end',
              justifyContent: 'center',
              pb: 0.5,
              background: `linear-gradient(to top, ${theme.palette.card.main} 30%, ${alpha(theme.palette.card.main, 0)} 100%)`,
            }}
          >
            <Box sx={{ color: 'text.secondary', fontSize: 15, display: 'flex' }}>
              <FontAwesomeIcon icon="chevron-down" />
            </Box>
          </Box>
        )}
      </Box>

      {/* Pinned footer: the gold "Done" CTA stays visible even when the controls
          above scroll (family-safe + length + mode + favorites can exceed a short
          viewport). flexShrink:0 keeps it out of the scroll region; the hairline
          separates it from the scrolling content. */}
      <Box
        sx={{
          flexShrink: 0,
          px: 5.5,
          pt: 2,
          pb: 'calc(16px + env(safe-area-inset-bottom, 0px))',
          borderTop: `1px solid ${alpha(theme.palette.stoneEdge.main, 0.16)}`,
        }}
      >
        <Button variant="contained" fullWidth onClick={onClose}>
          Done
        </Button>
      </Box>
    </Drawer>
  );
}
