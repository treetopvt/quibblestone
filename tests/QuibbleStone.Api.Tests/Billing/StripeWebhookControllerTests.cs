// ----------------------------------------------------------------------------
//  StripeWebhookControllerTests - exercises the webhook endpoint THROUGH the real
//  Stripe.net EventUtility.ConstructEvent (unlike StripeWebhookHandlerTests, which
//  operate on the normalized BillingEvent downstream of it). These lock in two things
//  the handler tests structurally cannot:
//    - a validly-signed event whose api_version differs from the SDK's pinned version
//      is ACCEPTED (200) and applied - the fix for the bug caught during the test-mode
//      end-to-end verification (real Stripe webhooks carry the ACCOUNT's api version,
//      so ConstructEvent must NOT throwOnApiVersionMismatch).
//    - a tampered/invalid signature is REJECTED (400) and applies nothing (AC-03).
//
//  The Stripe-Signature header is built with the same HMAC-SHA256 scheme Stripe uses,
//  so this is a faithful test of the real signature path with no live Stripe.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using QuibbleStone.Api.Accounts;
using QuibbleStone.Api.Billing;
using QuibbleStone.Api.Controllers;
using QuibbleStone.Api.Entitlements;

namespace QuibbleStone.Api.Tests.Billing;

public class StripeWebhookControllerTests
{
    private const string Secret = "whsec_testsecret_0123456789abcdef";
    private const string Purchaser = "buyer@example.com";

    // A checkout.session.completed event carrying an OLD account api_version (the value
    // a real Stripe account sends) plus our capability + purchaser metadata.
    private static string EventPayload() =>
        "{\"id\":\"evt_ctrl_1\",\"object\":\"event\",\"api_version\":\"2020-03-02\"," +
        "\"type\":\"checkout.session.completed\",\"data\":{\"object\":{\"id\":\"cs_ctrl_1\"," +
        "\"object\":\"checkout.session\",\"mode\":\"payment\",\"customer_email\":\"" + Purchaser + "\"," +
        "\"metadata\":{\"qs_capabilities\":\"library.full,play.remote\",\"qs_purchaser\":\"" + Purchaser + "\"}}}}";

    private static string Sign(string payload, long timestamp) => SignWith(payload, timestamp, Secret);

    private static string SignWith(string payload, long timestamp, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));
        return Convert.ToHexStringLower(hash);
    }

    private static (StripeWebhookController Controller, InMemoryEntitlementGrantStore Grants) NewController()
    {
        var grants = new InMemoryEntitlementGrantStore();
        var accounts = new InMemoryAccountStore();
        var processed = new InMemoryProcessedEventStore();
        var options = new StripeOptions { WebhookSigningSecret = Secret, SecretKey = "sk_test_x" };
        var handler = new StripeWebhookHandler(grants, accounts, processed, options);
        var controller = new StripeWebhookController(handler, options, NullLogger<StripeWebhookController>.Instance);
        return (controller, grants);
    }

    private static void SetRequest(StripeWebhookController controller, string payload, string signatureHeader)
    {
        var http = new DefaultHttpContext();
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(payload));
        http.Request.Headers["Stripe-Signature"] = signatureHeader;
        controller.ControllerContext = new ControllerContext { HttpContext = http };
    }

    [Fact]
    public async Task Validly_signed_event_with_mismatched_api_version_is_accepted_and_applied()
    {
        var (controller, grants) = NewController();
        var payload = EventPayload();
        var t = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        SetRequest(controller, payload, $"t={t},v1={Sign(payload, t)}");

        var action = await controller.Webhook(CancellationToken.None);

        // Not a 400: the older account api_version must NOT be rejected (the fix).
        Assert.IsType<OkObjectResult>(action);
        // And the grant path ran: the purchaser now holds the granted capabilities.
        var held = await grants.GetGrantsAsync(Purchaser);
        Assert.Contains(held, g => g.CapabilityKey == EntitlementCatalog.LibraryFull);
        Assert.Contains(held, g => g.CapabilityKey == EntitlementCatalog.PlayRemote);
    }

    // AC-04 (billing-entitlements/06): with BOTH modes' signing secrets configured, an
    // event signed with the LIVE secret is accepted even though Test is the active mode -
    // the controller tries each mode's secret, so a mode flip cannot orphan an in-flight
    // webhook signed under the other mode.
    [Fact]
    public async Task Event_signed_with_the_live_secret_is_accepted_while_test_is_active()
    {
        const string liveSecret = "whsec_live_9876543210fedcba";
        var grants = new InMemoryEntitlementGrantStore();
        var options = new StripeOptions
        {
            Test = new StripeModeConfig { SecretKey = "sk_test_x", WebhookSigningSecret = Secret },
            Live = new StripeModeConfig { SecretKey = "sk_live_x", WebhookSigningSecret = liveSecret },
        };
        var handler = new StripeWebhookHandler(grants, new InMemoryAccountStore(), new InMemoryProcessedEventStore(), options);
        var controller = new StripeWebhookController(handler, options, NullLogger<StripeWebhookController>.Instance);

        var payload = EventPayload();
        var t = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        SetRequest(controller, payload, $"t={t},v1={SignWith(payload, t, liveSecret)}");

        var action = await controller.Webhook(CancellationToken.None);

        // Accepted despite Test being the active mode (the default) - verified against the live secret.
        Assert.IsType<OkObjectResult>(action);
        var held = await grants.GetGrantsAsync(Purchaser);
        Assert.Contains(held, g => g.CapabilityKey == EntitlementCatalog.LibraryFull);
    }

    [Fact]
    public async Task Tampered_signature_is_rejected_and_grants_nothing()
    {
        var (controller, grants) = NewController();
        var payload = EventPayload();
        var t = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        // A valid-looking header with a WRONG signature.
        SetRequest(controller, payload, $"t={t},v1={new string('0', 64)}");

        var action = await controller.Webhook(CancellationToken.None);

        Assert.IsType<BadRequestResult>(action);
        Assert.Empty(await grants.GetGrantsAsync(Purchaser)); // nothing applied
    }
}
