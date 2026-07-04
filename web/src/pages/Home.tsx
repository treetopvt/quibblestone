// ----------------------------------------------------------------------------
//  Home - the welcome / entry screen (session-engine/01, design screen 1).
//
//  FIT-TO-VIEWPORT DE-CLUTTER (design-handoff, 2026-07): this screen was
//  rebuilt to fit ONE phone viewport (~390x844) without a stray gap. The
//  previous composition (kicker pill + hero + tagline paragraph + four
//  stacked text links) overflowed a real phone. The fix is structural, not
//  cosmetic:
//    - The kicker chip ("Family Word Quest") and the two-line tagline
//      paragraph are GONE - the stone-tablet hero is now the only product
//      pitch, and carries more visual weight (it is deliberately the biggest
//      thing on screen, per the design brief's "generous hero" note).
//    - The four stacked text links (solo / favorites / gallery / account)
//      are gone. "Play solo" is promoted to its own full-width pill (a way to
//      START playing, so it stays visually distinct from the CTAs above it).
//      Favorites / Our tales / Account collapse into a single-row, 5-column
//      icon utility bar, which also makes room for two more entries: "Get
//      more" (storefront/paywall, gold tint) and "Support" (tip jar, coral
//      tint). Both now open real destinations (billing-entitlements shipped
//      `/get-more` and `/support`) and render as fully-enabled, tappable
//      chips - same affordance as the other three.
//
//  VIEWPORT-RESILIENCE PASS (2026-07): the original de-clutter pinned the root
//  to a FIXED height (`height: 100dvh`, `overflow: hidden`) with a
//  `justifyContent: space-between` spread. That fit portrait phones but broke
//  everywhere else, so this pass replaces the fixed frame with a centered,
//  scroll-safe, responsive one:
//    - The root is now `minHeight: 100dvh` (NOT a clipped `height`) with
//      `justifyContent: center`. On a SHORT viewport (a landscape phone) the
//      column grows past the viewport and the PAGE scrolls, so nothing is ever
//      clipped - previously the CTAs, the "play solo" pill and the whole
//      utility bar were cut off in landscape and the game could not be
//      started at all. On a TALL viewport (a tablet held portrait) the column
//      centers as a block instead of flinging the hero to the top and the CTAs
//      to the bottom with a dead gap between them (the old space-between). This
//      is the same content-with-inner/page-scroll posture Solo / Lobby /
//      Reveal already use for their 100dvh screens.
//    - The layout scales UP on larger devices (tablet / desktop) via `md`
//      breakpoints rather than staying a lonely 430px phone strip: a wider
//      column, a bigger stone tablet, a larger wordmark and a larger mascot.
//      It stays a single centered column (not a bespoke multi-column desktop
//      layout - that is a separate, larger piece of work) but finally uses the
//      room instead of marooning a phone-sized card in a sea of parchment.
//
//  What's still here from the original build, unchanged in spirit:
//    - the stone-tablet hero panel (arched, glowing carved rim) with the
//      3-glyph rune row, the two-tone "QuibbleStone" wordmark, the "CARVE A
//      SILLY TALE" caption, and the HeroGuardian mascot
//    - a one-line tagline
//    - the gold "Create a game" primary CTA ("+" icon) and the
//      outlined-purple "Join a game" secondary button
//    - the "No account needed" reassurance line with a teal check icon
//
//  Contracts reused (never re-specified here): the gold CTA is just
//  <Button variant="contained"> and the outlined-purple secondary is just
//  <Button variant="outlined"> - both styled once in web/src/theme.ts. All
//  colors / gradients / radii come from the theme (theme.palette.tablet,
//  theme.palette.primary, theme.palette.teal, theme.palette.gold,
//  theme.palette.coral, ...) - no hardcoded hex here. Icons are FontAwesome
//  (already registered in web/src/fontawesome.ts).
//
//  Behavior (all wiring below is UNCHANGED from before this pass):
//    - "Create a game" calls onCreateGame (App wires it to open the HostSetup
//      screen, where the host names itself + picks a Guardian before the room
//      is minted - build/host-identity).
//    - "Join a game" opens the Join screen via onJoinGame.
//    - "Play solo right now" calls onPlaySolo - deliberately NOT gated by
//      `disabled` (the hub-connection state). Solo has no room, no join code,
//      and no SignalR round-trip at all, so it works even before (or without)
//      the real-time connection coming up.
//    - The utility bar's "Favorites" / "Our tales" / "Account" chips call
//      onFavorites / onGallery / onAccount respectively - also not gated by
//      `disabled`, since all three are device-local surfaces (favorites list,
//      IndexedDB gallery, purchaser restore) that need no hub connection.
//    - "Get more" opens the storefront/paywall via onGetMore (App wires this
//      to the /get-more route, billing-entitlements/04). "Support" opens the
//      tip jar via onSupport (App wires this to /support,
//      billing-entitlements/02). Neither is gated by `disabled` - browsing
//      what is on offer or leaving a tip needs no hub connection, same
//      reasoning as Favorites / Our tales / Account above.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { alpha, useTheme } from '@mui/material/styles';
import { Box, Button, Stack, Typography, useMediaQuery } from '@mui/material';
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
  /**
   * Open the device-local Favorites list (story-selection/06, AC-02).
   * Deliberately NOT gated by `disabled` - favorites need no hub connection.
   */
  onFavorites: () => void;
  /**
   * Open the device-local "Tales we've carved" gallery (keepsake-gallery/03,
   * AC-01). Deliberately NOT gated by `disabled` - the gallery needs no hub
   * connection, exactly like `onFavorites` above.
   */
  onGallery: () => void;
  /**
   * Open the PURCHASER-only Account / restore surface (accounts-identity/03,
   * AC-04). This is the ONE place the sign-in surface is reachable - it never
   * appears on Join / Lobby / word-entry / Reveal (a child's flow). Deliberately
   * NOT gated by `disabled`: restoring a purchase needs no hub connection, and
   * signing in has ZERO effect on free play (AC-03).
   */
  onAccount: () => void;
  /**
   * Open the "Get more" storefront / paywall surface (billing-entitlements/04).
   * Wired to the /get-more route. NOT gated by `disabled` - browsing what is on
   * offer needs no hub connection, and buying has zero effect on free play.
   */
  onGetMore: () => void;
  /**
   * Open the "Support us" tip jar surface (billing-entitlements/02). Wired to the
   * /support route. NOT gated by `disabled` - tipping needs no hub connection.
   */
  onSupport: () => void;
  /** True while a create-room request is in flight - disables the CTA to avoid double-taps. */
  creating?: boolean;
  /** True until the real-time connection is ready - the CTAs need the hub to act. */
  disabled?: boolean;
}

