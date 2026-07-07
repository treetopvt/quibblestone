# Story: Live/test Stripe mode - operator toggle UI

**Feature:** Billing & Entitlements  ·  **Status:** Complete (behind story 06's interim operator gate)  ·  **Issue:** TBD

## Context
Story 06 builds the server-side mode-aware config, the persisted active-mode flag, and a guarded
toggle endpoint. This story is the thin operator-facing surface on top of it: a screen (or a
section of one) where an operator can see which Stripe mode is currently active and switch it,
with the confirmation and visibility the footgun deserves - this is a single public environment
(`quibblestone.com`), and flipping the wrong way silently either declines a real supporter's card
(stuck in test) or risks a real charge during a demo (stuck in live). See
[feature.md](./feature.md) and [story 06](./06-live-test-mode-toggle.md).

## Acceptance Criteria
- [x] AC-01 (current mode always visible): Given the operator surface, when an operator views the
      billing/mode screen, then the currently ACTIVE mode (Live or Test) is displayed prominently
      and unambiguously - never inferred, never defaulted silently in the UI if the read fails
      (a failed read shows an explicit error state, not a blank or a guess).
- [x] AC-02 (confirmation-gated switch): Given an operator initiates a mode change, when they
      request the switch, then a confirmation step names both the current and target mode
      explicitly (e.g. "Switch from Test to Live?") before the change is submitted - there is no
      single-click, no-confirmation path to flip the mode.
- [x] AC-03 (switching TO live is the more deliberate action): Given the target mode is Live, when
      the confirmation is shown, then it carries a stronger warning than switching to Test (e.g.
      explicit copy that real cards will be charged) - consistent with story 06 AC-05's safe
      default, the UI treats "go live" as the direction that deserves more friction, not equal
      friction both ways.
- [x] AC-04 (last-changed visibility): Given the mode has been changed before, then the screen
      shows when it last changed (story 06 AC-07's timestamp) - enough for an operator to notice
      "oh, this has been in Live for three weeks" or "someone left this in Test."
- [x] AC-05 (reachable only from the operator surface, never the kid PWA): Given this screen, then
      it is reachable only from wherever the interim or real operator gate (story 06's Technical
      Notes) is enforced - it is never linked from, bundled with, or reachable through Join,
      Lobby, FillBlank, Reveal, Home, or any player-facing route.
- [x] AC-06 (theme-consistent, no new design system): Given the screen's visuals, then it is built
      from `web/src/theme.ts` tokens and FontAwesome icons like every other surface in the app -
      no hardcoded colors, no bespoke admin design language, per CLAUDE.md section 4.
- [x] AC-07 (child-safety-adjacent: no PII, no player data on this screen): Given this screen, then
      it displays only the mode value and its last-changed timestamp - no player, room, session, or
      purchaser data of any kind appears here or is fetched to render it.

## Out of Scope
- Building the toggle endpoint or the persisted flag - that is story 06; this story only calls it.
- Real operator authentication itself - `sysadmin-console/01` (#135); this story renders behind
  whichever gate is live at build time (story 06's interim secret, or the real operator session
  once #135 ships) and does not implement either.
- A history/audit list of every past mode change (only "last changed" per AC-04) - parked in
  feature.md; toy, not a system of record.
- Any indicator surfaced to PLAYERS (e.g. a "test mode" banner on the tip jar or paywall) - this
  screen is operator-only; whether a player-facing test-mode indicator is ever warranted is a
  separate product decision, not assumed here (see Technical Notes for the one narrow exception
  already covered elsewhere: purchase surfaces already show Stripe's own "test mode" watermark
  automatically when a test-mode Checkout Session is used - nothing extra to build).
- A standalone admin app/route tree if `sysadmin-console/01` has not landed yet - see Technical
  Notes for where this screen lives in the interim.

## Technical Notes
- **Dependency reality - where this screen lives.** `sysadmin-console/01` (#135) defines the
  separate, auth-gated back-office bundle/route tree (`web/admin/` or similar) this screen belongs
  in long-term. If #135 has landed by the time this story is built, add this screen there,
  behind its `[Authorize(Policy = "Operator")]`-guarded API calls and its own route. If #135 has
  NOT landed (the likely case given both are currently unbuilt), build this screen as a small,
  clearly-marked standalone route reachable only via a direct, unguessable-enough URL plus story
  06's interim server-side secret prompt (entered once per browser session, not persisted client-
  side beyond the session) - explicitly temporary, and noted as such in a header comment, so
  moving it into the real back office later is a relocation, not a rewrite. Do not invest in
  polish disproportionate to its temporary status.
  *(Update 2026-07-07: the "both are currently unbuilt" reality above is stale - sysadmin-console/01
  shipped via PR #158 and accounts-identity/02 via PR #147, so the real operator console and
  Operator scheme now exist. This screen still sits at its interim `/admin/billing-mode` route in
  the kid bundle behind the X-Operator-Secret gate; the remaining follow-up is the relocation the
  note above planned for - moving it into the operator console behind the real Operator scheme.)*
- Calls story 06's `GET /api/admin/stripe-mode` (render AC-01/AC-04) and
  `POST /api/admin/stripe-mode` (AC-02's confirmed switch). No new SignalR surface - this is
  request/response like the rest of the billing REST surface (`BillingController`), not a hub
  concern.
- Reuse `web/src/theme.ts` tokens, the shared Button/AppBar family, and FontAwesome icons per
  CLAUDE.md section 4 - a confirmation dialog (MUI `Dialog`) is the natural fit for AC-02/AC-03,
  not a bespoke modal.
- **Player-facing test-mode visibility already exists via Stripe itself**: Stripe's own hosted
  Checkout page shows a "TEST MODE" watermark automatically whenever a test-mode session is used,
  with zero code from this app - worth noting so nobody re-builds that signal here.

## Tests
| AC | Test |
|---|---|
| AC-01 | `manual: load the screen with each mode active - confirm the displayed mode matches story 06's persisted value; simulate a failed read - confirm an explicit error state, not a blank/guessed mode.` |
| AC-02 | `manual: attempt to change the mode - confirm a confirmation dialog naming both current and target mode appears before any change is submitted.` |
| AC-03 | `manual: compare the confirmation copy switching to Live vs. to Test - confirm the Live-bound confirmation carries the stronger warning.` |
| AC-04 | `manual: after a flip, reload the screen - confirm the last-changed timestamp updates.` |
| AC-05 | `manual: bundle/route audit - confirm no import edge or nav/deep-link path from the kid PWA (web/src/pages, Home, Join, Lobby, FillBlank, Reveal) reaches this screen.` |
| AC-06 | `manual: visual audit - confirm no hardcoded color/spacing; all styling resolves through web/src/theme.ts.` |
| AC-07 | `manual: code review - confirm no player/room/session/purchaser field is queried or rendered on this screen.` |

## Dependencies
- `billing-entitlements/06` - the toggle endpoint and persisted mode this screen calls; hard
  prerequisite (this story has nothing to render or call without it).
- `sysadmin-console/01` (#135) - the real back-office surface this screen ultimately belongs in;
  not required to start (see Technical Notes for the interim placement), but the move into the
  real back office should happen promptly once #135 ships.
