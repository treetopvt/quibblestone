// ----------------------------------------------------------------------------
//  ReactionRow - the four-pill reaction row on the Reveal (reveal-delight/01,
//  issue #56; docs/design/README.md Screens screen 6).
//
//  This is the lightest-weight, highest-warmth addition to the payoff moment
//  (README section 10): a way for the room to say "that was funny" without
//  typing anything. It renders four equal pill buttons - Laugh (gold), Heart
//  (coral), Wow (teal, the sparkle wand), Star (purple/primary) - each showing
//  a FontAwesome icon and a live count in Fredoka 600 16px (AC-01).
//
//  REUSE CONTRACT (why this is a standalone component, not inline in Reveal):
//  Reveal is deliberately ROOM-AGNOSTIC - it knows nothing about counts, the
//  hub, or solo-vs-group. It only renders whatever ReactNode the caller hands
//  to its `reactionRow` slot (mirroring its `attribution` / `taleFeedback`
//  slots). So the counting/broadcast wiring lives in the PARENT:
//    - Solo (AC-05) holds counts in local useState, bumps them on tap, no hub.
//    - Group play (AC-04) feeds counts from the hub's ReactionCountsChanged
//      broadcast and calls the hub's React invoke (fire-and-forget) on tap.
//  Either way this component is identical: it renders `counts`, calls
//  `onReact(type)` on a tap, and OWNS the ephemeral floating-icon animation
//  locally (the `floaters` state below).
//
//  Animation discipline (this feature's DOCUMENTED footgun - AC-02/AC-03): a
//  tap spawns a floating copy of the icon that RISES ~62px and scales to ~1.25
//  via a `transform`-ONLY @keyframes, then is removed from the DOM after
//  ~1100ms. Opacity is NEVER a keyframe step (an opacity keyframe with
//  fill-mode:both can leave a re-rendered/list element stuck invisible) - the
//  gentle fade-out is a plain CSS `transition` on the soon-removed element
//  instead. Only the FLOAT is gated behind prefers-reduced-motion; the count
//  increment (onReact) always fires so reactions work with motion off.
//
//  Child safety (AC-06): a reaction is a TYPE ENUM only - no free text, no
//  player identity. There is nothing here for the safety filter to check and no
//  new text-entry surface is introduced. Colors come from theme tokens only
//  (gold/coral/teal/primary .main); icons are FontAwesome, registered in
//  web/src/fontawesome.ts. No em dashes in any prose/strings.
// ----------------------------------------------------------------------------

import { useEffect, useRef, useState } from 'react';
import type { IconProp } from '@fortawesome/fontawesome-svg-core';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { keyframes, useTheme } from '@mui/material/styles';
import { Box, Stack, Typography } from '@mui/material';

/** The four allowed reaction types (Out of Scope: any type beyond these four). */
export type ReactionType = 'laugh' | 'heart' | 'wow' | 'star';

/** The live per-type tally the row renders (AC-01). Ephemeral per reveal - no persistence. */
export type ReactionCounts = Record<ReactionType, number>;

export interface ReactionRowProps {
  /** The current per-type counts to render (the caller owns where these come from - solo state or the hub). */
  counts: ReactionCounts;
  /** Called on a pill tap with the tapped type. The caller increments its own counts (and, in group play, invokes the hub). */
  onReact: (type: ReactionType) => void;
}

/** One pill's static config: which theme color and FontAwesome icon it wears. */
interface PillSpec {
  type: ReactionType;
  /** A theme palette key resolved as `theme.palette[color].main` (no hardcoded hex). */
  color: 'gold' | 'coral' | 'teal' | 'primary';
  icon: IconProp;
  /** Accessible label (a11y only - not shown; the pill shows the icon + count). */
  label: string;
}

// The four pills in design order (AC-01): Laugh gold, Heart coral, Wow teal
// (sparkle wand), Star purple/primary. Star + Wow reuse already-registered
// icons; Laugh + Heart are the two new registrations in fontawesome.ts.
const PILLS: readonly PillSpec[] = [
  { type: 'laugh', color: 'gold', icon: 'face-laugh-beam', label: 'Laugh' },
  { type: 'heart', color: 'coral', icon: 'heart', label: 'Heart' },
  { type: 'wow', color: 'teal', icon: 'wand-magic-sparkles', label: 'Wow' },
  { type: 'star', color: 'primary', icon: 'star', label: 'Star' },
];

// How long a floating icon lives before it is removed from the DOM (AC-02).
const FLOAT_MS = 1100;

// The rise+scale entrance (AC-02): TRANSFORM ONLY (translateY + scale), never an
// opacity step (AC-03's documented footgun). The fade-out is a CSS transition on
// the Floater element, not a keyframe here, so a re-render can never leave a
// re-used element stuck invisible.
const floatRise = keyframes`
  0% { transform: translateY(0) scale(1); }
  100% { transform: translateY(-62px) scale(1.25); }
`;

/** One live floating icon: a type + its own id so React can key + remove it. */
interface Floater {
  id: number;
  type: ReactionType;
}