/** One column of the bottom utility icon bar (40x40 chip + tiny label). */
interface UtilityBarItemProps {
  icon: 'star' | 'book-open' | 'user' | 'gift' | 'mug-saucer';
  label: string;
  chipBgcolor: string;
  iconColor: string;
  labelColor: string;
  onClick?: () => void;
  disabled?: boolean;
}

function UtilityBarItem({
  icon,
  label,
  chipBgcolor,
  iconColor,
  labelColor,
  onClick,
  disabled = false,
}: UtilityBarItemProps) {
  return (
    <Box
      component={disabled ? 'div' : 'button'}
      type={disabled ? undefined : 'button'}
      onClick={disabled ? undefined : onClick}
      aria-disabled={disabled || undefined}
      sx={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        gap: 0.75,
        flex: 1,
        border: 'none',
        background: 'none',
        p: 0,
        cursor: disabled ? 'default' : 'pointer',
        opacity: disabled ? 0.55 : 1,
        WebkitTapHighlightColor: 'transparent',
      }}
    >
      <Box
        aria-hidden
        sx={{
          width: 40,
          height: 40,
          borderRadius: '13px',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          fontSize: 16,
          bgcolor: chipBgcolor,
          color: iconColor,
        }}
      >
        <FontAwesomeIcon icon={icon} />
      </Box>
      <Typography
        sx={{
          fontFamily: '"Nunito", sans-serif',
          fontWeight: 800,
          fontSize: 10.5,
          lineHeight: 1.1,
          textAlign: 'center',
          color: labelColor,
        }}
      >
        {label}
      </Typography>
    </Box>
  );
}

