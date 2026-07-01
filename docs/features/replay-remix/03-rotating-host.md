# Story: Rotating host ("Pass the chisel")

**Feature:** Replay & Remix  ·  **Status:** Not Started  ·  **Issue:** TBD

## Context
Right now one player is always the host - whoever created the room - and the
host is the only one who can start a round or trigger a replay. For a family
playing several rounds, it is nicer when the driving seat moves around: let
the current host hand the role to another player between rounds, so it is not
always the same person tapping the buttons. See [feature.md](./feature.md) and
`docs/features/session-engine/feature.md` (the roster/presence model this
extends) and `docs/features/group-play/01-start-round.md` (the host-only
authorization this story adds a second path to change).

## Acceptance Criteria
- [ ] AC-01: Given I am the current host and the room is between rounds (Lobby
      or Round Complete, never mid-collection), then I see an action to pass
      the host role to another player in the roster (e.g. "Pass the chisel" on
      a player's roster tile).
- [ ] AC-02: Given I choose a player to receive the host role, then the host
      flag moves from me to that player: they gain the crown badge and the
      host-only "Start game" / "Play another round" / "Carve it again"
      capabilities, and I lose them, immediately and for everyone in the room
      in near-real-time over the one SignalR connection.
- [ ] AC-03: Given the host role has just passed, then the roster (Lobby,
      Round Complete crew row) updates for every player to show the crown
      badge on the new host and remove it from the previous host - no refresh
      needed.
- [ ] AC-04: Given I am NOT the current host, then I cannot pass the chisel;
      only the current host can trigger a handoff, and this is enforced
      server-side (not just hidden in the UI) - mirroring how `startRound` is
      already host-checked server-side in `group-play/01`.
- [ ] AC-05: Given the room is mid-round (any phase other than `lobby` or
      `roundComplete`), then the "Pass the chisel" action is not available -
      host handoff is deliberately a between-rounds action only, not a live
      mid-collection handoff.
- [ ] AC-06: Given the host role passes, then no new player data beyond the
      existing anonymous roster (nickname + Guardian variant) is required or
      collected to receive host status - becoming host carries no PII and no
      account, consistent with every player already being anonymous.

## Out of Scope
- Mid-round host handoff (explicitly excluded by AC-05; a dropped host
  mid-round is a reconnect-hardening concern, not this story).
- Host controls beyond what already exist (kicking a player, muting, skipping
  a slow writer) - those remain parked in `group-play/feature.md`'s "Parked -
  Phase 2+" and are untouched by this story.
- Auto-rotation (e.g. "host automatically rotates every round") - this story
  is a manual, deliberate handoff only; auto-rotation is a product idea worth
  a separate decision if it comes up.
- Passing host to a player who is not currently in the room (the target must
  be a live roster entry).
- Any change to how a room is created or who becomes host initially
  (`session-engine/01`'s "room creator is host" rule is unchanged).

## Technical Notes
- Adds a new hub method (e.g. a sibling of the existing room methods in
  `api/src/Hubs/GameHub.cs`, following the same pattern as `CreateRoom` /
  `JoinRoom`: validate, mutate room state under the room's existing lock,
  broadcast) that: (1) verifies the caller's connection is the current host
  (same authorization pattern as `group-play/01`'s `startRound` host check -
  do not invent a second authorization mechanism), (2) verifies the room phase
  is `lobby` or `roundComplete` (AC-05), (3) verifies the target player is a
  current roster member, (4) flips the `IsHost` flag on the room's player
  records, and (5) broadcasts the updated roster via the existing
  `RosterChanged` event (`api/src/Hubs/GameHub.cs`'s existing broadcast
  pattern) - no new broadcast event type needed, since the host flag already
  rides on `PlayerDto.IsHost` in every roster payload.
- Web: extend `web/src/signalr/useGameHub.ts` with the new invoke and reuse the
  existing `RosterChanged` handler (already updates `room` state) - the
  `isHost` boolean the hook already tracks locally (see the existing note in
  `useGameHub.ts`: "tracked from the caller's own action rather than read off
  the roster, because IsHost on a PlayerDto is not tied to a connection on the
  wire") needs to also flip when THIS client is the RECEIVING end of a handoff,
  not just when it calls `createRoom`/`joinRoom`. That is the one nuance worth
  flagging early: the hook currently sets `isHost` only from its own
  create/join call, so this story must add a path where an incoming
  `RosterChanged` can also set `isHost` true/false for the local client if the
  handoff targets/demotes them.
- UI: a small action on a roster tile (Lobby's player grid, or Round Complete's
  crew row) visible only when `isHost` is true and the room phase allows it
  (AC-01, AC-05). Reuses `<Guardian variant size />` tiles and the existing
  crown badge styling already established for the host indicator (Lobby design
  spec: "host = gold HOST chip + a gold crown badge above the avatar + a
  pulsing gold ring").
- No PII implication: the host flag is just a boolean on an already-anonymous
  player record (nickname + variant) - nothing new is collected (AC-06).

## Tests
| AC | Test |
|---|---|
| AC-01 | manual: current host sees "Pass the chisel" on Lobby and Round Complete; non-host does not |
| AC-02 | manual: two browser contexts, host hands off, receiving context gains host-only controls, original host loses them |
| AC-03 | manual: two browser contexts confirm crown badge moves without a refresh |
| AC-04 | manual: a direct hub invoke attempt from a non-host connection is rejected server-side |
| AC-05 | manual: attempt the handoff action mid-round (FillBlank/Waiting phase), confirm the action is unavailable |
| AC-06 | manual: confirm no new input field or data collection appears in the handoff flow |

## Dependencies
- group-play/01-start-round (the host-authorization pattern this mirrors)
- group-play/04-round-complete (one of the two phases a handoff is allowed in)
- session-engine/03-player-roster
- replay-remix/01-carve-it-again (recommended to land first so the
  host-authorization surface this story extends is proven and stable; not a
  hard technical dependency, since this story adds a distinct hub method, but
  both stories touch host-authorization logic in the same hub region)
