// ----------------------------------------------------------------------------
//  App - the QuibbleStone root and its router (design-system/04, react-router).
//
//  Routing model: real URLs via react-router, but REAL-TIME STATE STAYS THE
//  AUTHORITY. The routes are '/' (Home), '/host' (HostSetup), '/join' +
//  '/join/:code' (Join, deep-link pre-filled), '/solo', '/lobby', '/round',
//  '/reveal', '/recap'. Entry screens ('/', '/host', '/join', '/solo') are
//  user-driven. The LIVE game screens are driven by the hub: a single effect
//  derives the target path from hook state (reveal-recap > reveal > round >
//  lobby - the same precedence the old `view` switch used) and navigates there,
//  so a RoundStarted / RevealReady broadcast still routes EVERY player into the
//  round / shared reveal with no refresh (group-play/01, /03, /04). This is a
//  faithful refactor of the prior view-state router - the flow is unchanged; the
//  URL now reflects it (design-system/04, AC-03).
//
//  App owns the ONE SignalR connection via useGameHub, called ABOVE <Routes> so
//  navigation never remounts or duplicates it (AC-02). The LIVE room state lives
//  IN the hook (so RosterChanged broadcasts update every screen); App reads
//  `room` from there. The Home "Create a game" CTA opens HostSetup (the host
//  names itself + picks a Guardian BEFORE the room is minted); its onCreate calls
//  createRoom with that vetted name + variant (safety-filtered server-side) and
//  the room-effect routes the host into the lobby (session-engine/01, AC-01).
//  "Join a game" opens Join; a successful join sets the hook's room and the same
//  effect routes to the lobby (session-engine/02), while a failed join stays on
//  Join with the friendly error. A '/join/:code' deep link pre-fills the code
//  (session-engine/06). On a successful create OR join we remember the name +
//  Guardian device-local (identity.ts) and pre-fill both screens next time - no
//  PII off-device, no account.
//
//  group-play/03+04: the hook's `reveal` (RevealReady) is the SHARED reveal seam;
//  the client-local `showRoundComplete` flag layers the Round Complete recap over
//  it (the recap offers the actual replay / back-to-lobby actions). The crew
//  attribution is DERIVED CLIENT-SIDE from the reveal payload (buildCrew below):
//  the reveal already carries each blank's owner (nickname + variant), so
//  per-player word counts are grouped with no extra server round-trip, summing to
//  the total blanks (skips included). "Play another round" (host) calls startRound
//  again for the SAME room (no re-join, AC-04); "Back to lobby" clears round/reveal
//  for EVERYONE so all players land back on the still-live Lobby. `showRoundComplete`
//  resets whenever `reveal` clears, so a stale recap never persists.
//
//  'solo' (single-player/01) is a self-contained local flow: Solo never touches
//  `room`, `isHost`, or any hub call, so it lives at '/solo' with no live-state
//  precedence over it.
//
//  Refresh safety (AC-05): a live-game URL opened with no room (rooms are
//  ephemeral; rejoin is the separate resilience track) redirects home rather than
//  rendering a broken shell - the per-route guards below handle that.
//
//  story-selection/02 threads the host's story-length choice through the SAME
//  seam the family-safe flag already uses: handleStartRound now takes
//  (familySafe, lengthPref), remembers lengthPref as a sticky `lastLengthPref`
//  (mirroring lastFamilySafe) for the "Play another round" replay, and passes
//  both to the hook's startRound as ONE more parameter on the existing invoke -
//  no new hub method. The Lobby's onStart type is updated to match.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { useCallback, useEffect, useState } from 'react';
import { Navigate, Route, Routes, useLocation, useNavigate, useParams } from 'react-router-dom';
import { useGameHub, type RevealInfo } from './signalr/useGameHub';
import { seedLibrary } from './content/seedLibrary';
import { assemble, type SubmittedWord } from './engine/assemble';
import { Home } from './pages/Home';
import { Join, type JoinProps } from './pages/Join';
import { HostSetup } from './pages/HostSetup';
import { Lobby } from './pages/Lobby';
import { Solo } from './pages/Solo';
import { GroupRound } from './pages/GroupRound';
import { Reveal } from './pages/Reveal';
import { RoundComplete, type RoundCompleteCrewMember } from './pages/RoundComplete';
import { FAMILY_SAFE_DEFAULT } from './content/familySafe';
import type { LengthPreference } from './content/length';
import { DEFAULT_VARIANT } from './components';
import { toGuardianVariant, type GuardianVariant } from './components';
import { loadIdentity, saveIdentity } from './identity';

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
        variant: toGuardianVariant(w.variant),
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
 * with no local match (a catalog / library drift) sends the player home calmly.
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

