// ----------------------------------------------------------------------------
//  GroupRound - the group-play round screen (group-play/01 + /02 + /03,
//  issues #30/#31/#32).
//
//  group-play/02 narrowed collection to each player's OWN assigned blanks: the
//  server deals the template's blanks round-robin and tells THIS client only its
//  own blank indices (the hook's `assignedBlankIndices`, from the per-connection
//  "YourBlanks" message). This screen fills ONLY those blanks, by prompt only
//  (Classic blind - no story context, AC-02).
//
//  group-play/03 makes collection SERVER-AUTHORITATIVE: each blank's FillBlank
//  onSubmitWord calls the hub's submitWord(blankIndex, word) instead of a local
//  engine collection. The SERVER runs the child-safety filter FIRST and records
//  only on pass (AC-01, AC-06) - this screen never records an unchecked word and
//  never assembles the story itself. A SKIP submits an EMPTY word to the server so
//  the blank records an empty placeholder and completion is still reached, keeping
//  reveal alignment (the same "skip = empty placeholder" rule the engine documents;
//  the server enforces it). Once this client has submitted its LAST assigned blank,
//  it shows the Waiting interstitial until the hub's shared `reveal` arrives (App
//  routes every client to the shared Reveal the moment it does, AC-05).
//
//  What this screen keeps LOCAL: only a copy of THIS client's own submitted words
//  (blank prompt + the word), so the Waiting screen's "Review my words" needs no
//  server round-trip (AC-04). It never holds another player's words - those are
//  never sent before the reveal (AC-01).
//
//  group-play/05 (host picks the mode) makes this screen MODE-AWARE without any
//  new mechanics: it resolves the round's `mode` (chosen by the host, broadcast on
//  RoundStarted) through the SHARED mode registry (./modeRegistry.ts, the same list
//  Solo uses) and passes that mode's fill-time surfaces into FillBlank's optional
//  game-modes/03 slots - answerSurface for Word Bank (a curated tap list whose own
//  submit calls the SAME handleSubmitWord), nothing for Classic Blind / Progressive
//  Reveal. The collection seam is IDENTICAL for every mode (server-authoritative
//  submitWord); only the surface differs. Progressive Reveal's paced reveal is a
//  REVEAL-time surface, resolved on the shared Reveal screen (App's GroupReveal),
//  not here. Progressive Story is not offered for group (AC-05), so this screen
//  never needs a live cross-player "story so far".
//
//  This is COMPOSITION, not new mechanics: it wires FillBlank (reused as-is, its
//  onSubmitWord now pointed at the hub) and Waiting. It never forks the engine and
//  never reimplements assembly. If a template id has no match in the bundled
//  seedLibrary (a catalog / library drift), it renders a friendly notice instead
//  of throwing; blank indices outside the template are dropped, not crashed.
//
//  Two timing states this handles calmly:
//    - `assignedBlankIndices` is null: the round has started but this client's
//      "YourBlanks" has not arrived yet (a brief network beat). Show a calm
//      "dealing your blanks" state rather than an empty or broken screen.
//    - this client was dealt no blanks (fewer blanks than players): it has nothing
//      to fill, so it goes straight to the Waiting screen and waits for the reveal.
//
//  Styling: theme-driven only (no hex/px literals). FontAwesome only. No em
//  dashes in any prose/strings.
// ----------------------------------------------------------------------------

import { useMemo, useRef, useState } from 'react';
import { Box, Button, Stack, Typography } from '@mui/material';
import { AppBar, BottomActionBar } from '../components';
import { seedLibrary } from '../content/seedLibrary';
import { createCollection } from '../engine/engine';
import { getBlanks, type Blank } from '../engine/template';
import type { CollectProgress } from '../signalr/useGameHub';
import { FillBlank } from './FillBlank';
import { findMode } from './modeRegistry';
import { Waiting, type MyWord } from './Waiting';

