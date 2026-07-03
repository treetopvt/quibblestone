# Story: Fit-to-viewport screen de-clutter

**Feature:** Design System & UI Foundation  ·  **Status:** Complete  ·  **Issue:** TBD

## Context
Every core screen was built to a `maxWidth: 430` portrait column, but several
had grown taller than a real phone viewport: the Landing screen stacked a
kicker pill, a hero, a tagline paragraph, and four separate nav links; the
Waiting room pre-drew a full 6-seat roster grid AND four host-only round
controls inline; Gameplay carried a large "Blind mode" banner plus a redundant
counter; and the Reveal screen's celebration header and stone-tablet card
pushed the reaction row and CTAs past the fold. On a real device this meant
page scroll on screens that are meant to read as one glance-and-tap surface -
exactly the "chunky, high-contrast, big tap targets" brief (README section 10)
undercut by the player having to scroll to find the button they need.

An approved design handoff ("Tightened Screens", 2026-07) set one goal for
every core screen: **fit ONE phone viewport (~390x844) with NO page scroll**,
plus a set of per-screen clarity fixes (fewer redundant labels, controls
collapsed behind on-demand affordances instead of always inline). This story
records that pass, which has already shipped. It lives in `design-system`
(not `the-reveal` or `session-engine`) because the fix is one reusable layout
recipe applied across screens owned by several features - the same
"fixed-height flex column, one internal scroller" pattern this feature already
established the portrait column contract for (story 01). See
[feature.md](./feature.md).

The reaction-row narrowing (four reactions to three, plus the one-per-user
select/move/toggle rule) landed in the SAME de-clutter pass as part of
tightening the Reveal screen, but it is a distinct mechanic change with its
own acceptance criteria - it is recorded in
[`../reveal-delight/01-reaction-row.md`](../reveal-delight/01-reaction-row.md)
(Revised 2026-07-03), not here. This story is the layout/de-clutter change
only.

## Acceptance Criteria
- [x] AC-01: Given any of the four core screens (Landing, Waiting room,
      Gameplay, the Reveal) at a ~390x844 viewport, then the screen does NOT
      page-scroll - each screen's root is a fixed-height flex column
      (`height: 100dvh; display: flex; flexDirection: column; overflow:
      hidden`), with an internal-scroll region used only where content is
      genuinely long (the Reveal's story card; the settings sheet's body).
- [x] AC-02: Given the Landing screen (`Home.tsx`), then the "Family Word
      Quest" kicker pill and the duplicate tagline paragraph are removed (the
      stone-tablet hero, enlarged, is the sole product pitch) with one
      supporting tagline line; "Play solo right now" is a full-width pill
      distinct from the create/join CTAs; and Favorites / Our tales / Account
      collapse into a single bottom utility icon bar of five columns alongside
      two new, visually-present but DISABLED entries ("Get more", gold-tinted;
      "Support", coral-tinted) that render inert (no `onClick`, reduced
      opacity, `aria-disabled`) because no destination exists yet for either.
- [x] AC-03: Given the Waiting room (`Lobby.tsx`), then the roster renders as
      ONE horizontal row of present-player avatars plus a single dashed
      "+ invite" slot (replacing the pre-drawn 6-seat grid), and the host-only
      round controls (family-safe toggle, story-length choice, mode picker,
      "play a favorite" picker) collapse behind a tappable "Game settings"
      summary row that opens a slide-up bottom sheet
      (`GameSettingsSheet.tsx`, MUI `Drawer`); the "share this code" helper
      line is dropped as redundant with the existing share action.
- [x] AC-04: Given the Gameplay screen (`FillBlank.tsx`), then the tale
      subject renders as a compact, truncating "tale-title" pill (book icon)
      instead of a larger subject block; the "Blind mode - no peeking" banner
      is replaced by a small "Blind" chip inside the progress row (still
      suppressed when the active mode supplies see-context); and the
      redundant "X to go" counter is removed (the progress row already
      conveys this).
- [x] AC-05: Given the Reveal screen (`Reveal.tsx`), then the stone-tablet
      story card is the screen's single internal scroller (with a bottom
      fade cue signaling more content below) so the page itself never
      scrolls; the Favorite star toggle moves to the app-bar top-right
      (visually distinct from the reactions strip below); the celebratory
      header trims to one line plus a green "You filled N words together"
      subline; and a small "WHAT DID YOU THINK?" label introduces the
      reactions strip.
- [x] AC-06: Given every prop contract on the four touched screens, then this
      story changes NO public prop shape: `HomeProps`, `LobbyProps` (including
      `onStart(familySafe, lengthPref, modeId)`), `FillBlankProps`, and
      `RevealProps` are unchanged - this is a presentation/layout pass, not a
      behavior or data-contract change. All existing SignalR roster/toast/
      reconnect behavior on Lobby is preserved untouched.

## Out of Scope
- The reaction-row narrowing (four reactions to three) and the
  one-reaction-per-user select/move/toggle rule - that is a mechanic change
  with its own ACs, recorded in `reveal-delight/01-reaction-row.md` (Revised
  2026-07-03), even though it shipped in the same pass.
- Any new destination for the Landing screen's "Get more" or "Support" chips -
  they are inert placeholders here; wiring them is `billing-entitlements` /
  a future tip-jar story.
- Orientation/landscape handling - that is the separate, already-specified
  `design-system/03-orientation-and-landscape-readability`; this story does
  not touch its landscape media queries.
