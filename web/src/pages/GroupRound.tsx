// ----------------------------------------------------------------------------
//  GroupRound - the group-play round screen (group-play/01 + /02, issues #30/#31).
//
//  ============================ STILL INTERIM ON SUBMIT =======================
//  group-play/02 narrows collection to each player's OWN assigned blanks: the
//  server deals the template's blanks round-robin and tells THIS client only its
//  own blank indices (the hook's `assignedBlankIndices`, from the per-connection
//  "YourBlanks" message). This screen now fills ONLY those blanks, by prompt only
//  (Classic blind - no story context, AC-02).
//
//  What is STILL interim (group-play/03 replaces it): collection stays LOCAL over
//  the assigned subset - this screen does NOT submit to the hub and does NOT build
//  a shared reveal. On completion it shows a calm placeholder. Do not grow
//  reveal/submission logic here - that is group-play/03.
//  ============================================================================
//
//  This is COMPOSITION, not new mechanics (same reuse contract Solo documents):
//  it wires the engine (createCollection / collectWord / skipBlank /
//  isCollectionComplete), the Classic-blind mode config, the real safety filter
//  client (checkWord), and the shared FillBlank screen. It never forks the engine,
//  never reimplements collection, and never edits FillBlank. If a template id has
//  no match in the bundled seedLibrary (a catalog / library drift), it renders a
//  friendly notice instead of throwing.
//
//  Two timing states this handles calmly:
//    - `assignedBlankIndices` is null: the round has started but this client's
//      "YourBlanks" has not arrived yet (a brief network beat). Show a calm
//      "dealing your blanks" state rather than an empty or broken screen.
//    - an index points outside the template's blanks (a catalog / library drift):
//      it is filtered out rather than crashing, so the player fills whatever real
//      blanks they were dealt.
//
//  Child safety: every free-text submission routes through collectWord's injected
//  checkWord hook (the real server-backed filter) BEFORE it is recorded (AC-04) -
//  this file never bypasses that check. A skip records an empty placeholder via
//  the engine's skipBlank (never free text, never filtered) so positional
//  alignment holds across the assigned subset.
//
//  Styling: theme-driven only (no hex/px literals). FontAwesome only. No em
//  dashes in any prose/strings.
// ----------------------------------------------------------------------------

import { useMemo, useRef, useState } from 'react';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { Box, Button, Stack, Typography } from '@mui/material';
import { AppBar, BottomActionBar } from '../components';
import { seedLibrary } from '../content/seedLibrary';
import {
  collectWord,
  createCollection,
  isCollectionComplete,
  skipBlank,
  type CollectedWords,
} from '../engine/engine';
import { classicBlind } from '../engine/modes/classicBlind';
import { getBlanks, type Blank } from '../engine/template';
import { checkWord } from '../safety/checkWord';
import { FillBlank } from './FillBlank';

export interface GroupRoundProps {
  /** The round's template id (from the hub's RoundStarted broadcast); resolved to full content here. */
  templateId: string;
  /**
   * The blank INDICES this client owes for the round (group-play/02), from the
   * hub's per-connection "YourBlanks" message, or null until it arrives (a brief
   * "dealing your blanks" beat after the round starts). This client fills ONLY
   * these blanks, by prompt only (Classic blind, AC-02).
   */
  assignedBlankIndices: number[] | null;
  /** Leave the round and return Home. */
  onLeave: () => void;
}

/**
 * A local, non-identifying attribution tag for the engine's SubmittedWord shape
 * (interim: group-play/03 replaces local collection with hub submissions keyed
 * by the real player). Not an account, not PII, never transmitted.
 */
const INTERIM_PLAYER_ID = 'group-interim-player';

