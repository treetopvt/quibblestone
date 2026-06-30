# Story: See a live player roster

**Feature:** Session & Room Engine  ·  **Status:** Not Started

## Context
Everyone in a room needs to see who else is there - the social anchor of group
play and the host's signal for when to start. This is minimum-viable presence.
See [feature.md](./feature.md).

## Acceptance Criteria
- [ ] AC-01: Given I am in a room, then I see a live list of everyone currently in
      it, including myself, with the host clearly indicated.
- [ ] AC-02: Given a new player joins, then the roster updates for everyone in
      near-real-time without a manual refresh.
- [ ] AC-03: Given a player leaves (closes the app or navigates away), then the
      roster reflects their departure within a short, reasonable window.
- [ ] AC-04: Given I am the host, then I can see the current player count.
- [ ] AC-05: Given the roster is displayed, then every name shown has passed the
      safety filter (no unfiltered free text is ever rendered).

## Out of Scope
- Rich presence (typing / active indicators).
- "Reconnecting..." states and reconnect hardening (deferred).
- Host kicking or muting players.

## Technical Notes
- The hub broadcasts roster changes (join/leave) to the room group; the web
  client subscribes via the single connection hook and re-renders the roster.
- Leave detection can rely on the connection lifecycle for Slice 1 (a precise,
  reconnect-tolerant model comes with the later hardening pass).

## Dependencies
- session-engine/01-create-room
- session-engine/02-join-with-code
