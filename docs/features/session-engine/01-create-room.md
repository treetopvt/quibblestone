# Story: Create a room and get a join code

**Feature:** Session & Room Engine  ·  **Status:** Not Started

## Context
A host needs to start a session that others can join from their own devices.
This is the front door to group play, and everything else in the room rides on
it. See [feature.md](./feature.md).

## Acceptance Criteria
- [ ] AC-01: Given I am on the home screen, when I choose "Start a room", then a
      room is created and I land in its lobby as the host.
- [ ] AC-02: Given a room is created, then I am shown a short, human-friendly join
      code that is easy to read aloud and type (a few characters, no ambiguous
      glyphs such as O/0 or I/1/l).
- [ ] AC-03: Given a room is created, then its join code is unique among currently
      active rooms.
- [ ] AC-04: Given I am in the lobby and no one else has joined, then I see myself
      marked as host and a clear "waiting for players" state with the code shown.
- [ ] AC-05: Given a room has had no activity for a reasonable window, then it is
      allowed to expire (rooms are ephemeral; no durable persistence required).

## Out of Scope
- Reconnect / rejoin hardening (deferred past Slice 1).
- Custom room names, private or locked rooms, max-size enforcement tuning.
- Persisting room history.

## Technical Notes
- Add room-creation to the SignalR hub in `api/` (the skeleton `GameHub` grows
  here) plus whatever ephemeral in-memory room registry it needs.
- Web: a "Start a room" action on the placeholder home page (`web/src/`) that
  calls the hub and routes into a lobby view; reuse the single connection hook in
  `web/src/signalr/`.
- Code generation lives server-side; exclude ambiguous characters.

## Dependencies
- platform-devops (real-time backbone reachable). Otherwise none.
