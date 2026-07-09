// ----------------------------------------------------------------------------
//  useGameHub - React hook that owns the SignalR connection to the API's
//  GameHub. It is the web client's half of the real-time walking skeleton.
//
//  Responsibilities:
//    - Build and start ONE HubConnection (with automatic reconnect).
//    - Expose the live connection status so the UI can show connected / not.
//    - Own the current room state and the game invokes (createRoom, joinRoom,
//      clearRoom) plus the single "RosterChanged" handler.
//
//  Real game features (rooms, rosters, reveal) add more invokes/handlers on
//  this same connection rather than opening new ones.
//
//  session-engine/01 adds the first game invoke, createRoom, which asks the
//  hub's CreateRoom method to mint a room and returns the created room's state
//  (the join code + the roster with the host). build/host-identity gives the host
//  a display name + Guardian variant (picked on HostSetup and safety-filtered
//  server-side), so createRoom now takes (displayName, variant) and returns a
//  CreateRoomResult envelope (ok + room + friendly error) mirroring joinRoom. The
//  Player / RoomState / CreateRoomResult types below MIRROR the hub's PlayerDto /
//  RoomStateDto / CreateRoomResultDto wire contract (api/src/Hubs/GameHub.cs) -
//  keep them in sync.
//
//  session-engine/02 adds joinRoom (join an existing room by code + display
//  name, returning a JoinResult envelope) and, crucially, moves the LIVE room
//  state INTO this hook so roster updates flow through the ONE connection:
//  the hook registers a single "RosterChanged" handler (server broadcasts the
//  updated roster to a room's group when anyone joins) and exposes the current
//  `room`, so both the host and existing players see a newcomer appear in
//  near-real-time. App reads `room` from here instead of holding its own copy.
//
//  group-play/01 adds the round START seam on the SAME connection: startRound
//  (a host-only invoke - the server enforces the host check) and a single
//  "RoundStarted" handler that the server broadcasts to the whole room group so
//  every player transitions into word collection together. The hook exposes the
//  current `round` (the template id + mode + round number the client resolves
//  full content from) so App can route into the round. StartRoundResult /
//  RoundInfo below MIRROR the hub's StartRoundResultDto / RoundStartedDto wire
//  contract (api/src/Hubs/GameHub.cs) - keep them in sync BY HAND (no codegen).
//
//  group-play/02 adds one more handler on the SAME connection: "YourBlanks", a
//  PER-CONNECTION message the hub sends right after RoundStarted telling THIS
//  client only its own blank indices for the round (never another player's, no
//  PII). The hook exposes `assignedBlankIndices` (null until it arrives, cleared
//  on leave / round reset) so GroupRound fills only the blanks this player owes.
//  The YourBlanks type MIRRORS the hub's YourBlanksDto - keep it in sync BY HAND.
//
//  group-play/03 closes the real-time loop on the SAME connection: (a) submitWord
//  (an invoke that submits ONE word for ONE assigned blank; the server runs the
//  safety filter FIRST and records only on pass, so this maps 1:1 to FillBlank's
//  onSubmitWord accepted/message contract), (b) a "CollectProgress" handler ->
//  `collectProgress` (the "[N] of [M] done" counts + per-player done/writing list
//  the Waiting screen renders; NO submitted words, AC-01), and (c) a "RevealReady"
//  handler -> `reveal` (the ordered reveal words that route EVERY player to the
//  shared Reveal in near-real-time, AC-05 - the client resolves the template + does
//  assembly LOCALLY via the web engine; the server never assembles). The three new
//  types MIRROR the hub's SubmitWordResultDto / CollectProgressDto / RevealReadyDto
//  wire contracts - keep them in sync BY HAND (no codegen). All three states clear
//  on leave and reset on a fresh RoundStarted so a prior round never bleeds through.
//
//  group-play/04 adds the replay-loop seam on the SAME connection: (a) backToLobby
//  (a host-only invoke - the server enforces the host check - that ends the round
//  and returns EVERYONE to the lobby, same room + roster) and (b) a bare
//  "BackToLobby" handler that CLEARS round + reveal + collectProgress +
//  assignedBlankIndices so every client falls back to the Lobby it still holds
//  (room + roster stay set). "Play another round" needs no new invoke - it is just
//  startRound again on the same room; the server increments the round number and the
//  existing RoundStarted broadcast resets reveal + routes everyone into the new round.
//  NOTE: `round` is deliberately kept set THROUGH the reveal (RevealReady does NOT
//  clear it) so Round Complete can read round.roundNumber for its "ROUND N CARVED"
//  badge; round is cleared only on leave or on BackToLobby.
//
//  story-selection/02 adds ONE MORE PARAMETER to the EXISTING startRound invoke
//  (never a new hub method): the host's story-length choice ('quick' | 'full' |
//  'any', ../content/length.ts) travels alongside familySafe on the SAME
//  StartRound call, so the wire contract is now StartRound(code, familySafe,
//  lengthPref). The server enforces it authoritatively (same posture as the
//  family-safe gate) - keep this WIRE CONTRACT in sync BY HAND with
//  api/src/Hubs/GameHub.cs's StartRound signature.
//
//  story-selection/06 adds ONE MORE OPTIONAL PARAMETER to the SAME startRound
//  invoke (still never a new hub method): an explicit `templateId` for the
//  "play a favorite" seam (AC-03/AC-04). When supplied and non-empty, the
//  server plays that EXACT template, bypassing the length + freshness stages
//  entirely (a favorite is an explicit replay, not a random pick, and never
//  re-stamps freshness history) while STILL running the family-safe gate FIRST
//  (AC-06) - so a favorite that is not family-safe is still rejected in a
//  family-safe session. The wire contract is now StartRound(code, familySafe,
//  lengthPref, templateId). Omitting it (or passing undefined, which this hook
//  maps to `null` on the wire) reproduces today's random-pick behavior exactly
//  - every existing 3-argument caller keeps working unchanged.
//
//  session-engine/07 mints a caller-only reconnect token in CreateRoom/JoinRoom's
//  own result envelope (a `reconnectToken` field, never broadcast to the rest of
//  the room) and holds a dropped seat open for a grace window instead of evicting
//  it right away. session-engine/08 adds the `Rejoin(code, token)` hub method that
//  spends that token to reclaim the seat under a new connection and returns a
//  rehydration envelope. session-engine/09 (this file's latest addition) wires the
//  WEB half up: `../reconnect.ts` remembers the `{code, token}` handle
//  device-locally (saved on every successful createRoom/joinRoom, cleared on
//  clearRoom); an internal `rejoin()` helper invokes the hub's Rejoin and applies
//  a success into the SAME setters the normal join/round flow already populates -
//  no new parallel state tree. It is wired to BOTH the EXISTING
//  `connection.onreconnected(...)` handler (a same-tab network blip, AC-02) and a
//  new one-shot mount-time effect ("connected AND no in-memory room AND a stored
//  handle exists", AC-03 - covers a full page reload / app relaunch, not just a
//  same-tab blip). A rejected Rejoin discards the stored handle (AC-04). The hook
//  exposes `isRejoining` so story 10 can hold the live-route guards open while a
//  rejoin is in flight, instead of bouncing the player Home first.
//
//  session-engine/10 (web, presentation only - no change to this file's rejoin
//  MECHANICS) adds the `connected` flag to `Player` below (mirroring the hub's
//  `PlayerDto.Connected` from story 07, already riding along on every roster
//  broadcast unused until now) and combines `isRejoining` with `status` +
//  `../reconnect.ts`'s `loadReconnectHandle()` in `App.tsx` to decide whether a
//  live-route guard should show a calm "reconnecting..." beat instead of
//  bouncing Home - see App.tsx's `shouldHoldLiveRouteForResume`.
//
//  replay-remix/03 adds `passHost` (a host-only, phase-gated invoke - "Pass the
//  chisel" - that the SERVER rejects for a non-host caller or a mid-round
//  "prompting" phase, mirroring startRound's posture) on the SAME connection. It
//  reuses the EXISTING "RosterChanged" handler for its live effect (no new event
//  type - the moved IsHost flag already rides on every PlayerDto), but that
//  exposed the ONE nuance this story had to close: `isHost` was, until now, set
//  ONLY from this client's OWN createRoom/joinRoom action (see the comment on
//  `isHost` below) - so a client that RECEIVES a handoff (or loses the role) had
//  no path to learn it. `handleRosterChanged` now ALSO re-derives `isHost` for
//  THIS client by matching the incoming roster against `myNicknameRef` - the one
//  identity handle this client already holds locally (the nickname it created/
//  joined the room with). Nicknames are enforced unique within a room, case-
//  insensitively, at join (session-engine/02, AC-06) and never change after, so
//  this match stays valid for the room's whole lifetime. This IS a slightly
//  fragile handle (a wire-level connection/session id would be more robust, but
//  that would mean putting connection identity on PlayerDto, which the file's
//  own existing comment on `isHost` explains we deliberately do NOT do, for no-
//  PII reasons) - flagged here rather than inventing a new wire field.
//
//  Alpha-gate hardening (pre-friends-and-family audit) adds two resilience fixes
//  on this SAME connection, no parallel state machine:
//    - B1: `withAutomaticReconnect()`'s default policy retries a dropped connection
//      a few times (~0/2/10/30s, each delay now jittered per-client - see
//      `withReconnectJitter`) then permanently gives up and fires `onclose`, and
//      the very first `connection.start()` below was a single attempt with no retry
//      of its own (a cold app-service start, or opening the PWA before wifi
//      associates, could both fail outright). Either terminal case now falls into a
//      manual reconnect loop (`manualReconnectDelayMs` below is its pure backoff
//      schedule - 2s, 5s, 10s, 30s, then repeating at 30s, spread per-client by
//      `withReconnectJitter` so a mass reconnect does not stampede the hub in
//      lockstep - see docs/load-testing/findings.md) that keeps calling
//      `connection.start()` again while the app is foregrounded, plus an immediate
//      extra attempt on `document.visibilitychange` (back to `'visible'`) and
//      `window.online` - both realistic "phone was locked/out of signal, now isn't"
//      triggers. A successful manual restart is treated exactly like `onreconnected`
//      (status -> connected, then `rejoin()`, a no-op with no held seat). `status`
//      gains no new value for this: `'disconnected'` now doubles as "not connected,
//      and the hook is quietly retrying in the background" - Home reads it to show
//      legible copy plus a manual `retryConnection` (exposed below) instead of the
//      old silent, unexplained disabled CTA.
//    - B4: a REJECTED `Rejoin` (an expired token / an already-evicted seat) used to
//      discard only the stored `{code, token}` handle, leaving `room`/`round`/
//      `reveal` and "am I in a room" set to their stale pre-drop values - the screen
//      then froze forever (no more broadcasts arrive; this connection was never
//      re-added to the room's SignalR group). It now ALSO resets local state back to
//      "not in a room" via `resetRoomState` (the SAME helper `clearRoom` uses for a
//      deliberate Leave), guarded by re-checking the stored handle - the file's
//      existing staleness signal - so a rejection that resolves late never clobbers
//      a newer room that already landed. It also sets `rejoinFailedNotice` (exposed
//      below) so Home can explain the bounce instead of leaving it silent.
//
//  accounts-identity/09 extends the SAME accessTokenFactory (story 06) to also
//  cover a kid's device that is never signed in (README section 6): it now
//  prefers a live signed-in purchaser credential if present, ELSE falls back to
//  a stored family-device token (../account/familyDeviceToken.ts - deliberately
//  localStorage, surviving app restarts on the kid's own device, unlike the
//  in-memory PurchaserSession), ELSE supplies nothing (anonymous, unchanged free
//  play). `familyDeviceTokenRef` mirrors the CURRENT token the SAME way
//  `purchaserCredentialRef` mirrors the purchaser credential - read by the
//  factory getter on every (re)connection, never forcing one. Once per app
//  launch (a mount-time effect, not per hub reconnect), when a device token is
//  present and no purchaser is signed in, this hook calls the companion REST
//  refresh endpoint (`../account/deviceRedeemClient.ts`) to keep the token's
//  rolling TTL alive: on success the rotated replacement is persisted and
//  mirrored into the ref (picked up by the very next connect/reconnect, no
//  forced reconnect of its own); on `{ ok: false }` (a revoked/expired/unknown
//  token) the stored token is cleared, so the device falls back to anonymous
//  exactly as if it had never been linked - never a hard failure blocking play.
// ----------------------------------------------------------------------------

