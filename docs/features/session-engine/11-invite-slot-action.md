# Story: Wire the "+ invite" roster slot to the share action

**Feature:** Session & Room Engine  ·  **Status:** Not Started  <!-- Not Started | In Progress | Complete | Blocked | Dropped -->  ·  **Issue:** TBD

## Context
The fit-to-viewport de-clutter (`design-system/05`) replaced the Lobby's
per-empty-seat placeholder grid with a single trailing dashed "+ invite" slot
(`InviteSlot()` in `web/src/pages/Lobby.tsx`) - but that slot is purely
decorative: it renders the dashed circle and "invite" caption with no
`onClick`, so tapping it does nothing. Meanwhile the Lobby already has a fully
working invite mechanism in its stone-tablet `ShareWidget` (session-engine/04,
upgraded by session-engine/06): an outlined "Copy" button that copies a
tappable `/join/:code` deep link, and a filled-purple "Share" button that
invokes the Web Share API when available. This story makes the "+ invite" slot
DO the one thing its label promises - trigger that same invite action - so the
roster row is not a dead tap target sitting right next to a working one. See
[feature.md](./feature.md),
[04-copy-share-room-code.md](./04-copy-share-room-code.md) and
[06-share-room-link.md](./06-share-room-link.md) (the share/copy plumbing this
story reuses), and
[../design-system/05-fit-to-viewport-declutter.md](../design-system/05-fit-to-viewport-declutter.md)
(which introduced the slot as a static placeholder).

## Acceptance Criteria
- [ ] AC-01: Given I am on the Lobby roster row, when I tap the "+ invite"
      slot, then the SAME invite action the `ShareWidget`'s "Share" button
      performs runs: the Web Share API opens with the room's `/join/:code`
      deep link (built by `joinLink.ts`'s `buildJoinLink`, the same payload as
      story 06's Share button) when `navigator.share` is available.
- [ ] AC-02: Given the Web Share API is unavailable on the current browser
      (story 04 AC-04's fallback case), when I tap the "+ invite" slot, then
      the deep link is copied to the clipboard instead (the same payload the
      "Copy" button produces) and the slot briefly reflects a "Copied!"-style
      confirmation, mirroring the widget's own confirmation timer - no dead
      tap and no JS error either way.
- [ ] AC-03: Given the invite action is triggered from either the "+ invite"
      slot or the `ShareWidget`'s own Copy/Share buttons, then the underlying
      copy/share logic is NOT duplicated - both call the same shared
      function(s), so there is exactly one canonical invite action in the
      codebase (the current per-widget closures move to a shared helper the
      slot and the widget both call).
- [ ] AC-04: Given any player in the room (not only the host), when they tap
      the "+ invite" slot, then the same invite action runs for them too - the
      slot is not host-gated, matching the fact that the room code itself is
      already visible to every player on this screen (there is nothing
      host-only being exposed).
- [ ] AC-05: Given the "+ invite" slot now performs an action, then it exposes
      a proper `button` semantics (a real `button`/`role="button"` element,
      `aria-label` such as "Invite someone to this room", and a visible
      focus/pressed state) rather than a bare decorative `div`, consistent
      with the rest of the Lobby's tappable rows (e.g. the "Game settings"
      row).
- [ ] AC-06 (child safety / privacy, README section 6): the invite action
      carries only the anonymous room code / join link - no nickname, no
      free text, no PII - identical to story 06 AC-07's link contents, so
      there is nothing new here for the safety filter to check; this story
      adds no new free-text surface.

## Out of Scope
- Any change to WHAT is shared (the deep link payload, the "Copied!" wording,
  the Web Share message text) - all of that is exactly what stories 04/06
  already ship; this story only wires a second trigger to the existing
  action.
- QR code generation, a dedicated "invite" screen/modal, or a count of how
  many invites were sent - none of that exists today and none of it is
  needed to make the slot do something.
- Host-only gating of the slot - AC-04 deliberately keeps it available to
  every player, matching the already-visible room code; revisit only if a
  future story makes the code itself host-only.
- Any visual redesign of the slot's dashed-circle look (`design-system/05`
  already set that appearance) - this story only adds behavior, not a new
  look.

## Technical Notes
- **Web only** (`web/src/pages/Lobby.tsx`), no API/hub change - the room code
  is already client state, and both Copy and Share are pure client-side
  (session-engine/04's Technical Notes).
- Today `ShareWidget`'s `handleCopy` and `handleShare` (and the `joinLink`,
  `canShare`, and `copied` state they close over) are private to that
  component. AC-03 means lifting the copy/share behavior into a shared
  helper - either a small hook (e.g. `useInviteAction(code)` returning
  `{ share, copy, copied, canShare }`) or a plain exported function pair - that
  both `ShareWidget` and `InviteSlot` call, rather than `InviteSlot` growing
  its own second implementation of `navigator.clipboard.writeText` /
  `navigator.share`. Keep the existing feature-detection posture from story 04
  (`typeof navigator.share === 'function'`; do NOT gate on
  `navigator.canShare()` for a plain text/URL payload).
- Behavior split (AC-01/AC-02): when Share is available, tapping the slot
  should invoke Share directly (the highest-value single tap, matching what a
  player expects a big "+ invite" affordance to do); when Share is
  unavailable, fall back to Copy plus a lightweight local confirmation on the
  slot itself (reuse the widget's `COPIED_CONFIRMATION_MS` constant so the
  timing matches). This mirrors the widget's own Share-first, Copy-fallback
  posture rather than inventing a third UX pattern.
- `InviteSlot` currently renders as a bare `Stack`/`Box` with no interactive
  element (`web/src/pages/Lobby.tsx`, the `InviteSlot()` function). The
  smallest correct change is: wrap it as a `Box component="button" type="button"`
  (matching the pattern already used by the "Game settings" row a few lines
  below in the same file) with an `onClick` that calls the shared invite
  action, plus `aria-label` (AC-05). Preserve the existing dashed-circle
  visual (`borderPulse` keyframe, theme tokens) unchanged.
- No new FontAwesome icon needed (the slot already uses `faPlus`); no new
  theme tokens needed (reuse `sandstone`/`stoneEdge`/`primary` as today).
- No em dashes; hyphens/colons/parentheses only, matching house style.

## Tests
| AC | Test |
|---|---|
| AC-01 | manual: on a device/browser with Web Share support, tap "+ invite"; confirm the native share sheet opens with the same `/join/<code>` link the widget's own Share button produces |
| AC-02 | manual (desktop browser without Web Share): tap "+ invite"; confirm the link lands on the clipboard (paste to verify) and a brief confirmation shows, with no console error |
| AC-03 | code review: `InviteSlot` and `ShareWidget` both call the same exported helper/hook - no second `navigator.clipboard`/`navigator.share` call site |
| AC-04 | manual: as a non-host player in the room, tap "+ invite"; confirm the same behavior as AC-01/AC-02 (no host-only gate) |
| AC-05 | manual + code review: the slot is a real `button` (keyboard-focusable, visible focus ring) with an `aria-label`, not a non-interactive `div` |
| AC-06 | code review: the shared invite helper's payload is unchanged from story 06 (room code / join link only, no PII) |

## Dependencies
- session-engine/04-copy-share-room-code (the Copy/Share plumbing this story
  reuses)
- session-engine/06-share-room-link (the deep-link payload the slot shares)
- design-system/05-fit-to-viewport-declutter (introduced the decorative
  "+ invite" slot this story makes interactive)
