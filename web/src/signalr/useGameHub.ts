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
//  (the join code + the roster with the host). The Player / RoomState types
//  below MIRROR the hub's PlayerDto / RoomStateDto wire contract
//  (api/src/Hubs/GameHub.cs) - keep them in sync.
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
// ----------------------------------------------------------------------------

import { useCallback, useEffect, useRef, useState } from 'react';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';

const HUB_URL = import.meta.env.VITE_SIGNALR_HUB_URL;

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
 * never ships template content), the mode ("classic-blind" in Slice 1), and the
 * 1-based round number. group-play/02 adds a separate per-connection message for
 * each player's own blank assignments.
 */
export interface RoundInfo {
  templateId: string;
  mode: string;
  roundNumber: number;
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

export interface UseGameHub {
  status: ConnectionStatus;
  /** The current room (code + live roster), or null when not in one. Owned here so RosterChanged updates flow to every screen. */
  room: RoomState | null;
  /**
   * Whether THIS client is the host of the current room (session-engine/03).
   * True when this client created the room, false when it joined one, and
   * cleared when it leaves. The Lobby gates the host-only "Start game" CTA on
   * this (AC-05). It is tracked from the caller's own action rather than read
   * off the roster, because IsHost on a PlayerDto is not tied to a connection
   * on the wire (no PII), so a client cannot tell which roster row is "me".
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
   * Start a round as the host (group-play/01). Invokes the hub's host-only
   * StartRound with the current room code (from roomCodeRef) and the host's
   * family-safe toggle value; the SERVER enforces the host check and filters the
   * template catalog by the toggle (authoritative, AC-03/AC-04). Resolves with
   * the StartRoundResult envelope (ok + friendly error). Returns a not-connected
   * error envelope if the hub is not ready. Does NOT set `round` itself - the
   * server's RoundStarted broadcast drives that for everyone, including the host.
   */
  startRound: (familySafe: boolean) => Promise<StartRoundResult>;
  /**
   * Create a room and become its host (session-engine/01). On success updates
   * `room` and resolves with the created room's state, or undefined if the
   * connection is not ready. Uses the ONE shared connection - never a second.
   */
  createRoom: () => Promise<RoomState | undefined>;
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
}

export function useGameHub(): UseGameHub {
  const connectionRef = useRef<HubConnection | null>(null);
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
  // Whether this client is currently seated in a room, plus that room's code.
  // Kept as refs (not state) so the stable RosterChanged handler and clearRoom
  // read the latest value without re-binding. The handler guards on inRoomRef so
  // a roster broadcast that RACES a leave cannot resurrect room state after the
  // player has already gone Home (the post-leave re-entry bug).
  const inRoomRef = useRef(false);
  const roomCodeRef = useRef<string | null>(null);

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl(HUB_URL)
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Information)
      .build();

    connectionRef.current = connection;

    // `cancelled` guards EVERY state update below, so a handler that fires during
    // teardown (StrictMode's dev remount, an HMR swap, or a real unmount racing
    // an in-flight reconnect/close) can never setState on a torn-down effect.
    let cancelled = false;

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
    };
    connection.on('RosterChanged', handleRosterChanged);

    // Round start (group-play/01): the hub broadcasts "RoundStarted" to the whole
    // room group when the host starts a round, so every player - host and joiners
    // alike - moves into word collection together in near-real-time (AC-01,
    // AC-02). Registered ONCE here, before start(), so it is never missed, and
    // guarded by inRoomRef so a broadcast that races a leave cannot pull a player
    // who has gone Home back into a round.
    const handleRoundStarted = (info: RoundInfo) => {
      if (cancelled || !inRoomRef.current) return;
      // Reset any prior round's assignment so a stale one never shows for the new
      // round; "YourBlanks" (below) fills it in a beat later (group-play/02).
      setAssignedBlankIndices(null);
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

    connection.onreconnecting(() => {
      if (!cancelled) setStatus('connecting');
    });
    connection.onreconnected(() => {
      if (!cancelled) setStatus('connected');
    });
    connection.onclose(() => {
      if (!cancelled) setStatus('disconnected');
    });

    connection
      .start()
      .then(() => {
        if (!cancelled) setStatus('connected');
      })
      .catch(() => {
        if (!cancelled) setStatus('disconnected');
      });

    // Tear the connection down on unmount (and on StrictMode's dev remount).
    return () => {
      cancelled = true;
      connection.off('RosterChanged', handleRosterChanged);
      connection.off('RoundStarted', handleRoundStarted);
      connection.off('YourBlanks', handleYourBlanks);
      void connection.stop();
    };
  }, []);

  const createRoom = useCallback(
    async (): Promise<RoomState | undefined> => {
      const connection = connectionRef.current;
      if (!connection || connection.state !== HubConnectionState.Connected) {
        return undefined;
      }
      const created = await connection.invoke<RoomState>('CreateRoom');
      inRoomRef.current = true;
      roomCodeRef.current = created.code;
      setRoom(created);
      setIsHost(true); // the creator is the host (AC-05)
      return created;
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
        };
      }
      const result = await connection.invoke<JoinResult>('JoinRoom', code, displayName, variant);
      if (result.ok && result.room) {
        inRoomRef.current = true;
        roomCodeRef.current = result.room.code;
        setRoom(result.room);
        setIsHost(false); // a joiner is never the host (AC-05)
      }
      return result;
    },
    [],
  );

  const startRound = useCallback(
    async (familySafe: boolean): Promise<StartRoundResult> => {
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
      // (AC-03/AC-04). We do NOT set `round` here on success: the hub's
      // RoundStarted broadcast drives the transition for EVERYONE (host included)
      // so all players move together (AC-01, AC-02).
      return connection.invoke<StartRoundResult>('StartRound', code, familySafe);
    },
    [],
  );

  const clearRoom = useCallback(() => {
    const connection = connectionRef.current;
    const code = roomCodeRef.current;
    // Mark "left" BEFORE anything else so an in-flight RosterChanged is ignored.
    inRoomRef.current = false;
    roomCodeRef.current = null;
    setRoom(null);
    setIsHost(false);
    setRound(null); // group-play/01: drop any in-progress round on leave.
    setAssignedBlankIndices(null); // group-play/02: drop this client's blanks on leave.
    // Tell the server so this connection leaves the room group and drops off the
    // roster for everyone else (AC-04). Fire-and-forget: returning Home must not
    // block on the network, and a failure (e.g. already disconnected) is harmless.
    if (connection && connection.state === HubConnectionState.Connected && code) {
      void connection.invoke('LeaveRoom', code).catch(() => {});
    }
  }, []);

  return {
    status,
    room,
    isHost,
    round,
    assignedBlankIndices,
    createRoom,
    joinRoom,
    startRound,
    clearRoom,
  };
}
