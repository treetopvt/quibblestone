// ----------------------------------------------------------------------------
//  ReviewQueue - the OPERATOR review queue for reported public tales
//  (sysadmin-console/03, issue #137). The post-login back-office screen: a
//  signed-in operator sees every tale the crowd auto-hid (its already-filtered
//  content + report count) and, per tale, either CONFIRMS it stays hidden or
//  RESTORES it to serving. It is the adult-facing counterpart to the kid app - the
//  ONE visual language (web/src/theme.ts) but plain and utilitarian, not the kid
//  app's delight bar.
//
//  SEPARATE ADMIN BUNDLE / NO KID-APP EDGE (AC-04, from story 01): this file lives
//  in the admin bundle and imports NOTHING from the kid app (pages / signalr /
//  gallery / engine / components). It opens NO SignalR connection. It shares only the
//  MUI theme (via main.tsx's ThemeProvider) and its own FontAwesome registration.
//
//  ANONYMITY (AC-06, non-negotiable): the queue is CONTENT + a count. This screen
//  never shows or requests any reporter identity, player nickname, room, or session -
//  the operator reviews the published tale text only, never a person.
//
//  Styling: theme tokens ONLY (no hex / raw-px literals); FontAwesome icons only;
//  big tap targets. TS strict (no any - unknown is narrowed in the client).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { useCallback, useEffect, useState } from 'react';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { alpha, useTheme } from '@mui/material/styles';
import { Box, Button, Chip, CircularProgress, Stack, Typography } from '@mui/material';
import {
  confirmHiddenTale,
  loadReviewQueue,
  restoreHiddenTale,
  type ReportedTale,
} from './reportedTalesClient';

/** Props for {@link ReviewQueue}. */
interface ReviewQueueProps {
  /** The signed-in operator email (from the session check), shown in the header. */
  operatorEmail: string;
}

/** The screen's load phase. */
type LoadPhase = 'loading' | 'ready' | 'error';

/** Props for {@link TaleCard}. */
interface TaleCardProps {
  tale: ReportedTale;
  /** True while a confirm / restore action for this tale is in flight (buttons disabled). */
  pending: boolean;
  onConfirm: (slug: string) => void;
  onRestore: (slug: string) => void;
}

/** Renders one hidden tale's content + count and the two operator actions. */
function TaleCard({ tale, pending, onConfirm, onRestore }: TaleCardProps) {
  const theme = useTheme();
  return (
    <Stack
      spacing={2.5}
      sx={{
        p: 3.5,
        borderRadius: '24px',
        bgcolor: 'card.main',
        boxShadow: `0 10px 24px -16px ${alpha(theme.palette.stoneEdge.main, 0.6)}`,
      }}
    >
      <Stack direction="row" spacing={2} alignItems="flex-start" justifyContent="space-between">
        <Typography sx={{ fontWeight: 800, fontSize: 18, color: 'text.primary' }}>
          {tale.title}
        </Typography>
        <Chip
          icon={<FontAwesomeIcon icon="flag" style={{ fontSize: 12 }} />}
          label={`${tale.reportCount} ${tale.reportCount === 1 ? 'report' : 'reports'}`}
          sx={{
            flexShrink: 0,
            fontWeight: 800,
            color: 'coral.main',
            bgcolor: alpha(theme.palette.coral.main, 0.14),
            '& .MuiChip-icon': { color: 'coral.main' },
          }}
        />
      </Stack>

      {/* The already-filtered tale body: coral player-words distinct from template text. */}
      <Typography
        sx={{
          fontWeight: 600,
          fontSize: 15.5,
          lineHeight: 1.7,
          color: 'text.primary',
          whiteSpace: 'pre-wrap',
        }}
      >
        {tale.parts.map((part, index) => (
          <Box
            key={index}
            component="span"
            sx={part.isWord ? { color: 'coral.main', fontWeight: 800 } : undefined}
          >
            {part.text}
          </Box>
        ))}
      </Typography>

      {tale.bylineNames.length > 0 && (
        <Typography sx={{ fontWeight: 700, fontSize: 13, color: 'text.secondary' }}>
          carved by {tale.bylineNames}
        </Typography>
      )}

      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
        <Button
          variant="outlined"
          fullWidth
          disabled={pending}
          onClick={() => onRestore(tale.slug)}
          startIcon={<FontAwesomeIcon icon="rotate-left" style={{ width: 18, height: 18 }} />}
        >
          Restore
        </Button>
        <Button
          variant="contained"
          fullWidth
          disabled={pending}
          onClick={() => onConfirm(tale.slug)}
          startIcon={<FontAwesomeIcon icon="trash-can" style={{ width: 18, height: 18 }} />}
        >
          Keep hidden
        </Button>
      </Stack>
    </Stack>
  );
}

