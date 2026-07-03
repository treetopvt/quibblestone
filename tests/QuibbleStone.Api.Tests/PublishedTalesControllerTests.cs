// ----------------------------------------------------------------------------
//  PublishedTalesControllerTests - controller-level tests for keepsake-gallery/04
//  (issue #66), the HIGHEST child-safety-stakes surface in the app.
//
//  These exercise the REAL PublishedTalesController against the REAL
//  ContentSafetyFilter and a hand-rolled in-memory fake store (no mocking
//  framework in the harness, matching TelemetryControllerTests). They lock in the
//  non-negotiable public-surface guarantees:
//
//    - AC-03 RE-VET: a publish whose coral word fails the filter is REJECTED (400)
//      and NOTHING is stored - a lying client cannot get unfiltered content onto a
//      public page. A clean tale publishes and returns an unguessable slug + a
//      /t/<slug> url.
//    - AC-03 noindex: the public page ALWAYS carries X-Robots-Tag: noindex, nofollow
//      and a <meta name="robots" ...>, found or not.
//    - AC-05 expiry: a missing / expired slug renders the friendly 404 page.
//    - AC-05 disabled: with the disabled store, publish returns 503 "not available"
//      and the public GET 404s (the local-dev / no-Azure posture).
//    - AC-07 revoke: revoking removes the tale so its link stops resolving; it is
//      idempotent (revoking an unknown slug still succeeds).
//    - AC-04 FREE: there is no entitlement dependency to inject at all - the
//      controller takes only the store, the safety filter, and configuration.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using QuibbleStone.Api.PublishedTales;
using QuibbleStone.Api.Safety;

namespace QuibbleStone.Api.Tests;

public class PublishedTalesControllerTests
{
    private static readonly IContentSafetyFilter Safety = new ContentSafetyFilter();

