// ----------------------------------------------------------------------------
//  FreshnessContentSelector - the SERVER-SIDE freshness-ROTATION content stage
//  (story-selection/03).
//
//  This is the server analog of the web's pure freshness stage
//  (web/src/content/fresh.ts): given a pool of catalog entries (already
//  family-safe- and length-gated by the caller) and the ids of templates
//  already played in the CURRENT room, it narrows the pool to the ones NOT
//  yet played - so consecutive host round-starts in the same room never
//  repeat a template until every template in the eligible pool has been
//  played once (AC-02).
//
//  Where it sits in the ONE selection pipeline (GameHub.StartRound, AC-05):
//    family-safe gate (FamilySafeContentSelector, ALWAYS FIRST)
//      -> length filter (LengthContentSelector, SECOND)
//      -> freshness filter + recycle (this selector, THIRD and LAST)
//      -> random pick.
//  This selector only ever sees the subset the earlier two stages already
//  allowed, and it only ever REMOVES entries from that subset - it never
//  re-widens the pool, so it can never surface an unsafe or wrong-length
//  template.
//
//  Recycling (AC-03): once every template in the pool has been played (the
//  pool "runs dry" for this room), SelectFreshOrRecycle reopens the WHOLE pool
//  rather than returning nothing, ordered least-recently-played first
//  (entries never seen in playedIds sort first, then oldest-played to newest-
//  played). The random pick that follows is uniform over whatever list is
//  returned, so this ordering is a "prefer," not a functional requirement -
//  the load-bearing guarantee is that recycling never narrows below the full
//  pool and never returns empty for a non-empty pool, and never throws.
//
//  AC-04 bypass seam: there is no pinned-template replay path in the code yet
//  - GameHub.StartRound's replay counterpart is just StartRound again (a fresh
//  RANDOM pick), so every current call site routes through this selector and
//  records history via Room.MarkTemplatePlayed. A FUTURE pinned-template
//  replay (replay-remix/01, e.g. "carve it again") would need to SKIP both
//  this selector and MarkTemplatePlayed entirely for that one round - see the
//  comment in GameHub.StartRound marking exactly where that branch would go.
//
//  Pure + stateless by construction: data in, data out. No I/O, no mutable
//  state of its OWN (the per-room played history lives on Room.
//  PlayedTemplateIds, owned and locked by the Room itself) - so this selector
//  is registered as a singleton in DI (Program.cs) and shared by every
//  transient GameHub instance, exactly like FamilySafeContentSelector and
//  LengthContentSelector.
//
//  ============================ KEEP IN SYNC BY HAND ==========================
//  This MIRRORS web/src/content/fresh.ts: the SAME "exclude played ids" filter
//  and the SAME least-recently-played-first / full-pool-reopen recycle
//  behavior. There is no shared source and no codegen - the web stage and this
//  stage are kept in behavioral lockstep BY HAND. Change the filter or the
//  recycle behavior in one and you MUST change the other, or solo and group
//  play will recycle differently for the same played-history shape.
//  ============================================================================
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Content;

namespace QuibbleStone.Api.Safety;

/// <summary>
/// The server-side freshness selection rule (story-selection/03). Mirrors the
/// web's pure freshness stage (web/src/content/fresh.ts) over
/// <see cref="TemplateCatalogEntry.Id"/> - keep the two in behavioral sync by
/// hand (see the file header). Pure + stateless; registered as a singleton.
/// </summary>
public sealed class FreshnessContentSelector
{
    /// <summary>
    /// Filters <paramref name="pool"/> to entries whose id is NOT in
    /// <paramref name="playedIds"/> (AC-01/AC-02). Never mutates either input;
    /// returns a fresh list the caller owns. Assumes <paramref name="pool"/> is
    /// ALREADY the family-safe- and length-gated pool - it does not re-check
    /// safety or length, only freshness.
    /// </summary>
    /// <param name="pool">The already safety+length-filtered pool to narrow by freshness.</param>
    /// <param name="playedIds">The ids already played (order irrelevant here; oldest-first by convention elsewhere).</param>
    /// <returns>The subset of <paramref name="pool"/> not yet played (may be empty).</returns>
    public IReadOnlyList<TemplateCatalogEntry> SelectFresh(
        IReadOnlyList<TemplateCatalogEntry> pool,
        IReadOnlyList<string> playedIds)
    {
        if (playedIds.Count == 0)
        {
            return pool.ToArray();
        }

        var played = playedIds.ToHashSet();
        return pool.Where(entry => !played.Contains(entry.Id)).ToArray();
    }

