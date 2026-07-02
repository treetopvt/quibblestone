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
//  each RANDOM round start (beginRound with record:true), and story-selection/05
//  shows a quiet per-tale thumbs vote on Reveal (taleFeedback) - both anonymous,
//  both never gate the flow (see recordSoloServe / TaleFeedback).
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
  StoryLengthChoice,
} from '../components';
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
import { recordSoloServe } from '../telemetry/serveLog';
import { FillBlank } from './FillBlank';
import { Reveal } from './Reveal';
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
 * The solo personal summary (Reveal's `attribution` slot): title + "You
 * filled N words". Solo does NOT show the group Round Complete crew recap
 * (AC-07) - just a small, theme-driven, PII-free line about this one player.
 */
function PersonalSummary({ title, filledCount }: { title: string; filledCount: number }) {
  return (
    <Stack alignItems="center" spacing={0.5}>
      <Typography
        variant="overline"
        sx={{ fontSize: 11, fontWeight: 800, color: 'text.secondary', lineHeight: 1 }}
      >
        {title}
      </Typography>
      <Typography sx={{ fontSize: 14, fontWeight: 700, color: 'teal.dark' }}>
        You filled {filledCount} {filledCount === 1 ? 'word' : 'words'}
      </Typography>
    </Stack>
  );
}

/**
 * One tappable mode card in the solo picker (single-player/02, AC-01): icon +
 * label + blurb as a big tap target, teal-highlighted when selected (reusing
 * the same teal tap language as WordBankAnswer's chips and FillBlank's spark
 * row). Disabled when the mode has no eligible template for the current
 * family-safe position, so a player can never select a mode that cannot start
 * (AC-04).
 *
 * A11y: the picker is a SINGLE-CHOICE control, so each card is a `role="radio"`
 * with `aria-checked` (not a toggle button with `aria-pressed`) inside the
 * parent `role="radiogroup"` - screen readers then announce "one of N" rather
 * than an independent on/off toggle.
 */
function ModeCard({
  mode,
  selected,
  disabled,
  onSelect,
}: {
  mode: SoloMode;
  selected: boolean;
  disabled: boolean;
  onSelect: () => void;
}) {
  const theme = useTheme();
  return (
    <Box
      component="button"
      type="button"
      role="radio"
      aria-checked={selected}
      disabled={disabled}
      onClick={onSelect}
      sx={{
        display: 'flex',
        alignItems: 'center',
        gap: 2,
        width: '100%',
        textAlign: 'left',
        cursor: 'pointer',
        border: `2px solid ${selected ? theme.palette.teal.main : alpha(theme.palette.teal.main, 0.24)}`,
        borderRadius: 3,
        px: 2.5,
        py: 2,
        bgcolor: selected ? alpha(theme.palette.teal.main, 0.14) : 'background.paper',
        '&:hover': { bgcolor: alpha(theme.palette.teal.main, 0.08) },
        '&:focus-visible': { outline: `2px solid ${theme.palette.teal.dark}`, outlineOffset: 2 },
        '&:disabled': { cursor: 'not-allowed', opacity: 0.45 },
      }}
    >
      <Box
        sx={{
          flexShrink: 0,
          width: 44,
          height: 44,
          borderRadius: 2,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          bgcolor: alpha(theme.palette.teal.main, 0.16),
          color: theme.palette.teal.dark,
        }}
      >
        <FontAwesomeIcon icon={mode.icon} style={{ width: 20, height: 20 }} />
      </Box>
      <Stack spacing={0.25} sx={{ flexGrow: 1 }}>
        <Typography sx={{ fontWeight: 800, fontSize: 15.5, color: 'text.primary' }}>
          {mode.config.label}
        </Typography>
        <Typography sx={{ fontWeight: 600, fontSize: 13, color: 'text.secondary', lineHeight: 1.4 }}>
          {mode.blurb}
        </Typography>
      </Stack>
      {selected && (
        <FontAwesomeIcon
          icon="circle-check"
          style={{ width: 20, height: 20, color: theme.palette.teal.main, flexShrink: 0 }}
        />
      )}
    </Box>
  );
}

/** The lightweight solo setup screen: family-safe toggle + length choice + mode picker + a gold "Start" CTA. */
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
  return (
    <Box sx={{ position: 'relative', minHeight: '100dvh', maxWidth: 430, mx: 'auto' }}>
      <AppBar
        title="Play solo"
        leftAction={{ icon: 'arrow-left', label: 'Back to home', onClick: onExit }}
      />

      <Stack spacing={4} sx={{ px: 5.5, pt: 3 }}>
        <Typography sx={{ fontSize: 16, fontWeight: 600, color: 'text.secondary', textAlign: 'center' }}>
          A quick one-player round, right now - no room, no code, just you and a silly tale.
        </Typography>

        <FamilySafeToggle checked={familySafe} onChange={onFamilySafeChange} />
        <StoryLengthChoice value={lengthPref} onChange={onLengthPrefChange} />

        <Stack spacing={1.5} role="radiogroup" aria-labelledby="solo-mode-picker-label">
          <Typography
            id="solo-mode-picker-label"
            sx={{
              fontWeight: 800,
              fontSize: 12.5,
              color: 'text.secondary',
              textTransform: 'uppercase',
              letterSpacing: 0.6,
            }}
          >
            Pick a mode
          </Typography>
          {SOLO_MODES.map((soloMode) => (
            <ModeCard
              key={soloMode.config.id}
              mode={soloMode}
              selected={soloMode.config.id === mode.config.id}
              // A mode with no eligible template at the current family-safe
              // position (e.g. Word Bank when no family-safe template has a
              // bank) is disabled, not a dead Start button (AC-04).
              disabled={soloMode.eligibleTemplates(seedLibrary, familySafe).length === 0}
              onSelect={() => onModeChange(soloMode)}
            />
          ))}
        </Stack>

        <BottomActionBarSpacer />
      </Stack>

      <BottomActionBar>
        <Button variant="contained" fullWidth onClick={onStart}>
          Start
        </Button>
      </BottomActionBar>
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

  // The collection lives in a ref (not state): collectWord mutates it in
  // place and FillBlank re-renders are driven by `blankIndex`/`phase`
  // instead, matching engine.ts's "mutates and returns `collected`" contract.
  const collectionRef = useRef<CollectedWords>(createCollection());

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
    setPhase('fill');
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

  return (
    <Reveal
      assembled={assembled}
      template={template}
      attribution={<PersonalSummary title={assembled.title} filledCount={filledCount} />}
      onPlayAgain={handlePlayAgain}
      onHome={onExit}
      exitAction={{ label: 'Back to home', onClick: onExit }}
      taleFeedback={{ templateId: template.id, mode: 'solo' }}
      favorite={{ templateId: template.id, title: template.title }}
      revealPresentation={revealSurfaces.revealPresentation}
    />
  );
}