export interface GroupRoundProps {
  /** The round's template id (from the hub's RoundStarted broadcast); resolved to full content here. */
  templateId: string;
  /**
   * The round's mode id (group-play/05), from the hub's RoundStarted broadcast -
   * the mode the HOST chose for the whole room. Resolved through the SHARED mode
   * registry (findMode) to the mode's fill-time surfaces (answerSurface for Word
   * Bank; none for Classic Blind / Progressive Reveal), which plug into FillBlank's
   * game-modes/03 optional slots. The reveal-time surface (Progressive Reveal's
   * paced body) is resolved on the shared Reveal screen, not here. An out-of-sync
   * id falls back to Classic Blind (findMode never returns undefined), so a drift
   * renders a safe default rather than crashing the round.
   */
  mode: string;
  /**
   * The blank INDICES this client owes for the round (group-play/02), from the
   * hub's per-connection "YourBlanks" message, or null until it arrives (a brief
   * "dealing your blanks" beat after the round starts). This client fills ONLY
   * these blanks, by prompt only (Classic blind, AC-02).
   */
  assignedBlankIndices: number[] | null;
  /**
   * Room-wide collection progress (group-play/03), or null until the first
   * "CollectProgress" broadcast. Passed straight through to the Waiting screen's
   * status card + progress row; carries NO other player's words (AC-01).
   */
  collectProgress: CollectProgress | null;
  /**
   * Submit ONE word for ONE assigned blank (group-play/03). The SERVER runs the
   * safety filter FIRST and records only on pass (AC-01, AC-06); resolves with
   * FillBlank's { accepted, message } contract. A skip submits an empty word.
   */
  submitWord: (blankIndex: number, word: string) => Promise<{ accepted: boolean; message?: string }>;
  /**
   * reveal-delight/03 (AC-04): the nickname wearing the Golden Guardian crown this
   * round (the previous round's funniest-word winner), or null. Passed straight to
   * the Waiting screen so the crowned player's Guardian shows the crown in the
   * progress row.
   */
  crownedNickname?: string | null;
  /** Leave the round and return Home. */
  onLeave: () => void;
}

/** One of this client's assigned blanks, paired with its TEMPLATE blank index (the wire key for submitWord). */
interface AssignedBlank {
  /** The blank's index into the template's ordered blanks - the authoritative key the server records against. */
  index: number;
  /** The resolved Blank (prompt / category / spark words) rendered by FillBlank. */
  blank: Blank;
}

/** A calm chrome shell (AppBar + centered content + a single back CTA) shared by the non-fill states. */
function RoundNotice({ children, onLeave }: { children: React.ReactNode; onLeave: () => void }) {
  return (
    <Box sx={{ position: 'relative', minHeight: '100dvh', maxWidth: 430, mx: 'auto' }}>
      <AppBar
        title="Round"
        leftAction={{ icon: 'xmark', label: 'Leave round', onClick: onLeave }}
      />
      <Stack spacing={3} alignItems="center" sx={{ px: 5.5, pt: 6, textAlign: 'center' }}>
        {children}
      </Stack>
      <BottomActionBar>
        <Button variant="contained" fullWidth onClick={onLeave}>
          Back to home
        </Button>
      </BottomActionBar>
    </Box>
  );
}

/**
 * The calm "dealing your blanks" beat (group-play/02): the round has started but
 * this client's own blanks have not arrived from the hub yet. Deliberately
 * passive and reassuring - not a spinner-of-doom.
 */
function DealingBlanks({ onLeave }: { onLeave: () => void }) {
  return (
    <RoundNotice onLeave={onLeave}>
      <Typography sx={{ fontFamily: '"Fredoka", sans-serif', fontWeight: 600, fontSize: 20 }}>
        Dealing your blanks...
      </Typography>
      <Typography sx={{ fontSize: 15, fontWeight: 600, color: 'text.secondary' }}>
        Hang tight - your crew is sharing out the words. Yours land in just a moment.
      </Typography>
    </RoundNotice>
  );
}

