# Story: See a live player roster

**Feature:** Session & Room Engine  ·  **Status:** Complete

## Context
Everyone in a room needs to see who else is there - the social anchor of group
play and the host's signal for when to start. This is minimum-viable presence.
See [feature.md](./feature.md).

## Acceptance Criteria
- [x] AC-01: Given I am in a room, then I see a 3-column grid of player tiles;
      each tile shows the player's Guardian avatar, display name, and a role chip
      (gold "HOST" chip + pulsing gold ring + crown badge for the host; teal
      "READY" chip for other players). A "Carvers gathered" header shows the live
      count as a teal chip (e.g. "2 of 6"). See `docs/design/README.md` Screens -
      screen 3 (Lobby) and `docs/design/screens/03-lobby.png`.
- [x] AC-02: Given a new player joins, then their tile fills an empty slot with a
      scale-pop entrance animation (transform:scale only - no opacity keyframe)
      and a transient dark toast at bottom-center reads "[Name] pulled up a
      stone" (visible for ~2.6s, then slides out). The count chip updates. See
      `docs/design/README.md` Screens screen 3 (behavior) and Implementation
      Gotchas.
- [x] AC-03: Given a player slot is not yet filled, then it renders as a dashed
      circle with 3 pulsing dots and "waiting..." text; the border color pulses
      purple.
- [x] AC-04: Given a player leaves (closes the app or navigates away), then the
      roster reflects their departure within a short, reasonable window (slot
      reverts to empty).
- [x] AC-05: Given I am the host, then I can see the current player count and the
      pinned gold "Start game" CTA; a note reads "You're the host - start whenever
      your crew's ready" with a crown glyph. Only the host sees the Start button.
- [x] AC-06: Given the roster is displayed, then every name shown has passed the
      safety filter (no unfiltered free text is ever rendered).

## Out of Scope
- Rich presence (typing / active indicators).
- "Reconnecting..." states and reconnect hardening (deferred to Phase 2).
- Host kicking, muting, or skipping slow players (Phase 2, parked).
- Capacity controls beyond the default 6-player limit.

## Technical Notes
- The hub broadcasts roster changes (join/leave) to the room group; the web
  client subscribes via the single connection hook and re-renders the roster.
- Player tile: 74px circle (`#FBF6EA`, `2.5px #E0CDA0`), contains a
  `<Guardian variant size={52} />`, name (Fredoka 500 15px), and role chip.
  Host tile gets an additional crown badge (24px, positioned above avatar) and
  a pulsing gold ring (`@keyframes`, 2.4s). See `docs/design/README.md`
  Screens screen 3 for full styling.
- Toast ("Name pulled up a stone"): a bottom-center transient overlay,
  dark background, slide-up in and slide-out, 2.6s lifetime. Implemented as a
  short-lived state item, not a permanent UI element.
- Scale-pop on player arrival: `transform: scale(0) -> scale(1)`, ~0.45s ease.
  Never animate opacity (see design pack Gotchas).
- Leave detection relies on the SignalR connection lifecycle for Slice 1.

## Tests
- `tests/QuibbleStone.Api.Tests/RoomRegistryLeaveTests.cs` (xUnit): removing a
  connection drops that player and returns the still-active room; removing the
  last player removes the room and frees the code (`ActiveRoomCount` -> 0);
  removing an unknown/empty connection is a no-op (AC-04).
- `tests/QuibbleStone.Api.Tests/GameHubDisconnectTests.cs` (xUnit):
  `OnDisconnectedAsync` broadcasts the trimmed roster to survivors when members
  remain, drops the room and does not broadcast when the last player leaves, and
  is a no-op for an unseated connection (AC-04).
- The live roster UI (3-column tiles, host crown/ring, teal READY, live count,
  scale-pop entrance, join toast, dashed empty slots, host-only Start - AC-01/02/
  03/05) is covered by the Phase 4 two-browser real-time walkthrough (Vitest is
  pure-logic only in Slice 1).

## Dependencies
- session-engine/01-create-room
- session-engine/02-join-with-code
- session-engine/05-guardian-avatar-selection (Guardian variant on each tile)
- design-system/02-guardian-component
