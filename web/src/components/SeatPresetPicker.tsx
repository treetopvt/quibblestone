// ----------------------------------------------------------------------------
//  SeatPresetPicker - the one-tap row of kid seat presets shown ABOVE the manual
//  name + Guardian fields in the join flow (accounts-identity/08, issue #228).
//
//  A parent who has saved presets (nickname + Guardian) on the Account page sees a
//  row of one-tap chips here, so a kid does not have to re-type their name and re-
//  pick their Guardian every car ride. It is PURELY PRESENTATIONAL and holds no
//  state: the caller passes the presets and an onSelect handler.
//
//  THE HARD BOUNDARY (AC-03, the single most important thing about this component):
//  tapping a chip is EXACTLY equivalent to typing that nickname and picking that
//  Guardian by hand. This component NEVER submits, never talks to the hub, and never
//  carries a "preset" marker anywhere - onSelect just hands the preset's nickname +
//  variant back to the caller, which fills the SAME controlled fields the manual
//  path uses and submits through the SAME CreateRoom / JoinRoom invoke. There is no
//  parallel "preset join" path; the server cannot tell a preset tap from typing.
//
//  Renders NOTHING when there are no presets (AC-06): a device with no family
//  credential, or a family with no saved presets, sees only the manual fields.
//
//  Styling comes entirely from the MUI theme (theme.palette.card / gold / primary,
//  theme.spacing) - no hardcoded hex or raw-px spacing. Big tap targets (chunky
//  chips, README design brief). The avatar is the shared <Guardian> (never rebuilt).
//  FontAwesome icons only.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { alpha, useTheme } from '@mui/material/styles';
import { Box, Stack, Typography } from '@mui/material';
import { Guardian } from './Guardian';
import type { SeatPreset } from '../account/seatPresetsClient';

export interface SeatPresetPickerProps {
  /** The family's saved presets to offer as one-tap chips. Empty -> renders nothing. */
  presets: SeatPreset[];
  /** Called with the tapped preset; the caller fills the SAME name/variant fields (AC-03). */
  onSelect: (preset: SeatPreset) => void;
}

/**
 * A one-tap row of seat-preset chips (nickname + mini Guardian) shown above the
 * manual identity fields. Presentational and controlled: tapping a chip calls
 * onSelect with that preset - it fills, it never submits. Renders null when there
 * are no presets to offer (AC-06).
 */
export function SeatPresetPicker({ presets, onSelect }: SeatPresetPickerProps) {
  const theme = useTheme();

  // AC-06: no presets (no family credential on this device, or none saved yet) means
  // no picker at all - the manual fields stand alone, exactly as before this story.
  if (presets.length === 0) {
    return null;
  }

  return (
    <Stack spacing={1.5}>
      <Typography sx={{ fontSize: 14, fontWeight: 700, color: 'text.secondary' }}>
        Quick pick a saved seat
      </Typography>
      <Box
        sx={{ display: 'flex', flexWrap: 'wrap', gap: 1.5 }}
        role="group"
        aria-label="Saved seat presets"
      >
        {presets.map((preset) => (
          <Box
            key={preset.id}
            component="button"
            type="button"
            onClick={() => onSelect(preset)}
            aria-label={`Use ${preset.nickname}'s saved seat`}
            sx={{
              display: 'flex',
              alignItems: 'center',
              gap: 1,
              // Big tap target: a chunky pill, comfortably over the 44px minimum.
              minHeight: 48,
              px: 2,
              py: 1,
              border: `2px solid ${alpha(theme.palette.primary.main, 0.25)}`,
              borderRadius: '999px',
              bgcolor: 'card.main',
              cursor: 'pointer',
              // A gentle press-in on tap (transform only, per the design pack).
              transition: 'transform 0.12s ease, border-color 0.12s ease',
              '&:hover': { borderColor: alpha(theme.palette.primary.main, 0.5) },
              '&:active': { transform: 'scale(0.96)' },
            }}
          >
            <Guardian variant={preset.variant} size={30} />
            <Typography sx={{ fontSize: 15, fontWeight: 800, color: 'text.primary' }}>
              {preset.nickname}
            </Typography>
          </Box>
        ))}
      </Box>
    </Stack>
  );
}
