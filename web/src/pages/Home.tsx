// ----------------------------------------------------------------------------
//  Home - the welcome / entry screen (session-engine/01, design screen 1).
//
//  The front door to QuibbleStone: no login, no PII. Faithfully recreates
//  docs/design/Home.dc.html / docs/design/README.md (Screens - screen 1):
//    - a kicker chip (purple pill, teal glowing dot, "FAMILY WORD QUEST")
//    - a stone-tablet hero panel (arched, glowing carved rim) with the two-tone
//      "QuibbleStone" wordmark, a "CARVE A SILLY TALE" caption, and the
//      HeroGuardian mascot
//    - a tagline
//    - the gold "Create a game" primary CTA ("+" icon) and the outlined-purple
//      "Join a game" secondary button
//    - a "No account needed - just pick a name & play" reassurance line with a
//      teal check icon
//
//  Contracts reused (never re-specified here): the gold CTA is just
//  <Button variant="contained"> and the outlined-purple secondary is just
//  <Button variant="outlined"> - both styled once in web/src/theme.ts. All
//  colors / gradients / radii come from the theme (theme.palette.tablet,
//  theme.palette.primary, theme.palette.teal, ...) - no hardcoded hex here.
//  Icons are FontAwesome (registered in web/src/fontawesome.ts).
//
//  Behavior: "Create a game" calls onCreateGame (App wires it to open the
//  HostSetup screen, where the host names itself + picks a Guardian before the
//  room is minted - build/host-identity). "Join a game" opens the Join screen via
//  onJoinGame. Home animations (mascot bob is built into
//  HeroGuardian; sparkles / ambient-glow pulse) are a delight-tier pass and are
//  intentionally minimal here (out of scope per the story).
//
//  Solo entry (single-player/01, ADDITIVE): a clearly-secondary text link,
//  "Or play solo right now", sits below the reassurance row and calls
//  onPlaySolo. It is deliberately NOT gated by `disabled` (the hub connection
//  state) - Solo has no room, no join code, and no SignalR round-trip at all,
//  so it works even before (or without) the real-time connection coming up.
//  The Create/Join CTAs above it are untouched by this addition.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { alpha, useTheme } from '@mui/material/styles';
import { Box, Button, Link, Stack, Typography } from '@mui/material';
import { HeroGuardian } from '../components';

export interface HomeProps {
  /** Create a room and land in the lobby as host (AC-01). App wires this to the hub. */
  onCreateGame: () => void;
  /** Go to the Join screen (story 02). A no-op placeholder until then (AC-01). */
  onJoinGame: () => void;
  /**
   * Start a local solo round (single-player/01). Deliberately NOT gated by
   * `disabled` at the call site below - Solo needs no SignalR connection.
   */
  onPlaySolo: () => void;
  /** True while a create-room request is in flight - disables the CTA to avoid double-taps. */
  creating?: boolean;
  /** True until the real-time connection is ready - the CTAs need the hub to act. */
  disabled?: boolean;
}

