// ----------------------------------------------------------------------------
//  FreshnessContentSelectorTests - unit tests for the story-selection/03
//  server freshness stage (api/src/Safety/FreshnessContentSelector.cs).
//
//  These exercise the pure selector directly over hand-built catalog entries,
//  mirroring the web's fresh.test.ts. They lock in the two behaviors the
//  pipeline depends on:
//
//    1. SelectFresh excludes played ids without mutating either input.
//    2. SelectFreshOrRecycle recycles once every entry has been played by
//       reopening the pool minus the single most-recently-played story (so the
//       wrap never immediately repeats the tale just served, W-001), never
//       returning empty for a non-empty pool and never throwing.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Content;
using QuibbleStone.Api.Safety;

namespace QuibbleStone.Api.Tests;

public class FreshnessContentSelectorTests
{
    private readonly FreshnessContentSelector _selector = new();

    private static IReadOnlyList<TemplateCatalogEntry> Pool =>
    [
        new TemplateCatalogEntry("a", FamilySafe: true, BlankCount: 5),
        new TemplateCatalogEntry("b", FamilySafe: true, BlankCount: 5),
        new TemplateCatalogEntry("c", FamilySafe: true, BlankCount: 5),
    ];

    [Fact]
    public void SelectFresh_excludes_played_ids()
    {
        var result = _selector.SelectFresh(Pool, ["b"]);

        Assert.Equal(new[] { "a", "c" }, result.Select(e => e.Id));
    }

    [Fact]
    public void SelectFresh_returns_everything_when_nothing_has_been_played()
    {
        var result = _selector.SelectFresh(Pool, []);

        Assert.Equal(new[] { "a", "b", "c" }, result.Select(e => e.Id));
    }

    [Fact]
    public void SelectFresh_returns_empty_once_everything_has_been_played()
    {
        var result = _selector.SelectFresh(Pool, ["a", "b", "c"]);

        Assert.Empty(result);
    }

    [Fact]
    public void SelectFresh_ignores_played_ids_not_in_the_pool()
    {
        var result = _selector.SelectFresh(Pool, ["not-in-pool"]);

        Assert.Equal(new[] { "a", "b", "c" }, result.Select(e => e.Id));
    }

    [Fact]
    public void SelectFresh_does_not_mutate_the_input()
    {
        var pool = Pool;
        var before = pool.Select(e => e.Id).ToArray();
        var playedIds = new List<string> { "a" };
        var playedBefore = playedIds.ToArray();

        _ = _selector.SelectFresh(pool, playedIds);

        Assert.Equal(before, pool.Select(e => e.Id));
        Assert.Equal(playedBefore, playedIds);
    }

    [Fact]
    public void SelectFreshOrRecycle_returns_the_fresh_subset_when_the_pool_is_not_exhausted()
    {
        var result = _selector.SelectFreshOrRecycle(Pool, ["a"]);

        Assert.Equal(new[] { "b", "c" }, result.Select(e => e.Id));
    }

    [Fact]
    public void SelectFreshOrRecycle_recycles_minus_the_most_recently_played_when_exhausted()
    {
        // Every entry played, 'c' most recently. Recycle reopens the pool but
        // EXCLUDES 'c' so the just-served story cannot repeat at the wrap (W-001).
        var result = _selector.SelectFreshOrRecycle(Pool, ["a", "b", "c"]);

        Assert.Equal(2, result.Count);
        Assert.DoesNotContain("c", result.Select(e => e.Id));
        Assert.Equal(new HashSet<string> { "a", "b" }, result.Select(e => e.Id).ToHashSet());
    }

    [Fact]
    public void SelectFreshOrRecycle_orders_the_recycled_pool_least_recently_played_first()
    {
        // 'a' oldest, 'c' newest; recycle drops the newest ('c') and returns the
        // remainder least-recently-played first.
        var result = _selector.SelectFreshOrRecycle(Pool, ["a", "b", "c"]);

        Assert.Equal(new[] { "a", "b" }, result.Select(e => e.Id));
    }

    [Fact]
    public void SelectFreshOrRecycle_never_immediately_repeats_the_just_played_story_across_a_wrap()
    {
        // Drive many rounds picking the first eligible each time; the id chosen
        // last round must never be offered again the very next round (W-001).
        var playedIds = new List<string>();
        string? previous = null;
        for (var round = 0; round < 20; round++)
        {
            var eligible = _selector.SelectFreshOrRecycle(Pool, playedIds);
            Assert.NotEmpty(eligible);
            if (previous is not null)
            {
                Assert.DoesNotContain(previous, eligible.Select(e => e.Id));
            }

            var chosen = eligible[0];
            previous = chosen.Id;
            playedIds.Remove(chosen.Id);
            playedIds.Add(chosen.Id);
        }
    }

    [Fact]
    public void SelectFreshOrRecycle_returns_the_lone_entry_on_a_size_one_pool()
    {
        IReadOnlyList<TemplateCatalogEntry> solo = [new TemplateCatalogEntry("a", FamilySafe: true, BlankCount: 5)];

        var result = _selector.SelectFreshOrRecycle(solo, ["a"]);

        Assert.Equal(new[] { "a" }, result.Select(e => e.Id));
    }

    [Fact]
    public void SelectFreshOrRecycle_returns_empty_only_when_the_pool_itself_is_empty()
    {
        var result = _selector.SelectFreshOrRecycle([], ["anything"]);

        Assert.Empty(result);
    }

    [Fact]
    public void SelectFreshOrRecycle_never_throws_or_returns_empty_across_many_recycles()
    {
        var playedIds = new List<string>();
        for (var round = 0; round < 10; round++)
        {
            var eligible = _selector.SelectFreshOrRecycle(Pool, playedIds);
            Assert.NotEmpty(eligible);

            var chosen = eligible[0];
            playedIds.Remove(chosen.Id);
            playedIds.Add(chosen.Id);
        }
    }

    [Fact]
    public void SelectFreshOrRecycle_never_mutates_its_inputs()
    {
        var pool = Pool;
        var playedIds = new List<string> { "a", "b", "c" };
        var poolBefore = pool.Select(e => e.Id).ToArray();
        var playedBefore = playedIds.ToArray();

        _ = _selector.SelectFreshOrRecycle(pool, playedIds);

        Assert.Equal(poolBefore, pool.Select(e => e.Id));
        Assert.Equal(playedBefore, playedIds);
    }
}
