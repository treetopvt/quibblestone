// ----------------------------------------------------------------------------
//  fresh.ts - the freshness-ROTATION content stage (story-selection/03).
//
//  This module is the THIRD (and LAST) content-selection stage in
//  QuibbleStone's one selection pipeline, sitting immediately AFTER the
//  story-length stage (./length.ts) and immediately BEFORE the random pick:
//
//    family-safe gate (selectTemplates) -> length filter
//      (selectByLengthOrFallback) -> FRESHNESS (this file) -> random pick.
//
//  Given a pool of templates (already family-safe- and length-gated by the
//  caller) and the ids of templates already played, it narrows the pool to
//  the ones NOT yet played - so consecutive random picks never repeat a
//  template until every template in the eligible pool has been played once
//  (AC-01, AC-02). This stage never re-widens the pool: it only ever removes
//  entries the earlier stages already allowed, so it can never surface an
//  unsafe or wrong-length template (AC-05).
//
//  Recycling (AC-03): once every template in the pool has been played (the
//  pool "runs dry"), selectFreshOrRecycle reopens the pool rather than
//  returning nothing - an exhausted pool degrades to "eligible again," never
//  to an error or a stuck round. Because the pick that follows is UNIFORM
//  (pickRandomTemplate ignores array order), "least-recently-played first"
//  cannot be delivered by ordering alone, so recycling instead EXCLUDES the
//  single most-recently-played template when the pool holds >=2 stories: the
//  story just served is the one thing that cannot come back at the wrap, so a
//  player never hears the same tale twice in a row even across the boundary
//  (story-selection/03 W-001 decision). With a 1-template pool a repeat is
//  unavoidable, so the lone template is returned. The reopened list is still
//  ordered least-recently-played first (cosmetic under a uniform pick, but it
//  keeps the "stalest first" intent legible). Guarantee: recycling NEVER
//  returns empty for a non-empty pool and NEVER throws.
//
//  Explicit-replay bypass seam (AC-04): there is no pinned-template replay UI
//  in this story - both solo's "Play another round" and group's host
//  "Play another round" are FRESH RANDOM picks today, so both route through
//  this stage and both record history. A FUTURE pinned-template replay (e.g.
//  replay-remix/01's "carve it again") must BYPASS this stage entirely - it
//  should neither filter through selectFresh/selectFreshOrRecycle NOR append
//  the replayed id to played history, because replaying a favorite must not
//  make the random pick "forget" the other templates the player has not seen
//  yet. See web/src/pages/Solo.tsx's beginRound and
//  api/src/Hubs/GameHub.cs's StartRound for the call-site comments marking
//  exactly where that future bypass would branch around this stage.
//
//  Pure by construction: data in, data out. No React, no fetch, no SignalR,
//  no localStorage - this file has ZERO knowledge of WHERE playedIds comes
//  from (see ./playedHistory.ts for the device-local storage that feeds it
//  solo-side; the server's per-room equivalent is Room.PlayedTemplateIds).
//  Never mutates its inputs - safe to unit test in isolation (fresh.test.ts)
//  and safe to import from any layer.
//
//  ======================== KEEP THE SERVER MIRROR IN SYNC ====================
//  api/src/Safety/FreshnessContentSelector.cs is the C# MIRROR of this module:
//  the SAME "exclude played ids" filter and the SAME recycle behavior (reopen
//  least-recently-played first, excluding the most-recently-played story when
//  the pool has >=2). There is no shared source and no
//  codegen - the web stage and the C# stage are kept in behavioral lockstep BY
//  HAND. Change the filter or the recycle behavior here and you MUST change
//  FreshnessContentSelector.cs to match, or solo and group play will recycle
//  differently for the same played history shape.
//  ============================================================================
// ----------------------------------------------------------------------------

import type { Template } from '../engine/template';

/**
 * The freshness selection rule (AC-01/AC-02): given a pool of templates and
 * the ids already played, returns only the templates NOT in `playedIds`.
 *
 * Assumes `templates` is ALREADY the family-safe- and length-gated pool (the
 * earlier pipeline stages ran first) - this stage does not re-check safety or
 * length, only freshness. Never mutates `templates`; returns a fresh array
 * (which may be empty when every template in the pool has been played).
 */
export function selectFresh(
  templates: readonly Template[],
  playedIds: readonly string[],
): Template[] {
  if (playedIds.length === 0) {
    return [...templates];
  }
  const played = new Set(playedIds);
  return templates.filter((t) => !played.has(t.id));
}

/**
 * Reopens `pool` for recycling (AC-03) once selectFresh has come up empty.
 * Orders the pool least-recently-played first (a template never seen in
 * `playedIds` sorts first - defensive - then oldest-played to newest-played),
 * then EXCLUDES the single most-recently-played template (the last entry after
 * the sort) when the pool holds >=2 stories, so the story just served can never
 * be picked again immediately at the wrap (story-selection/03 W-001). A
 * 1-template pool returns that lone template (a repeat is unavoidable). Never
 * returns empty for a non-empty `pool` and never throws; never mutates inputs.
 */
function recycleExcludingMostRecent(
  pool: readonly Template[],
  playedIds: readonly string[],
): Template[] {
  // Earlier index in playedIds = played longer ago = more eligible right now.
  // An id absent from playedIds (defensive; should not happen once the pool
  // is confirmed exhausted) sorts as "most eligible" via -1.
  const playedOrder = new Map(playedIds.map((id, index) => [id, index]));
  const leastRecentFirst = [...pool].sort((a, b) => {
    const orderA = playedOrder.get(a.id) ?? -1;
    const orderB = playedOrder.get(b.id) ?? -1;
    return orderA - orderB;
  });
  // Drop the most-recently-played (last after the sort) when doing so still
  // leaves at least one choice - that is the just-served story we must not repeat.
  return leastRecentFirst.length >= 2 ? leastRecentFirst.slice(0, -1) : leastRecentFirst;
}

/**
 * The compose helper that adds the recycle-on-exhaustion behavior (AC-03):
 * applies selectFresh to `pool`, and if every template in `pool` has already
 * been played, RECYCLES by reopening the pool while excluding the single
 * most-recently-played story (see recycleExcludingMostRecent) so the wrap never
 * immediately repeats the tale just served - rather than returning nothing.
 *
 * `pool` MUST already be the output of the family-safe + length stages (AC-05)
 * - recycling reopens templates within that already-vetted pool, it never
 * re-widens to unsafe or wrong-length content. Returns `[]` only when `pool`
 * itself is empty (nothing upstream survived); otherwise NEVER empty, NEVER
 * throws. Never mutates either input.
 */
export function selectFreshOrRecycle(
  pool: readonly Template[],
  playedIds: readonly string[],
): Template[] {
  if (pool.length === 0) {
    return [];
  }
  const fresh = selectFresh(pool, playedIds);
  return fresh.length > 0 ? fresh : recycleExcludingMostRecent(pool, playedIds);
}
