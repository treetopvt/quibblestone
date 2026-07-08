// ----------------------------------------------------------------------------
//  FamilySafeToggle - the session-level family-safe control (child-safety/02).
//
//  A controlled MUI Switch, wrapped in a chunky, high-contrast, big-tap-target
//  label row (a FontAwesome shield-heart glyph, the "Family-safe" label, and a
//  short state-aware sub-caption), styled entirely from theme tokens
//  (web/src/theme.ts) - no hex/rgb literals or raw-px spacing here.
//
//  Safe by default (AC-02): this component has no opinion on the INITIAL
//  value - it is fully controlled via `checked` / `onChange`. The caller
//  (the screen that owns the session's toggle state) MUST default its state
//  to `true` so a fresh session starts family-safe-on; see
//  ../content/familySafe.ts for the pure selection rule this toggle drives.
//
//  AGE GATE (grown-up content): turning family-safe OFF now unlocks the
//  non-family-safe ("teen-plus") story tier in the seed library (see
//  ../content/seedLibrary.ts and selectTemplates when familySafeOn=false), so
//  that ONE direction is gated behind an explicit 18+ confirmation dialog. The
//  component intercepts the switch: turning family-safe back ON is always
//  immediate; turning it OFF opens the confirm dialog and only calls
//  `onChange(false)` once the player confirms. A confirmation is remembered for
//  the life of this toggle instance so we do not nag on every flip (a fresh
//  session still defaults family-safe ON and would re-ask). The confirmation is
//  ephemeral component state only - never localStorage/cookie/VITE_ (CLAUDE.md
//  section 4), matching the safe-by-default, no-account posture.
//
//  Scope (child safety, README section 6): flipping this toggle only widens
//  or narrows which CURATED TEMPLATES are offered (see selectTemplates in
//  ../content/familySafe.ts) - it never relaxes the profanity/safety filter
//  that always runs on a player's submitted free text (child-safety/01,
//  AC-04). This component renders no free text of its own and performs no
//  content filtering itself; it is purely the on/off control plus its gate.
// ----------------------------------------------------------------------------

import { useState } from 'react';
import {
  Box,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Stack,
  Switch,
  Typography,
} from '@mui/material';
import { alpha, useTheme } from '@mui/material/styles';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { resolveFamilySafeToggle } from './familySafeGate';

export interface FamilySafeToggleProps {
  /** Current toggle position. Callers should default this to `true` (safe by default, AC-02). */
  checked: boolean;
  /** Called with the new toggle position whenever the player flips the switch (turning OFF only after the 18+ confirmation). */
  onChange: (checked: boolean) => void;
}

export function FamilySafeToggle({ checked, onChange }: FamilySafeToggleProps) {
  const theme = useTheme();
  // Remembered only for this toggle instance so we do not re-prompt on every
  // flip; a fresh session defaults family-safe ON and would ask again.
  const [ageConfirmed, setAgeConfirmed] = useState(false);
  const [confirmOpen, setConfirmOpen] = useState(false);

  // Turning family-safe back ON is always immediate. Turning it OFF (unlocking
  // grown-up content) is gated by the 18+ confirmation, unless already confirmed.
  const requestChange = (next: boolean) => {
    const action = resolveFamilySafeToggle(next, ageConfirmed);
    if (action.kind === 'apply') {
      onChange(action.familySafe);
      return;
    }
    setConfirmOpen(true);
  };

  const confirmOff = () => {
    setAgeConfirmed(true);
    setConfirmOpen(false);
    onChange(false);
  };

  return (
    <>
      <Stack
        direction="row"
        alignItems="center"
        spacing={3}
        sx={{
          px: 5,
          py: 4,
          borderRadius: '20px',
          bgcolor: 'card.main',
          border: `2px solid ${theme.palette.stoneSlot.main}`,
        }}
      >
        <Box
          sx={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            width: 48,
            height: 48,
            flexShrink: 0,
            borderRadius: '14px',
            bgcolor: checked ? theme.palette.teal.main : theme.palette.coral.main,
            color: theme.palette.common.white,
          }}
        >
          <FontAwesomeIcon icon="shield-heart" fontSize={22} />
        </Box>

        <Stack spacing={1} sx={{ flexGrow: 1, minWidth: 0 }}>
          <Typography variant="subtitle1" sx={{ color: 'text.primary' }}>
            Family-safe
          </Typography>
          <Typography variant="body2" sx={{ color: 'text.secondary' }}>
            {checked ? 'Only kid-friendly tales and words' : 'Grown-up tales unlocked (18+)'}
          </Typography>
        </Stack>

        <Switch
          checked={checked}
          onChange={(event) => requestChange(event.target.checked)}
          inputProps={{ 'aria-label': 'Family-safe toggle' }}
          sx={{
            width: 62,
            height: 38,
            p: 0,
            flexShrink: 0,
            '& .MuiSwitch-switchBase': {
              p: 1,
              '&.Mui-checked': {
                transform: 'translateX(24px)',
                color: theme.palette.common.white,
                '& + .MuiSwitch-track': {
                  backgroundColor: theme.palette.teal.main,
                  opacity: 1,
                },
              },
            },
            '& .MuiSwitch-thumb': {
              width: 22,
              height: 22,
              boxShadow: 'none',
            },
            '& .MuiSwitch-track': {
              borderRadius: '19px',
              backgroundColor: theme.palette.stoneSlot.alt,
              opacity: 1,
            },
          }}
        />
      </Stack>

      {/* AC (age gate): confirm 18+ before unlocking the grown-up story tier. */}
      <Dialog open={confirmOpen} onClose={() => setConfirmOpen(false)} maxWidth="xs" fullWidth>
        <DialogTitle sx={{ fontWeight: 700 }}>Show grown-up tales?</DialogTitle>
        <DialogContent>
          <Stack spacing={2}>
            <Stack
              direction="row"
              spacing={1.5}
              sx={{
                p: 2,
                borderRadius: '14px',
                bgcolor: alpha(theme.palette.coral.main, 0.12),
                border: `1px solid ${alpha(theme.palette.coral.main, 0.3)}`,
                alignItems: 'flex-start',
              }}
            >
              <Box sx={{ color: 'coral.main', display: 'flex', mt: 0.25 }}>
                <FontAwesomeIcon icon="triangle-exclamation" />
              </Box>
              <Typography variant="body2" sx={{ color: 'text.primary', fontWeight: 600 }}>
                Turning family-safe off unlocks tales written for grown-ups (dating,
                nightlife, partying, and cheeky humor). These are not for kids.
              </Typography>
            </Stack>
            <Typography variant="body2" sx={{ color: 'text.secondary' }}>
              The word filter on typed answers still runs either way, and you can switch
              family-safe back on any time.
            </Typography>
          </Stack>
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 3, gap: 1.5 }}>
          <Button variant="outlined" onClick={() => setConfirmOpen(false)} fullWidth>
            Keep it on
          </Button>
          <Button variant="contained" color="primary" onClick={confirmOff} fullWidth>
            Yes, I am 18+
          </Button>
        </DialogActions>
      </Dialog>
    </>
  );
}
