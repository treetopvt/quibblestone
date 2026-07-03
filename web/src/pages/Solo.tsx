// ----------------------------------------------------------------------------
//  Solo - the local, single-client solo round (single-player/01 + /02, the
//  "my family is laughing in the car" thin slice).
//
//  This screen is almost entirely COMPOSITION, not new mechanics: it wires
//  together pieces that already exist -
//    - the engine (createCollection / collectWord / isCollectionComplete /
//      assembleStory, ../engine/engine.ts)
//    - the solo mode registry (./soloModes.ts): the player picks one of the
//      four modes (Classic blind, Word Bank, Progressive Story, Progressive
//      Reveal) at setup and the round plays it, resolving that mode's config
//      (passed to collectWord) and its ModeSurfaces (into FillBlank / Reveal's
//      game-modes/03 optional slots) - this file never edits those two screens
//    - the family-safe content gate (via each mode's eligibleTemplates, which
//      applies ../content/familySafe.ts's selectTemplates rule)
//    - the length + freshness selection stages (story-selection/01-03)
//    - the real safety filter client (checkWord, ../safety/checkWord.ts)
//    - the shared FillBlank filler screen and Reveal payoff screen (both
//      REUSED as-is - this file never edits them, per the reuse contract
//      documented in their own headers)
//  into a small local state machine: 'setup' -> 'fill' -> 'reveal'. There is
//  NO room, NO join code, NO SignalR connection, NO account, and NO PII
//  anywhere in this flow - it is a single browser tab playing itself, the
//  "bored in the back seat, right now" loop the thin vertical slice exists
//  to prove out (README section 7 / 8).
//
//  SOLO_PLAYER_ID below is a local, hardcoded, non-identifying constant used
//  only to satisfy assemble()'s SubmittedWord.playerSessionId shape (it is
//  never sent anywhere, never persisted, and never shown to the player) - it
//  is NOT an account and carries no personal information.
//
//  Why skip records an EMPTY placeholder word (not nothing): the engine's
//  toOrderedWords() walks the template's blanks in body order and OMITS any
//  blank with no entry in the collection at all - it does not "pad" gaps.
//  assemble() then matches the remaining words to blanks purely by their
//  POSITION in that ordered list. If a skipped blank in the MIDDLE of a
//  template were left absent from the collection instead of recorded as an
//  empty word, every blank AFTER it would silently shift left and fill with
//  the wrong word. Recording `{ playerSessionId: SOLO_PLAYER_ID, word: '' }`
//  keeps the collection's size equal to the blank count, which keeps every
//  later word aligned to its correct blank - and assemble() already renders
//  an empty-string word as literally nothing, so the blank reads as left
//  blank in the final story (AC-06). A skip never calls checkWord: an empty
//  string is not free text a player wrote, so there is nothing to filter.
//
//  The replay loop (AC-06, "one tap, no bounce back to setup"): Reveal's
//  "Play another round" button calls handlePlayAgain directly, which picks a
//  fresh random template from the SAME selection pipeline, resets the
//  collection and blank index, and goes straight back to 'fill' - it never
//  revisits 'setup'. The family-safe toggle, length choice, and mode are
//  therefore sticky for the whole solo session until the player exits to Home.
//
//  Child safety: every FREE-TEXT submission is routed through collectWord's
//  injected SafetyCheck hook (checkWord, the real server-backed filter) BEFORE
//  it is ever recorded (AC-04) - this file never reimplements or bypasses that
//  check, and passes the SELECTED mode's config to collectWord so the seam is
//  identical across modes. Word Bank is the one mode that legitimately skips the
//  free-text filter (its words are curated, pre-vetted content, gated by
//  family-safe at OFFERING time via each mode's eligibleTemplates, not per tap) -
//  collectWord already branches on mode.answer === 'word-bank' for exactly this.
//  The family-safe toggle only narrows which curated templates each mode offers;
//  it never touches the profanity filter.
//
//  Selection pipeline (story-selection/02 + /03): the SOLO length choice
//  ("Quick tale" / "Full tale", defaulting to 'full', AC-01/AC-06) sits beside
//  the family-safe toggle and mode picker. The pick composes ALL content-
//  selection stages in FIXED order, safety FIRST, freshness LAST (AC-05): the
//  selected mode's eligibleTemplates (already family-safe-gated + mode-eligible)
//  -> selectByLengthOrFallback (story-selection/01's length stage, degrading to
//  the mode pool if the length preference would leave nothing, AC-06) ->
//  selectFreshOrRecycle (../content/fresh.ts, excluding templates this DEVICE has
//  already played per ../content/playedHistory.ts's localStorage history, and
//  recycling once the pool runs dry rather than erroring, AC-03) ->
//  pickRandomTemplate. Both handleStart and handlePlayAgain are fresh RANDOM
//  picks, so both route through pickTemplate and both APPEND the chosen id to
//  device history in beginRound, AFTER the pick (AC-01). The history stores
//  template IDS ONLY - no words, no PII (AC-06) - and SURVIVES a page refresh
//  because it lives in localStorage, not component state.
//
//  AC-04 bypass seam (story-selection/06, "play a favorite"): beginRound now
//  takes an explicit `{ record: boolean }` option. `record: true` (the RANDOM
//  pick path - handleStart / handlePlayAgain, unchanged behavior) fires
//  recordSoloServe AND appendPlayedId, exactly as before this story. `record:
//  false` is the EXPLICIT-REPLAY path (a favorite picked from the Favorites
//  screen, wired via the `initialFavorite` prop's mount effect below): it runs
//  NEITHER side effect, because playing a favorite must not re-stamp freshness
//  history (a favorite would otherwise make the random pick "forget" other
//  unplayed stories, story-selection/03 AC-04) and is not a serve/thumbs
//  telemetry signal (story-selection/06's Out of Scope - a star is a private
//  device-local shortcut, never routed into the serve log).
//
//  story-selection/04 fire-and-forgets an anonymous "template served" event on
//  each RANDOM round start (beginRound with record:true) - anonymous, never
//  gates the flow (see recordSoloServe). The solo Reveal deliberately does NOT
//  pass Reveal's `taleFeedback` slot (story-selection/05's "Did you like this
//  story?" thumbs): it read as a redundant second sentiment control beside the
//  Love/Wow/Didn't-like reaction row and stole story real estate, so it was
//  dropped from solo (the UX de-clutter). Group play keeps that vote on its
//  Round Complete recap, not the transient reveal, so nothing there changes.
//
//  platform-devops/05 (anonymous product-usage) adds two more fire-and-forget,
//  no-PII beacons on the SAME never-gate posture: a "RoundStarted" (the mode
//  played) and a "RoundCompleted" with the session DURATION when the round reaches
//  the reveal (see recordSoloRoundStarted / recordSoloRoundCompleted). Unlike the
//  serve log + freshness history above (content curation, RANDOM-pick only),
//  product usage counts EVERY round - a favorite replay (record:false) included -
//  so these fire on BOTH paths, before the record gate. They ride story 04's App
//  Insights pipeline (a DIFFERENT surface from the serve log's Table Storage):
//  which modes get played + how long a session lasts + approximate anonymous
//  device reach, never a person.
// ----------------------------------------------------------------------------

