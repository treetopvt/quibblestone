// ----------------------------------------------------------------------------
//  ReportedTalesControllerTests - the OPERATOR review queue for reported public
//  tales (sysadmin-console/03, issue #137, AC-03). Two layers:
//
//    1. Direct controller tests against the in-memory FakePublishedTaleStore (the
//       same store-setup seam PublishedTalesControllerTests use): the queue lists a
//       hidden tale with its content + count, confirm keeps it gone, restore resumes
//       serving and resets the count, and confirm / restore on a non-hidden slug are
//       idempotent no-ops.
//    2. A WebApplicationFactory boundary + end-to-end walk that boots the REAL app
//       with a seeded store: an UNAUTHENTICATED caller gets 401 (the queue is behind
//       story 01's "Operator" policy, AC-03), and an allowlisted operator can read
//       the queue, confirm (the tale stops serving at /t/{slug}), and restore (the
//       tale serves again with its report count reset).
//
//  ANONYMITY (AC-06): every assertion is on CONTENT + a count. Nothing here surfaces
//  or requires a reporter identity, player nickname, room, or session.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuibbleStone.Api.Admin;
using QuibbleStone.Api.PublishedTales;
using QuibbleStone.Api.Settings;

namespace QuibbleStone.Api.Tests.Admin;

public sealed class ReportedTalesControllerTests
{
    private const string OperatorEmail = "ops@quibblestone.com";

