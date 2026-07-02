// ----------------------------------------------------------------------------
//  Solo - the local, single-client Classic-blind round (single-player/01,
//  issue #29, "my family is laughing in the car" thin slice).
//
//  This screen is almost entirely COMPOSITION, not new mechanics: it wires
//  together pieces that already exist -
//    - the engine (createCollection / collectWord / isCollectionComplete /
//      assembleStory, ../engine/engine.ts)
//    - the Classic-blind mode config (../engine/modes/classicBlind.ts)
//    - the family-safe content gate (selectTemplates, ../content/familySafe.ts)
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
//  fresh random template from the SAME family-safe selection, resets the
//  collection and blank index, and goes straight back to 'fill' - it never
//  revisits 'setup'. The family-safe toggle position is therefore sticky for
//  the whole solo session until the player exits to Home.
//
//  Child safety: every free-text submission is routed through collectWord's
//  injected SafetyCheck hook (checkWord, the real server-backed filter) BEFORE
//  it is ever recorded (AC-04) - this file never reimplements or bypasses
//  that check. The family-safe toggle only narrows which curated templates
//  selectTemplates offers; it never touches the profanity filter.
//
//  story-selection/02 adds the SOLO length choice ("Quick tale" / "Full tale",
//  defaulting to 'full', AC-01/AC-06) alongside the family-safe toggle. The
//  pick composes both content-selection stages in FIXED order, safety FIRST
//  (AC-05): selectTemplates (family-safe gate) -> selectByLengthOrFallback
//  (story-selection/01's length stage, which degrades to the family-safe pool
//  if the length preference would leave nothing, AC-06) -> pickRandomTemplate.
//  The length choice is sticky across the solo session's replay, exactly like
//  the family-safe toggle already is (see handlePlayAgain below).
//
//  story-selection/03 adds the FRESHNESS stage as the LAST filter before the
//  random pick (AC-05): ... -> selectByLengthOrFallback -> selectFreshOrRecycle
//  (../content/fresh.ts, filtering out templates this DEVICE has already
//  played, per ../content/playedHistory.ts's localStorage-backed history) ->
//  pickRandomTemplate. Both handleStart and handlePlayAgain are fresh RANDOM
//  picks, so both route through pickTemplate below and both APPEND the chosen
//  id to device history in beginRound, AFTER the pick (AC-01). Freshness never
//  re-widens the pool - it only narrows the already safety+length-filtered
//  set, and recycles (reopens) it once every template has been played rather
//  than erroring (AC-03). The device history stores template IDS ONLY, no
//  words, no PII (AC-06); it SURVIVES a page refresh because it lives in
//  localStorage, not component state.
//
//  AC-04 bypass seam (no pinned-replay UI exists yet): a FUTURE pinned-
//  template "replay this exact tale" affordance would call beginRound directly
//  with a specific template, SKIPPING both pickTemplate's freshness filter and
//  beginRound's appendPlayedId call - see the comment on beginRound below for
//  exactly where that branch would go. Today, every call site (handleStart,
//  handlePlayAgain) is a random pick, so both filter and both record history.
// ----------------------------------------------------------------------------

import { useRef, useState } from 'react';
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
import { classicBlind } from '../engine/modes/classicBlind';
import { getBlanks, type Template } from '../engine/template';
import { checkWord } from '../safety/checkWord';
import { recordSoloServe } from '../telemetry/serveLog';
import { FillBlank } from './FillBlank';
import { Reveal } from './Reveal';

