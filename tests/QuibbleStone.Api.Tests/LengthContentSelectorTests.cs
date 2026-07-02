// ----------------------------------------------------------------------------
//  LengthContentSelectorTests - unit tests for the story-selection/01 server
//  length stage (api/src/Safety/LengthContentSelector.cs).
//
//  These exercise the pure selector directly over hand-built catalog entries
//  (blank counts around the QUICK_MAX_BLANKS boundary), mirroring the web's
//  length.test.ts. They lock in the two behaviors the pipeline depends on:
//
//    1. Length filtering per preference over BlankCount ("quick" keeps only
//       short stories, "full" only long ones, "any" keeps everything) - the
//       server analog of selectByLength, so a family-safe-off + quick round
//       picks only quick ids (AC-03).
//    2. The empty-pool fallback (AC-06): a length preference that would leave an
//       EMPTY pool degrades to the family-safe pool rather than returning
//       nothing, so a round never fails just because a length class is empty.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Content;
using QuibbleStone.Api.Safety;

namespace QuibbleStone.Api.Tests;

public class LengthContentSelectorTests
{
    private readonly LengthContentSelector _selector = new();

    // A family-safe pool (family-safe gate already ran) mixing quick and full
    // stories around the quick <= 6 / full >= 7 boundary.
    private static IReadOnlyList<TemplateCatalogEntry> MixedPool =>
    [
        new TemplateCatalogEntry("quick-4", FamilySafe: true, BlankCount: 4),
        new TemplateCatalogEntry("full-10", FamilySafe: true, BlankCount: 10),
        new TemplateCatalogEntry("quick-6", FamilySafe: true, BlankCount: 6), // AT threshold -> quick
        new TemplateCatalogEntry("full-7", FamilySafe: true, BlankCount: 7),  // just over -> full
    ];

    [Fact]
    public void SelectByLength_quick_returns_only_quick_ids()
    {
        var result = _selector.SelectByLength(MixedPool, LengthContentSelector.Quick);

        Assert.Equal(new[] { "quick-4", "quick-6" }, result.Select(e => e.Id));
        Assert.All(result, e => Assert.True(e.BlankCount <= LengthContentSelector.QuickMaxBlanks));
    }

    [Fact]
    public void SelectByLength_full_returns_only_full_ids()
    {
        var result = _selector.SelectByLength(MixedPool, LengthContentSelector.Full);

        Assert.Equal(new[] { "full-10", "full-7" }, result.Select(e => e.Id));
        Assert.All(result, e => Assert.True(e.BlankCount > LengthContentSelector.QuickMaxBlanks));
    }

    [Fact]
    public void SelectByLength_any_returns_the_pool_unfiltered()
    {
        var result = _selector.SelectByLength(MixedPool, LengthContentSelector.Any);

        Assert.Equal(new[] { "quick-4", "full-10", "quick-6", "full-7" }, result.Select(e => e.Id));
    }

    [Fact]
    public void SelectByLength_does_not_mutate_the_input()
    {
        var pool = MixedPool;
        var before = pool.Select(e => e.Id).ToArray();

        _ = _selector.SelectByLength(pool, LengthContentSelector.Quick);

        Assert.Equal(before, pool.Select(e => e.Id));
    }

    [Fact]
    public void SelectByLengthOrFallback_falls_back_to_family_safe_pool_when_length_pool_is_empty()
    {
        // A full-only family-safe pool with a "quick" preference matches nothing -
        // it must degrade to the family-safe pool rather than return empty (AC-06).
        IReadOnlyList<TemplateCatalogEntry> fullOnly =
        [
            new TemplateCatalogEntry("full-9", FamilySafe: true, BlankCount: 9),
            new TemplateCatalogEntry("full-10", FamilySafe: true, BlankCount: 10),
        ];

        var result = _selector.SelectByLengthOrFallback(fullOnly, LengthContentSelector.Quick);

        Assert.Equal(new[] { "full-9", "full-10" }, result.Select(e => e.Id));
    }

    [Fact]
    public void SelectByLengthOrFallback_returns_the_filtered_pool_when_it_is_non_empty()
    {
        var result = _selector.SelectByLengthOrFallback(MixedPool, LengthContentSelector.Quick);

        Assert.Equal(new[] { "quick-4", "quick-6" }, result.Select(e => e.Id));
    }
}
