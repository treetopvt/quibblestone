# Story: Collect words and ready the reveal

**Feature:** Group Play Experience  ·  **Status:** Not Started

## Context
Players submit their assigned words; the host watches progress; once everything is
in, the room is ready for the shared reveal. This closes the real-time loop that
makes group play work. See [feature.md](./feature.md).

## Acceptance Criteria
- [ ] AC-01: Given I have been assigned a blank, when I submit a word, then it is
      accepted only after passing the safety filter and is recorded for the round.
- [ ] AC-02: Given I am the host, then I see collection progress (how many blanks
      are in / outstanding) in near-real-time.
- [ ] AC-03: Given all assigned blanks have been submitted, then the round is ready
      and the reveal can be triggered (see the-reveal).
- [ ] AC-04: Given I have submitted my word, then I see a waiting state and do not
      see other players' answers or the story before the reveal (Classic blind).
- [ ] AC-05: Given any submitted word, then it passed the safety filter before
      being recorded or revealed.

## Out of Scope
- Editing a submitted word after sending.
- Progressive reveal (a later mode).
- Handling a player who never submits (timeout/skip is later hardening); reconnect.

## Technical Notes
- Submissions are hub messages bound to the round + the player's assigned blank;
  the server validates (safety filter) and tracks completion, broadcasting
  progress to the host.
- When complete, transition the room to the reveal (the-reveal/01 broadcasts it).

## Dependencies
- group-play/02-distribute-blanks
- the-reveal/01-text-reveal
- child-safety/01-profanity-filter
