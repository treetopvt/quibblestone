// ----------------------------------------------------------------------------
//  OperatorAuthorizationTests - the LOAD-BEARING admin-boundary tests
//  (sysadmin-console/01, issue #135, AC-03/AC-06). These boot the REAL app in
//  memory (WebApplicationFactory) with a configured operator allowlist and assert
//  the ACTUAL HTTP status of the admin endpoint under the "Operator" policy:
//
//    - AC-03 (THE negative test): a purchaser-scoped Data Protection credential -
//      minted under AccountsController.PurchaserSessionPurpose using the SAME
//      running app's key ring - presented to the Operator-policy endpoint is
//      REJECTED (401). This is the `purchaser == admin` bug proven impossible: a
//      valid "signed in as some purchaser" credential does NOT satisfy an admin
//      endpoint, because the operator handler only unprotects under the DEDICATED
//      operator purpose (different derived key) and only then checks the allowlist.
//    - AC-06: an UNAUTHENTICATED request to the admin endpoint gets 401 - no admin
//      data is served before an operator session exists.
//    - A valid operator credential whose email is NOT (or no longer) on the
//      allowlist is rejected 401 - membership is re-checked every request (AC-05).
//    - Positive control (AC-01): an allowlisted operator credential is accepted
//      (200) and the endpoint echoes ONLY that operator email (AC-07).
//    - End-to-end (AC-01): the request -> verify (dev-echoed token) -> session HTTP
//      walk establishes a working operator session for an allowlisted email.
//
//  Data Protection is resolved from the SAME factory.Services the handler uses, so
//  the purchaser credential is a GENUINE one on the app's real key ring - not a
//  hand-forged stand-in. That is what makes the negative test structural, not
//  cosmetic.
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

public sealed class OperatorAuthorizationTests : IClassFixture<OperatorAuthorizationTests.AdminApiFactory>
{
    private const string AllowlistedOperator = "ops@quibblestone.com";
    private readonly AdminApiFactory _factory;

    public OperatorAuthorizationTests(AdminApiFactory factory) => _factory = factory;

    // ---- AC-03: the load-bearing negative test ----------------------------------

    [Fact]
    public async Task PurchaserCredential_PresentedToAnAdminEndpoint_IsRejected()
    {
        // A GENUINE purchaser credential on the running app's OWN key ring, minted
        // under the purchaser purpose EXACTLY as AccountsController mints it.
        var purchaserCredential = ProtectUnder(AccountsController.PurchaserSessionPurpose, "buyer@example.com");

        var response = await GetSession(purchaserCredential);

        // The purchaser is signed in as a purchaser - and is STILL locked out of the
        // admin surface. Purchaser is not operator (AC-03).
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- AC-06: unauthenticated gets nothing ------------------------------------

    [Fact]
    public async Task Unauthenticated_AdminEndpoint_Is401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/admin/session");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- allowlist re-checked every request (AC-05) -----------------------------

    [Fact]
    public async Task OperatorCredentialForANonAllowlistedEmail_IsRejected()
    {
        // A perfectly valid credential under the correct operator purpose - but for
        // an email that is NOT on the allowlist. The handler re-checks membership
        // every request, so this is rejected.
        var credential = ProtectUnder(OperatorSession.OperatorSessionPurpose, "used-to-be-an-operator@example.com");

        var response = await GetSession(credential);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- positive control (AC-01/AC-07) -----------------------------------------

    [Fact]
    public async Task AllowlistedOperatorCredential_IsAccepted_AndEchoesOnlyTheEmail()
    {
        var credential = ProtectUnder(OperatorSession.OperatorSessionPurpose, AllowlistedOperator);

        var response = await GetSession(credential);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<OperatorSessionResult>();
        Assert.NotNull(body);
        Assert.Equal(AllowlistedOperator, body!.Email);
    }

    // ---- end-to-end HTTP walk (AC-01) -------------------------------------------

    [Fact]
    public async Task RequestThenVerify_ForAnAllowlistedOperator_EstablishesASession()
    {
        var client = _factory.CreateClient();

        // 1. Request a link (Development env echoes a walkable token).
        var requestResponse = await client.PostAsJsonAsync(
            "/api/admin/login/request", new { email = AllowlistedOperator });
        Assert.Equal(HttpStatusCode.OK, requestResponse.StatusCode);
        var request = await requestResponse.Content.ReadFromJsonAsync<OperatorLoginRequestResult>();
        Assert.NotNull(request);
        Assert.False(string.IsNullOrEmpty(request!.DevToken));

        // 2. Verify the token -> signed-in with an operator credential.
        var verifyResponse = await client.PostAsJsonAsync(
            "/api/admin/login/verify", new { token = request.DevToken });
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);
        var verify = await verifyResponse.Content.ReadFromJsonAsync<OperatorLoginVerifyResult>();
        Assert.NotNull(verify);
        Assert.Equal("signed-in", verify!.Outcome);
        Assert.False(string.IsNullOrEmpty(verify.Credential));

        // 3. The minted credential opens the admin endpoint (a real established
        //    session), and it echoes the operator email.
        var sessionResponse = await GetSession(verify.Credential!);
        Assert.Equal(HttpStatusCode.OK, sessionResponse.StatusCode);
        var session = await sessionResponse.Content.ReadFromJsonAsync<OperatorSessionResult>();
        Assert.Equal(AllowlistedOperator, session!.Email);
    }

    [Fact]
    public async Task VerifyForANonOperator_DoesNotEstablishASession()
    {
        var client = _factory.CreateClient();

        var requestResponse = await client.PostAsJsonAsync(
            "/api/admin/login/request", new { email = "stranger@example.com" });
        var request = await requestResponse.Content.ReadFromJsonAsync<OperatorLoginRequestResult>();
        Assert.False(string.IsNullOrEmpty(request!.DevToken));

        var verifyResponse = await client.PostAsJsonAsync(
            "/api/admin/login/verify", new { token = request.DevToken });
        var verify = await verifyResponse.Content.ReadFromJsonAsync<OperatorLoginVerifyResult>();

        // A valid link for a non-operator: not-authorized, no credential (AC-02).
        Assert.Equal("not-authorized", verify!.Outcome);
        Assert.Null(verify.Credential);
    }

    // ---- helpers ----------------------------------------------------------------

    /// <summary>
    /// Protects "email|issuedAt" under <paramref name="purpose"/> using the RUNNING
    /// app's Data Protection provider - so the value is a genuine credential on the
    /// same key ring the operator handler reads (the structural point of AC-03).
    /// </summary>
    private string ProtectUnder(string purpose, string email)
    {
        var provider = _factory.Services.GetRequiredService<IDataProtectionProvider>();
        var protector = provider.CreateProtector(purpose).ToTimeLimitedDataProtector();
        var payload = $"{email}|{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        return protector.Protect(payload, TimeSpan.FromHours(1));
    }

    private async Task<HttpResponseMessage> GetSession(string bearer)
    {
        var client = _factory.CreateClient();
        var message = new HttpRequestMessage(HttpMethod.Get, "/api/admin/session");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return await client.SendAsync(message);
    }

    /// <summary>
    /// Boots the real API in memory with a configured operator allowlist. Development
    /// environment so the login request endpoint echoes a walkable token for the
    /// end-to-end walk. The allowlist is supplied via in-memory configuration exactly
    /// as an App Service setting would supply it (AC-05) - never source, never VITE_*.
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
