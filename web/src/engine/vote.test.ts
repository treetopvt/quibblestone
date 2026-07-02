// ----------------------------------------------------------------------------
//  vote.test.ts - Vitest spec for the pure vote-collection primitive
//  (reveal-delight/03, #58). This is the ONE required automated test for the
//  story: it pins the create/cast/move/tally/tie contract the Golden Guardian
//  award relies on AND the parked Versus/Duel mode will import unchanged. Every
//  hub/crown/UI acceptance criterion for story 03 is otherwise manual.
//
//  Covers: createVote (order preserved, de-duped, empty), castVote (records,
//  one-active-vote-per-voter MOVE, ignores an unknown option, purity - never
//  mutates its input), and tally (per-option counts including zeros, the single
//  winner, the documented "first option to reach the max" tie-break, and the
//  no-votes -> null winner case).
// ----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';
import { castVote, createVote, tally } from './vote';

describe('createVote', () => {
  it('preserves option order and starts with no voters', () => {
    const vote = createVote(['a', 'b', 'c']);
    expect(vote.optionIds).toEqual(['a', 'b', 'c']);
    expect(vote.byVoter).toEqual({});
    expect(vote.castOrder).toEqual([]);
  });

  it('de-duplicates option ids (first occurrence wins)', () => {
    const vote = createVote(['a', 'b', 'a', 'c', 'b']);
    expect(vote.optionIds).toEqual(['a', 'b', 'c']);
  });

  it('supports an empty option set (no winner to tally)', () => {
    const vote = createVote([]);
    expect(vote.optionIds).toEqual([]);
    expect(tally(vote).winnerId).toBeNull();
  });
});

describe('castVote', () => {
  it('records a voter choice', () => {
    const vote = castVote(createVote(['a', 'b']), 'alice', 'a');
    expect(vote.byVoter).toEqual({ alice: 'a' });
    expect(vote.castOrder).toEqual(['alice']);
  });

  it('MOVES a re-cast (one active vote per voter, not a second vote)', () => {
    let vote = createVote(['a', 'b', 'c']);
    vote = castVote(vote, 'alice', 'a');
    vote = castVote(vote, 'alice', 'c');
    // Still exactly one active vote for alice, now on 'c'.
    expect(vote.byVoter).toEqual({ alice: 'c' });
    // castOrder keeps alice's ORIGINAL position (still one voter).
    expect(vote.castOrder).toEqual(['alice']);
    const counts = tally(vote).counts;
    expect(counts).toEqual({ a: 0, b: 0, c: 1 });
  });

  it('ignores a vote for an option outside the offered set', () => {
    const base = createVote(['a', 'b']);
    const after = castVote(base, 'alice', 'zzz');
    // Unknown option -> unchanged vote (nothing recorded).
    expect(after).toBe(base);
    expect(after.byVoter).toEqual({});
  });

  it('never mutates its input (purity)', () => {
    const base = castVote(createVote(['a', 'b']), 'alice', 'a');
    const next = castVote(base, 'bob', 'b');
    // The original is untouched...
    expect(base.byVoter).toEqual({ alice: 'a' });
    expect(base.castOrder).toEqual(['alice']);
    // ...and the new vote carries both.
    expect(next.byVoter).toEqual({ alice: 'a', bob: 'b' });
    expect(next.castOrder).toEqual(['alice', 'bob']);
  });
});

describe('tally', () => {
  it('counts every option (zeros included) and names the clear winner', () => {
    let vote = createVote(['a', 'b', 'c']);
    vote = castVote(vote, 'alice', 'b');
    vote = castVote(vote, 'bob', 'b');
    vote = castVote(vote, 'cara', 'a');
    const result = tally(vote);
    expect(result.counts).toEqual({ a: 1, b: 2, c: 0 });
    expect(result.winnerId).toBe('b');
  });

  it('breaks a tie by first option to REACH the max count', () => {
    // 'a' reaches 1 (its max) on alice's cast BEFORE 'b' reaches 1 on bob's,
    // so the 1-1 tie resolves to 'a' - deterministic, cast-order driven.
    let vote = createVote(['a', 'b']);
    vote = castVote(vote, 'alice', 'a');
    vote = castVote(vote, 'bob', 'b');
    expect(tally(vote).winnerId).toBe('a');

    // Reverse the cast order and the winner flips to 'b' (first to reach 1 now).
    let reversed = createVote(['a', 'b']);
    reversed = castVote(reversed, 'bob', 'b');
    reversed = castVote(reversed, 'alice', 'a');
    expect(tally(reversed).winnerId).toBe('b');
  });

  it('is not fooled by option declaration order in a tie (cast order wins)', () => {
    // 'b' is declared AFTER 'a', but 'b' reaches the max (1) first by cast order.
    let vote = createVote(['a', 'b']);
    vote = castVote(vote, 'bob', 'b');
    vote = castVote(vote, 'alice', 'a');
    expect(tally(vote).winnerId).toBe('b');
  });

  it('returns a null winner when nobody has voted', () => {
    const vote = createVote(['a', 'b', 'c']);
    const result = tally(vote);
    expect(result.counts).toEqual({ a: 0, b: 0, c: 0 });
    expect(result.winnerId).toBeNull();
  });

  it('reflects a moved vote in the final tally', () => {
    let vote = createVote(['a', 'b']);
    vote = castVote(vote, 'alice', 'a');
    vote = castVote(vote, 'bob', 'a');
    // bob moves to 'b': now 1-1, and 'a' reached its max (1) first (alice) -> 'a'.
    vote = castVote(vote, 'bob', 'b');
    const result = tally(vote);
    expect(result.counts).toEqual({ a: 1, b: 1 });
    expect(result.winnerId).toBe('a');
  });
});
