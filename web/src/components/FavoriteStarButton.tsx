// ----------------------------------------------------------------------------
//  FavoriteStarButton - the shared star TOGGLE control (story-selection/06,
//  "Favorite a story and replay it (device-local)", AC-01).
//
//  A single big-tap-target star that lets a player mark the story template
//  they just played as a favorite: FILLED gold when favorited, OUTLINE
//  (theme text-secondary) when not, always reflecting the current state.
//  Rendered on BOTH the solo Reveal screen and the group Round Complete
//  screen (see each file's optional `favorite` / inline usage) - this
//  component itself has no idea which screen it is on; it only needs a
//  template id + a display title.
//
//  State: seeded ONCE from ../content/favorites.ts's `isFavorite(templateId)`
//  on mount, then flipped locally via `toggleFavorite` on every tap - the
//  local state IS the source of truth for rendering (no re-read from storage
//  after mount), so the control never flickers or double-reads. Storage is a
//  device-local convenience (see favorites.ts's header); a write failure there
//  is silent and never blocks this control from reflecting the tap.
//
//  Child safety / scope (AC-05, AC-07): favoriting introduces NO free-text
//  surface (the title is the template's own already-vetted title, never
//  player input) and consumes no entitlement/capability check - it is free,
//  device-local, and anonymous, matching favorites.ts's contract exactly.
//
//  Styling: an MUI IconButton sized as a generous 52px circle (a chunky,
//  high-contrast, big tap target - CLAUDE.md section A), theme tokens only
//  (gold.main for the filled state, text.secondary for the outline state,
//  no hex/raw-px literals). Icons are FontAwesome, registered in
//  web/src/fontawesome.ts: the filled star is the already-registered solid
//  `icon="star"`; the outline star is the free-regular-pack `icon={['far',
//  'star']}` this story adds.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { useState } from 'react';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { IconButton, useTheme } from '@mui/material';
import { isFavorite, toggleFavorite } from '../content/favorites';

export interface FavoriteStarButtonProps {
  /** The story template being favorited/unfavorited. */
  templateId: string;
  /** The template's display title, cached alongside the id for the Favorites list (favorites.ts, AC-05). */
  title: string;
}

/** A generous 52px circular tap target for the star toggle (big tap targets, CLAUDE.md section A). */
const STAR_BUTTON_SIZE = 52;

export function FavoriteStarButton({ templateId, title }: FavoriteStarButtonProps) {
  const theme = useTheme();
  // Seeded once from device-local storage on mount; every subsequent render
  // reflects local state only (see file header) - toggling never re-reads
  // storage mid-session.
  const [favorited, setFavorited] = useState(() => isFavorite(templateId));

  const handleToggle = () => {
    const next = toggleFavorite({ templateId, title });
    setFavorited(next);
  };

  return (
    <IconButton
      onClick={handleToggle}
      aria-label={favorited ? 'Remove from favorites' : 'Add to favorites'}
      aria-pressed={favorited}
      sx={{
        width: STAR_BUTTON_SIZE,
        height: STAR_BUTTON_SIZE,
        color: favorited ? theme.palette.gold.main : theme.palette.text.secondary,
      }}
    >
      <FontAwesomeIcon icon={favorited ? 'star' : ['far', 'star']} style={{ width: 24, height: 24 }} />
    </IconButton>
  );
}