- A redesign of any screen NOT named above (HostSetup, Join, Waiting-for-
  others, Round Complete, Solo, Recap) - those were left as they were; only
  the four screens listed were in the approved handoff's scope.
- Changing the underlying round-setup/round-start/reveal DATA flow - every
  behavior change here is chrome (where a control lives, how it is labeled),
  never what it does or what it sends over the wire.

## Technical Notes
- **The reusable recipe:** every touched screen's root becomes a fixed-height
  flex column - `height: '100dvh'`, `display: 'flex'`, `flexDirection:
  'column'`, `overflow: 'hidden'` - with vertical rhythm distributed via flex
  (`justifyContent: 'space-between'`, `mt: 'auto'` on trailing blocks) instead
  of a scrolling `minHeight: 100vh` stack. Exactly ONE region inside that
  column is allowed to scroll internally (`overflowY: 'auto'`, `minHeight: 0`
  on its flex ancestor) when its content is genuinely unbounded - the Reveal's
  story card, and the new settings sheet's body. This is now the standard
  pattern for any future full-screen page in this codebase; reach for it
  before reaching for a page-level scroll.
- **Landing (`web/src/pages/Home.tsx`):** `HomeProps` unchanged. New internal
  `UtilityBarItem` renders the five-column bottom bar; the two disabled
  entries render via `component={disabled ? 'div' : 'button'}` +
  `aria-disabled` rather than being omitted, so the bar visually communicates
  "more is coming" without being interactive.
- **Waiting room (`web/src/pages/Lobby.tsx` + new
  `web/src/components/GameSettingsSheet.tsx`):** `GameSettingsSheet` is a pure
  layout/chrome wrapper over MUI's `<Drawer anchor="bottom">` - it owns none
  of the settings state (family-safe, length preference, mode, favorite-picker
  visibility all stay on `Lobby`) and takes the existing control components as
  `children`, so no settings component itself changed. `LobbyProps` and the
  `onStart(familySafe, lengthPref, modeId)` signature are unchanged.
- **Gameplay (`web/src/pages/FillBlank.tsx`):** `FillBlankProps` and the
  shared reuse contract (the `seeContext` slot, the submit affordance) are
  unchanged; the tale-title pill and the "Blind" chip are new presentational
  elements inside the existing progress row region.
- **The Reveal (`web/src/pages/Reveal.tsx`):** `RevealProps` unchanged. The
  fixed-height flex root plus the story card's `overflowY: auto` + a
  `mask-image` bottom fade is the same recipe as above; the Favorite star
  moved into the app-bar's right action slot (a slot the shared `AppBar`
  contract, `design-system/01`, already supports) rather than a bespoke
  floating button.
- **Cross-reference (reaction narrowing, Task/story coordination):** the
  three-reaction set and the FontAwesome `faFaceSurprise` icon it needed
  landed in this same pass, alongside the layout-only icons this story
  itself needed - all registered together in `web/src/fontawesome.ts` under
  the "Screen de-clutter / fit-to-viewport redesign" comment block: `faBookOpen`
  (Landing "Our tales" chip + the Gameplay tale-title pill), `faGift` (Landing
  "Get more"), `faMugSaucer` (Landing "Support"), `faSliders` (Lobby's
  collapsed "Game settings" row), `faChevronRight` (that row's chevron), and
  `faFaceSurprise` (the Reveal's "Wow" reaction pill - see
  `reveal-delight/01-reaction-row.md`). The reaction mechanic itself is
  out of scope here (see Out of Scope); only the icon registration is shared.
- No hardcoded hex/raw-px color - every new element pulls from theme tokens
  (`theme.palette.gold`, `.coral`, `.primary`, `.stoneEdge`, `card.main`), per
  CLAUDE.md section 4. FontAwesome-only icons. No em dashes in any new prose.

## Tests
| AC | Test |
|---|---|
| AC-01 | manual: each of the four screens rendered at 390x844 shows no page scrollbar; `npm run test:e2e` smoke still loads and connects |
| AC-02 | manual: Landing shows the enlarged hero, one tagline line, the full-width "Play solo" pill, and a 5-column utility bar with 2 visibly-disabled entries |
| AC-03 | manual: Lobby shows a single roster row + "+ invite" slot and a "Game settings" row that opens a bottom sheet containing all four round-setup controls |
| AC-04 | manual: Gameplay shows the tale-title pill, a small "Blind" chip in the progress row (Classic blind), and no "X to go" counter |
| AC-05 | manual: Reveal's story card scrolls internally with a bottom fade; the Favorite star sits in the app-bar; header is one line + subline; reactions strip has the "WHAT DID YOU THINK?" label |
| AC-06 | `npm run test:unit` (298 unit tests green) + `dotnet test` (365 API tests green) + code review: no prop-shape diffs on `HomeProps`/`LobbyProps`/`FillBlankProps`/`RevealProps` |

## Dependencies
- design-system/01-mui-theme-and-app-shell (theme tokens, the shared AppBar
  action-slot contract this story's Favorite-star move relies on)
- the-reveal/01-text-reveal (the Reveal screen this restructures)
- session-engine/03-player-roster (the Lobby roster row this restructures)
- game-modes/02 (the FillBlank screen this restructures)
- reveal-delight/01-reaction-row (coordinated in the same pass; see that
  story's Revised 2026-07-03 note for the reaction-mechanic change itself)
