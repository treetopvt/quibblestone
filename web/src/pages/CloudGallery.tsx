// ----------------------------------------------------------------------------
//  CloudGallery - the PURCHASER cloud keepsake gallery sub-view
//  (keepsake-gallery/05, issue #154). Rendered INSIDE Account.tsx's signed-in
//  state, where the in-memory purchaser `credential` is in scope (an in-memory
//  credential would not survive a route change, so this is deliberately NOT its
//  own route). It is only ever reached by a signed-in purchaser - anonymous
//  players never see any cloud-gallery entry point (AC-02); the device-local
//  Gallery.tsx (keepsake-gallery/03) remains everyone's free default.
//
//  WHAT IT DOES:
//    - Lists the purchaser's cloud tales (fetchCloudGallery), newest-first, with
//      client-side SEARCH by title/byline and SORT by date (AC-03) over the
//      returned bounded set.
//    - UPLOADS this device's local tales that carry flattened `parts` and are
//      not yet synced (saveCloudTale each, then markTaleSynced) - a clear,
//      consented action, never automatic (AC-01). Shows counts and a friendly
//      "nothing new to sync" state.
//    - Per-tale DELETE (deleteCloudTale) and REVOKE-all (revokeCloudGallery,
//      AC-06) behind a confirm.
//    - Renders each cloud tale from its flattened `parts` (title + coral words +
//      "carved by <byline>") as a DOM stone-tablet using THEME tokens - it does
//      NOT call renderTabletImage (cloud tales have no assembled/template/image),
//      mirroring Gallery.tsx's card language.
//
//  CHILD SAFETY (AC-05): renders ONLY the already-filtered parts + byline
//  nickname(s). No purchaser email or PII is surfaced onto any tale a child
//  might see. All content arrived already vetted (server re-vets on save).
//
//  FAIL GRACEFUL: every client call resolves a status ('ok' | 'signed-out' |
//  'error') and never throws; this view surfaces calm, friendly notes (never a
//  raw error, never a dead end) and treats 'signed-out' as "your sign-in
//  expired". No console.* anywhere.
//
//  Styling: theme tokens ONLY (web/src/theme.ts) - no hex/raw-px. FontAwesome
//  icons only (registered in web/src/fontawesome.ts). Big tap targets.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { useCallback, useEffect, useMemo, useState } from 'react';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { alpha, useTheme } from '@mui/material/styles';
import { Box, Button, CircularProgress, Stack, TextField, Typography } from '@mui/material';
import {
  deleteCloudTale,
  fetchCloudGallery,
  revokeCloudGallery,
  saveCloudTale,
  type CloudTale,
} from '../gallery/cloudGalleryClient';
import { listTales, markTaleSynced, type TaleMeta } from '../gallery/localGallery';

export interface CloudGalleryProps {
  /** The signed-in purchaser credential (in memory in Account) - the effective cloud-sync gate (AC-04). */
  credential: string;
}

/** A short, friendly date string ("Jul 2, 2026") from an ISO UTC stamp. Falls back to '' rather than throwing. */
function formatCreated(createdUtc: string): string {
  try {
    const date = new Date(createdUtc);
    if (Number.isNaN(date.getTime())) return '';
    return date.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
  } catch {
    return '';
  }
}

/** Sort order for the browsed set (AC-03) - by creation date, either direction. */
type SortDir = 'newest' | 'oldest';

/** The local tales eligible to upload: they carry flattened `parts` and are not yet synced from this device. */
function uploadableLocalTales(local: readonly TaleMeta[]): TaleMeta[] {
  return local.filter((tale) => Array.isArray(tale.parts) && tale.parts.length > 0 && !tale.cloudTaleId);
}

/**
 * One cloud tale rendered as a DOM stone-tablet from its flattened parts - the
 * title, the interleaved literal text + coral-highlighted words, and the byline.
 * Never an image (cloud tales have no rendered blob), never engine data.
 */
