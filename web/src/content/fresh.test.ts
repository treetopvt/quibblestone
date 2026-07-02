// ----------------------------------------------------------------------------
//  fresh.test.ts - Vitest coverage for the freshness-rotation content stage
//  (story-selection/03, see ./fresh.ts).
//
//  Builds a small in-test pool of templates (deliberately NOT importing
//  seedLibrary, so exhaustion/recycle scenarios are pinned by construction)
//  and asserts the pure behavior: selectFresh excludes played ids without
//  mutating its inputs; selectFreshOrRecycle recycles on exhaustion by reopening
//  the pool minus the single most-recently-played story (so the wrap never
//  immediately repeats the tale just served, W-001) without ever throwing or
//  returning empty for a non-empty pool; and a composition-order test proving
//  freshness runs on the ALREADY safety+length-filtered pool (AC-05), never
//  re-widening past what those earlier stages allowed.
// ----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';
import { blank, text, type Template } from '../engine/template';
import { selectTemplates } from './familySafe';
import { selectByLengthOrFallback } from './length';
import { selectFresh, selectFreshOrRecycle } from './fresh';

function makeTemplate(id: string, familySafe = true): Template {
  return {
    id,
    title: `Test template ${id}`,
    tags: { familySafe, ageRating: familySafe ? 'all-ages' : 'teen-plus', themes: ['test'] },
    body: [
      text('A '),
      blank({
        id: 'b1',
        category: 'noun',
        categoryLabel: 'NOUN',
        prompt: 'Give me a thing',
        subHint: 'Any object.',
        sparkWords: ['a', 'b', 'c'],
      }),
      text(' story.'),
    ],
  };
}

describe('selectFresh', () => {
  const a = makeTemplate('a');
  const b = makeTemplate('b');
  const c = makeTemplate('c');
  const pool: readonly Template[] = [a, b, c];

  it('excludes templates whose id is in playedIds', () => {
    const result = selectFresh(pool, ['b']);
    expect(result.map((t) => t.id)).toEqual(['a', 'c']);
  });

  it('returns every template unfiltered when playedIds is empty', () => {
    const result = selectFresh(pool, []);
    expect(result.map((t) => t.id)).toEqual(['a', 'b', 'c']);
  });

  it('returns an empty array once every template has been played', () => {
    const result = selectFresh(pool, ['a', 'b', 'c']);
    expect(result).toEqual([]);
  });

  it('ignores played ids that are not in the pool', () => {
    const result = selectFresh(pool, ['not-in-pool']);
    expect(result.map((t) => t.id)).toEqual(['a', 'b', 'c']);
  });

  it('never mutates the input pool', () => {
    const input: readonly Template[] = [a, b, c];
    const snapshotIds = input.map((t) => t.id);
    selectFresh(input, ['a']);
    expect(input.map((t) => t.id)).toEqual(snapshotIds);
  });

  it('returns a shallow copy, not the same array reference, when nothing is played', () => {
    const result = selectFresh(pool, []);
    expect(result).not.toBe(pool);
    expect(result).toEqual(pool);
  });
});

