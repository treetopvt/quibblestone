// ----------------------------------------------------------------------------
//  StripeCheckoutServiceTests - the checkout session-options builder + the disabled
//  config-off path (billing-entitlements/03, #72). The session-options mapping is a
//  pure static, so AC-02 ("both modes via the SAME service") is unit-testable without
//  a Stripe network call; the disabled service proves the config-off no-op.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Billing;
using QuibbleStone.Api.Entitlements;

namespace QuibbleStone.Api.Tests.Billing;

public class StripeCheckoutServiceTests
{
    private static CheckoutRequest Request(CheckoutMode mode) => new(
        Mode: mode,
        PriceId: "price_123",
        SuccessUrl: "https://app/success",
        CancelUrl: "https://app/cancel",
        CapabilityKeys: [EntitlementCatalog.LibraryFull, EntitlementCatalog.PlayRemote],
        PurchaserEmail: "buyer@example.com");

    // AC-02: a payment-mode request builds a "payment" session with the capability +
    // purchaser metadata stamped, and no subscription data.
    [Fact]
    public void Payment_mode_builds_a_payment_session_with_metadata()
    {
        var options = StripeCheckoutService.BuildSessionOptions(Request(CheckoutMode.Payment));

        Assert.Equal("payment", options.Mode);
        Assert.Equal("price_123", Assert.Single(options.LineItems).Price);
        Assert.Equal("buyer@example.com", options.CustomerEmail);
        Assert.Equal("library.full,play.remote", options.Metadata[BillingMetadata.CapabilitiesKey]);
        Assert.Equal("buyer@example.com", options.Metadata[BillingMetadata.PurchaserKey]);
        Assert.Null(options.SubscriptionData); // one-time: no subscription metadata
    }

    // AC-02: a subscription-mode request builds a "subscription" session AND stamps the
    // same metadata onto the subscription, so lifecycle events can resolve capabilities.
    [Fact]
    public void Subscription_mode_builds_a_subscription_session_and_stamps_subscription_metadata()
    {
        var options = StripeCheckoutService.BuildSessionOptions(Request(CheckoutMode.Subscription));

        Assert.Equal("subscription", options.Mode);
        Assert.NotNull(options.SubscriptionData);
        Assert.Equal("library.full,play.remote", options.SubscriptionData!.Metadata[BillingMetadata.CapabilitiesKey]);
        Assert.Equal("buyer@example.com", options.SubscriptionData.Metadata[BillingMetadata.PurchaserKey]);
    }

    // Config-off: the disabled service reports not-enabled and returns the "not
    // available" result rather than throwing (the config-presence split).
    [Fact]
    public async Task Disabled_service_returns_not_available()
    {
        var service = new DisabledStripeCheckoutService();

        Assert.False(service.IsEnabled);
        var result = await service.CreateCheckoutSessionAsync(Request(CheckoutMode.Payment));
        Assert.False(result.Enabled);
        Assert.Null(result.Url);
    }

    // BillingMetadata round-trips capability keys through the comma-joined metadata form.
    [Fact]
    public void Capability_metadata_round_trips()
    {
        var joined = BillingMetadata.JoinCapabilities([EntitlementCatalog.LibraryFull, EntitlementCatalog.Pack("spooky")]);
        var split = BillingMetadata.SplitCapabilities(joined);

        Assert.Equal(["library.full", "pack.spooky"], split);
        Assert.Empty(BillingMetadata.SplitCapabilities(null));
        Assert.Empty(BillingMetadata.SplitCapabilities(""));
    }
}
