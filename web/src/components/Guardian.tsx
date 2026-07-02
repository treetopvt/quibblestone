// ----------------------------------------------------------------------------
//  Guardian - the small stone-guardian avatar, in 6 selectable variants.
//
//  A single reusable inline-SVG component (viewBox 0 0 56 56) used wherever a
//  player needs a face: the Join avatar grid, Lobby player tiles, the Waiting
//  progress row, and the Round Complete crew recap. Every variant shares the
//  same carved-sandstone body (head, eyes, smile) and adds one distinguishing
//  feature on top, per docs/design/README.md "Shared Component: Guardian" and
//  docs/design/Guardian.dc.html (the design reference this was recreated from
//  using React SVG idioms - no dangerouslySetInnerHTML, no inline HTML).
//
//  Consistency rule (AC-04): callers pass the `variant` the player actually
//  chose at join time - this component does not hard-code or guess canonical
//  names (Pip/Maple/Bramble/Wren/Flint/Juniper); that mapping lives wherever
//  player records are kept.
//
//  Deliberately hardcoded SVG colors: the Guardian is illustrative content,
//  not theme chrome, so this is the one component in the tree that does NOT
//  pull fill/stroke values from web/src/theme.ts. No animation lives here -
//  the screens that place a Guardian own any idle/reaction motion, which
//  keeps this component simple and composable.
// ----------------------------------------------------------------------------

export type GuardianVariant = 'purple' | 'gold' | 'coral' | 'teal' | 'sand' | 'plum';

export interface GuardianProps {
  /** Selects the eye color and the one distinguishing head feature. */
  variant: GuardianVariant;
  /** Rendered width/height. Numeric values are treated as px. Defaults to 56. */
  size?: number | string;
  /**
   * reveal-delight/03 (AC-04): when true, draw a small gold crown overlay ON TOP
   * of whichever variant this Guardian is - the temporary "Golden Guardian"
   * badge worn by the contributor of the funniest word for the NEXT round only.
   * This is a LAYERED overlay, NOT a new variant: the six variants are the
   * player's chosen identity; the crown is a transient state over that identity
   * (so a "gold" Guardian keeps its own head feature and wears the crown above
   * it). The lifecycle (which round it shows for) is decided by the caller /
   * server round state - this component only draws it when told to. Defaults to
   * false, so every existing call site renders exactly as before.
   */
  crowned?: boolean;
}

/** Shared carved-stone colors, common to every variant (AC-02). */
const HEAD_FILL = '#E0CDA0';
const HEAD_STROKE = '#B49B6E';
const SMILE_STROKE = '#7C6442';

// reveal-delight/03 (AC-04): the awarded-crown overlay colors. Like the rest of
// this component these are deliberately HARDCODED illustrative SVG values (the
// Guardian is the one component that does not pull from the theme) - the gold
// matches the theme's gold CTA token value (tokens.goldMain, #FFB22E) so the
// crown reads as the same "gold" as the Reveal winner ring, without importing
// the theme into this pure SVG.
const CROWN_FILL = '#FFB22E';
const CROWN_STROKE = '#B07908';
const CROWN_JEWEL = '#FFF3D6';

/** Eye color per variant (also reused by some distinguishing features). */
const EYE_COLOR: Record<GuardianVariant, string> = {
  purple: '#6C4BD8',
  gold: '#E89A12',
  coral: '#FF6B57',
  teal: '#2FB8A0',
  sand: '#7C6442',
  plum: '#9B7BE0',
};

/** The common sandstone head + eyes + smile, shared by every variant. */
function GuardianBody({ variant }: { variant: GuardianVariant }) {
  const eyeColor = EYE_COLOR[variant];

  // The "sand" variant has stone ears that sit outside a narrower head, so
  // its head/eye geometry is offset slightly from the other five variants -
  // this matches the reference geometry in Guardian.dc.html exactly.
  if (variant === 'sand') {
    return (
      <>
        <rect x="12" y="12" width="32" height="36" rx="14" fill={HEAD_FILL} stroke={HEAD_STROKE} strokeWidth={1.6} />
        <rect x="19" y="26" width="7" height="11" rx="3.5" fill={eyeColor} />
        <rect x="30" y="26" width="7" height="11" rx="3.5" fill={eyeColor} />
        <path d="M23 41 q5 6 10 0" stroke={SMILE_STROKE} strokeWidth={2.6} fill="none" strokeLinecap="round" />
      </>
    );
  }

  return (
    <>
      <rect x="10" y="12" width="36" height="36" rx="14" fill={HEAD_FILL} stroke={HEAD_STROKE} strokeWidth={1.6} />
      <rect x="18" y="26" width="7" height="11" rx="3.5" fill={eyeColor} />
      <rect x="31" y="26" width="7" height="11" rx="3.5" fill={eyeColor} />
      <path d="M22 41 q6 6 12 0" stroke={SMILE_STROKE} strokeWidth={2.6} fill="none" strokeLinecap="round" />
    </>
  );
}

