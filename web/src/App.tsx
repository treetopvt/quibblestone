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
//  Waiting interstitial.
//
//  group-play/04: the replay loop closes here. GroupReveal's "Play another round"
//  now shows the Round Complete recap FIRST (a client-local `showRoundComplete`
//  flag, so each player can view the recap before the next round; AC-01). The crew
//  attribution is DERIVED CLIENT-SIDE from the reveal payload (buildCrew below):
//  the reveal already carries each blank's owner (nickname + variant), so per-player
//  word counts are grouped/counted with no extra server round-trip, and they sum to
//  the total blanks (every blank counts, including skips). RoundComplete takes
//  precedence over GroupReveal for that client while the flag is set. Its host-only
//  gold "Play another round" calls startRound again for the SAME room (no re-join,
//  AC-04) - the server increments the round number and broadcasts RoundStarted,
//  which clears reveal + routes EVERYONE into the new round. Its host-only "Back to
//  lobby" calls backToLobby; the hub's bare "BackToLobby" broadcast clears
//  round/reveal for EVERYONE so all players land back on the still-live Lobby (same
//  code + roster, AC-05). The host's last family-safe choice on the Lobby is kept
//  sticky in App state and reused for the replay (like Solo's toggle). `showRoundComplete`
//  is reset whenever `reveal` clears (a new round starts, or back-to-lobby fires),
//  so a stale recap never persists into the next round.
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
import { RoundComplete, type RoundCompleteCrewMember } from './pages/RoundComplete';
import { FAMILY_SAFE_DEFAULT } from './content/familySafe';
import type { GuardianVariant } from './components';

// The set of screens App can show.
type View = 'home' | 'join' | 'lobby' | 'solo';

/**
 * Derive the per-player crew recap from the shared reveal payload (group-play/04,
 * AC-03/AC-06) - CLIENT-SIDE, no extra server round-trip. The reveal already
 * carries each blank's owner (nickname + Guardian variant, already filtered at
 * join), so we group the ordered words by nickname and count per player. EVERY
 * blank counts toward its owner - including a skipped/empty-word blank - so the
 * per-player counts SUM to reveal.words.length (the total template blanks). A blank
 * with an EMPTY nickname is an unfilled blank (a player who left before submitting):
 * it has no owner, so it is skipped entirely (no PII, no phantom crew member) - the
 * total blanks reported to RoundComplete matches the words that actually have an
 * owner. Crew members come out in first-appearance (reveal) order.
 */
function buildCrew(words: RevealInfo['words']): {
  crew: RoundCompleteCrewMember[];
  totalWords: number;
} {
  const byNickname = new Map<string, RoundCompleteCrewMember>();
  let totalWords = 0;
  for (const w of words) {
    // Skip an unfilled blank (a disconnected player left it empty): no owner, no PII.
    if (w.nickname === '') continue;
    totalWords += 1;
    const existing = byNickname.get(w.nickname);
    if (existing) {
      existing.wordCount += 1;
    } else {
      byNickname.set(w.nickname, {
        nickname: w.nickname,
        variant: w.variant as GuardianVariant,
        wordCount: 1,
      });
    }
  }
  return { crew: [...byNickname.values()], totalWords };
}

/**
 * The shared group reveal (group-play/03, AC-05): resolves the template from the
 * bundled seedLibrary by the reveal's id, maps the hub's ordered reveal words
 * (blank order) into the engine's SubmittedWord[] (playerSessionId = nickname, an
 * anonymous in-session tag - no PII), and assembles the story LOCALLY via the web
 * engine (the ONE place assembly lives - the server never assembles). It then
 * hands the assembled story + template to the shared Reveal AS-IS. A template id
 * with no local match (a catalog / library drift) renders a friendly notice.
 *
 * group-play/04: onPlayAgain now shows the Round Complete recap (App flips its
 * client-local showRoundComplete flag) rather than exiting Home - the recap is what
 * offers the actual replay / back-to-lobby actions.
 */
