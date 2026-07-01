// ----------------------------------------------------------------------------
//  GroupRound - the INTERIM group-play round screen (group-play/01, issue #30).
//
//  ============================ INTERIM - READ THIS ===========================
//  group-play/01 only proves that the HOST can start a round and that EVERY
//  player transitions into word collection together (AC-01, AC-02). The real
//  group mechanics land in the NEXT stories and REPLACE the body of this file:
//    - group-play/02 narrows collection to each player's OWN assigned blanks
//      (server round-robin distribution; each client learns only its prompts).
//    - group-play/03 wires the hub SUBMIT + the shared, broadcast reveal.
//  Until then, this screen deliberately runs the SAME local Classic-blind
//  collection Solo runs - over the FULL template resolved from seedLibrary by
//  the round's templateId - so there is something real on screen the instant a
//  round starts. It does NOT submit to the hub and does NOT build a reveal; on
//  completion it shows a calm interim placeholder. Do not grow reveal/submission
//  logic here - that is group-play/03.
//  ============================================================================
//
//  This is COMPOSITION, not new mechanics (same reuse contract Solo documents):
//  it wires the engine (createCollection / collectWord / isCollectionComplete),
//  the Classic-blind mode config, the real safety filter client (checkWord), and
//  the shared FillBlank screen. It never forks the engine, never reimplements
//  collection, and never edits FillBlank. If a template id has no match in the
//  bundled seedLibrary (a catalog / library drift), it renders a friendly notice
//  instead of throwing.
//
//  Child safety: every free-text submission routes through collectWord's injected
//  checkWord hook (the real server-backed filter) BEFORE it is recorded (AC-04) -
//  this file never bypasses that check.
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
  type CollectedWords,
} from '../engine/engine';
import { classicBlind } from '../engine/modes/classicBlind';
import { getBlanks } from '../engine/template';
import { checkWord } from '../safety/checkWord';
import { FillBlank } from './FillBlank';

export interface GroupRoundProps {
  /** The round's template id (from the hub's RoundStarted broadcast); resolved to full content here. */
  templateId: string;
  /** Leave the round and return Home. */
  onLeave: () => void;
}

/**
 * A local, non-identifying attribution tag for the engine's SubmittedWord shape
 * (interim: group-play/03 replaces local collection with hub submissions keyed
 * by the real player). Not an account, not PII, never transmitted.
 */
const INTERIM_PLAYER_ID = 'group-interim-player';

/**
 * The calm interim "round complete" placeholder (group-play/01). group-play/03
 * replaces this with the shared, broadcast reveal - there is intentionally no
 * reveal or submission here yet.
 */
function InterimComplete({ onLeave }: { onLeave: () => void }) {
  return (
    <Box sx={{ position: 'relative', minHeight: '100dvh', maxWidth: 430, mx: 'auto' }}>
      <AppBar
        title="Round"
        leftAction={{ icon: 'xmark', label: 'Leave round', onClick: onLeave }}
      />
      <Stack spacing={3} alignItems="center" sx={{ px: 5.5, pt: 6, textAlign: 'center' }}>
        <Box sx={{ color: 'teal.main', fontSize: 44, display: 'flex' }}>
          <FontAwesomeIcon icon="check" />
        </Box>
        <Typography sx={{ fontFamily: '"Fredoka", sans-serif', fontWeight: 600, fontSize: 20 }}>
          Your words are carved
        </Typography>
        <Typography sx={{ fontSize: 15, fontWeight: 600, color: 'text.secondary' }}>
          Hang tight - the shared reveal arrives in a later update. For now, this
          is where your crew's story will come together.
        </Typography>
      </Stack>
      <BottomActionBar>
        <Button variant="contained" fullWidth onClick={onLeave}>
          Back to home
        </Button>
      </BottomActionBar>
    </Box>
  );
}

export function GroupRound({ templateId, onLeave }: GroupRoundProps) {
  // Resolve the full template from the bundled seed library BY ID (the server
  // ships only the id - content stays client-side). Memoized on the id.
  const template = useMemo(
    () => seedLibrary.find((t) => t.id === templateId),
    [templateId],
  );

  const [phase, setPhase] = useState<'fill' | 'done'>('fill');
  const [blankIndex, setBlankIndex] = useState(0);

  // The collection lives in a ref: collectWord mutates it in place, and
  // re-renders are driven by blankIndex/phase (matching engine.ts's contract).
  const collectionRef = useRef<CollectedWords>(createCollection());

  // A template id with no match in the bundled library (catalog / library drift)
  // - render a friendly notice rather than throwing.
  if (!template) {
    return (
      <Box sx={{ position: 'relative', minHeight: '100dvh', maxWidth: 430, mx: 'auto' }}>
        <AppBar
          title="Round"
          leftAction={{ icon: 'xmark', label: 'Leave round', onClick: onLeave }}
        />
        <Stack spacing={3} alignItems="center" sx={{ px: 5.5, pt: 6, textAlign: 'center' }}>
          <Typography sx={{ fontSize: 15, fontWeight: 600, color: 'text.secondary' }}>
            We could not find that tale on this device - please head back and try
            starting again.
          </Typography>
        </Stack>
        <BottomActionBar>
          <Button variant="contained" fullWidth onClick={onLeave}>
            Back to home
          </Button>
        </BottomActionBar>
      </Box>
    );
  }

  if (phase === 'done') {
    return <InterimComplete onLeave={onLeave} />;
  }

  const blanks = getBlanks(template);
  const currentBlank = blanks[blankIndex];

  const advance = () => {
    if (isCollectionComplete(template, collectionRef.current)) {
      setPhase('done');
      return;
    }
    setBlankIndex((index) => index + 1);
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
    // Record an empty placeholder to preserve positional alignment (same rule as
    // Solo - never leave a skipped blank absent from the collection).
    collectionRef.current.set(currentBlank.id, { playerSessionId: INTERIM_PLAYER_ID, word: '' });
    advance();
  };

  if (currentBlank) {
    return (
      <FillBlank
        key={currentBlank.id}
        subject={template.title}
        blank={currentBlank}
        wordNumber={blankIndex + 1}
        totalWords={blanks.length}
        onSubmitWord={handleSubmitWord}
        onSkip={handleSkip}
      />
    );
  }

  // A fully-collected round that fell through the guard above.
  return <InterimComplete onLeave={onLeave} />;
}
