# Story: Create a room and get a join code

**Feature:** Session & Room Engine  ·  **Status:** Not Started

## Context
A host needs to start a session that others can join from their own devices.
This is the front door to group play, and everything else in the room rides on
it. See [feature.md](./feature.md).

## Acceptance Criteria
- [ ] AC-01: Given I am on the home screen, when I tap "Create a game" (the gold
      primary CTA, "+" icon), then a room is created and I land in its lobby as
      the host. The home screen also offers a "Join a game" outlined-purple
      secondary button and a reassurance line "No account needed - just pick a
      name & play" with a teal check icon. See `docs/design/README.md` Screens -
      screen 1 (Home) and `docs/design/screens/01-home.png`.
- [ ] AC-02: Given a room is created, then I am shown a short, human-friendly join
      code that is easy to read aloud and type (4 characters, no ambiguous glyphs
      such as O/0 or I/1/l). The design shows a 4-slot carved code widget (e.g.
      "MOSS") prominently on both the Join and Lobby screens.
- [ ] AC-03: Given a room is created, then its join code is unique among currently
      active rooms.
- [ ] AC-04: Given I am in the lobby and no one else has joined, then I see myself
      marked as host (with the gold HOST chip and crown badge) and a clear
      "waiting for players" state with the code shown; empty player slots show
      dashed borders with pulsing dots. See `docs/design/README.md` Screens -
      screen 3 (Lobby) and `docs/design/screens/03-lobby.png`.
- [ ] AC-05: Given a room has had no activity for a reasonable window, then it is
      allowed to expire (rooms are ephemeral; no durable persistence required).

## Out of Scope
- Reconnect / rejoin hardening (deferred past Slice 1).
- Custom room names, private or locked rooms, max-size enforcement tuning.
- Persisting room history.
- Home screen animations (mascot bob, twinkling sparkles, ambient glow pulse)
  are a delight-tier pass; the screen layout and CTAs are in scope.

## Technical Notes
- Add room-creation to the SignalR hub in `api/` (the skeleton `GameHub` grows
  here) plus whatever ephemeral in-memory room registry it needs.
- Web: implement the Home screen (`web/src/`) with the gold "Create a game"
  CTA and the outlined-purple "Join a game" button, using the shared Button
  components from design-system/01. The hero mascot SVG (design-system/01
  story) is used here. Reuse the single connection hook in `web/src/signalr/`.
- Home screen layout: kicker chip (purple pill, teal glowing dot, "FAMILY WORD
  QUEST" text), stone-tablet hero panel with wordmark, tagline, action buttons.
  See `docs/design/README.md` Screens screen 1 for full spec.
- Code generation lives server-side; generate 4-character codes, exclude O, 0,
  I, 1, l.

## Dependencies
- platform-devops (real-time backbone reachable).
- design-system/01-mui-theme-and-app-shell (theme + button components).