import { useCallback, useEffect, useRef, useState } from 'react';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import type { LengthPreference } from '../content/length';
import type { ReactionCounts, ReactionType } from '../components/ReactionRow';
import { clearReconnectHandle, loadReconnectHandle, saveReconnectHandle } from '../reconnect';
import { usePurchaserSession } from '../account/PurchaserSession';
import {
  clearFamilyDeviceToken,
  loadFamilyDeviceToken,
  saveFamilyDeviceToken,
} from '../account/familyDeviceToken';
import { refreshFamilyDeviceToken } from '../account/deviceRedeemClient';

const HUB_URL = import.meta.env.VITE_SIGNALR_HUB_URL;

// reveal-delight/01 (AC-04): a fresh all-zero reaction tally. Reaction counts are
// EPHEMERAL per reveal (Out of Scope: no persistence), so this is both the initial
// value and what the hook resets to whenever a new round starts / the reveal clears.
const ZERO_REACTIONS: ReactionCounts = { love: 0, wow: 0, nope: 0 };

/**
 * session-engine/13 (AC-03/W1): the exact "unknown or expired room code"
 * string the hub's `RoomNotFoundMessage` const returns (api/src/Hubs/GameHub.cs)
 * whenever an in-room call looks a room up by code and it is gone (the idle
 * sweep evicted it, or - belt and suspenders - any other code-lookup miss).
 * Mirrored here so {@link isRoomNotFoundError} can recognize it reliably. A
 * plain string comparison is admittedly a little brittle to a future copy
 * change on the server - acceptable for this story's scope; a structured
 * error code is the fuller fix if this pattern grows.
 */
const ROOM_NOT_FOUND_MESSAGE =
  "We couldn't find a game with that code - double-check and try again.";

/**
 * session-engine/13 (AC-03/W1): true when a room-scoped call's error is the
 * server's "we couldn't find a game with that code" message - the signal that
 * the room the client believed it was still in is gone server-side (most
 * often the 30-minute idle sweep, since a still-connected seat now exempts a
 * room from it). Pure and exported so it is unit-testable without a live
 * HubConnection, alongside this file's other small pure helpers
 * (`manualReconnectDelayMs`, `withReconnectJitter`). Null (no error, or a call
 * that never returns one) is never a match.
 */
export function isRoomNotFoundError(error: string | null): boolean {
  return error === ROOM_NOT_FOUND_MESSAGE;
}

/**
 * B1 (alpha-gate hardening): the manual reconnect loop's backoff schedule, in
 * milliseconds, once the automatic-reconnect policy has given up (`onclose`)
 * or the very first `connection.start()` fails outright. `attempt` is 0-based
 * (0 = the first manual retry); attempts past the schedule's end repeat at the
 * last (30s) value forever - the loop never truly gives up while the app stays
 * foregrounded. Pure and exported so it is unit-testable without a live
 * HubConnection (this file has no React-render test harness - see
 * App.test.ts's header note for the same posture).
 */
export function manualReconnectDelayMs(attempt: number): number {
  const scheduleSeconds = [2, 5, 10, 30];
  const index = Math.min(Math.max(attempt, 0), scheduleSeconds.length - 1);
  return scheduleSeconds[index] * 1000;
}

/**
 * Equal-jitter transform (the AWS "equal jitter" recipe) over a base backoff:
 * a random point in [base/2, base]. Bounded on BOTH ends on purpose - never
 * longer than `baseMs` (so jitter can never regress the worst-case recovery
 * time or creep toward the seat-grace window) and never shorter than half (so
 * it cannot collapse into a tight retry loop).
 *
 * Why jitter at all: after a hub restart or a shared-network blip, every
 * client's reconnect fires on the SAME fixed schedule and stampedes the connect
 * path in lockstep - the thundering herd that pegged the UAT B1 to ~98-99% CPU
 * in the load test (docs/load-testing/findings.md, "Ceiling probe"). Spreading
 * each client across a per-client window smears that arrival instead of spiking
 * it, at zero cost to a lone reconnecting client.
 *
 * `random` is injected (default `Math.random`) ONLY so tests are deterministic;
 * production always uses `Math.random`. A base of 0 (SignalR's immediate first
 * auto-reconnect attempt) stays 0 - the fast first retry is left intact.
 */
export function withReconnectJitter(baseMs: number, random: () => number = Math.random): number {
  if (baseMs <= 0) return 0;
  const half = baseMs / 2;
  return Math.round(half + random() * half);
}

/**
 * The manual reconnect loop's actual wait: `manualReconnectDelayMs`'s fixed
 * 2s/5s/10s/30s schedule spread per-client by {@link withReconnectJitter}. The
 * bare schedule stays exported as the pre-jitter reference (and its own test).
 */
export function jitteredManualReconnectDelayMs(
  attempt: number,
  random: () => number = Math.random,
): number {
  return withReconnectJitter(manualReconnectDelayMs(attempt), random);
}

/**
 * SignalR's DEFAULT automatic-reconnect delays (retry at 0, 2s, 10s, 30s of
 * elapsed downtime, then give up). Made explicit so {@link jitteredAutoReconnectDelayMs}
 * can jitter each delay while reproducing the schedule's LENGTH exactly.
 */
export const AUTO_RECONNECT_BASE_MS: readonly number[] = [0, 2000, 10000, 30000];

/**
 * The jittered next-delay for `withAutomaticReconnect`. Mirrors the default
 * four-attempt schedule ({@link AUTO_RECONNECT_BASE_MS}) with per-client jitter,
 * and returns `null` once the schedule is exhausted so SignalR gives up on
 * EXACTLY the same attempt it does today - preserving the give-up -> `onclose`
 * -> manual-loop handoff the B1 hardening relies on. `previousRetryCount` is
 * SignalR's 0-based count of retries already made (from its `RetryContext`).
 */
export function jitteredAutoReconnectDelayMs(
  previousRetryCount: number,
  random: () => number = Math.random,
): number | null {
  if (previousRetryCount < 0 || previousRetryCount >= AUTO_RECONNECT_BASE_MS.length) {
    return null;
  }
  return withReconnectJitter(AUTO_RECONNECT_BASE_MS[previousRetryCount], random);
}

/**
 * B1 follow-up (connect-hang fix, 2026-07-07): the narrow shape `startWithTimeout`
 * needs from a `HubConnection` - just `start`/`stop` - so a test can pass a plain
 * mock instead of a real connection (no `any`, no casting).
 */
export interface StartStoppable {
  start(): Promise<void>;
  stop(): Promise<void>;
}

/** How long the initial connect (and each manual retry) waits before treating a still-pending `start()` as hung. A single named constant, easy to tune - see `startWithTimeout`'s header for why it exists. */
export const CONNECT_TIMEOUT_MS = 20_000;

/**
 * B1 follow-up (connect-hang fix, 2026-07-07): races `connection.start()`
 * against a timeout. A hung negotiate/handshake - one that never resolves NOR
 * rejects, which a healthy local dev server never triggers but a real network
 * condition against the live hub can - otherwise leaves `connection.state`
 * wedged at `Connecting` forever. Every retry path in this file checks exactly
 * that state before acting (`attemptManualReconnect`'s own guard, below), so a
 * hang does not just delay reconnection - it permanently blocks the timer
 * loop, the visibility/online listeners, AND the user's own "Try again" tap.
 * The only escape was a full page reload (which throws the wedged
 * `HubConnection` away for a fresh one), and even that would hit the same wall
 * again if the underlying condition persisted.
 *
 * On timeout this calls `connection.stop()` - SignalR's documented way to
 * abort a connection attempt in progress, safe to call from any state - which
 * returns the connection to `Disconnected` so the normal retry/backoff path
 * below can take over instead of staying wedged. Both branches of the
 * original `start()` are handled via `.then(onFulfilled, onRejected)` (not a
 * separate `.catch`), so a late settlement after the timeout already fired is
 * never an unhandled rejection - the `settled` guard just makes it a no-op.
 */
export function startWithTimeout(connection: StartStoppable, timeoutMs: number): Promise<void> {
  return new Promise((resolve, reject) => {
    let settled = false;
    const timer = setTimeout(() => {
      if (settled) return;
      settled = true;
      void connection.stop().catch(() => {});
      reject(new Error('SignalR connect timed out'));
    }, timeoutMs);

    connection.start().then(
      () => {
        if (settled) return;
        settled = true;
        clearTimeout(timer);
        resolve();
      },
      (err: unknown) => {
        if (settled) return;
        settled = true;
        clearTimeout(timer);
        reject(err);
      },
    );
  });
}

/**
 * `'disconnected'` covers BOTH "never connected yet" and, since the B1 fix,
 * "was connected, dropped, and the hook's manual reconnect loop is quietly
 * retrying in the background" - there is deliberately no separate "gave up
 * for good" value (the loop never truly stops while the app is foregrounded).
 * `'connecting'` covers the very first connect and an automatic-reconnect
 * attempt in progress.
 */
export type ConnectionStatus = 'connecting' | 'connected' | 'disconnected';

/**
 * One player in a room, as sent by the hub (PlayerDto). Anonymous by design -
 * an in-session nickname (empty for the host until story 02 adds a name step),
 * a Guardian variant, and whether they are the host. No PII (README section 6).
 */
export interface Player {
  nickname: string;
  variant: string;
  isHost: boolean;
  /**
   * session-engine/07 (server) marks a seat's Connected flag false while it is
   * held through a disconnect grace window (a dropped connection, not a
   * deliberate leave - a deliberate leave removes the seat entirely rather than
   * flipping this), true otherwise. Mirrors the hub's `PlayerDto.Connected`.
   * session-engine/10 renders a dimmed/pulsing "reconnecting..." tile for a
   * seat with `connected === false` instead of the ordinary READY/HOST chip.
   */
  connected: boolean;
}

/**
 * The state of a room as returned by createRoom (RoomStateDto): the join code
 * plus the current roster (host first). Story 03 broadcasts this same shape on
 * roster changes.
 */
export interface RoomState {
  code: string;
  players: Player[];
}

/**
 * The result of joining a room (JoinResultDto), mirroring the hub's wire
 * contract. On a rejected join (unknown/expired code, an empty/too-long name, a
 * name the safety filter blocks, or a name already taken in the room), ok is
 * false and error carries a friendly, kid-readable message to show inline. On
 * success, ok is true and room is the updated roster.
 */
export interface JoinResult {
  ok: boolean;
  room: RoomState | null;
  error: string | null;
  /**
   * session-engine/07 (AC-06): this caller's own opaque, server-minted
   * reconnect handle on success (null on failure). Returned ONLY here - never
   * broadcast on RoomState/Player - so no other player can see or spend it.
   * session-engine/09 saves it via `saveReconnectHandle` alongside `room`.
   */
  reconnectToken: string | null;
}

/**
 * The result of creating a room as the host (CreateRoomResultDto, build/host-identity),
 * mirroring the hub's wire contract. The host now supplies a display name + Guardian
 * variant (picked on HostSetup), so createRoom can fail the SAME way a join can: on a
 * rejected create (an empty/too-long name, or a name the safety filter blocks) ok is
 * false and error carries a friendly, kid-readable message to show inline. On success
 * ok is true and room is the minted room's state (code + roster with the host).
 */
export interface CreateRoomResult {
  ok: boolean;
  room: RoomState | null;
  error: string | null;
  /**
   * session-engine/07 (AC-06): this host's own opaque, server-minted reconnect
   * handle on success (null on failure). Returned ONLY here - never broadcast
   * on RoomState/Player. session-engine/09 saves it via `saveReconnectHandle`
   * alongside `room`.
   */
  reconnectToken: string | null;
}

/**
 * The result of the host starting a round (group-play/01), mirroring the hub's
 * StartRoundResultDto wire contract. On a rejected start (unknown/expired code,
 * the caller is not the host, too few players, or no template available) ok is
 * false and error carries a friendly, kid-readable message to show inline. On
 * success ok is true and error is null - the actual round detail arrives for
 * EVERY player via the RoundStarted broadcast, not through this envelope.
 */
export interface StartRoundResult {
  ok: boolean;
  error: string | null;
}

