// ----------------------------------------------------------------------------
//  App - the QuibbleStone root and its router (design-system/04, react-router).
//
//  Routing model: real URLs via react-router, but REAL-TIME STATE STAYS THE
//  AUTHORITY. The routes are '/' (Home), '/host' (HostSetup), '/join' +
//  '/join/:code' (Join, deep-link pre-filled), '/solo', '/favorites',
//  '/gallery', '/lobby', '/round', '/reveal', '/recap'. Entry screens ('/',
//  '/host', '/join', '/solo', '/favorites', '/gallery') are user-driven. The
//  LIVE game screens are driven by the hub: a single effect
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
//  keepsake-gallery/02 (PART C): GroupReveal turns the SAME `buildCrew` crew list
//  into a plain-text "carved by [names]" byline (../gallery/byline.ts's
//  formatCrewByline) passed as Reveal's `saveImageByline` prop, so the saved/shared
//  tablet image finally carries a byline for group play (keepsake-gallery/01's
//  previously-unwired seam) - no second data source, no hub call.
//
//  keepsake-gallery/03 adds '/gallery' (the device-local "Tales we've carved"
//  history screen, Gallery.tsx) as one more user-driven entry screen, wired
//  from Home's new "Tales we've carved" tertiary nav link exactly like
//  '/favorites' - a plain route change, no hub call, no room.
//
//  'solo' (single-player/01) is a self-contained local flow: Solo never touches
//  `room`, `isHost`, or any hub call, so it lives at '/solo' with no live-state
//  precedence over it.
//
//  Refresh safety (AC-05): a live-game URL opened with no room (rooms are
//  ephemeral; rejoin is the separate resilience track) redirects home rather than
//  rendering a broken shell - the per-route guards below handle that.
//
//  session-engine/10 narrows WHEN that redirect fires: a stored reconnect handle
//  (../reconnect.ts's loadReconnectHandle) means a resume may still be pending,
//  so the guards hold the screen with a calm "reconnecting your game..." beat
//  (ResumingLiveScreen below) instead of bouncing Home first. This covers BOTH
//  the cold-reload window (the connection has not yet reached "connected", so
//  story 09's mount-time Rejoin has not had a chance to fire) and the in-flight-
//  Rejoin window (the hook's `isRejoining`, story 09) - see the pure
//  `shouldHoldLiveRouteForResume` below for the exact combined condition. Once a
//  resume resolves - success (room/round/reveal populate, and the routing effect
//  above lands the player on the right screen with no extra code, AC-02) or
//  failure (story 09 discards the stale handle, AC-04) - the guard falls
//  straight through to today's `<Navigate to="/" />` (AC-03). This story does
//  NOT touch the reconnect MECHANICS (grace window, token, Rejoin, auto-rejoin
//  triggers) - stories 07-09 own those; this is presentation + routing only.
//
//  story-selection/02 threads the host's story-length choice through the SAME
//  seam the family-safe flag already uses: handleStartRound now takes
//  (familySafe, lengthPref), remembers lengthPref as a sticky `lastLengthPref`
//  (mirroring lastFamilySafe) for the "Play another round" replay, and passes
//  both to the hook's startRound as ONE more parameter on the existing invoke -
//  no new hub method. The Lobby's onStart type is updated to match.
//
//  story-selection/06 ("Favorite a story and replay it (device-local)") adds
//  the '/favorites' entry screen plus TWO replay seams on the SAME startRound
//  invoke (never a new hub method, AC-03):
//    - SOLO: picking a favorite on the Favorites screen sets `pendingFavorite`
//      (a tiny `{ templateId }`) and navigates to '/solo', which renders Solo
//      with `initialFavorite={pendingFavorite}` - Solo's own mount effect does
//      the actual family-safe-gated, freshness-bypassing start (AC-04/AC-06).
//      `pendingFavorite` is cleared right after navigating so a LATER, plain
//      "Or play solo right now" visit to '/solo' never re-fires the same
//      favorite (Solo only ever consumes `initialFavorite` once per mount).
//    - GROUP: the host picks from an INLINE favorites picker on Lobby.tsx
//      (no navigation, room state stays put); `handlePlayFavorite` invokes the
//      hub's startRound with the Lobby's CURRENT family-safe toggle (AC-06)
//      plus the picked `templateId` - the server plays that EXACT template,
//      still family-safe-gated first (AC-06), and the existing RoundStarted
//      broadcast routes everyone into the round exactly like any other start.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { useCallback, useEffect, useState } from 'react';
import { Navigate, Route, Routes, useLocation, useNavigate, useParams } from 'react-router-dom';
import { Box, Button, Stack, Typography } from '@mui/material';
import { keyframes } from '@mui/material/styles';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import {
  useGameHub,
  type ConnectionStatus,
  type RevealInfo,
  type StartRoundResult,
} from './signalr/useGameHub';
import { loadReconnectHandle } from './reconnect';
import { seedLibrary } from './content/seedLibrary';
import { assemble, type SubmittedWord } from './engine/assemble';
import { Home } from './pages/Home';
import { Join, type JoinProps } from './pages/Join';
import { HostSetup } from './pages/HostSetup';
import { Lobby } from './pages/Lobby';
import { Solo } from './pages/Solo';
import { Favorites } from './pages/Favorites';
import { Gallery } from './pages/Gallery';
import { Account } from './pages/Account';
import { GetMore } from './pages/GetMore';
import { Support } from './pages/Support';
import type { FavoriteEntry } from './content/favorites';
import { GroupRound } from './pages/GroupRound';
import { findGroupMode } from './pages/modeRegistry';
import { Reveal, type WordAttribution } from './pages/Reveal';
import { formatCrewByline, joinNamesReadably } from './gallery/byline';
import { buildRevealParts } from './pages/revealParts';
import { publishTale, revokeTale, slugFromTaleUrl, type PublishTalePart } from './gallery/publishTale';
import { RoundComplete, type RoundCompleteCrewMember } from './pages/RoundComplete';
import { FAMILY_SAFE_DEFAULT } from './content/familySafe';
import type { LengthPreference } from './content/length';
import { DEFAULT_VARIANT, ReactionRow } from './components';
import { toGuardianVariant, type GuardianVariant } from './components';
import type { ReactionCounts, ReactionType } from './components';
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
 * Build the Reveal's `wordAttribution.contributorFor` lookup (reveal-delight/04,
 * AC-01/AC-03/AC-06) from the SAME reveal payload `buildCrew` above already reads -
 * no extra server round-trip, no roster lookup, no second data source. Each reveal
 * word already carries its own nickname + Guardian variant, so this just indexes
 * them by nickname (which is exactly the `playerSessionId` GroupReveal's `assemble()`
 * call below uses, per the SubmittedWord mapping). An unfilled blank has an empty
 * nickname and is never indexed, so `contributorFor` naturally returns `undefined`
 * for it (AC-03) - and for a `playerSessionId` this reveal never carried a nickname
 * for (e.g. a contributor whose entry never resolved), matching this story's
 * graceful "no name" fallback rather than a crash.
 */
