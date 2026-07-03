# Story: Rejoin - reclaim your seat and resume the round

**Feature:** Session & Room Engine  ·  **Status:** Not Started  <!-- Not Started | In Progress | Complete | Blocked | Dropped -->  ·  **Issue:** #142

## Context
Story 07 holds a dropped seat open for a grace window and mints a reconnect token
when a player is seated; on its own that story only delays the eventual
eviction/abort. This story spends the token: a new `Rejoin(code, token)` hub
method lets the SAME device reclaim its held seat under a brand-new SignalR
connection (SignalR's own `withAutomaticReconnect` always gets a fresh
`connectionId` after a drop) and rehydrates enough round state - its own
outstanding blanks, the room's collection progress, or the shared reveal if the
round already finished - that the resuming client can pick up exactly where it
left off, rather than looking like it re-joined a fresh game. See
[feature.md](./feature.md) and `07-disconnect-grace-window.md` (the grace window
and token this story consumes).

## Acceptance Criteria
- [ ] AC-01: Given a device holds a reconnect token for a seat still within its
      grace window, when it calls `Rejoin(code, token)` on a NEW connection, then
      the server reclaims that seat under the new connection (re-subscribes it to
      the room's SignalR group), marks it connected again, and cancels the pending
      grace-expiry eviction for that seat.
- [ ] AC-02: Given the reclaim succeeds, then the caller receives the current room
      roster, whether it is the host, and the round's phase ("lobby" | "prompting"
      | "reveal").
- [ ] AC-03: Given the round is "prompting", then the rehydration ALSO carries this
      seat's own remaining (not-yet-submitted) blank indices only - never another
      player's - and the room's current collection progress ("[N] of [M] done"), so
      the resumed client's word-collection screen shows exactly what is left,
      nothing already-answered re-asked.
- [ ] AC-04: Given the round is "reveal", then the rehydration carries the shared
      reveal payload (the ordered words) so the resuming client renders the same
      reveal everyone else is looking at.
- [ ] AC-05: Given the token is unknown, already evicted (grace expired), or names
      a different room than `code`, then `Rejoin` fails with a friendly,
      kid-readable error and reclaims nothing - the caller is told plainly rather
      than silently failing or throwing.
- [ ] AC-06: Given the reclaim succeeds, then the OTHER players in the room see the
      roster flip that seat back to connected (the same `RosterChanged` broadcast,
      now carrying that player's `Connected: true`) in near-real-time.
- [ ] AC-07 (child safety / no PII): the rehydration payload carries no more than
      the room already exposes elsewhere in this codebase (own blank indices,
      room-wide progress/reveal) - no other player's private state, no token, and
      no new personal data.

## Out of Scope
- Reaction-tally and Golden Guardian vote-state rehydration on a reveal-phase
  reconnect - a fast-follow (see feature.md Parked). The reveal WORDS resume; the
  room-wide reaction/vote counters may show at their just-reset defaults until the
  next broadcast nudges them.
- Host migration - a reconnecting HOST whose grace already expired (evicted) has
  no seat left to reclaim; that gap is the parked host-migration item, unchanged
  by this story.
- Any client-side wiring (storing/sending the token automatically, deciding WHEN
  to call `Rejoin`) - that is story 09.
- Multi-device / cross-device resume (the same player picking up on a DIFFERENT
  device) - the token is device-local by design (see feature.md Parked).

## Technical Notes
- `api/src/Rooms/Room.cs`: add `ReclaimSeat(token, newConnectionId)` under the
  existing `_gate` lock, returning an outcome the hub maps to the friendly
  envelope (not-found / reclaimed). Reuse the EXISTING
  `GetProgress()`/`GetProgressCounts()`/`BuildReveal()` for AC-03/AC-04's
  rehydration data - do not build a second, parallel round-state projection. Add
  a small pure helper that, given a connection's own assignment, returns just the
  blank indices NOT yet present in `Submissions` (mirrors the shape of the
  existing `YourBlanksDto` - index-only, no words, no PII).
- `api/src/Hubs/GameHub.cs`: new `Task<RejoinResultDto> Rejoin(string code, string
  token)`. On success, cancel the pending grace-expiry continuation from story 07
  (guard the same disconnect-episode check that story introduced, so a race
  between "grace expires" and "Rejoin lands" resolves deterministically under the
  lock - whichever wins, the other is a no-op). Re-run
  `Groups.AddToGroupAsync(Context.ConnectionId, room.Code)` for the NEW connection
  (the OLD connection's group membership already tore down via SignalR's own
  disconnect handling). Broadcast the updated roster exactly like `JoinRoom` does
  (AC-06).
- `RejoinResultDto` mirrors every other hub result envelope in this codebase
  (`Ok`, `Error`, plus the rehydration fields) - never throws for an expected
  failure (AC-05), matching `JoinResultDto`/`StartRoundResultDto`/etc.
- This is a NEW hub method (unlike most prior session-engine stories, which grew
  an existing method) - keep the DTO shape hand-in-sync with `useGameHub.ts`
  (story 09 consumes it) exactly like every other hub contract here; there is no
  codegen step (CLAUDE.md section 4 / the orchestration playbook).

## Tests
| AC | Test |
|---|---|
| AC-01 | xUnit: a valid token reclaims the seat under a new `connectionId` and cancels the pending grace eviction |
| AC-02 | xUnit: the roster + host flag + phase come back correctly for each of "lobby" / "prompting" / "reveal" |
| AC-03 | xUnit: a "prompting" rejoin returns ONLY its own remaining blank indices (never another seat's) plus the current progress counts |
| AC-04 | xUnit: a "reveal" rejoin returns the ordered reveal words |
| AC-05 | xUnit: an unknown / already-evicted / wrong-room token returns a friendly `Ok:false` envelope and mutates nothing |
| AC-06 | xUnit: a successful reclaim broadcasts `RosterChanged` with that seat's `Connected` flipped true |
| AC-07 | code review: the rehydration payload is inspected against every other DTO in `GameHub.cs` for scope creep |
| manual | disconnect a device mid-round (e.g. toggle airplane mode), reconnect within the grace window, confirm it lands back in the SAME round with only its outstanding blanks to fill (full walkthrough pairs with story 10's client) |

## Dependencies
- session-engine/07-disconnect-grace-window
