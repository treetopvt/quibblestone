// ----------------------------------------------------------------------------
//  BillingTests - the product -> capability map (billing-entitlements/04) and the
//  BillingController checkout/tip surface (billing-04 + billing-02). Uses the real
//  ProductCatalog and a capturing fake checkout service (no Stripe network), so the
//  load-bearing rules are pinned:
//    - family-plan maps to the FULL bundle; a pack maps to its single key; the tip
//      maps to EMPTY capabilities (AC-01 / story-02 AC-02).
//    - the client can only buy a KNOWN product (unknown -> 404; a tip cannot be
//      bought via the paywall /checkout path).
//    - a tip's message is safety-filtered before checkout (story-02 AC-05).
//    - config-off (disabled checkout) returns a clean not-enabled result.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.Mvc;
using QuibbleStone.Api.Billing;
using QuibbleStone.Api.Controllers;
using QuibbleStone.Api.Entitlements;
using QuibbleStone.Api.Safety;

namespace QuibbleStone.Api.Tests.Billing;

public class BillingTests
{
    private static StripeOptions ConfiguredOptions() => new()
    {
        PriceIds = new Dictionary<string, string>
        {
            ["family-plan"] = "price_family",
            ["pack.spooky"] = "price_spooky",
            ["tip"] = "price_tip",
        },
    };

    // ---- ProductCatalog: the capability map (AC-01 / story-02 AC-02) ----------

    [Fact]
    public void FamilyPlan_maps_to_the_full_bundle_as_a_subscription()
    {
        var catalog = new ProductCatalog(ConfiguredOptions());

        var plan = catalog.Resolve("family-plan");

        Assert.NotNull(plan);
        Assert.Equal(CheckoutMode.Subscription, plan!.Mode);
        Assert.Equal(
            new[] { EntitlementCatalog.LibraryFull, EntitlementCatalog.PlayRemote, EntitlementCatalog.PlayLargeGroup },
            plan.CapabilityKeys);
        Assert.True(plan.IsPurchasable);
    }

    [Fact]
    public void Pack_maps_to_its_single_capability_key_as_a_one_time_payment()
    {
        var catalog = new ProductCatalog(ConfiguredOptions());

        var pack = catalog.Resolve("pack.spooky");

        Assert.NotNull(pack);
        Assert.Equal(CheckoutMode.Payment, pack!.Mode);
        Assert.Equal(new[] { "pack.spooky" }, pack.CapabilityKeys);
    }

    [Fact]
    public void Tip_maps_to_no_capabilities_and_is_excluded_from_the_paywall()
    {
        var catalog = new ProductCatalog(ConfiguredOptions());

        var tip = catalog.Resolve(catalog.TipProductId);

        Assert.NotNull(tip);
        Assert.Empty(tip!.CapabilityKeys); // entitlement-neutral (story 02 AC-02)
        Assert.DoesNotContain(catalog.PaywallProducts, p => p.ProductId == catalog.TipProductId);
    }

    [Fact]
    public void Products_are_not_purchasable_without_a_configured_price_id()
    {
        var catalog = new ProductCatalog(new StripeOptions()); // no price ids

        Assert.All(catalog.PaywallProducts, p => Assert.False(p.IsPurchasable));
    }

    // ---- BillingController: checkout resolves capabilities server-side (AC-01) -