export function Home({
  onCreateGame,
  onJoinGame,
  onPlaySolo,
  onFavorites,
  onGallery,
  onAccount,
  onGetMore,
  onSupport,
  creating = false,
  disabled = false,
}: HomeProps) {
  const theme = useTheme();
  // P3 (tablet / desktop): scale the hero up on md+ rather than leaving a
  // phone-sized card marooned in a wide viewport. A boolean media-query drives
  // the one prop that is a raw number (HeroGuardian's width); everything else
  // scales via responsive sx breakpoint objects below.
  const mdUp = useMediaQuery(theme.breakpoints.up('md'));

  return (
    <Stack
      alignItems="center"
      sx={{
        position: 'relative',
        // P1: minHeight (NOT a clipped fixed `height`) so a SHORT viewport (a
        // landscape phone) grows the column past the fold and the PAGE scrolls -
        // the CTAs / play-solo pill / utility bar are never cropped off-screen
        // as they were under the old `height: 100dvh; overflow: hidden`.
        minHeight: '100dvh',
        display: 'flex',
        flexDirection: 'column',
        // P2: center the column as a block. On a TALL viewport (a tablet held
        // portrait) this centers the whole card rather than flinging the hero to
        // the top and the CTAs to the bottom with a dead gap (the old
        // space-between). When content is taller than the viewport there is no
        // free space to distribute, so children stack from the top and the page
        // scrolls - centering never clips.
        justifyContent: 'center',
        // Screen content horizontal padding (design: 26px on Home) via the theme
        // spacing scale (1 = 4px); roomier vertical breathing space on md+.
        px: 6.5,
        py: { xs: 3, md: 5 },
        mx: 'auto',
        // P3: a wider column on tablet / desktop, instead of a lonely 430px phone
        // strip stranded in a sea of parchment.
        maxWidth: { xs: 430, md: 500 },
      }}
    >
      {/* HERO GROUP: the stone tablet plus its ambient glow, wrapped together so
          the glow tracks the hero once the column is vertically centered. Before
          the viewport-resilience pass the glow was pinned to the ROOT's top edge,
          which detached it from the (now centered) hero on tall viewports. */}
      <Box sx={{ position: 'relative', width: '100%', display: 'flex', justifyContent: 'center' }}>
        {/* Ambient magic glow behind the hero (purple -> gold radial), centered on
            the tablet; grows a touch on md+ with the larger hero. */}
        <Box
          aria-hidden
          sx={{
            position: 'absolute',
            top: '46%',
            left: '50%',
            transform: 'translate(-50%, -50%)',
            width: { xs: 430, md: 500 },
            height: { xs: 430, md: 500 },
            borderRadius: '50%',
            pointerEvents: 'none',
            zIndex: 0,
            background: `radial-gradient(circle, ${alpha(theme.palette.primary.main, 0.3)} 0%, ${alpha(
              theme.palette.gold.main,
              0.14,
            )} 42%, ${alpha(theme.palette.parchment.mid, 0)} 70%)`,
          }}
        />

        {/* STONE-TABLET HERO: the sole product pitch (kicker pill + tagline
            paragraph were removed). The brand centerpiece: arched panel, glowing
            carved rim, wordmark + mascot. */}
        <Box
          sx={{
            position: 'relative',
            zIndex: 1,
            width: '100%',
            // Widened from 296 -> 336 (xs) so the 42px "QuibbleStone" wordmark
            // (~279px of glyphs) fits INSIDE the 26px rim padding on both sides
            // instead of bleeding into the carved rim (inner = 336 - 52 = 284px).
            // On md+ the tablet and wordmark grow together (384 / 46px), holding
            // the same "fits with clearance" relationship on larger devices.
            maxWidth: { xs: 336, md: 384 },
            px: 6.5,
            pt: 6,
            pb: 4.5,
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
              fontSize: { xs: 42, md: 46 },
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
              fontFamily: '"Nunito", sans-serif',
              fontSize: 12,
              fontWeight: 700,
              letterSpacing: '2px',
              textTransform: 'uppercase',
              color: 'text.secondary',
            }}
          >
            Carve a silly tale
          </Typography>

          {/* Hero mascot (owns its own idle bob); grows on md+ (188 vs 158). */}
          <Box sx={{ display: 'flex', justifyContent: 'center', mt: 2.25 }}>
            <HeroGuardian width={mdUp ? 188 : 158} />
          </Box>
        </Box>
      </Box>

      {/* One-line tagline (the tablet is now the only pitch; this is a single
          supporting line, not a paragraph). */}
      <Typography
        sx={{
          mt: 2,
          textAlign: 'center',
          fontFamily: '"Nunito", sans-serif',
          fontSize: { xs: 16.5, md: 18 },
          lineHeight: 1.3,
          fontWeight: 600,
          color: 'text.secondary',
        }}
      >
        Fill in the blanks together and watch a wild tale get carved into stone.
      </Typography>

      {/* Primary actions + reassurance + play-solo path, below the hero + tagline.
          (The old `mt: 'auto'` bottom-pin is gone now that the whole column is
          vertically centered rather than spread with space-between.) */}
      <Stack spacing={2} sx={{ width: '100%', pt: 3 }}>
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
        <Stack direction="row" spacing={1.5} alignItems="center" justifyContent="center">
          <Box sx={{ color: 'teal.main', fontSize: 15, display: 'flex' }}>
            <FontAwesomeIcon icon="check" />
          </Box>
          <Typography sx={{ fontSize: 13, fontWeight: 700, color: 'text.secondary' }}>
            No account needed - pick a name &amp; play
          </Typography>
        </Stack>

        {/* Play-path link (single-player/01): a subtle full-width pill, a way
            to START playing, so it stays visually distinct from the utility
            nav bar below. Not gated by `disabled` - Solo needs no hub
            connection. */}
        <Box
          component="button"
          type="button"
          onClick={onPlaySolo}
          sx={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            gap: 1.5,
            width: '100%',
            height: 46,
            borderRadius: '14px',
            border: 'none',
            bgcolor: alpha(theme.palette.primary.main, 0.08),
            color: 'primary.main',
            cursor: 'pointer',
            WebkitTapHighlightColor: 'transparent',
          }}
        >
          <FontAwesomeIcon icon="play" style={{ fontSize: 13 }} />
          <Typography
            component="span"
            sx={{ fontFamily: '"Fredoka", sans-serif', fontWeight: 600, fontSize: 16, color: 'inherit' }}
          >
            Play solo right now
          </Typography>
        </Box>
      </Stack>

      {/* Utility icon bar: folds Favorites / Our tales / Account / Get more /
          Support into ONE row of five equal columns, replacing the old
          stacked text links. Get more (gold) opens the storefront/paywall
          (/get-more) and Support (coral) opens the tip jar (/support) - both
          wired now that billing-entitlements shipped those surfaces. */}
      <Stack
        direction="row"
        sx={{
          width: '100%',
          mt: 2,
          pt: 2,
          borderTop: `1.5px solid ${alpha(theme.palette.stoneEdge.main, 0.16)}`,
        }}
      >
        <UtilityBarItem
          icon="star"
          label="Favorites"
          chipBgcolor={alpha(theme.palette.primary.main, 0.1)}
          iconColor={theme.palette.primary.main}
          labelColor={theme.palette.text.secondary}
          onClick={onFavorites}
        />
        <UtilityBarItem
          icon="book-open"
          label="Our tales"
          chipBgcolor={alpha(theme.palette.primary.main, 0.1)}
          iconColor={theme.palette.primary.main}
          labelColor={theme.palette.text.secondary}
          onClick={onGallery}
        />
        <UtilityBarItem
          icon="user"
          label="Account"
          chipBgcolor={alpha(theme.palette.primary.main, 0.1)}
          iconColor={theme.palette.primary.main}
          labelColor={theme.palette.text.secondary}
          onClick={onAccount}
        />
        <UtilityBarItem
          icon="gift"
          label="Get more"
          chipBgcolor={alpha(theme.palette.gold.main, 0.16)}
          iconColor={theme.palette.gold.dark}
          labelColor={theme.palette.gold.dark}
          onClick={onGetMore}
        />
        <UtilityBarItem
          icon="mug-saucer"
          label="Support"
          chipBgcolor={alpha(theme.palette.coral.main, 0.14)}
          iconColor={theme.palette.coral.main}
          labelColor={theme.palette.text.secondary}
          onClick={onSupport}
        />
      </Stack>
    </Stack>
  );
}
