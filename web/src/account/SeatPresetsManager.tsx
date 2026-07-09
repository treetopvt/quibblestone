// ----------------------------------------------------------------------------
//  SeatPresetsManager - the "Manage kid seat presets" area on the Account page
//  (accounts-identity/08, issue #228). A signed-in family-account holder creates,
//  edits, and deletes named seat presets here (AC-01). Rendered ONLY in Account.tsx's
//  signed-in phase, where the family credential is in scope - so an anonymous player
//  can never reach it, and presets are managed ONLY from the adult Account page
//  (never a kid's device - Out of Scope).
//
//  WHAT A PRESET IS (AC-01/AC-05): each preset holds ONLY a nickname (free text, the
//  same max length + server-side safety filter as any display name) and a Guardian
//  variant - nothing else. There is NO per-preset history, gallery, entitlement,
//  login, or PII field anywhere in this component. The add / edit form REUSES the
//  shared <PlayerIdentityFields> (the same name field + Guardian avatar picker the
//  join screens use) rather than forking a second avatar grid.
//
//  SAFETY IS SERVER-SIDE (AC-04/AC-07): this component never pre-approves a nickname.
//  It sends the candidate to the preset endpoint, which trims + length-caps it and
//  runs it through the SAME content-safety filter as any display name; a rejected
//  name comes back as a friendly message shown inline, and nothing is stored.
//
//  Styling: theme tokens ONLY (web/src/theme.ts) - no hex / raw-px. Big tap targets,
//  FontAwesome icons only (web/src/fontawesome.ts). Matches the Account page's
//  stone-tablet card language.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { useEffect, useState } from 'react';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { alpha, useTheme } from '@mui/material/styles';
import { Button, CircularProgress, IconButton, Stack, Typography } from '@mui/material';
import { PlayerIdentityFields, DEFAULT_VARIANT, Guardian } from '../components';
import type { GuardianVariant } from '../components';
import {
  fetchPresets,
  createPreset,
  updatePreset,
  deletePreset,
  type SeatPreset,
} from './seatPresetsClient';

export interface SeatPresetsManagerProps {
  /** The signed-in family credential (in scope only in Account.tsx's signed-in phase). */
  credential: string;
}

/** The manager's mode: browsing the list, or editing the draft for a new / existing preset. */
type Mode = { kind: 'list' } | { kind: 'add' } | { kind: 'edit'; preset: SeatPreset };

/** The in-progress draft for the add / edit form (local state; the server is authoritative). */
interface Draft {
  nickname: string;
  variant: GuardianVariant;
}