function CloudTaleCard({
  tale,
  onDelete,
  deleting,
}: {
  tale: CloudTale;
  onDelete: () => void;
  deleting: boolean;
}) {
  const theme = useTheme();
  const created = formatCreated(tale.createdUtc);
  return (
    <Stack
      spacing={2}
      sx={{
        p: 3,
        borderRadius: 3,
        bgcolor: 'card.main',
        border: `2px solid ${alpha(theme.palette.stoneEdge.main, 0.3)}`,
      }}
    >
      <Typography
        component="h3"
        sx={{
          fontFamily: '"Fredoka", sans-serif',
          fontWeight: 700,
          fontSize: 18,
          lineHeight: 1.2,
          color: 'primary.main',
        }}
      >
        {tale.title}
      </Typography>
      <Typography
        component="p"
        sx={{
          fontFamily: '"Nunito", sans-serif',
          fontWeight: 600,
          fontSize: 15,
          lineHeight: 1.6,
          color: 'text.primary',
        }}
      >
        {tale.parts.map((part, index) =>
          part.isWord && part.text.length > 0 ? (
            <Box
              key={`w-${index}`}
              component="span"
              sx={{ color: 'coral.main', fontWeight: 800 }}
            >
              {part.text}
            </Box>
          ) : (
            <Box key={`t-${index}`} component="span">
              {part.text}
            </Box>
          ),
        )}
      </Typography>
      <Stack direction="row" spacing={1.5} alignItems="center" justifyContent="space-between">
        <Typography sx={{ fontSize: 12.5, fontWeight: 700, color: 'text.secondary' }}>
          {created ? `Carved ${created}` : ''}
          {tale.bylineNames ? `${created ? ' - ' : ''}${tale.bylineNames}` : ''}
        </Typography>
        <Button
          variant="text"
          size="small"
          onClick={onDelete}
          disabled={deleting}
          aria-label={`Remove "${tale.title}" from the cloud gallery`}
          startIcon={<FontAwesomeIcon icon="circle-xmark" style={{ width: 14, height: 14 }} />}
          sx={{ color: 'text.secondary', fontWeight: 800, fontSize: 12.5 }}
        >
          Remove
        </Button>
      </Stack>
    </Stack>
  );
}

