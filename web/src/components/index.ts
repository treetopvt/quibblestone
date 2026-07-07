// ----------------------------------------------------------------------------
//  components/index.ts - the shared-UI barrel.
//
//  One import surface for every reusable presentational piece, so a screen
//  pulls the shared look-and-feel from a single place instead of guessing file
//  paths or re-specifying components per screen (CLAUDE.md section 4 - reuse the
//  shared contracts; the design pack is one consistent system, not per-screen
//  styling). Prefer:
//
//      import { AppBar, BottomActionBar, Guardian, HeroGuardian } from '../components';
//
//  over deep per-file imports. Adding a new shared component? Create it under
//  web/src/components/ and re-export it here so it joins the shared surface.
//
//  Styling still lives in the MUI theme (web/src/theme.ts), NOT here - this
//  barrel only collects the components; their look comes from the theme. See
//  web/src/components/README.md for the reuse rules.
//
//  Note: isolatedModules is on (tsconfig), so runtime exports use `export {}`
//  and type-only exports use `export type {}` - keep that split when adding to
//  this file.
// ----------------------------------------------------------------------------

// App shell: the single AppBar recipe (centered title + 42x42 icon slots).
export { AppBar } from './AppBar';
export type { AppBarAction, AppBarProps } from './AppBar';

// Pinned-action wrapper (reserves room + fade scrim) used by FillBlank, Reveal,
// Round Complete. Pair the bar with its spacer so content never hides behind it.
export { BottomActionBar, BottomActionBarSpacer } from './BottomActionBar';
export type { BottomActionBarProps } from './BottomActionBar';

// The 6-variant stone-guardian avatar (Join, Lobby, Waiting, Round Complete).
export { Guardian } from './Guardian';
export type { GuardianVariant, GuardianProps } from './Guardian';

// The full-size hero mascot lives under web/src/assets (it is illustrative art,
// not theme chrome), but it is re-exported here so screens import all shared UI
// from one place (Home and Waiting use it).
export { HeroGuardian } from '../assets/HeroGuardian';
export type { HeroGuardianProps } from '../assets/HeroGuardian';

// The session-level family-safe toggle (child-safety/02): a controlled
// switch that gates which curated templates a player is offered. Pair with
// the pure selection rule in web/src/content/familySafe.ts; never relaxes
// the profanity filter (child-safety/01).
export { FamilySafeToggle } from './FamilySafeToggle';
export type { FamilySafeToggleProps } from './FamilySafeToggle';

// The shared identity controls (build/host-identity): the "Display name" field +
// "Choose your guardian" avatar grid, used by BOTH Join (a joiner names itself)
// and HostSetup (the host names itself before the room is minted). Controlled /
// presentational; also the one source of the shared identity constants.
export {
  PlayerIdentityFields,
  GUARDIAN_VARIANTS,
  DEFAULT_VARIANT,
  MAX_NAME_LENGTH,
  toGuardianVariant,
} from './PlayerIdentityFields';
export type { PlayerIdentityFieldsProps } from './PlayerIdentityFields';

// The session-level story-length choice (story-selection/02): a controlled
// Quick tale / Full tale segmented pair, in the same visual family as
// FamilySafeToggle. Pair with the pure length pipeline in
// web/src/content/length.ts; never touches the family-safe gate or the
// profanity filter.
export { StoryLengthChoice } from './StoryLengthChoice';
export type { StoryLengthChoiceProps } from './StoryLengthChoice';

// The quiet, per-player thumbs up/down curation vote on a story template
// (story-selection/05, issue #95): rendered at the end of a tale on BOTH
// Reveal.tsx (solo) and RoundComplete.tsx (group), visually subordinate to
// the primary "Play another round" CTA. A plain per-device REST write via
// ../telemetry/feedbackLog.ts - NOT the reveal-delight Reaction row (no
// SignalR, no room state, no aggregate shown to players).
export { TaleFeedback } from './TaleFeedback';
export type { TaleFeedbackProps } from './TaleFeedback';

// The four-pill reaction row on the Reveal (reveal-delight/01, issue #56): Laugh
// / Heart / Wow / Star pills with live counts + a floating-icon pop. Room-
// agnostic - the caller feeds `counts` (solo state or the hub's
// ReactionCountsChanged broadcast) and handles `onReact` (local bump, or the
// hub's fire-and-forget React invoke in group play). Unlike TaleFeedback this IS
// the real-time reaction surface (SignalR-backed room aggregate in group play).
export { ReactionRow } from './ReactionRow';
export type { ReactionRowProps, ReactionType, ReactionCounts } from './ReactionRow';

// The app's last-resort render-error safety net (B5, alpha-gate hardening):
// a class component (required - React error boundaries have no Hook
// equivalent) wrapping <App/> in main.tsx. Renders a minimal "Something went
// off" + reload screen instead of a permanent blank page on an uncaught
// render error.
export { ErrorBoundary } from './ErrorBoundary';
export type { ErrorBoundaryProps } from './ErrorBoundary';

// The shared star favorite/unfavorite toggle (story-selection/06, AC-01):
// rendered on Reveal (solo, via its optional `favorite` prop) and
// RoundComplete (group, unconditionally - a private per-device action any
// player may do). Pair with the pure device-local store in
// web/src/content/favorites.ts.
export { FavoriteStarButton } from './FavoriteStarButton';
export type { FavoriteStarButtonProps } from './FavoriteStarButton';

// The Lobby's host-only "Game settings" bottom sheet (screen de-clutter /
// fit-to-viewport redesign): slide-up chrome over the existing FamilySafeToggle
// / StoryLengthChoice / ModePicker / favorites-picker controls, so the Waiting
// room's main screen stays a fixed, non-scrolling layout.
export { GameSettingsSheet } from './GameSettingsSheet';
export type { GameSettingsSheetProps } from './GameSettingsSheet';
