# Feature: Session & Room Engine

## Summary
The SignalR backbone for multiplayer: a host creates a room, players join with a
short code and a nickname, and everyone sees a live roster. Everything in group
play rides on this. Slice 1 shipped the minimum viable version (create / join /
roster) with reconnect-hardening deliberately deferred; stories 07-10 are that
deferred hardening pass, now shipped (PR #151) - "Don't Lose the Room" (README
section 1's "tolerance for brief connectivity drops (dead zones)").

## README reference
README section 7 (Epic Map - Phase 0, Session & Room Engine) and section 8
(Roadmap - Slice 1: "create room, join code, roster; skip reconnect-hardening").
Architecture: section 4 (cloud real-time, SignalR). Reconnect specifically:
section 1 ("the car case is handled by reconnect logic and light caching on top
of the same cloud backbone, not a separate system") and section 6 / CLAUDE.md
section 5 (anonymity - the reconnect handle must never become an off-device
identity). Roadmap: [`docs/ROADMAP.md`](../../ROADMAP.md) "Don't Lose the Room -
reconnect + rejoin" (shipped via PR #151).

## Stories
<!-- Status: Not Started | In Progress | Complete | Blocked | Dropped -->
| Story | Issue | Title | Status |
|---|---|---|---|
| 01 | #20 | Create a room and get a join code | Complete |
| 02 | #21 | Join a room with a code and nickname | Complete |
| 03 | #22 | See a live player roster | Complete |
| 04 | #23 | Copy and share the room code from the Lobby | Complete |
| 05 | #24 | Guardian avatar selection at join | Complete |
| 06 | #115 | Share a join link to the room (deep-link share) | Complete |
| 07 | #141 | Hold the seat: a disconnect grace window | Complete |
| 08 | #142 | Rejoin: reclaim your seat and resume the round | Complete |
| 09 | #143 | Web: remember this seat and rejoin automatically | Complete |
| 10 | #144 | Web: resume the live screen, don't bounce Home | Complete |
| 11 | - | Wire the "+ invite" roster slot to the share action | Complete |
| 12 | #180 | Email a game invite to a friend | Complete |
| 13 | #186 | Round and room lifecycle guards (StartRound / JoinRoom / idle sweep) | Not Started |

## Dependencies
- platform-devops (the real-time backbone must be deployable and reachable).
- child-safety (nicknames are free text and must be filtered before display).
- design-system (Guardian component is used in avatar selection and the roster).

## Design notes
- Real-time runs over the one SignalR hub in `api/` (the skeleton's `GameHub`
  grows room methods; the web client uses the single connection hook in
  `web/src/signalr/`). After a server event, the client updates room state.
- Rooms are **ephemeral session state** (README section 4 - this is a toy, not a
  system of record). No durable persistence is required for Slice 1; a room can
  live in memory / short-lived storage and expire when idle.
- **Identity is anonymous** (README section 3): a player is a nickname + a
  Guardian variant, no account and no PII. The account hooks come later (Phase
  2), but Slice 1 collects nothing about players beyond an in-session nickname
  and chosen avatar variant.
- Join codes are short and human-friendly (easy to read aloud in a car): a few
  characters, no ambiguous glyphs (no O/0, I/1/l). The design spec shows a
  4-character code (e.g. "MOSS") in carved slots on the Join screen and
  prominently on the Lobby. See `docs/design/README.md` Screens 2 and 3.
- The Lobby's share widget (story 04) covers the "different houses" use case
  without requiring accounts: copy or Web Share. Story 06 upgrades that widget's
  payload from the bare code to a tappable `/join/:code` deep link once client
  routing lands (design-system Parked #59), so a recipient taps straight onto a
  pre-filled Join screen instead of retyping the code. Story 06 is the LIVE-room
  share; sharing a FINISHED tale's read-only page is a different surface, owned
  by `keepsake-gallery/04-shareable-tale-link`.
- Reconnect tolerance (the car "dead zone" case) was a Phase-later hardening pass
  through Slice 1 - the seams were kept clean on purpose (`inRoomRef`/`roomCodeRef`
  guard the one connection against race-y broadcasts; the SignalR client already
  runs `withAutomaticReconnect()`). Stories 07-10 are that hardening pass: a
  disconnect **grace window** (hold the seat instead of evicting/aborting
  instantly), a **device-local anonymous reconnect token** that decouples a
  player's identity from its ephemeral SignalR `connectionId`, a **`Rejoin`** hub
  method that rehydrates the returning client's round state, and the **web resume**
  flow that lands the client back on its live screen instead of Home. This is a
  `session-engine` concern (the room/roster/round-state model), not a per-mode
  one - "one engine, many thin modes" (README section 4) still holds; reconnect
  never forks by mode.
- The reconnect token is minted server-side (never client-chosen) and returned
  ONLY to its owning connection in the create/join/rejoin result envelope - it
  must never appear on `RoomStateDto`/`PlayerDto` (which every player in the room
  receives), or another player could use it to hijack a seat. It is scoped to
  exactly one seat in one ephemeral room and is discarded with that room - never
  a cross-room or cross-device identity (README section 6 / CLAUDE.md section 5).

## Parked - Phase 2+
- Device-local remembered profile (name + variant pre-filled on a return visit) is
  now **delivered** via `localStorage` (`web/src/identity.ts`, host-identity work).
  What remains parked is **cross-device identity sync** (the same name + variant on a
  player's other devices), which needs the account/entitlement seam (README section 3,
  monetization) rather than a standalone player store - see #51.
- **Host migration**: when the host leaves mid-session and its own grace window
  (story 07) expires without a reconnect, the room is left exactly as before this
  feature - no remaining player is promoted to host, so nobody can start the next
  round or return to the lobby until someone re-creates a room. Promoting a
  remaining player to host (or otherwise keeping the room startable) instead of
  leaving it hostless - see #50. Deliberately out of scope for stories 07-10 (they
  hold a dropped seat open; they do not reassign a role no one is holding).
- **Reveal-phase reconnect telemetry** (reaction tally + Golden Guardian vote
  status) is not rehydrated by story 08 - a device reconnecting mid-reveal sees
  the shared story again but may see reset reaction/vote counts until the next
  broadcast nudges them. A fast-follow once 07-10 are proven.
- **Multi-device handoff** (the same player resuming on a DIFFERENT device) - the
  reconnect token is deliberately device-local, mirroring `identity.ts`; picking up
  a session on another device is a cross-device identity problem (see the bullet
  above), not this feature.
- "Tales we've carved" local history (design pack Expansion 5) - **delivered**, see
  `keepsake-gallery/03`.

## Decisions
- 2026-07-03: **Reconnect lands as session-engine stories 07-10, not a new feature
  folder.** `feature.md` already named reconnect as this feature's deferred
  hardening and the roadmap files it under `session-engine (deferred)` - the room/
  roster/round-state model this touches is entirely the model stories 01-06 already
  built, so a separate feature would just be an artificial split of the same
  surface (`api/src/Rooms/Room.cs`, `RoomRegistry.cs`, `api/src/Hubs/GameHub.cs`,
  `web/src/signalr/useGameHub.ts`).
- 2026-07-03: **Four stories, an intentionally serial API-then-web chain** (07 grace
  window -> 08 Rejoin hub method -> 09 web token + auto-rejoin -> 10 web resume UI),
  mirroring this feature's own 01-06 chain (each grows the same `GameHub.cs` /
  `useGameHub.ts`, so concurrency is 1 throughout). Each of 07/08 is independently
  testable via xUnit even before the web half exists (07 alone changes the observable
  timing of an abort/eviction; 08 adds a callable-but-not-yet-called hub method);
  09/10 are where a human actually sees the payoff.
- 2026-07-03: **The reconnect token is minted at `CreateRoom`/`JoinRoom` and spent at
  a new `Rejoin(code, token)` hub method** - not a client-generated device id sent at
  join. A server-minted, cryptographically random, single-room-scoped token is harder
  to guess/replay than any client-chosen value and requires no new cross-room index
  (the client already remembers `{code, token}` together, mirroring `identity.ts`'s
  `{nickname, variant}` pattern one level up).
- 2026-07-03: **A disconnect starts a scheduled grace-expiry continuation, not a
  purely lazy sweep.** `RoomRegistry`'s existing 30-minute idle sweep is lazy (runs
  on next access) because nothing needs to happen if nobody touches an abandoned
  room; a 20-60 SECOND grace window is different - the other seated players are
  actively waiting on the dropped seat's blanks, so a scheduled push (evict + abort
  + broadcast) is needed even if nobody else calls the hub in the meantime. This is
  the one place in the codebase a scheduled timer is justified over lazy-on-access.
