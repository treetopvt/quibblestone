// ----------------------------------------------------------------------------
//  Gallery - the "Tales we've carved" device-local history screen
//  (keepsake-gallery/03, "Tales we've carved local history", issue #65).
//
//  Reachable from Home ("Tales we've carved"): shows every tale a player has
//  saved as an image on THIS device (../gallery/localGallery.ts, newest-saved
//  first), each with its saved image as a thumbnail, its title, and its saved
//  date (AC-01). Tapping a tale opens the full saved image with a re-share
//  action that reuses story 02's Web-Share pattern via the shared
//  ../gallery/shareImageFile.ts helper (AC-02).
//
//  This screen consumes ONLY what localGallery.ts already stored - the
//  rendered image blob plus its small metadata record. It never re-renders
//  from engine data (no `assemble()`, no `Template`, no `AssembledStory` in
//  sight) - per the feature's Decisions log, story 03 is a pure "list of saved
//  images" consumer of story 01's output, disjoint from `the-reveal` and the
//  engine entirely. Re-sharing shares the STORED blob directly, never a fresh
//  render.
//
//  Object URLs: each thumbnail/full image is rendered from a
//  `URL.createObjectURL(blob)` - every URL created here is tracked and
//  revoked on unmount (or when the tale list is reloaded) so this screen never
//  leaks blob URLs.
//
//  Empty state: a friendly "no tales carved yet" message (AC-01) rather than a
//  blank screen, mirroring Favorites.tsx's EmptyFavorites posture.
//
//  Styling: reuses the app's existing stone-tablet-adjacent card language
//  (teal-bordered rounded cards, theme tokens only - no hex/raw-px) and the
//  shared <AppBar> - no new visual system for this screen (AC per Technical
//  Notes). Icons are FontAwesome only, registered in web/src/fontawesome.ts.
//  Big tap targets, kid-readable.
//
//  Child safety (AC-05, AC-06): this screen displays only already-saved
//  images + already-vetted title/date/byline-names metadata - no free-text
//  entry point of its own. Clearing browser storage empties this screen's
//  list entirely (expected, documented device-local behavior, not a bug this
//  screen works around).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { useEffect, useState } from 'react';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { alpha, useTheme } from '@mui/material/styles';
import { Box, Button, Stack, Typography } from '@mui/material';
import { AppBar } from '../components';
import { getTaleImage, listTales, type TaleMeta } from '../gallery/localGallery';
import { shareImageFile, slugifyTitle } from '../gallery/shareImageFile';

export interface GalleryProps {
  /** Return to Home (the app-bar back action). */
  onBack: () => void;
}

/** A short, friendly saved-date string ("Jul 2, 2026"). Falls back to '' rather than throwing on an odd timestamp. */
function formatSavedAt(savedAt: number): string {
  try {
    return new Date(savedAt).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
  } catch {
    return '';
  }
}

/** The friendly, non-dead-end empty state (AC-01): a hint, not a blank screen. */
function EmptyGallery() {
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
          bgcolor: alpha(theme.palette.primary.main, 0.14),
          color: theme.palette.primary.main,
          fontSize: 26,
        }}
      >
        <FontAwesomeIcon icon="images" />
      </Box>
      <Typography sx={{ fontWeight: 800, fontSize: 16, color: 'text.primary' }}>
        No tales carved yet
      </Typography>
      <Typography sx={{ fontWeight: 600, fontSize: 14, color: 'text.secondary', maxWidth: 260 }}>
        Save a finished tale as an image from the Reveal screen to find it here.
      </Typography>
    </Stack>
  );
}

interface GalleryCardProps {
  tale: TaleMeta;
  imageUrl?: string;
  onOpen: () => void;
}

/** One saved-tale card: thumbnail (or a placeholder while it loads) + title + saved date, a big tappable area (AC-01/AC-02). */
function GalleryCard({ tale, imageUrl, onOpen }: GalleryCardProps) {
  const theme = useTheme();
  return (
    <Box
      component="button"
      type="button"
      onClick={onOpen}
      aria-label={`Open "${tale.title}"`}
      sx={{
        display: 'flex',
        flexDirection: 'column',
        textAlign: 'left',
        p: 0,
        border: `2px solid ${alpha(theme.palette.teal.main, 0.24)}`,
        borderRadius: 3,
        bgcolor: 'background.paper',
        overflow: 'hidden',
        cursor: 'pointer',
        '&:hover': { bgcolor: alpha(theme.palette.teal.main, 0.08) },
        '&:focus-visible': { outline: `2px solid ${theme.palette.teal.dark}`, outlineOffset: -2 },
      }}
    >
      <Box
        sx={{
          width: '100%',
          aspectRatio: '4 / 3',
          bgcolor: alpha(theme.palette.primary.main, 0.08),
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          overflow: 'hidden',
          color: alpha(theme.palette.primary.main, 0.4),
        }}
      >
        {imageUrl ? (
          <Box
            component="img"
            src={imageUrl}
            alt={tale.title}
            sx={{ width: '100%', height: '100%', objectFit: 'cover', display: 'block' }}
          />
        ) : (
          <FontAwesomeIcon icon="images" style={{ width: 22, height: 22 }} />
        )}
      </Box>
      <Stack spacing={0.25} sx={{ p: 1.5 }}>
        <Typography noWrap sx={{ fontWeight: 800, fontSize: 14, color: 'text.primary' }}>
          {tale.title}
        </Typography>
        <Typography sx={{ fontWeight: 700, fontSize: 11.5, color: 'text.secondary' }}>
          {formatSavedAt(tale.savedAt)}
        </Typography>
      </Stack>
    </Box>
  );
}