/**
 * The current round as broadcast by the hub's RoundStarted event
 * (RoundStartedDto). Carries ONLY the selected template's id (the client
 * resolves the full prose/body from its bundled seedLibrary BY ID - the server
 * never ships template content), the mode the HOST chose (group-play/05: one of
 * the offered ids, resolved through the shared mode registry to render the right
 * surfaces - it was pinned to "classic-blind" through Slice 1), and the 1-based
 * round number. group-play/02 adds a separate per-connection message for each
 * player's own blank assignments.
 */
export interface RoundInfo {
  templateId: string;
  mode: string;
  roundNumber: number;
  /**
   * reveal-delight/03 (AC-04): the nickname wearing the Golden Guardian crown for
   * THIS round (the previous round's funniest-word winner), or null when no crown
   * applies. Server-tracked round state (mirrors RoundStartedDto.CrownedNickname);
   * the hook lifts it into `crownedNickname` for the screens that render a
   * Guardian, and it clears on the next round unless re-awarded.
   */
  crownedNickname: string | null;
}

/**
 * reveal-delight/03: the payload of the hub's "GoldenGuardianVoteCast" broadcast
 * (GoldenGuardianVoteCastDto) - just the live "N of M voted" figures the Reveal
 * shows. No per-word tallies (AC-02), no identity (AC-07).
 */
interface GoldenGuardianVoteCast {
  votedCount: number;
  totalVoters: number;
}

/**
 * reveal-delight/03: the payload of the hub's "GoldenGuardianResolved" broadcast
 * (GoldenGuardianResolvedDto) - the winning coral word's blank token (ringed gold on
 * the Reveal) and the winning contributor's nickname. Both null when the vote
 * resolved with zero votes (no winner, no crown). The blank token is an already-
 * vetted, already-shown word's position (AC-07, no PII).
 */
interface GoldenGuardianResolved {
  winningBlankId: string | null;
  playerSessionId: string | null;
}

/**
 * The blanks THIS client owes for the current round (group-play/02), mirroring
 * the hub's YourBlanksDto wire contract. Sent per-connection as "YourBlanks"
 * right after RoundStarted - it carries ONLY this player's blank INDICES into
 * the round template's ordered blanks (the client resolves each to its prompt
 * via getBlanks(template)[index], Classic blind - prompt only, AC-02). It never
 * carries another player's blanks and no PII. The hook exposes just the indices;
 * they are null until "YourBlanks" arrives (a brief "dealing your blanks" beat
 * after the round starts) and cleared on leave / when a round resets.
 */
interface YourBlanks {
  blankIndices: number[];
}

/**
 * The result of submitting one word (group-play/03), mirroring the hub's
 * SubmitWordResultDto wire contract. On a rejected submission (no round / not in
 * the prompting phase, a word the safety filter blocks, or a blank that is not
 * this connection's) ok is false and error carries a friendly, kid-readable
 * message. On success ok is true and error is null. The submitWord invoke maps
 * this 1:1 to FillBlank's onSubmitWord contract (ok -> accepted, error -> message).
 */
interface SubmitWordResult {
  ok: boolean;
  error: string | null;
}

/**
 * One player's collection progress as sent by the hub's "CollectProgress" event
 * (PlayerProgressDto): an anonymous nickname + Guardian variant + whether they
 * have submitted ALL their assigned blanks. NO submitted words (AC-01) and no PII
 * beyond the already-filtered nickname + variant.
 */
export interface PlayerProgress {
  nickname: string;
  variant: string;
  done: boolean;
}

/**
 * Room-wide collection progress as broadcast by the hub's "CollectProgress" event
 * (CollectProgressDto), mirroring its wire contract: the "[N] of [M] quibblers
 * done" counts plus the per-player done/writing list the Waiting screen renders.
 * It carries NO submitted words (AC-01) - only who is done and who is still
 * writing. Null until the first progress arrives, cleared on leave / round reset.
 */
export interface CollectProgress {
  doneCount: number;
  playerCount: number;
  players: PlayerProgress[];
}

/**
 * One blank position for the reveal as sent by the hub's "RevealReady" event
 * (RevealWordDto): the submitted word plus its owning player (nickname + variant),
 * in blank order. An unfilled blank (a player who left) is an empty word
 * attributed to no one, so the client's assemble() keeps alignment. Every word
 * here already passed the safety filter server-side (AC-06).
 */
export interface RevealWord {
  word: string;
  nickname: string;
  variant: string;
}

/**
 * The shared reveal payload as broadcast by the hub's "RevealReady" event
 * (RevealReadyDto), mirroring its wire contract: the round's template id plus the
 * ordered reveal words (blank order). This is what routes EVERY player to the
 * shared Reveal in near-real-time (AC-05). The client resolves the template from
 * its bundled seedLibrary by id and assembles the story LOCALLY via the web engine
 * (the server never assembles). Null until it arrives, cleared on leave / round
 * reset so a prior round's reveal never bleeds into the next.
 */
export interface RevealInfo {
  templateId: string;
  words: RevealWord[];
}

/**
 * The outcome of invoking the hub's Rejoin(code, token) (session-engine/08's
 * RejoinResultDto), mirroring its wire contract. Like every other hub result
 * envelope here (JoinResult, StartRoundResult) this is a friendly result, NOT
 * an exception channel: an unknown/expired token or an already-evicted seat
 * comes back as ok:false with a kid-readable error (AC-05), never a throw. On
 * success (ok:true) it carries EXACTLY what the resuming client needs to pick
 * up where it left off - the SAME shapes the normal join/round flow already
 * uses (RoomState, RoundInfo, YourBlanks, CollectProgress, RevealInfo) so
 * session-engine/09's rejoin() helper can feed them straight into the
 * existing setters (setRoom, setIsHost, setRound, setAssignedBlankIndices,
 * setCollectProgress, setReveal) with no new parallel state tree. `round`,
 * `yourBlanks`, `progress`, and `reveal` are only ever non-null for the phase
 * that produces them (a "prompting" round -> round + yourBlanks + progress; a
 * "reveal" round -> round + reveal; the lobby -> just room + isHost + phase).
 */
interface RejoinResult {
  ok: boolean;
  error: string | null;
  room: RoomState | null;
  isHost: boolean;
  // Carried for wire-contract parity with story 08's RejoinResultDto. rejoin() derives
  // the resumed screen from round/reveal/progress (the existing routing effect's inputs),
  // so `phase` itself is not read here - it stays on the type as the server's own label.
  phase: string | null;
  round: RoundInfo | null;
  yourBlanks: YourBlanks | null;
  progress: CollectProgress | null;
  reveal: RevealInfo | null;
}

