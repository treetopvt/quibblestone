// ----------------------------------------------------------------------------
//  LengthContentSelector - the SERVER-SIDE story-length content stage
//  (story-selection/01).
//
//  This is the server analog of the web's pure length stage
//  (web/src/content/length.ts): given a pool of catalog entries and a length
//  preference, it returns the subset whose story LENGTH fits. A template's
//  length class (quick | full) is DERIVED from its blank count, never authored -
//  it comes purely from TemplateCatalogEntry.BlankCount against the single
//  threshold constant below (story-selection/01 AC-01). There is no length tag
//  on a catalog entry and no change to the catalog shape.
//
//  Where it sits in the ONE selection pipeline (GameHub.StartRound, AC-03):
//    family-safe gate (FamilySafeContentSelector, ALWAYS FIRST)
//      -> length filter (this selector)
//      -> empty-pool fallback (this selector)
//      -> random pick.
//  This selector only ever sees the subset the family-safe gate already allowed,
//  so relaxing length can never widen the safety set. It has NO knowledge of the
//  family-safe flag itself and never reorders around that gate.
//
//  Empty-pool fallback (AC-06): if a length preference selects NOTHING from the
//  family-safe pool (e.g. "quick" when only full stories survived the safety
//  gate), selection DEGRADES to the family-safe pool rather than failing the
//  round - a longer story is a fine outcome, an errored round is not.
//  SelectByLengthOrFallback below expresses that, identically to the web mirror.
//
//  Pure + stateless by construction: data in, data out. No I/O, no mutable
//  state - so it is registered as a singleton in DI (Program.cs) and shared by
//  every transient GameHub instance, exactly like FamilySafeContentSelector.
//
//  ============================ KEEP IN SYNC BY HAND ==========================
//  This MIRRORS web/src/content/length.ts: the SAME threshold, the SAME
//  quick/full split over the blank count, and the SAME empty-pool fallback.
//  There is no shared source and no codegen - the web stage and this stage are
//  kept in behavioral lockstep BY HAND (same discipline as
//  FamilySafeContentSelector / the DTOs). Change the threshold or the
//  filter/fallback behavior in one and you MUST change the other, or solo and
//  group play will offer different pools for the same preference.
//  ============================================================================
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Content;

namespace QuibbleStone.Api.Safety;

/// <summary>
/// The server-side story-length selection rule (story-selection/01). Mirrors the
/// web's pure length stage (web/src/content/length.ts) over
/// <see cref="TemplateCatalogEntry.BlankCount"/> - keep the two in behavioral
/// sync by hand (see the file header). Pure + stateless; registered as a singleton.
/// </summary>
public sealed class LengthContentSelector
{
    /// <summary>
    /// The SINGLE length-class threshold (AC-01): an entry with this many blanks
    /// or FEWER is "quick"; more than this is "full". Mirrors the web's
    /// QUICK_MAX_BLANKS - keep the two equal by hand.
    /// </summary>
    public const int QuickMaxBlanks = 6;

    /// <summary>
    /// Length preference values, mirroring the web's LengthPreference union
    /// ("quick" | "full" | "any"). "any" means do not filter by length at all,
    /// and is the default until story-selection/02 wires a UI to pick one.
    /// </summary>
    public const string Any = "any";
    public const string Quick = "quick";
    public const string Full = "full";

    /// <summary>
    /// Filters <paramref name="pool"/> by the length preference (AC-02):
    ///
    /// - "quick" -> only entries with BlankCount &lt;= <see cref="QuickMaxBlanks"/>.
    /// - "full"  -> only entries with BlankCount &gt; <see cref="QuickMaxBlanks"/>.
    /// - "any" (and any unrecognized value) -> every entry, unfiltered.
    ///
    /// Never mutates the input; returns a fresh list the caller owns. Assumes
    /// <paramref name="pool"/> is ALREADY the family-safe-gated pool - it does not
    /// re-check safety, only length.
    /// </summary>
    /// <param name="pool">The family-safe-gated pool to filter by length.</param>
    /// <param name="lengthPref">"quick", "full", or "any" (unrecognized -> "any").</param>
    /// <returns>The length-matched subset (a new list; may be empty).</returns>
    public IReadOnlyList<TemplateCatalogEntry> SelectByLength(
        IReadOnlyList<TemplateCatalogEntry> pool,
        string lengthPref)
    {
        return lengthPref switch
        {
            Quick => pool.Where(entry => entry.BlankCount <= QuickMaxBlanks).ToArray(),
            Full => pool.Where(entry => entry.BlankCount > QuickMaxBlanks).ToArray(),
            _ => pool.ToArray(),
        };
    }

    /// <summary>
    /// Applies <see cref="SelectByLength"/> with the empty-pool fallback (AC-06):
    /// if the length preference selected NOTHING from
    /// <paramref name="familySafePool"/>, degrade to the family-safe pool (a fresh
    /// list) rather than return an empty pool that would fail the round. With
    /// "any", or whenever the length filter matches at least one entry, the
    /// filtered result is returned unchanged.
    ///
    /// <paramref name="familySafePool"/> MUST already be the output of the
    /// family-safe gate - the fallback degrades to a longer story, never to
    /// unsafe content. Never mutates the input.
    /// </summary>
    /// <param name="familySafePool">The family-safe-gated pool (never bypass the safety gate).</param>
    /// <param name="lengthPref">"quick", "full", or "any" (unrecognized -> "any").</param>
    /// <returns>The length-matched subset, or the family-safe pool when that subset is empty.</returns>
    public IReadOnlyList<TemplateCatalogEntry> SelectByLengthOrFallback(
        IReadOnlyList<TemplateCatalogEntry> familySafePool,
        string lengthPref)
    {
        var filtered = SelectByLength(familySafePool, lengthPref);
        return filtered.Count > 0 ? filtered : familySafePool.ToArray();
    }
}
