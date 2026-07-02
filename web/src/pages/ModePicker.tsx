// ----------------------------------------------------------------------------
//  ModePicker - the shared, single-select mode picker (group-play/05, AC-01).
//
//  single-player/02 first built these picker cards inline in Solo.tsx. group-play/05
//  EXTRACTS them here so the SOLO setup screen and the group LOBBY host controls
//  render the exact same card visuals from the exact same component - the host
//  picks a mode the same way a solo player does, with no re-specified markup
//  (AC-01). It reads the shared mode registry (./modeRegistry.ts) and is agnostic
//  about WHICH list it shows: Solo passes GAME_MODES (all four), the Lobby passes
//  GROUP_MODES (the three offered for group, Progressive Story excluded - AC-04/AC-05).
//
//  A mode with no eligible template for the CURRENT family-safe position is
//  disabled (not a dead Start button, AC-04) - e.g. Word Bank when no family-safe
//  template carries a bank. Eligibility is the mode's own `eligibleTemplates`
//  gate against the bundled seedLibrary; the picker never re-derives it.
//
//  A11y: this is a SINGLE-CHOICE control, so the list is a `role="radiogroup"`
//  and each card is a `role="radio"` with `aria-checked` (not toggle buttons with
//  `aria-pressed`), so screen readers announce "one of N" rather than N
//  independent on/off toggles. The group label is linked by a generated id
//  (useId) so two instances on different screens never collide.
//
//  Styling: every color / radius / spacing comes from the MUI theme (no hex/px
//  literals). FontAwesome only. Big tap targets (chunky card padding) per the
//  design brief. No em dashes in any prose/strings.
// ----------------------------------------------------------------------------

import { useId } from 'react';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { alpha, useTheme } from '@mui/material/styles';
import { Box, Stack, Typography } from '@mui/material';
import { seedLibrary } from '../content/seedLibrary';
import type { GameMode } from './modeRegistry';

/**
 * One tappable mode card (single-player/02, AC-01): icon + label + blurb as a
 * big tap target, teal-highlighted when selected (the same teal tap language as
 * WordBankAnswer's chips and FillBlank's spark row). Disabled when the mode has
 * no eligible template for the current family-safe position, so a player/host can
 * never select a mode that cannot start (AC-04).
 */
function ModeCard({
  mode,
  selected,
  disabled,
  onSelect,
}: {
  mode: GameMode;
  selected: boolean;
  disabled: boolean;
  onSelect: () => void;
}) {
  const theme = useTheme();
  return (
    <Box
      component="button"
      type="button"
      role="radio"
      aria-checked={selected}
      disabled={disabled}
      onClick={onSelect}
      sx={{
        display: 'flex',
        alignItems: 'center',
        gap: 2,
        width: '100%',
        textAlign: 'left',
        cursor: 'pointer',
        border: `2px solid ${selected ? theme.palette.teal.main : alpha(theme.palette.teal.main, 0.24)}`,
        borderRadius: 3,
        px: 2.5,
        py: 2,
        bgcolor: selected ? alpha(theme.palette.teal.main, 0.14) : 'background.paper',
        '&:hover': { bgcolor: alpha(theme.palette.teal.main, 0.08) },
        '&:focus-visible': { outline: `2px solid ${theme.palette.teal.dark}`, outlineOffset: 2 },
        '&:disabled': { cursor: 'not-allowed', opacity: 0.45 },
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
          bgcolor: alpha(theme.palette.teal.main, 0.16),
          color: theme.palette.teal.dark,
        }}
      >
        <FontAwesomeIcon icon={mode.icon} style={{ width: 20, height: 20 }} />
      </Box>
      <Stack spacing={0.25} sx={{ flexGrow: 1 }}>
        <Typography sx={{ fontWeight: 800, fontSize: 15.5, color: 'text.primary' }}>
          {mode.config.label}
        </Typography>
        <Typography sx={{ fontWeight: 600, fontSize: 13, color: 'text.secondary', lineHeight: 1.4 }}>
          {mode.blurb}
        </Typography>
      </Stack>
      {selected && (
        <FontAwesomeIcon
          icon="circle-check"
          style={{ width: 20, height: 20, color: theme.palette.teal.main, flexShrink: 0 }}
        />
      )}
    </Box>
  );
}

export interface ModePickerProps {
  /** The modes to offer, in picker order (Solo: GAME_MODES; group Lobby: GROUP_MODES). */
  modes: readonly GameMode[];
  /** The currently-selected mode's config id (single-select). */
  selectedId: string;
  /** Called with the picked mode when a card is tapped. */
  onSelect: (mode: GameMode) => void;
  /**
   * The current family-safe toggle position: a mode with no eligible template
   * under it is disabled (AC-04), computed via the mode's own eligibleTemplates
   * gate against the bundled seedLibrary (never re-derived here).
   */
  familySafe: boolean;
  /** The uppercase group label (default "Pick a mode"). */
  label?: string;
}

/**
 * The single-select mode picker: a labelled `radiogroup` of mode cards. Solo and
 * the group Lobby both render this, differing only in which `modes` they pass.
 */
export function ModePicker({ modes, selectedId, onSelect, familySafe, label = 'Pick a mode' }: ModePickerProps) {
  // Generated id so two pickers on different screens never share a label id.
  const labelId = useId();
  return (
    <Stack spacing={1.5} role="radiogroup" aria-labelledby={labelId}>
      <Typography
        id={labelId}
        sx={{
          fontWeight: 800,
          fontSize: 12.5,
          color: 'text.secondary',
          textTransform: 'uppercase',
          letterSpacing: 0.6,
        }}
      >
        {label}
      </Typography>
      {modes.map((mode) => (
        <ModeCard
          key={mode.config.id}
          mode={mode}
          selected={mode.config.id === selectedId}
          // A mode with no eligible template at the current family-safe position
          // (e.g. Word Bank when no family-safe template has a bank) is disabled,
          // not a dead Start button (AC-04).
          disabled={mode.eligibleTemplates(seedLibrary, familySafe).length === 0}
          onSelect={() => onSelect(mode)}
        />
      ))}
    </Stack>
  );
}