export function buildContributorLookup(words: RevealInfo['words']): WordAttribution {
  const byNickname = new Map<string, { nickname: string; variant: GuardianVariant }>();
  for (const w of words) {
    if (w.nickname === '') continue;
    if (!byNickname.has(w.nickname)) {
      byNickname.set(w.nickname, { nickname: w.nickname, variant: toGuardianVariant(w.variant) });
    }
  }
  return {
    contributorFor: (playerSessionId: string) => byNickname.get(playerSessionId),
  };
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
  mode,
  reactionCounts,
  onReact,
  isHost,
  goldenGuardianVotedCount,
  goldenGuardianTotalVoters,
  goldenGuardianResolved,
  goldenGuardianWinningBlankId,
  onCastGoldenGuardianVote,
  onCloseGoldenGuardianVoting,
  onPlayAgain,
  onHome,
}: {
  reveal: RevealInfo;
  mode: string;
  reactionCounts: ReactionCounts;
  onReact: (type: ReactionType) => void;
  isHost: boolean;
  goldenGuardianVotedCount: number;
  goldenGuardianTotalVoters: number;
  goldenGuardianResolved: boolean;
  goldenGuardianWinningBlankId: string | null;
  onCastGoldenGuardianVote: (blankId: string) => void;
  onCloseGoldenGuardianVoting: () => void;
  onPlayAgain: () => void;
  onHome: () => void;
}) {
  // reveal-delight/03 (AC-01): MY current vote is client-local (I know what I tapped
  // instantly). GroupReveal remounts per reveal (a new round clears `reveal` to null
  // first, unmounting this), so this state starts fresh each round - no manual reset.
  const [myVote, setMyVote] = useState<string | undefined>(undefined);

  // reactions v2 (one-per-user): MY current reaction selection is client-local for
  // the highlight (the SERVER is authoritative for the counts, via SetReaction's
  // select/move/toggle - see useGameHub's react()). Like myVote it starts fresh each
  // round because GroupReveal remounts per reveal. A tap SELECTS / MOVES / TOGGLES
  // OFF locally to mirror what the server does, then fires the hub invoke.
  const [myReaction, setMyReaction] = useState<ReactionType | null>(null);

  const template = seedLibrary.find((t) => t.id === reveal.templateId);

  if (!template) {
    // A catalog / library drift (the round used a template this device does not
    // have). Rather than crash, send the player home with a calm message.
    return (
      <Home
        onCreateGame={onHome}
        onJoinGame={onHome}
        onPlaySolo={onHome}
        onFavorites={onHome}
        onGallery={onHome}
        onAccount={onHome}
        onSupport={onHome}
        onGetMore={onHome}
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
  // reveal-delight/04 (AC-01/AC-06): derived purely from this reveal payload -
  // no new hub message, no second connection (see buildContributorLookup doc).
  const wordAttribution = buildContributorLookup(reveal.words);
  // keepsake-gallery/02 (PART C wiring): the SAME crew this reveal's Round
  // Complete recap derives (buildCrew above) - never a second data source -
  // turned into a plain-text "carved by [names]" byline for the saved/shared
  // tablet image (Reveal's saveImageByline prop, keepsake-gallery/01's
  // previously-unwired seam). Undefined for a round with no resolvable crew
  // (every blank went unfilled), so Reveal simply omits the byline (AC-02
  // "when present").
  const crewNames = buildCrew(reveal.words).crew.map((member) => member.nickname);
  const saveImageByline = formatCrewByline(crewNames);

  // keepsake-gallery/04 (AC-01/AC-03): the host-only public-link share. Built from
  // the SAME already-assembled, already-filtered data the reveal renders - the
  // interleaved parts (buildRevealParts) and the crew nicknames (buildCrew) - never
  // a second data source and never raw submissions or PII. `publish` POSTs to the
  // server (which re-vets the coral words + byline and mints the unguessable slug);
  // `revoke` deletes the tale so its link stops resolving (AC-07). Passed to Reveal
  // ONLY for the host (opt-in, never automatic, AC-03); non-hosts and solo omit it.
  const publicShare = isHost
    ? {
        publish: async (): Promise<string | null> => {
          const parts: PublishTalePart[] = buildRevealParts(template, assembled)
            .filter((part) => part.kind === 'text' || part.word !== '')
            .map((part) =>
              part.kind === 'text'
                ? { isWord: false, text: part.text }
                : { isWord: true, text: part.word },
            );
          const link = await publishTale({
            title: assembled.title,
            parts,
            bylineNames: joinNamesReadably(crewNames),
          });
          return link?.url ?? null;
        },
        revoke: async (url: string): Promise<void> => {
          await revokeTale(slugFromTaleUrl(url));
        },
      }
    : undefined;

  // group-play/05 (AC-03): resolve the round's mode to its REVEAL-time surface via
  // the shared registry, restricted to the OFFERED GROUP set (findGroupMode).
  // Progressive Reveal supplies a paced, word-by-word body (each client paces the
  // SAME already-broadcast assembled story locally - no new hub message); Classic
  // Blind / Word Bank supply none, so Reveal renders its default coral body. ANY
  // non-offered id - including a known-but-deferred "progressive-story" (AC-05) -
  // falls back to Classic Blind, so a wire drift renders the safe default, never a
  // Progressive Story reveal surface and never crashes.
  const revealSurfaces = findGroupMode(mode).revealSurfaces({ template, assembled });

  return (
    <Reveal
      assembled={assembled}
      template={template}
      onPlayAgain={onPlayAgain}
      playAgainLabel="See the round recap"
      onHome={onHome}
      exitAction={{ label: 'Back to home', onClick: onHome }}
      revealPresentation={revealSurfaces.revealPresentation}
      wordAttribution={wordAttribution}
      saveImageByline={saveImageByline}
      publicShare={publicShare}
      // reveal-delight/01 (AC-04) + reactions v2: counts are server-authoritative
      // (from the hub's ReactionCountsChanged broadcast, where the server de-dupes
      // ONE PER USER) and a tap fires the hub's React invoke, so every player sees
      // the tally update in near-real-time. `selected` is MY local pick, updated to
      // mirror the server's select/move/toggle so the row highlights the pill I hold.
      reactionRow={
        <ReactionRow
          counts={reactionCounts}
          selected={myReaction}
          onReact={(type) => {
            // Mirror the server's one-per-user rule locally for the highlight: tapping
            // the pill I already hold TOGGLES it off (null); any other tap SELECTS /
            // MOVES to it. Then fire the hub invoke, which is authoritative for counts.
            setMyReaction((current) => (current === type ? null : type));
            onReact(type);
          }}
        />
      }
      // reveal-delight/03 (AC-01/02/03): the funniest-word vote. Present ONLY in
      // group play (solo omits it entirely, AC-06). Reveal turns each coral word into
      // a tap target and paints the winner; the hub carries votes/resolution and the
      // crown (which App threads onto the NEXT round's Guardians).
      goldenGuardian={{
        phase: goldenGuardianResolved ? 'resolved' : 'voting',
        onVote: (blankId) => {
          // Optimistically mark my pick (perceived responsiveness), then fire the
          // hub invoke - the server keeps one active vote per voter and broadcasts.
          setMyVote(blankId);
          onCastGoldenGuardianVote(blankId);
        },
        myVote,
        votedCount: goldenGuardianVotedCount,
        totalVoters: goldenGuardianTotalVoters,
        winningBlankId: goldenGuardianWinningBlankId ?? undefined,
        // Host-only low-pressure "Reveal the winner" affordance (AC-03).
        onCloseVoting: isHost ? onCloseGoldenGuardianVoting : undefined,
      }}
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

/**
 * session-engine/10 (AC-01, AC-03): should a live-route guard (/lobby, /round,
 * /reveal) hold the current screen with the "reconnecting" beat instead of
 * falling through to today's redirect Home? True while a resume is genuinely
 * possible AND has not settled into a terminal failure - gated on a stored
 * reconnect handle actually existing (no handle means there is nothing to
 * resume, so redirect immediately exactly as today - AC-03's "no stored handle"
 * case). Given a handle, the ONLY case we redirect Home is a SETTLED
 * `'disconnected'` connection: SignalR's `withAutomaticReconnect()` does not
 * cover the initial `start()`, so a cold reload / relaunch with no network
 * lands on a terminal `'disconnected'` that never becomes `'connected'` on its
 * own - holding the beat there would strand the player forever, so we fall
 * through to the redirect (AC-03). We hold for every non-terminal case:
 *   - the COLD-RELOAD window (`'connecting'`): the connection has not yet
 *     reached `connected`, so story 09's mount-time Rejoin cannot fire yet.
 *   - the CONNECTED-but-resume-pending window (`'connected'`, room still null):
 *     the Rejoin is in flight or about to fire (`isRejoining` covers the former).
 * Holding through the connected-but-pending window is what stops the one-commit
 * flash to Home on a SUCCESSFUL cold-reload resume (`'connected'` lands a render
 * before `isRejoining` flips and before `room` populates - AC-01). Convergence
 * needs no extra state: a SUCCESSFUL resume populates room/round/reveal so the
 * guard's OWN state check renders the live screen before this helper is even
 * consulted (AC-02); a FAILED resume discards the handle (rejoin(), AC-04) so
 * `hasReconnectHandle` is false next render. A hung `'connecting'` that never
 * settles is not a trap either - ResumingLiveScreen carries a "Back to home"
 * escape hatch.
 *
 * Pure (no localStorage / hook access) so it is unit-testable without mocking
 * either - callers derive `hasReconnectHandle` from
 * `loadReconnectHandle() !== null` (../reconnect.ts) and pass it in.
 */
export function shouldHoldLiveRouteForResume(params: {
  status: ConnectionStatus;
  isRejoining: boolean;
  hasReconnectHandle: boolean;
}): boolean {
  if (!params.hasReconnectHandle) return false;
  // A settled 'disconnected' (e.g. a cold reload during a real network outage,
  // which never auto-retries the initial start) resolves negatively -> redirect
  // Home (AC-03). Every other state ('connecting', or 'connected' while the
  // resume is still pending) holds the calm beat. `isRejoining` is subsumed by
  // this (it only ever occurs while 'connected'), kept in the signature for
  // caller clarity.
  return params.status !== 'disconnected';
}

// A calm pulsing beat for ResumingLiveScreen's icon (AC-01, AC-06). Opacity-only
// on a single decorative icon (not a mounted/unmounted list item), the same
// posture Lobby's own "waiting..." dots already use - see Lobby.tsx's `dots`
// keyframe doc for why that is safe here (a steady looping style, not an
// entrance/exit animation that could strand something invisible).
const resumePulse = keyframes`
  0%, 100% { opacity: .4; }
  50% { opacity: 1; }
`;

/**
 * session-engine/10 (AC-01, AC-06): the brief, calm beat a live-route guard
 * shows in place of Home while `shouldHoldLiveRouteForResume` is true. Mirrors
 * GroupRound.tsx's own "Dealing your blanks..." beat's posture - deliberately
 * passive and reassuring, no spinner-of-doom, no technical jargon, no alarm
 * ("hang tight," never an error state). It carries ONE low-key escape hatch (a
 * "Back to home" text button): the beat normally resolves itself in a moment
 * (the resume lands and the routing effect takes over, or it fails and the
 * existing redirect fires), but a connection stuck at `'connecting'` on a real
 * dead zone would otherwise have no in-app exit - so an always-available way
 * out keeps the "never trapped, always a big tap target" posture (README
 * section 10). `onGoHome` clears the room + the stored handle (handleGoHome).
 */
interface ResumingLiveScreenProps {
  onGoHome: () => void;
}

function ResumingLiveScreen({ onGoHome }: ResumingLiveScreenProps) {
  return (
    <Box
      sx={{
        minHeight: '100dvh',
        maxWidth: 430,
        mx: 'auto',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        px: 5.5,
      }}
    >
      <Stack spacing={2} alignItems="center" sx={{ textAlign: 'center' }}>
        <Box
          aria-hidden
          sx={{
            color: 'primary.main',
            fontSize: 34,
            display: 'flex',
            animation: `${resumePulse} 1.6s ease-in-out infinite`,
          }}
        >
          <FontAwesomeIcon icon="plug" />
        </Box>
        <Typography sx={{ fontFamily: '"Fredoka", sans-serif', fontWeight: 600, fontSize: 20 }}>
          Reconnecting your game...
        </Typography>
        <Typography sx={{ fontSize: 15, fontWeight: 600, color: 'text.secondary' }}>
          Hang tight - picking up right where you left off.
        </Typography>
        <Button
          onClick={onGoHome}
          sx={{ mt: 1, fontWeight: 700, color: 'text.secondary', textTransform: 'none' }}
        >
          Back to home
        </Button>
      </Stack>
    </Box>
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
    createRoom,
    joinRoom,
    startRound,
    backToLobby,
    roundNotice,
    dismissRoundNotice,
    clearRoom,
    isRejoining,
  } = useGameHub();

  const navigate = useNavigate();
  const location = useLocation();

  // session-engine/10 (AC-01, AC-03): whether a resume is still pending for the
  // live-route guards below - see shouldHoldLiveRouteForResume's doc for the
  // exact combined condition. Read `loadReconnectHandle()` fresh each render
  // (a cheap, try/catch-guarded localStorage read - reconnect.ts) rather than
  // caching it in state, so a handle cleared elsewhere (a deliberate leave via
  // clearRoom, or story 09's rejoin() discarding a rejected one) is picked up
  // on the very next render with no extra effect/subscription of its own.
  const resumePending = shouldHoldLiveRouteForResume({
    status,
    isRejoining,
    hasReconnectHandle: loadReconnectHandle() !== null,
  });

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

  // group-play/05 (AC-07): the host's last chosen MODE, kept sticky the SAME way
  // as lastFamilySafe / lastLengthPref so "Play another round" reuses it rather
  // than silently resetting to Classic Blind. Defaults to 'classic-blind' so a
  // lobby that never touches the mode picker replays exactly as before this story.
  const [lastModeId, setLastModeId] = useState<string>('classic-blind');

  // story-selection/06 (AC-03): a favorite picked on the Favorites screen for
  // the SOLO replay seam - set right before navigating to '/solo', passed
  // through as Solo's `initialFavorite` prop, then cleared once Solo has
  // rendered with it (the effect below) so a LATER plain visit to '/solo'
  // never re-fires the same favorite (Solo remounts fresh per route change,
  // so its own fired-once ref alone cannot guard across remounts).
  const [pendingFavorite, setPendingFavorite] = useState<{ templateId: string } | null>(null);

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

  // story-selection/06 (AC-03/AC-04): once Solo has rendered at '/solo' with a
  // pending favorite (passed as its `initialFavorite` prop, which Solo's own
  // mount effect consumes exactly once), clear it here so navigating away and
  // back to '/solo' later (e.g. "Or play solo right now") starts a fresh,
  // ordinary solo session rather than re-replaying the same favorite.
  useEffect(() => {
    if (pendingFavorite && location.pathname === '/solo') {
      setPendingFavorite(null);
    }
  }, [pendingFavorite, location.pathname]);

  // Start a round (host) with the host's family-safe + story-length picks,
  // remembering both as sticky for the replay loop (group-play/04,
  // story-selection/02). Used by the Lobby's Start CTA.
  const handleStartRound = useCallback(
    (familySafe: boolean, lengthPref: LengthPreference, modeId: string) => {
      setLastFamilySafe(familySafe);
      setLastLengthPref(lengthPref);
      setLastModeId(modeId); // group-play/05 (AC-07): remember the mode for the replay loop.
      void startRound(familySafe, lengthPref, modeId);
    },
    [startRound],
  );

  // group-play/04: "Play another round" from the Round Complete recap (host). Reuses
  // the SAME room + players (no re-join, AC-04) and the host's sticky family-safe pick.
  // The server increments the round and broadcasts RoundStarted, which clears reveal
  // (resetting showRoundComplete) and routes EVERYONE into the new round.
  const handlePlayAnotherRound = useCallback(async () => {
    setPlayAgainError(null);
    // group-play/05 (AC-07): reuse the host's sticky mode so the replay never
    // silently resets to Classic Blind (the host can still change it next round
    // via the lobby picker after "Back to lobby").
    const result = await startRound(lastFamilySafe, lastLengthPref, lastModeId);
    // A rejected start (a carver left so the room is back to one player, or a
    // transient not-connected) resolves ok=false; surface the friendly reason on the
    // recap rather than leaving the gold CTA a silent no-op. On success the server's
    // RoundStarted broadcast clears reveal and routes everyone into the new round.
    if (!result.ok) {
      setPlayAgainError(result.error ?? 'Could not start another round - please try again.');
    }
  }, [startRound, lastFamilySafe, lastLengthPref, lastModeId]);

  // replay-remix/01 (AC-01/AC-02/AC-04): "Carve it again" from the Round Complete
  // recap (host). Same seam as handlePlayAnotherRound above, but pins the just-
  // finished template id (reveal.templateId) instead of leaving it undefined - the
  // server then plays that EXACT tale again (host-checked and family-safe-gated
  // exactly like handlePlayFavorite's explicit pick above) rather than a new
  // random/host pick. Reuses the SAME sticky family-safe + mode the host is
  // already playing with, so no new picker is shown (AC-02 out-of-scope). On
  // success the server's RoundStarted broadcast clears reveal and routes EVERYONE
  // into the new round together (AC-04) - this handler never sets round locally.
  const handleCarveItAgain = useCallback(async () => {
    if (!reveal) {
      return;
    }
    setPlayAgainError(null);
    const result = await startRound(lastFamilySafe, lastLengthPref, lastModeId, reveal.templateId);
    if (!result.ok) {
      setPlayAgainError(result.error ?? 'Could not start another round - please try again.');
    }
  }, [startRound, lastFamilySafe, lastLengthPref, lastModeId, reveal]);

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

  // "My favorites" (story-selection/06, AC-02): no hub call, no room - a route change.
  const handleOpenFavorites = useCallback(() => {
    navigate('/favorites');
  }, [navigate]);

  // "Tales we've carved" (keepsake-gallery/03, AC-01): no hub call, no room -
  // a route change, mirroring handleOpenFavorites above.
  const handleOpenGallery = useCallback(() => {
    navigate('/gallery');
  }, [navigate]);

  // "Account" (accounts-identity/03, AC-04): the purchaser-only sign-in / restore
  // surface - no hub call, no room, a plain route change like Favorites / Gallery
  // above. Reachable ONLY from Home; never from a child's play flow (AC-04). Free
  // play never depends on it (AC-03).
  const handleOpenAccount = useCallback(() => {
    navigate('/account');
  }, [navigate]);

  // "Get more" (billing-entitlements/04, AC-05) and "Support us" (billing-
  // entitlements/02, AC-01): purchaser-facing surfaces reached ONLY from Home, a
  // plain route change like Account / Gallery above - never a child's play flow, no
  // hub call, and free play never depends on either.
  const handleOpenGetMore = useCallback(() => {
    navigate('/get-more');
  }, [navigate]);

  const handleOpenSupport = useCallback(() => {
    navigate('/support');
  }, [navigate]);

  // Favorites screen's onPick (SOLO replay, AC-03/AC-04): remember the picked
  // template and route to '/solo', where Solo's own mount effect resolves it,
  // gates it through family-safe, and starts it with the freshness bypass.
  const handlePickFavorite = useCallback(
    (entry: FavoriteEntry) => {
      setPendingFavorite({ templateId: entry.templateId });
      navigate('/solo');
    },
    [navigate],
  );

  // Lobby's host-only inline favorites picker (GROUP replay, AC-03/AC-04/AC-06):
  // invokes the SAME startRound seam with an explicit templateId - the server
  // plays that exact template (family-safe-gated first, freshness bypassed,
  // AC-04) instead of a random pick. Reuses the host's sticky family-safe /
  // length picks (the length preference is moot once a templateId is supplied,
  // but travels along on the same call for wire-contract consistency). No new
  // hub method; the existing RoundStarted broadcast routes everyone in on success.
  const handlePlayFavorite = useCallback(
    // The Lobby passes its CURRENT family-safe toggle + selected mode (not the
    // sticky values) so a favorite is gated on exactly what the host sees on that
    // screen (AC-06). A favorite plays in the host's CHOSEN mode (group-play/05):
    // the server enforces per-mode eligibility for explicit picks too, so a favorite
    // eligible for the mode (e.g. a word-bank tale under Word Bank, any tale under
    // Progressive Reveal) plays in it, and one that is not (e.g. a bank-less tale
    // under Word Bank) is rejected with the friendly inline error the Lobby already
    // shows - rather than silently downgrading to Classic Blind. The lengthPref is
    // moot with an explicit templateId but travels along for wire-contract consistency.
    (templateId: string, familySafe: boolean, modeId: string): Promise<StartRoundResult> => {
      // Restamp the sticky family-safe + mode so a later "Play another round" reuses
      // what the host JUST played, not a stale earlier pick (AC-07). This matters now
      // that a favorite carries the host's real chosen mode (before, favorites were
      // always Classic Blind, so there was nothing mode-wise to remember).
      setLastFamilySafe(familySafe);
      setLastModeId(modeId);
      return startRound(familySafe, lastLengthPref, modeId, templateId);
    },
    [startRound, lastLengthPref],
  );

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
            crownedNickname={crownedNickname}
            isHost={isHost}
            canPlayAgain={room.players.length >= 2}
            playAgainError={playAgainError}
            onPlayAgain={() => void handlePlayAnotherRound()}
            onCarveItAgain={() => void handleCarveItAgain()}
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
            onFavorites={handleOpenFavorites}
            onGallery={handleOpenGallery}
            onAccount={handleOpenAccount}
            onSupport={handleOpenSupport}
            onGetMore={handleOpenGetMore}
            disabled={status !== 'connected'}
          />
        }
      />
      <Route path="/account" element={<Account onBack={handleGoHome} />} />
      <Route path="/get-more" element={<GetMore onBack={handleGoHome} />} />
      <Route path="/support" element={<Support onBack={handleGoHome} />} />
      <Route
        path="/favorites"
        element={<Favorites onBack={handleGoHome} onPick={handlePickFavorite} />}
      />
      <Route path="/gallery" element={<Gallery onBack={handleGoHome} />} />
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
      <Route
        path="/solo"
        element={<Solo onExit={handleGoHome} initialFavorite={pendingFavorite ?? undefined} />}
      />
      {/* Live game screens: guarded so a refresh with no live room redirects home
          (AC-05). The real-time effect above keeps the URL in sync while playing. */}
      <Route
        path="/lobby"
        element={
          room ? (
            <Lobby
              room={room}
              isHost={isHost}
              crownedNickname={crownedNickname}
              onLeave={handleGoHome}
              onStart={handleStartRound}
              onPlayFavorite={handlePlayFavorite}
              notice={roundNotice}
              onDismissNotice={dismissRoundNotice}
            />
          ) : resumePending ? (
            <ResumingLiveScreen onGoHome={handleGoHome} />
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
              mode={round.mode}
              assignedBlankIndices={assignedBlankIndices}
              collectProgress={collectProgress}
              submitWord={submitWord}
              crownedNickname={crownedNickname}
              // game-modes/07 AC-03: the AI "Fresh runes" jumble needs the join
              // code (the server resolves it to the room's anonymous InstanceId)
              // and the round's family-safe toggle (sticky in App).
              roomCode={room.code}
              familySafe={lastFamilySafe}
              onLeave={handleGoHome}
            />
          ) : resumePending ? (
            <ResumingLiveScreen onGoHome={handleGoHome} />
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
              // group-play/05: the mode is carried on `round`, which the hook keeps
              // set THROUGH the reveal (RevealReady does not clear it), so Progressive
              // Reveal can pace its body here. Fall back to Classic Blind if a race
              // ever leaves round momentarily null.
              mode={round?.mode ?? 'classic-blind'}
              reactionCounts={reactionCounts}
              onReact={react}
              isHost={isHost}
              goldenGuardianVotedCount={goldenGuardianVotedCount}
              goldenGuardianTotalVoters={goldenGuardianTotalVoters}
              goldenGuardianResolved={goldenGuardianResolved}
              goldenGuardianWinningBlankId={goldenGuardianWinningBlankId}
              onCastGoldenGuardianVote={castGoldenGuardianVote}
              onCloseGoldenGuardianVoting={closeGoldenGuardianVoting}
              onPlayAgain={() => setShowRoundComplete(true)}
              onHome={handleGoHome}
            />
          ) : resumePending ? (
            <ResumingLiveScreen onGoHome={handleGoHome} />
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
