// ----------------------------------------------------------------------------
//  StoryLengthChoice - the session-level story-length control (story-selection/02).
//
//  A controlled, chunky, big-tap-target segmented pair - "Quick tale" / "Full
//  tale" - in the SAME visual family as FamilySafeToggle (a carved-stone card,
//  a FontAwesome glyph, a label + short caption), styled entirely from theme
//  tokens (web/src/theme.ts) - no hex/rgb literals or raw-px spacing here.
//
//  This is purely the CONTROL: it has no opinion on the initial value (fully
//  controlled via `value` / `onChange`) and it performs no content selection
//  itself. Story-selection/01's pure pipeline (selectByLengthOrFallback,
//  ../content/length.ts) is what actually narrows the template pool - Solo and
//  Lobby wire this component's value into that pipeline (Solo directly; Lobby
//  via the host-only "Start game" invoke, story-selection/02 AC-02).
//
//  Default (AC-06): the caller MUST default its state to 'full' so a session
//  that never touches this control behaves exactly like before story-selection
//  existed - the quick pool is opt-in, never dealt by default.
//
//  Copy note: both options describe TURN COUNT, never quality - "about 5
//  blanks" reads as "fewer turns", not "a worse story" (a short tale is just as
//  fun as a long one).
//
//  `value` is typed as the full LengthPreference union (../content/length.ts)
//  for convenience at call sites, but this control only ever emits 'quick' or
//  'full' - it renders no third option for 'any'.
// ----------------------------------------------------------------------------

import { Box, Stack, Typography, useTheme } from '@mui/material';
import { alpha } from '@mui/material/styles';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import type { IconName } from '@fortawesome/fontawesome-svg-core';
import type { LengthPreference } from '../content/length';

export interface StoryLengthChoiceProps {
  /** Current length choice. Callers should default this to 'full' (AC-06). */
  value: LengthPreference;
  /** Called with the newly tapped option ('quick' or 'full') whenever the player picks one. */
  onChange: (value: LengthPreference) => void;
}

interface LengthOption {
  value: 'quick' | 'full';
  label: string;
  caption: string;
  icon: IconName;
}

const OPTIONS: readonly LengthOption[] = [
  { value: 'quick', label: 'Quick tale', caption: 'A short one - about 5 blanks', icon: 'bolt' },
  { value: 'full', label: 'Full tale', caption: 'The whole story - more turns to go round', icon: 'book' },
];

export function StoryLengthChoice({ value, onChange }: StoryLengthChoiceProps) {
  const theme = useTheme();

  return (
    <Stack
      spacing={3}
      sx={{
        px: 5,
        py: 4,
        borderRadius: '20px',
        bgcolor: 'card.main',
        border: `2px solid ${theme.palette.stoneSlot.main}`,
      }}
    >
      <Stack direction="row" alignItems="center" spacing={3}>
        <Box
          sx={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            width: 48,
            height: 48,
            flexShrink: 0,
            borderRadius: '14px',
            bgcolor: theme.palette.coral.main,
            color: theme.palette.common.white,
          }}
        >
          <FontAwesomeIcon icon="hourglass-half" fontSize={20} />
        </Box>

        <Stack spacing={1} sx={{ flexGrow: 1, minWidth: 0 }}>
          <Typography variant="subtitle1" sx={{ color: 'text.primary' }}>
            Story length
          </Typography>
          <Typography variant="body2" sx={{ color: 'text.secondary' }}>
            Fewer turns, same silly fun
          </Typography>
        </Stack>
      </Stack>

      <Stack direction="row" spacing={1.5} role="group" aria-label="Story length">
        {OPTIONS.map((option) => {
          const selected = value === option.value;
          return (
            <Box
              key={option.value}
              component="button"
              type="button"
              onClick={() => onChange(option.value)}
              aria-pressed={selected}
              sx={{
                flex: 1,
                display: 'flex',
                flexDirection: 'column',
                alignItems: 'center',
                gap: 0.75,
                px: 2,
                py: 2.5,
                minHeight: 76,
                borderRadius: '16px',
                border: `2.5px solid ${selected ? theme.palette.primary.main : theme.palette.stoneSlot.main}`,
                bgcolor: selected ? theme.palette.primary.main : 'transparent',
                color: selected ? theme.palette.common.white : theme.palette.text.primary,
                cursor: 'pointer',
                fontFamily: 'inherit',
              }}
            >
              <FontAwesomeIcon icon={option.icon} fontSize={18} />
              <Typography
                sx={{
                  fontFamily: '"Fredoka", sans-serif',
                  fontWeight: 600,
                  fontSize: 15,
                  lineHeight: 1.1,
                  color: 'inherit',
                }}
              >
                {option.label}
              </Typography>
              <Typography
                sx={{
                  fontFamily: '"Nunito", sans-serif',
                  fontWeight: 600,
                  fontSize: 11,
                  lineHeight: 1.2,
                  textAlign: 'center',
                  color: selected ? alpha(theme.palette.common.white, 0.85) : theme.palette.text.secondary,
                }}
              >
                {option.caption}
              </Typography>
            </Box>
          );
        })}
      </Stack>
    </Stack>
  );
}
