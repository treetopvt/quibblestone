// ----------------------------------------------------------------------------
//  vote.ts - a tiny, PURE, reusable "tap to pick one winner among a small set of
//  options" primitive (reveal-delight/03, issue #58).
//
//  WHY THIS EXISTS AS ITS OWN MODULE (and why it is deliberately GENERAL):
//  two features in the backlog need the exact same shape - "give the room a
//  small set of options, let each member cast ONE vote, tally, surface a single
//  winner":
//    - Golden Guardian (this story): the options are the coral filled words on
//      the Reveal; the room taps the funniest one.
//    - The parked Versus/Duel mode (docs/features/game-modes/feature.md "Parked
//      - Phase 2+/3"): the options are two competing answers; the room taps the
//      funnier one.
//  Rather than let each invent its own tally logic (which would drift), this
//  story builds the primitive ONCE, with NO opinion on:
//    - what an option REPRESENTS (a coral word, a Versus answer, anything) - an
//      option is just an opaque string id, and
//    - how a result RENDERS (that is the caller's screen).
//  Golden Guardian imports it here; the parked Versus/Duel mode imports it
//  UNMODIFIED when it is eventually scheduled. Keep it general - resist adding a
//  Golden-Guardian-specific field here; that belongs in the caller.
//
//  PURITY: every function returns a NEW Vote and never mutates its input (the
//  Vote's arrays/maps are treated as immutable). This keeps it trivially
//  unit-testable under Vitest (vote.test.ts) with no React or hub harness, the
//  same way the rest of web/src/engine is pure.
//
//  ONE VOTE PER VOTER: a voter has at most one active vote. Re-casting MOVES the
//  vote to the new option (it never stacks a second vote) - see castVote.
//
//  TIE-BREAK (documented + deterministic, never dramatic - this is a toy, not a
//  ranked contest): the winner is the option with the most votes; ties are
//  broken by "first option to REACH that max count wins" (see tally). Because
//  cast order is preserved (votes are appended in the order they arrive), this
//  is fully deterministic for a given sequence of casts - no randomness, no
//  Math.max ambiguity. If NO votes have been cast, there is no winner (null).
//
//  Child safety / no PII (reveal-delight/03 AC-07): a vote is (voterId, optionId)
//  - both opaque, already-vetted ids the caller supplies (here: an in-session
//  nickname and an already-safety-filtered coral word's blank id). This module
//  introduces no free text and stores no identity beyond the ids it is handed.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

/**
 * A single, in-progress vote over a fixed set of options. Treat every field as
 * IMMUTABLE - the functions below return a new Vote rather than mutating one.
 */
export interface Vote {
  /**
   * The allowed option ids, in the order they were offered (createVote's input
   * order). A cast for an id NOT in this set is ignored (castVote returns the
   * vote unchanged), so the option set is authoritative.
   */
  readonly optionIds: readonly string[];
  /**
   * Each voter's current single choice, keyed by voterId. A voter absent from
   * this map has not voted; a present voter has exactly one active option id
   * (re-casting overwrites it - one active vote per voter).
   */
  readonly byVoter: Readonly<Record<string, string>>;
  /**
   * The order in which voterIds FIRST cast a vote, oldest first. Used only to
   * make the tie-break deterministic ("first option to reach the max"): replayed
   * in this order, the running counts reach the max in a fixed sequence. A voter
   * that moves its vote keeps its original position (it is still the same single
   * voter), so a move never reorders the tie-break.
   */
  readonly castOrder: readonly string[];
}

/** The result of tallying a vote: per-option counts plus the single winner (or null). */
export interface VoteTally {
  /**
   * Votes per option id, for EVERY option in optionIds (an option with no votes
   * is present with 0). The caller decides whether to surface these; Golden
   * Guardian deliberately does NOT show per-option counts mid-vote (AC-02).
   */
  readonly counts: Readonly<Record<string, number>>;
  /**
   * The single winning option id: the option with the most votes, ties broken by
   * "first option to reach that max count" (see the module header). Null when no
   * votes have been cast at all - never a ranked list, never a runner-up.
   */
  readonly winnerId: string | null;
}

