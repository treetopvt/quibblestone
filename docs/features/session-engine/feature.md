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

## Dependencies
- platform-devops (the real-time backbone must be deployable and reachable).
- child-safety (nicknames are free text and must be filtered before display).

## Design notes
- Real-time runs over the one SignalR hub in `api/` (the skeleton's `GameHub`
  grows room methods; the web client uses the single connection hook in
  `web/src/signalr/`). After a server event, the client updates room state.
- Rooms are **ephemeral session state** (README section 4 - this is a toy, not a
  system of record). No durable persistence is required for Slice 1; a room can
  live in memory / short-lived storage and expire when idle.
- **Identity is anonymous** (README section 3): a player is a nickname in a room,
  no account and no PII. The account hooks come later (Phase 2), but Slice 1
  collects nothing about players beyond an in-session nickname.
- Join codes are short and human-friendly (easy to read aloud in a car): a few
  characters, no ambiguous glyphs (no O/0, I/1/l).
- Reconnect tolerance (the car "dead zone" case) is a Phase-later hardening pass,
  not Slice 1. Keep the seams clean so it can be added without a rewrite.
