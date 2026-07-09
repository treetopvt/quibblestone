// ----------------------------------------------------------------------------
//  StripeReconciliationEndpointTests - the operator Stripe-resync endpoint
//  (billing-entitlements/08, #215, AC-06). Boots the REAL app in memory
//  (WebApplicationFactory) so the ACTUAL Operator-policy auth + the rate-limiting
//  middleware are exercised, with the Stripe-coupled edge replaced by a fake source:
//    - AC-06a (operator-only): an unauthenticated POST is rejected 401 - no resync
//      runs before an operator session exists.
//    - AC-06b (idempotent): invoking resync twice against the same fake Stripe state
//      produces the same grants, no duplicate rows.
//    - AC-06d (rate-limited): a burst beyond StripeResyncRateLimit.PermitLimit gets 429.
//
//  Each test uses its OWN factory instance so the GLOBAL resync rate-limit budget is
//  isolated per test (the limiter state is per-app-instance).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuibbleStone.Api.Accounts;
using QuibbleStone.Api.Admin;
using QuibbleStone.Api.Billing;
using QuibbleStone.Api.Entitlements;

namespace QuibbleStone.Api.Tests.Billing;

public sealed class StripeReconciliationEndpointTests
{
    private const string AllowlistedOperator = "ops@quibblestone.com";

    // A fake Stripe edge that returns ONE candidate subscription for whatever email it is
    // asked about, carrying that same email as qs_purchaser metadata (so the service's
    // metadata match passes). IsEnabled so the reconciliation service actually runs.
    private sealed class EchoSubscriptionSource : IStripeSubscriptionSource
    {
        // Captured ONCE so two resync runs see the SAME Stripe state (a moving period end
        // would not be "the same state" - idempotency is about unchanged upstream state).
        private readonly DateTimeOffset _periodEnd = DateTimeOffset.UtcNow.AddDays(30);

        public bool IsEnabled => true;

        public Task<IReadOnlyList<ReconciliationCandidate>> ListCandidatesAsync(string email, CancellationToken ct = default)
        {
            IReadOnlyList<ReconciliationCandidate> candidates =
            [
                new ReconciliationCandidate(
                    SubscriptionId: "sub_echo",
                    PurchaserMetadata: email,
                    CapabilityKeys: [EntitlementCatalog.LibraryFull],
                    ProductId: "family-plan",
                    Status: "active",
                    CurrentPeriodEnd: _periodEnd),
            ];
            return Task.FromResult(candidates);
        }
    }

    // AC-06a: no operator session -> 401, before any resync runs.
    [Fact]
    public async Task Unauthenticated_resync_is_rejected()
    {
        await using var factory = new ResyncApiFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/admin/billing/resync", new { accountId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // AC-06b: invoking resync twice against the same fake Stripe state produces the same
    // grants (one row per capability), through the real endpoint + service + stores.
    [Fact]
    public async Task Resync_twice_is_idempotent_no_duplicate_rows()
    {
        await using var factory = new ResyncApiFactory();
        var accounts = factory.Services.GetRequiredService<IAccountStore>();
        var grantStore = factory.Services.GetRequiredService<IEntitlementGrantStore>();
        var account = await accounts.CreateOrGetAsync("buyer@example.com");

        var client = factory.CreateClient();
        var first = await Resync(factory, client, account.Id);
        var afterFirst = await grantStore.GetGrantsAsync(account.Id);
        var second = await Resync(factory, client, account.Id);
        var afterSecond = await grantStore.GetGrantsAsync(account.Id);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        // One grant for the reconciled capability, identical across the two runs.
        var g1 = Assert.Single(afterFirst);
        var g2 = Assert.Single(afterSecond);
        Assert.Equal(EntitlementCatalog.LibraryFull, g2.CapabilityKey);
        Assert.Equal(g1.ValidThrough, g2.ValidThrough);
        Assert.Equal("sub_echo", g2.StripeSubscriptionId);
        Assert.Equal(StripeMode.Test, g2.Mode);
    }

    // AC-06d: a burst beyond the permit limit gets 429 (the endpoint's global rate limiter).
    [Fact]
    public async Task Burst_beyond_the_permit_limit_gets_429()
    {
        await using var factory = new ResyncApiFactory();
        var client = factory.CreateClient();
        var accountId = Guid.NewGuid(); // a not-found id still consumes a permit (limiter runs first)

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < StripeResyncRateLimit.PermitLimit + 1; i++)
        {
            var response = await Resync(factory, client, accountId);
            statuses.Add(response.StatusCode);
        }

        // The first PermitLimit calls are allowed (200); the one beyond is throttled (429).
        Assert.Equal(StripeResyncRateLimit.PermitLimit, statuses.Count(s => s == HttpStatusCode.OK));
        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }

    // ---- helpers ----------------------------------------------------------------

    private async Task<HttpResponseMessage> Resync(ResyncApiFactory factory, HttpClient client, Guid accountId)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/admin/billing/resync")
        {
            Content = JsonContent.Create(new { accountId }),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", OperatorCredential(factory));
        return await client.SendAsync(message);
    }

    private static string OperatorCredential(ResyncApiFactory factory)
    {
        var provider = factory.Services.GetRequiredService<IDataProtectionProvider>();
        var protector = provider.CreateProtector(OperatorSession.OperatorSessionPurpose).ToTimeLimitedDataProtector();
        var payload = $"{AllowlistedOperator}|{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        return protector.Protect(payload, TimeSpan.FromHours(1));
    }

    // Boots the real API with an operator allowlist and the fake Stripe source swapped in
    // for the live one (so the service runs without live Stripe).
    private sealed class ResyncApiFactory : WebApplicationFactory<Program>
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
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IStripeSubscriptionSource>();
                services.AddSingleton<IStripeSubscriptionSource, EchoSubscriptionSource>();
            });
        }
    }
}