export function CloudGallery({ credential }: CloudGalleryProps) {
  const theme = useTheme();
  const [loading, setLoading] = useState(true);
  const [status, setStatus] = useState<'ok' | 'signed-out' | 'error'>('ok');
  const [tales, setTales] = useState<CloudTale[]>([]);
  const [localTales, setLocalTales] = useState<TaleMeta[]>([]);

  const [query, setQuery] = useState('');
  const [sortDir, setSortDir] = useState<SortDir>('newest');

  const [uploading, setUploading] = useState(false);
  const [uploadNote, setUploadNote] = useState<string | null>(null);
  const [deletingId, setDeletingId] = useState<string | null>(null);
  const [confirmRevoke, setConfirmRevoke] = useState(false);
  const [revoking, setRevoking] = useState(false);
  // A transient, per-action note (a failed delete / revoke) shown WITHOUT tearing
  // down the browsed list - unlike `status`, it never replaces the whole view.
  const [actionNote, setActionNote] = useState<string | null>(null);

  // A "soft" re-sync used AFTER a mutation: it refreshes on 'ok' and gates on
  // 'signed-out', but a transient 'error' on the follow-up read leaves the
  // current (optimistically-updated) list intact rather than escalating one
  // failed action into the full-screen error panel (CR-W1). Never sets loading.
  const reloadSoft = useCallback(async () => {
    const [cloud, local] = await Promise.all([fetchCloudGallery(credential), listTales()]);
    setLocalTales(local);
    if (cloud.status === 'ok') {
      setStatus('ok');
      setTales(cloud.tales);
    } else if (cloud.status === 'signed-out') {
      setStatus('signed-out');
    }
  }, [credential]);

  useEffect(() => {
    let active = true;
    void (async () => {
      const [cloud, local] = await Promise.all([fetchCloudGallery(credential), listTales()]);
      if (!active) return;
      setStatus(cloud.status);
      setTales(cloud.tales);
      setLocalTales(local);
      setLoading(false);
    })();
    return () => {
      active = false;
    };
    // `reload` is a stable useCallback over the same deps; the inline body here
    // adds an `active` cancel guard the mount path needs (user-driven reloads do
    // not, since they run in response to a fresh interaction).
  }, [credential]);

  const pendingUploads = useMemo(() => uploadableLocalTales(localTales), [localTales]);

  // The browsed set (AC-03): client-side search over title + byline, then sort by date.
  const visibleTales = useMemo(() => {
    const needle = query.trim().toLowerCase();
    const filtered = needle
      ? tales.filter(
          (tale) =>
            tale.title.toLowerCase().includes(needle) || tale.bylineNames.toLowerCase().includes(needle),
        )
      : tales;
    const sorted = [...filtered].sort((a, b) => {
      const at = new Date(a.createdUtc).getTime() || 0;
      const bt = new Date(b.createdUtc).getTime() || 0;
      return sortDir === 'newest' ? bt - at : at - bt;
    });
    return sorted;
  }, [tales, query, sortDir]);

  // Upload every uploadable local tale (AC-01) - a consented, explicit action.
  // Each successful save is marked synced locally so it is never re-uploaded
  // (dedupe). A per-tale failure (re-vet 400, transport) is counted and skipped,
  // never crashing the batch. A 401 mid-batch flips the whole view to signed-out.
  const handleUpload = async () => {
    if (uploading || pendingUploads.length === 0) return;
    setUploading(true);
    setUploadNote(null);
    setActionNote(null);
    let synced = 0;
    let skipped = 0;
    try {
      for (const tale of pendingUploads) {
        const parts = tale.parts;
        if (!parts || parts.length === 0) {
          skipped += 1;
          continue;
        }
        const result = await saveCloudTale(credential, {
          title: tale.title,
          parts,
          bylineNames: tale.bylineNames ?? '',
        });
        if (result.status === 'signed-out') {
          setStatus('signed-out');
          setUploading(false);
          return;
        }
        if (result.status === 'ok' && result.taleId) {
          await markTaleSynced(tale.id, result.taleId);
          synced += 1;
        } else {
          skipped += 1;
        }
      }
      await reloadSoft();
      setUploadNote(
        skipped > 0
          ? `Synced ${synced} tale${synced === 1 ? '' : 's'} - ${skipped} could not be synced just now.`
          : `Synced ${synced} tale${synced === 1 ? '' : 's'} to your cloud gallery.`,
      );
    } finally {
      setUploading(false);
    }
  };

  const handleDelete = async (taleId: string) => {
    if (deletingId) return;
    setDeletingId(taleId);
    setActionNote(null);
    try {
      const result = await deleteCloudTale(credential, taleId);
      if (result.status === 'signed-out') {
        setStatus('signed-out');
        return;
      }
      if (result.status === 'error') {
        // A transient delete failure leaves the tale present. Surface a small
        // note WITHOUT tearing down the browsed list + search (CR-W1) - do not
        // re-list here, since that follow-up read could itself blip to error.
        setActionNote('Could not remove that tale just now - please try again.');
        return;
      }
      // Deleted: drop it optimistically, then soft re-sync to server truth (a
      // transient follow-up read failure keeps the optimistic view, never the
      // full-screen error panel).
      setTales((current) => current.filter((tale) => tale.taleId !== taleId));
      await reloadSoft();
    } finally {
      setDeletingId(null);
    }
  };

  // Revoke ALL cloud tales (AC-06). The server's revoke-all is FAIL-LOUD: a 2xx
  // means the full sweep completed (it throws, surfacing a non-2xx, if any row
  // could not be removed). So a single 'ok' is trustworthy - clear the view; no
  // read-back-retry loop needed (which, fired with no backoff, only risked
  // showing "still not empty" against a lagging read - CR-W2). Only a genuine
  // 'error' means the sweep did not complete, so keep the confirm open to retry.
  const handleRevoke = async () => {
    if (revoking) return;
    setRevoking(true);
    setActionNote(null);
    try {
      const result = await revokeCloudGallery(credential);
      if (result.status === 'signed-out') {
        setStatus('signed-out');
        setConfirmRevoke(false);
        return;
      }
      if (result.status === 'error') {
        // The sweep did not complete - reflect current truth and let the
        // purchaser try again (no dead end). Local tales keep their sync stamp;
        // harmless (they are simply not re-uploaded).
        setActionNote('Could not revoke cloud sync just now - please try again.');
        await reloadSoft();
        return;
      }
      // Full sweep done (trusted 2xx): clear the cloud view.
      setStatus('ok');
      setTales([]);
      setConfirmRevoke(false);
      setLocalTales(await listTales());
    } finally {
      setRevoking(false);
    }
  };

  if (loading) {
    return <CircularProgress size={22} sx={{ mt: 1 }} />;
  }

  if (status === 'signed-out') {
    return (
      <Typography sx={{ fontSize: 13, fontWeight: 700, color: 'text.secondary', textAlign: 'center' }}>
        Your sign-in expired - request a fresh link to open your cloud gallery.
      </Typography>
    );
  }

  if (status === 'error') {
    return (
      <Typography sx={{ fontSize: 13, fontWeight: 700, color: 'text.secondary', textAlign: 'center' }}>
        We could not reach your cloud gallery just now - please try again in a moment.
      </Typography>
    );
  }

  return (
    <Stack spacing={2.5} sx={{ width: '100%' }}>
      <Typography sx={{ fontSize: 13, fontWeight: 800, color: 'text.secondary', textAlign: 'center' }}>
        Your cloud gallery
      </Typography>

      {actionNote && (
        <Typography role="status" sx={{ fontSize: 12.5, fontWeight: 700, color: 'error.main', textAlign: 'center' }}>
          {actionNote}
        </Typography>
      )}

      {/* Upload this device's un-synced local tales (AC-01) - explicit, consented. */}
      <Stack spacing={1.5}>
        <Button
          variant="outlined"
          fullWidth
          onClick={() => void handleUpload()}
          disabled={uploading || pendingUploads.length === 0}
          startIcon={<FontAwesomeIcon icon="images" style={{ width: 16, height: 16 }} />}
        >
          {uploading
            ? 'Syncing...'
            : pendingUploads.length === 0
              ? 'Nothing new to sync from this device'
              : `Sync ${pendingUploads.length} tale${pendingUploads.length === 1 ? '' : 's'} from this device`}
        </Button>
        {uploadNote && (
          <Typography role="status" sx={{ fontSize: 12.5, fontWeight: 700, color: 'text.secondary', textAlign: 'center' }}>
            {uploadNote}
          </Typography>
        )}
      </Stack>

      {/* Browse controls (AC-03): search by title/byline + sort by date. */}
      {tales.length > 0 && (
        <Stack spacing={1.5}>
          <TextField
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            fullWidth
            size="small"
            label="Search by title or name"
            placeholder="dragon, Mia..."
          />
          <Button
            variant="text"
            onClick={() => setSortDir((current) => (current === 'newest' ? 'oldest' : 'newest'))}
            startIcon={<FontAwesomeIcon icon="hourglass-half" style={{ width: 14, height: 14 }} />}
            sx={{ alignSelf: 'flex-start', fontWeight: 800, fontSize: 13 }}
          >
            {sortDir === 'newest' ? 'Newest first' : 'Oldest first'}
          </Button>
        </Stack>
      )}

      {tales.length === 0 ? (
        <Typography sx={{ fontSize: 13.5, fontWeight: 700, color: 'text.secondary', textAlign: 'center' }}>
          No tales in your cloud gallery yet - sync some from this device to keep them across your devices.
        </Typography>
      ) : visibleTales.length === 0 ? (
        <Typography sx={{ fontSize: 13.5, fontWeight: 700, color: 'text.secondary', textAlign: 'center' }}>
          No tales match "{query.trim()}".
        </Typography>
      ) : (
        <Stack spacing={2}>
          {visibleTales.map((tale) => (
            <CloudTaleCard
              key={tale.taleId}
              tale={tale}
              deleting={deletingId === tale.taleId}
              onDelete={() => void handleDelete(tale.taleId)}
            />
          ))}
        </Stack>
      )}

      {/* Revoke cloud sync (AC-06) - behind a confirm so a tap is never destructive by accident. */}
      {tales.length > 0 && (
        <Stack spacing={1.5} sx={{ pt: 1 }}>
          {confirmRevoke ? (
            <Stack spacing={1.5}>
              <Typography sx={{ fontSize: 13, fontWeight: 800, color: 'error.main', textAlign: 'center' }}>
                Remove every tale from your cloud gallery? Your device-local tales stay put.
              </Typography>
              <Stack direction="row" spacing={1.5}>
                <Button
                  variant="text"
                  fullWidth
                  onClick={() => setConfirmRevoke(false)}
                  disabled={revoking}
                  sx={{ fontWeight: 800 }}
                >
                  Keep them
                </Button>
                <Button
                  variant="contained"
                  fullWidth
                  color="error"
                  onClick={() => void handleRevoke()}
                  disabled={revoking}
                  aria-busy={revoking}
                >
                  {revoking ? 'Removing...' : 'Remove all'}
                </Button>
              </Stack>
            </Stack>
          ) : (
            <Button
              variant="text"
              fullWidth
              onClick={() => setConfirmRevoke(true)}
              startIcon={<FontAwesomeIcon icon="circle-xmark" style={{ width: 14, height: 14 }} />}
              sx={{ color: alpha(theme.palette.coral.main, 0.9), fontWeight: 800, fontSize: 13 }}
            >
              Revoke cloud sync
            </Button>
          )}
        </Stack>
      )}
    </Stack>
  );
}
