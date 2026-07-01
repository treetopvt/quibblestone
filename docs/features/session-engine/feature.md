# Feature: Session & Room Engine

## Summary
The SignalR backbone for multiplayer: a host creates a room, players join with a
short code and a nickname, and everyone sees a live roster. Everything in group
play rides on this. Slice 1 is the minimum viable version (create / join / roster);
reconnect-hardening is deliberately deferred.

## README reference
README section 7 (Epic Map - Phase 0, Session & Room Engine) and section 8
(Roadmap - Slice 1: "create room, join code, roster; skip reconnect-hardening").
Architecture: section 4 (cloud real-time, SignalR).

## Stories
- [ ] 01 - Create a room and get a join code
- [ ] 02 - Join a room with a code and nickname
- [ ] 03 - See a live player roster
- [ ] 04 - Copy and share the room code from the Lobby
- [ ] 05 - Guardian avatar selection at join

## Dependencies
- platform-devops (the real-time backbone must be deployable and reachable).
- child-safety (nicknames are free text and must be filtered before display).
- design-system (Guardian component is used in avatar selection and the roster).

## Design notes
- Real-time runs over the one SignalR hub in `api/` (the skeleton's `GameHub`
  grows room methods; the web client uses the single connection hook in
  `web/src/signalr/`). After a server event, the client updates room state.
- Rooms are **ephemeral session state** (README section 4 - this is a toy, not a
  system of record). No durable persistence is required for Slice 1; a room can
  live in memory / short-lived storage and expire when idle.
- **Identity is anonymous** (README section 3): a player is a nickname + a
  Guardian variant, no account and no PII. The account hooks come later (Phase
  2), but Slice 1 collects nothing about players beyond an in-session nickname
  and chosen avatar variant.
- Join codes are short and human-friendly (easy to read aloud in a car): a few
  characters, no ambiguous glyphs (no O/0, I/1/l). The design spec shows a
  4-character code (e.g. "MOSS") in carved slots on the Join screen and
  prominently on the Lobby. See `docs/design/README.md` Screens 2 and 3.
- The Lobby's share widget (story 04) covers the "different houses" use case
  without requiring accounts: copy or Web Share.
- Reconnect tolerance (the car "dead zone" case) is a Phase-later hardening
  pass, not Slice 1. Keep the seams clean so it can be added without a rewrite.

## Parked - Phase 2+
- Device-local remembered profile (name + variant pre-filled on a return visit) is
  now **delivered** via `localStorage` (`web/src/identity.ts`, host-identity work).
  What remains parked is **cross-device identity sync** (the same name + variant on a
  player's other devices), which needs the account/entitlement seam (README section 3,
  monetization) rather than a standalone player store - see #51.
- **Host migration**: when the host leaves mid-session, promote a remaining player to
  host (or otherwise keep the room startable) instead of leaving it hostless - see #50.
  Part of the deferred reconnect / resilience hardening (design pack Expansion 5).
- Room reconnection after a dropped connection (the car "dead zone" case; design pack
  Expansion 5).
- "Tales we've carved" local history (design pack Expansion 5).
