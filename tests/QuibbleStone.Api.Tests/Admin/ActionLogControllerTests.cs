// ----------------------------------------------------------------------------
//  ActionLogControllerTests - the OPERATOR read surface for the action log
//  (sysadmin-console/06, issue #233, AC-03). Two layers, mirroring the
//  StripeModeControllerTests boundary pattern:
//
//    1. A direct-controller test over a seeded InMemoryOperatorActionLog (a
//       controllable clock so rows get distinct, chosen ages): the response comes
//       back NEWEST-FIRST and capped at the 200-row page size - seeding 205 rows
//       proves the cap actually bites, not just "returns everything".
//    2. A WebApplicationFactory boundary walk (AC-05's same Operator/Ops-scope
//       posture as StripeModeController): GET /api/admin/action-log is 401
//       unauthenticated, 401 for a genuine purchaser credential, and 200 for an
//       allowlisted operator (today's single operator holds every scope).
//
//  ANONYMITY (AC-06): every assertion here is operator email + action + target +
//  note + timestamp. Nothing references a player nickname, room, or session.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuibbleStone.Api.Admin;
using QuibbleStone.Api.Controllers;
using QuibbleStone.Api.Settings;

namespace QuibbleStone.Api.Tests.Admin;

public sealed class ActionLogControllerTests
{
    private const string AllowlistedOperator = "ops@quibblestone.com";
    private const string ActionLogEndpoint = "/api/admin/action-log";

    // ---- AC-03: direct-controller, newest-first + capped ---------------------------

    [Fact]
    public async Task Get_returns_rows_newest_first()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var tick = start;
        var actionLog = new InMemoryOperatorActionLog(() => tick);
        var controller = new ActionLogController(actionLog);

        await actionLog.AppendAsync("ops@quibblestone.com", "settings.put", "key-one", "oldest", CancellationToken.None);
        tick = tick.AddMinutes(1);
        await actionLog.AppendAsync("ops@quibblestone.com", "settings.put", "key-two", "middle", CancellationToken.None);
        tick = tick.AddMinutes(1);
        await actionLog.AppendAsync("ops@quibblestone.com", "settings.put", "key-three", "newest", CancellationToken.None);

        var result = await controller.Get(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var view = Assert.IsType<ActionLogViewResult>(ok.Value);
        Assert.Equal(3, view.Rows.Count);
        Assert.Equal("newest", view.Rows[0].Note);
        Assert.Equal("middle", view.Rows[1].Note);
        Assert.Equal("oldest", view.Rows[2].Note);
    }

    [Fact]
    public async Task Get_is_capped_at_200_rows_even_when_more_were_appended()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var tick = start;
        var actionLog = new InMemoryOperatorActionLog(() => tick);
        var controller = new ActionLogController(actionLog);

        // Seed 205 rows, each one tick apart, so ordering is unambiguous even under the same clock resolution.
        for (var i = 0; i < 205; i++)
        {
            await actionLog.AppendAsync("ops@quibblestone.com", "settings.put", $"key-{i:000}", $"note-{i:000}", CancellationToken.None);
            tick = tick.AddSeconds(1);
        }

        var result = await controller.Get(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var view = Assert.IsType<ActionLogViewResult>(ok.Value);
        Assert.Equal(200, view.Rows.Count);
        // Newest-first: the very last appended row (key-204) leads the page.
        Assert.Equal("key-204", view.Rows[0].Target);
    }

    [Fact]
    public async Task Get_carries_only_operator_plane_facts()
    {
        var actionLog = new InMemoryOperatorActionLog();
        var controller = new ActionLogController(actionLog);
        await actionLog.AppendAsync("ops@quibblestone.com", "entitlement.grant", "buyer@example.com", "library.full", CancellationToken.None);

        var result = await controller.Get(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var view = Assert.IsType<ActionLogViewResult>(ok.Value);
        var row = Assert.Single(view.Rows);
        Assert.Equal("ops@quibblestone.com", row.OperatorEmail);
        Assert.Equal("entitlement.grant", row.Action);
        Assert.Equal("buyer@example.com", row.Target);
        Assert.Equal("library.full", row.Note);
    }

    // ---- Boundary + end-to-end via the REAL app (AC-05, Ops scope) ------------------

    [Fact]
    public async Task Endpoint_is_401_when_unauthenticated()
    {
        using var factory = new AdminApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync(ActionLogEndpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PurchaserCredential_IsRejected()
    {
        using var factory = new AdminApiFactory();
        // A GENUINE purchaser credential on the app's own key ring - never satisfies the operator policy.
        var purchaserCredential = factory.ProtectUnder(AccountsController.PurchaserSessionPurpose, "buyer@example.com");
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", purchaserCredential);

        var response = await client.GetAsync(ActionLogEndpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AllowlistedOperator_CanReadTheActionLog_OverHttp()
    {
        using var factory = new AdminApiFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", factory.OperatorCredential());

        var response = await client.GetAsync(ActionLogEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var view = await response.Content.ReadFromJsonAsync<ActionLogViewResult>();
        Assert.NotNull(view);
    }

    /// <summary>
    /// Boots the real API in memory with a configured operator allowlist (Development
    /// environment, matching the story 01/04/05 boundary tests). AC-05.
    /// </summary>
    private sealed class AdminApiFactory : WebApplicationFactory<Program>
    {
        /// <summary>A genuine operator credential on the running app's own key ring.</summary>
        public string OperatorCredential() =>
            ProtectUnder(OperatorSession.OperatorSessionPurpose, AllowlistedOperator);

        /// <summary>Protects "email|issuedAt" under a purpose using the app's real key ring.</summary>
        public string ProtectUnder(string purpose, string email)
        {
            var provider = Services.GetRequiredService<IDataProtectionProvider>();
            var protector = provider.CreateProtector(purpose).ToTimeLimitedDataProtector();
            var payload = $"{email}|{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
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
        }
    }
}