/** The one distinguishing feature per variant (AC-01), layered above the body. */
function GuardianFeature({ variant }: { variant: GuardianVariant }) {
  switch (variant) {
    case 'purple':
      // Small square block on top of the head.
      return <rect x="23" y="6" width="10" height="9" rx="2.5" fill="#C9B488" />;
    case 'gold':
      // Gold zig-zag crown.
      return <path d="M16 14 l4 -8 l4 6 l4 -7 l4 7 l4 -6 l4 8 z" fill="#FFB22E" />;
    case 'coral':
      // Two small horns.
      return (
        <>
          <path d="M18 14 l-2 -8 l8 6 z" fill="#FF6B57" />
          <path d="M38 14 l2 -8 l-8 6 z" fill="#FF6B57" />
        </>
      );
    case 'teal':
      // Leaf sprout.
      return (
        <>
          <path d="M28 14 q-1 -8 -8 -9 q3 7 8 9z" fill="#2FB8A0" />
          <rect x="26.5" y="7" width="3" height="7" rx="1.5" fill="#1f8a78" />
        </>
      );
    case 'sand':
      // Round stone ears on the sides.
      return (
        <>
          <circle cx="13" cy="28" r="6" fill="#D3BE92" stroke={HEAD_STROKE} strokeWidth={1.4} />
          <circle cx="43" cy="28" r="6" fill="#D3BE92" stroke={HEAD_STROKE} strokeWidth={1.4} />
        </>
      );
    case 'plum':
      // Single antenna with a glowing dot.
      return (
        <>
          <path d="M28 13 q4 -5 9 -9" stroke="#9B7BE0" strokeWidth={2.4} fill="none" strokeLinecap="round" />
          <circle cx="38" cy="4" r="3.4" fill="#9B7BE0" />
        </>
      );
  }
}

/**
 * reveal-delight/03 (AC-04): the awarded "Golden Guardian" crown, drawn as the
 * TOP-MOST layer so it sits over whichever variant feature is beneath it (a small
 * three-point crown with a base band and jewels). Deliberately floated in the top
 * band above the head so it reads as "wearing a crown" rather than replacing the
 * variant's own head feature. Purely additive - only rendered when `crowned`.
 */
function GuardianCrown() {
  return (
    <>
      {/* Crown band + three points, one path so the outline stays crisp. */}
      <path
        d="M17 12 L19 4 L24 9 L28 2 L32 9 L37 4 L39 12 Z"
        fill={CROWN_FILL}
        stroke={CROWN_STROKE}
        strokeWidth={1.4}
        strokeLinejoin="round"
      />
      {/* Base band under the points for a solid, chunky read. */}
      <rect x="17" y="11" width="22" height="3.4" rx="1.2" fill={CROWN_FILL} stroke={CROWN_STROKE} strokeWidth={1.2} />
      {/* Three small jewels on the points (pale gold highlight). */}
      <circle cx="19" cy="6" r="1.3" fill={CROWN_JEWEL} />
      <circle cx="28" cy="4" r="1.5" fill={CROWN_JEWEL} />
      <circle cx="37" cy="6" r="1.3" fill={CROWN_JEWEL} />
    </>
  );
}

export function Guardian({ variant, size = 56, crowned = false }: GuardianProps) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 56 56"
      role="img"
      aria-label={crowned ? `${variant} guardian avatar wearing the golden crown` : `${variant} guardian avatar`}
    >
      {/* Sand's ears sit behind the head in the reference; every other
          variant's feature sits in front (crown, horns, sprout, antenna). */}
      {variant === 'sand' && <GuardianFeature variant={variant} />}
      <GuardianBody variant={variant} />
      {variant !== 'sand' && <GuardianFeature variant={variant} />}
      {/* reveal-delight/03 (AC-04): the awarded crown is the TOP-MOST layer, over
          the variant feature - a temporary state, never a new variant. */}
      {crowned && <GuardianCrown />}
    </svg>
  );
}