export interface UseGameHub {
  status: ConnectionStatus;
  /**
   * B1 (alpha-gate hardening): fire an immediate manual reconnect attempt,
   * bypassing whatever backoff wait the hook's own reconnect loop is mid-way
   * through. Wired to Home's "Try again" tap when `status === 'disconnected'`
   * so the disconnected state is actionable, not just legible. A safe no-op
   * when already connected or already mid-attempt (the underlying attempt
   * function guards both).
   */
  retryConnection: () => void;
  /** The current room (code + live roster), or null when not in one. Owned here so RosterChanged updates flow to every screen. */
  room: RoomState | null;
  /**
   * Whether THIS client is the host of the current room (session-engine/03).
   * True when this client created the room, false when it joined one, and
   * cleared when it leaves. The Lobby gates the host-only "Start game" CTA on
   * this (AC-05). It is tracked from the caller's own action rather than read
   * off the roster, because IsHost on a PlayerDto is not tied to a connection
   * on the wire (no PII), so a client cannot tell which roster row is "me" -
   * EXCEPT that replay-remix/03's "Pass the chisel" ALSO re-derives it from an
   * incoming "RosterChanged" broadcast, by matching the roster against the
   * nickname this client created/joined with (`myNicknameRef`) - the one
   * additional path a handoff needs, since a client can become (or stop being)
   * host without ever calling createRoom/joinRoom/startRound itself.
   */
  isHost: boolean;
  /**
   * The current round (template id + mode + round number), or null while in the
   * lobby (group-play/01). Set from the hub's "RoundStarted" broadcast so every
   * player - host and joiners alike - transitions into the round together; App
   * routes into word collection when this is non-null. Cleared on leave.
   */
  round: RoundInfo | null;
  /**
   * The blank INDICES this client owes for the current round (group-play/02),
   * or null until the hub's per-connection "YourBlanks" message arrives (a brief
   * beat after `round` is set). GroupRound renders FillBlank over ONLY these
   * blanks, resolved to prompts via getBlanks(template)[index] (Classic blind -
   * prompt only, AC-02). Cleared on leave and whenever `round` resets, so a stale
   * assignment never bleeds into the next round.
   */
  assignedBlankIndices: number[] | null;
  /**
   * Room-wide collection progress (group-play/03), or null until the first
   * "CollectProgress" broadcast arrives. Carries the "[N] of [M] done" counts and
   * the per-player done/writing list (NO submitted words, AC-01) the Waiting
   * screen renders. Cleared on leave and reset on a fresh RoundStarted.
   */
  collectProgress: CollectProgress | null;
  /**
   * The shared reveal payload (group-play/03), or null until the hub's
   * "RevealReady" broadcast arrives (the moment the LAST assigned blank is
   * submitted). Non-null routes EVERY player to the shared Reveal in
   * near-real-time (AC-05); the client resolves the template + assembles LOCALLY
   * via the web engine. Cleared on leave and reset on a fresh RoundStarted.
   */
  reveal: RevealInfo | null;
  /**
   * The room-wide reaction tally for the CURRENT reveal (reveal-delight/01,
   * AC-04), fed by the hub's "ReactionCountsChanged" broadcast. Server-
   * authoritative so no client double-counts (the instant floater gives the
   * tapper perceived responsiveness). Starts all-zero and RESETS to all-zero
   * whenever a new round starts or the reveal clears (counts are ephemeral per
   * reveal - Out of Scope: no persistence). GroupReveal feeds this straight into
   * <ReactionRow counts=...>.
   */
  reactionCounts: ReactionCounts;
  /**
   * Fire-and-forget a reaction for the current reveal (reveal-delight/01, AC-04).
   * Invokes the hub's React with the current room code (from roomCodeRef) and the
   * tapped type; the SERVER increments the room's tally and broadcasts
   * "ReactionCountsChanged" to the whole room group, so every player (the tapper
   * included) sees the count update. The payload is a TYPE ENUM only - no text, no
   * identity (AC-06). A no-op when not connected / not in a room (solo never calls
   * this - it bumps local state instead, AC-05).
   */
  react: (type: ReactionType) => void;
  /**
   * reveal-delight/03 (AC-04): the nickname wearing the Golden Guardian crown for
   * the CURRENT round (the previous round's funniest-word winner), or null when no
   * crown applies. Lifted from the "RoundStarted" broadcast's CrownedNickname, so
   * it is server-tracked round state (never a client timer): it is set for exactly
   * the round after a vote resolves and CLEARED on the next round (unless re-awarded)
   * / on back-to-lobby / on leave. App threads it to Lobby/Waiting/RoundComplete so
   * the matching player's <Guardian crowned /> shows the crown.
   */
  crownedNickname: string | null;
  /**
   * reveal-delight/03 (AC-02): how many present players have voted in the current
   * reveal's Golden Guardian funniest-word vote (the "N" in "N of M voted"), from the
   * hub's "GoldenGuardianVoteCast" broadcast. Resets to 0 on a fresh round / leave.
   */
  goldenGuardianVotedCount: number;
  /** reveal-delight/03 (AC-02): the total present voters (the "M" in "N of M voted"). Resets to 0 on a fresh round / leave. */
  goldenGuardianTotalVoters: number;
  /**
   * reveal-delight/03 (AC-03): whether the current reveal's Golden Guardian vote has
   * RESOLVED (every present player voted, or the host closed it). Distinguishes
   * "resolved with no winner" (resolved true, winningBlankId null) from "still
   * voting". Resets to false on a fresh round / leave.
   */
  goldenGuardianResolved: boolean;
  /**
   * reveal-delight/03 (AC-03): the winning coral word's blank token once the vote
   * resolves (the Reveal rings it gold), or null (not resolved, or resolved with zero
   * votes). Resets to null on a fresh round / leave.
   */
  goldenGuardianWinningBlankId: string | null;
  /**
   * reveal-delight/03 (AC-01): fire-and-forget cast/MOVE of this client's single
   * Golden Guardian vote for the tapped coral word's blank token. Invokes the hub's
   * CastGoldenGuardianVote with the current room code; the SERVER records it, keeps
   * one active vote per voter, and broadcasts the live "N of M voted" (and, on
   * resolution, the winner). The payload is an already-vetted, already-shown word's
   * position - no new text, no PII (AC-07). A no-op when not connected / not in a room.
   */
  castGoldenGuardianVote: (blankId: string) => void;
  /**
   * reveal-delight/03 (AC-03): fire-and-forget host-only "Reveal the winner" - closes
   * Golden Guardian voting early (the SERVER enforces the host check and resolves with
   * whatever votes are in). A no-op when not connected / not in a room / not the host.
   */
  closeGoldenGuardianVoting: () => void;
  /**
   * Submit ONE word for ONE of this client's assigned blanks (group-play/03).
   * Invokes the hub's SubmitWord with the current room code (from roomCodeRef),
   * the blank INDEX, and the word; the SERVER runs the safety filter FIRST and
   * records only on pass (AC-01, AC-06). A SKIP submits an empty word so the blank
   * records an empty placeholder (preserving reveal alignment). Resolves with
   * { accepted, message } - the exact shape FillBlank's onSubmitWord expects
   * (accepted:false shows the message inline and lets the player retry). Does NOT
   * set `reveal` itself: the server's RevealReady broadcast drives that for
   * everyone. Returns a not-connected failure if the hub is not ready.
   */
  submitWord: (blankIndex: number, word: string) => Promise<{ accepted: boolean; message?: string }>;
  /**
   * replay-remix/02 (AC-04/AC-06/AC-07): remix ONE blank of the just-finished
   * reveal. Invokes the hub's RemixWord with the current room code (from
   * roomCodeRef), the blank INDEX (body-order blank position, the same
   * convention `submitWord` and the Golden Guardian vote token already use),
   * and the new word; the SERVER runs the safety filter FIRST (same posture as
   * `submitWord`) and, on success, re-broadcasts "RevealReady" so EVERY player
   * (including this one) picks up the swapped word through the EXISTING
   * `reveal` state / RevealReady handler - this invoke does NOT set `reveal`
   * itself. ANY live room member may call this (no host guard, per the
   * 2026-07-04 Decisions-log call) - ask the caller UI, not this hook, to gate
   * who sees the "Remix a word" affordance if that ever changes. Resolves with
   * { accepted, message } (the same shape `submitWord` returns, matching
   * `FillBlank`'s `onSubmitWord` contract). Returns a not-connected failure if
   * the hub is not ready.
   */
  remixWord: (blankIndex: number, word: string) => Promise<{ accepted: boolean; message?: string }>;
  /**
   * Start a round as the host (group-play/01; story-selection/02 adds the
   * length parameter; group-play/05 adds the host's chosen MODE; story-selection/06
   * adds the optional explicit-template parameter). Invokes the hub's host-only
   * StartRound with the current room code (from roomCodeRef), the host's
   * family-safe toggle value, the host's story-length choice, the host's chosen
   * mode id, and an OPTIONAL explicit templateId; the SERVER enforces the host
   * check, validates the mode against the offered set (group-play/05, AC-02/AC-05),
   * and filters the template catalog by family-safe FIRST then by the mode's
   * eligibility (authoritative, AC-03/AC-04/AC-06, story-selection/02 AC-03/AC-05). When
   * `templateId` is supplied (the group "play a favorite" seam, story-selection/06
   * AC-03), the server plays that EXACT template instead of a random pick,
   * skipping length + freshness and never re-stamping freshness history
   * (AC-04) - family-safe still gates it first (AC-06). Resolves with the
   * StartRoundResult envelope (ok + friendly error). Returns a not-connected
   * error envelope if the hub is not ready. Does NOT set `round` itself - the
   * server's RoundStarted broadcast drives that for everyone, including the host.
   */
  startRound: (
    familySafe: boolean,
    lengthPref: LengthPreference,
    mode: string,
    templateId?: string,
  ) => Promise<StartRoundResult>;
  /**
   * Return the whole group to the lobby as the host (group-play/04, AC-05).
   * Invokes the hub's host-only BackToLobby with the current room code (from
   * roomCodeRef); the SERVER enforces the host check. Resolves with { ok, error }
   * (a friendly, kid-readable message on an expected rejection). Does NOT clear
   * round/reveal itself - the server's bare "BackToLobby" broadcast drives that for
   * EVERYONE (host included) so all players land back on the Lobby with the code +
   * roster preserved. Returns a not-connected failure if the hub is not ready.
   */
  backToLobby: () => Promise<{ ok: boolean; error: string | null }>;
  /**
   * replay-remix/03 (AC-01/AC-02/AC-04/AC-05): host-only, phase-gated "Pass the
   * chisel" - hand the host role to another roster player, BY NICKNAME, between
   * rounds only. Invokes the hub's PassHost with the current room code (from
   * roomCodeRef) and the target's nickname; the SERVER re-enforces the host
   * check and rejects a mid-round ("prompting") attempt (authoritative, same
   * posture as startRound/backToLobby). Resolves with { ok, error } (a friendly,
   * kid-readable message on an expected rejection). Does NOT flip `isHost`
   * itself on success - the server's reused "RosterChanged" broadcast carries
   * the moved flag to EVERY client (including this one), and the handler above
   * re-derives `isHost` from it. Returns a not-connected failure if the hub is
   * not ready.
   */
  passHost: (targetNickname: string) => Promise<{ ok: boolean; error: string | null }>;
  /** A friendly notice to show on the lobby when a round was reset (a player left mid-round), or null. */
  roundNotice: string | null;
  /** Dismiss the round-aborted lobby notice. */
  dismissRoundNotice: () => void;
  /**
   * Create a room and become its host (session-engine/01 + build/host-identity)
   * with a chosen display name + Guardian variant. Resolves with the
   * CreateRoomResult envelope (mirroring joinRoom's shape); on ok it also updates
   * `room` and sets isHost. Returns a not-connected error envelope if the hub is
   * not ready. The display name is safety-checked server-side (same gate as
   * joiners). Uses the ONE shared connection - never a second.
   */
  createRoom: (displayName: string, variant: string) => Promise<CreateRoomResult>;
  /**
   * Join an existing room by code with a display name and Guardian variant
   * (session-engine/02). Resolves with the JoinResult envelope; on ok it also
   * updates `room`. Returns a not-connected error envelope if the hub is not
   * ready. The display name is safety-checked server-side.
   */
  joinRoom: (code: string, displayName: string, variant: string) => Promise<JoinResult>;
  /**
   * Leave the current room and return Home. Tells the server (LeaveRoom) so this
   * connection is removed from the room group and its player leaves the roster
   * for everyone else, then clears local room state. The shared connection stays
   * open for the next game. Safe to call when not in a room (no-op).
   */
  clearRoom: () => void;
  /**
   * session-engine/09: true while an auto-rejoin attempt (triggered by either
   * a same-tab reconnect, AC-02, or the one-shot mount-time check, AC-03) is
   * in flight on the shared connection - false the rest of the time,
   * including "no stored handle to try" and "already resolved". This story
   * adds no UI of its own; story 10 consumes this to hold the live-route
   * guards open (a brief "reconnecting..." beat) instead of bouncing the
   * player Home before the rejoin has a chance to land.
   */
  isRejoining: boolean;
  /**
   * B4 (alpha-gate hardening): a brief, friendly explanation for Home after a
   * REJECTED Rejoin reset local state back to "not in a room" (an expired
   * token or an already-evicted seat) - so landing back on Home reads as an
   * explained outcome, not a silent teleport. Null the rest of the time.
   * Cleared by a fresh SUCCESSFUL createRoom/joinRoom/rejoin (which supersedes
   * it), never by a timer or by simply navigating around Home.
   */
  rejoinFailedNotice: string | null;
}