import { useEffect, useRef, useState } from 'react';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { alpha, useTheme } from '@mui/material/styles';
import { Box, Button, Stack, Typography } from '@mui/material';
import {
  AppBar,
  BottomActionBar,
  BottomActionBarSpacer,
  FamilySafeToggle,
  GameSettingsSheet,
  ReactionRow,
  StoryLengthChoice,
} from '../components';
import type { ReactionCounts, ReactionType } from '../components';
import { FAMILY_SAFE_DEFAULT, selectTemplates } from '../content/familySafe';
import { selectFreshOrRecycle } from '../content/fresh';
import { selectByLengthOrFallback, type LengthPreference } from '../content/length';
import { appendPlayedId, loadPlayedIds } from '../content/playedHistory';
import { seedLibrary } from '../content/seedLibrary';
import {
  assembleStory,
  collectWord,
  createCollection,
  isCollectionComplete,
  skipBlank,
  type CollectedWords,
} from '../engine/engine';
import { getBlanks, type Template } from '../engine/template';
import { checkWord } from '../safety/checkWord';
import { createAiJumbleRequester } from '../ai/jumbleClient';
import { getOrCreateSessionId, recordSoloServe } from '../telemetry/serveLog';
import { recordSoloRoundCompleted, recordSoloRoundStarted } from '../telemetry/usageBeacon';
import { FillBlank } from './FillBlank';
import { Reveal } from './Reveal';
import { ModePicker } from './ModePicker';
import { DEFAULT_SOLO_MODE, SOLO_MODES, type SoloMode } from './soloModes';

