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
//
//  group-play/03: the hook's `reveal` (set from the hub's RevealReady broadcast the
//  moment the LAST assigned blank is submitted) is the SHARED reveal seam. When it
//  becomes non-null - for EVERY player at once (AC-05) - App routes to the shared
//  Reveal (GroupReveal below), which resolves the template from the bundled
//  seedLibrary and assembles the story LOCALLY via the web engine (the server ships
//  ordered words, never an assembled story). This is checked AHEAD of the round so
//  a finished round lands on the reveal, not back in FillBlank. `collectProgress`
//  and `submitWord` flow into GroupRound so it can submit server-side and show the
//  Waiting interstitial. GroupReveal's "Play another round" is interim here (the
//  Round Complete replay loop is group-play/04) - it exits to Home for now.
//
//  'solo' (single-player/01, ADDITIVE) is a self-contained local flow: Solo
//  never touches `room`, `isHost`, or any hub call - it ignores the room
//  state entirely, so it is checked ahead of the room-driven views below. The
//  existing home/join/lobby wiring is untouched by this addition.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { useCallback, useEffect, useState } from 'react';
import { useGameHub, type RevealInfo } from './signalr/useGameHub';
import { seedLibrary } from './content/seedLibrary';
import { assemble, type SubmittedWord } from './engine/assemble';
import { Home } from './pages/Home';
import { Join } from './pages/Join';
import { Lobby } from './pages/Lobby';
import { Solo } from './pages/Solo';
import { GroupRound } from './pages/GroupRound';
import { Reveal } from './pages/Reveal';

// The set of screens App can show.
type View = 'home' | 'join' | 'lobby' | 'solo';

/**
 * The shared group reveal (group-play/03, AC-05): resolves the template from the
 * bundled seedLibrary by the reveal's id, maps the hub's ordered reveal words
 * (blank order) into the engine's SubmittedWord[] (playerSessionId = nickname, an
 * anonymous in-session tag - no PII), and assembles the story LOCALLY via the web
 * engine (the ONE place assembly lives - the server never assembles). It then
 * hands the assembled story + template to the shared Reveal AS-IS. A template id
 * with no local match (a catalog / library drift) renders a friendly notice.
 *
 * onPlayAgain is INTERIM for gp/03 (the Round Complete replay loop is gp/04) - it
 * exits to Home for now; gp/04 owns the full crew recap + replay wiring.
 */
function GroupReveal({ reveal, onHome }: { reveal: RevealInfo; onHome: () => void }) {
  const template = seedLibrary.find((t) => t.id === reveal.templateId);

  if (!template) {
    // A catalog / library drift (the round used a template this device does not
    // have). Rather than crash, send the player home with a calm message.
    return (
      <Home
        onCreateGame={onHome}
        onJoinGame={onHome}
        onPlaySolo={onHome}
        creating={false}
        disabled={false}
      />
    );
  }

  // Map the ordered reveal words into SubmittedWord[] (blank order). The
  // playerSessionId is the anonymous nickname (no PII); assemble() pairs each word
  // to its blank purely by position, exactly the contract the server built to.
  const words: SubmittedWord[] = reveal.words.map((w) => ({
    playerSessionId: w.nickname,
    word: w.word,
  }));
  const assembled = assemble(template, words);

  return (
    <Reveal
      assembled={assembled}
      template={template}
      onPlayAgain={onHome}
      onHome={onHome}
      exitAction={{ label: 'Back to home', onClick: onHome }}
    />
  );
}

export default function App() {
  const {
    status,
    room,
    isHost,
    round,
    assignedBlankIndices,
    collectProgress,
    reveal,
    submitWord,
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

  // group-play/03: the shared reveal takes precedence over the round (AC-05). When
  // the hub's RevealReady broadcast lands, `reveal` is set for EVERY player at once
  // - done or still on the Waiting screen - and everyone routes to the shared
  // Reveal in near-real-time without a refresh. Checked AHEAD of the round below so
  // a finished round lands on the payoff, not back in FillBlank.
  if (reveal && room) {
    return <GroupReveal reveal={reveal} onHome={handleGoHome} />;
  }

  // group-play/01: once a round has started, EVERY player in the room routes into
  // it (the hook's `round` is set from the RoundStarted broadcast for everyone,
  // AC-01/AC-02). This takes precedence over the lobby view. group-play/03 wires
  // GroupRound to submit words server-side (submitWord) and pass collection
  // progress into its Waiting interstitial. Leaving the round clears the room (and
  // the round) and returns Home.
  if (round && room) {
    return (
      <GroupRound
        templateId={round.templateId}
        assignedBlankIndices={assignedBlankIndices}
        collectProgress={collectProgress}
        submitWord={submitWord}
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
