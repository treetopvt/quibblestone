// ----------------------------------------------------------------------------
//  App - the QuibbleStone root, and the app's minimal view router.
//
//  There is NO react-router in this project (CLAUDE.md - and deliberately not
//  added): navigation is a single `view` state switched here. The views are
//  'home', 'join', and 'lobby'; later stories extend this seam (story 03
//  replaces the Lobby placeholder). Keep the switch small so those stories can
//  grow it without a rewrite.
//
//  App owns the ONE SignalR connection via useGameHub (never a second one). The
//  LIVE room state lives IN the hook (so RosterChanged broadcasts update every
//  screen); App reads `room` from there rather than holding its own copy. The
//  Home "Create a game" CTA calls createRoom and lands the host in the lobby
//  (session-engine/01, AC-01). "Join a game" opens the Join screen; a successful
//  join sets the hook's room and flips App to the lobby (session-engine/02,
//  AC-01), while a failed join stays on Join showing the friendly error.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { useCallback, useEffect, useState } from 'react';
import { useGameHub } from './signalr/useGameHub';
import { Home } from './pages/Home';
import { Join } from './pages/Join';
import { Lobby } from './pages/Lobby';

// The set of screens App can show.
type View = 'home' | 'join' | 'lobby';

export default function App() {
  const { status, room, isHost, createRoom, joinRoom, clearRoom } = useGameHub();
  const [view, setView] = useState<View>('home');
  const [creating, setCreating] = useState(false);

  // When a room becomes available (created or joined), land in the lobby. This
  // is the single place a set room flips the view, so both createRoom and a
  // successful joinRoom converge here (AC-01).
  useEffect(() => {
    if (room) {
      setView('lobby');
    }
  }, [room]);

  // "Create a game": ask the hub for a room; the effect above lands us in the
  // lobby once the hook's room is set.
  const handleCreateGame = useCallback(async () => {
    if (creating) return;
    setCreating(true);
    try {
      await createRoom();
    } finally {
      setCreating(false);
    }
  }, [creating, createRoom]);

  // "Join a game": open the Join screen (session-engine/02).
  const handleJoinGame = useCallback(() => {
    setView('join');
  }, []);

  // Leave the lobby / Join screen and return Home (drops the room; rooms are
  // ephemeral and the server sweeps idle ones - AC-05).
  const handleGoHome = useCallback(() => {
    clearRoom();
    setView('home');
  }, [clearRoom]);

  if (view === 'lobby' && room) {
    // onStart is the SEAM for group-play/01 (round start). For story 03 it is a
    // no-op placeholder - the host-only CTA renders, but starting a round is a
    // later story; do not invent round logic here.
    return (
      <Lobby room={room} isHost={isHost} onLeave={handleGoHome} onStart={() => {}} />
    );
  }

  if (view === 'join') {
    return (
      <Join onJoin={joinRoom} onBack={handleGoHome} disabled={status !== 'connected'} />
    );
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