export interface SoloProps {
  /** Return to Home. Solo never set a room, so this is purely a view change for the caller. */
  onExit: () => void;
  /**
   * Optional favorite to replay (story-selection/06, AC-03/AC-04): when
   * supplied, Solo skips setup entirely and starts THIS exact template on
   * mount, with the freshness bypass + no history re-stamp (AC-04) - see the
   * effect below. App sets this from the Favorites screen's `onPick` and
   * clears it once consumed, so navigating to '/solo' again (e.g. via "Or
   * play solo right now") never re-fires the same favorite.
   */
  initialFavorite?: { templateId: string };
}

/**
 * A local, non-identifying attribution tag for assemble()'s SubmittedWord
 * shape. Not an account, not PII, never transmitted anywhere - solo is a
 * single browser tab with no server-side notion of "this player."
 */
const SOLO_PLAYER_ID = 'solo-player';

/** A fresh all-zero reaction tally for a new solo reveal (reveal-delight/01, AC-05). */
const ZERO_REACTIONS: ReactionCounts = { love: 0, wow: 0, nope: 0 };

/** The three phases of the local solo state machine. */
type SoloPhase = 'setup' | 'fill' | 'reveal';

/**
 * Picks one template at random from a non-empty list (AC-02: "pick or be
 * given a template" - given-at-random is fine for the thin slice). Returns
 * `undefined` for an empty list so callers can guard the "no templates"
 * case explicitly instead of reaching for a non-null assertion.
 */
export function pickRandomTemplate(templates: readonly Template[]): Template | undefined {
  if (templates.length === 0) return undefined;
  const index = Math.floor(Math.random() * templates.length);
  return templates[index];
}

/**
 * Counts collected entries with a non-empty word - the personal summary's
 * "You filled N words" figure (skips recorded as empty placeholders do not
 * count, per the file header).
 */
function countFilledWords(collected: CollectedWords): number {
  let count = 0;
  for (const entry of collected.values()) {
    // Treat whitespace-only as unfilled to stay consistent with FillBlank,
    // which trims and rejects empty submissions - so the only ''/blank entries
    // here are the placeholders a skip records (which must not count).
    if (entry.word.trim().length > 0) count += 1;
  }
  return count;
}

/**
 * The solo personal summary (Reveal's `attribution` slot): a single, quiet
 * "You filled N words" line. It deliberately does NOT echo the tale title -
 * that already appears in the story card just below, so repeating it as an
 * uppercase kicker was redundant clutter (the UX de-clutter). Solo also does
 * NOT show the group Round Complete crew recap (AC-07) - just this one
 * theme-driven, PII-free line about the single player.
 */
function PersonalSummary({ filledCount }: { filledCount: number }) {
  return (
    <Typography sx={{ fontFamily: '"Nunito", sans-serif', fontSize: 14, fontWeight: 800, color: 'teal.dark' }}>
      You filled {filledCount} {filledCount === 1 ? 'word' : 'words'}
    </Typography>
  );
}

/**
 * The solo setup screen. Mirrors the group-play (Lobby) settings experience
 * exactly (fit-to-viewport redesign, 2026-07): instead of stacking the
 * family-safe toggle, story-length choice, and mode picker inline (which
 * pushed the Start CTA past the fold), it shows a single collapsed "Game
 * settings" row that opens the SHARED <GameSettingsSheet> slide-up bottom
 * sheet (../components/GameSettingsSheet.tsx) holding those same three
 * controls. The main surface is a fixed-height, non-scrolling flex column:
 * intro line, the settings row, and the pinned gold "Start" CTA.
 *
 * The sheet-open flag is pure local UI state owned here (it only decides
 * WHEN the controls are visible); the parent Solo still owns every actual
 * setting value (familySafe / lengthPref / mode) via the passed props, so
 * this component reuses the sheet as chrome without duplicating any state.
 */
