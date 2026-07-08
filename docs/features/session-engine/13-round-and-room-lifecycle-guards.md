# Story: Round and room lifecycle guards - StartRound, JoinRoom, and the idle sweep

**Feature:** Session & Room Engine  ·  **Status:** Not Started  <!-- Not Started | In Progress | Complete | Blocked | Dropped -->  ·  **Issue:** TBD

## Context
This story closes three still-open "W-tier" warnings from the roadmap's pre-beta
release-readiness audit (`docs/ROADMAP.md`, "The alpha gate - fix before inviting
the family"): **W1**, **W3**, and **W4**. The audit's blocker/high items (B1-B5)
and W5 are already merged and the alpha gate itself is closed for the
friends-and-family test, but the roadmap explicitly left these three as
fast-follows, not blockers ("W1-W4 stay fast-follows, not blockers"). Verified
directly against the current code on 2026-07-08 - all three still reproduce and
are not yet filed as GitHub issues.

They are grouped into ONE story here, rather than three, because all three are
the SAME class of gap: a room/round lifecycle transition (start a round, join a
room, expire an idle room) that the server does not yet guard correctly against
a chaotic, real living-room session - a host double-tapping "Start" mid-deal, a
cousin's phone joining a beat too late, or a long, chatty game leaving a room's
last-hub-activity clock stale while every seat is still very much connected.
Each fix is small, surgical, and server-authoritative, living in the SAME
room/round lifecycle model this feature already owns (`api/src/Hubs/GameHub.cs`,
`api/src/Rooms/Room.cs`, `api/src/Rooms/RoomRegistry.cs`) - none of them touch a
new surface, and none of them add a new free-text field or collect anything new
about a player (see Out of Scope). See [feature.md](./feature.md) and
`docs/ROADMAP.md`'s "The alpha gate" table for the original audit entries.

Bug 1 (W3, AC-01) edits the SAME `StartRound` hub method that
`../group-play/01-start-round.md` specifies from the player-facing side ("the
host kicks off a round... the round begins for everyone in the room"). This
story is a correctness hardening of that EXISTING flow (closing a re-entrancy
hole a double-tap can trigger), not a new capability, so it stays here in
session-engine (which owns `GameHub.cs`'s room/round lifecycle end to end)
rather than forking into group-play - flag this cross-reference if
`group-play/01` (or any other story touching `StartRound`) is ever in flight at
the same time as this one.

## Acceptance Criteria
- [ ] AC-01 (W3 - StartRound rejects a re-start mid-round): Given a room's round
      is in the "prompting" phase (`room.CurrentRound is not null` and
      `CurrentRound.Phase == "prompting"`), when `StartRound`
      (`api/src/Hubs/GameHub.cs`) is called again for that same room (a host
      double-tap, or any other caller), then the server rejects the call with a
      friendly `StartRoundResultDto(false, "...")` - mirroring the EXACT
      phase-check style `PassHost` already uses in this same file (`round is not
      null && string.Equals(round.Phase, "prompting", StringComparison.Ordinal)`)
      - BEFORE resolving the mode, running the family-safe/length/freshness
      template-selection pipeline, dealing any blanks, or touching a single
      already-submitted word: nothing already collected is discarded and nobody
      is re-dealt. A round in the "reveal" phase (a FINISHED round - exactly how
      "Play another round" already works today, per `BackToLobby`'s own doc
      comment: "the replay counterpart is just StartRound again on the same
      room") or "lobby" (no round at all) is UNAFFECTED and still starts a round
      normally - the guard catches ONLY "prompting," never a broader "any round
      exists" condition that would break the existing replay flow.
- [ ] AC-02 (W4 - JoinRoom rejects a join mid-round): Given a room's round is NOT
      in the "lobby" phase (`room.CurrentRound is not null` - this covers BOTH
      "prompting" and "reveal"), when a new player calls `JoinRoom`
      (`api/src/Hubs/GameHub.cs`) with that room's code, then the server rejects
      the join with a friendly `JoinResultDto(false, null, "...")`, checked
      FIRST - right after the room lookup and before the display-name length
      check, the content-safety filter, or seating the player - so a blocked
      joiner never pays for the async safety-filter round-trip on a join that
      was always going to fail, and is never partially seated. This covers BOTH
      non-lobby phases deliberately: blocking only "prompting" would still let a
      joiner land exactly during "reveal" and reproduce the audit's own named
      symptom ("yanked into a reveal they did not play") - `RoomStateDto` (what
      `JoinRoom` returns on success) carries no phase or round information at
      all, so a joining client has no way to detect a live round on its own; the
      block must be server-side and must cover both phases. Once the round
      returns to "lobby" (via `BackToLobby`, or a room that never started one),
      `JoinRoom` behaves exactly as it does today, with no change to the
      existing name-length, safety-filter, uniqueness, or W2 capacity checks.
- [ ] AC-03 (W1 - idle sweep exemption plus a graceful client fallback): Given a
      room has at least one still-connected seat
      (`room.SnapshotPlayers().Any(p => p.Connected)` - the SAME `Connected` flag
      session-engine/07's disconnect grace window already maintains), when
      `RoomRegistry.SweepExpired()` (`api/src/Rooms/RoomRegistry.cs`) runs after
      the 30-minute `InactivityWindow` has elapsed since `LastActiveUtc`, then
      that room is EXEMPT from the sweep and stays reachable (`TryGet` still
      resolves it) for as long as any seat stays connected - a long, chatty
      session's room is never pulled out from under still-connected players, no
      matter how stale `LastActiveUtc` gets. A room where EVERY seat has
      disconnected (nobody `Connected`) is still swept exactly as today once
      past the window - this story narrows WHEN the sweep fires, it does not
      remove it. AND, given any in-room hub call that looks a room up by code
      (`StartRound`, `BackToLobby`, `PassHost`, `SubmitWord`, `RemixWord`) comes
      back with the server's "we couldn't find a game with that code" outcome
      for a room the client believed it was still in, then the web client
      (`web/src/signalr/useGameHub.ts`) clears its local room/round/reveal state
      through the EXISTING `resetRoomState()` helper (the same helper the
      alpha-gate B4 fix's rejected-`Rejoin` path already uses) so the EXISTING
      live-route guards (`web/src/App.tsx`, session-engine/10) return the player
      Home gracefully, instead of leaving a screen frozen on a room the server no
      longer has.

## Out of Scope
- A "spectator" join mode for a late arrival - AC-02 uses the simpler
  block-with-friendly-message the roadmap and this story both recommend; letting
  a late joiner watch (not play) the current round is a nice-to-have parked for
  later if the friends-and-family test surfaces real demand for it.
- Any change to `PassHost`'s or `BackToLobby`'s OWN existing guards - `PassHost`
  already correctly blocks a mid-"prompting" handoff; this story reuses its
  phase-check STYLE as the mirror for AC-01 but does not modify it.
- Blocking a `Rejoin` during "prompting" or "reveal" - `Rejoin` reclaims a seat a
  device ALREADY held before the round started; it is a resume, not a new join,
  and must keep working mid-round exactly as today (that is the entire point of
  session-engine/07-08). AC-02 only blocks brand-new seats.
- A room where every seat has disconnected getting swept sooner than the
  existing 30-minute window, and any change to `SeatGraceService`'s separate,
  much shorter 3-minute grace window (session-engine/07) - a room mid-grace is a
  different clock from the 30-minute idle sweep and is untouched here.
- Lengthening `InactivityWindow` as an alternative to the exemption - the
  roadmap offered "exempt... or lengthen" as two options; this story picks the
  exemption (see Technical Notes for why).
- Any new free-text or PII surface: none of the three fixes add a field a player
  types into or a screen that shows new player-submitted content. AC-02's
  rejection message is a fixed, server-authored string with no player input
  echoed back, and the existing `JoinRoom` nickname-safety-filter path is
  completely unchanged, just short-circuited earlier when a round is already
  live. No new data is collected about any player by any of the three fixes
  (README section 6 / CLAUDE.md section 5).
- Rate-limiting `StartRound`/`JoinRoom` themselves - a separate "notes-tier" item
  in the same roadmap audit, not part of this story.
- Redesigning "we couldn't find a game with that code" into a structured error
  CODE across the whole hub surface - this story only centralizes the literal
  string (today duplicated with no shared constant) enough to make the client
  check in AC-03 reliable. A fuller structured-error-envelope redesign is a
  bigger, separate concern if this class of problem grows.

## Technical Notes
- **AC-01 - `StartRound`** (`api/src/Hubs/GameHub.cs`, currently ~line 927):
  today's validation order is room lookup, host check (`room.IsHost(...)` /
  `room.HasHost`), player-count >= 2, THEN mode resolution and the four-stage
  template-selection pipeline. Insert the new phase guard as its own step right
  after the host check and before the player-count check - mirroring
  `PassHost`'s own order in this same file (room lookup -> host check -> phase
  check -> action). Copy `PassHost`'s exact condition (same file, ~line
  1351-1357):
  ```csharp
  var round = room.CurrentRound;
  if (round is not null && string.Equals(round.Phase, "prompting", StringComparison.Ordinal))
  {
      return new StartRoundResultDto(false, "...");
  }
  ```
  `Room.CurrentRound` (`api/src/Rooms/Room.cs`) already returns a detached
  snapshot (null while in the lobby) - reuse it directly, do not add a second
  phase accessor. Do NOT gate on "any round exists" (`CurrentRound is not null`
  alone) - that would ALSO block a "reveal"-phase restart and break "Play
  another round," a deliberate, already-shipped behavior. Suggested (not
  mandatory) copy, matching this file's existing stone-carving tone ("Only the
  host can start the game." / "The chisel can only pass between rounds, not
  mid-round."): something like "This tale's already being carved - wait for the
  reveal before starting a new one." No DTO shape change:
  `StartRoundResultDto(bool Ok, string? Error)` already carries everything
  needed.
- **AC-02 - `JoinRoom`** (`api/src/Hubs/GameHub.cs`, currently ~line 571): today's
  order is (1) room lookup, (2) name-length check, (3) content-safety filter,
  (4) seat via `Room.AddPlayer` (uniqueness + W2 capacity). Insert the new phase
  check immediately after (1) and before (2) - the cheapest possible
  short-circuit, so a blocked joiner never pays for the async safety-filter
  round-trip. Condition: `room.CurrentRound is not null` (covers "prompting" AND
  "reveal" - deliberately broader than AC-01's "prompting"-only guard, since
  `JoinRoom` has no "replay" concept to protect the way `StartRound`'s
  reveal-phase restart does). `RoomStateDto(string Code, IReadOnlyList<PlayerDto>
  Players)` (this file, ~line 91) carries NO phase or round field at all - a
  joining client cannot infer "a round is already live" from the success
  envelope, so this guard has to be server-side. Suggested (not mandatory)
  copy: something like "This crew's mid-tale right now - hang tight and you'll
  be seated for the next round." No DTO shape change:
  `JoinResultDto(bool Ok, RoomStateDto? Room, string? Error, string?
  ReconnectToken = null)` already carries everything needed (the same shape
  every other pre-seat rejection in this method already uses). Does NOT affect
  `Rejoin` (this file, session-engine/08) - see Out of Scope.
- **AC-03 - `RoomRegistry.SweepExpired()`** (`api/src/Rooms/RoomRegistry.cs`,
  currently ~line 300): today's cull is one condition,
  `pair.Value.LastActiveUtc < cutoff`. Narrow it to also require no connected
  seat: `pair.Value.LastActiveUtc < cutoff &&
  !pair.Value.SnapshotPlayers().Any(p => p.Connected)`. `Room.SnapshotPlayers()`
  (`api/src/Rooms/Room.cs`, ~line 1109) and `Player.Connected`
  (session-engine/07) are both ALREADY public - no new surface, just a stronger
  cull predicate.
  **Testability seam (required, not optional, for this AC):**
  `RoomRegistryTests.cs`'s own header comment today explicitly says sweep/expiry
  is "left to a manual/integration check... wiring a fake clock is out of scope
  for Slice 1" - this story is exactly what puts it back in scope, since "the
  sweep behaves differently depending on connection state" is the whole point
  of the fix and deserves a fast, deterministic xUnit test, not a 30-minute
  sleep. Mirror `SeatGraceService`'s EXACT dual-constructor shape
  (`api/src/Rooms/SeatGraceService.cs`: a public `DefaultGraceWindow`-using DI
  constructor plus a public test constructor taking an explicit `TimeSpan`):
  rename the current `private static readonly TimeSpan InactivityWindow =
  TimeSpan.FromMinutes(30)` to a public `DefaultInactivityWindow`, and add a new
  public constructor overload taking an explicit `TimeSpan inactivityWindow`
  stored on an instance field that `SweepExpired()` reads. The existing public
  parameterless constructor keeps today's 30-minute default for every other test
  file and DI registration untouched - do not disturb any existing `new
  RoomRegistry()` call site. A test can then build a `RoomRegistry` with a
  millisecond-scale window, seed one room with a connected seat and one where
  every seat has been marked disconnected, wait past the tiny window, and assert
  one still resolves via `TryGet` while the other does not. `Room.MarkDisconnected(connectionId)`
  is already directly callable from a test with no hub/DI ceremony -
  `RoomCapacityTests.cs`'s existing `A_held_disconnected_seat_still_counts_toward_the_cap`
  test is the precedent for calling it straight from a test. Once this ships,
  `RoomRegistryTests.cs`'s header comment disclaiming sweep coverage should be
  updated - it will no longer be accurate.
  **Client fallback:** the literal string "We couldn't find a game with that
  code - double-check and try again." is today duplicated, unDRY, six times
  across `GameHub.cs` (`JoinRoom`, `StartRound`, `BackToLobby`, `PassHost`,
  `SubmitWord`, `RemixWord`). Collapse it to one named constant (e.g. a `private
  const string RoomNotFoundMessage = "...";` on `GameHub`) so there is a single
  source of truth, and mirror that exact literal client-side so
  `useGameHub.ts` can recognize it reliably (a plain string comparison is
  admittedly a little brittle to a future copy change - acceptable for this
  story's scope; a structured error code is the fuller fix if this pattern
  grows, see Out of Scope). Wire ONLY the in-room call wrappers that return a
  room-scoped `{ok, error}` envelope AFTER the player has already joined
  (`startRound`, `backToLobby`, `passHost`, `submitWord`, `remixWord`) to call
  the EXISTING `resetRoomState()` (`web/src/signalr/useGameHub.ts`, ~line 941)
  when they see that exact message. Do NOT wire `joinRoom` / `createRoom` (a
  "not found" there is a normal PRE-join validation message shown inline, never
  a zombie-state reset) or `rejoin` (its own failure path is already
  alpha-gate-B4-fixed and calls `resetRoomState()` today). If a small pure
  classifier helper is extracted for this (e.g. `isRoomNotFoundError(error:
  string | null): boolean`), it can sit alongside this file's other small pure
  helpers (`manualReconnectDelayMs`, `withReconnectJitter`) and get the same
  kind of Vitest coverage `useGameHub.test.ts` already gives those - the actual
  state-reset WIRING is verified manually/Playwright, matching how this file's
  other internal wiring (e.g. the `rejoin()` trigger paths, session-engine/09)
  is verified today, since no hook-rendering Vitest harness exists in this repo.
  **Why exemption over lengthening the window:** the roadmap offered both;
  this story picks the exemption because a room can legitimately run far longer
  than any fixed ceiling while any seat stays connected, and lengthening still
  leaves the exact same class of bug possible for a session longer than
  whatever new, larger number was picked.

## Tests
| AC | Test |
|---|---|
| AC-01 | xUnit: `tests/QuibbleStone.Api.Tests/GameHubStartRoundTests.cs`, new `StartRound_rejects_a_second_start_while_the_round_is_prompting` - start a round, call `StartRound` again on the same room, assert `Ok:false` and that the round's assignments/submissions are byte-for-byte unchanged (nothing re-dealt, nothing discarded) |
| AC-01 | xUnit (regression guard): `GameHubStartRoundTests.cs`, new `StartRound_from_reveal_phase_still_starts_a_fresh_round` - drive a round to "reveal" (submit every blank), then call `StartRound` again and assert `Ok:true`, exactly like "Play another round" today |
| AC-02 | xUnit: `tests/QuibbleStone.Api.Tests/GameHubJoinTests.cs`, new `JoinRoom_rejects_a_join_while_the_round_is_prompting` and `JoinRoom_rejects_a_join_while_the_round_is_in_reveal` - both assert `Ok:false`, `PlayerCount` unchanged, and no `RosterChanged` broadcast fires |
| AC-02 | xUnit (regression guard): `GameHubJoinTests.cs`, new `JoinRoom_succeeds_again_once_the_round_returns_to_lobby` - call `BackToLobby` then `JoinRoom`, assert `Ok:true` |
| AC-03 | xUnit: new `tests/QuibbleStone.Api.Tests/RoomRegistrySweepTests.cs` (mirroring `RoomRegistryLeaveTests.cs`'s topical-split precedent), built on a `RoomRegistry` constructed with a millisecond-scale test `inactivityWindow`: a room with one connected seat survives the sweep past that window; a room where every seat has been `MarkDisconnected(...)`-ed is still swept past the same window, unchanged from today |
| AC-03 | manual (client fallback - no hook-rendering Vitest harness exists in this repo today): with the API running, get a client into a room, make the room disappear server-side (a short test `inactivityWindow` plus a wait, or simply restarting the API), then trigger any in-room action (submit a word, tap Back to lobby); confirm the client returns to Home with a calm notice rather than a frozen screen |
| manual | a friends-and-family-style walkthrough covering all three at once: a host double-taps Start mid-deal (AC-01), a late phone tries to join mid-round (AC-02), and a long, idle-but-connected session survives past 30 minutes with no room loss (AC-03) |

## Dependencies
- session-engine/01-create-room (the room registry and `TryGet` this story
  hardens)
- session-engine/02-join-with-code (`JoinRoom`, AC-02's target)
- session-engine/07-disconnect-grace-window (the `Player.Connected` flag AC-03
  reads)
- group-play/01-start-round (`StartRound`, AC-01's target - see Context's
  cross-reference note)

All four are already shipped/Complete - this is a hardening pass on existing,
live code, not new construction, so there is nothing here blocking on
in-progress work elsewhere.