export function Gallery({ onBack }: GalleryProps) {
  const theme = useTheme();
  const [tales, setTales] = useState<TaleMeta[]>([]);
  const [imageUrls, setImageUrls] = useState<Record<string, string>>({});
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [resharing, setResharing] = useState(false);
  // Set when a re-share tap could not open the share sheet (no Web Share file
  // support - most desktops). Without this the button would silently do
  // nothing; instead we tell the player where sharing does work. The stored
  // tale has no engine story text to fall back to sharing as plain text, so an
  // honest "not here" message is the right graceful degrade (keepsake/03 review
  // WARN-01). Cleared whenever a fresh share is attempted or the view changes.
  const [reshareUnavailable, setReshareUnavailable] = useState(false);

  // Load the saved tale list + each tale's image as an object URL. Every URL
  // created is tracked in the effect-local `urls` map and revoked on cleanup
  // (unmount, or a re-run) so this screen never leaks blob URLs. `cancelled`
  // stops a stale load from setting state (or creating more URLs) after the
  // effect has been torn down.
  useEffect(() => {
    let cancelled = false;
    const urls: Record<string, string> = {};

    async function load() {
      const metas = await listTales();
      if (cancelled) return;
      setTales(metas);

      for (const meta of metas) {
        const blob = await getTaleImage(meta.id);
        if (cancelled) return;
        if (blob) {
          const url = URL.createObjectURL(blob);
          urls[meta.id] = url;
          setImageUrls((current) => ({ ...current, [meta.id]: url }));
        }
      }
    }
    void load();

    return () => {
      cancelled = true;
      Object.values(urls).forEach((url) => URL.revokeObjectURL(url));
    };
  }, []);

  // Clear any "sharing not available" note when switching tales / leaving the
  // detail view, so it never lingers onto a different tale (keepsake/03 WARN-01).
  useEffect(() => {
    setReshareUnavailable(false);
  }, [selectedId]);

  const selectedTale = tales.find((tale) => tale.id === selectedId);
  const selectedUrl = selectedId ? imageUrls[selectedId] : undefined;

  // Re-share (AC-02): shares the STORED blob directly - re-fetched here so the
  // share action always has the freshest read, never a re-render from engine
  // data (this feature stores images, it does not reassemble them).
  const handleReshare = async () => {
    if (!selectedTale || resharing) return;
    setResharing(true);
    setReshareUnavailable(false);
    try {
      const blob = await getTaleImage(selectedTale.id);
      if (!blob) return;
      const file = new File([blob], `${slugifyTitle(selectedTale.title)}.png`, { type: 'image/png' });
      // shareImageFile never throws; it resolves false when Web Share file
      // support is absent (or the share failed for a non-cancel reason). The
      // stored image has no engine text to fall back to sharing as plain text,
      // so instead of a silent no-op we surface a brief "not on this device"
      // note so the tap is never a dead end (keepsake/03 review WARN-01).
      const shared = await shareImageFile({
        file,
        title: selectedTale.title,
        text: selectedTale.bylineNames ?? selectedTale.title,
      });
      if (!shared) setReshareUnavailable(true);
    } finally {
      setResharing(false);
    }
  };

  return (
    <Box sx={{ position: 'relative', minHeight: '100dvh', maxWidth: 430, mx: 'auto' }}>
      <AppBar
        title={selectedTale ? selectedTale.title : "Tales we've carved"}
        leftAction={
          selectedTale
            ? { icon: 'arrow-left', label: 'Back to gallery', onClick: () => setSelectedId(null) }
            : { icon: 'arrow-left', label: 'Back to home', onClick: onBack }
        }
      />

      {selectedTale ? (
        <Stack spacing={3} sx={{ px: 5.5, pt: 3, pb: 6 }}>
          <Box
            sx={{
              borderRadius: 3,
              overflow: 'hidden',
              border: `2px solid ${alpha(theme.palette.stoneEdge.main, 0.3)}`,
            }}
          >
            {selectedUrl && (
              <Box
                component="img"
                src={selectedUrl}
                alt={selectedTale.title}
                sx={{ width: '100%', display: 'block' }}
              />
            )}
          </Box>
          <Typography sx={{ fontWeight: 700, fontSize: 13, color: 'text.secondary', textAlign: 'center' }}>
            Carved {formatSavedAt(selectedTale.savedAt)}
            {selectedTale.bylineNames ? ` - ${selectedTale.bylineNames}` : ''}
          </Typography>
          <Button
            variant="contained"
            fullWidth
            onClick={() => void handleReshare()}
            disabled={resharing}
            aria-busy={resharing}
            startIcon={<FontAwesomeIcon icon="share-nodes" style={{ width: 18, height: 18 }} />}
          >
            {resharing ? 'Preparing to share...' : 'Share this tale'}
          </Button>
          {reshareUnavailable && (
            <Typography
              role="status"
              sx={{ fontSize: 12.5, fontWeight: 700, color: 'text.secondary', textAlign: 'center' }}
            >
              Sharing isn't available on this device - it works from a phone's share menu.
            </Typography>
          )}
        </Stack>
      ) : (
        <Stack sx={{ px: 5.5, pt: 3, pb: 6 }} spacing={3}>
          <Typography sx={{ fontSize: 14, fontWeight: 600, color: 'text.secondary', textAlign: 'center' }}>
            Every tale you've saved as an image on this device, newest first.
          </Typography>
          {tales.length === 0 ? (
            <EmptyGallery />
          ) : (
            <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(2, 1fr)', gap: 2 }}>
              {tales.map((tale) => (
                <GalleryCard
                  key={tale.id}
                  tale={tale}
                  imageUrl={imageUrls[tale.id]}
                  onOpen={() => setSelectedId(tale.id)}
                />
              ))}
            </Box>
          )}
        </Stack>
      )}
    </Box>
  );
}
