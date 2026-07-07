// ----------------------------------------------------------------------------
//  WordBankAnswer - the Word Bank mode's answer surface (game-modes/04,
//  AC-01/02/03/07) PLUS the "Fresh Runes" jumble on top (game-modes/07).
//
//  What this is: a tappable chip/tile list of curated words drawn from a
//  template's `wordBank` (web/src/engine/template.ts's `WordBankEntry`),
//  filtered down to the entries whose `category` matches the CURRENT blank's
//  `category` (AC-02). It plugs into FillBlank's `answerSurface` slot
//  (game-modes/03's `ModeSurfaces` contract, web/src/pages/modeSurfaces.ts) -
//  it does NOT import or edit FillBlank.tsx itself (AC-01, AC-07). Whichever
//  parent resolves the active mode (the shared mode registry, modeRegistry.ts)
//  instantiates this component and passes it into FillBlank's `answerSurface`
//  prop; this file supplies the surface plus a colocated factory
//  (`wordBankSurfaces`, below) that pairs it with the `ModeSurfaces` shape.
//
//  FRESH RUNES (game-modes/07): instead of showing the WHOLE category pool at
//  once, the surface offers a SUBSET and a "Fresh runes" action that swaps it
//  for a fresh set for the SAME category (AC-01) - so a player who does not
//  like the options just re-rolls. Two SOURCES sit behind one button:
//    - FREE, deterministic reshuffle (AC-02): the default. A pure re-sample of
//      a different subset from the curated, already-vetted pool
//      (../../content/wordBankJumble.ts's `nextOptions`) - instant, offline,
//      free, and the always-safe fallback. Curated words skip the free-text
//      profanity filter EXACTLY as game-modes/04 documents (pre-vetted lists);
//      the family-safe gate is applied UPSTREAM at content-selection time
//      (offerWordBankTemplates), never a per-tap check here.
//    - AI, on demand (AC-03): when a `requestAiJumble` fetcher is injected
//      (Phase D wiring, ai-on-demand-generation/05), the button PREFERS it -
//      a server-side, gated, MODERATED set of fresh words - and falls back to
//      the deterministic reshuffle whenever the gate says it fell back
//      (quota-exhausted, breaker-open, AI unavailable, or too few safe words -
//      game-modes/07 AC-03, story 05 AC-06). The browser NEVER calls AI: the
//      fetcher is a thin REST call the parent injects (see wordBankSurfaces),
//      keeping this surface transport-agnostic (WordBankAnswer imports no
//      fetch/SignalR - the FillBlank reuse contract).
//
//  No engine/axis leak (AC-06): this is purely an answer-surface enhancement +
//  a swappable option SOURCE. It adds NO ModeConfig axis and does NOT touch
//  FillBlank.tsx / Reveal.tsx / engine.ts; a jumbled pick still submits through
//  the SAME `onSubmit` -> `collectWord` path as any word-bank pick.
//
//  Tap-then-submit, not tap-to-submit: tapping a chip SELECTS it (visually
//  highlighted, same teal family as FillBlank's "Need a spark?" row) as the
//  current answer; a separate "Choose this word" submit affordance actually
//  records it. This mirrors FillBlank's own free-text flow so the player always
//  gets one deliberate confirm step, not an accidental tap-and-advance.
//
//  Child safety: tapped words are recorded via the SAME `onSubmit` callback
//  every mode calls (there is never a second path into collection) - engine.ts's
//  `collectWord` already skips the free-text safety check for word-bank picks
//  (curated/pre-vetted). AI-sourced words are a DIFFERENT source: they are
//  moderated SERVER-SIDE by the gate BEFORE they ever reach this surface
//  (game-modes/07 AC-04, story 05), so what this component renders is always
//  already-vetted, from either source. This component carries no PII.
//
//  Pure helpers (AC-02, unit-testable without rendering): `wordsForCategory`
//  (here) and `nextOptions` (../../content/wordBankJumble.ts) are exported
//  standalone so the category-filter + reshuffle logic is asserted in plain
//  Vitest .ts files (this repo has no render harness).
//
//  Styling: every color/spacing token comes from web/src/theme.ts (no hex/px
//  literals). Reuses the teal MUI Chip tap language from FillBlank's spark row.
//  Big tap targets (chunky padding) per the design brief. Icons are FontAwesome
//  only, registered in web/src/fontawesome.ts.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { useEffect, useState } from 'react';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { alpha, useTheme } from '@mui/material/styles';
import { Box, Button, Stack, Typography } from '@mui/material';
import type { Blank, BlankCategory, WordBankEntry } from '../../engine/template';
import { DEFAULT_OFFERING_SIZE, nextOptions, normalizeWord } from '../../content/wordBankJumble';
import type { ModeSurfaces } from '../modeSurfaces';

