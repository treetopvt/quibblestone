# Story: Web - remember this seat and rejoin automatically

**Feature:** Session & Room Engine  ·  **Status:** Not Started  <!-- Not Started | In Progress | Complete | Blocked | Dropped -->  ·  **Issue:** #143

## Context
Story 08 gives the server a way to reclaim a held seat; the web client needs to
actually call it. Two situations both need to trigger the same call: the SAME open
tab's SignalR connection drops and its ALREADY-wired automatic reconnect
(`withAutomaticReconnect`) succeeds (a network blip), and the app is relaunched or
the page reloads while a reconnect handle is still on the device (a phone-lock /
app-restart, arguably the bigger half of README section 1's "brief connectivity
drops (dead zones)" car scenario). This story wires both triggers to the new
`Rejoin` invoke and applies the rehydrated state, mirroring the device-local
convenience pattern `identity.ts` already established for the remembered
nickname/variant. See [feature.md](./feature.md) and
`08-rejoin-and-resume.md` (the hub method this consumes).

## Acceptance Criteria
- [ ] AC-01: Given a player successfully creates or joins a room, then the device
      stores its `{room code, reconnect token}` pair in a NEW, versioned,
      device-local module (mirroring `identity.ts`'s shape/robustness posture),
      overwriting whatever was stored before - a device holds at most one "current
      seat" handle at a time.
- [ ] AC-02: Given the SAME open tab's SignalR connection drops and its automatic
      reconnect succeeds (`onreconnected` fires), when a stored handle exists, then
      the hook automatically invokes `Rejoin(code, token)` on the new connection -
      no user action required - and applies the returned state (room, host flag,
      round phase, remaining blanks, progress, or reveal) into the SAME hook state
      the normal join/round flow already populates.
- [ ] AC-03: Given the app (re)mounts with NO in-memory room but a stored reconnect
      handle exists (a full page reload or app relaunch during a live game), then,
      once the connection reaches `connected`, the hook attempts the SAME `Rejoin`
      automatically - covering "the app was killed and reopened," not just a
      same-tab network blip.
- [ ] AC-04: Given a `Rejoin` attempt fails (unknown/expired token, an evicted
      seat), then the stored handle is discarded immediately (so a later
      successful create/join is never haunted by a stale token), and the client
      falls back to whatever the app already does with no room (story 10 owns the
      exact resulting screen).
- [ ] AC-05: Given a player deliberately leaves a room (`LeaveRoom`/`clearRoom`) or
      returns Home, then the stored reconnect handle is cleared immediately - a
      deliberate exit must never auto-resume later.
- [ ] AC-06 (child safety / no PII): the stored handle is exactly `{code, token}` -
      two opaque strings, no nickname, no name, no cross-room history - matching
      `identity.ts`'s "device-local convenience, never an account" posture; it
      never leaves the device except as the `Rejoin` invoke's own arguments.

## Out of Scope
- Any visible UI change (a "reconnecting..." banner/tile, routing to the resumed
  screen) - story 10.
- Deciding what screen to land on after a successful rejoin - the existing routing
  effect in `App.tsx` already derives the screen from `room`/`round`/`reveal`;
  story 10 verifies/adjusts it end-to-end.
- Retrying a FAILED `Rejoin` attempt (one attempt per trigger is enough for Slice
  1; no backoff loop).

## Technical Notes
- New `web/src/reconnect.ts`: `loadReconnectHandle()` / `saveReconnectHandle(code,
  token)` / `clearReconnectHandle()`, mirroring `identity.ts`'s try/catch-
  everywhere + shape-validation posture (never trust the stored bytes, never a
  non-null assertion) and its OWN versioned key (e.g. `qs.reconnect.v1`) - a
  SEPARATE key from `identity.ts` (nickname/variant is cosmetic pre-fill; this is
  a live capability handle, and the two must not be conflated or cleared
  together).
- `web/src/signalr/useGameHub.ts`: `createRoom`/`joinRoom` call
  `saveReconnectHandle` on success (alongside the existing `room`/`isHost`
  updates). Add an internal `rejoin()` helper that invokes the hub's `Rejoin` and
  wire it to BOTH the EXISTING `connection.onreconnected(...)` handler (extend it
  in place - do not register a second) and a one-shot mount-time effect
  ("connected AND no room AND a stored handle exists"). On success, apply the
  rehydrated fields into the SAME setters the existing flows already use
  (`setRoom`, `setIsHost`, `setRound`, `setAssignedBlankIndices`,
  `setCollectProgress`, `setReveal`) - no new parallel state. On failure, call
  `clearReconnectHandle()`. `clearRoom` (already invoked on deliberate leave/Home)
  also calls `clearReconnectHandle()` (AC-05).
- Guard against a double-fire using the SAME `inRoomRef`/`roomCodeRef` pattern
  already in this file for exactly this class of race (see its own "post-leave
  re-entry bug" comment) - do not invent a second guard.
- No new `HubConnection` - this rides the one connection `useGameHub` already
  owns (README section 4 / CLAUDE.md section 4).

## Tests
| AC | Test |
|---|---|
| AC-01, AC-06 | Vitest: `reconnect.ts`'s load/save/clear round-trips a valid `{code, token}` handle and rejects a malformed/corrupt one - follow `web/src/content/playedHistory.test.ts`'s pattern (node env, a tiny in-memory `Storage`-shaped fake stubbed onto `globalThis.window`, since there is no real DOM in the Vitest config) |
| AC-02 | manual / dev-server: force a brief connection drop (e.g. restart the local API mid-session) and confirm the client auto-rejoins with no user action once it is back within the grace window |
| AC-03 | manual: reload the page mid-round with a stored handle present; confirm a `Rejoin` attempt fires once connected |
| AC-04 | xUnit-adjacent manual / Vitest: a rejected `Rejoin` response clears the stored handle (assert `loadReconnectHandle()` returns null afterward) |
| AC-05 | Vitest / manual: `clearRoom()` (Leave / Home) clears the handle |
| Full user-visible confirmation | rides with story 10's Playwright walkthrough |

## Dependencies
- session-engine/08-rejoin-and-resume
