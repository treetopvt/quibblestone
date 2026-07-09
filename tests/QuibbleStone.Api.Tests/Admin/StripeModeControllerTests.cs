// ----------------------------------------------------------------------------
//  StripeModeControllerTests - the operator-gated mode-toggle endpoint AFTER
//  sysadmin-console/04 (one console, one auth). Relocated + rewritten from the old
//  Billing/StripeModeControllerTests: the endpoint is now guarded by the REAL
//  "Operator" authorization policy (the SAME policy AdminEntitlementsController and
//  ReportedTalesController use), NOT the deleted interim X-Operator-Secret gate.
//
//  These boot the REAL app in memory (WebApplicationFactory) with a configured
//  operator allowlist and assert the ACTUAL HTTP status, mirroring
//  OperatorAuthorizationTests' pattern:
//    - AC-01 (negative): GET/POST with NO operator credential is 401 - the
//      authorization middleware rejects before the action runs, and the mode never
//      changes.
//    - AC-01 (negative): a genuine PURCHASER credential (minted under the purchaser
//      purpose on the app's own key ring) is rejected 401 - purchaser != operator.
//    - AC-01 (positive): an allowlisted operator credential reads (GET 200) and flips
//      (POST 200) the mode.
//    - An unknown mode value is a 400 and changes nothing (never a silent go-Live).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuibbleStone.Api.Admin;
using QuibbleStone.Api.Controllers;

namespace QuibbleStone.Api.Tests.Admin;

public sealed class StripeModeControllerTests
{
    private const string AllowlistedOperator = "ops@quibblestone.com";
    private const string StripeModeEndpoint = "/api/admin/stripe-mode";

    // ---- AC-01 negative: no operator credential -> 401, no read, no change --------

    [Fact]
    public async Task Get_without_an_operator_credential_is_unauthorized()
    {
        using var factory = new AdminApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync(StripeModeEndpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_without_an_operator_credential_is_unauthorized()
    {
        using var factory = new AdminApiFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(StripeModeEndpoint, new { mode = "live" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- AC-01 negative: a purchaser credential is not an operator credential -----

    [Fact]
    public async Task PurchaserCredential_IsRejected()
    {
        using var factory = new AdminApiFactory();
        // A GENUINE purchaser credential on the running app's OWN key ring, minted under
        // the purchaser purpose - it must NOT satisfy the operator policy (purchaser !=
        // operator, OperatorSession's structural guarantee).
        var purchaser = ProtectUnder(factory, AccountsController.PurchaserSessionPurpose, "buyer@example.com");

        var response = await Send(factory, HttpMethod.Get, StripeModeEndpoint, purchaser);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- AC-01 positive: an allowlisted operator can read + flip the mode ----------

    [Fact]
    public async Task Get_with_an_allowlisted_operator_returns_the_active_mode()
    {
        using var factory = new AdminApiFactory();
        var operatorCredential = ProtectUnder(factory, OperatorSession.OperatorSessionPurpose, AllowlistedOperator);

        var response = await Send(factory, HttpMethod.Get, StripeModeEndpoint, operatorCredential);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var view = await response.Content.ReadFromJsonAsync<StripeModeView>();
        Assert.NotNull(view);
        Assert.Equal("test", view!.ActiveMode); // Test is the default with no persisted flip.
    }

    [Fact]
    public async Task Post_with_an_allowlisted_operator_flips_the_mode()
    {
        using var factory = new AdminApiFactory();
        var operatorCredential = ProtectUnder(factory, OperatorSession.OperatorSessionPurpose, AllowlistedOperator);

        var response = await Send(factory, HttpMethod.Post, StripeModeEndpoint, operatorCredential, new { mode = "live" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var view = await response.Content.ReadFromJsonAsync<StripeModeView>();
        Assert.NotNull(view);
        Assert.Equal("live", view!.ActiveMode);
        Assert.NotNull(view.LastChangedUtc);

        // The flip is visible on the next read (same in-memory store across the app).
        var readBack = await Send(factory, HttpMethod.Get, StripeModeEndpoint, operatorCredential);
        var after = await readBack.Content.ReadFromJsonAsync<StripeModeView>();
        Assert.Equal("live", after!.ActiveMode);
    }

    // ---- an unknown mode value is a 400 and changes nothing -----------------------

    [Fact]
    public async Task Post_with_an_unknown_mode_is_rejected()
    {
        using var factory = new AdminApiFactory();
        var operatorCredential = ProtectUnder(factory, OperatorSession.OperatorSessionPurpose, AllowlistedOperator);

        var response = await Send(factory, HttpMethod.Post, StripeModeEndpoint, operatorCredential, new { mode = "banana" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- helpers -----------------------------------------------------------------

    private static string ProtectUnder(AdminApiFactory factory, string purpose, string email)
    {
        var provider = factory.Services.GetRequiredService<IDataProtectionProvider>();
        var protector = provider.CreateProtector(purpose).ToTimeLimitedDataProtector();
        var payload = $"{email}|{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        return protector.Protect(payload, TimeSpan.FromHours(1));
    }

    private static async Task<HttpResponseMessage> Send(
        AdminApiFactory factory, HttpMethod method, string url, string bearer, object? body = null)
    {
        var client = factory.CreateClient();
        var message = new HttpRequestMessage(method, url)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", bearer) },
        };
        if (body is not null)
        {
            message.Content = JsonContent.Create(body);
        }
        return await client.SendAsync(message);
    }

    /// <summary>
    /// Boots the real API in memory with a configured operator allowlist (the same
    /// pattern OperatorAuthorizationTests uses). No storage is configured, so the
    /// in-memory active-mode store backs the flip - a POST persists for the app's
    /// lifetime and is visible on a subsequent GET.
    /// </summary>
    public sealed class AdminApiFactory : WebApplicationFactory<Program>
    {
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