/** The on-brand label for the jumble action (AC-07): QuibbleStone's stone/carving voice, not a generic "shuffle". Named here so the copy lives in one place, not a bare literal in JSX. */
export const FRESH_RUNES_LABEL = 'Fresh runes';

/**
 * Returns only the word-bank entries whose category matches `category`
 * (AC-02). Pure and standalone so it is unit-testable without rendering
 * anything - see WordBankAnswer.test.ts.
 */
export function wordsForCategory(
  wordBank: readonly WordBankEntry[],
  category: BlankCategory,
): WordBankEntry[] {
  return wordBank.filter((entry) => entry.category === category);
}

/**
 * The moderated result of one AI jumble request (game-modes/07 AC-03, backed by
 * ai-on-demand-generation/05). The parent injects a `RequestAiJumble` fetcher
 * that resolves to this; the surface never constructs the words itself.
 */
export interface AiJumbleOutcome {
  /** The moderated, safe-to-display words (already vetted server-side by the gate). Empty when the gate fell back. */
  words: string[];
  /** The per-session "Fresh Runes left" quota remaining, for the meter (ai-cost-gate/03). */
  remainingQuota: number;
  /** True when the gate degraded to the fallback (quota-exhausted, breaker-open, AI unavailable, too few safe) - the caller runs the deterministic reshuffle. */
  fellBack: boolean;
}

/**
 * The AI jumble fetcher the parent injects (Phase D). Given the blank's
 * `category` and the words already shown (the avoid-list), it resolves to an
 * <see cref="AiJumbleOutcome"/> or null on any transport failure - either of
 * which the surface treats as "fall back to the deterministic reshuffle". The
 * parent closes over the anonymous session key + the family-safe toggle, so the
 * surface stays transport- and identity-agnostic.
 */
export type RequestAiJumble = (
  category: BlankCategory,
  avoid: readonly string[],
) => Promise<AiJumbleOutcome | null>;

export interface WordBankAnswerProps {
  /** The template's full curated word list (web/src/engine/template.ts's `Template.wordBank`). */
  wordBank: readonly WordBankEntry[];
  /** The blank currently being filled; only entries matching its category are shown (AC-02). */
  blank: Blank;
  /**
   * Submits the selected word. The parent wires this to the SAME
   * `onSubmitWord` FillBlank was given (AC-03) - there is never a second path
   * into `collectWord`. Resolves with the outcome; a rejected submission (e.g.
   * an unknown blank id) is shown inline and the player can pick again without
   * advancing.
   */
  onSubmit: (word: string) => Promise<{ accepted: boolean; message?: string }>;
  /**
   * OPTIONAL AI jumble fetcher (game-modes/07 AC-03). When provided, "Fresh
   * runes" PREFERS AI-generated words and falls back to the deterministic
   * reshuffle when the gate falls back. When absent (Phase A, the free layer
   * shipping alone), "Fresh runes" is the deterministic reshuffle only.
   */
  requestAiJumble?: RequestAiJumble;
  /** The subset size a jumble offers at once (defaults to the shared DEFAULT_OFFERING_SIZE). */
  offeringSize?: number;
}