function SoloSetup({
  familySafe,
  onFamilySafeChange,
  lengthPref,
  onLengthPrefChange,
  mode,
  onModeChange,
  onStart,
  onExit,
}: {
  familySafe: boolean;
  onFamilySafeChange: (checked: boolean) => void;
  lengthPref: LengthPreference;
  onLengthPrefChange: (value: LengthPreference) => void;
  mode: SoloMode;
  onModeChange: (mode: SoloMode) => void;
  onStart: () => void;
  onExit: () => void;
}) {
  const theme = useTheme();
  // Pure local UI state: whether the settings bottom sheet is open. It never
  // affects what a round starts with - only whether the controls are visible.
  const [settingsOpen, setSettingsOpen] = useState(false);

  // The collapsed row's summary subtitle, reflecting the CURRENT values so the
  // row is informative even closed (e.g. "Full tale - Classic - Family-safe
  // on"). Identical shape to the group Waiting room's summary.
  const lengthLabel = lengthPref === 'quick' ? 'Quick tale' : 'Full tale';
  const settingsSummary = `${lengthLabel} - ${mode.config.label} - Family-safe ${familySafe ? 'on' : 'off'}`;

  return (
    <Box
      sx={{
        position: 'relative',
        height: '100dvh',
        display: 'flex',
        flexDirection: 'column',
        overflow: 'hidden',
        maxWidth: 430,
        mx: 'auto',
      }}
    >
      <AppBar
        title="Play solo"
        leftAction={{ icon: 'arrow-left', label: 'Back to home', onClick: onExit }}
      />

      <Stack spacing={4} sx={{ flex: 1, minHeight: 0, overflowY: 'auto', px: 5.5, pt: 3 }}>
        <Typography sx={{ fontSize: 16, fontWeight: 600, color: 'text.secondary', textAlign: 'center' }}>
          A quick one-player round, right now - no room, no code, just you and a silly tale.
        </Typography>

        {/* Collapsed "Game settings" row: tapping it opens <GameSettingsSheet>
            below, which holds the SAME controls the group Waiting room puts in
            its sheet. Identical chrome to Lobby's row (sliders chip, title +
            live summary, chevron). */}
        <Box
          component="button"
          type="button"
          onClick={() => setSettingsOpen(true)}
          aria-haspopup="dialog"
          sx={{
            display: 'flex',
            alignItems: 'center',
            gap: 2,
            width: '100%',
            textAlign: 'left',
            cursor: 'pointer',
            px: 3,
            py: 2.5,
            bgcolor: 'card.main',
            borderRadius: '20px',
            border: `1.5px solid ${alpha(theme.palette.stoneEdge.main, 0.22)}`,
            fontFamily: 'inherit',
            '&:focus-visible': { outline: `2px solid ${theme.palette.primary.main}`, outlineOffset: 2 },
          }}
        >
          <Box
            sx={{
              flexShrink: 0,
              width: 44,
              height: 44,
              borderRadius: '14px',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              bgcolor: alpha(theme.palette.primary.main, 0.14),
              color: theme.palette.primary.main,
            }}
          >
            <FontAwesomeIcon icon="sliders" style={{ width: 19, height: 19 }} />
          </Box>
          <Stack spacing={0.25} sx={{ flexGrow: 1, minWidth: 0 }}>
            <Typography
              sx={{ fontFamily: '"Fredoka", sans-serif', fontWeight: 600, fontSize: 16.5, color: 'text.primary' }}
            >
              Game settings
            </Typography>
            <Typography
              noWrap
              sx={{ fontFamily: '"Nunito", sans-serif', fontWeight: 700, fontSize: 13, color: 'text.secondary' }}
            >
              {settingsSummary}
            </Typography>
          </Stack>
          <Box sx={{ flexShrink: 0, color: 'text.secondary', display: 'flex' }}>
            <FontAwesomeIcon icon="chevron-right" style={{ width: 16, height: 16 }} />
          </Box>
        </Box>

        <BottomActionBarSpacer />
      </Stack>

      <BottomActionBar>
        <Button variant="contained" fullWidth onClick={onStart}>
          Start
        </Button>
      </BottomActionBar>

      {/* The SHARED settings bottom sheet - the SAME component the group
          Waiting room uses. Pure slide-up chrome over a dim scrim; the
          controls inside stay controlled by the parent Solo's state. Solo has
          no "Play a favorite" panel here (that is a host-only group affordance),
          so the sheet holds just the three round-setup controls. */}
      <GameSettingsSheet open={settingsOpen} onClose={() => setSettingsOpen(false)}>
        <FamilySafeToggle checked={familySafe} onChange={onFamilySafeChange} />
        <StoryLengthChoice value={lengthPref} onChange={onLengthPrefChange} />
        <ModePicker
          modes={SOLO_MODES}
          selectedId={mode.config.id}
          onSelect={onModeChange}
          familySafe={familySafe}
          label="Choose a mode"
        />
      </GameSettingsSheet>
    </Box>
  );
}

