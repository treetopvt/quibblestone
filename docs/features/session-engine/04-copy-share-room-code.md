# Story: Copy and share the room code from the Lobby

**Feature:** Session & Room Engine  ·  **Status:** In Review

## Context
Players joining remotely need to receive the room code without the host having
to read it aloud. The Lobby provides two affordances: a one-tap copy (with a
"Copied!" confirmation) and the Web Share API so the host can send the code via
any installed share target (Messages, WhatsApp, etc.). This is the "different
houses" use case. See [feature.md](./feature.md) and
`docs/design/README.md` Screens - screen 3 (Lobby).

## Acceptance Criteria
- [x] AC-01: Given I am on the Lobby screen, then the room code is displayed
      prominently in the stone-tablet share widget alongside "Copy" and "Share"
      buttons.
- [x] AC-02: Given I tap "Copy", then the room code is copied to the clipboard
      and the button label changes to a teal-check "Copied!" confirmation for
      approximately 1.8 seconds, then reverts to "Copy". See
      `docs/design/README.md` Screens - screen 3 and
      `docs/design/screens/03-lobby.png`.
- [x] AC-03: Given I tap "Share", then the browser's Web Share API is invoked
      with the room code and a short human-readable message (e.g. "Join my
      QuibbleStone game! Room code: MOSS").
- [x] AC-04: Given the Web Share API is not available on the current browser
      (e.g. desktop Chrome without share support), then the "Share" button is
      hidden or falls back gracefully (e.g. the Copy affordance remains and no
      JS error is thrown).
- [x] AC-05: Given the room code is shown in the Lobby, then it displays in
      Fredoka 700 (40px in the reworked stone-tablet share widget - a touch
      larger than the original 38px mock so it reads as the panel's hero),
      purple (`#6C4BD8`), centered and letter-spaced, and reads as plain text
      (no PII, just the code). See `docs/design/README.md` Screens - screen 3.

## Out of Scope
- Sharing a link with the code pre-filled in the URL (a later enhancement).
- QR code generation.
- Deep-link / app-install flow.

## Technical Notes
- Web only (`web/src/`). No API change needed - the code is already in client
  state from session-engine/01.
- `navigator.clipboard.writeText()` for copy. For Web Share availability the
  implementation feature-detects `typeof navigator.share === 'function'` (and
  hides the Share button when absent). Note: it deliberately does NOT gate on
  `navigator.canShare()` - that predicate is meant for file/data payloads and
  can spuriously reject a valid text-only share, so a plain-text code share must
  not depend on it.
- The "Copied!" state is purely local component state (a boolean + a
  `setTimeout` to revert); no server round-trip.
- The "Copy" button is the outlined-purple secondary variant; the "Share"
  button is the filled-purple style per the design spec (note: this is a
  third button style - filled purple, not the outlined variant - see
  `docs/design/README.md` Screens screen 3: "Share (filled purple, white text,
  share-nodes icon)"). Add this variant to the MUI theme if it is not already
  present.
- See `docs/design/README.md` Interactions & Behavior - Copy/Share.

## Tests
- Web-only UI behavior (Copy flips to a teal "Copied!" for ~1.8s then reverts -
  AC-02; Share is feature-detected and hidden when Web Share is unavailable -
  AC-04; the code renders prominently - AC-01/05) is covered by the Phase 4
  browser walkthrough. Vitest is pure-logic only and there is no React
  component-render harness in Slice 1, so there is no unit test for this widget;
  Playwright coverage of Copy/Share is a candidate for a later testing pass.

## Dependencies
- session-engine/01-create-room
- session-engine/03-player-roster (Lobby screen must exist)
- design-system/01-mui-theme-and-app-shell