/**
 * The tappable word-bank list with a "Fresh runes" re-roll: select a word, then
 * confirm with "Choose this word". Renders nothing extra when the category has
 * no words (a bank-less/mismatched-category template never crashes - it simply
 * has nothing to tap and the jumble soft-disables).
 */
export function WordBankAnswer({
  wordBank,
  blank,
  onSubmit,
  requestAiJumble,
  offeringSize = DEFAULT_OFFERING_SIZE,
}: WordBankAnswerProps) {
  const theme = useTheme();
  const [selected, setSelected] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [jumbling, setJumbling] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  // The currently-offered subset (a slice of the category pool), and the
  // cumulative set of words shown so far for THIS blank (so each jumble favors
  // words not just shown - game-modes/07 AC-02).
  const [offered, setOffered] = useState<string[]>(() =>
    nextOptions(wordBank, blank.category, [], offeringSize),
  );
  const [shown, setShown] = useState<string[]>(offered);
  // The per-session "Fresh Runes left" meter (ai-cost-gate/03), only known once
  // an AI jumble has run; null means "not applicable / not yet fetched".
  const [remainingQuota, setRemainingQuota] = useState<number | null>(null);

  // Reset the offered set + jumble history whenever the option POOL changes (a
  // new category or a new word bank): the mode registry may reuse this component
  // instance, so state must not leak a previous pool's words. Keyed on the actual
  // inputs (category + wordBank + offeringSize) rather than blank.id, since
  // Blank.id is only unique WITHIN a template (engine/template.ts) - two templates
  // could share a blank id, and this way a pool swap always resets. wordBank is a
  // stable reference per round (the registry passes a shared empty constant when a
  // template has none), so this fires on a genuine pool change, not every render.
  useEffect(() => {
    const fresh = nextOptions(wordBank, blank.category, [], offeringSize);
    setOffered(fresh);
    setShown(fresh);
    setSelected(null);
    setErrorMessage(null);
    setRemainingQuota(null);
  }, [blank.category, wordBank, offeringSize]);

  // Every unique word available for this blank's category, in authored order -
  // used to decide whether a deterministic re-roll can even offer anything new
  // (a huge size returns them all from the pure helper, no duplicate logic).
  const allCategoryWords = nextOptions(wordBank, blank.category, [], Number.MAX_SAFE_INTEGER);
  // Normalize the shown-set the SAME way the pure reshuffle helper does
  // (trim + lower via normalizeWord), so the exhaustion count can never drift
  // from what nextOptions considers "already shown".
  const shownSet = new Set(shown.map(normalizeWord));
  const freshRemaining = allCategoryWords.filter((w) => !shownSet.has(normalizeWord(w))).length;

  // The deterministic re-roll can offer something new only while unseen words
  // remain OR the pool is larger than one screenful (so it can cycle to a
  // different slice). With an AI fetcher the button is always useful (AI can
  // mint brand-new words), so it only soft-disables in the pure-free case.
  const canDeterministicReroll = allCategoryWords.length > offered.length;
  const jumbleDisabled =
    submitting || jumbling || (requestAiJumble === undefined && !canDeterministicReroll);

  const applyOffered = (next: string[], exhausted: boolean) => {
    if (next.length === 0) {
      return;
    }
    setOffered(next);
    // Keep the current selection only if it survived into the new set.
    setSelected((sel) => (sel !== null && next.includes(sel) ? sel : null));
    // Grow the cumulative history so the next re-roll favors unseen words; once
    // the pool is exhausted, restart the walk from the newly-shown set so the
    // action keeps rotating rather than sticking on one slice (AC-02 cycle).
    setShown((prev) => (exhausted ? next : dedupe([...prev, ...next])));
  };

  const reshuffleDeterministic = () => {
    const next = nextOptions(wordBank, blank.category, shown, offeringSize);
    // "exhausted" once this re-roll consumes the last unseen words.
    applyOffered(next, freshRemaining <= offeringSize);
  };

  const handleJumble = async () => {
    if (jumbleDisabled) {
      return;
    }
    setErrorMessage(null);

    // AI-preferred path (AC-03): try the gated fetcher, fall back on anything
    // short of a usable, non-fell-back set.
    if (requestAiJumble !== undefined) {
      setJumbling(true);
      try {
        const outcome = await requestAiJumble(blank.category, shown);
        if (outcome !== null && !outcome.fellBack && outcome.words.length > 0) {
          // AI words are already moderated server-side (story 05) - display them,
          // and surface the meter only for a genuine AI success (so it never
          // counts down invisibly where AI is unavailable and every tap falls back).
          setRemainingQuota(outcome.remainingQuota);
          applyOffered(outcome.words, false);
          return;
        }
        // null outcome or a fell-back gate: degrade to the free reshuffle (AC-03).
      } finally {
        setJumbling(false);
      }
    }

    reshuffleDeterministic();
  };

  const handleTap = (word: string) => {
    setSelected(word);
    setErrorMessage(null);
  };

  const handleSubmit = async () => {
    if (selected === null || submitting) return;
    setSubmitting(true);
    setErrorMessage(null);
    try {
      const result = await onSubmit(selected);
      if (!result.accepted) {
        setErrorMessage(result.message ?? 'That word did not work. Try another!');
        return;
      }
      setSelected(null);
    } finally {
      setSubmitting(false);
    }
  };

  return (
    // No outer margin: this surface renders inside FillBlank's pinned interaction
    // zone, whose flex `gap` handles spacing to the "Skip" link below it.
    <Stack>
      <Stack
        direction="row"
        alignItems="center"
        justifyContent="space-between"
        spacing={1}
        sx={{ mb: 1.25 }}
      >
        <Typography
          sx={{
            fontFamily: '"Nunito", sans-serif',
            fontWeight: 700,
            fontSize: 12.5,
            color: 'text.secondary',
          }}
        >
          Tap a word from the bank
        </Typography>
        {/* Fresh Runes re-roll (AC-01/07), INLINE with the instruction (not on
            its own row below): swaps the offered words for a fresh set for the
            same category. Sitting up here keeps the layout compact and, with the
            reserved-height bank below, keeps "Choose this word" from shifting.
            A compact pill (on-brand label + dice glyph); soft-disables when a
            re-roll can offer nothing new. */}
        <Box
          component="button"
          type="button"
          disabled={jumbleDisabled}
          onClick={handleJumble}
          aria-label={`${FRESH_RUNES_LABEL}: offer a fresh set of words`}
          sx={{
            flexShrink: 0,
            display: 'inline-flex',
            alignItems: 'center',
            gap: 0.75,
            border: `2px solid ${alpha(theme.palette.teal.main, 0.5)}`,
            cursor: 'pointer',
            bgcolor: 'transparent',
            px: 1.75,
            py: 0.75,
            borderRadius: 999,
            color: theme.palette.teal.dark,
            fontFamily: '"Nunito", sans-serif',
            fontWeight: 800,
            fontSize: 13,
            whiteSpace: 'nowrap',
            '&:hover': { bgcolor: alpha(theme.palette.teal.main, 0.1) },
            '&:focus-visible': {
              outline: `2px solid ${theme.palette.teal.dark}`,
              outlineOffset: 2,
            },
            '&:disabled': { cursor: 'not-allowed', opacity: 0.45 },
          }}
        >
          <FontAwesomeIcon icon="dice" style={{ width: 14, height: 14 }} />
          {jumbling ? 'Carving...' : FRESH_RUNES_LABEL}
        </Box>
      </Stack>
      {/* The "N fresh runes left" meter (AC-08 / ai-cost-gate/03), shown only
          once an AI jumble has reported a remaining count. Sits just under the
          row so it never destabilizes the label / Fresh-runes baseline. */}
      {remainingQuota !== null && (
        <Typography
          sx={{
            textAlign: 'right',
            fontFamily: '"Nunito", sans-serif',
            fontWeight: 700,
            fontSize: 11.5,
            color: 'text.secondary',
            mt: -0.5,
            mb: 1,
          }}
        >
          {remainingQuota} fresh {remainingQuota === 1 ? 'rune' : 'runes'} left
        </Typography>
      )}

      {/* Reserved-height chip bank: a stable minHeight (enough for the full
          offering of ~3 rows) with chips top-aligned, so the "Choose this word"
          button below lands in the SAME place no matter how many words the
          current blank or a fresh re-roll offers - it no longer drifts with the
          bank size. A small category simply leaves a little parchment space
          below its chips rather than pulling the button up. */}
      <Stack
        direction="row"
        spacing={1.25}
        flexWrap="wrap"
        useFlexGap
        sx={{ mb: 1.5, minHeight: 152, alignContent: 'flex-start' }}
      >
        {offered.map((word, index) => {
          const isSelected = word === selected;
          return (
            <Box
              // Composite key (word + index): a curated bank may legitimately
              // repeat a word, and a duplicate React key would corrupt chip
              // selection/reuse - the index disambiguates.
              key={`${word}-${index}`}
              component="button"
              type="button"
              // Expose selection to assistive tech, and lock the bank while a
              // submission or jumble is in flight so a stray tap cannot race it.
              aria-pressed={isSelected}
              disabled={submitting || jumbling}
              onClick={() => handleTap(word)}
              sx={{
                border: 'none',
                cursor: 'pointer',
                px: 3,
                py: 1.75,
                borderRadius: 999,
                bgcolor: isSelected
                  ? alpha(theme.palette.teal.main, 0.32)
                  : alpha(theme.palette.teal.main, 0.14),
                color: theme.palette.teal.dark,
                fontFamily: '"Nunito", sans-serif',
                fontWeight: 700,
                fontSize: 15,
                '&:hover': { bgcolor: alpha(theme.palette.teal.main, 0.22) },
                '&:focus-visible': {
                  outline: `2px solid ${theme.palette.teal.dark}`,
                  outlineOffset: 2,
                },
                '&:disabled': { cursor: 'not-allowed', opacity: 0.6 },
              }}
            >
              {word}
            </Box>
          );
        })}
      </Stack>

      {errorMessage && (
        <Typography
          role="alert"
          sx={{
            mb: 2.5,
            fontFamily: '"Nunito", sans-serif',
            fontWeight: 700,
            fontSize: 13.5,
            color: 'coral.main',
          }}
        >
          {errorMessage}
        </Typography>
      )}

      <Button
        type="button"
        variant="contained"
        fullWidth
        disabled={submitting || jumbling || selected === null}
        onClick={handleSubmit}
      >
        {submitting ? 'Choosing...' : 'Choose this word'}
        <FontAwesomeIcon icon="arrow-right" style={{ width: 18, height: 18 }} />
      </Button>
    </Stack>
  );
}

/** De-duplicates a word list (trim + lower via normalizeWord) while preserving first-seen order + casing. */
function dedupe(words: readonly string[]): string[] {
  const seen = new Set<string>();
  const out: string[] = [];
  for (const word of words) {
    const key = normalizeWord(word);
    if (seen.has(key)) continue;
    seen.add(key);
    out.push(word);
  }
  return out;
}

/**
 * Colocated `ModeSurfaces` factory pairing this component with Word Bank's
 * `ModeConfig` (web/src/engine/modes/wordBank.ts), matching the pattern every
 * mode uses (game-modes/03's contract): pure axis config lives in
 * `engine/modes/`, the paired React surface lives here in the pages layer. The
 * shared mode registry (modeRegistry.ts) supplies the runtime props - including
 * the optional `requestAiJumble` fetcher (Phase D) - and reads `.answerSurface`
 * off the result to pass into FillBlank.
 */
export function wordBankSurfaces(args: WordBankAnswerProps): ModeSurfaces {
  return {
    answerSurface: <WordBankAnswer {...args} />,
  };
}
