// ----------------------------------------------------------------------------
//  AppBar - the ONE shared app-bar recipe for every QuibbleStone screen.
//
//  Per docs/design/README.md (Global System - "CONSISTENT APP BAR - do not
//  deviate") and DESIGN_RULES.md, every screen-level app bar is:
//    - a flex row, 42x42 icon buttons (radius 14, rgba(43,38,34,.07) fill)
//    - a centered Fredoka 600 21px title
//    - the side without an action button balanced by an empty 42px spacer
//
//  This story (design-system/01, AC-03) builds that contract ONCE as a
//  reusable component so screen stories (Join, Lobby, FillBlank, Waiting,
//  Reveal, RoundComplete) import <AppBar> instead of re-specifying the icon
//  button size/radius/fill or the title typography per screen. The visual
//  tokens (icon button size/radius/background, title font) live in
//  web/src/theme.ts (MuiAppBar/MuiToolbar/MuiIconButton overrides + the
//  Fredoka heading typography); this component only assembles the layout and
//  supplies the per-screen content (title text, left/right icon + handler).
//
//  Icons are FontAwesome only (CLAUDE.md section 4), registered once in
//  web/src/fontawesome.ts.
// ----------------------------------------------------------------------------

import type { ReactNode } from 'react';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import type { IconName } from '@fortawesome/fontawesome-svg-core';
import { AppBar as MuiAppBar, Box, IconButton, Toolbar, Typography, useTheme } from '@mui/material';

/** One app-bar action: an icon button with an accessible label and handler. */
export interface AppBarAction {
  icon: IconName;
  /** Accessible label - announced by screen readers, not rendered as text. */
  label: string;
  onClick: () => void;
}

export interface AppBarProps {
  /** Centered screen title, e.g. "Waiting room". */
  title: string;
  /** Left-side icon action (back, close/leave, home). Omit to render a balancing spacer. */
  leftAction?: AppBarAction;
  /** Right-side icon action (settings, help, share). Omit to render a balancing spacer. */
  rightAction?: AppBarAction;
}

/** The fixed 42x42 footprint shared by every icon button and its balancing spacer. */
const ICON_SLOT_SIZE = 42;
/** App-bar icon-button corner radius (AC-03). Kept local so it does not leak onto other IconButtons. */
const ICON_SLOT_RADIUS = 14;

function AppBarSlot({ action }: { action: AppBarAction | undefined }): ReactNode {
  const theme = useTheme();

  if (!action) {
    // Balancing spacer (AC-03): an empty 42x42 box so the title stays centered
    // even when only one side has an action.
    return <Box sx={{ width: ICON_SLOT_SIZE, height: ICON_SLOT_SIZE }} />;
  }

  return (
    <IconButton
      onClick={action.onClick}
      aria-label={action.label}
      sx={{
        width: ICON_SLOT_SIZE,
        height: ICON_SLOT_SIZE,
        borderRadius: `${ICON_SLOT_RADIUS}px`,
        bgcolor: theme.palette.appBarIcon.fill,
        '&:hover': { bgcolor: theme.palette.appBarIcon.hoverFill },
      }}
    >
      <FontAwesomeIcon
        icon={action.icon}
        style={{ width: 18, height: 18 }}
        // Design spec: stroke #2B2622, stroke-width 2.4. FontAwesome solid
        // icons are filled shapes, not strokes, so the closest faithful
        // mapping is the theme's primary text color as the icon fill.
        color={theme.palette.text.primary}
      />
    </IconButton>
  );
}

export function AppBar({ title, leftAction, rightAction }: AppBarProps) {
  return (
    <MuiAppBar>
      <Toolbar>
        <AppBarSlot action={leftAction} />
        <Typography
          variant="h6"
          component="h1"
          sx={{ flex: 1, textAlign: 'center', fontSize: 21 }}
        >
          {title}
        </Typography>
        <AppBarSlot action={rightAction} />
      </Toolbar>
    </MuiAppBar>
  );
}
