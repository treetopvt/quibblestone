// ----------------------------------------------------------------------------
//  Favorites - the device-local Favorites LIST screen (story-selection/06,
//  "Favorite a story and replay it (device-local)", AC-02/AC-03).
//
//  Reachable from Home ("My favorites"): shows every template a player has
//  starred on THIS device (../content/favorites.ts, newest-favorited first),
//  each with a light length-class hint ("Quick tale" / "Full tale" - resolved
//  by looking the template up in ../content/seedLibrary.ts and classifying it
//  via ../content/length.ts's classifyLength, the SAME derivation the rest of
//  the app uses; never a new authored tag). Tapping a row starts a NEW round
//  on that EXACT template with fresh blanks, no template picker (AC-03) - App
//  wires `onPick` to the solo-replay seam (Solo.tsx's `initialFavorite` prop).
//  A small per-row remove control keeps the list manageable, and the empty
//  state is a friendly, non-dead-end line (the AppBar's back action is always
//  there too) rather than a blank screen.
//
//  Reuse contract: the list BODY is factored into the exported <FavoritesList>
//  (entries + tap + remove, no screen chrome) so the group host's INLINE
//  favorites picker on Lobby.tsx reuses the exact same component instead of a
//  second implementation (see Lobby.tsx's host-only "Play a favorite" panel).
//  <Favorites> itself is just <FavoritesList> wrapped in this screen's AppBar
//  + intro copy.
//
//  Child safety / scope (AC-05, AC-06, AC-07): this screen renders only
//  already-vetted template titles (no free text of its own) and performs no
//  entitlement check - reading, picking, or removing a favorite is free and
//  device-local. The family-safe gate itself runs at the REPLAY seam (Solo.tsx
//  / the server for a group pick), not here - this is just the list.
//
//  Styling: theme tokens only (no hex/raw-px). FontAwesome icons only,
//  registered in web/src/fontawesome.ts. Big tap targets, kid-readable.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { useState } from 'react';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { alpha, useTheme } from '@mui/material/styles';
import { Box, IconButton, Stack, Typography } from '@mui/material';
import { AppBar } from '../components';
import { classifyLength } from '../content/length';
import { loadFavorites, removeFavorite, type FavoriteEntry } from '../content/favorites';
import { seedLibrary } from '../content/seedLibrary';

export interface FavoritesProps {
  /** Return to Home (the app-bar back action). */
  onBack: () => void;
  /** Picking a favorite (AC-03): App wires this into the solo-replay seam. */
  onPick: (entry: FavoriteEntry) => void;
}

/** The friendly, non-dead-end empty state (AC-02): a hint, not a blank screen. */
function EmptyFavorites() {
  const theme = useTheme();
  return (
    <Stack alignItems="center" spacing={2} sx={{ py: 9, px: 3, textAlign: 'center' }}>
      <Box
        aria-hidden
        sx={{
          width: 64,
          height: 64,
          borderRadius: '50%',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          bgcolor: alpha(theme.palette.gold.main, 0.14),
          color: theme.palette.gold.main,
          fontSize: 26,
        }}
      >
        <FontAwesomeIcon icon={['far', 'star']} />
      </Box>
      <Typography sx={{ fontWeight: 800, fontSize: 16, color: 'text.primary' }}>
        No favorites yet
      </Typography>
      <Typography sx={{ fontWeight: 600, fontSize: 14, color: 'text.secondary', maxWidth: 260 }}>
        Star a tale you love to find it here
      </Typography>
    </Stack>
  );
}

/**
 * One favorited row: a big tappable area (icon + title + length hint) plus a
 * SIBLING remove IconButton (never nested inside the tap area - two
 * interactive elements cannot nest in valid HTML). Resolves the light
 * length-class hint from the live seed library; a favorite whose template is
 * no longer in the library still shows its cached title with no hint rather
 * than crashing (a library drift is handled gracefully, not fatally).
 */
