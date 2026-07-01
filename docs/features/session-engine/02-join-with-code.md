# Story: Join a room with a code and nickname

**Feature:** Session & Room Engine  ·  **Status:** In Review

## Context
Players join a host's room from their own device with no account - just a code
and a nickname. This is the anonymous, no-PII identity posture (README section 3)
and the child-privacy stance (section 6). See [feature.md](./feature.md).

## Acceptance Criteria
- [x] AC-01: Given I have a valid join code, when I enter it into the 4-slot
      carved code input and provide a display name, then I join that room and land
      in its lobby. The Join screen has two stacked cards: a room-code card and a
      character card (display name + avatar grid). The pinned gold CTA reads
      "Join [CODE] ->" with the entered code interpolated. See
      `docs/design/README.md` Screens - screen 2 (Join) and
      `docs/design/screens/02-join.png`.
- [x] AC-02: Given I am joining, then I am asked only for a code and a display
      name - never for an account, email, or any other personal information. A
      reassurance line "100% anonymous - no email, no account" (shield icon) is
      visible on the screen.
- [x] AC-03: Given I enter a display name (max 14 characters; a live character
      counter shows "n/14"), then it is checked by the safety filter before it is
      shown to anyone; a failing name is rejected with a friendly message and I
      can try another. No PII is collected.
- [x] AC-04: Given I enter a code that is invalid or expired, then I see a
      friendly error and I am not joined.
- [x] AC-05: Given I successfully join, then the host and other players see me
      appear in the roster in near-real-time, represented by my display name and
      Guardian variant.
- [x] AC-06: Given my chosen display name is already in use in that room, then I
      am prompted to choose a different one.

## Out of Scope
- Rejoin-after-disconnect (reconnect hardening deferred to Phase 2).
- Persistent identity or account (Phase 2).
- Avatar selection UI - that is story 05; this story covers the code + name
  form flow, joining the room, and the server-side validation.

## Technical Notes
- Hub method to join by code; server validates the code and display name
  (uniqueness within the room, safety filter) and broadcasts the roster change.
  The join payload also includes the player's chosen Guardian variant (story 05
  populates it; this story can default to `teal` until 05 lands).
- Room-code card: four carved slots (`height:64px`, `#DFD2B4`, radius 16,
  inset shadow `inset 0 3px 7px rgba(120,96,52,.45)`, Fredoka 600 32px
  `#6C4BD8`). The header row shows "ROOM CODE" (purple, uppercase) and a teal
  chip "from the host". See `docs/design/README.md` Screens screen 2.
- Display name field: MUI outlined text field, 56px, `#FBF6EA` fill,
  `2px solid #6C4BD8`, radius 16, floating label "Display name" in purple,
  person icon, live `n/14` counter, Fredoka 500 19px input text.
- Nickname safety check goes through the child-safety filter (see
  `docs/features/child-safety/`); do not show an unfiltered name to anyone.
- Web: the join form uses MUI; validation messages are friendly and brief (the
  audience includes kids).

## Tests
- `tests/QuibbleStone.Api.Tests/GameHubJoinTests.cs` (xUnit, 8): unknown/expired
  code rejected (AC-04); empty/whitespace/over-14 name rejected (AC-03); a blocked
  name is never added to the roster (AC-03, child safety); case-insensitive
  duplicate name rejected (AC-06); success path adds the player, subscribes the
  connection to the room group, and broadcasts `RosterChanged` (AC-01, AC-05);
  variant defaults to `teal` when unset. Uses the real `GameHub` + `RoomRegistry`
  + `ContentSafetyFilter` (no mocking lib) with small SignalR fakes.

## Dependencies
- session-engine/01-create-room
- child-safety/01-profanity-filter
- design-system/01-mui-theme-and-app-shell