export function SeatPresetsManager({ credential }: SeatPresetsManagerProps) {
  const theme = useTheme();
  const [presets, setPresets] = useState<SeatPreset[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState(false);
  const [mode, setMode] = useState<Mode>({ kind: 'list' });
  const [draft, setDraft] = useState<Draft>({ nickname: '', variant: DEFAULT_VARIANT });
  const [formError, setFormError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  // Load the family's presets on mount (and if the credential changes). A transport
  // failure shows a calm note, never a crash.
  useEffect(() => {
    let active = true;
    setLoading(true);
    void fetchPresets(credential).then((result) => {
      if (!active) return;
      setPresets(result.presets);
      setLoadError(result.status === 'error');
      setLoading(false);
    });
    return () => {
      active = false;
    };
  }, [credential]);

  const startAdd = () => {
    setDraft({ nickname: '', variant: DEFAULT_VARIANT });
    setFormError(null);
    setMode({ kind: 'add' });
  };

  const startEdit = (preset: SeatPreset) => {
    setDraft({ nickname: preset.nickname, variant: preset.variant });
    setFormError(null);
    setMode({ kind: 'edit', preset });
  };

  const cancelForm = () => {
    setFormError(null);
    setMode({ kind: 'list' });
  };

  // Save the draft (create or update). The server vets the nickname (length + safety);
  // an 'invalid' result shows its friendly message inline and keeps the form open.
  const saveDraft = async () => {
    if (busy) return;
    setBusy(true);
    setFormError(null);
    const result =
      mode.kind === 'edit'
        ? await updatePreset(credential, mode.preset.id, draft.nickname, draft.variant)
        : await createPreset(credential, draft.nickname, draft.variant);
    setBusy(false);

    if (result.status === 'ok') {
      // Reflect the saved preset in the list without a full refetch: replace on edit,
      // append on add (the server returns the canonical stored record).
      setPresets((current) =>
        mode.kind === 'edit'
          ? current.map((p) => (p.id === result.preset.id ? result.preset : p))
          : [...current, result.preset],
      );
      setMode({ kind: 'list' });
      return;
    }
    if (result.status === 'invalid') {
      setFormError(result.message);
      return;
    }
    setFormError(
      result.status === 'signed-out'
        ? 'Your sign-in expired - request a fresh link and try again.'
        : 'We could not save that just now - please try again in a moment.',
    );
  };

  const removePreset = async (preset: SeatPreset) => {
    if (busy) return;
    setBusy(true);
    const result = await deletePreset(credential, preset.id);
    setBusy(false);
    if (result === 'ok') {
      setPresets((current) => current.filter((p) => p.id !== preset.id));
    } else {
      setLoadError(true);
    }
  };

  const nicknameReady = draft.nickname.trim().length > 0;

  return (
    <Stack spacing={2} sx={{ width: '100%' }}>
      <Stack spacing={0.5} sx={{ textAlign: 'center' }}>
        <Typography sx={{ fontSize: 15, fontWeight: 800, color: 'text.primary' }}>
          Kid seat presets
        </Typography>
        <Typography sx={{ fontSize: 13, fontWeight: 600, color: 'text.secondary' }}>
          Save a name + Guardian so a kid can pick their seat in one tap - no re-typing
          every ride. It is just a shortcut, never a separate login.
        </Typography>
      </Stack>

      {loading ? (
        <CircularProgress size={22} sx={{ alignSelf: 'center' }} />
      ) : mode.kind === 'list' ? (
        <>
          {presets.length === 0 ? (
            <Typography sx={{ fontSize: 13.5, fontWeight: 700, color: 'text.secondary', textAlign: 'center' }}>
              No seat presets yet - add one so your crew can jump straight into the game.
            </Typography>
          ) : (
            <Stack spacing={1.25}>
              {presets.map((preset) => (
                <Stack
                  key={preset.id}
                  direction="row"
                  spacing={1.5}
                  alignItems="center"
                  sx={{ px: 2, py: 1.25, borderRadius: '14px', bgcolor: 'background.default' }}
                >
                  <Guardian variant={preset.variant} size={34} />
                  <Typography sx={{ flex: 1, fontSize: 14.5, fontWeight: 800, color: 'text.primary' }}>
                    {preset.nickname}
                  </Typography>
                  <IconButton
                    aria-label={`Edit ${preset.nickname}`}
                    onClick={() => startEdit(preset)}
                    disabled={busy}
                    sx={{ color: 'primary.main' }}
                  >
                    <FontAwesomeIcon icon="pen" style={{ width: 16, height: 16 }} />
                  </IconButton>
                  <IconButton
                    aria-label={`Delete ${preset.nickname}`}
                    onClick={() => void removePreset(preset)}
                    disabled={busy}
                    sx={{ color: 'error.main' }}
                  >
                    <FontAwesomeIcon icon="trash" style={{ width: 16, height: 16 }} />
                  </IconButton>
                </Stack>
              ))}
            </Stack>
          )}

          {loadError && (
            <Typography sx={{ fontSize: 12.5, fontWeight: 700, color: 'error.main', textAlign: 'center' }}>
              Something did not load quite right - please try again.
            </Typography>
          )}

          <Button
            variant="outlined"
            fullWidth
            onClick={startAdd}
            startIcon={<FontAwesomeIcon icon="plus" style={{ width: 16, height: 16 }} />}
          >
            Add a seat preset
          </Button>
        </>
      ) : (
        // Add / edit form: the SHARED identity controls (name field + Guardian avatar
        // picker) driven by the local draft, then Save / Cancel.
        <Stack
          spacing={2.5}
          sx={{
            p: 3,
            borderRadius: '18px',
            bgcolor: 'background.default',
            boxShadow: `inset 0 0 0 1px ${alpha(theme.palette.stoneEdge.main, 0.25)}`,
          }}
        >
          <PlayerIdentityFields
            nickname={draft.nickname}
            variant={draft.variant}
            onNicknameChange={(nickname) => setDraft((d) => ({ ...d, nickname }))}
            onVariantChange={(variant) => setDraft((d) => ({ ...d, variant }))}
          />

          {formError && (
            <Typography role="alert" sx={{ fontSize: 13, fontWeight: 700, color: 'error.main', textAlign: 'center' }}>
              {formError}
            </Typography>
          )}

          <Stack direction="row" spacing={1.5}>
            <Button variant="text" fullWidth onClick={cancelForm} disabled={busy} sx={{ fontWeight: 800 }}>
              Cancel
            </Button>
            <Button
              variant="contained"
              fullWidth
              onClick={() => void saveDraft()}
              disabled={busy || !nicknameReady}
              startIcon={<FontAwesomeIcon icon="check" style={{ width: 16, height: 16 }} />}
            >
              {busy ? 'Saving...' : mode.kind === 'edit' ? 'Save changes' : 'Save preset'}
            </Button>
          </Stack>
        </Stack>
      )}
    </Stack>
  );
}
