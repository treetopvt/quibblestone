# Story: Hold the seat - a disconnect grace window

**Feature:** Session & Room Engine  ·  **Status:** Not Started  <!-- Not Started | In Progress | Complete | Blocked | Dropped -->  ·  **Issue:** #141

## Context
Today a dropped connection is treated as a *leave*: `GameHub.OnDisconnectedAsync`
calls `RoomRegistry.RemoveConnection` immediately, which evicts the seat from the
roster, and if a round is mid-collection ("prompting"), `HandlePlayerLeftAsync`
aborts the whole round for everyone (`RoundAborted`, back to the lobby). A car dead
zone, a phone lock, or a brief network blip is a TRANSIENT drop, not a deliberate
departure - README section 1 calls this out by name ("tolerance for brief
connectivity drops (dead zones)"), and section 8's flagship scenario is a family in
a car. This story is the foundation the rest of "Don't Lose the Room" builds on:
hold a dropped seat open for a short grace window instead of evicting/aborting
instantly, and mint the reconnect handle (an opaque, server-generated token,
returned only to its owner) that story 08's `Rejoin` will spend to reclaim it. A
deliberate `LeaveRoom` is unaffected - it still evicts immediately, exactly as
today. See [feature.md](./feature.md) (Decisions log) and `docs/ROADMAP.md`
("Don't Lose the Room - reconnect + rejoin").

## Acceptance Criteria
- [ ] AC-01: Given a player is seated in a live room and their connection drops
      abnormally (`OnDisconnectedAsync` fires - not a deliberate `LeaveRoom`), when
      the drop is detected, then the seat is marked disconnected rather than
      removed - the roster still reports the seat present (now flagged
      not-connected) and the room's player count is unchanged.
- [ ] AC-02: Given the disconnected seat is holding and the room's current round is
      "prompting", then the round is NOT aborted while the grace window is running -
      the other seated players keep collecting words normally, and the disconnected
      seat's still-outstanding blanks simply remain unsubmitted for now.
- [ ] AC-03: Given the grace window elapses with no reconnect for that seat, then
      the seat is evicted exactly as it was before this story (the roster drops it,
      `RosterChanged` broadcasts the trimmed roster), and if the round is still
      "prompting" at that point, it aborts with the existing friendly `RoundAborted`
      notice - i.e. the eventual end state matches today's behavior, only deferred by
      the grace window.
- [ ] AC-04: Given a player deliberately leaves (`LeaveRoom`), then their seat is
      evicted immediately with no grace window - grace applies only to an unplanned
      drop, never an intentional leave.
- [ ] AC-05: Given every seated player's grace has expired (or they left
      deliberately) and the roster is empty, then the room is freed exactly as
      today - a pending grace timer never keeps an abandoned room alive past that.
- [ ] AC-06 (child safety / seat-hijack guard): Given a seat is created
      (`CreateRoom` or `JoinRoom`), then the server mints an opaque,
      cryptographically random reconnect token for that seat and returns it ONLY to
      the owning connection in that call's own result envelope - it is NEVER
      included in the roster shape (`RoomStateDto`/`PlayerDto`) broadcast to the
      whole room, so no other player can ever see or use it to hijack the seat.
- [ ] AC-07 (child safety / no PII): the reconnect token carries no nickname, no
      device fingerprint, and no cross-room identity - it is a random opaque value
      scoped to exactly one seat in one ephemeral room, discarded when that seat is
      gone (evicted, or the room expires), and is never used to correlate a player
      across rooms or devices.

## Out of Scope
- Actually spending the token to reclaim a seat (the `Rejoin` hub method) - that is
  story 08; this story only mints the token and holds the seat.
- Any client-visible behavior (a "reconnecting" tile, a resume flow) - the web
  client does not consume any of this yet; see stories 09 and 10.
- Host migration: if the HOST's grace expires without a reconnect, the room is left
  exactly as it is today (no host to start/`BackToLobby` again) - parked, see
  feature.md.
- Persisting seat/grace state beyond process memory - still fully ephemeral, no DB
  (CLAUDE.md section 10).
