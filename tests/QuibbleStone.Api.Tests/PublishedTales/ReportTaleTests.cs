// ----------------------------------------------------------------------------
//  ReportTaleTests - the report -> auto-hide -> serve-under-review path for public
//  keepsake tales (sysadmin-console/03, issue #137). Exercises the REAL
//  PublishedTalesController against the REAL ContentSafetyFilter and the in-memory
//  FakePublishedTaleStore the existing PublishedTalesControllerTests use (same
//  store-setup seam). They lock in the load-bearing guarantees:
//
//    - AC-01: a visitor reports a slug (no sign-in / account / PII) and a report is
//      recorded; the endpoint always returns the SAME neutral acknowledgement.
//    - AC-02: N reports (AutoHideThreshold) flip the tale to hidden; GET /t/{slug}
//      then serves the NEUTRAL "under review" page - NOT the tale, and DISTINCT from
//      the "drifted away" 404 (a revoked / expired tale). This distinction is the
//      whole point (a legitimate host must not read as a moderated bad actor).
//    - AC-04: the report path never re-runs the content-safety filter (a report is a
//      human signal, not a second automated check). Reporting works on content that
//      already passed publish - it is orthogonal to the filter.
//    - AC-07: a never-reported tale serves EXACTLY as before (200, the tale page).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using QuibbleStone.Api.PublishedTales;
using QuibbleStone.Api.Safety;

namespace QuibbleStone.Api.Tests;

public sealed class ReportTaleTests
{
    private static readonly IContentSafetyFilter Safety = new ContentSafetyFilter();

