// ----------------------------------------------------------------------------
//  useGameHub - React hook that owns the SignalR connection to the API's
//  GameHub. It is the web client's half of the real-time walking skeleton.
//
//  Responsibilities:
//    - Build and start ONE HubConnection (with automatic reconnect).
//    - Expose the live connection status so the UI can show connected / not.
//    - Expose ping(message), which invokes the hub's Ping method and resolves
//      with the server echo.
//
//  Real game features (rooms, rosters, reveal) add more invokes/handlers on
//  this same connection rather than opening new ones.
//
//  session-engine/01 adds the first game invoke, createRoom, which asks the
//  hub's CreateRoom method to mint a room and returns the created room's state
//  (the join code + the roster with the host). The Player / RoomState types
//  below MIRROR the hub's PlayerDto / RoomStateDto wire contract
//  (api/src/Hubs/GameHub.cs) - keep them in sync. Later stories (02 join,
//  05 avatar, 03 roster) add joinRoom + a roster-changed handler here.
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

export interface UseGameHub {
  status: ConnectionStatus;
  ping: (message: string) => Promise<string | undefined>;
  /**
   * Create a room and become its host (session-engine/01). Resolves with the
   * created room's state (code + roster), or undefined if the connection is not
   * ready. Uses the ONE shared connection - never opens a second.
   */
  createRoom: () => Promise<RoomState | undefined>;
}

export function useGameHub(): UseGameHub {
  const connectionRef = useRef<HubConnection | null>(null);
  const [status, setStatus] = useState<ConnectionStatus>('connecting');

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl(HUB_URL)
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Information)
      .build();

    connectionRef.current = connection;

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

  const ping = useCallback(
    async (message: string): Promise<string | undefined> => {
      const connection = connectionRef.current;
      if (!connection || connection.state !== HubConnectionState.Connected) {
        return undefined;
      }
      return connection.invoke<string>('Ping', message);
    },
    [],
  );

  const createRoom = useCallback(
    async (): Promise<RoomState | undefined> => {
      const connection = connectionRef.current;
      if (!connection || connection.state !== HubConnectionState.Connected) {
        return undefined;
      }
      return connection.invoke<RoomState>('CreateRoom');
    },
    [],
  );

  return { status, ping, createRoom };
}