export function Home({ onCreateGame, onJoinGame, onPlaySolo, creating = false, disabled = false }: HomeProps) {
  const theme = useTheme();

  return (
    <Stack
      alignItems="center"
      sx={{
        position: 'relative',
        minHeight: '100vh',
        // Screen content horizontal padding (design: 26px on Home) via the
        // theme spacing scale (1 = 4px), plus breathing room top and bottom.
        px: 6.5,
        pt: 3,
        pb: 6.5,
        mx: 'auto',
        maxWidth: 430,
      }}
    >
      {/* Ambient magic glow behind the hero (purple -> gold radial). */}
      <Box
        aria-hidden
        sx={{
          position: 'absolute',
          top: theme.spacing(-11),
          left: '50%',
          transform: 'translateX(-50%)',
          width: 430,
          height: 430,
          borderRadius: '50%',
          pointerEvents: 'none',
          background: `radial-gradient(circle, ${alpha(theme.palette.primary.main, 0.3)} 0%, ${alpha(
            theme.palette.gold.main,
            0.14,
          )} 42%, ${alpha(theme.palette.parchment.mid, 0)} 70%)`,
        }}
      />

      {/* Kicker chip: purple pill, teal glowing dot, "FAMILY WORD QUEST". */}
      <Box
        sx={{
          display: 'inline-flex',
          alignItems: 'center',
          gap: 1.75,
          mt: 1.5,
          px: 3.5,
          py: 1.5,
          borderRadius: 999,
          bgcolor: alpha(theme.palette.primary.main, 0.1),
          border: `1px solid ${alpha(theme.palette.primary.main, 0.22)}`,
        }}
      >
        <Box
          aria-hidden
          sx={{
            width: 6,
            height: 6,
            borderRadius: '50%',
            bgcolor: 'teal.main',
            boxShadow: `0 0 8px ${theme.palette.teal.main}`,
          }}
        />
        <Typography
          variant="overline"
          sx={{ fontSize: 12.5, fontWeight: 800, lineHeight: 1, color: 'primary.main' }}
        >
          Family Word Quest
        </Typography>
      </Box>

      {/* STONE-TABLET HERO: arched panel, glowing carved rim, wordmark + mascot. */}
      <Box
        sx={{
          position: 'relative',
          mt: 5,
          width: 296,
          maxWidth: '100%',
          px: 6.5,
          pt: 7.5,
          pb: 5.5,
          borderRadius: '96px 96px 30px 30px',
          background: theme.palette.tablet.gradient,
          boxShadow: `0 26px 50px -22px ${alpha(theme.palette.primary.main, 0.55)}, inset 0 3px 0 ${alpha(
            theme.palette.common.white,
            0.55,
          )}, inset 0 -5px 14px ${alpha(theme.palette.stoneEdge.main, 0.4)}, 0 0 0 6px ${alpha(
            theme.palette.common.white,
            0.3,
          )}`,
        }}
      >
        {/* Glowing carved rim (absolutely-positioned inset border + inner glow). */}
        <Box
          aria-hidden
          sx={{
            position: 'absolute',
            inset: '9px',
            borderRadius: '84px 84px 22px 22px',
            border: `2.5px solid ${alpha(theme.palette.stoneEdge.main, 0.5)}`,
            boxShadow: `inset 0 0 18px ${alpha(theme.palette.gold.main, 0.3)}`,
            pointerEvents: 'none',
          }}
        />

        {/* Top rune inscription. */}
        <Box
          aria-hidden
          sx={{
            display: 'flex',
            justifyContent: 'center',
            gap: 2.75,
            mb: 1.5,
            fontFamily: '"Fredoka", sans-serif',
            fontSize: 13,
            fontWeight: 600,
          }}
        >
          <Box component="span" sx={{ color: alpha(theme.palette.primary.main, 0.55) }}>
            &#10022;
          </Box>
          <Box
            component="span"
            sx={{
              color: alpha(theme.palette.gold.main, 0.85),
              textShadow: `0 0 8px ${alpha(theme.palette.gold.main, 0.6)}`,
            }}
          >
            &#9672;
          </Box>
          <Box component="span" sx={{ color: alpha(theme.palette.primary.main, 0.55) }}>
            &#10022;
          </Box>
        </Box>

        {/* Wordmark: "Quibble" purple + "Stone" gold, carved emboss. */}
        <Typography
          component="h1"
          sx={{
            textAlign: 'center',
            fontFamily: '"Fredoka", sans-serif',
            fontWeight: 700,
            fontSize: 39,
            lineHeight: 0.98,
            letterSpacing: '0.5px',
            textShadow: `0 2px 0 ${alpha(theme.palette.common.white, 0.45)}, 0 3px 6px ${alpha(
              theme.palette.stoneEdge.main,
              0.35,
            )}`,
          }}
        >
          <Box component="span" sx={{ color: 'primary.main' }}>
            Quibble
          </Box>
          <Box component="span" sx={{ color: 'gold.main' }}>
            Stone
          </Box>
        </Typography>

        <Typography
          sx={{
            textAlign: 'center',
            mt: 1,
            fontSize: 12.5,
            fontWeight: 700,
            letterSpacing: '2px',
            textTransform: 'uppercase',
            color: 'text.secondary',
          }}
        >
          Carve a silly tale
        </Typography>

        {/* Hero mascot (owns its own idle bob). */}
        <Box sx={{ display: 'flex', justifyContent: 'center', mt: 2.5 }}>
          <HeroGuardian width={158} />
        </Box>
      </Box>

      {/* Tagline. */}
      <Typography
        sx={{
          mt: 5,
          px: 1.5,
          textAlign: 'center',
          fontSize: 16,
          lineHeight: 1.45,
          fontWeight: 600,
          color: 'text.secondary',
          textWrap: 'pretty',
        }}
      >
        Fill in the blanks together and watch a wild story get carved into stone - perfect for
        car rides &amp; kitchen tables.
      </Typography>

      {/* Actions: gold "Create a game" CTA + outlined-purple "Join a game". */}
      <Stack spacing={3.5} sx={{ width: '100%', mt: 'auto', pt: 5 }}>
        <Button
          variant="contained"
          fullWidth
          onClick={onCreateGame}
          disabled={disabled || creating}
          startIcon={<FontAwesomeIcon icon="plus" />}
        >
          {creating ? 'Creating...' : 'Create a game'}
        </Button>

        <Button
          variant="outlined"
          fullWidth
          onClick={onJoinGame}
          disabled={disabled}
          startIcon={<FontAwesomeIcon icon="right-to-bracket" />}
        >
          Join a game
        </Button>

        {/* Reassurance row: teal check + "No account needed". */}
        <Stack direction="row" spacing={1.75} alignItems="center" justifyContent="center" sx={{ mt: 0.5 }}>
          <Box sx={{ color: 'teal.main', fontSize: 16, display: 'flex' }}>
            <FontAwesomeIcon icon="check" />
          </Box>
          <Typography sx={{ fontSize: 13.5, fontWeight: 700, color: 'text.secondary' }}>
            No account needed - just pick a name &amp; play
          </Typography>
        </Stack>

        {/* Secondary/tertiary affordance (single-player/01): a plain text
            link, not styled as a Button, so it reads clearly as the lesser
            option beside the gold/outlined CTAs above. Not gated by
            `disabled` - Solo needs no hub connection. */}
        <Box sx={{ textAlign: 'center' }}>
          <Link
            component="button"
            type="button"
            onClick={onPlaySolo}
            underline="none"
            sx={{ fontSize: 13.5, fontWeight: 700, color: 'primary.main' }}
          >
            Or play solo right now
          </Link>
        </Box>
      </Stack>
    </Stack>
  );
}
