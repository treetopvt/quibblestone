// ----------------------------------------------------------------------------
//  App - the QuibbleStone root, and the app's minimal view router.
//
//  There is NO react-router in this project (CLAUDE.md - and deliberately not
//  added): navigation is a single `view` state switched here. The views are
//  'home', 'join', 'lobby', and 'solo'; later stories extend this seam. Keep the
//  switch small so those stories can grow it without a rewrite.
//
//  App owns the ONE SignalR connection via useGameHub (never a second one). The
//  LIVE room state lives IN the hook (so RosterChanged broadcasts update every
//  screen); App reads `room` from there rather than holding its own copy. The
//  Home "Create a game" CTA calls createRoom and lands the host in the lobby
//  (session-engine/01, AC-01). "Join a game" opens the Join screen; a successful
//  join sets the hook's room and flips App to the lobby (session-engine/02,
//  AC-01), while a failed join stays on Join showing the friendly error.
//
//  group-play/01: the hook's `round` (set from the hub's RoundStarted broadcast)
//  is the round seam. When it becomes non-null - which happens for EVERY player
//  in the room the moment the host starts (AC-01, AC-02) - App routes into the
//  round (GroupRound) regardless of the current view. The Lobby's host-only Start
//  CTA is wired to startRound (host-only + family-safe-filtered server-side).
//  GroupRound is INTERIM (see its header): group-play/02 and /03 replace it with
//  assigned blanks + hub submit + the shared reveal.
//
//  'solo' (single-player/01, ADDITIVE) is a self-contained local flow: Solo
//  never touches `room`, `isHost`, or any hub call - it ignores the room
//  state entirely, so it is checked ahead of the room-driven views below. The
//  existing home/join/lobby wiring is untouched by this addition.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { useCallback, useEffect, useState } from 'react';
import { useGameHub } from './signalr/useGameHub';
import { Home } from './pages/Home';
import { Join } from './pages/Join';
import { Lobby } from './pages/Lobby';
import { Solo } from './pages/Solo';
import { GroupRound } from './pages/GroupRound';

// The set of screens App can show.
type View = 'home' | 'join' | 'lobby' | 'solo';

export default function App() {
  const {
    status,
    room,
    isHost,
    round,
    assignedBlankIndices,
    createRoom,
    joinRoom,
    startRound,
    clearRoom,
  } = useGameHub();
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

  // "Or play solo right now" (single-player/01): no hub call, no room - just
  // a local view change.
  const handlePlaySolo = useCallback(() => {
    setView('solo');
  }, []);

  // Leave the lobby / Join screen and return Home (drops the room; rooms are
  // ephemeral and the server sweeps idle ones - AC-05). Solo never sets a
  // room, so clearRoom() is a harmless no-op when returning from 'solo'.
  const handleGoHome = useCallback(() => {
    clearRoom();
    setView('home');
  }, [clearRoom]);

  if (view === 'solo') {
    return <Solo onExit={handleGoHome} />;
  }

  // group-play/01: once a round has started, EVERY player in the room routes into
  // it (the hook's `round` is set from the RoundStarted broadcast for everyone,
  // AC-01/AC-02). This takes precedence over the lobby view. Leaving the round
  // clears the room (and the round) and returns Home.
  if (round && room) {
    return (
      <GroupRound
        templateId={round.templateId}
        assignedBlankIndices={assignedBlankIndices}
        onLeave={handleGoHome}
      />
    );
  }

  if (view === 'lobby' && room) {
    // The host-only Start CTA calls onStart with the host's family-safe toggle
    // value; startRound invokes the hub (host-only + server-filtered, AC-03/
    // AC-04). We do NOT flip the view here - the server's RoundStarted broadcast
    // sets the hook's `round`, which routes everyone (host included) into the
    // round above. A rejected start (non-host, too few players) resolves ok=false;
    // the CTA is only shown to the host with a full roster, so it is a safe no-op.
    return (
      <Lobby
        room={room}
        isHost={isHost}
        onLeave={handleGoHome}
        onStart={(familySafe) => void startRound(familySafe)}
      />
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
      onPlaySolo={handlePlaySolo}
      creating={creating}
      disabled={status !== 'connected'}
    />
  );
}