    private static IConfiguration Config(string? webAppBaseUrl = "https://play.example.test") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PublishedTales:WebAppBaseUrl"] = webAppBaseUrl,
            })
            .Build();

    private static PublishedTalesController NewController(IPublishedTaleStore store)
    {
        var controller = new PublishedTalesController(store, Safety, Config())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };
        // A request scheme/host so the returned publish url can be composed.
        controller.HttpContext.Request.Scheme = "https";
        controller.HttpContext.Request.Host = new HostString("tales.example.test");
        return controller;
    }

    private static PublishTaleRequest CleanTale(string coralWord = "banana") => new(
        Title: "The space llama saga",
        Parts:
        [
            new PublishTalePartRequest(IsWord: false, Text: "Once upon a time a "),
            new PublishTalePartRequest(IsWord: true, Text: coralWord),
            new PublishTalePartRequest(IsWord: false, Text: " danced."),
        ],
        BylineNames: "Sam & Mia");

    // ---- AC-03: server-side re-vet -------------------------------------------

    [Fact]
    public async Task Publish_rejects_a_coral_word_that_fails_the_safety_filter()
    {
        var store = new FakePublishedTaleStore();
        var controller = NewController(store);

        var result = await controller.Publish(CleanTale(coralWord: "shit"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        // NOTHING may be stored when the re-vet fails.
        Assert.Empty(store.Tales);
    }

    [Fact]
    public async Task Publish_rejects_an_unsafe_LITERAL_part_a_lying_client_tags_as_not_a_word()
    {
        // Security review CR-001: the server must NOT trust the client's IsWord
        // flag - a crafted request marking unfiltered text as a "literal" template
        // run (IsWord=false) must still be re-vetted and rejected, or unsafe content
        // reaches the public, child-visible page. This is the whole point of AC-03's
        // server-side re-vet (the client is not trusted).
        var store = new FakePublishedTaleStore();
        var controller = NewController(store);

        var request = new PublishTaleRequest(
            Title: "x",
            Parts: [new PublishTalePartRequest(IsWord: false, Text: "shit")],
            BylineNames: string.Empty);

        var result = await controller.Publish(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        // NOTHING may be stored when a literal part fails the re-vet.
        Assert.Empty(store.Tales);
    }

    [Fact]
    public async Task Publish_rejects_a_byline_that_fails_the_safety_filter()
    {
        var store = new FakePublishedTaleStore();
        var controller = NewController(store);

        var request = CleanTale() with { BylineNames = "fuck" };
        var result = await controller.Publish(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(store.Tales);
    }

    [Fact]
    public async Task Publish_stores_a_clean_tale_and_returns_an_unguessable_slug_and_url()
    {
        var store = new FakePublishedTaleStore();
        var controller = NewController(store);

        var result = await controller.Publish(CleanTale(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var stored = Assert.Single(store.Tales);

        // The slug is unguessable: full length, unambiguous alphabet.
        Assert.Equal(SlugGenerator.SlugLength, stored.Slug.Length);
        foreach (var ch in stored.Slug)
        {
            Assert.Contains(ch, SlugGenerator.Alphabet);
        }

        // A TTL is stamped in the future (AC-05).
        Assert.True(stored.ExpiresUtc > stored.CreatedUtc);
        Assert.Equal(PublishedTalesController.TaleTtl, stored.ExpiresUtc - stored.CreatedUtc);

        // The response carries the slug and the /t/<slug> url on THIS app's base.
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.Contains(stored.Slug, json);
        Assert.Contains($"https://tales.example.test/t/{stored.Slug}", json);
    }

    [Fact]
    public async Task Publish_rejects_an_empty_title()
    {
        var store = new FakePublishedTaleStore();
        var controller = NewController(store);

        var request = CleanTale() with { Title = "   " };
        var result = await controller.Publish(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(store.Tales);
    }

    [Fact]
    public async Task Publish_drops_empty_coral_words_but_keeps_the_story()
    {
        var store = new FakePublishedTaleStore();
        var controller = NewController(store);

        var request = new PublishTaleRequest(
            Title: "Gappy tale",
            Parts:
            [
                new PublishTalePartRequest(false, "Start "),
                new PublishTalePartRequest(true, ""),      // an unfilled blank - dropped
                new PublishTalePartRequest(false, " end"),
            ],
            BylineNames: "");

        var result = await controller.Publish(request, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        var stored = Assert.Single(store.Tales);
        // The empty coral word is dropped; only the two literal text parts remain.
        Assert.Equal(2, stored.Parts.Count);
        Assert.All(stored.Parts, p => Assert.False(p.IsWord));
    }

    // ---- AC-03: noindex + public page ----------------------------------------

    [Fact]
    public async Task Public_page_sends_noindex_header_and_meta_for_a_found_tale()
    {
        var store = new FakePublishedTaleStore();
        var controller = NewController(store);
        await controller.Publish(CleanTale(), CancellationToken.None);
        var slug = store.Tales.Single().Slug;

        var result = await controller.Page(slug, CancellationToken.None);

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal(StatusCodes.Status200OK, content.StatusCode);
        Assert.Equal("noindex, nofollow", controller.Response.Headers["X-Robots-Tag"]);
        Assert.Contains("name=\"robots\"", content.Content);
        Assert.Contains("noindex", content.Content);
        // The coral word renders inside a coral span; the byline renders "carved by".
        Assert.Contains("banana", content.Content);
        Assert.Contains("carved by", content.Content);
        // The gold CTA points at the configured web app base (never hardcoded).
        Assert.Contains("Play QuibbleStone", content.Content);
    }

    [Fact]
    public async Task Public_page_404s_and_still_sends_noindex_for_a_missing_slug()
    {
        var store = new FakePublishedTaleStore();
        var controller = NewController(store);

        var result = await controller.Page("NOPEXXXXXXXX", CancellationToken.None);

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, content.StatusCode);
        Assert.Equal("noindex, nofollow", controller.Response.Headers["X-Robots-Tag"]);
        Assert.Contains("noindex", content.Content);
    }

    [Fact]
    public async Task Public_page_html_encodes_stored_content()
    {
        // Defense in depth: even already-filtered content is HTML-encoded so no
        // markup can be injected onto the public page.
        var store = new FakePublishedTaleStore();
        var controller = NewController(store);
        var request = CleanTale() with { Title = "<script>x</script>" };
        await controller.Publish(request, CancellationToken.None);
        var slug = store.Tales.Single().Slug;

        var result = await controller.Page(slug, CancellationToken.None);

        var content = Assert.IsType<ContentResult>(result);
        Assert.DoesNotContain("<script>x</script>", content.Content);
        Assert.Contains("&lt;script&gt;", content.Content);
    }

    // ---- AC-05: expiry-on-read -----------------------------------------------

    [Fact]
    public async Task Public_page_404s_an_expired_tale()
    {
        var store = new FakePublishedTaleStore();
        var controller = NewController(store);
        // Seed a tale that expired a second ago - the store applies expiry-on-read.
        var now = DateTimeOffset.UtcNow;
        store.Seed(new PublishedTale(
            Slug: "EXPIREDSLUG9",
            Title: "Old tale",
            Parts: [new TalePart(false, "gone")],
            BylineNames: "",
            CreatedUtc: now - PublishedTalesController.TaleTtl,
            ExpiresUtc: now - TimeSpan.FromSeconds(1)));

        var result = await controller.Page("EXPIREDSLUG9", CancellationToken.None) as ContentResult;

        Assert.NotNull(result);
        Assert.Equal(StatusCodes.Status404NotFound, result!.StatusCode);
    }

    // ---- AC-05: disabled fallback (no storage) -------------------------------

    [Fact]
    public async Task Publish_returns_503_when_the_store_is_disabled()
    {
        var controller = new PublishedTalesController(new DisabledPublishedTaleStore(), Safety, Config())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var result = await controller.Publish(CleanTale(), CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, obj.StatusCode);
    }

    [Fact]
    public async Task Public_page_404s_when_the_store_is_disabled()
    {
        var controller = new PublishedTalesController(new DisabledPublishedTaleStore(), Safety, Config())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var result = await controller.Page("ANYSLUGXXXXX", CancellationToken.None) as ContentResult;

        Assert.NotNull(result);
        Assert.Equal(StatusCodes.Status404NotFound, result!.StatusCode);
    }

    // ---- AC-07: revoke --------------------------------------------------------

    [Fact]
    public async Task Revoke_removes_the_tale_so_its_link_stops_resolving()
    {
        var store = new FakePublishedTaleStore();
        var controller = NewController(store);
        await controller.Publish(CleanTale(), CancellationToken.None);
        var slug = store.Tales.Single().Slug;

        var revokeResult = await controller.Revoke(slug, CancellationToken.None);
        Assert.IsType<NoContentResult>(revokeResult);

        var page = await controller.Page(slug, CancellationToken.None) as ContentResult;
        Assert.Equal(StatusCodes.Status404NotFound, page!.StatusCode);
    }

    [Fact]
    public async Task Revoke_is_idempotent_for_an_unknown_slug()
    {
        var store = new FakePublishedTaleStore();
        var controller = NewController(store);

        var result = await controller.Revoke("NEVEREXISTED", CancellationToken.None);
        Assert.IsType<NoContentResult>(result);
    }
}

/// <summary>
/// Hand-rolled in-memory published-tale store for controller tests (no mocking
/// framework in the harness). Mirrors the real store's semantics that the
/// controller depends on: IsEnabled true, point read by slug, lazy expiry-on-read
/// (an expired tale reads as null), and idempotent revoke.
/// </summary>
internal sealed class FakePublishedTaleStore : IPublishedTaleStore
{
    private readonly Dictionary<string, PublishedTale> _byslug = new();

    public IReadOnlyList<PublishedTale> Tales => _byslug.Values.ToList();

    public bool IsEnabled => true;

    public void Seed(PublishedTale tale) => _byslug[tale.Slug] = tale;

    public Task PublishAsync(PublishedTale tale, CancellationToken cancellationToken = default)
    {
        _byslug[tale.Slug] = tale;
        return Task.CompletedTask;
    }

    public Task<PublishedTale?> GetAsync(string slug, CancellationToken cancellationToken = default)
    {
        if (!_byslug.TryGetValue(slug, out var tale))
        {
            return Task.FromResult<PublishedTale?>(null);
        }
        if (tale.IsExpired(DateTimeOffset.UtcNow))
        {
            _byslug.Remove(slug);
            return Task.FromResult<PublishedTale?>(null);
        }
        return Task.FromResult<PublishedTale?>(tale);
    }

    public Task RevokeAsync(string slug, CancellationToken cancellationToken = default)
    {
        _byslug.Remove(slug);
        return Task.CompletedTask;
    }
}
