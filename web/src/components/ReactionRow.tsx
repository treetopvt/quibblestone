// ----------------------------------------------------------------------------
//  ReactionRow - the three-pill reaction row on the Reveal (reveal-delight/01,
//  issue #56; reactions v2 - the UX de-clutter).
//
//  This is the lightest-weight, highest-warmth addition to the payoff moment
//  (README section 10): a way for the room to say "that landed" without typing
//  anything. It renders THREE equal pill buttons - Love (teal, thumbs-up), Wow
//  (gold, face-surprise), Didn't like (coral, thumbs-down) - each showing a
//  FontAwesome icon, its VISIBLE label (so the pill's meaning is never a guess),
//  and a live count (AC-01).
//
//  The set was narrowed from four (Laugh/Heart/Wow/Star) to three, and the tap
//  behavior changed from a free-for-all bump to ONE REACTION PER USER: a tap
//  SELECTS a reaction, tapping a different one MOVES it, and tapping the one you
//  already hold toggles it off. This row is a CONTROLLED single-select: the
//  caller owns the selection state and passes it in via `selected`, exactly the
//  way the rest of Reveal keeps components room-agnostic (mirroring how Golden
//  Guardian's `myVote` is caller-owned). The row itself just renders `counts` +
//  `selected` and calls `onReact(type)` on a tap - it never tracks who holds
//  what. The MOVE/TOGGLE arithmetic on the counts lives in the caller (solo does
//  it locally; group play lets the SERVER do it and mirrors the broadcast tally).
//
//  REUSE CONTRACT (why this is a standalone component, not inline in Reveal):
//  Reveal is deliberately ROOM-AGNOSTIC - it knows nothing about counts, the
//  hub, or solo-vs-group. It only renders whatever ReactNode the caller hands
//  to its `reactionRow` slot (mirroring its `attribution` / `taleFeedback`
//  slots). So the counting/broadcast/selection wiring lives in the PARENT:
//    - Solo (AC-05) holds `counts` + `myReaction` in local useState, applies the
//      move/toggle on tap, and passes `selected={myReaction}` - no hub.
//    - Group play (AC-04) feeds `counts` from the hub's ReactionCountsChanged
//      broadcast (the SERVER de-dupes per connection) and tracks its own
//      `myReaction` locally for the highlight, calling the hub's React invoke
//      (fire-and-forget) on tap.
//  Either way this component is identical: it renders `counts` + `selected`,
//  calls `onReact(type)` on a tap, and OWNS the ephemeral floating-icon
//  animation locally (the `floaters` state below).
//
//  Animation discipline (this feature's DOCUMENTED footgun - AC-02/AC-03): a
//  tap spawns a floating copy of the icon that RISES ~62px and scales to ~1.25
//  via a `transform`-ONLY @keyframes, then is removed from the DOM after
//  ~1100ms. Opacity is NEVER a keyframe step (an opacity keyframe with
//  fill-mode:both can leave a re-rendered/list element stuck invisible) - the
//  gentle fade-out is a plain CSS `transition` on the soon-removed element
//  instead. Only the FLOAT is gated behind prefers-reduced-motion; the
//  count/selection change (onReact) always fires so reactions work with motion off.
//
//  Child safety (AC-06): a reaction is a TYPE ENUM only - no free text, no
//  player identity. There is nothing here for the safety filter to check and no
//  new text-entry surface is introduced. Colors come from theme tokens only
//  (teal/gold/coral .main); icons are FontAwesome, registered in
//  web/src/fontawesome.ts. No em dashes in any prose/strings.
// ----------------------------------------------------------------------------

import { useEffect, useRef, useState } from 'react';
import type { IconProp } from '@fortawesome/fontawesome-svg-core';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { keyframes, useTheme } from '@mui/material/styles';
import { Box, Stack, Typography } from '@mui/material';

/**
 * The three allowed reaction types (Out of Scope: any type beyond these three).
 * `'nope'` is the internal, readable id for the "Didn't like" pill.
 */
export type ReactionType = 'love' | 'wow' | 'nope';

/** The live per-type tally the row renders (AC-01). Ephemeral per reveal - no persistence. */
export type ReactionCounts = Record<ReactionType, number>;

export interface ReactionRowProps {
  /** The current per-type counts to render (the caller owns where these come from - solo state or the hub). */
  counts: ReactionCounts;
  /**
   * The reaction THIS user currently holds (shown highlighted), or null/undefined
   * when they hold none. The row is a CONTROLLED single-select: the caller owns
   * this state and applies the move/toggle to `counts` (or lets the server do it).
   */
  selected?: ReactionType | null;
  /**
   * Called on a pill tap with the tapped type. The caller decides what that means
   * against the current `selected`: SELECT (none held), MOVE (a different one held),
   * or TOGGLE OFF (the same one held). In group play it also invokes the hub.
   */
  onReact: (type: ReactionType) => void;
}

/** One pill's static config: which theme color and FontAwesome icon it wears. */
interface PillSpec {
  type: ReactionType;
  /** A theme palette key resolved as `theme.palette[color].main` (no hardcoded hex). */
  color: 'teal' | 'gold' | 'coral';
  icon: IconProp;
  /** Accessible label (a11y only - not shown; the pill shows the icon + count). */
  label: string;
}