export function Solo({ onExit, initialFavorite }: SoloProps) {
  const [phase, setPhase] = useState<SoloPhase>('setup');
  const [familySafe, setFamilySafe] = useState(FAMILY_SAFE_DEFAULT);
  // story-selection/02, AC-01/AC-06: defaults to 'full' so a session that never
  // touches the length choice behaves exactly like before this story existed.
  const [lengthPref, setLengthPref] = useState<LengthPreference>('full');
  // The mode the player picks at setup (default Classic blind, so the existing
  // zero-choice flow still works with one tap on Start - single-player/02
  // AC-01/AC-06). Its config drives collectWord and its surfaces plug into
  // FillBlank / Reveal.
  const [mode, setMode] = useState<SoloMode>(DEFAULT_SOLO_MODE);
  const [template, setTemplate] = useState<Template | undefined>(undefined);
  const [blankIndex, setBlankIndex] = useState(0);
  // reveal-delight/01 (AC-05) + reactions v2: solo reactions are purely LOCAL - a
  // single tab reacting to its own tale, no room and no hub. Solo implements the
  // ONE-REACTION-PER-USER rule itself: `reactionCounts` is the tally and
  // `myReaction` is the single reaction this player currently holds (null when
  // none). Both start empty and reset each new round in beginRound so they are
  // ephemeral per reveal (Out of Scope: no persistence across a replay).
  // The tally AND this player's single held reaction live in ONE state atom so a
  // tap updates both ATOMICALLY from the same prior value. (They were two separate
  // useState before; the count updater closed over the outer `myReaction`, which
  // could be stale when React batches rapid taps and decrement the wrong prior
  // reaction - Copilot review on #156.)
  const [reactions, setReactions] = useState<{ counts: ReactionCounts; mine: ReactionType | null }>({
    counts: ZERO_REACTIONS,
    mine: null,
  });

  // A tap SELECTS (none held -> +1), MOVES (a different one held -> -old +new), or
  // TOGGLES OFF (the held one -> -1, clear) this player's single reaction. Both the
  // counts and the selection are derived from `prev` in a SINGLE functional update,
  // so they can never desync. Mirrors the server's SetReaction move/toggle for group
  // play, so solo and group behave identically.
  const handleReact = (type: ReactionType) => {
    setReactions((prev) => {
      const counts = { ...prev.counts };
      if (prev.mine === type) {
        counts[type] = Math.max(0, counts[type] - 1); // toggle off
        return { counts, mine: null };
      }
      if (prev.mine !== null) counts[prev.mine] = Math.max(0, counts[prev.mine] - 1); // move: drop the old
      counts[type] += 1; // select / move: take the new
      return { counts, mine: type };
    });
  };

  // The collection lives in a ref (not state): collectWord mutates it in
  // place and FillBlank re-renders are driven by `blankIndex`/`phase`
  // instead, matching engine.ts's "mutates and returns `collected`" contract.
  const collectionRef = useRef<CollectedWords>(createCollection());

  // platform-devops/05 (AC-02): when the current round OPENED (Date.now() ms), so
  // the anonymous "RoundCompleted" usage beacon can measure this solo session's
  // DURATION (reveal time minus this). A ref, not state - it never drives a render,
  // and it must survive the fill -> reveal transition without re-triggering one.
  const roundStartRef = useRef<number | null>(null);

  const handleFamilySafeChange = (checked: boolean) => {
    setFamilySafe(checked);
    // If the current mode has no eligible template at the new family-safe
    // position, fall back to Classic blind (always eligible) so the picker
    // never sits on an unstartable mode (AC-04).
    if (mode.eligibleTemplates(seedLibrary, checked).length === 0) {
      setMode(DEFAULT_SOLO_MODE);
    }
  };

  /**
   * Begin a round on `chosen` (story-selection/06, AC-04). `record` controls
   * whether the RANDOM-pick side effects fire:
   *  - `record: true` (handleStart / handlePlayAgain, the fresh-random path,
   *    UNCHANGED behavior): fires recordSoloServe (story-selection/04's
   *    anonymous serve telemetry) AND appendPlayedId (story-selection/03's
   *    freshness history), exactly as before this story.
   *  - `record: false` (the explicit favorite-replay path, see the
   *    initialFavorite effect below): fires NEITHER - a favorite is an
   *    EXPLICIT replay, so it must not re-stamp freshness history (which
   *    would make the random pick "forget" other unplayed stories,
   *    story-selection/03 AC-04) and is not a serve/thumbs telemetry signal
   *    (story-selection/06's Out of Scope).
   */
  const beginRound = (chosen: Template, { record }: { record: boolean }) => {
    collectionRef.current = createCollection();
    setTemplate(chosen);
    setBlankIndex(0);
    // reveal-delight/01 (AC-05) + reactions v2: reactions are per-reveal ephemeral,
    // so a fresh round starts every count back at zero AND clears this player's held
    // reaction (they re-pick each reveal).
    setReactions({ counts: ZERO_REACTIONS, mine: null });
    setPhase('fill');
    // platform-devops/05 (anonymous product-usage, AC-01/AC-02): mark the round
    // start time and fire-and-forget one anonymous "RoundStarted" usage event with
    // the selected MODE (solo context) so "which modes get played" is answerable.
    // This runs on EVERY round - BEFORE the record gate below - because product
    // usage counts a favorite replay (record:false) too, unlike the serve log +
    // freshness history (content curation, random-pick only). It rides story 04's
    // App Insights pipeline via /api/usage (a DIFFERENT surface from the serve log,
    // Table Storage) - never awaits, never retries, never blocks the transition to
    // 'fill' (AC-08). Carries no PII - just the stable mode id + an anonymous
    // device id (AC-04).
    roundStartRef.current = Date.now();
    recordSoloRoundStarted(mode.config.id);
    if (!record) return;
    // story-selection/04 (anonymous serve log, AC-02): fire-and-forget one
    // anonymous "template served" event. It never awaits, never retries, and
    // never blocks the transition to 'fill' (AC-03), and carries no PII - just
    // the template + the current family-safe toggle (AC-04).
    recordSoloServe({ template: chosen, familySafe });
    // story-selection/03 (freshness rotation, AC-01): record this template as
    // played on THIS device, AFTER the pick, so the NEXT pickTemplate() call
    // excludes it until the eligible pool runs dry.
    appendPlayedId(chosen.id);
  };

  // story-selection/02 + /03: compose the content-selection stages in FIXED
  // order, safety FIRST, freshness LAST (AC-05) - the selected mode's
  // eligibleTemplates (already family-safe-gated + mode-eligible) -> length
  // stage (degrades to the mode pool if the length preference would leave
  // nothing, AC-06) -> freshness stage (excludes templates already played on
  // this device; recycles the whole pool once it runs dry rather than erroring,
  // AC-03) -> pickRandomTemplate. Length + freshness only ever NARROW within the
  // already safety+mode-filtered pool - they never widen it.
  const pickTemplate = () =>
    pickRandomTemplate(
      selectFreshOrRecycle(
        selectByLengthOrFallback(mode.eligibleTemplates(seedLibrary, familySafe), lengthPref),
        loadPlayedIds(),
      ),
    );

  const handleStart = () => {
    // Draw from the selected mode's eligible pool, narrowed by length + freshness
    // (see pickTemplate). Guard the "no templates" case rather than asserting
    // non-null; a disabled mode can never be selected, so this is defensive.
    const chosen = pickTemplate();
    if (!chosen) return;
    beginRound(chosen, { record: true });
  };

  const handlePlayAgain = () => {
    const chosen = pickTemplate();
    if (!chosen) return;
    beginRound(chosen, { record: true });
  };

  // story-selection/06 (AC-03/AC-04/AC-06): while a favorite is still being
  // resolved, render NOTHING rather than flashing the setup picker (AC-03 -
  // "no template picker" for a favorite replay). Seeded true only when
  // `initialFavorite` was supplied at mount; flipped false by the effect
  // below once it has either started the round or given up (a missing /
  // ineligible favorite), so a failure still falls through to the normal
  // setup screen instead of a permanently blank one.
  const [resolvingFavorite, setResolvingFavorite] = useState(() => Boolean(initialFavorite));

  // Consume `initialFavorite` ONCE on mount, guarded by a fired-once ref so a
  // re-render (or React StrictMode's dev double-invoke) can never start the
  // same favorite twice. Resolves the template from the seed library, runs it
  // through the FAMILY-SAFE GATE FIRST (selectTemplates, AC-06 - a favorite
  // that is not family-safe under the current toggle position is never
  // started), and on eligibility starts it via beginRound with `record: false`
  // (the explicit-replay bypass, AC-04 - no freshness re-stamp, no
  // serve/thumbs telemetry). A missing or ineligible favorite (a library
  // drift, or the family-safe toggle excluding it) falls back to the normal
  // setup screen gracefully rather than erroring. Depends only on
  // `initialFavorite` deliberately - `familySafe` reads its mount-time default
  // (FAMILY_SAFE_DEFAULT) here, matching the setup screen's own initial value.
  const consumedInitialFavorite = useRef(false);
  useEffect(() => {
    if (!initialFavorite || consumedInitialFavorite.current) return;
    consumedInitialFavorite.current = true;
    const found = seedLibrary.find((t) => t.id === initialFavorite.templateId);
    const eligible = found ? selectTemplates([found], familySafe) : [];
    if (found && eligible.length > 0) {
      beginRound(found, { record: false });
    }
    setResolvingFavorite(false);
  }, [initialFavorite]);

  if (resolvingFavorite) {
    return null;
  }

  if (phase === 'setup' || !template) {
    return (
      <SoloSetup
        familySafe={familySafe}
        onFamilySafeChange={handleFamilySafeChange}
        lengthPref={lengthPref}
        onLengthPrefChange={setLengthPref}
        mode={mode}
        onModeChange={setMode}
        onStart={handleStart}
        onExit={onExit}
      />
    );
  }

  const blanks = getBlanks(template);
  const currentBlank = blanks[blankIndex];

  const advance = () => {
    if (isCollectionComplete(template, collectionRef.current)) {
      // platform-devops/05 (AC-02): the round just completed (moving to the
      // reveal), so fire-and-forget one anonymous "RoundCompleted" usage event
      // with the MODE + the measured session DURATION (now minus the round start).
      // ONE-SHOT LATCH: setPhase('reveal') is async, so a double-tap of the final
      // blank could re-enter here before the fill UI unmounts; guarding on (and
      // then clearing) roundStartRef makes the completion event fire EXACTLY ONCE
      // per round (beginRound re-stamps it next round). It never blocks the
      // transition and carries no PII - a duration + a stable mode id + an
      // anonymous device id (AC-04/AC-08).
      const startedAt = roundStartRef.current;
      if (startedAt !== null) {
        roundStartRef.current = null;
        recordSoloRoundCompleted(mode.config.id, Date.now() - startedAt);
      }
      setPhase('reveal');
      return;
    }
    setBlankIndex((index) => index + 1);
  };

  const handleSubmitWord = async (word: string) => {
    // Unreachable given the `phase === 'fill' && currentBlank` render guard
    // below, but return a FAILURE (not a silent success) so an unexpected state
    // surfaces to the player + logs instead of clearing the input as if the
    // word were accepted (PR review hardening).
    if (!currentBlank) {
      return { accepted: false, message: 'Something went off - please try again.' } as const;
    }
    const result = await collectWord(
      collectionRef.current,
      template,
      mode.config,
      currentBlank.id,
      { playerSessionId: SOLO_PLAYER_ID, word },
      checkWord,
    );
    if (result.accepted) {
      advance();
    }
    return result;
  };

  const handleSkip = () => {
    if (!currentBlank) return;
    // Record an empty placeholder (see file header) to preserve positional
    // alignment - never leave a skipped blank absent from the collection. The
    // "skip = empty placeholder" rule now lives in the engine (skipBlank) so
    // solo and group-play share it and cannot drift.
    skipBlank(collectionRef.current, template, currentBlank.id, SOLO_PLAYER_ID);
    advance();
  };

  if (phase === 'fill' && currentBlank) {
    // Resolve the active mode's fill-time surfaces for THIS blank. Classic
    // blind returns {} (no surfaces), so FillBlank renders its free-text
    // default unchanged (AC-06); Word Bank supplies answerSurface, Progressive
    // Story supplies seeContext built from the collection so far.
    const fillSurfaces = mode.fillSurfaces({
      template,
      collectedSoFar: collectionRef.current,
      currentBlank,
      onSubmit: handleSubmitWord,
      // AI "Fresh runes" for Word Bank (game-modes/07 AC-03): solo has no room,
      // so the gate meters on the anonymous device-local telemetry session id.
      // The button falls back to the free reshuffle whenever the gate does.
      requestAiJumble: createAiJumbleRequester({
        familySafe,
        themes: template.tags.themes,
        sessionId: getOrCreateSessionId(),
      }),
    });
    return (
      <FillBlank
        // Key by blank id so each blank gets a structurally fresh FillBlank
        // (input/error/submitting state reset per blank, not just imperatively).
        key={currentBlank.id}
        subject={template.title}
        blank={currentBlank}
        wordNumber={blankIndex + 1}
        totalWords={blanks.length}
        onSubmitWord={handleSubmitWord}
        onSkip={handleSkip}
        onExit={onExit}
        exitLabel="Back to home"
        seeContext={fillSurfaces.seeContext}
        answerSurface={fillSurfaces.answerSurface}
      />
    );
  }

  // 'reveal' (or a fully-collected round that fell through the guard above).
  const assembled = assembleStory(template, collectionRef.current);
  const filledCount = countFilledWords(collectionRef.current);
  // Classic blind / Word Bank / Progressive Story return {} here (no override),
  // so Reveal renders its default at-end coral body; Progressive Reveal
  // supplies revealPresentation to pace the finished story one word at a time.
  const revealSurfaces = mode.revealSurfaces({ template, assembled });

  // keepsake-gallery/02 (PART C wiring): `saveImageByline` is deliberately
  // OMITTED here, not passed as an invented placeholder. Solo has no room, no
  // join flow, and collects no nickname at all - SOLO_PLAYER_ID (see file
  // header) is an internal, non-identifying constant that is never shown to
  // the player, so there is no faithful "carved by [nickname]" string to give
  // (unlike group play's GroupReveal, which has a real in-session nickname per
  // player). The saved/shared solo image still renders the title + coral story
  // faithfully with no byline, matching keepsake-gallery/01's AC-02 "when
  // present" wording.
  return (
    <Reveal
      assembled={assembled}
      template={template}
      attribution={<PersonalSummary filledCount={filledCount} />}
      onPlayAgain={handlePlayAgain}
      onHome={onExit}
      exitAction={{ label: 'Back to home', onClick: onExit }}
      favorite={{ templateId: template.id, title: template.title }}
      revealPresentation={revealSurfaces.revealPresentation}
      reactionRow={<ReactionRow counts={reactions.counts} selected={reactions.mine} onReact={handleReact} />}
    />
  );
}
