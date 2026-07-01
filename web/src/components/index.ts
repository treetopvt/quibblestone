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
