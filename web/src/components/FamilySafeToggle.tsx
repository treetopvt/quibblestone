// ----------------------------------------------------------------------------
//  FamilySafeToggle - the session-level family-safe control (child-safety/02).
//
//  A controlled MUI Switch, wrapped in a chunky, high-contrast, big-tap-target
//  label row (a FontAwesome shield-heart glyph, the "Family-safe" label, and a
//  short sub-caption), styled entirely from theme tokens (web/src/theme.ts) -
//  no hex/rgb literals or raw-px spacing here.
//
//  Safe by default (AC-02): this component has no opinion on the INITIAL
//  value - it is fully controlled via `checked` / `onChange`. The caller
//  (the screen that owns the session's toggle state) MUST default its state
//  to `true` so a fresh session starts family-safe-on; see
//  ../content/familySafe.ts for the pure selection rule this toggle drives.
//
//  Scope (child safety, README section 6): flipping this toggle only widens
//  or narrows which CURATED TEMPLATES are offered (see selectTemplates in
//  ../content/familySafe.ts) - it never relaxes the profanity/safety filter
//  that always runs on a player's submitted free text (child-safety/01,
//  AC-04). This component renders no free text of its own and performs no
//  content filtering itself; it is purely the on/off control.
// ----------------------------------------------------------------------------

import { Box, Stack, Switch, Typography, useTheme } from '@mui/material';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';

export interface FamilySafeToggleProps {
  /** Current toggle position. Callers should default this to `true` (safe by default, AC-02). */
  checked: boolean;
  /** Called with the new toggle position whenever the player flips the switch. */
  onChange: (checked: boolean) => void;
}

export function FamilySafeToggle({ checked, onChange }: FamilySafeToggleProps) {
  const theme = useTheme();

  return (
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
          bgcolor: theme.palette.teal.main,
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
          Only kid-friendly tales and words
        </Typography>
      </Stack>

      <Switch
        checked={checked}
        onChange={(event) => onChange(event.target.checked)}
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
  );
}