export function GroupRound({
  templateId,
  mode,
  assignedBlankIndices,
  collectProgress,
  submitWord,
  crownedNickname,
  onLeave,
}: GroupRoundProps) {
  // Resolve the round's mode (group-play/05) to its registry entry ONCE per mode.
  // The whole room plays the mode the host chose; findMode falls back to Classic
  // blind for an out-of-sync id, so this never throws (AC-03/AC-08).
  const gameMode = useMemo(() => findMode(mode), [mode]);
  // Resolve the full template from the bundled seed library BY ID (the server
  // ships only the id - content stays client-side). Memoized on the id.
  const template = useMemo(
    () => seedLibrary.find((t) => t.id === templateId),
    [templateId],
  );

  // Resolve this client's assigned blank INDICES to { index, blank } pairs (prompt
  // only - Classic blind, AC-02). Keeping the TEMPLATE index alongside each blank
  // matters: it is the authoritative key submitWord records against server-side,
  // and it is NOT the same as the local fill position (drift indices are dropped).
  // Indices outside the template's blanks (a catalog / library drift) are dropped
  // rather than crashing. Memoized on the template + the assignment.
  const assignedBlanks = useMemo<AssignedBlank[]>(() => {
    if (!template || !assignedBlankIndices) return [];
    const blanks = getBlanks(template);
    return assignedBlankIndices
      .map((index) => ({ index, blank: blanks[index] }))
      .filter((entry): entry is AssignedBlank => entry.blank !== undefined);
  }, [template, assignedBlankIndices]);

  const [phase, setPhase] = useState<'fill' | 'submitted'>('fill');
  const [blankPosition, setBlankPosition] = useState(0);

  // A LOCAL copy of THIS client's own submitted words (prompt + word), in fill
  // order, so the Waiting screen's "Review my words" needs no server round-trip
  // (AC-04). Kept in a ref (like Solo's collection) - re-renders are driven by
  // blankPosition/phase. It holds only this client's words, never another's.
  const myWordsRef = useRef<MyWord[]>([]);

  // A template id with no match in the bundled library (catalog / library drift)
  // - render a friendly notice rather than throwing.
  if (!template) {
    return (
      <RoundNotice onLeave={onLeave}>
        <Typography sx={{ fontSize: 15, fontWeight: 600, color: 'text.secondary' }}>
          We could not find that tale on this device - please head back and try
          starting again.
        </Typography>
      </RoundNotice>
    );
  }

  // The round has started but this client's blanks have not arrived yet (a brief
  // network beat after RoundStarted) - show the calm "dealing" state (gp/02).
  if (assignedBlankIndices === null) {
    return <DealingBlanks onLeave={onLeave} />;
  }

  // This client has submitted all its blanks (or was dealt none): wait for the
  // shared reveal. App routes every client to the shared Reveal when the hook's
  // `reveal` arrives (AC-05); until then this is the passive Waiting interstitial.
  if (phase === 'submitted' || assignedBlanks.length === 0) {
    return (
      <Waiting
        progress={collectProgress}
        myWords={myWordsRef.current}
        crownedNickname={crownedNickname}
        onLeave={onLeave}
      />
    );
  }

  const current = assignedBlanks[blankPosition];

  // Move to the next assigned blank, or to the Waiting screen once this client has
  // submitted its LAST one. Completion for THIS client is "filled all my own
  // blanks" - the SERVER decides when the whole ROUND is complete (and broadcasts
  // the reveal); this client just waits after its own last word.
  const advance = () => {
    if (blankPosition + 1 >= assignedBlanks.length) {
      setPhase('submitted');
      return;
    }
    setBlankPosition((position) => position + 1);
  };

  // Record this client's own word locally (for "Review my words") after the server
  // has accepted it. Never records a word the server rejected.
  const recordLocally = (word: string) => {
    if (!current) return;
    myWordsRef.current = [...myWordsRef.current, { prompt: current.blank.prompt, word }];
  };

  const handleSubmitWord = async (word: string) => {
    // Unreachable given the currentBlank render guard below, but fail (not
    // silently succeed) so an unexpected state surfaces instead of clearing the
    // input as if the word were accepted.
    if (!current) {
      return { accepted: false, message: 'Something went off - please try again.' } as const;
    }
    // SERVER-AUTHORITATIVE submit: the hub runs the safety filter FIRST and records
    // only on pass (AC-01, AC-06). On accepted we mirror the word locally and
    // advance; on rejection FillBlank shows the message inline and lets the player
    // retry (no local record, no advance).
    const result = await submitWord(current.index, word);
    if (result.accepted) {
      recordLocally(word);
      advance();
    }
    return result;
  };

  const handleSkip = () => {
    if (!current) return;
    // A skip submits an EMPTY word to the server so the blank records an empty
    // placeholder (preserving reveal alignment - the server's skip rule). An empty
    // word passes the safety filter, and this blank is this client's own, so the
    // submit succeeds; we record the empty word locally and advance. Fire-and-await
    // in an inner async since FillBlank's onSkip is synchronous (void).
    const blankIndex = current.index;
    void (async () => {
      const result = await submitWord(blankIndex, '');
      if (result.accepted) {
        recordLocally('');
        advance();
      }
    })();
  };

  // Resolve the mode's fill-time surfaces for THIS blank (group-play/05, AC-03).
  // Classic blind returns {} (FillBlank renders its free-text default unchanged);
  // Word Bank supplies answerSurface (the curated tap list), whose own submit
  // affordance calls the SAME handleSubmitWord - never a second path into the
  // server submit (game-modes/03 AC-06). Progressive Reveal changes only the
  // reveal, so it too returns {} here. collectedSoFar is an empty collection:
  // group play never offers Progressive Story (AC-05), the only mode that reads
  // it, so the three group modes ignore it.
  const fillSurfaces = gameMode.fillSurfaces({
    template,
    collectedSoFar: createCollection(),
    currentBlank: current.blank,
    onSubmit: handleSubmitWord,
  });

  return (
    <FillBlank
      key={current.blank.id}
      subject={template.title}
      blank={current.blank}
      wordNumber={blankPosition + 1}
      totalWords={assignedBlanks.length}
      onSubmitWord={handleSubmitWord}
      onSkip={handleSkip}
      onExit={onLeave}
      seeContext={fillSurfaces.seeContext}
      answerSurface={fillSurfaces.answerSurface}
    />
  );
}