export function ReviewQueue({ operatorEmail }: ReviewQueueProps) {
  const theme = useTheme();
  const [phase, setPhase] = useState<LoadPhase>('loading');
  const [tales, setTales] = useState<ReportedTale[]>([]);
  const [message, setMessage] = useState<string>('');
  // The slug of a tale whose confirm / restore action is currently in flight.
  const [pendingSlug, setPendingSlug] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    const result = await loadReviewQueue();
    if (result.ok) {
      setTales(result.tales);
      setPhase('ready');
      setMessage('');
    } else {
      setPhase('error');
      setMessage(result.message);
    }
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  // Confirm / restore surface a transport or auth failure instead of silently
  // refreshing unchanged: the client never throws (it resolves { ok: false, message }),
  // so on failure we show that message in the error panel; on success we reload the
  // queue so the actioned tale drops off.
  const handleConfirm = async (slug: string) => {
    if (pendingSlug) return;
    setPendingSlug(slug);
    const result = await confirmHiddenTale(slug).finally(() => setPendingSlug(null));
    if (result.ok) {
      await refresh();
    } else {
      setPhase('error');
      setMessage(result.message);
    }
  };

  const handleRestore = async (slug: string) => {
    if (pendingSlug) return;
    setPendingSlug(slug);
    const result = await restoreHiddenTale(slug).finally(() => setPendingSlug(null));
    if (result.ok) {
      await refresh();
    } else {
      setPhase('error');
      setMessage(result.message);
    }
  };

  return (
    <Box sx={{ minHeight: '100dvh', maxWidth: 560, mx: 'auto' }}>
      <Stack spacing={4} sx={{ px: { xs: 3, sm: 5 }, pt: 6, pb: 6 }}>
        {/* Header: reads as the operator moderation console, not the kid app. */}
        <Stack spacing={1.5} alignItems="center" sx={{ textAlign: 'center' }}>
          <Box
            aria-hidden
            sx={{
              width: 56,
              height: 56,
              borderRadius: '50%',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              bgcolor: alpha(theme.palette.primary.main, 0.12),
              color: 'primary.main',
              fontSize: 22,
            }}
          >
            <FontAwesomeIcon icon="shield-halved" />
          </Box>
          <Typography sx={{ fontWeight: 800, fontSize: 20, color: 'text.primary' }}>
            Reported tales
          </Typography>
          <Typography sx={{ fontWeight: 600, fontSize: 13.5, color: 'text.secondary' }}>
            Signed in as {operatorEmail}
          </Typography>
        </Stack>

        {phase === 'loading' && (
          <Stack alignItems="center" sx={{ py: 6 }}>
            <CircularProgress color="primary" />
          </Stack>
        )}

        {phase === 'error' && (
          <Stack spacing={2.5} alignItems="center" sx={{ py: 4, textAlign: 'center' }}>
            <Box aria-hidden sx={{ color: 'gold.main', fontSize: 30 }}>
              <FontAwesomeIcon icon="triangle-exclamation" />
            </Box>
            <Typography sx={{ fontWeight: 600, fontSize: 14.5, color: 'text.secondary', maxWidth: 340 }}>
              {message}
            </Typography>
            <Button variant="outlined" onClick={() => void refresh()}>
              Try again
            </Button>
          </Stack>
        )}

        {phase === 'ready' && tales.length === 0 && (
          <Stack spacing={2.5} alignItems="center" sx={{ py: 6, textAlign: 'center' }}>
            <Box aria-hidden sx={{ color: 'teal.main', fontSize: 34 }}>
              <FontAwesomeIcon icon="circle-check" />
            </Box>
            <Typography sx={{ fontWeight: 800, fontSize: 17, color: 'text.primary' }}>
              Nothing to review
            </Typography>
            <Typography sx={{ fontWeight: 600, fontSize: 14, color: 'text.secondary', maxWidth: 320 }}>
              No tales have been auto-hidden. Reported tales appear here once they pass the threshold.
            </Typography>
          </Stack>
        )}

        {phase === 'ready' && tales.length > 0 && (
          <Stack spacing={3}>
            {tales.map((tale) => (
              <TaleCard
                key={tale.slug}
                tale={tale}
                pending={pendingSlug === tale.slug}
                onConfirm={(slug) => void handleConfirm(slug)}
                onRestore={(slug) => void handleRestore(slug)}
              />
            ))}
          </Stack>
        )}
      </Stack>
    </Box>
  );
}