/** A calm chrome shell (AppBar + centered content + a single back CTA) shared by the non-fill states. */
function RoundNotice({
  children,
  onLeave,
  icon,
}: {
  children: React.ReactNode;
  onLeave: () => void;
  icon?: 'check';
}) {
  return (
    <Box sx={{ position: 'relative', minHeight: '100dvh', maxWidth: 430, mx: 'auto' }}>
      <AppBar
        title="Round"
        leftAction={{ icon: 'xmark', label: 'Leave round', onClick: onLeave }}
      />
      <Stack spacing={3} alignItems="center" sx={{ px: 5.5, pt: 6, textAlign: 'center' }}>
        {icon === 'check' && (
          <Box sx={{ color: 'teal.main', fontSize: 44, display: 'flex' }}>
            <FontAwesomeIcon icon="check" />
          </Box>
        )}
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

/**
 * The calm interim "round complete" placeholder (group-play/01). group-play/03
 * replaces this with the shared, broadcast reveal - there is intentionally no
 * reveal or submission here yet.
 */
function InterimComplete({ onLeave }: { onLeave: () => void }) {
  return (
    <RoundNotice onLeave={onLeave} icon="check">
      <Typography sx={{ fontFamily: '"Fredoka", sans-serif', fontWeight: 600, fontSize: 20 }}>
        Your words are carved
      </Typography>
      <Typography sx={{ fontSize: 15, fontWeight: 600, color: 'text.secondary' }}>
        Hang tight - the shared reveal arrives in a later update. For now, this
        is where your crew's story will come together.
      </Typography>
    </RoundNotice>
  );
}

export function GroupRound({ templateId, assignedBlankIndices, onLeave }: GroupRoundProps) {
  // Resolve the full template from the bundled seed library BY ID (the server
  // ships only the id - content stays client-side). Memoized on the id.
  const template = useMemo(
    () => seedLibrary.find((t) => t.id === templateId),
    [templateId],
  );

  // Resolve this client's assigned blank INDICES to their Blank objects (prompt
  // only - Classic blind, AC-02). Indices outside the template's blanks (a
  // catalog / library drift) are dropped rather than crashing. Memoized on the
  // template + the assignment so it is stable across re-renders driven by
  // blankPosition/phase (matching the engine's ref-based collection contract).
  const assignedBlanks = useMemo<Blank[]>(() => {
    if (!template || !assignedBlankIndices) return [];
    const blanks = getBlanks(template);
    return assignedBlankIndices
      .map((index) => blanks[index])
      .filter((b): b is Blank => b !== undefined);
  }, [template, assignedBlankIndices]);

  const [phase, setPhase] = useState<'fill' | 'done'>('fill');
  const [blankPosition, setBlankPosition] = useState(0);

  // The collection lives in a ref: collectWord/skipBlank mutate it in place, and
  // re-renders are driven by blankPosition/phase (matching engine.ts's contract).
  const collectionRef = useRef<CollectedWords>(createCollection());

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

  if (phase === 'done') {
    return <InterimComplete onLeave={onLeave} />;
  }

  // This client was dealt no real blanks (fewer blanks than players, or a full
  // drift) - nothing to fill, so land on the calm complete placeholder.
  if (assignedBlanks.length === 0) {
    return <InterimComplete onLeave={onLeave} />;
  }

  const currentBlank = assignedBlanks[blankPosition];

  const advance = () => {
    // Complete once every ASSIGNED blank has an entry (this client only owes its
    // own subset, not the whole template).
    const allFilled = assignedBlanks.every((b) => collectionRef.current.has(b.id));
    if (allFilled || isCollectionComplete(template, collectionRef.current)) {
      setPhase('done');
      return;
    }
    setBlankPosition((position) => position + 1);
  };

  const handleSubmitWord = async (word: string) => {
    // Unreachable given the currentBlank render guard below, but fail (not
    // silently succeed) so an unexpected state surfaces instead of clearing the
    // input as if the word were accepted.
    if (!currentBlank) {
      return { accepted: false, message: 'Something went off - please try again.' } as const;
    }
    const result = await collectWord(
      collectionRef.current,
      template,
      classicBlind,
      currentBlank.id,
      { playerSessionId: INTERIM_PLAYER_ID, word },
      checkWord,
    );
    if (result.accepted) {
      advance();
    }
    return result;
  };

  const handleSkip = () => {
    if (!currentBlank) return;
    // Record an empty placeholder via the engine's skipBlank so positional
    // alignment holds (same rule as Solo - never leave a skipped blank absent).
    skipBlank(collectionRef.current, template, currentBlank.id, INTERIM_PLAYER_ID);
    advance();
  };

  if (currentBlank) {
    return (
      <FillBlank
        key={currentBlank.id}
        subject={template.title}
        blank={currentBlank}
        wordNumber={blankPosition + 1}
        totalWords={assignedBlanks.length}
        onSubmitWord={handleSubmitWord}
        onSkip={handleSkip}
      />
    );
  }

  // A fully-collected round that fell through the guard above.
  return <InterimComplete onLeave={onLeave} />;
}
