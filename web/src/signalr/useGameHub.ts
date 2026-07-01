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

    // Live roster updates (session-engine/02): the hub broadcasts the updated
    // roster to a room's group whenever someone joins. Registered ONCE here,
    // before start(), so it is never missed and there is only one handler on
    // the one connection. Whoever is in the room (host + players) gets the new
    // roster and the UI updates in near-real-time (AC-05).
    connection.on('RosterChanged', (state: RoomState) => {
      // Ignore a broadcast that arrives after we have left (raced our LeaveRoom /
      // group removal) - otherwise it would resurrect room state and bounce a
      // player who just went Home back into the lobby.
      if (!inRoomRef.current) return;
      setRoom(state);
    });

    connection.onreconnecting(() => setStatus('connecting'));
    connection.onreconnected(() => setStatus('connected'));
    connection.onclose(() => setStatus('disconnected'));

    let cancelled = false;
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

  const clearRoom = useCallback(() => {
    const connection = connectionRef.current;
    const code = roomCodeRef.current;
    // Mark "left" BEFORE anything else so an in-flight RosterChanged is ignored.
    inRoomRef.current = false;
    roomCodeRef.current = null;
    setRoom(null);
    setIsHost(false);
    // Tell the server so this connection leaves the room group and drops off the
    // roster for everyone else (AC-04). Fire-and-forget: returning Home must not
    // block on the network, and a failure (e.g. already disconnected) is harmless.
    if (connection && connection.state === HubConnectionState.Connected && code) {
      void connection.invoke('LeaveRoom', code).catch(() => {});
    }
  }, []);

  return { status, room, isHost, createRoom, joinRoom, clearRoom };
}
