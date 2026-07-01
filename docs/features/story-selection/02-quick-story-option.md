# Story: Quick story option (solo + group)

**Feature:** Story Selection & Freshness  ·  **Status:** Not Started  ·  **Issue:** #92

## Context
Story 01 gave the pipeline a length filter; this story gives the PLAYER the
choice. A solo player about to fill 10 blanks alone, or a host with a carful
of kids and a 5-minute drive, should be able to say "make it a quick one" and
get a 4-6 blank tale. The choice is a mode-style configuration flag on the
existing flows - not a new mode, not a new engine path. See
[feature.md](./feature.md).

## Acceptance Criteria
- [ ] AC-01: Given the solo start screen, when I am choosing to play, then I
      see a low-friction story-length choice (e.g. "Quick tale" / "Full tale")
      with big tap targets, defaulting to Full. Picking Quick makes the solo
      pick draw from the quick pool via story 01's pipeline.
- [ ] AC-02: Given the group lobby, when I am the host, then I see the same
      length choice alongside the existing family-safe toggle; non-hosts do
      not control it. The choice is sent with the existing start-round invoke
      (one more parameter on the existing hub method, not a new method).
- [ ] AC-03: Given the host chose Quick, then the SERVER enforces it: the
      round's template is drawn quick-first server-side (story 01's mirrored
      filter), never trusted from a client-side pick - same authority posture
      as the family-safe gate.
- [ ] AC-04: Given a Quick round in a group larger than the quick template's
      blank count, then the round still works: distribution's existing
      round-robin rule applies unchanged, and players left without a blank
      this round wait on the existing interstitial. (A visible "short story,
      not everyone carves this round" hint on that interstitial is welcome
      but its wording is not load-bearing.)
- [ ] AC-05: Given the length choice on any surface, then it never weakens
      safety: the family-safe gate still runs first, and the safety filter on
      submitted words is untouched.
- [ ] AC-06: Given I make no choice anywhere, then behavior is today's: Full
      is the default on both surfaces and existing flows read identically.

## Out of Scope
- Remembering the choice across sessions (no accounts; a device-local
  preference is a nice-to-have parked with keepsake-gallery's local-storage
  posture).
- A third "epic / extra-long" class, timers, or per-player length preferences.
- Auto-choosing length from player count (considered and parked - see
  feature.md Parked; explicit beats clever until playtesting says otherwise).

## Technical Notes
- Solo: the choice lives on the solo entry surface next to the family-safe
  toggle; wire it into `Solo.tsx`'s pick as `selectByLength(selectTemplates(
  seedLibrary, familySafe), lengthPref)` - stages compose, order fixed
  (safety first).
- Group: extend the existing start-round hub invoke signature
  (`web/src/signalr/useGameHub.ts` + `GameHub.StartRound`) with the length
  pref, exactly how the family-safe flag already travels. Wire-contract
  discipline: DTO/param mirrored by hand, noted in both headers.
- UI: MUI theme components only, reuse the toggle/button patterns the lobby
  and solo screens already use (design-system); FontAwesome for any icon.
  "Quick" copy should signal fewer turns, not lower quality ("A short one -
  about 5 blanks").
- Replay seams: "Play another round" / future "Carve it again" keep whatever
  length the room last used - the choice is room state, not per-invoke
  ceremony (store it beside familySafe on the Room record).

## Tests
| AC | Test |
|---|---|
| AC-01 | Vitest on the solo pick composition; manual: solo start shows the choice, Quick yields a 4-6 blank story |
| AC-02 | manual: two browser contexts - host sees the control, guest does not |
| AC-03 | API unit test: StartRound with quick pref selects only quick catalog ids regardless of client state |
| AC-04 | existing `distribute.test.ts` already covers M < N; manual: 7 players, quick story, interstitial shows for unassigned players |
| AC-05 | API unit test: quick pref + family-safe on never yields a non-family-safe id |
| AC-06 | existing solo/group specs unchanged and green |

## Dependencies
- story-selection/01 (the pipeline + quick seed content).
- group-play (lobby, start-round invoke, interstitial) - Complete.