    /// <summary>
    /// Constructs the controller over a fresh InMemoryOperatorActionLog (sysadmin-console/06) with
    /// a ClaimsPrincipal ControllerContext so User.Identity?.Name is non-null when Confirm / Restore
    /// append their action-log row.
    /// </summary>
    private static (ReportedTalesController Controller, InMemoryOperatorActionLog ActionLog) NewController(FakePublishedTaleStore store)
    {
        var actionLog = new InMemoryOperatorActionLog();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, OperatorEmail)], "Operator"));
        var controller = new ReportedTalesController(store, actionLog)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } },
        };
        return (controller, actionLog);
    }

    private static PublishedTale SampleTale(string slug) => new(
        Slug: slug,
        Title: "The flagged saga",
        Parts:
        [
            new TalePart(false, "Once a "),
            new TalePart(true, "wombat"),
            new TalePart(false, " sang."),
        ],
        BylineNames: "Sam & Mia",
        CreatedUtc: DateTimeOffset.UtcNow,
        ExpiresUtc: DateTimeOffset.UtcNow + PublishedTalesController.TaleTtl);

    // ---- Direct controller behavior (AC-03) ----------------------------------

    [Fact]
    public async Task Queue_lists_a_hidden_tale_with_its_content_and_report_count()
    {
        var store = new FakePublishedTaleStore();
        store.Seed(SampleTale("HIDDENSLUG12"));
        store.SeedModeration("HIDDENSLUG12", reportCount: 4, isHidden: true);
        // A second, NOT-hidden tale must not appear in the queue.
        store.Seed(SampleTale("VISIBLESLUG1"));

        var (controller, _) = NewController(store);
        var result = await controller.Queue(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var queue = Assert.IsType<ReportedTalesQueueResult>(ok.Value);
        var entry = Assert.Single(queue.Tales);
        Assert.Equal("HIDDENSLUG12", entry.Slug);
        Assert.Equal("The flagged saga", entry.Title);
        Assert.Equal(4, entry.ReportCount);
        // The content is carried so the operator can review it (a coral player-word).
        Assert.Contains(entry.Parts, p => p.IsWord && p.Text == "wombat");
    }

    [Fact]
    public async Task Confirm_keeps_a_hidden_tale_gone_so_it_never_serves_again()
    {
        var store = new FakePublishedTaleStore();
        store.Seed(SampleTale("HIDDENSLUG12"));
        store.SeedModeration("HIDDENSLUG12", reportCount: 3, isHidden: true);

        var (controller, _) = NewController(store);
        var result = await controller.Confirm("HIDDENSLUG12", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var action = Assert.IsType<ReportedTaleActionResult>(ok.Value);
        Assert.True(action.Applied);
        // Gone from serving AND from the queue.
        Assert.Null(await store.GetAsync("HIDDENSLUG12", CancellationToken.None));
        var queue = await store.ListHiddenAsync(CancellationToken.None);
        Assert.Empty(queue);
    }

    [Fact]
    public async Task Restore_resumes_serving_and_resets_the_report_count()
    {
        var store = new FakePublishedTaleStore();
        store.Seed(SampleTale("HIDDENSLUG12"));
        store.SeedModeration("HIDDENSLUG12", reportCount: 3, isHidden: true);

        var (controller, _) = NewController(store);
        var result = await controller.Restore("HIDDENSLUG12", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var action = Assert.IsType<ReportedTaleActionResult>(ok.Value);
        Assert.True(action.Applied);
        // Serving again, off the queue, and the count reset so the same reports do not
        // immediately re-hide it (AC-03).
        Assert.NotNull(await store.GetAsync("HIDDENSLUG12", CancellationToken.None));
        var state = await store.GetModerationAsync("HIDDENSLUG12", CancellationToken.None);
        Assert.Equal(0, state.ReportCount);
        Assert.False(state.IsHidden);
    }

    [Fact]
    public async Task Confirm_and_restore_are_idempotent_no_ops_for_a_non_hidden_slug()
    {
        var store = new FakePublishedTaleStore();
        store.Seed(SampleTale("VISIBLESLUG1")); // present but never reported / hidden

        var (controller, actionLog) = NewController(store);

        var confirm = await controller.Confirm("VISIBLESLUG1", CancellationToken.None);
        var restore = await controller.Restore("VISIBLESLUG1", CancellationToken.None);

        var confirmAction = Assert.IsType<ReportedTaleActionResult>(Assert.IsType<OkObjectResult>(confirm).Value);
        var restoreAction = Assert.IsType<ReportedTaleActionResult>(Assert.IsType<OkObjectResult>(restore).Value);
        Assert.False(confirmAction.Applied);
        Assert.False(restoreAction.Applied);
        // A not-hidden tale is untouched by either no-op.
        Assert.NotNull(await store.GetAsync("VISIBLESLUG1", CancellationToken.None));
    }

    // ---- AC-05 (sysadmin-console/06): a no-op writes NO action-log row -----------

    [Fact]
    public async Task Confirm_and_restore_on_a_not_hidden_slug_write_no_action_log_row()
    {
        var store = new FakePublishedTaleStore();
        store.Seed(SampleTale("VISIBLESLUG1")); // present but never reported / hidden
        var (controller, actionLog) = NewController(store);

        await controller.Confirm("VISIBLESLUG1", CancellationToken.None);
        await controller.Restore("VISIBLESLUG1", CancellationToken.None);

        Assert.Empty(actionLog.Entries);
    }

    [Fact]
    public async Task Confirm_and_restore_on_an_unknown_slug_write_no_action_log_row()
    {
        var store = new FakePublishedTaleStore();
        var (controller, actionLog) = NewController(store);

        await controller.Confirm("NOSUCHSLUG12", CancellationToken.None);
        await controller.Restore("NOSUCHSLUG12", CancellationToken.None);

        Assert.Empty(actionLog.Entries);
    }

    // ---- Boundary + end-to-end via the REAL app (AC-03 policy) ----------------

    [Fact]
    public async Task Queue_endpoint_is_behind_the_operator_policy_401_when_unauthenticated()
    {
        using var factory = new SeededAdminApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/admin/reported-tales");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Operator_can_review_confirm_and_restore_a_hidden_tale_over_http()
    {
        using var factory = new SeededAdminApiFactory();
        factory.Store.Seed(SampleTale("HIDDENSLUG12"));
        factory.Store.SeedModeration("HIDDENSLUG12", reportCount: 3, isHidden: true);

        var operatorClient = factory.CreateClient();
        operatorClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", factory.OperatorCredential());

        // 1. The queue lists the hidden tale + its count.
        var queueResponse = await operatorClient.GetAsync("/api/admin/reported-tales");
        Assert.Equal(HttpStatusCode.OK, queueResponse.StatusCode);
        var queue = await queueResponse.Content.ReadFromJsonAsync<ReportedTalesQueueResult>();
        Assert.NotNull(queue);
        var entry = Assert.Single(queue!.Tales);
        Assert.Equal("HIDDENSLUG12", entry.Slug);
        Assert.Equal(3, entry.ReportCount);

        // The hidden tale serves the neutral under-review page (200), not the tale.
        var underReview = await factory.CreateClient().GetAsync("/t/HIDDENSLUG12");
        Assert.Equal(HttpStatusCode.OK, underReview.StatusCode);
        Assert.Contains("under review", await underReview.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        // 2. Restore -> the tale serves normally again with the report count reset.
        var restoreResponse = await operatorClient.PostAsync("/api/admin/reported-tales/HIDDENSLUG12/restore", null);
        Assert.Equal(HttpStatusCode.OK, restoreResponse.StatusCode);
        var served = await factory.CreateClient().GetAsync("/t/HIDDENSLUG12");
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Contains("wombat", await served.Content.ReadAsStringAsync());

        // Re-hide and confirm -> the tale is gone (404) for good.
        factory.Store.SeedModeration("HIDDENSLUG12", reportCount: 3, isHidden: true);
        var confirmResponse = await operatorClient.PostAsync("/api/admin/reported-tales/HIDDENSLUG12/confirm", null);
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
        var gone = await factory.CreateClient().GetAsync("/t/HIDDENSLUG12");
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    /// <summary>
    /// Boots the real API in memory with a configured operator allowlist and the
    /// published-tale store REPLACED by a seedable in-memory fake, so the operator
    /// review queue can be exercised end-to-end over HTTP (AC-03) through the SAME
    /// store the public serve path reads. Development environment (matches the story
    /// 01 boundary tests). The allowlist is supplied via in-memory configuration
    /// exactly as an App Service setting would (AC-05) - never source, never VITE_*.
    /// </summary>
    private sealed class SeededAdminApiFactory : WebApplicationFactory<Program>
    {
        private const string AllowlistedOperator = "ops@quibblestone.com";

        public FakePublishedTaleStore Store { get; } = new();

        /// <summary>A genuine operator credential on the running app's own key ring.</summary>
        public string OperatorCredential()
        {
            var provider = Services.GetRequiredService<IDataProtectionProvider>();
            var protector = provider
                .CreateProtector(QuibbleStone.Api.Admin.OperatorSession.OperatorSessionPurpose)
                .ToTimeLimitedDataProtector();
            var payload = $"{AllowlistedOperator}|{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            return protector.Protect(payload, TimeSpan.FromHours(1));
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Operator:AllowedEmails:0"] = AllowlistedOperator,
                });
            });
            builder.ConfigureTestServices(services =>
            {
                // Replace the (disabled, no-storage) store with the seedable fake so
                // the queue + serve path share one in-memory store.
                var existing = services.Single(d => d.ServiceType == typeof(IPublishedTaleStore));
                services.Remove(existing);
                services.AddSingleton<IPublishedTaleStore>(Store);
            });
        }
    }
}