export function useGameHub(): UseGameHub {
  const connectionRef = useRef<HubConnection | null>(null);
  // accounts-identity/06 (ADR 0002 Decision F, #210): the live purchaser credential
  // (accounts-identity/03's in-memory PurchaserSession) that a SIGNED-IN host supplies
  // to the hub via SignalR's standard accessTokenFactory, so GameHub.OnConnectedAsync
  // can resolve their family-plan grant once at connect time. This hook stays generic:
  // it reads ONLY the session's current credential string here - it renders no purchaser
  // UI and does not otherwise depend on sign-in (free play is unchanged when signed out,
  // where the credential is null and the factory sends an empty token). Mirrored into a
  // ref so the accessTokenFactory getter (SignalR calls it before EVERY (re)connection)
  // reads the CURRENT value without rebuilding the one shared connection - a purchaser
  // who signs in mid-app-life is picked up on the next reconnect, no forced reconnect.
  const { credential } = usePurchaserSession();
  const purchaserCredentialRef = useRef<string | null>(credential);
  useEffect(() => {
    purchaserCredentialRef.current = credential;
  }, [credential]);
  // accounts-identity/09: the stored family-device token (../account/familyDeviceToken.ts),
  // read ONCE at mount into a ref - mirrors purchaserCredentialRef's shape so the
  // accessTokenFactory getter below reads the CURRENT value with no rebuild of the
  // one shared connection. Updated in place (never via setState - this never needs
  // to trigger a render) by the refresh effect just below, and by a caller that
  // saves a freshly redeemed token (RedeemDevice.tsx) picking it up on this
  // device's NEXT app launch (this ref is seeded once per mount, not re-read live).
  const familyDeviceTokenRef = useRef<string | null>(loadFamilyDeviceToken());
  // accounts-identity/09: once per app launch (empty deps - never per hub
  // reconnect), if this device holds a family-device token AND no purchaser is
  // signed in, silently rotate it via the companion refresh endpoint to keep its
  // rolling TTL alive (the story's Technical Notes: "the client calls it once per
  // app launch, not per hub reconnect"). `credential` is read from the closure at
  // the moment this effect first runs (mount); a purchaser who signs in only
  // AFTER this has already fired does not retroactively cancel an in-flight
  // refresh, which is harmless either way (the resolver at CreateRoom already
  // prefers a purchaser session first). On success the rotated token is persisted
  // and mirrored into the ref for the next connect/reconnect; ONLY on a definitive
  // dead-token signal (result.dead - the server was reached and said the token is
  // revoked / expired / unknown) is the stored token cleared so the device falls back
  // to anonymous play. A TRANSIENT failure (offline, 5xx, a 429 from the refresh
  // throttle) leaves the token in place so a passing hiccup at launch never unlinks a
  // valid device (WR-001) - a kid's device cannot re-sign-in, so it would otherwise
  // force a manual re-link. Never a hard failure blocking the app from loading.
  useEffect(() => {
    const token = familyDeviceTokenRef.current;
    if (!token || credential !== null) return;
    let cancelled = false;
    void refreshFamilyDeviceToken(token).then((result) => {
      if (cancelled) return;
      if (result.ok && result.token) {
        familyDeviceTokenRef.current = result.token;
        saveFamilyDeviceToken(result.token);
      } else if (result.dead) {
        familyDeviceTokenRef.current = null;
        clearFamilyDeviceToken();
      }
      // result.ok === false && result.dead === false -> transient: keep the token, retry next launch.
    });
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
  const [status, setStatus] = useState<ConnectionStatus>('connecting');
  const [room, setRoom] = useState<RoomState | null>(null);
  // Whether this client hosts the current room (set by createRoom / joinRoom,
  // cleared by clearRoom) - the Lobby's host-only Start CTA reads this (AC-05).
  const [isHost, setIsHost] = useState(false);
  // The current round, set from the "RoundStarted" broadcast (group-play/01) and
  // cleared on leave. Non-null flips App into word collection for every player.
  const [round, setRound] = useState<RoundInfo | null>(null);
  // This client's own blank indices for the round (group-play/02), set from the
  // per-connection "YourBlanks" message and cleared on leave / round reset. Null
  // until it arrives (a brief "dealing your blanks" beat after `round` is set).
  const [assignedBlankIndices, setAssignedBlankIndices] = useState<number[] | null>(null);
  // Room-wide collection progress (group-play/03), set from "CollectProgress" and
  // cleared on leave / round reset. Drives the Waiting screen's progress row.
  const [collectProgress, setCollectProgress] = useState<CollectProgress | null>(null);
  // The shared reveal payload (group-play/03), set from "RevealReady" and cleared
  // on leave / round reset. Non-null routes every player into the shared Reveal.
  const [reveal, setReveal] = useState<RevealInfo | null>(null);
  // The room-wide reaction tally for the current reveal (reveal-delight/01,
  // AC-04), set from "ReactionCountsChanged" and RESET to all-zero on a fresh
  // RoundStarted / BackToLobby / RoundAborted / leave (ephemeral per reveal).
  const [reactionCounts, setReactionCounts] = useState<ReactionCounts>(ZERO_REACTIONS);
  // reveal-delight/03 (AC-04): the crowned player's nickname for the CURRENT round,
  // lifted from the "RoundStarted" broadcast's CrownedNickname. Set for exactly the
  // round after a vote resolves; cleared on the next round (unless re-awarded) /
  // BackToLobby / RoundAborted / leave. App threads it to the Guardian-rendering
  // screens so the winner wears the crown.
  const [crownedNickname, setCrownedSessionId] = useState<string | null>(null);
  // reveal-delight/03 (AC-02/AC-03): the current reveal's Golden Guardian vote state,
  // fed by the "GoldenGuardianVoteCast" (live "N of M") and "GoldenGuardianResolved"
  // (winner) broadcasts. All ephemeral per reveal: RESET on a fresh RoundStarted /
  // BackToLobby / RoundAborted / leave, exactly like the reaction tally.
  const [goldenGuardianVotedCount, setGoldenGuardianVotedCount] = useState(0);
  const [goldenGuardianTotalVoters, setGoldenGuardianTotalVoters] = useState(0);
  const [goldenGuardianResolved, setGoldenGuardianResolved] = useState(false);
  const [goldenGuardianWinningBlankId, setGoldenGuardianWinningBlankId] = useState<string | null>(null);
  // A friendly notice shown on the lobby when a round was reset because a player
  // left mid-collection (group-play recovery, "RoundAborted"). Cleared when a fresh
  // round starts, on leave, and on manual dismiss.
  const [roundNotice, setRoundNotice] = useState<string | null>(null);
  // Whether this client is currently seated in a room, plus that room's code.
  // Kept as refs (not state) so the stable RosterChanged handler and clearRoom
  // read the latest value without re-binding. The handler guards on inRoomRef so
  // a roster broadcast that RACES a leave cannot resurrect room state after the
  // player has already gone Home (the post-leave re-entry bug).
  const inRoomRef = useRef(false);
  const roomCodeRef = useRef<string | null>(null);
  // replay-remix/03: the ONE identity handle this client holds for "which roster
  // row is me" - the (trimmed) nickname it created/joined the room with. Set on a
  // successful createRoom/joinRoom, cleared on clearRoom. Nicknames are unique
  // within a room, case-insensitively, at join (session-engine/02, AC-06) and
  // never change afterward, so matching by it stays valid for the room's life.
  // Used ONLY by handleRosterChanged below to re-derive `isHost` when a "Pass the
  // chisel" handoff makes/unmakes this client the host without it calling
  // createRoom/joinRoom/startRound itself.
  const myNicknameRef = useRef<string | null>(null);
  // room-start-duplicate-members: the latest `isHost` mirrored into a ref so the stable
  // "HostGranted" handler (registered once, below) reads the current value without
  // re-binding, and shows the "you're the host now" notice only on a genuine false->true
  // handover (never twice if a duplicate message ever arrived). Kept in sync with the
  // state by the effect just below, so every setIsHost site (create/join/rejoin/clear/
  // promote) flows through it.
  const isHostRef = useRef(false);
  // session-engine/09: whether an auto-rejoin attempt is currently in flight.
  // `rejoiningRef` is the synchronous double-fire guard `rejoin()` checks
  // FIRST (so the two triggers, AC-02's onreconnected and AC-03's mount-time
  // effect, can never both invoke Rejoin at once); `isRejoining` mirrors it
  // into state so the hook can expose it (story 10 holds live-route guards
  // open while it is true).
  const rejoiningRef = useRef(false);
  const [isRejoining, setIsRejoining] = useState(false);

  // room-start-duplicate-members: keep isHostRef in lockstep with the isHost state so the
  // once-registered "HostGranted" handler always reads the current value.
  useEffect(() => {
    isHostRef.current = isHost;
  }, [isHost]);

  // B1 (alpha-gate hardening): the manual reconnect loop's bookkeeping - refs,
  // not state, because they are read/written from a setTimeout callback and
  // the visibilitychange/online listeners inside the mount effect below, never
  // rendered directly. `attemptManualReconnectRef` lets the STABLE
  // `retryConnection` (exposed below) call into the mount effect's own attempt
  // function without depending on it (the effect owns the one `connection` and
  // runs once per mount).
  const manualReconnectTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const manualReconnectAttemptRef = useRef(0);
  const startInFlightRef = useRef(false);
  const attemptManualReconnectRef = useRef<() => Promise<void>>(async () => {});

  // B4 (alpha-gate hardening): a brief, friendly explanation shown on Home
  // after a REJECTED Rejoin reset local state back to "not in a room" (an
  // expired token or an already-evicted seat) - so landing back on Home reads
  // as an explained outcome, not a silent teleport. Set only in rejoin()'s
  // ok:false branch below; cleared by a fresh SUCCESSFUL createRoom/joinRoom/
  // rejoin (which supersedes it) rather than a timer or a dismiss action.
  const [rejoinFailedNotice, setRejoinFailedNotice] = useState<string | null>(null);

  /**
   * B4 (alpha-gate hardening): reset every piece of local room/round/reveal
   * state back to "not in a room" - the shared core of both a deliberate Leave
   * (`clearRoom`, below) and a REJECTED `Rejoin` (`rejoin`, below). Marks
   * `inRoomRef` false BEFORE anything else so an in-flight broadcast racing
   * this reset is ignored - the same guard `clearRoom` has always relied on.
   * Deliberately does NOT touch the stored reconnect handle or tell the server
   * anything: callers decide that part themselves (`clearRoom` also clears the
   * handle + tells the server `LeaveRoom`; a rejected rejoin has already lost
   * the handle and the server has already dropped the seat, so neither applies
   * there). Also deliberately does NOT touch `rejoinFailedNotice` - that is a
   * DIFFERENT, longer-lived notice than the room state below (it should
   * survive a plain Leave-from-Home so the player still sees it), cleared only
   * by a fresh successful create/join/rejoin.
   */
  const resetRoomState = useCallback(() => {
    inRoomRef.current = false;
    roomCodeRef.current = null;
    myNicknameRef.current = null;
    setRoom(null);
    setIsHost(false);
    setRound(null);
    setAssignedBlankIndices(null);
    setCollectProgress(null);
    setReveal(null);
    setReactionCounts(ZERO_REACTIONS);
    setGoldenGuardianVotedCount(0);
    setGoldenGuardianTotalVoters(0);
    setGoldenGuardianResolved(false);
    setGoldenGuardianWinningBlankId(null);
    setCrownedSessionId(null);
    setRoundNotice(null);
  }, []);

  /**
   * session-engine/09: attempt to reclaim a previously-held seat on THIS
   * connection by invoking the hub's Rejoin(code, token) with whatever handle
   * `reconnect.ts` has stored. Never called directly by the UI - only from the
   * two triggers below: the EXISTING `connection.onreconnected(...)` handler
   * (a same-tab network blip, AC-02) and the one-shot mount-time effect
   * further down (a full page reload / app relaunch, AC-03). A no-op when
   * there is no stored handle or the connection is not ready.
   *
   * On success (ok:true) it applies the rehydrated fields into the SAME
   * setters the normal join/round flow already populates - no new parallel
   * state tree - after first checking the room this handle names is still the
   * one worth resuming: if `inRoomRef` is already true for a DIFFERENT code
   * (the player deliberately left and created/joined a fresh room, or another
   * rejoin already landed, while this call was in flight), the stale result
   * is dropped rather than clobbering what is already live (the file's
   * existing "post-leave re-entry" guard, reused - not a second mechanism).
   *
   * On an explicit rejection (ok:false - unknown/expired token, an evicted
   * seat) the stored handle is discarded immediately (AC-04) so a later
   * create/join is never haunted by a stale token. A THROWN invoke (a
   * transient disconnect / hub error, not a real rejection) leaves the
   * handle alone so the next trigger can simply try again - no retry loop is
   * added here (Out of Scope: retrying a FAILED rejoin).
   *
   * `rejoiningRef` is a synchronous guard set true before the very first
   * `await`, so the two triggers can never double-fire even if they land in
   * the same tick; it also backs the exposed `isRejoining` signal.
   */
  const rejoin = useCallback(async (): Promise<void> => {
    if (rejoiningRef.current) return; // already attempting - never a double-fire
    const handle = loadReconnectHandle();
    if (!handle) return;

    const connection = connectionRef.current;
    if (!connection || connection.state !== HubConnectionState.Connected) return;

    rejoiningRef.current = true;
    setIsRejoining(true);
    try {
      const result = await connection.invoke<RejoinResult>('Rejoin', handle.code, handle.token);

      if (result.ok) {
        // AC-05 guard: a deliberate leave/Home (clearRoom -> clearReconnectHandle)
        // or a fresh create/join may have landed while this Rejoin was in flight.
        // The stored handle is the authoritative "is this still the seat to resume?"
        // signal: clearRoom clears it, and a fresh create/join overwrites it with a
        // different code. If it is gone or now names a different room, this result
        // is stale - dropping it stops a resumed player being yanked back into a room
        // they explicitly left (server LeaveRoom-vs-Rejoin ordering is nondeterministic,
        // so the client cannot rely on the server having returned ok:false here).
        const current = loadReconnectHandle();
        if (!current || current.code !== handle.code) {
          return;
        }
        // Something else already claimed a DIFFERENT room while this call was
        // in flight (a second rejoin that landed first) - never resurrect/clobber it.
        if (inRoomRef.current && roomCodeRef.current !== handle.code) {
          return;
        }
        inRoomRef.current = true;
        roomCodeRef.current = handle.code;
        // B4: a successful rejoin supersedes any earlier rejoin failure's notice.
        setRejoinFailedNotice(null);
        if (result.room) setRoom(result.room);
        setIsHost(result.isHost);
        // replay-remix/03: myNicknameRef is normally set by createRoom/joinRoom in
        // THIS tab session; a rejoin after a full reload/relaunch starts it null
        // (a fresh component mount). It can only be safely recovered here for the
        // HOST case - a room has exactly one host, so a rejoin landing as host can
        // match that one roster entry. A non-host rejoin has no reliable way to
        // tell which roster row is "me" from this envelope alone (the nickname
        // handle's known fragility - see the file header and openQuestions), so
        // myNicknameRef stays null for that case until this client's own
        // createRoom/joinRoom next runs.
        if (result.isHost && result.room) {
          const hostEntry = result.room.players.find((p) => p.isHost);
          if (hostEntry) {
            myNicknameRef.current = hostEntry.nickname;
          }
        }
        setRound(result.round);
        setAssignedBlankIndices(result.yourBlanks ? result.yourBlanks.blankIndices : null);
        setCollectProgress(result.progress);
        setReveal(result.reveal);
      } else {
        // AC-04 / B4: an expected rejection (an unknown/expired token, an
        // already-evicted seat). Re-check the stored handle FIRST, using the
        // SAME staleness signal the ok:true branch above relies on: if a
        // deliberate Leave (clearRoom) or a fresh create/join has already
        // landed while this call was in flight, the handle is already gone or
        // already names a different room, and this late failure is stale news
        // that must not touch state that has already moved on. Always discard
        // the handle regardless (AC-04) - a rejected token must never be
        // retried - but only reset local state + surface the friendly notice
        // when the failure is still about the CURRENT situation.
        const current = loadReconnectHandle();
        const stillRelevant = current !== null && current.code === handle.code;
        clearReconnectHandle();
        if (stillRelevant) {
          // B4: the seat is gone server-side and this connection was never
          // re-added to the room's group, so no more broadcasts will ever
          // arrive - reset back to "not in a room" (the SAME reset a
          // deliberate Leave uses) so the app naturally routes Home instead of
          // freezing on stale room/round/reveal state, and explain why.
          resetRoomState();
          setRejoinFailedNotice('That seat timed out - rejoin with a new code.');
        }
      }
    } catch {
      // A thrown invoke (transient disconnect / hub error) is not a
      // rejection - leave the stored handle alone so the next trigger can
      // simply retry.
    } finally {
      rejoiningRef.current = false;
      setIsRejoining(false);
    }
  }, [resetRoomState]);

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl(HUB_URL, {
        // accounts-identity/06 (AC-01), extended by accounts-identity/09: supply the
        // signed-in purchaser's EXISTING credential (no new credential type is minted
        // for this) if present, ELSE fall back to this device's stored family-device
        // token (accounts-identity/09 - a kid's device that is never signed in but
        // was linked from a parent's Account page), ELSE supply nothing (fully
        // anonymous, unchanged free play). A GETTER, not a snapshot - SignalR calls it
        // before every (re)connection attempt, so it reads both refs' CURRENT values;
        // a purchaser who signs in, or a device that gets linked/rotated/revoked,
        // after the connection is built is picked up on the next reconnect without a
        // forced one. Empty string when neither is present - the server treats an
        // absent/empty token as anonymous, so free play is byte-for-byte unchanged
        // (AC-05 of story 06, AC-06 of story 09).
        accessTokenFactory: () => purchaserCredentialRef.current ?? familyDeviceTokenRef.current ?? '',
      })
      // Jittered automatic reconnect: the default policy's fixed 0/2/10/30s
      // schedule makes every client stampede the hub in lockstep after a
      // restart (the herd that pegged the UAT B1 to ~98-99% CPU - see
      // docs/load-testing/findings.md). This custom policy jitters each delay
      // and returns null after the SAME four attempts, so the give-up ->
      // onclose -> manual-loop handoff (below) is unchanged.
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (retryContext) =>
          jitteredAutoReconnectDelayMs(retryContext.previousRetryCount),
      })
      .configureLogging(LogLevel.Information)
      .build();

    connectionRef.current = connection;

    // `cancelled` guards EVERY state update below, so a handler that fires during
    // teardown (StrictMode's dev remount, an HMR swap, or a real unmount racing
    // an in-flight reconnect/close) can never setState on a torn-down effect.
    let cancelled = false;

    // reveal-delight/03: reset the ephemeral Golden Guardian VOTE state (the per-reveal
    // "N of M" + winner). Used by the round-ending handlers (BackToLobby / RoundAborted)
    // and clearRoom's leave. It does NOT touch `crownedNickname`: the crown is
    // server-tracked round state that lasts the whole NEXT round (AC-04), so it must
    // survive a back-to-lobby (the crowned player still wears it in the lobby and the
    // round that follows). The crown is only ever (re)set by "RoundStarted" (from the
    // round's CrownedNickname) and cleared on leave (clearRoom).
    const resetGoldenGuardianVote = () => {
      setGoldenGuardianVotedCount(0);
      setGoldenGuardianTotalVoters(0);
      setGoldenGuardianResolved(false);
      setGoldenGuardianWinningBlankId(null);
    };

    // Live roster updates (session-engine/02): the hub broadcasts the updated
    // roster to a room's group whenever someone joins. Registered ONCE here,
    // before start(), so it is never missed and there is only one handler on
    // the one connection. Whoever is in the room (host + players) gets the new
    // roster and the UI updates in near-real-time (AC-05). Detached on cleanup.
    const handleRosterChanged = (state: RoomState) => {
      // Ignore a broadcast that arrives after we have left (raced our LeaveRoom /
      // group removal) or during teardown - otherwise it would resurrect room
      // state and bounce a player who just went Home back into the lobby.
      if (cancelled || !inRoomRef.current) return;
      setRoom(state);
      // replay-remix/03 (AC-02/AC-03): re-derive OUR OWN host flag from the
      // incoming roster. A "Pass the chisel" handoff can make (or unmake) this
      // client the host WITHOUT it having called createRoom/joinRoom/startRound
      // itself, so `isHost` must also flip here, live, for both the newly-made
      // host AND the outgoing one. Matched by the one identity handle this
      // client holds - the nickname it created/joined with (see myNicknameRef's
      // own comment for why nickname, not a wire identity field).
      const myNickname = myNicknameRef.current;
      if (myNickname) {
        const mine = state.players.find(
          (p) => p.nickname.toLowerCase() === myNickname.toLowerCase(),
        );
        if (mine) {
          setIsHost(mine.isHost);
        }
      }
    };
    connection.on('RosterChanged', handleRosterChanged);

    // Host handover (room-start-duplicate-members): the server sends this ONLY to the
    // connection it just promoted to host after another player left (Room.EnsureHostLocked),
    // so the promoted client can turn its host-only Start CTA on - the room is otherwise
    // hosted but this client has no way to know it (the roster DTO carries no identity).
    // Guarded by inRoomRef so a message racing our own leave cannot revive host state after
    // we have gone Home, and by isHostRef so the friendly notice fires only on a genuine
    // false->true handover (a no-op if we somehow already hold the flag).
    const handleHostGranted = () => {
      if (cancelled || !inRoomRef.current || isHostRef.current) return;
      isHostRef.current = true;
      setIsHost(true);
      setRoundNotice("You're the host now - tap Start game when your crew's ready.");
    };
    connection.on('HostGranted', handleHostGranted);

    // Round start (group-play/01): the hub broadcasts "RoundStarted" to the whole
    // room group when the host starts a round, so every player - host and joiners
    // alike - moves into word collection together in near-real-time (AC-01,
    // AC-02). Registered ONCE here, before start(), so it is never missed, and
    // guarded by inRoomRef so a broadcast that races a leave cannot pull a player
    // who has gone Home back into a round.
    const handleRoundStarted = (info: RoundInfo) => {
      if (cancelled || !inRoomRef.current) return;
      // Reset any prior round's assignment so a stale one never shows for the new
      // round; "YourBlanks" (below) fills it in a beat later (group-play/02). Also
      // drop the prior round's progress + reveal (group-play/03) so a replay starts
      // clean and a stale reveal never re-routes a fresh round to the payoff screen.
      setAssignedBlankIndices(null);
      setCollectProgress(null);
      setReveal(null);
      setReactionCounts(ZERO_REACTIONS); // reveal-delight/01: reactions are per-reveal; a new round starts them at zero.
      // reveal-delight/03: the funniest-word vote is per-reveal too, so a fresh round
      // starts it clean. The CROWN, however, is carried on THIS round (info.crowned
      // Nickname) - the previous round's winner wears it now (AC-04); it is null when
      // not re-awarded, which clears a stale crown.
      setGoldenGuardianVotedCount(0);
      setGoldenGuardianTotalVoters(0);
      setGoldenGuardianResolved(false);
      setGoldenGuardianWinningBlankId(null);
      setCrownedSessionId(info.crownedNickname ?? null);
      setRoundNotice(null); // a fresh round clears any prior "someone left" notice.
      setRound(info);
    };
    connection.on('RoundStarted', handleRoundStarted);

    // Per-player blank assignment (group-play/02): right after RoundStarted the
    // hub sends THIS connection its own blanks as "YourBlanks" (only its indices,
    // no other player, no PII). Registered ONCE here, before start(), so it is
    // never missed, and guarded by inRoomRef so a message racing a leave cannot
    // set an assignment for a player who has gone Home.
    const handleYourBlanks = (payload: YourBlanks) => {
      if (cancelled || !inRoomRef.current) return;
      setAssignedBlankIndices(payload.blankIndices);
    };
    connection.on('YourBlanks', handleYourBlanks);

    // Collection progress (group-play/03): the hub broadcasts "CollectProgress" to
    // the whole room group after each submission so every client can render the
    // Waiting screen's progress row (done/writing) and "[N] of [M] done" counts. It
    // carries NO submitted words (AC-01). Registered ONCE, guarded by inRoomRef so a
    // broadcast racing a leave cannot revive state for a player who has gone Home.
    const handleCollectProgress = (payload: CollectProgress) => {
      if (cancelled || !inRoomRef.current) return;
      setCollectProgress(payload);
    };
    connection.on('CollectProgress', handleCollectProgress);

    // Reveal transition (group-play/03, AC-05): the hub broadcasts "RevealReady" to
    // the whole room group the moment the LAST assigned blank is submitted, so every
    // player - done or still writing - moves to the shared Reveal in near-real-time
    // without refreshing. It ships the template id + ordered words; the client
    // resolves the template and assembles LOCALLY (the server never assembles).
    // Registered ONCE, guarded by inRoomRef.
    const handleRevealReady = (payload: RevealInfo) => {
      if (cancelled || !inRoomRef.current) return;
      setReveal(payload);
    };
    connection.on('RevealReady', handleRevealReady);

    // Reaction tally (reveal-delight/01, AC-04; reactions v2): the hub broadcasts
    // "ReactionCountsChanged" to the whole room group whenever any player reacts,
    // so every client's reaction row shows the updated count in near-real-time.
    // The payload is the full tally ({ love, wow, nope }) - server-authoritative,
    // and the server now de-dupes ONE REACTION PER CONNECTION (a tap selects, a
    // different tap moves, the same tap toggles off), so no client can inflate. The
    // camelCased wire fields (love/wow/nope) match the ReactionCounts keys exactly,
    // so the payload feeds the row straight through. Registered ONCE, guarded by
    // inRoomRef so a broadcast racing a leave cannot revive state for a gone-Home
    // client. Reset to all-zero on a fresh RoundStarted / BackToLobby (ephemeral).
    // The client tracks its OWN current selection locally (GroupReveal's myReaction)
    // for the highlight - the hub is authoritative for counts, not the selection.
    const handleReactionCountsChanged = (payload: ReactionCounts) => {
      if (cancelled || !inRoomRef.current) return;
      setReactionCounts(payload);
    };
    connection.on('ReactionCountsChanged', handleReactionCountsChanged);

    // Golden Guardian live vote progress (reveal-delight/03, AC-02): the hub
    // broadcasts "GoldenGuardianVoteCast" whenever any player casts/moves a vote, so
    // every client's Reveal shows the same "N of M voted" status. Per-word tallies are
    // NOT shipped mid-vote (AC-02). Registered ONCE, guarded by inRoomRef.
    const handleGoldenGuardianVoteCast = (payload: GoldenGuardianVoteCast) => {
      if (cancelled || !inRoomRef.current) return;
      setGoldenGuardianVotedCount(payload.votedCount);
      setGoldenGuardianTotalVoters(payload.totalVoters);
    };
    connection.on('GoldenGuardianVoteCast', handleGoldenGuardianVoteCast);

    // Golden Guardian resolution (reveal-delight/03, AC-03): the hub broadcasts
    // "GoldenGuardianResolved" the moment the vote resolves (all present voted, or the
    // host closed it), carrying the winning blank token (ringed gold on the Reveal) and
    // the winning contributor's nickname. winningBlankId is null when nobody voted (no
    // winner, no crown). The CROWN itself is applied on the NEXT round's RoundStarted
    // (server-tracked), so this handler only paints the current reveal's winner - it
    // does NOT set crownedNickname. Registered ONCE, guarded by inRoomRef.
    const handleGoldenGuardianResolved = (payload: GoldenGuardianResolved) => {
      if (cancelled || !inRoomRef.current) return;
      setGoldenGuardianResolved(true);
      setGoldenGuardianWinningBlankId(payload.winningBlankId ?? null);
    };
    connection.on('GoldenGuardianResolved', handleGoldenGuardianResolved);

    // Back to lobby (group-play/04, AC-05): the host ended the round, so the hub
    // broadcasts a BARE "BackToLobby" to the whole room group. Every player drops
    // the round/reveal/progress/assignment locally and falls back to the Lobby it
    // STILL holds (room + roster stay set - the round ending does not change the
    // roster). Registered ONCE, guarded by inRoomRef so a broadcast racing a leave
    // cannot touch state for a player who has already gone Home.
    const handleBackToLobby = () => {
      if (cancelled || !inRoomRef.current) return;
      setRound(null);
      setReveal(null);
      setReactionCounts(ZERO_REACTIONS); // reveal-delight/01: drop the reveal's reaction tally on return to lobby.
      resetGoldenGuardianVote(); // reveal-delight/03: drop the per-reveal vote state on return to lobby (the crown persists, AC-04).
      setCollectProgress(null);
      setAssignedBlankIndices(null);
      setRoundNotice(null); // clear any stale round-aborted notice on a normal return too (Copilot review).
    };
    connection.on('BackToLobby', handleBackToLobby);

    // Round aborted (group-play recovery): a player left mid-collection, so the hub
    // reset the round and broadcasts "RoundAborted" with a friendly reason. Every
    // remaining player drops the round/reveal/progress/assignment (like BackToLobby)
    // and falls back to the still-live Lobby, where the reason shows as a notice.
    // Guarded by inRoomRef so a broadcast racing a leave cannot touch a gone-Home client.
    const handleRoundAborted = (payload: { reason: string }) => {
      if (cancelled || !inRoomRef.current) return;
      setRound(null);
      setReveal(null);
      setReactionCounts(ZERO_REACTIONS); // reveal-delight/01: drop the reveal's reaction tally on an aborted round too.
      resetGoldenGuardianVote(); // reveal-delight/03: drop the per-reveal vote state on an aborted round (the crown persists, AC-04).
      setCollectProgress(null);
      setAssignedBlankIndices(null);
      setRoundNotice(payload.reason);
    };
    connection.on('RoundAborted', handleRoundAborted);

    // B1 (alpha-gate hardening): the manual reconnect loop, used only once
    // `withAutomaticReconnect()`'s own policy has given up (`onclose`, below)
    // or the very first `start()` fails outright (`.catch`, below) - SignalR
    // retries neither case on its own. `scheduleManualReconnect` arms the next
    // attempt on `jitteredManualReconnectDelayMs`'s backoff (2s/5s/10s/30s base,
    // jittered per-client, then 30s forever); `attemptManualReconnect` is the
    // attempt itself, reused
    // verbatim by the `visibilitychange`/`online` listeners below for an
    // IMMEDIATE extra try (bypassing whatever wait is left) the moment the app
    // is plausibly back - "phone was locked/out of signal, now isn't".
    const scheduleManualReconnect = () => {
      if (cancelled) return;
      if (manualReconnectTimerRef.current !== null) {
        clearTimeout(manualReconnectTimerRef.current);
      }
      const attempt = manualReconnectAttemptRef.current;
      manualReconnectAttemptRef.current = attempt + 1;
      manualReconnectTimerRef.current = setTimeout(() => {
        manualReconnectTimerRef.current = null;
        void attemptManualReconnect();
      }, jitteredManualReconnectDelayMs(attempt));
    };

    const attemptManualReconnect = async () => {
      if (cancelled) return;
      // Only while foregrounded - a backgrounded/locked screen pauses the
      // loop rather than burning battery retrying unseen; becoming visible
      // again fires an immediate attempt of its own (below), so nothing here
      // is lost, just deferred.
      if (document.visibilityState !== 'visible') return;
      // Never overlap a start()/stop() already in flight (SignalR throws if
      // start() is called while one is running) and never race the automatic-
      // reconnect policy while it still owns the connection.
      if (startInFlightRef.current || connection.state !== HubConnectionState.Disconnected) {
        return;
      }
      startInFlightRef.current = true;
      try {
        await startWithTimeout(connection, CONNECT_TIMEOUT_MS);
        if (cancelled) return;
        manualReconnectAttemptRef.current = 0;
        if (manualReconnectTimerRef.current !== null) {
          clearTimeout(manualReconnectTimerRef.current);
          manualReconnectTimerRef.current = null;
        }
        setStatus('connected');
        // Mirror onreconnected below: the connection is back on a NEW id, so a
        // held seat needs an explicit Rejoin to be re-added to the room's
        // group. rejoin() itself no-ops with no stored handle.
        void rejoin();
      } catch {
        if (cancelled) return;
        scheduleManualReconnect();
      } finally {
        startInFlightRef.current = false;
      }
    };
    attemptManualReconnectRef.current = attemptManualReconnect;

    connection.onreconnecting(() => {
      if (!cancelled) setStatus('connecting');
    });
    connection.onreconnected(() => {
      if (!cancelled) setStatus('connected');
      // session-engine/09 (AC-02): a same-tab automatic reconnect just
      // succeeded on a NEW connection id - if a seat is still held, reclaim
      // it right away with no user action. `rejoin()` itself no-ops when
      // there is no stored handle, so this is safe to call unconditionally.
      if (!cancelled) void rejoin();
    });
    connection.onclose(() => {
      if (cancelled) return;
      setStatus('disconnected');
      // B1: `withAutomaticReconnect()`'s policy just exhausted its retries (or
      // the connection closed for some other terminal reason) - `onclose` is
      // SignalR's final word until `start()` is called again. A fresh
      // terminal disconnect starts its own backoff from the top.
      manualReconnectAttemptRef.current = 0;
      scheduleManualReconnect();
    });

    startWithTimeout(connection, CONNECT_TIMEOUT_MS)
      .then(() => {
        if (!cancelled) setStatus('connected');
      })
      .catch(() => {
        if (cancelled) return;
        setStatus('disconnected');
        // B1: the very first attempt was otherwise a single shot with no
        // retry of its own (a cold app-service start, or opening the PWA
        // before wifi associates) - fall into the SAME manual loop `onclose`
        // uses above. A hung attempt (never resolves nor rejects - see
        // `startWithTimeout`'s header) lands here too once CONNECT_TIMEOUT_MS
        // elapses, instead of leaving `status` wedged at 'connecting' forever.
        manualReconnectAttemptRef.current = 0;
        scheduleManualReconnect();
      });

    // B1: an immediate extra attempt (bypassing whatever backoff wait is
    // left) the moment the app is plausibly reachable again - both realistic
    // "phone was locked/out of signal, now isn't" triggers. A safe no-op the
    // rest of the time (`attemptManualReconnect` itself guards on being
    // disconnected and not already mid-attempt).
    const handleVisibilityChange = () => {
      if (document.visibilityState === 'visible') {
        void attemptManualReconnect();
      }
    };
    document.addEventListener('visibilitychange', handleVisibilityChange);
    const handleOnline = () => {
      void attemptManualReconnect();
    };
    window.addEventListener('online', handleOnline);

    // Tear the connection down on unmount (and on StrictMode's dev remount).
    return () => {
      cancelled = true;
      if (manualReconnectTimerRef.current !== null) {
        clearTimeout(manualReconnectTimerRef.current);
        manualReconnectTimerRef.current = null;
      }
      document.removeEventListener('visibilitychange', handleVisibilityChange);
      window.removeEventListener('online', handleOnline);
      connection.off('RosterChanged', handleRosterChanged);
      connection.off('HostGranted', handleHostGranted);
      connection.off('RoundStarted', handleRoundStarted);
      connection.off('YourBlanks', handleYourBlanks);
      connection.off('CollectProgress', handleCollectProgress);
      connection.off('RevealReady', handleRevealReady);
      connection.off('ReactionCountsChanged', handleReactionCountsChanged);
      connection.off('GoldenGuardianVoteCast', handleGoldenGuardianVoteCast);
      connection.off('GoldenGuardianResolved', handleGoldenGuardianResolved);
      connection.off('BackToLobby', handleBackToLobby);
      connection.off('RoundAborted', handleRoundAborted);
      void connection.stop();
    };
  }, []);

  // session-engine/09 (AC-03): the app (re)mounted with no in-memory room but
  // a stored reconnect handle - once the connection reaches `connected`,
  // attempt the SAME Rejoin automatically (covers a full page reload / app
  // relaunch mid-game, not just AC-02's same-tab network blip). Deliberately
  // one-shot: a successful rejoin flips `inRoomRef.current` true, and a
  // failed one clears the stored handle (inside `rejoin()`, AC-04) - either
  // way this effect's own guard condition goes false on the next run, so no
  // separate "already attempted" ref is needed to stop it from retrying.
  useEffect(() => {
    if (status !== 'connected') return;
    if (inRoomRef.current) return;
    if (!loadReconnectHandle()) return;
    void rejoin();
  }, [status, rejoin]);

  /**
   * B1 (alpha-gate hardening): fire an immediate manual reconnect attempt,
   * bypassing whatever backoff wait the mount effect's loop is mid-way
   * through. Wired to Home's "Try again" tap when `status === 'disconnected'`
   * so the disconnected state is actionable, not just legible. Reads the
   * latest attempt function off `attemptManualReconnectRef` (set inside the
   * mount effect) rather than depending on it directly, so this stays a
   * stable callback across the connection's whole lifetime. A safe no-op when
   * already connected or already mid-attempt (`attemptManualReconnect` itself
   * guards both).
   */
  const retryConnection = useCallback(() => {
    void attemptManualReconnectRef.current();
  }, []);

  const createRoom = useCallback(
    async (displayName: string, variant: string): Promise<CreateRoomResult> => {
      const connection = connectionRef.current;
      if (!connection || connection.state !== HubConnectionState.Connected) {
        return {
          ok: false,
          room: null,
          error: "We're not connected yet - give it a moment and try again.",
          reconnectToken: null,
        };
      }
      // build/host-identity: the host name + variant travel to the hub, which
      // safety-filters the name server-side (same gate as joiners) before minting
      // the room. On a rejected create the envelope carries a friendly error the
      // HostSetup screen shows inline; on ok it carries the minted room state.
      try {
        const result = await connection.invoke<CreateRoomResult>('CreateRoom', displayName, variant);
        if (result.ok && result.room) {
          inRoomRef.current = true;
          roomCodeRef.current = result.room.code;
          // replay-remix/03: remember the nickname we created with - the identity
          // handle that handleRosterChanged needs to re-derive `isHost` if a later
          // "Pass the chisel" handoff moves the role off this client.
          myNicknameRef.current = displayName.trim();
          setRoom(result.room);
          setIsHost(true); // the creator is the host (AC-05)
          // B4: a fresh successful create supersedes any earlier rejoin-failed notice.
          setRejoinFailedNotice(null);
          // session-engine/09 (AC-01): remember this seat's {code, token} handle
          // device-locally so a later drop / reload can auto-rejoin it. A no-op
          // (never throws) when the server did not mint a token or storage is
          // unavailable.
          if (result.reconnectToken) {
            saveReconnectHandle(result.room.code, result.reconnectToken);
          }
        }
        return result;
      } catch {
        // A thrown invoke (transient disconnect / hub error) must surface as a
        // friendly envelope, never an unhandled rejection (Copilot review).
        return {
          ok: false,
          room: null,
          error: "Something went off - give it a moment and try again.",
          reconnectToken: null,
        };
      }
    },
    [],
  );

  const joinRoom = useCallback(
    async (code: string, displayName: string, variant: string): Promise<JoinResult> => {
      const connection = connectionRef.current;
      if (!connection || connection.state !== HubConnectionState.Connected) {
        return {
          ok: false,
          room: null,
          error: "We're not connected yet - give it a moment and try again.",
          reconnectToken: null,
        };
      }
      try {
        const result = await connection.invoke<JoinResult>('JoinRoom', code, displayName, variant);
        if (result.ok && result.room) {
          inRoomRef.current = true;
          roomCodeRef.current = result.room.code;
          // replay-remix/03: remember the nickname we joined with - the identity
          // handle that handleRosterChanged needs to re-derive `isHost` if a later
          // "Pass the chisel" handoff makes this client the new host.
          myNicknameRef.current = displayName.trim();
          setRoom(result.room);
          setIsHost(false); // a joiner is never the host (AC-05)
          // B4: a fresh successful join supersedes any earlier rejoin-failed notice.
          setRejoinFailedNotice(null);
          // session-engine/09 (AC-01): remember this seat's {code, token} handle
          // device-locally so a later drop / reload can auto-rejoin it.
          if (result.reconnectToken) {
            saveReconnectHandle(result.room.code, result.reconnectToken);
          }
        }
        return result;
      } catch {
        // A thrown invoke must surface as a friendly envelope, not a rejection (Copilot review).
        return {
          ok: false,
          room: null,
          error: "Something went off - give it a moment and try again.",
          reconnectToken: null,
        };
      }
    },
    [],
  );

  const startRound = useCallback(
    async (
      familySafe: boolean,
      lengthPref: LengthPreference,
      mode: string,
      templateId?: string,
    ): Promise<StartRoundResult> => {
      const connection = connectionRef.current;
      const code = roomCodeRef.current;
      if (
        !connection ||
        connection.state !== HubConnectionState.Connected ||
        !code
      ) {
        return {
          ok: false,
          error: "We're not connected yet - give it a moment and try again.",
        };
      }
      // The server enforces the host check and filters the catalog by familySafe
      // (family-safe FIRST) then lengthPref (story-selection/02 AC-03/AC-05,
      // AC-04) - UNLESS an explicit templateId is supplied (story-selection/06,
      // AC-03/AC-04), in which case the server plays that exact template,
      // bypassing length + freshness, still gating on familySafe first (AC-06).
      // `templateId ?? null` maps an omitted 4th argument to the wire's `null`
      // (the hub's optional string parameter), so every existing 3-argument
      // caller reproduces today's random-pick behavior unchanged. We do NOT set
      // `round` here on success: the hub's RoundStarted broadcast drives the
      // transition for EVERYONE (host included) so all players move together
      // (AC-01, AC-02).
      try {
        // Normalize the explicit-pick id client-side: an empty or whitespace-only
        // string is NOT an explicit pick, so send null rather than passing it over
        // the wire (the server treats blank as "no pick" too, but normalizing here
        // avoids ever sending an ambiguous value from a corrupted favorite id).
        const explicitTemplateId = templateId && templateId.trim().length > 0 ? templateId : null;
        const result = await connection.invoke<StartRoundResult>(
          'StartRound',
          code,
          familySafe,
          lengthPref,
          mode,
          explicitTemplateId,
        );
        // session-engine/13 (AC-03/W1): the room this client believed it was
        // still in is gone server-side (most often the idle sweep) - reset
        // local state back to "not in a room" so the existing live-route
        // guards return the player Home instead of a frozen screen.
        if (isRoomNotFoundError(result.error)) {
          resetRoomState();
          // The room is gone server-side, so any persisted reconnect handle for
          // it is now stale - drop it too (Copilot review) so a later reload /
          // auto-reconnect does not attempt a doomed Rejoin into an
          // already-evicted room. Mirrors clearRoom's reset+clear pairing (the
          // server-side LeaveRoom is moot here: the room no longer exists).
          clearReconnectHandle();
        }
        return result;
      } catch {
        // A thrown invoke must surface as a friendly envelope, not a rejection (Copilot review).
        return { ok: false, error: "Something went off - give it a moment and try again." };
      }
    },
    [resetRoomState],
  );

  const backToLobby = useCallback(
    async (): Promise<{ ok: boolean; error: string | null }> => {
      const connection = connectionRef.current;
      const code = roomCodeRef.current;
      if (
        !connection ||
        connection.state !== HubConnectionState.Connected ||
        !code
      ) {
        return {
          ok: false,
          error: "We're not connected yet - give it a moment and try again.",
        };
      }
      // The server enforces the host check (group-play/04, AC-05). We do NOT clear
      // round/reveal here on success: the hub's bare "BackToLobby" broadcast drives
      // that for EVERYONE (host included) so all players fall back to the Lobby
      // together, with the code + roster preserved.
      try {
        const result = await connection.invoke<{ ok: boolean; error: string | null }>('BackToLobby', code);
        // session-engine/13 (AC-03/W1): see startRound's matching comment above.
        if (isRoomNotFoundError(result.error)) {
          resetRoomState();
          // The room is gone server-side, so any persisted reconnect handle for
          // it is now stale - drop it too (Copilot review) so a later reload /
          // auto-reconnect does not attempt a doomed Rejoin into an
          // already-evicted room. Mirrors clearRoom's reset+clear pairing (the
          // server-side LeaveRoom is moot here: the room no longer exists).
          clearReconnectHandle();
        }
        return result;
      } catch {
        // A thrown invoke must surface as a friendly envelope, not a rejection (Copilot review).
        return { ok: false, error: "Something went off - give it a moment and try again." };
      }
    },
    [resetRoomState],
  );

  const passHost = useCallback(
    async (targetNickname: string): Promise<{ ok: boolean; error: string | null }> => {
      const connection = connectionRef.current;
      const code = roomCodeRef.current;
      if (
        !connection ||
        connection.state !== HubConnectionState.Connected ||
        !code
      ) {
        return {
          ok: false,
          error: "We're not connected yet - give it a moment and try again.",
        };
      }
      // The SERVER enforces the host check + the between-rounds phase gate
      // (replay-remix/03, AC-04/AC-05) - we never set `isHost` here on success;
      // the reused "RosterChanged" broadcast (handled above) carries the moved
      // flag to EVERY client, including this one.
      try {
        const result = await connection.invoke<{ ok: boolean; error: string | null }>(
          'PassHost',
          code,
          targetNickname,
        );
        // session-engine/13 (AC-03/W1): see startRound's matching comment above.
        if (isRoomNotFoundError(result.error)) {
          resetRoomState();
          // The room is gone server-side, so any persisted reconnect handle for
          // it is now stale - drop it too (Copilot review) so a later reload /
          // auto-reconnect does not attempt a doomed Rejoin into an
          // already-evicted room. Mirrors clearRoom's reset+clear pairing (the
          // server-side LeaveRoom is moot here: the room no longer exists).
          clearReconnectHandle();
        }
        return result;
      } catch {
        // A thrown invoke must surface as a friendly envelope, not a rejection (Copilot review).
        return { ok: false, error: "Something went off - give it a moment and try again." };
      }
    },
    [resetRoomState],
  );

  const submitWord = useCallback(
    async (blankIndex: number, word: string): Promise<{ accepted: boolean; message?: string }> => {
      const connection = connectionRef.current;
      const code = roomCodeRef.current;
      if (
        !connection ||
        connection.state !== HubConnectionState.Connected ||
        !code
      ) {
        return {
          accepted: false,
          message: "We're not connected yet - give it a moment and try again.",
        };
      }
      // The SERVER runs the safety filter FIRST and records only on pass (AC-01,
      // AC-06); we never set `reveal` here - the RevealReady broadcast drives that
      // for everyone. Map the SubmitWordResult envelope to FillBlank's contract.
      try {
        const result = await connection.invoke<SubmitWordResult>('SubmitWord', code, blankIndex, word);
        // session-engine/13 (AC-03/W1): see startRound's matching comment above.
        if (isRoomNotFoundError(result.error)) {
          resetRoomState();
          // The room is gone server-side, so any persisted reconnect handle for
          // it is now stale - drop it too (Copilot review) so a later reload /
          // auto-reconnect does not attempt a doomed Rejoin into an
          // already-evicted room. Mirrors clearRoom's reset+clear pairing (the
          // server-side LeaveRoom is moot here: the room no longer exists).
          clearReconnectHandle();
        }
        return result.ok
          ? { accepted: true }
          : { accepted: false, message: result.error ?? 'That word is not allowed here. Try another!' };
      } catch {
        // The skip path calls this fire-and-forget, so a thrown invoke would be an
        // unhandled rejection; return a friendly failure instead (Copilot review).
        return { accepted: false, message: "Something went off - give it a moment and try again." };
      }
    },
    [resetRoomState],
  );

  const remixWord = useCallback(
    async (blankIndex: number, word: string): Promise<{ accepted: boolean; message?: string }> => {
      const connection = connectionRef.current;
      const code = roomCodeRef.current;
      if (
        !connection ||
        connection.state !== HubConnectionState.Connected ||
        !code
      ) {
        return {
          accepted: false,
          message: "We're not connected yet - give it a moment and try again.",
        };
      }
      // The SERVER runs the safety filter FIRST and swaps the one blank only on
      // pass (AC-06); we never set `reveal` here - the re-broadcast "RevealReady"
      // drives that for everyone (AC-07), reusing the same handler `submitWord`'s
      // round-complete broadcast already triggers.
      try {
        const result = await connection.invoke<SubmitWordResult>('RemixWord', code, blankIndex, word);
        // session-engine/13 (AC-03/W1): see startRound's matching comment above.
        if (isRoomNotFoundError(result.error)) {
          resetRoomState();
          // The room is gone server-side, so any persisted reconnect handle for
          // it is now stale - drop it too (Copilot review) so a later reload /
          // auto-reconnect does not attempt a doomed Rejoin into an
          // already-evicted room. Mirrors clearRoom's reset+clear pairing (the
          // server-side LeaveRoom is moot here: the room no longer exists).
          clearReconnectHandle();
        }
        return result.ok
          ? { accepted: true }
          : { accepted: false, message: result.error ?? 'That word is not allowed here. Try another!' };
      } catch {
        return { accepted: false, message: 'Something went off - give it a moment and try again.' };
      }
    },
    [resetRoomState],
  );

  const react = useCallback((type: ReactionType) => {
    const connection = connectionRef.current;
    const code = roomCodeRef.current;
    // Fire-and-forget (reveal-delight/01, AC-04): the perceived-responsiveness
    // floater already fired in ReactionRow; the authoritative count arrives via
    // the "ReactionCountsChanged" broadcast. A no-op when not connected / not in a
    // room (a solo player never calls this - Solo bumps local state, AC-05). The
    // payload is a type enum only - no text, no identity (AC-06). A thrown invoke
    // (transient disconnect) is swallowed: a dropped reaction is harmless.
    if (!connection || connection.state !== HubConnectionState.Connected || !code) {
      return;
    }
    void connection.invoke('React', code, type).catch(() => {});
  }, []);

  const castGoldenGuardianVote = useCallback((blankId: string) => {
    const connection = connectionRef.current;
    const code = roomCodeRef.current;
    // Fire-and-forget (reveal-delight/03, AC-01): the SERVER records/moves the vote
    // and broadcasts the live "N of M" (and, on resolution, the winner). A no-op when
    // not connected / not in a room (solo never calls this - it omits the vote step,
    // AC-06). A thrown invoke (transient disconnect) is swallowed: a dropped vote is
    // harmless (the player can tap again). The payload is an already-vetted word's
    // blank token - no text, no PII (AC-07).
    if (!connection || connection.state !== HubConnectionState.Connected || !code) {
      return;
    }
    void connection.invoke('CastGoldenGuardianVote', code, blankId).catch(() => {});
  }, []);

  const closeGoldenGuardianVoting = useCallback(() => {
    const connection = connectionRef.current;
    const code = roomCodeRef.current;
    // Fire-and-forget host-only "Reveal the winner" (reveal-delight/03, AC-03): the
    // SERVER enforces the host check and resolves with whatever votes are in, then
    // broadcasts the winner to everyone. A no-op when not connected / not in a room.
    if (!connection || connection.state !== HubConnectionState.Connected || !code) {
      return;
    }
    void connection.invoke('CloseGoldenGuardianVoting', code).catch(() => {});
  }, []);

  const dismissRoundNotice = useCallback(() => setRoundNotice(null), []);

  const clearRoom = useCallback(() => {
    const connection = connectionRef.current;
    const code = roomCodeRef.current;
    // B4: the shared room/round/reveal reset (also used by a rejected Rejoin) -
    // it marks "left" BEFORE anything else so an in-flight RosterChanged is
    // ignored, exactly as this function always has.
    resetRoomState();
    // session-engine/09 (AC-05): a deliberate leave / Home must never auto-resume
    // later, so the stored reconnect handle is discarded immediately here too.
    clearReconnectHandle();
    // Tell the server so this connection leaves the room group and drops off the
    // roster for everyone else (AC-04). Fire-and-forget: returning Home must not
    // block on the network, and a failure (e.g. already disconnected) is harmless.
    if (connection && connection.state === HubConnectionState.Connected && code) {
      void connection.invoke('LeaveRoom', code).catch(() => {});
    }
  }, [resetRoomState]);

  return {
    status,
    retryConnection,
    room,
    isHost,
    round,
    assignedBlankIndices,
    collectProgress,
    reveal,
    reactionCounts,
    react,
    crownedNickname,
    goldenGuardianVotedCount,
    goldenGuardianTotalVoters,
    goldenGuardianResolved,
    goldenGuardianWinningBlankId,
    castGoldenGuardianVote,
    closeGoldenGuardianVoting,
    submitWord,
    remixWord,
    createRoom,
    joinRoom,
    startRound,
    backToLobby,
    passHost,
    roundNotice,
    dismissRoundNotice,
    clearRoom,
    isRejoining,
    rejoinFailedNotice,
  };
}
