// ----------------------------------------------------------------------------
//  TaleFeedback - the quiet, per-player thumbs up / thumbs down curation vote
//  on a STORY TEMPLATE (story-selection/05, issue #95).
//
//  WHAT THIS IS: at the end of a tale - solo Reveal.tsx AND group
//  RoundComplete.tsx - this renders "Did you like this story?" with two thumb
//  tap targets. Tapping a thumb records a per-player, per-round vote on the
//  TEMPLATE (a curation signal for later "which tales land?" analysis, reusing
//  story-selection/04's ITelemetrySink via ../telemetry/feedbackLog.ts) and is
//  visually SUBORDINATE to the primary "Play another round" CTA it sits beside
//  (AC-01): a small muted row, never full-width, never gold.
//
//  WHAT THIS IS NOT (read this before touching it): it is NOT the
//  reveal-delight Reaction row. There is no live room tally, no SignalR, no
//  party emoji, no aggregate count shown to players (contrast reveal-delight/01,
//  which IS room state). This is a plain, per-device REST write - every player
//  votes independently and one player's tap never touches another's (AC-03).
//  Do not let this surface grow past "one quiet thumbs control".
//
//  VOTE IDENTITY (AC-02, AC-06): a fresh, opaque VoteId is minted ONCE per
//  mount (crypto.randomUUID()) - i.e. once per round's viewing of this screen -
//  and reused for every tap while the component stays mounted. The server
//  upserts on VoteId (its storage RowKey), so tapping up then down OVERWRITES
//  the same row rather than double-counting; only the FINAL tap before leaving
//  the screen counts. A genuinely different round remounts this component
//  (callers key their parent screen per round, e.g. GroupRound's
//  `key={round.roundNumber}`), which mints a fresh VoteId.
//
//  FAIL-SOFT (AC-05): the highlighted thumb comes from LOCAL React state set
//  synchronously on tap, INDEPENDENT of the network call's outcome.
//  recordFeedback() is fire-and-forget with a swallowed .catch(), so a
//  down/slow/unreachable sink never blocks or un-highlights the choice, and
//  never delays or gates leaving this screen.
//
//  NO NAG (AC-07): there is no default selection, no reminder, no badge for
//  skipping - the control starts with neither thumb highlighted and stays that
//  way until tapped. Silence writes nothing: recordFeedback is only ever
//  called from the tap handler, never on mount or on an interval.
//
//  NO PII / NO FREE TEXT (AC-04): the payload is templateId + vote ("up"/"down")
//  + mode + the opaque per-device session GUID (reused from
//  ../telemetry/serveLog.ts, never re-minted here) + this component's per-round
//  VoteId. Nothing typed, nothing personally identifying - there is no
//  free-text surface here for the safety filter to see.
//
//  Styling: theme tokens only (no hex/raw-px). FontAwesome thumbs icons
//  (registered in fontawesome.ts). Deliberately QUIET - a muted text-secondary
//  ring at rest, a small teal/coral fill only on the selected thumb - so it
//  never reads as the next-step CTA. Circles are 40px (a comfortable tap
//  target for a family/kid audience) while staying visibly smaller than the
//  62px full-width primary buttons this control always sits beside or below.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { useState } from 'react';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import { Box, Stack, Typography } from '@mui/material';
import { recordFeedback, type FeedbackVote } from '../telemetry/feedbackLog';

export interface TaleFeedbackProps {
  /** The story template this vote is about (the curation subject, not the round instance). */
  templateId: string;
  /** The play mode the tale was served under ("solo" or "classic-blind"). */
  mode: string;
}

/**
 * Pure vote-state transition (story-selection/05, AC-02): tapping a thumb
 * ALWAYS sets it as the new (and only) current vote - last tap wins, so
 * tapping up then down leaves the final displayed state 'down' (never a
 * toggle-off back to null, never a double-count of two simultaneous votes).
 * There is no skip-to-null transition here: AC-07 ("skipping writes nothing")
 * is guaranteed by the CALLER (the component below) only ever invoking this
 * from a tap handler, never on mount or on a timer - silence never calls this
 * at all. Exported so the transition is independently testable without a
 * render harness (see TaleFeedback.test.ts), matching this codebase's
 * "extract the pure logic, test that" convention (e.g. Solo.tsx's
 * pickRandomTemplate).
 */
export function applyVoteTap(_current: FeedbackVote | null, tapped: FeedbackVote): FeedbackVote {
  return tapped;
}

/**
 * The shared thumbs feedback control (AC-01 through AC-07). Mints its own
 * per-round VoteId on mount and keeps the current vote as local component
 * state - see the file header for the full contract.
 */
export function TaleFeedback({ templateId, mode }: TaleFeedbackProps) {
  // One opaque VoteId per mount (i.e. per round's viewing of this screen), so a
  // changed vote upserts the SAME row server-side (AC-02) rather than creating
  // a fresh, double-counted row per tap.
  const [voteId] = useState(() => crypto.randomUUID());
  const [vote, setVote] = useState<FeedbackVote | null>(null);

  const handleVote = (next: FeedbackVote) => {
    // Reflect the choice immediately and unconditionally (AC-05: fail-soft,
    // never gated on the network result).
    setVote((current) => applyVoteTap(current, next));
    recordFeedback({ templateId, vote: next, mode, voteId });
  };

  return (
    <Stack direction="row" alignItems="center" justifyContent="center" spacing={1.5} sx={{ py: 1 }}>
      <Typography
        sx={{
          fontFamily: '"Nunito", sans-serif',
          fontWeight: 700,
          fontSize: 12.5,
          color: 'text.secondary',
        }}
      >
        Did you like this story?
      </Typography>
      <Stack direction="row" spacing={1}>
        <ThumbButton direction="up" selected={vote === 'up'} onClick={() => handleVote('up')} />
        <ThumbButton direction="down" selected={vote === 'down'} onClick={() => handleVote('down')} />
      </Stack>
    </Stack>
  );
}

/** One thumb tap target: quiet at rest, a small teal/coral fill when selected (AC-02). */
function ThumbButton({
  direction,
  selected,
  onClick,
}: {
  direction: FeedbackVote;
  selected: boolean;
  onClick: () => void;
}) {
  const label = direction === 'up' ? 'Yes, I liked this story' : 'No, not for me';
  const selectedColor = direction === 'up' ? 'teal.main' : 'coral.main';

  return (
    <Box
      component="button"
      type="button"
      aria-label={label}
      aria-pressed={selected}
      onClick={onClick}
      sx={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        width: 40,
        height: 40,
        border: '1.5px solid',
        borderColor: selected ? selectedColor : 'divider',
        borderRadius: '50%',
        bgcolor: selected ? selectedColor : 'transparent',
        color: selected ? 'common.white' : 'text.secondary',
        cursor: 'pointer',
        transition: 'background-color 120ms ease, color 120ms ease, border-color 120ms ease',
      }}
    >
      <FontAwesomeIcon
        icon={direction === 'up' ? 'thumbs-up' : 'thumbs-down'}
        style={{ width: 15, height: 15 }}
      />
    </Box>
  );
}
