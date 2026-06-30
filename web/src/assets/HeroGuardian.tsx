// ----------------------------------------------------------------------------
//  HeroGuardian - the full-size, posed stone-guardian mascot.
//
//  This is the DETAILED hero build of QuibbleStone's mascot, used on the Home
//  and Waiting screens (docs/design/screens/01-home.png,
//  docs/design/screens/05-waiting.png). It is recreated (not copied) from the
//  inline-SVG reference in docs/design/Home.dc.html as a React/JSX component
//  per design-system/01 AC-08.
//
//  NOTE - the small 6-variant avatar (`<Guardian variant size />`, used in
//  player tiles / roster grids) is a SEPARATE component owned by
//  design-system/02 (web/src/components/Guardian.tsx). This component is only
//  the larger, single, illustrated pose: aura glow, carved-stone body, arms,
//  feet, moss accents, a glowing forehead rune, and a carved smile.
//
//  Like the small Guardian, this is illustrative content rather than theme
//  chrome - its fill/stroke colors are HARDCODED ON PURPOSE (see
//  design-system/02 guardrail, which applies equally here: the mascot's
//  stone/moss/glow palette is part of the character art, not the app's UI
//  chrome, so it does not route through theme.ts).
//
//  Rendered as inline SVG (viewBox 0 0 158 150) so it scales losslessly to
//  whatever size the caller renders it at - no raster image, no pixelation.
//  A gentle idle bob/glow animation is included via CSS keyframes (transform-
//  only, per docs/design/README.md Implementation Gotchas - never drive
//  entrance/idle motion with opacity keyframes that can leave a re-rendered
//  element stuck invisible). Pass `animate={false}` to render statically
//  (e.g. if a consuming screen wants to own its own motion or honor
//  reduced-motion - that pass is out of scope for this story).
// ----------------------------------------------------------------------------

import { useId } from 'react';
import { Box, keyframes } from '@mui/material';

export interface HeroGuardianProps {
  /** Rendered width in CSS px; height follows the SVG's intrinsic 158:150 aspect ratio. Default 158 (matches the design reference). */
  width?: number;
  /** Play the idle bob (transform-only). Default true. */
  animate?: boolean;
}

const bob = keyframes`
  0%, 100% { transform: translateY(0) rotate(0deg); }
  50% { transform: translateY(-4px) rotate(-1.2deg); }
`;

const glow = keyframes`
  0%, 100% { opacity: .55; transform: scale(1); }
  50% { opacity: 1; transform: scale(1.06); }
`;

const eyePulse = keyframes`
  0%, 100% { opacity: .85; }
  50% { opacity: 1; }
`;

export function HeroGuardian({ width = 158, animate = true }: HeroGuardianProps) {
  // SVG <defs> ids must be unique per instance so multiple HeroGuardians on
  // one page (unlikely, but cheap to guard) don't collide.
  const reactId = useId();
  const auraId = `qsAura-${reactId}`;
  const bodyId = `qsBody-${reactId}`;
  const armId = `qsArm-${reactId}`;
  const eyeGlowId = `qsEyeGlow-${reactId}`;

  const height = (width * 150) / 158;

  return (
    <Box
      sx={{
        width,
        height,
        animation: animate ? `${bob} 5s ease-in-out infinite` : 'none',
      }}
    >
      <svg width={width} height={height} viewBox="0 0 158 150" role="img" aria-label="The QuibbleStone guardian">
        <defs>
          <radialGradient id={auraId} cx="50%" cy="48%" r="55%">
            <stop offset="0%" stopColor="#FFB22E" stopOpacity=".55" />
            <stop offset="45%" stopColor="#2FB8A0" stopOpacity=".22" />
            <stop offset="100%" stopColor="#2FB8A0" stopOpacity="0" />
          </radialGradient>
          <linearGradient id={bodyId} x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor="#F1E6CC" />
            <stop offset="55%" stopColor="#DECBA0" />
            <stop offset="100%" stopColor="#C7B083" />
          </linearGradient>
          <linearGradient id={armId} x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor="#E7D6B0" />
            <stop offset="100%" stopColor="#CBB488" />
          </linearGradient>
          <filter id={eyeGlowId} x="-60%" y="-60%" width="220%" height="220%">
            <feGaussianBlur stdDeviation="2.4" result="b" />
            <feMerge>
              <feMergeNode in="b" />
              <feMergeNode in="SourceGraphic" />
            </feMerge>
          </filter>
        </defs>

        {/* aura */}
        <ellipse
          cx={79}
          cy={78}
          rx={70}
          ry={64}
          fill={`url(#${auraId})`}
          style={{
            animation: animate ? `${glow} 4.5s ease-in-out infinite` : 'none',
            transformOrigin: '79px 78px',
          }}
        />

        {/* feet */}
        <ellipse cx={60} cy={135} rx={17} ry={10} fill="#BFA87C" />
        <ellipse cx={98} cy={135} rx={17} ry={10} fill="#BFA87C" />

        {/* arms */}
        <rect x={22} y={86} width={20} height={40} rx={10} fill={`url(#${armId})`} />
        <rect x={116} y={86} width={20} height={40} rx={10} fill={`url(#${armId})`} />

        {/* body */}
        <rect x={33} y={40} width={92} height={92} rx={34} fill={`url(#${bodyId})`} stroke="#B49B6E" strokeWidth={2} />

        {/* carved cracks */}
        <path d="M52 48 l7 12 l-5 9" stroke="#B49B6E" strokeWidth={2} fill="none" strokeLinecap="round" opacity={0.55} />
        <path d="M112 70 l-9 7" stroke="#B49B6E" strokeWidth={2} fill="none" strokeLinecap="round" opacity={0.5} />

        {/* moss accents */}
        <ellipse cx={42} cy={52} rx={9} ry={5} fill="#2FB8A0" opacity={0.55} />
        <ellipse cx={118} cy={120} rx={8} ry={4.5} fill="#2FB8A0" opacity={0.5} />

        {/* forehead rune */}
        <path d="M79 52 l6 6 l-6 6 l-6 -6 z" fill="#FFB22E" opacity={0.9} filter={`url(#${eyeGlowId})`} />

        {/* brows */}
        <rect x={50} y={74} width={16} height={4.5} rx={2.2} fill="#9C815A" />
        <rect x={92} y={74} width={16} height={4.5} rx={2.2} fill="#9C815A" />

        {/* eyes */}
        <g
          filter={`url(#${eyeGlowId})`}
          style={{ animation: animate ? `${eyePulse} 3.5s ease-in-out infinite` : 'none' }}
        >
          <rect x={50} y={80} width={16} height={20} rx={8} fill="#2FB8A0" />
          <rect x={92} y={80} width={16} height={20} rx={8} fill="#2FB8A0" />
        </g>
        <circle cx={55} cy={86} r={3.2} fill="#EAFBF6" />
        <circle cx={97} cy={86} r={3.2} fill="#EAFBF6" />

        {/* cheeks */}
        <circle cx={46} cy={106} r={4} fill="#FF6B57" opacity={0.5} />
        <circle cx={112} cy={106} r={4} fill="#FF6B57" opacity={0.5} />

        {/* smile */}
        <path d="M64 110 q15 13 30 0" stroke="#7C6442" strokeWidth={3.4} fill="none" strokeLinecap="round" />
      </svg>
    </Box>
  );
}