export interface SoloProps {
  /** Return to Home. Solo never set a room, so this is purely a view change for the caller. */
  onExit: () => void;
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

/** The lightweight solo setup screen: family-safe toggle + length choice + a gold "Start" CTA. */
function SoloSetup({
  familySafe,
  onFamilySafeChange,
  lengthPref,
  onLengthPrefChange,
  onStart,
  onExit,
}: {
  familySafe: boolean;
  onFamilySafeChange: (checked: boolean) => void;
  lengthPref: LengthPreference;
  onLengthPrefChange: (value: LengthPreference) => void;
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

export function Solo({ onExit }: SoloProps) {
  const [phase, setPhase] = useState<SoloPhase>('setup');
  const [familySafe, setFamilySafe] = useState(FAMILY_SAFE_DEFAULT);
  // story-selection/02, AC-01/AC-06: defaults to 'full' so a session that never
  // touches the length choice behaves exactly like before this story existed.
  const [lengthPref, setLengthPref] = useState<LengthPreference>('full');
  const [template, setTemplate] = useState<Template | undefined>(undefined);
  const [blankIndex, setBlankIndex] = useState(0);

  // The collection lives in a ref (not state): collectWord mutates it in
  // place and FillBlank re-renders are driven by `blankIndex`/`phase`
  // instead, matching engine.ts's "mutates and returns `collected`" contract.
  const collectionRef = useRef<CollectedWords>(createCollection());

  const beginRound = (chosen: Template) => {
    collectionRef.current = createCollection();
    setTemplate(chosen);
    setBlankIndex(0);
    setPhase('fill');
    // story-selection/04 (anonymous serve log, AC-02): fire-and-forget one
    // anonymous "template served" event. This covers BOTH handleStart and
    // handlePlayAgain (both route through beginRound). It never awaits, never
    // retries, and never blocks the transition to 'fill' (AC-03), and carries no
    // PII - just the template + the current family-safe toggle (AC-04).
    recordSoloServe({ template: chosen, familySafe });
    // story-selection/03 (freshness rotation, AC-01): record this template as
    // played on THIS device, AFTER the pick, so the NEXT pickTemplate() call
    // excludes it until the eligible pool runs dry. Both call sites that reach
    // beginRound today (handleStart, handlePlayAgain) are fresh RANDOM picks
    // that already ran through selectFreshOrRecycle, so both must append here.
    //
    // AC-04 bypass seam: a FUTURE pinned-template replay ("play this exact
    // tale again") would call beginRound with a specific template WITHOUT this
    // appendPlayedId call (and without the caller having filtered through
    // selectFreshOrRecycle) - replaying a favorite must not make the random
    // pick "forget" the other templates this device has not seen yet. No such
    // path exists today; this comment marks where it would branch in.
    appendPlayedId(chosen.id);
  };

  // story-selection/02 + /03: compose the content-selection stages in FIXED
  // order, safety FIRST, freshness LAST (AC-05) - family-safe gate -> length
  // stage (degrades to the family-safe pool if the length preference would
  // leave nothing, AC-06) -> freshness stage (excludes templates already
  // played on this device; recycles the whole pool once it runs dry rather
  // than erroring, AC-03) -> pickRandomTemplate. Freshness only ever narrows
  // within the already safety+length-filtered pool - it never widens it.
  const pickTemplate = () =>
    pickRandomTemplate(
      selectFreshOrRecycle(
        selectByLengthOrFallback(selectTemplates(seedLibrary, familySafe), lengthPref),
        loadPlayedIds(),
      ),
    );

  const handleStart = () => {
    const chosen = pickTemplate();
    // Guard the "no templates match the current family-safe position" case
    // rather than asserting non-null; the seed library always has at least
    // one family-safe template today, but this keeps the type honest.
    if (!chosen) return;
    beginRound(chosen);
  };

  const handlePlayAgain = () => {
    const chosen = pickTemplate();
    if (!chosen) return;
    beginRound(chosen);
  };

  if (phase === 'setup' || !template) {
    return (
      <SoloSetup
        familySafe={familySafe}
        onFamilySafeChange={setFamilySafe}
        lengthPref={lengthPref}
        onLengthPrefChange={setLengthPref}
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
      classicBlind,
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
      />
    );
  }

  // 'reveal' (or a fully-collected round that fell through the guard above).
  const assembled = assembleStory(template, collectionRef.current);
  const filledCount = countFilledWords(collectionRef.current);

  return (
    <Reveal
      assembled={assembled}
      template={template}
      attribution={<PersonalSummary title={assembled.title} filledCount={filledCount} />}
      onPlayAgain={handlePlayAgain}
      onHome={onExit}
      exitAction={{ label: 'Back to home', onClick: onExit }}
    />
  );
}