    private static IConfiguration Config() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PublishedTales:WebAppBaseUrl"] = "https://play.example.test",
            })
            .Build();

    private static PublishedTalesController NewController(IPublishedTaleStore store)
    {
        var controller = new PublishedTalesController(store, Safety, Config())
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };
        controller.HttpContext.Request.Scheme = "https";
        controller.HttpContext.Request.Host = new HostString("tales.example.test");
        return controller;
    }

    private static PublishTaleRequest CleanTale() => new(
        Title: "The space llama saga",
        Parts:
        [
            new PublishTalePartRequest(IsWord: false, Text: "Once upon a time a "),
            new PublishTalePartRequest(IsWord: true, Text: "banana"),
            new PublishTalePartRequest(IsWord: false, Text: " danced."),
        ],
        BylineNames: "Sam & Mia");

    private static async Task<string> PublishAndGetSlug(PublishedTalesController controller, FakePublishedTaleStore store)
    {
        await controller.Publish(CleanTale(), CancellationToken.None);
        return store.Tales.Single().Slug;
    }

    // ---- AC-01: a report is recorded, response is neutral --------------------

    [Fact]
    public async Task Report_records_a_report_against_the_slug_and_returns_a_neutral_thanks()
    {
        var store = new FakePublishedTaleStore();
        var controller = NewController(store);
        var slug = await PublishAndGetSlug(controller, store);

        var result = await controller.Report(slug, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        var state = await store.GetModerationAsync(slug, CancellationToken.None);
        Assert.Equal(1, state.ReportCount);
        Assert.False(state.IsHidden);
    }

    [Fact]
    public async Task Report_of_an_unknown_slug_still_returns_the_same_neutral_thanks()
    {
        // No oracle: an unknown slug gets the SAME acknowledgement as a real one, and
        // nothing is recorded (AC-01/AC-06 - the endpoint leaks no existence signal).
        var store = new FakePublishedTaleStore();
        var controller = NewController(store);

        var result = await controller.Report("NEVEREXISTED", CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        var state = await store.GetModerationAsync("NEVEREXISTED", CancellationToken.None);
        Assert.Equal(0, state.ReportCount);
        Assert.False(state.IsHidden);
    }

    // ---- AC-02: N reports auto-hide; serve the under-review page --------------

    [Fact]
    public async Task Reaching_the_threshold_auto_hides_the_tale()
    {
        var store = new FakePublishedTaleStore();
        var controller = NewController(store);
        var slug = await PublishAndGetSlug(controller, store);

        for (var i = 0; i < PublishedTalesController.AutoHideThreshold; i++)
        {
            await controller.Report(slug, CancellationToken.None);
        }

        var state = await store.GetModerationAsync(slug, CancellationToken.None);
        Assert.True(state.IsHidden);
        Assert.Equal(PublishedTalesController.AutoHideThreshold, state.ReportCount);
    }

    [Fact]
    public async Task Below_the_threshold_the_tale_still_serves_normally()
    {
        var store = new FakePublishedTaleStore();
        var controller = NewController(store);
        var slug = await PublishAndGetSlug(controller, store);

        // One below the threshold: not hidden yet.
        for (var i = 0; i < PublishedTalesController.AutoHideThreshold - 1; i++)
        {
            await controller.Report(slug, CancellationToken.None);
        }

        var page = await controller.Page(slug, CancellationToken.None) as ContentResult;
        Assert.NotNull(page);
        Assert.Equal(StatusCodes.Status200OK, page!.StatusCode);
        // Still the tale itself (the coral word renders), not under review.
        Assert.Contains("banana", page.Content);
        Assert.DoesNotContain("under review", page.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_hidden_tale_serves_the_neutral_under_review_page_not_the_tale()
    {
        var store = new FakePublishedTaleStore();
        var controller = NewController(store);
        var slug = await PublishAndGetSlug(controller, store);
        store.SeedModeration(slug, PublishedTalesController.AutoHideThreshold, isHidden: true);

        var page = await controller.Page(slug, CancellationToken.None) as ContentResult;

        Assert.NotNull(page);
        // The under-review page is a 200 (the tale exists, it is just paused) and does
        // NOT show the tale content.
        Assert.Equal(StatusCodes.Status200OK, page!.StatusCode);
        Assert.DoesNotContain("banana", page.Content);
        Assert.Contains("under review", page.Content, StringComparison.OrdinalIgnoreCase);
        // noindex still holds on the under-review page.
        Assert.Equal("noindex, nofollow", controller.Response.Headers["X-Robots-Tag"]);
    }

    [Fact]
    public async Task The_under_review_page_is_DISTINCT_from_the_404_drifted_away_page()
    {
        // THE load-bearing distinction (AC-02): a moderated (hidden) tale must read
        // differently from a revoked / expired tale (the 404), so a legitimate host
        // who revoked their own tale is never confused with a moderated bad actor.
        var store = new FakePublishedTaleStore();
        var controller = NewController(store);
        var slug = await PublishAndGetSlug(controller, store);
        store.SeedModeration(slug, PublishedTalesController.AutoHideThreshold, isHidden: true);

        var hiddenController = NewController(store);
        var underReview = await hiddenController.Page(slug, CancellationToken.None) as ContentResult;

        // A genuinely revoked tale -> the 404 "drifted away" page.
        await hiddenController.Revoke("SOMEOTHERSLUG", CancellationToken.None);
        var missingController = NewController(store);
        var notFound = await missingController.Page("SOMEOTHERSLUG", CancellationToken.None) as ContentResult;

        Assert.NotNull(underReview);
        Assert.NotNull(notFound);
        // Different HTTP status (200 under review vs 404 gone) AND different copy.
        Assert.Equal(StatusCodes.Status200OK, underReview!.StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, notFound!.StatusCode);
        Assert.NotEqual(underReview.Content, notFound.Content);
        Assert.Contains("under review", underReview.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("drifted away", notFound.Content, StringComparison.OrdinalIgnoreCase);
    }

    // ---- AC-04: the report path never re-runs the content-safety filter -------

    [Fact]
    public async Task Report_does_not_re_run_the_content_safety_filter()
    {
        // A report is a HUMAN signal, not a second automated content check. It records
        // against an ALREADY-PUBLISHED (already-filtered) tale and never re-vets - so
        // reporting a perfectly clean tale still records a report (the path is
        // orthogonal to the filter). A throwing filter would prove a re-vet ran.
        var store = new FakePublishedTaleStore();
        var controller = new PublishedTalesController(store, new ThrowingContentSafetyFilter(), Config())
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };
        controller.HttpContext.Request.Scheme = "https";
        controller.HttpContext.Request.Host = new HostString("tales.example.test");
        // Seed a tale directly (bypassing publish, which DOES use the filter).
        store.Seed(new PublishedTale(
            Slug: "SEEDEDSLUG12",
            Title: "Clean tale",
            Parts: [new TalePart(false, "hello")],
            BylineNames: "",
            CreatedUtc: DateTimeOffset.UtcNow,
            ExpiresUtc: DateTimeOffset.UtcNow + PublishedTalesController.TaleTtl));

        // If the report path touched the filter, this would throw.
        var result = await controller.Report("SEEDEDSLUG12", CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        var state = await store.GetModerationAsync("SEEDEDSLUG12", CancellationToken.None);
        Assert.Equal(1, state.ReportCount);
    }

    // ---- AC-07: a never-reported tale serves exactly as before ----------------

    [Fact]
    public async Task A_never_reported_tale_serves_exactly_as_before()
    {
        var store = new FakePublishedTaleStore();
        var controller = NewController(store);
        var slug = await PublishAndGetSlug(controller, store);

        var page = await controller.Page(slug, CancellationToken.None) as ContentResult;

        Assert.NotNull(page);
        Assert.Equal(StatusCodes.Status200OK, page!.StatusCode);
        Assert.Contains("banana", page.Content);
        Assert.Contains("carved by", page.Content);
        // The additive machinery: the tale page now carries a "report this tale"
        // control, but the tale itself renders unchanged.
        Assert.Contains("Report this tale", page.Content);
    }
}

/// <summary>
/// A content-safety filter that throws if it is ever consulted - used to prove the
/// report path NEVER re-runs the filter (AC-04). Any call is a test failure.
/// </summary>
internal sealed class ThrowingContentSafetyFilter : IContentSafetyFilter
{
    public ValueTask<ContentSafetyResult> CheckAsync(string? candidate, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("The report path must never run the content-safety filter (AC-04).");
}