// The three pills in design order (reactions v2): Love teal (thumbs-up), Wow gold
// (face-surprise, the "whoa" moment), Didn't like coral (thumbs-down). All three
// icons are already registered in fontawesome.ts.
const PILLS: readonly PillSpec[] = [
  { type: 'love', color: 'teal', icon: 'thumbs-up', label: 'Love' },
  { type: 'wow', color: 'gold', icon: 'face-surprise', label: 'Wow' },
  { type: 'nope', color: 'coral', icon: 'thumbs-down', label: "Didn't like" },
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
 * (guarded for non-browser/SSR). Only the float is gated on this - the
 * count/selection change always fires (AC-05 / the reduced-motion note in the story).
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

  // Keep the latest onDone in a ref so the mount effect below can fire it without
  // listing it as a dependency. The parent (ReactionRow) passes a fresh inline
  // `onDone` on every render (it closes over removeFloater), so depending on it
  // would restart this effect - re-arming the fade and pushing the removal
  // timeout out - on any parent re-render (e.g. a live count update), leaving the
  // floater on screen far longer than FLOAT_MS.
  const onDoneRef = useRef(onDone);
  onDoneRef.current = onDone;

  useEffect(() => {
    // Kick the fade on the next frame so the transition has a start state to move
    // from, and schedule removal once the ~1100ms rise/fade completes. Runs ONCE
    // on mount (empty deps) - the removal fires exactly FLOAT_MS after the icon
    // appears, regardless of parent re-renders.
    const raf = requestAnimationFrame(() => setLeaving(true));
    const timer = window.setTimeout(() => onDoneRef.current(), FLOAT_MS);
    return () => {
      cancelAnimationFrame(raf);
      window.clearTimeout(timer);
    };
  }, []);

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
 * The reaction row: three theme-colored pills, each with an icon + live count and
 * its own floating-icon layer, rendered as a CONTROLLED single-select over
 * `selected`. Tapping a pill always calls onReact (AC-02/AC-05) and, unless
 * reduced motion is on, spawns a rising/fading floater (AC-02/AC-03). The pill the
 * user currently holds (`selected`) wears a filled, ringed "selected" state.
 */
export function ReactionRow({ counts, selected, onReact }: ReactionRowProps) {
  const theme = useTheme();
  const [floaters, setFloaters] = useState<Floater[]>([]);
  // A monotonic id source so every floater has a stable, unique React key even
  // when the same pill is tapped rapidly.
  const nextId = useRef(0);

  const handleTap = (type: ReactionType) => {
    // The selection/count change ALWAYS happens (never gated on motion) - AC-05.
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
    <Stack direction="row" spacing={1.25} sx={{ px: 4, pt: 0.5, pb: 0.5 }}>
      {PILLS.map((pill) => {
        const pillColor = theme.palette[pill.color].main;
        const isSelected = selected === pill.type;
        return (
          <Box
            key={pill.type}
            component="button"
            type="button"
            aria-label={`${pill.label} (${counts[pill.type]})`}
            aria-pressed={isSelected}
            onClick={() => handleTap(pill.type)}
            sx={{
              // Compact but still a comfortable tap target - deliberately slimmer
              // than the app's chunky CTAs so the reactions never steal story real
              // estate on the payoff screen.
              position: 'relative',
              flex: 1,
              minWidth: 0,
              minHeight: 38,
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              gap: 0.5,
              cursor: 'pointer',
              border: `1.5px solid ${pillColor}`,
              borderRadius: 999,
              px: 0.75,
              py: 0.5,
              // Selected pills fill with their color; unselected stay paper with a
              // colored outline. The selection highlight is what shows the user
              // which single reaction they currently hold (one-per-user).
              bgcolor: isSelected ? pillColor : theme.palette.background.paper,
              color: isSelected ? theme.palette.common.white : pillColor,
              transition: 'background-color 120ms ease-out, transform 120ms ease-out',
              '&:hover': { bgcolor: isSelected ? pillColor : theme.palette.action.hover },
              '&:active': { transform: 'scale(0.94)' },
              '&:focus-visible': { outline: `2px solid ${pillColor}`, outlineOffset: 2 },
            }}
          >
            <FontAwesomeIcon icon={pill.icon} style={{ width: 14, height: 14 }} />
            {/* The visible label so the pill's meaning is obvious (Love / Wow /
                Didn't like), not just an icon a player has to guess at. Reads
                white on a selected (filled) pill, the pill's own color otherwise. */}
            <Typography
              component="span"
              sx={{
                fontFamily: '"Fredoka", sans-serif',
                fontWeight: 600,
                fontSize: 12.5,
                lineHeight: 1,
                whiteSpace: 'nowrap',
                color: isSelected ? theme.palette.common.white : pillColor,
              }}
            >
              {pill.label}
            </Typography>
            <Typography
              component="span"
              sx={{
                fontFamily: '"Fredoka", sans-serif',
                fontWeight: 700,
                fontSize: 13,
                lineHeight: 1,
                // The count reads white on a selected (filled) pill, primary text
                // otherwise, so it stays legible against either background.
                color: isSelected ? theme.palette.common.white : 'text.primary',
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