- 2026-07-03: **Reveal-phase reconnect rehydrates the story but not the reaction/
  vote tallies** (parked above) - keeps story 08 to the core "resume a mid-round
  drop" scenario (README section 8's flagship car scenario) rather than growing
  into a full state-machine rehydration of every reveal-delight surface.

**Open questions for the next build session (not yet decided):**

*(2026-07-07: all three were resolved in the build (PR #151); the questions are kept
below for the record. Grace window: 30 seconds, one named constant -
`SeatGraceService.DefaultGraceWindow` in `api/src/Rooms/SeatGraceService.cs` - with a
test constructor that injects a smaller window. Host drop: the grace window applies
UNIFORMLY (no host special-casing of the window itself), and a follow-up hardening went
further than the original plan - when a host's seat is evicted (grace expiry or a
deliberate leave), `Room.EnsureHostLocked` migrates the host flag to the earliest-joined
still-connected seat and the epilogue nudges that connection with "HostGranted"
(`api/src/Rooms/Room.cs`), so the room never sits hostless/unstartable. Token storage:
exactly ONE device-local `{code, token}` pair under the versioned key `qs.reconnect.v1`,
overwritten on every create/join and shape-validated on load (`web/src/reconnect.ts`) -
the accepted single-slot limitation.)*

- **Grace window length.** No value is picked yet - a starting guess is
  20-30 seconds (a short tunnel/phone-lock blip, not "went inside a store"), but
  this is a product feel call, not a technical one; make it a single named
  constant so it is cheap to tune after playtesting.
- **A mid-round HOST drop**: this plan applies the grace window UNIFORMLY (host or
  not) - the round simply waits out the grace window for ANY dropped seat,
  including the host's, before aborting. An alternative would treat a host drop
  specially (e.g. a shorter grace, or an immediate "waiting for host" state) since
  only the host can `StartRound`/`BackToLobby` again - flag if that special-casing
  is wanted, or if uniform treatment (the simpler, one-engine-shaped default) is
  fine for this slice.
- **Reconnect-token storage granularity**: this plan stores exactly ONE `{code,
  token}` pair (the device's single "current seat"), overwritten on every new
  create/join. If a player could plausibly hold seats in two rooms at once (not a
  goal today), a single-slot store would only remember the most recent - flag if
  that is an acceptable Slice-1 limitation (it matches how `identity.ts` already
  only remembers one name/variant, not a history).