    /// <summary>
    /// Applies <see cref="SelectFresh"/> with the recycle-on-exhaustion behavior
    /// (AC-03): if every entry in <paramref name="pool"/> has already been
    /// played, RECYCLES by reopening the pool least-recently-played first and
    /// EXCLUDING the single most-recently-played story when the pool holds >=2
    /// (see <see cref="RecycleExcludingMostRecent"/>), so the tale just served
    /// can never be picked again immediately at the wrap (story-selection/03
    /// W-001) - rather than returning nothing.
    ///
    /// <paramref name="pool"/> MUST already be the output of the family-safe +
    /// length stages (AC-05) - recycling only ever reopens templates within that
    /// already-vetted pool, never re-widening to unsafe or wrong-length content.
    /// Returns an empty list only when <paramref name="pool"/> itself is empty;
    /// otherwise NEVER empty, NEVER throws. Never mutates either input.
    /// </summary>
    /// <param name="pool">The already safety+length-filtered pool.</param>
    /// <param name="playedIds">The ids already played in this room, oldest-first by convention.</param>
    /// <returns>The fresh subset, or (on exhaustion) the pool reopened minus the most-recently-played story.</returns>
    public IReadOnlyList<TemplateCatalogEntry> SelectFreshOrRecycle(
        IReadOnlyList<TemplateCatalogEntry> pool,
        IReadOnlyList<string> playedIds)
    {
        if (pool.Count == 0)
        {
            return [];
        }

        var fresh = SelectFresh(pool, playedIds);
        return fresh.Count > 0 ? fresh : RecycleExcludingMostRecent(pool, playedIds);
    }

    /// <summary>
    /// Reopens <paramref name="pool"/> for recycling, ordered so an entry never
    /// seen in <paramref name="playedIds"/> sorts first (defensive - should not
    /// occur once the pool is confirmed exhausted), then oldest-played to
    /// most-recently-played last, THEN drops the most-recently-played entry (the
    /// last after the sort) when the pool holds >=2 so the just-served story
    /// cannot repeat immediately at the wrap (W-001). A 1-entry pool returns that
    /// lone entry (a repeat is unavoidable). Never empty for a non-empty pool,
    /// never throws, never mutates either input.
    /// </summary>
    private static IReadOnlyList<TemplateCatalogEntry> RecycleExcludingMostRecent(
        IReadOnlyList<TemplateCatalogEntry> pool,
        IReadOnlyList<string> playedIds)
    {
        // Earlier index in playedIds = played longer ago = more eligible right
        // now. An id absent from playedIds sorts as "most eligible" via -1.
        var playedOrder = new Dictionary<string, int>();
        for (var i = 0; i < playedIds.Count; i++)
        {
            playedOrder[playedIds[i]] = i;
        }

        var leastRecentFirst = pool
            .OrderBy(entry => playedOrder.TryGetValue(entry.Id, out var order) ? order : -1)
            .ToArray();

        // Drop the most-recently-played (last after the sort) when doing so still
        // leaves at least one choice - that is the just-served story we must not repeat.
        return leastRecentFirst.Length >= 2
            ? leastRecentFirst[..^1]
            : leastRecentFirst;
    }
}