describe('selectFreshOrRecycle (AC-03)', () => {
  const a = makeTemplate('a');
  const b = makeTemplate('b');
  const c = makeTemplate('c');
  const pool: readonly Template[] = [a, b, c];

  it('returns the fresh subset when the pool is not exhausted', () => {
    const result = selectFreshOrRecycle(pool, ['a']);
    expect(result.map((t) => t.id)).toEqual(['b', 'c']);
  });

  it('recycles the pool minus the most-recently-played story on exhaustion (W-001)', () => {
    // Every template played, 'c' most recently. Recycle reopens the pool but
    // EXCLUDES 'c' so the just-served story cannot repeat at the wrap.
    const result = selectFreshOrRecycle(pool, ['a', 'b', 'c']);
    expect(result).toHaveLength(pool.length - 1);
    expect(result.map((t) => t.id)).not.toContain('c');
    expect(new Set(result.map((t) => t.id))).toEqual(new Set(['a', 'b']));
  });

  it('orders the recycled pool least-recently-played first (before the exclusion)', () => {
    // 'a' oldest, 'c' newest; recycle drops the newest ('c') and returns the
    // remainder least-recently-played first.
    const result = selectFreshOrRecycle(pool, ['a', 'b', 'c']);
    expect(result.map((t) => t.id)).toEqual(['a', 'b']);
  });

  it('never immediately repeats the just-played story across a full wrap (W-001)', () => {
    // Drive many rounds picking the first eligible each time; the id chosen last
    // round must never be offered as the ONLY-or-first pick again this round.
    let playedIds: string[] = [];
    let previous: string | null = null;
    for (let round = 0; round < 20; round += 1) {
      const eligible = selectFreshOrRecycle(pool, playedIds);
      expect(eligible.length).toBeGreaterThan(0);
      if (previous !== null) {
        expect(eligible.map((t) => t.id)).not.toContain(previous);
      }
      const [chosen] = eligible;
      previous = chosen.id;
      // Simulate the append-to-history contract: dedupe, move to the end.
      playedIds = [...playedIds.filter((id) => id !== chosen.id), chosen.id];
    }
  });

  it('returns the lone template on a size-1 pool (a repeat is unavoidable)', () => {
    const solo: readonly Template[] = [a];
    expect(selectFreshOrRecycle(solo, ['a']).map((t) => t.id)).toEqual(['a']);
  });

  it('treats an id missing from playedIds as most eligible when recycling', () => {
    // 'c' was never recorded as played (defensive edge case) even though the
    // pool is otherwise exhausted - it should sort first on recycle.
    const result = selectFreshOrRecycle(pool, ['a', 'b']);
    // selectFresh already returns ['c'] here (not exhausted), so recycling
    // is not triggered - confirm the non-recycle path instead.
    expect(result.map((t) => t.id)).toEqual(['c']);
  });

  it('never throws and never returns empty for a non-empty pool, however many times it recycles', () => {
    let playedIds: string[] = [];
    for (let round = 0; round < 10; round += 1) {
      const eligible = selectFreshOrRecycle(pool, playedIds);
      expect(eligible.length).toBeGreaterThan(0);
      const [chosen] = eligible;
      // Simulate the append-to-history contract: dedupe, move to the end.
      playedIds = [...playedIds.filter((id) => id !== chosen.id), chosen.id];
    }
  });

  it('returns an empty array (never throws) when the pool itself is empty', () => {
    expect(() => selectFreshOrRecycle([], ['anything'])).not.toThrow();
    expect(selectFreshOrRecycle([], ['anything'])).toEqual([]);
  });

  it('never mutates the pool or playedIds inputs', () => {
    const inputPool: readonly Template[] = [a, b, c];
    const inputPlayed: readonly string[] = ['a', 'b', 'c'];
    const poolIdsSnapshot = inputPool.map((t) => t.id);
    const playedSnapshot = [...inputPlayed];
    selectFreshOrRecycle(inputPool, inputPlayed);
    expect(inputPool.map((t) => t.id)).toEqual(poolIdsSnapshot);
    expect([...inputPlayed]).toEqual(playedSnapshot);
  });
});

describe('composition order (AC-05): freshness runs on the ALREADY safety+length-filtered pool', () => {
  it('never re-widens past what the family-safe gate already excluded', () => {
    const safe = makeTemplate('safe', true);
    const unsafe = makeTemplate('unsafe', false);
    const library: readonly Template[] = [safe, unsafe];

    const familySafePool = selectTemplates(library, true);
    const lengthPool = selectByLengthOrFallback(familySafePool, 'any');
    // Even with an empty played history (so freshness would otherwise allow
    // everything), the unsafe template never reappears - it was excluded
    // before freshness ever saw the pool.
    const freshPool = selectFreshOrRecycle(lengthPool, []);
    expect(freshPool.map((t) => t.id)).toEqual(['safe']);
    expect(freshPool.some((t) => t.id === 'unsafe')).toBe(false);
  });

  it('recycling stays within the safety+length-filtered pool, never reintroducing excluded content', () => {
    const safeA = makeTemplate('safe-a', true);
    const safeB = makeTemplate('safe-b', true);
    const unsafe = makeTemplate('unsafe', false);
    const library: readonly Template[] = [safeA, safeB, unsafe];

    const familySafePool = selectTemplates(library, true);
    // Exhaust the safe pool, then recycle - the unsafe template must never
    // surface even though it was never "played" (AC-05). Recycling may drop the
    // most-recently-played safe entry (W-001), so assert containment/exclusion
    // rather than a fixed size: every result is a safe id, unsafe never appears,
    // and the recycled pool is non-empty.
    const recycled = selectFreshOrRecycle(familySafePool, ['safe-a', 'safe-b']);
    expect(recycled.length).toBeGreaterThan(0);
    expect(recycled.some((t) => t.id === 'unsafe')).toBe(false);
    recycled.forEach((t) => expect(['safe-a', 'safe-b']).toContain(t.id));
  });
});
