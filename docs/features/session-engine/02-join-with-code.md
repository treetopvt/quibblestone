# Story: Join a room with a code and nickname

**Feature:** Session & Room Engine  ·  **Status:** Not Started

## Context
Players join a host's room from their own device with no account - just a code
and a nickname. This is the anonymous, no-PII identity posture (README section 3)
and the child-privacy stance (section 6). See [feature.md](./feature.md).

## Acceptance Criteria
- [ ] AC-01: Given I have a valid join code, when I enter it together with a
      nickname, then I join that room and land in its lobby.
- [ ] AC-02: Given I am joining, then I am asked only for a code and a nickname -
      never for an account, email, or any other personal information.
- [ ] AC-03: Given I enter a nickname, then it is checked by the safety filter
      before it is shown to anyone; a failing nickname is rejected with a friendly
      message and I can try another.
- [ ] AC-04: Given I enter a code that is invalid or expired, then I see a friendly
      error and I am not joined.
- [ ] AC-05: Given I successfully join, then the host and other players see me
      appear in the roster in near-real-time.
- [ ] AC-06: Given my chosen nickname is already in use in that room, then I am
      prompted to choose a different one.

## Out of Scope
- Rejoin-after-disconnect (reconnect hardening deferred).
- Avatars, persistent identity, accounts (Phase 2).

## Technical Notes
- Hub method to join by code; server validates the code and nickname (uniqueness
  within the room) and broadcasts the roster change.
- Nickname safety check goes through the child-safety filter (see
  `docs/features/child-safety/`); do not show an unfiltered nickname to anyone.
- Web: a join form (code + nickname) using react-hook-form + MUI; friendly,
  short validation messages (the audience includes kids).

## Dependencies
- session-engine/01-create-room
- child-safety/01-profanity-filter