/**
 * Join route wrapper: reads the optional `:code` route param (design-system/04,
 * session-engine/06 deep link) and hands it to Join as `initialCode` (Join
 * normalizes it through the same rule as typed input). Serves BOTH '/join' (no
 * param) and '/join/:code'. Kept a module-scope component (not an inline element)
 * so it is not redefined on every App render.
 */
function JoinRoute(props: Omit<JoinProps, 'initialCode'>) {
  const { code } = useParams();
  // `key` on the route param forces Join to remount when the `:code` changes, so
  // navigating between share links (e.g. /join/AAAA -> /join/BBBB) within the SPA
  // re-seeds the form. Join uses react-hook-form `defaultValues`, which only apply
  // on mount, so without this the pre-filled code would go stale (Copilot review).
  return <Join {...props} initialCode={code ?? ''} key={code ?? 'join'} />;
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

  const navigate = useNavigate();
  const location = useLocation();

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
  // the same room reuses it without re-toggling (like Solo's toggle).
  const [lastFamilySafe, setLastFamilySafe] = useState(FAMILY_SAFE_DEFAULT);

  // story-selection/02: the host's last story-length choice, kept sticky the
  // SAME way as lastFamilySafe (client-sticky, NOT room state) so a replay in
  // the same room reuses it without re-picking. Defaults to 'full' (AC-06).
  const [lastLengthPref, setLastLengthPref] = useState<LengthPreference>('full');

  // build/host-identity: pre-fill HostSetup + Join from the player's last-used
  // name + Guardian (device-local via identity.ts; NO PII off-device, NO account).
  // Read ONCE on mount. Falls back to '' + the default variant on a fresh device.
  const [identity] = useState(() => loadIdentity());
  const initialNickname = identity?.nickname ?? '';
  const initialVariant: GuardianVariant = identity?.variant ?? DEFAULT_VARIANT;

  // Real-time state is the AUTHORITY; the URL reflects it (design-system/04, AC-03).
  // When the hub sets room/round/reveal (or the client flips the recap flag), route
  // into the matching live screen using the SAME precedence the old view switch used
  // (recap > reveal > round > lobby). Entry screens (no room) are user-driven, so we
  // never force-navigate there. `replace` + the pathname guard prevent nav loops.
  useEffect(() => {
    if (!room) return;
    const target =
      reveal && showRoundComplete ? '/recap' : reveal ? '/reveal' : round ? '/round' : '/lobby';
    if (location.pathname !== target) {
      navigate(target, { replace: true });
    }
  }, [room, round, reveal, showRoundComplete, location.pathname, navigate]);

  // group-play/04: drop a stale Round Complete recap the moment the reveal clears.
  // The hook clears `reveal` both when a fresh round starts (RoundStarted) and when
  // back-to-lobby fires (BackToLobby), so resetting the flag off `reveal` covers both
  // transitions with one guard.
  useEffect(() => {
    if (!reveal) {
      setShowRoundComplete(false);
      setPlayAgainError(null);
    }
  }, [reveal]);

  // Start a round (host) with the host's family-safe + story-length picks,
  // remembering both as sticky for the replay loop (group-play/04,
  // story-selection/02). Used by the Lobby's Start CTA.
  const handleStartRound = useCallback(
    (familySafe: boolean, lengthPref: LengthPreference) => {
      setLastFamilySafe(familySafe);
      setLastLengthPref(lengthPref);
      void startRound(familySafe, lengthPref);
    },
    [startRound],
  );

  // group-play/04: "Play another round" from the Round Complete recap (host). Reuses
  // the SAME room + players (no re-join, AC-04) and the host's sticky family-safe pick.
  // The server increments the round and broadcasts RoundStarted, which clears reveal
  // (resetting showRoundComplete) and routes EVERYONE into the new round.
  const handlePlayAnotherRound = useCallback(async () => {
    setPlayAgainError(null);
    const result = await startRound(lastFamilySafe, lastLengthPref);
    // A rejected start (a carver left so the room is back to one player, or a
    // transient not-connected) resolves ok=false; surface the friendly reason on the
    // recap rather than leaving the gold CTA a silent no-op. On success the server's
    // RoundStarted broadcast clears reveal and routes everyone into the new round.
    if (!result.ok) {
      setPlayAgainError(result.error ?? 'Could not start another round - please try again.');
    }
  }, [startRound, lastFamilySafe, lastLengthPref]);

  // group-play/04: "Back to lobby" from the Round Complete recap (host). The hub's
  // bare "BackToLobby" broadcast clears round/reveal for EVERYONE so all players land
  // back on the still-live Lobby with the code + roster preserved (AC-05).
  const handleBackToLobby = useCallback(() => {
    void backToLobby();
  }, [backToLobby]);

  // "Create a game": open HostSetup (build/host-identity) so the host names itself +
  // picks a Guardian BEFORE the room is minted. The create happens on HostSetup's
  // onCreate; the room-effect above routes the host into the lobby once room is set.
  const handleCreateGame = useCallback(() => {
    navigate('/host');
  }, [navigate]);

  // HostSetup's onCreate: mint the room with the host's chosen name + variant (server
  // safety-filters the name); on ok remember the identity device-local and the
  // room-effect lands the host in the lobby, on !ok the friendly error is returned to
  // HostSetup to show inline.
  const handleCreateRoom = useCallback(
    async (displayName: string, variant: string) => {
      const result = await createRoom(displayName, variant);
      if (result.ok) {
        saveIdentity(displayName.trim(), toGuardianVariant(variant));
      }
      return result;
    },
    [createRoom],
  );

  // "Join a game": open the Join screen (session-engine/02).
  const handleJoinGame = useCallback(() => {
    navigate('/join');
  }, [navigate]);

  // Join's onJoin (wraps session-engine/02's joinRoom): on a successful join, remember
  // the identity device-local so a returning joiner is pre-filled next time.
  const handleJoinRoom = useCallback(
    async (code: string, displayName: string, variant: string) => {
      const result = await joinRoom(code, displayName, variant);
      if (result.ok) {
        saveIdentity(displayName.trim(), toGuardianVariant(variant));
      }
      return result;
    },
    [joinRoom],
  );

  // "Or play solo right now" (single-player/01): no hub call, no room - a route change.
  const handlePlaySolo = useCallback(() => {
    navigate('/solo');
  }, [navigate]);

  // Leave the current flow and return Home (drops the room; rooms are ephemeral and
  // the server sweeps idle ones). Solo never sets a room, so clearRoom() is a harmless
  // no-op when returning from '/solo'.
  const handleGoHome = useCallback(() => {
    clearRoom();
    navigate('/');
  }, [clearRoom, navigate]);

  // Recap element (group-play/04): needs `reveal` (crew + title) and `round`
  // (round.roundNumber for the badge), both still set when the recap shows.
  // templateId is passed for story-selection/05's quiet thumbs feedback. Guarded
  // on `showRoundComplete` too so a manual hit on /recap during a reveal renders
  // nothing (the state-authoritative effect then routes correctly), avoiding a
  // recap flash before the redirect (Copilot review).
  const recapElement =
    showRoundComplete && reveal && round && room ? (
      (() => {
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
            templateId={reveal.templateId}
          />
        );
      })()
    ) : (
      <Navigate to="/" replace />
    );

  return (
    <Routes>
      <Route
        path="/"
        element={
          <Home
            onCreateGame={handleCreateGame}
            onJoinGame={handleJoinGame}
            onPlaySolo={handlePlaySolo}
            disabled={status !== 'connected'}
          />
        }
      />
      <Route
        path="/host"
        element={
          <HostSetup
            onCreate={handleCreateRoom}
            onBack={handleGoHome}
            disabled={status !== 'connected'}
            initialNickname={initialNickname}
            initialVariant={initialVariant}
          />
        }
      />
      {/* '/join' and '/join/:code' share JoinRoute; the deep link seeds the code. */}
      {['/join', '/join/:code'].map((path) => (
        <Route
          key={path}
          path={path}
          element={
            <JoinRoute
              onJoin={handleJoinRoom}
              onBack={handleGoHome}
              disabled={status !== 'connected'}
              initialNickname={initialNickname}
              initialVariant={initialVariant}
            />
          }
        />
      ))}
      <Route path="/solo" element={<Solo onExit={handleGoHome} />} />
      {/* Live game screens: guarded so a refresh with no live room redirects home
          (AC-05). The real-time effect above keeps the URL in sync while playing. */}
      <Route
        path="/lobby"
        element={
          room ? (
            <Lobby
              room={room}
              isHost={isHost}
              onLeave={handleGoHome}
              onStart={handleStartRound}
              notice={roundNotice}
              onDismissNotice={dismissRoundNotice}
            />
          ) : (
            <Navigate to="/" replace />
          )
        }
      />
      <Route
        path="/round"
        element={
          round && room ? (
            <GroupRound
              // Remount per round so GroupRound's internal state always starts fresh
              // on a replay - never inherits the previous round's phase or words.
              key={round.roundNumber}
              templateId={round.templateId}
              assignedBlankIndices={assignedBlankIndices}
              collectProgress={collectProgress}
              submitWord={submitWord}
              onLeave={handleGoHome}
            />
          ) : (
            <Navigate to="/" replace />
          )
        }
      />
      <Route
        path="/reveal"
        element={
          reveal && room ? (
            <GroupReveal
              reveal={reveal}
              onPlayAgain={() => setShowRoundComplete(true)}
              onHome={handleGoHome}
            />
          ) : (
            <Navigate to="/" replace />
          )
        }
      />
      <Route path="/recap" element={recapElement} />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}