- Picking the final grace-window length (see feature.md's open questions) - ship a
  single named constant so it is cheap to tune after playtesting.

## Technical Notes
- `api/src/Rooms/Room.cs`: `Player` is an immutable `record` keyed by
  `ConnectionId`. "Mark disconnected" rebuilds the roster entry under the
  existing `_gate` lock (remove + re-add), the same pattern `RecordSubmission`
  already uses to swap in a fresh dictionary - it does not mutate a field in
  place. Track a per-seat `ReconnectToken` and a disconnected-since timestamp
  (or an explicit connected/disconnected marker); do NOT remove the seat from
  `_players` on a transient drop (that would break `PlayerCount`, `IsHost`, and
  the round's `Assignments`/`Submissions`, all of which key off the seat still
  being present).
- The grace-expiry notification must be a PROACTIVE push (evict + possibly abort
  + broadcast), not a lazy "recompute on next read": unlike
  `RoomRegistry.SweepExpired`'s lazy 30-minute idle sweep (fine because nothing
  needs to happen if nobody touches an abandoned room), a 20-60 SECOND grace
  window has other seated players actively waiting on the dropped seat's blanks -
  if nobody else calls the hub in the interim, they would otherwise wait forever.
  Schedule ONE fire-and-forget delayed eviction from `OnDisconnectedAsync` as an
  `async` task: `await Task.Delay(graceWindow, ct)` under a per-seat
  `CancellationTokenSource`, then re-check under the room's `_gate` (the same
  disconnect episode / token is still pending) before evicting - so a reconnect
  that lands in between (story 08's `Rejoin`) cancels the token rather than
  evicting a fresh connection, and any fault is caught + logged, never unobserved.
  Deliberately NOT `Task.Delay(...).ContinueWith(...)` - ContinueWith's default
  scheduler + unobserved-exception semantics are a footgun for a fire-and-forget
  timer. This is the one place in this codebase a scheduled timer is justified
  over lazy-on-access.
- `api/src/Rooms/RoomRegistry.cs`: `OnDisconnectedAsync`'s call swaps from the
  existing `RemoveConnection` (still used, unchanged, by `LeaveRoom`'s immediate
  path - AC-04) to a new mark-disconnected path that returns enough information
  to know whether to schedule the grace timer (only when the connection actually
  owned a seat).
- `api/src/Hubs/GameHub.cs`: `CreateRoomResultDto` / `JoinResultDto` gain a
  `ReconnectToken` field, returned ONLY to the caller (AC-06). `PlayerDto` gains a
  `Connected` bool (consumed by web story 10's roster tile; not rendered by
  anything until then). `OnDisconnectedAsync` no longer calls
  `HandlePlayerLeftAsync` synchronously for an abnormal close - it marks the seat
  disconnected and schedules the grace-expiry epilogue, which (on actual expiry)
  performs the SAME eviction + conditional `RoundAborted` + `RosterChanged`
  broadcast this story preserves as the eventual outcome (AC-03). `LeaveRoom`'s
  immediate path is UNCHANGED (AC-04).
- Suggested grace window: 20-30 seconds as a starting point (a short car-tunnel /
  phone-lock blip, not "went inside a store") - flagged as an open decision in
  feature.md; make it a single named constant for easy tuning.
- Optional, non-blocking: reuse `platform-devops/04`'s `_appInsights` pipeline to
  fire anonymous `HubGraceStarted` / `HubGraceExpired` events (no room/nickname
  payload), mirroring the existing `HubAbnormalDisconnect` event - useful signal
  later, but not a hard AC for this story.
- `GameHub`'s constructor now also takes `IEntitlementService` (ai-cost-gate/02,
  already merged, unrelated to this story) - any new/edited test builder
  constructing a `GameHub` needs that extra argument.

## Tests
| AC | Test |
|---|---|
| AC-01, AC-02 | `tests/QuibbleStone.Api.Tests/GameHubDisconnectTests.cs` (extend): an abnormal disconnect during a "prompting" round does not immediately abort/evict - the roster still reports the seat, `PlayerCount` unchanged, no `RoundAborted` fires synchronously |
| AC-03 | xUnit with a short, test-configured grace window (or an injectable clock): after the window elapses with no reconnect, the seat is evicted (`RosterChanged`) and, if still "prompting", `RoundAborted` fires - matching pre-story behavior, just deferred |
| AC-04 | `tests/QuibbleStone.Api.Tests/RoomRegistryLeaveTests.cs` / `GameHubDisconnectTests.cs`: `LeaveRoom` still evicts immediately with no grace, even mid-round |
| AC-05 | xUnit: the room frees (`ActiveRoomCount` -> 0) once every seat is evicted after grace, not before |
| AC-06 | xUnit: inspect `CreateRoomResultDto`/`JoinResultDto` for the token field, and `RoomStateDto`/`PlayerDto` to confirm it is absent from the broadcast shape |
| AC-07 | code review: the token is a random opaque value with no nickname/fingerprint encoded, scoped to one room, discarded on eviction/expiry |
| manual | pull a device offline for a short beat during a round; confirm the round does not immediately abort (a stand-in for the full E2E, which lands with story 10) |

## Dependencies
- session-engine/01-create-room
- session-engine/02-join-with-code
- session-engine/03-player-roster
