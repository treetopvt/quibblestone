// ----------------------------------------------------------------------------
//  distribute.test.ts - Vitest spec for the pure round-robin blank distribution
//  (group-play/02, #31). This is the PRIME unit-test target for the feature: it
//  pins the exact dealing rule (blank k -> player k % N) and the invariants the
//  server's C# mirror must also uphold (see distribute.ts header).
//
//  Covers: the AC-01 worked example (8 blanks / 5 players -> 2/2/2/1/1), the
//  Slice-1 2-player target (AC-03), M > N and M < N (AC-04), full coverage +
//  uniqueness of every blank index, the counts-differ-by-at-most-one invariant,
//  everyone-contributes when M >= N, round-robin (not chunked) ordering, and the
//  guarded degenerate inputs.
// ----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';
import { distributeBlanks } from './distribute';

/** Flattens the per-player assignment back into one sorted list of every dealt index. */
function allAssigned(assignment: number[][]): number[] {
  return assignment.flat().sort((a, b) => a - b);
}

describe('distributeBlanks', () => {
  it('deals 8 blanks across 5 players as 2/2/2/1/1 round-robin (AC-01)', () => {
    const assignment = distributeBlanks(5, 8);

    // blank k -> player k % 5:
    //   p0: 0,5   p1: 1,6   p2: 2,7   p3: 3   p4: 4
    expect(assignment).toEqual([[0, 5], [1, 6], [2, 7], [3], [4]]);
    expect(assignment.map((blanks) => blanks.length)).toEqual([2, 2, 2, 1, 1]);
  });

  it('works for the Slice-1 target of 2 players and a typical 5-blank template (AC-03)', () => {
    const assignment = distributeBlanks(2, 5);

    //   p0: 0,2,4   p1: 1,3
    expect(assignment).toEqual([[0, 2, 4], [1, 3]]);
    expect(assignment.map((blanks) => blanks.length)).toEqual([3, 2]);
  });

  it('assigns every blank exactly once when there are MORE blanks than players (AC-04)', () => {
    const playerCount = 3;
    const blankCount = 10;
    const assignment = distributeBlanks(playerCount, blankCount);

    // Full coverage + uniqueness: 0..9 each appear exactly once, no gaps.
    expect(allAssigned(assignment)).toEqual([0, 1, 2, 3, 4, 5, 6, 7, 8, 9]);
    expect(assignment).toHaveLength(playerCount);
  });

  it('assigns every blank exactly once when there are FEWER blanks than players (AC-04)', () => {
    const playerCount = 5;
    const blankCount = 3;
    const assignment = distributeBlanks(playerCount, blankCount);

    // Every blank dealt once; the extra players simply get nothing this round.
    expect(allAssigned(assignment)).toEqual([0, 1, 2]);
    expect(assignment).toEqual([[0], [1], [2], [], []]);
    expect(assignment).toHaveLength(playerCount);
  });

  it('keeps per-player counts differing by at most one for many N x M shapes', () => {
    for (let playerCount = 1; playerCount <= 8; playerCount += 1) {
      for (let blankCount = 0; blankCount <= 20; blankCount += 1) {
        const assignment = distributeBlanks(playerCount, blankCount);
        const counts = assignment.map((blanks) => blanks.length);
        const min = Math.min(...counts);
        const max = Math.max(...counts);
        expect(max - min).toBeLessThanOrEqual(1);
      }
    }
  });

  it('covers every blank index exactly once (no gaps, no duplicates) for many shapes', () => {
    for (let playerCount = 1; playerCount <= 8; playerCount += 1) {
      for (let blankCount = 0; blankCount <= 20; blankCount += 1) {
        const assignment = distributeBlanks(playerCount, blankCount);
        const expected = Array.from({ length: blankCount }, (_, k) => k);
        expect(allAssigned(assignment)).toEqual(expected);
      }
    }
  });

  it('has everyone contribute at least one blank when M >= N', () => {
    for (let playerCount = 1; playerCount <= 8; playerCount += 1) {
      for (let blankCount = playerCount; blankCount <= 20; blankCount += 1) {
        const assignment = distributeBlanks(playerCount, blankCount);
        for (const blanks of assignment) {
          expect(blanks.length).toBeGreaterThanOrEqual(1);
        }
      }
    }
  });

  it('spreads each player round-robin (NOT chunked) - a player owns non-contiguous blanks', () => {
    // Chunked would give p0 = [0,1,2], p1 = [3,4,5]; round-robin interleaves.
    const assignment = distributeBlanks(2, 6);
    expect(assignment).toEqual([[0, 2, 4], [1, 3, 5]]);
  });

  it('returns each player list already sorted ascending', () => {
    const assignment = distributeBlanks(3, 11);
    for (const blanks of assignment) {
      const sorted = [...blanks].sort((a, b) => a - b);
      expect(blanks).toEqual(sorted);
    }
  });

  it('guards degenerate inputs: no players -> empty result', () => {
    expect(distributeBlanks(0, 5)).toEqual([]);
    expect(distributeBlanks(-3, 5)).toEqual([]);
  });

  it('guards degenerate inputs: no blanks -> one empty bucket per player', () => {
    expect(distributeBlanks(3, 0)).toEqual([[], [], []]);
    expect(distributeBlanks(3, -2)).toEqual([[], [], []]);
  });
});
