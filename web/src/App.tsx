// ----------------------------------------------------------------------------
//  App - the QuibbleStone root, and the app's minimal view router.
//
//  There is NO react-router in this project (CLAUDE.md - and deliberately not
//  added): navigation is a single `view` state switched here. Story 01 has two
//  views - 'home' and 'lobby'; later stories extend this seam (story 02 adds
//  'join', story 03 replaces the Lobby placeholder). Keep the switch small and
//  the room state lifted here so those stories can grow it without a rewrite.
//
//  App owns the ONE SignalR connection via useGameHub (never a second one). The
//  Home screen's "Create a game" CTA calls the hub's createRoom; when it
//  resolves with the room state (code + host roster), App stores it and flips
//  the view to the lobby, landing the host in their room (session-engine/01,
//  AC-01). "Join a game" is a seam for story 02 and is a no-op for now.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { useCallback, useState } from 'react';
import { useGameHub } from './signalr/useGameHub';
import type { RoomState } from './signalr/useGameHub';
import { Home } from './pages/Home';
import { Lobby } from './pages/Lobby';

// The set of screens App can show. Story 02 adds 'join'.
type View = 'home' | 'lobby';

export default function App() {
  const { status, createRoom } = useGameHub();
  const [view, setView] = useState<View>('home');
  const [room, setRoom] = useState<RoomState | null>(null);
  const [creating, setCreating] = useState(false);

  // "Create a game": ask the hub for a room, then land in the lobby as host.
  const handleCreateGame = useCallback(async () => {
    if (creating) return;
    setCreating(true);
    try {
      const created = await createRoom();
      if (created) {
        setRoom(created);
        setView('lobby');
      }
    } finally {
      setCreating(false);
    }
  }, [creating, createRoom]);

  // "Join a game" is the story-02 seam - a no-op placeholder until the Join
  // screen exists. Wired now so the Home contract (AC-01) is complete.
  const handleJoinGame = useCallback(() => {
    // Story 02 will setView('join') here.
  }, []);

  // Leave the lobby and return Home (drops the local room state; rooms are
  // ephemeral and the server sweeps idle ones - AC-05).
  const handleLeaveLobby = useCallback(() => {
    setRoom(null);
    setView('home');
  }, []);

  if (view === 'lobby' && room) {
    return <Lobby room={room} onLeave={handleLeaveLobby} />;
  }

  return (
    <Home
      onCreateGame={() => void handleCreateGame()}
      onJoinGame={handleJoinGame}
      creating={creating}
      disabled={status !== 'connected'}
    />
  );
}