function FavoriteRow({
  entry,
  onPick,
  onRemove,
}: {
  entry: FavoriteEntry;
  onPick: () => void;
  onRemove: () => void;
}) {
  const theme = useTheme();
  const template = seedLibrary.find((t) => t.id === entry.templateId);
  const lengthHint = template ? (classifyLength(template) === 'quick' ? 'Quick tale' : 'Full tale') : null;

  return (
    <Stack
      direction="row"
      alignItems="stretch"
      sx={{
        borderRadius: 3,
        border: `2px solid ${alpha(theme.palette.teal.main, 0.24)}`,
        bgcolor: 'background.paper',
        overflow: 'hidden',
      }}
    >
      <Box
        component="button"
        type="button"
        onClick={onPick}
        sx={{
          flex: 1,
          minWidth: 0,
          display: 'flex',
          alignItems: 'center',
          gap: 2,
          textAlign: 'left',
          border: 'none',
          bgcolor: 'transparent',
          cursor: 'pointer',
          px: 2.5,
          py: 2,
          '&:hover': { bgcolor: alpha(theme.palette.teal.main, 0.08) },
          '&:focus-visible': { outline: `2px solid ${theme.palette.teal.dark}`, outlineOffset: -2 },
        }}
      >
        <Box
          sx={{
            flexShrink: 0,
            width: 44,
            height: 44,
            borderRadius: 2,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            bgcolor: alpha(theme.palette.gold.main, 0.18),
            color: theme.palette.gold.dark,
          }}
        >
          <FontAwesomeIcon icon="star" style={{ width: 18, height: 18 }} />
        </Box>
        <Stack spacing={0.25} sx={{ flexGrow: 1, minWidth: 0 }}>
          <Typography
            noWrap
            sx={{ fontWeight: 800, fontSize: 15.5, color: 'text.primary' }}
          >
            {entry.title}
          </Typography>
          {lengthHint && (
            <Typography sx={{ fontWeight: 700, fontSize: 12, color: 'text.secondary' }}>
              {lengthHint}
            </Typography>
          )}
        </Stack>
        <Box sx={{ flexShrink: 0, color: theme.palette.teal.main, display: 'flex' }}>
          <FontAwesomeIcon icon="arrow-right" style={{ width: 16, height: 16 }} />
        </Box>
      </Box>
      <Box sx={{ display: 'flex', alignItems: 'center', pr: 1 }}>
        <IconButton
          onClick={onRemove}
          aria-label={`Remove ${entry.title} from favorites`}
          sx={{ width: 40, height: 40, color: 'text.secondary' }}
        >
          <FontAwesomeIcon icon="xmark" style={{ width: 16, height: 16 }} />
        </IconButton>
      </Box>
    </Stack>
  );
}

export interface FavoritesListProps {
  /** Picking a favorite (AC-03). The caller owns what "start on this exact template" means. */
  onPick: (entry: FavoriteEntry) => void;
}

/**
 * The reusable favorites list BODY (no screen chrome): reads the device-local
 * list on mount, re-reads it after a remove so the row disappears immediately,
 * and renders the friendly empty state when there is nothing starred yet.
 * Reused by both the standalone Favorites screen (solo) and Lobby's inline
 * host favorites picker (group) - see the file header.
 */
export function FavoritesList({ onPick }: FavoritesListProps) {
  const [favorites, setFavorites] = useState<FavoriteEntry[]>(() => loadFavorites());

  const handleRemove = (templateId: string) => {
    removeFavorite(templateId);
    setFavorites(loadFavorites());
  };

  if (favorites.length === 0) {
    return <EmptyFavorites />;
  }

  return (
    <Stack spacing={1.5}>
      {favorites.map((entry) => (
        <FavoriteRow
          key={entry.templateId}
          entry={entry}
          onPick={() => onPick(entry)}
          onRemove={() => handleRemove(entry.templateId)}
        />
      ))}
    </Stack>
  );
}

export function Favorites({ onBack, onPick }: FavoritesProps) {
  return (
    <Box sx={{ position: 'relative', minHeight: '100dvh', maxWidth: 430, mx: 'auto' }}>
      <AppBar
        title="My favorites"
        leftAction={{ icon: 'arrow-left', label: 'Back to home', onClick: onBack }}
      />
      <Stack sx={{ px: 5.5, pt: 3, pb: 6 }} spacing={3}>
        <Typography sx={{ fontSize: 14, fontWeight: 600, color: 'text.secondary', textAlign: 'center' }}>
          Tap a tale to play it again with brand-new words.
        </Typography>
        <FavoritesList onPick={onPick} />
      </Stack>
    </Box>
  );
}