/**
 * Create an empty vote over the given option ids (order preserved). Duplicate
 * ids are de-duplicated (first occurrence wins) so the option set is clean.
 */
export function createVote(optionIds: readonly string[]): Vote {
  const seen = new Set<string>();
  const uniqueOptions: string[] = [];
  for (const id of optionIds) {
    if (!seen.has(id)) {
      seen.add(id);
      uniqueOptions.push(id);
    }
  }
  return { optionIds: uniqueOptions, byVoter: {}, castOrder: [] };
}

/**
 * Cast (or MOVE) one voter's vote to optionId, returning a NEW Vote (the input
 * is never mutated). Rules:
 *   - optionId must be one of the vote's optionIds; otherwise the vote is
 *     returned UNCHANGED (an unknown option is ignored, not recorded).
 *   - one active vote per voter: if the voter already voted, this OVERWRITES
 *     their choice (moves the vote) rather than adding a second - their position
 *     in castOrder is preserved.
 *   - a brand-new voter is appended to castOrder (for the deterministic
 *     tie-break).
 */
export function castVote(vote: Vote, voterId: string, optionId: string): Vote {
  // An option outside the offered set is not a valid target - ignore it so a
  // stray/crafted id can never enter the tally (mirrors the server's own guard).
  if (!vote.optionIds.includes(optionId)) {
    return vote;
  }

  const isNewVoter = !(voterId in vote.byVoter);

  return {
    optionIds: vote.optionIds,
    byVoter: { ...vote.byVoter, [voterId]: optionId },
    // A returning voter keeps its original cast position (still one voter); only
    // a first-time voter extends the order used for the tie-break.
    castOrder: isNewVoter ? [...vote.castOrder, voterId] : vote.castOrder,
  };
}

/**
 * Tally the vote: counts per option (0 for options with no votes) and the single
 * winner. The winner is the option with the most votes; ties are broken by "first
 * option to REACH that max count" - deterministic given the cast order (see the
 * module header). Returns winnerId null when no votes have been cast.
 */
export function tally(vote: Vote): VoteTally {
  // Seed every offered option at 0 so the counts map is complete and stable.
  const counts: Record<string, number> = {};
  for (const id of vote.optionIds) {
    counts[id] = 0;
  }
  for (const voterId of Object.keys(vote.byVoter)) {
    const chosen = vote.byVoter[voterId];
    // chosen is always a known option (castVote guards it), but guard defensively.
    if (chosen in counts) {
      counts[chosen] += 1;
    }
  }

  // Determine the winner by REPLAYING the counts in cast order, tracking which
  // option first reaches the eventual maximum. This makes the tie-break "first
  // option to reach the max" fully deterministic (no Math.max option-order
  // ambiguity): we find the max, then walk the casts and take the first option
  // whose running count hits it.
  let maxCount = 0;
  for (const id of vote.optionIds) {
    if (counts[id] > maxCount) {
      maxCount = counts[id];
    }
  }
  if (maxCount === 0) {
    // No votes at all - no winner (never a fabricated one).
    return { counts, winnerId: null };
  }

  const running: Record<string, number> = {};
  for (const id of vote.optionIds) {
    running[id] = 0;
  }
  for (const voterId of vote.castOrder) {
    const chosen = vote.byVoter[voterId];
    if (chosen in running) {
      running[chosen] += 1;
      if (running[chosen] === maxCount) {
        // First option to REACH the max wins the tie (documented rule).
        return { counts, winnerId: chosen };
      }
    }
  }

  // Unreachable given maxCount > 0 (some option must reach it during the replay),
  // but return a stable fallback rather than null so the type stays honest.
  return { counts, winnerId: null };
}