function GroupReveal({
  reveal,
  onPlayAgain,
  onHome,
}: {
  reveal: RevealInfo;
  onPlayAgain: () => void;
  onHome: () => void;
}) {
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
      onPlayAgain={onPlayAgain}
      playAgainLabel="See the round recap"
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
    backToLobby,
    roundNotice,
    dismissRoundNotice,
    clearRoom,
  } = useGameHub();
  const [view, setView] = useState<View>('home');
  const [creating, setCreating] = useState(false);

  // group-play/04: whether THIS client is viewing the Round Complete recap (a
  // client-local step shown after the reveal, before the next round; AC-01). Set
  // when the player taps "Play another round" on the reveal; reset whenever `reveal`
  // clears (a new round starts, or back-to-lobby fires) so a stale recap never
  // persists into the next round.
  const [showRoundComplete, setShowRoundComplete] = useState(false);

  // group-play/04: a friendly message when a "Play another round" attempt is rejected
  // server-side (e.g. the other carver left so the room is back to one player - the
  // hub needs at least two). Surfaced on the Round Complete recap so the gold CTA is
  // never a live-but-silent no-op; cleared whenever the recap is left (reveal clears).
  const [playAgainError, setPlayAgainError] = useState<string | null>(null);

  // group-play/04: the host's last family-safe choice, kept sticky so a replay in
  // the same room reuses it without re-toggling (like Solo's toggle). Seeded from
  // the shared safe-by-default token. The Lobby's Start CTA persists the host's
  // pick here; RoundComplete's "Play another round" reuses it.
  const [lastFamilySafe, setLastFamilySafe] = useState(FAMILY_SAFE_DEFAULT);

  // When a room becomes available (created or joined), land in the lobby. This
  // is the single place a set room flips the view, so both createRoom and a
  // successful joinRoom converge here (AC-01).
  useEffect(() => {
    if (room) {
      setView('lobby');
    }
  }, [room]);

  // group-play/04: drop a stale Round Complete recap the moment the reveal clears.
  // The hook clears `reveal` both when a fresh round starts (RoundStarted) and when
  // back-to-lobby fires (BackToLobby), so resetting the flag off `reveal` covers
  // both transitions with one guard - the recap can never bleed into the next round
  // or the lobby.
  useEffect(() => {
    if (!reveal) {
      setShowRoundComplete(false);
      setPlayAgainError(null);
    }
  }, [reveal]);

  // Start a round (host) with the host's family-safe pick, remembering it as sticky
  // for the replay loop (group-play/04). Used by the Lobby's Start CTA.
  const handleStartRound = useCallback(
    (familySafe: boolean) => {
      setLastFamilySafe(familySafe);
      void startRound(familySafe);
    },
    [startRound],
  );

  // group-play/04: "Play another round" from the Round Complete recap (host). Reuses
  // the SAME room + players (no re-join, AC-04) and the host's sticky family-safe
  // pick. The server increments the round number and broadcasts RoundStarted, which
  // clears reveal (resetting showRoundComplete via the effect above) and routes
  // EVERYONE into the new round.
  const handlePlayAnotherRound = useCallback(async () => {
    setPlayAgainError(null);
    const result = await startRound(lastFamilySafe);
    // A rejected start (a carver left so the room is back to one player, or a
    // transient not-connected) resolves ok=false; surface the friendly reason on the
    // recap rather than leaving the gold CTA a silent no-op. On success the server's
    // RoundStarted broadcast clears reveal and routes everyone into the new round.
    if (!result.ok) {
      setPlayAgainError(result.error ?? 'Could not start another round - please try again.');
    }
  }, [startRound, lastFamilySafe]);

  // group-play/04: "Back to lobby" from the Round Complete recap (host). The hub's
  // bare "BackToLobby" broadcast clears round/reveal for EVERYONE so all players land
  // back on the still-live Lobby with the code + roster preserved (AC-05).
  const handleBackToLobby = useCallback(() => {
    void backToLobby();
  }, [backToLobby]);

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

  // group-play/04: once THIS client taps "Play another round" on the reveal, show
  // the Round Complete recap (AC-01) - checked AHEAD of GroupReveal so it takes
  // precedence for this client until the host acts. It needs both `reveal` (crew +
  // title, derived client-side) and `round` (round.roundNumber for the badge), which
  // are still set here (RevealReady does NOT clear `round`). The crew attribution is
  // derived from the reveal payload with no extra server round-trip.
  if (showRoundComplete && reveal && round && room) {
    const template = seedLibrary.find((t) => t.id === reveal.templateId);
    const { crew, totalWords } = buildCrew(reveal.words);
    return (
      <RoundComplete
        roundNumber={round.roundNumber}
        title={template ? template.title : 'Your tale'}
        crew={crew}
        totalWords={totalWords}
        isHost={isHost}
        canPlayAgain={room.players.length >= 2}
        playAgainError={playAgainError}
        onPlayAgain={() => void handlePlayAnotherRound()}
        onBackToLobby={handleBackToLobby}
        onLeave={handleGoHome}
      />
    );
  }

  // group-play/03: the shared reveal takes precedence over the round (AC-05). When
  // the hub's RevealReady broadcast lands, `reveal` is set for EVERY player at once
  // - done or still on the Waiting screen - and everyone routes to the shared
  // Reveal in near-real-time without a refresh. Checked AHEAD of the round below so
  // a finished round lands on the payoff, not back in FillBlank. group-play/04:
  // "Play another round" flips showRoundComplete (handled above) rather than exiting.
  if (reveal && room) {
    return (
      <GroupReveal
        reveal={reveal}
        onPlayAgain={() => setShowRoundComplete(true)}
        onHome={handleGoHome}
      />
    );
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
        onStart={handleStartRound}
        notice={roundNotice}
        onDismissNotice={dismissRoundNotice}
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