    [Fact]
    public async Task Checkout_family_plan_starts_a_session_with_the_full_bundle()
    {
        var checkout = new CapturingCheckoutService();
        var controller = NewController(checkout);

        var action = await controller.Checkout(new CheckoutRequestBody("family-plan", "buyer@example.com"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action);
        var result = Assert.IsType<CheckoutStartResult>(ok.Value);
        Assert.True(result.Enabled);
        Assert.NotNull(checkout.LastRequest);
        Assert.Equal(CheckoutMode.Subscription, checkout.LastRequest!.Mode);
        Assert.Equal(
            new[] { EntitlementCatalog.LibraryFull, EntitlementCatalog.PlayRemote, EntitlementCatalog.PlayLargeGroup },
            checkout.LastRequest.CapabilityKeys);
        Assert.Equal("buyer@example.com", checkout.LastRequest.PurchaserEmail);
        // Return URLs must target the paywall route that reads ?purchase (GetMore).
        Assert.EndsWith("/get-more?purchase=success", checkout.LastRequest.SuccessUrl);
        Assert.EndsWith("/get-more?purchase=cancel", checkout.LastRequest.CancelUrl);
    }

    [Fact]
    public async Task Checkout_rejects_an_unknown_product()
    {
        var controller = NewController(new CapturingCheckoutService());

        var action = await controller.Checkout(new CheckoutRequestBody("no-such-product", null), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(action);
    }

    [Fact]
    public async Task Checkout_refuses_to_sell_the_tip_via_the_paywall_path()
    {
        var checkout = new CapturingCheckoutService();
        var controller = NewController(checkout);

        var action = await controller.Checkout(new CheckoutRequestBody("tip", null), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(action);
        Assert.Null(checkout.LastRequest); // never started a session
    }

    // ---- BillingController: tip is entitlement-neutral + safety-filtered ---------

    [Fact]
    public async Task Tip_starts_a_session_with_no_capabilities()
    {
        var checkout = new CapturingCheckoutService();
        var controller = NewController(checkout);

        await controller.Tip(new TipRequestBody("You all are the best!"), CancellationToken.None);

        Assert.NotNull(checkout.LastRequest);
        Assert.Empty(checkout.LastRequest!.CapabilityKeys); // grants nothing (story 02 AC-02)
        Assert.Equal(CheckoutMode.Payment, checkout.LastRequest.Mode);
        // Return URLs must target /support (where Support reads ?tip), NOT Home - so the
        // gold-Guardian thank-you / cancel state actually shows (Copilot review fix).
        Assert.EndsWith("/support?tip=success", checkout.LastRequest.SuccessUrl);
        Assert.EndsWith("/support?tip=cancel", checkout.LastRequest.CancelUrl);
    }

    [Fact]
    public async Task Tip_blocks_an_unsafe_message_before_checkout()
    {
        var checkout = new CapturingCheckoutService();
        var controller = NewController(checkout, safety: new BlockingSafetyFilter());

        var action = await controller.Tip(new TipRequestBody("something blocked"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action);
        var result = Assert.IsType<CheckoutStartResult>(ok.Value);
        Assert.Null(result.Url);
        Assert.Null(checkout.LastRequest); // never reached checkout (AC-05)
    }

    // ---- BillingController: config-off is a clean not-enabled result ------------

    [Fact]
    public async Task Checkout_when_billing_disabled_returns_not_enabled()
    {
        var controller = NewController(new DisabledStripeCheckoutService());

        var action = await controller.Checkout(new CheckoutRequestBody("family-plan", null), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action);
        var result = Assert.IsType<CheckoutStartResult>(ok.Value);
        Assert.False(result.Enabled);
        Assert.Null(result.Url);
    }

    [Fact]
    public async Task Tip_when_billing_disabled_returns_not_enabled()
    {
        var controller = NewController(new DisabledStripeCheckoutService());

        var action = await controller.Tip(new TipRequestBody(null), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action);
        var result = Assert.IsType<CheckoutStartResult>(ok.Value);
        Assert.False(result.Enabled);
        Assert.Null(result.Url);
    }

    // ---- helpers ----------------------------------------------------------------

    private static BillingController NewController(IStripeCheckoutService checkout, IContentSafetyFilter? safety = null)
    {
        var options = ConfiguredOptions();
        return new BillingController(checkout, new ProductCatalog(options), safety ?? new AllowAllSafetyFilter(), options);
    }

    /// <summary>An enabled checkout that captures the last request instead of calling Stripe.</summary>
    private sealed class CapturingCheckoutService : IStripeCheckoutService
    {
        public CheckoutRequest? LastRequest { get; private set; }
        public bool IsEnabled => true;

        public Task<CheckoutSessionResult> CreateCheckoutSessionAsync(CheckoutRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            return Task.FromResult(new CheckoutSessionResult(Enabled: true, Url: "https://stripe.test/session", SessionId: "cs_test"));
        }
    }

    private sealed class AllowAllSafetyFilter : IContentSafetyFilter
    {
        public ValueTask<ContentSafetyResult> CheckAsync(string? candidate, CancellationToken cancellationToken = default)
            => new(ContentSafetyResult.Allowed);
    }

    private sealed class BlockingSafetyFilter : IContentSafetyFilter
    {
        public ValueTask<ContentSafetyResult> CheckAsync(string? candidate, CancellationToken cancellationToken = default)
            => new(ContentSafetyResult.Blocked("Let's keep it friendly."));
    }
}
