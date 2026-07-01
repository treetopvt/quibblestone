// ----------------------------------------------------------------------------
//  PlayerIdentityFields - the shared "who are you" controls (name + Guardian).
//
//  Both the Join screen (a JOINER names themselves before entering a room) and
//  the HostSetup screen (build/host-identity: the HOST names themselves before
//  the room is minted) need the exact SAME identity controls: a "Display name"
//  outlined field (person icon, live n/14 counter, capped at 14 chars) and a
//  "Choose your guardian" 3-column, 6-tile single-select avatar grid (gold ring
//  + a pop-in gold check on the selected tile). Rather than fork that markup per
//  screen (a smell - CLAUDE.md section 4 keeps the design one consistent system),
//  it lives here ONCE and both screens render it.
//
//  This is a PURELY PRESENTATIONAL, CONTROLLED component: it holds no form state
//  of its own (no react-hook-form inside) - the caller owns nickname + variant and
//  passes them in with change handlers, so the Join screen wires its existing
//  react-hook-form Controllers to these props and HostSetup wires its own form.
//  It is the ONE source of truth for the shared identity constants the two callers
//  must agree on: GUARDIAN_VARIANTS (the grid order), DEFAULT_VARIANT ("teal"), and
//  MAX_NAME_LENGTH (14, kept in sync with the hub's server-side check).
//
//  Styling comes entirely from the MUI theme (theme.palette.guardianAccent /
//  gold / primary, theme.spacing) - no hardcoded hex or raw-px spacing here (the
//  fixed avatar-tile geometry uses literal px strings, never bare sx numbers,
//  which would multiply by theme.shape.borderRadius). Icons are FontAwesome only.
//  The Guardian avatar is the shared <Guardian> component (never rebuilt here).
//
//  Child safety: this component only COLLECTS the free-text name; it never vets
//  it. The name is authoritatively safety-filtered SERVER-SIDE (the hub, same gate
//  for host + joiner) before it is ever stored or shown - the cap here is UX only.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { alpha, useTheme } from '@mui/material/styles';
import { Box, InputAdornment, TextField, Typography } from '@mui/material';
import { Guardian } from './Guardian';
import type { GuardianVariant } from './Guardian';

/** The six selectable Guardian variants, in the design's grid order (AC-01). */
export const GUARDIAN_VARIANTS: GuardianVariant[] = [
  'purple',
  'gold',
  'coral',
  'teal',
  'sand',
  'plum',
];

/** Default avatar selection (AC-01, docs/design/README.md screen 2 State). */
export const DEFAULT_VARIANT: GuardianVariant = 'teal';

/** Max display-name length (AC-03) - kept in sync with the hub's server check. */
export const MAX_NAME_LENGTH = 14;

/** The avatar tile's edge length (docs/design/Join.dc.html avatar grid: 78x78). */
const TILE_SIZE = 78;

export interface PlayerIdentityFieldsProps {
  /** The current display-name value (owned by the caller's form). */
  nickname: string;
  /** The current Guardian variant selection (owned by the caller's form). */
  variant: GuardianVariant;
  /** Called with the next name whenever the field changes (already capped at MAX_NAME_LENGTH). */
  onNicknameChange: (name: string) => void;
  /** Called with the next variant when a guardian tile is tapped. */
  onVariantChange: (variant: GuardianVariant) => void;
}

/**
 * The shared display-name field + "Choose your guardian" avatar grid. Fully
 * controlled: renders `nickname` / `variant` and reports changes up. No form
 * state, no submit, no card chrome - the caller wraps this in its own card.
 */
export function PlayerIdentityFields({
  nickname,
  variant,
  onNicknameChange,
  onVariantChange,
}: PlayerIdentityFieldsProps) {
  const theme = useTheme();

  return (
    <>
      <TextField
        value={nickname}
        onChange={(event) => onNicknameChange(event.currentTarget.value.slice(0, MAX_NAME_LENGTH))}
        label="Display name"
        variant="outlined"
        fullWidth
        slotProps={{
          htmlInput: { maxLength: MAX_NAME_LENGTH, 'aria-label': 'Display name' },
          input: {
            startAdornment: (
              <InputAdornment position="start">
                <Box sx={{ color: 'primary.main', display: 'flex' }}>
                  <FontAwesomeIcon icon="user" />
                </Box>
              </InputAdornment>
            ),
          },
        }}
        helperText={`${nickname.length}/${MAX_NAME_LENGTH}`}
        sx={{ '& .MuiFormHelperText-root': { textAlign: 'right', fontWeight: 700 } }}
      />

      {/* Avatar grid (session-engine/05): "Choose your guardian" + a 3-column,
          6-tile single-select grid. Selected tile gets a gold ring + a pop-in
          gold check badge (AC-02). */}
      <Typography sx={{ fontSize: 16, fontWeight: 600, mt: 1 }}>Choose your guardian</Typography>
      <Box
        sx={{
          display: 'grid',
          gridTemplateColumns: 'repeat(3, 1fr)',
          gap: 3.5,
          justifyItems: 'center',
        }}
        role="radiogroup"
        aria-label="Choose your guardian"
      >
        {GUARDIAN_VARIANTS.map((v) => {
          const selected = variant === v;
          return (
            <Box
              key={v}
              component="button"
              type="button"
              role="radio"
              aria-checked={selected}
              aria-label={`${v} guardian`}
              onClick={() => onVariantChange(v)}
              sx={{
                position: 'relative',
                width: TILE_SIZE,
                height: TILE_SIZE,
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                border: 'none',
                borderRadius: '22px',
                bgcolor: theme.palette.guardianAccent[v].tileTint,
                cursor: 'pointer',
                padding: 0,
              }}
            >
              <Guardian variant={v} size={52} />
              {selected && (
                <>
                  {/* Gold selection ring (AC-02: 3px solid gold, inset -3px, radius 25). */}
                  <Box
                    aria-hidden
                    sx={{
                      position: 'absolute',
                      inset: '-3px',
                      border: `3px solid ${theme.palette.gold.main}`,
                      borderRadius: '25px',
                      pointerEvents: 'none',
                    }}
                  />
                  {/* Gold check badge - pops in with a ~0.25s scale animation
                      (design pack Gotcha: transform:scale only, never opacity,
                      for list-item entrances). */}
                  <Box
                    aria-hidden
                    sx={{
                      position: 'absolute',
                      top: -7,
                      right: -7,
                      width: 24,
                      height: 24,
                      borderRadius: '50%',
                      bgcolor: 'gold.main',
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'center',
                      boxShadow: `0 3px 8px -2px ${alpha(theme.palette.gold.main, 0.9)}`,
                      fontSize: 12,
                      color: 'text.primary',
                      animation: 'qsGuardianCheckPop 0.25s ease',
                      '@keyframes qsGuardianCheckPop': {
                        '0%': { transform: 'scale(0.4)' },
                        '60%': { transform: 'scale(1.15)' },
                        '100%': { transform: 'scale(1)' },
                      },
                    }}
                  >
                    <FontAwesomeIcon icon="check" />
                  </Box>
                </>
              )}
            </Box>
          );
        })}
      </Box>
    </>
  );
}