/**
 * True when the user prefers reduced motion. Read once per render via matchMedia
 * (guarded for non-browser/SSR). Only the float is gated on this - the count
 * increment always fires (AC-05 / the reduced-motion note in the story).
 */
function prefersReducedMotion(): boolean {
  return (
    typeof window !== 'undefined' &&
    typeof window.matchMedia === 'function' &&
    window.matchMedia('(prefers-reduced-motion: reduce)').matches
  );
}

/**
 * One floating icon that rises (transform keyframe) and fades (opacity
 * transition, NOT a keyframe - AC-03), then removes itself after ~1100ms.
 * Manages its own fade + teardown so the parent only tracks the list.
 */
function FloatingIcon({
  icon,
  color,
  onDone,
}: {
  icon: IconProp;
  color: string;
  onDone: () => void;
}) {
  // Start visible, then flip to trigger the opacity TRANSITION (never a keyframe).
  const [leaving, setLeaving] = useState(false);

  useEffect(() => {
    // Kick the fade on the next frame so the transition has a start state to move
    // from, and schedule removal once the ~1100ms rise/fade completes.
    const raf = requestAnimationFrame(() => setLeaving(true));
    const timer = window.setTimeout(onDone, FLOAT_MS);
    return () => {
      cancelAnimationFrame(raf);
      window.clearTimeout(timer);
    };
  }, [onDone]);

  return (
    <Box
      aria-hidden
      sx={{
        position: 'absolute',
        bottom: '100%',
        left: '50%',
        marginLeft: '-10px',
        fontSize: 20,
        color,
        pointerEvents: 'none',
        // Rise + scale via transform ONLY (AC-03). `forwards` holds the final
        // transform for the brief moment before the element is removed.
        animation: `${floatRise} ${FLOAT_MS}ms ease-out forwards`,
        // Fade-out as a plain transition on this soon-removed element - never a
        // keyframe opacity step (the documented footgun, AC-03).
        opacity: leaving ? 0 : 1,
        transition: `opacity ${FLOAT_MS}ms ease-out`,
      }}
    >
      <FontAwesomeIcon icon={icon} />
    </Box>
  );
}

/**
 * The reaction row: four theme-colored pills, each with an icon + live count and
 * its own floating-icon layer. Tapping a pill always calls onReact (AC-02/AC-05)
 * and, unless reduced motion is on, spawns a rising/fading floater (AC-02/AC-03).
 */
export function ReactionRow({ counts, onReact }: ReactionRowProps) {
  const theme = useTheme();
  const [floaters, setFloaters] = useState<Floater[]>([]);
  // A monotonic id source so every floater has a stable, unique React key even
  // when the same pill is tapped rapidly (Out of Scope: no per-player de-dupe).
  const nextId = useRef(0);

  const handleTap = (type: ReactionType) => {
    // The count increment ALWAYS happens (never gated on motion) - AC-05.
    onReact(type);
    // Gate ONLY the float behind reduced motion (AC-02 reduced-motion note).
    if (prefersReducedMotion()) return;
    const id = nextId.current;
    nextId.current += 1;
    setFloaters((current) => [...current, { id, type }]);
  };

  const removeFloater = (id: number) =>
    setFloaters((current) => current.filter((floater) => floater.id !== id));

  return (
    <Stack direction="row" spacing={1.5} sx={{ px: 5, pt: 1, pb: 1.5 }}>
      {PILLS.map((pill) => {
        const pillColor = theme.palette[pill.color].main;
        return (
          <Box
            key={pill.type}
            component="button"
            type="button"
            aria-label={`${pill.label} (${counts[pill.type]})`}
            onClick={() => handleTap(pill.type)}
            sx={{
              // Big, chunky, high-contrast tap target (README section 10).
              position: 'relative',
              flex: 1,
              minWidth: 0,
              minHeight: 52,
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              gap: 1,
              cursor: 'pointer',
              border: `2px solid ${pillColor}`,
              borderRadius: 999,
              px: 1.5,
              py: 1,
              bgcolor: theme.palette.background.paper,
              color: pillColor,
              transition: 'background-color 120ms ease-out, transform 120ms ease-out',
              '&:hover': { bgcolor: theme.palette.action.hover },
              '&:active': { transform: 'scale(0.94)' },
              '&:focus-visible': { outline: `2px solid ${pillColor}`, outlineOffset: 2 },
            }}
          >
            <FontAwesomeIcon icon={pill.icon} style={{ width: 18, height: 18 }} />
            <Typography
              component="span"
              sx={{
                fontFamily: '"Fredoka", sans-serif',
                fontWeight: 600,
                fontSize: 16,
                lineHeight: 1,
                color: 'text.primary',
              }}
            >
              {counts[pill.type]}
            </Typography>

            {/* This pill's floating-icon layer: each rising/fading copy removes
                itself after ~1100ms (AC-02). Absolutely positioned above the pill
                so it never shifts layout. */}
            {floaters
              .filter((floater) => floater.type === pill.type)
              .map((floater) => (
                <FloatingIcon
                  key={floater.id}
                  icon={pill.icon}
                  color={pillColor}
                  onDone={() => removeFloater(floater.id)}
                />
              ))}
          </Box>
        );
      })}
    </Stack>
  );
}
