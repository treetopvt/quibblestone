// ----------------------------------------------------------------------------
//  StripeOptionsTests - the mode-aware config shape (billing-entitlements/06 AC-01):
//  Live and Test credential sets are held simultaneously, resolved independently, and
//  the legacy flat fields act as the back-compat fallback for a mode with no own config.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Billing;

namespace QuibbleStone.Api.Tests.Billing;

public class StripeOptionsTests
{
    // AC-01: both modes configured -> each resolves to its OWN credentials, neither
    // overwriting the other.
    [Fact]
    public void Both_modes_resolve_independently()
    {
        var options = new StripeOptions
        {
            Live = new StripeModeConfig { SecretKey = "sk_live_1", WebhookSigningSecret = "whsec_live", PriceIds = { ["tip"] = "price_live" } },
            Test = new StripeModeConfig { SecretKey = "sk_test_1", WebhookSigningSecret = "whsec_test", PriceIds = { ["tip"] = "price_test" } },
        };

        Assert.Equal("sk_live_1", options.ForMode(StripeMode.Live).SecretKey);
        Assert.Equal("price_live", options.ForMode(StripeMode.Live).PriceIds["tip"]);
        Assert.Equal("sk_test_1", options.ForMode(StripeMode.Test).SecretKey);
        Assert.Equal("price_test", options.ForMode(StripeMode.Test).PriceIds["tip"]);
    }

    // Back-compat: only the flat fields configured (an old single-mode wiring) -> BOTH
    // modes fall back to the flat credentials.
    [Fact]
    public void Flat_fields_are_the_fallback_for_an_unconfigured_mode()
    {
        var options = new StripeOptions
        {
            SecretKey = "sk_test_flat",
            WebhookSigningSecret = "whsec_flat",
            PriceIds = { ["tip"] = "price_flat" },
        };

        Assert.Equal("sk_test_flat", options.ForMode(StripeMode.Test).SecretKey);
        Assert.Equal("sk_test_flat", options.ForMode(StripeMode.Live).SecretKey);
        Assert.Equal("price_flat", options.ForMode(StripeMode.Live).PriceIds["tip"]);
    }

    // A per-mode section takes precedence over the flat fallback for that mode only.
    [Fact]
    public void Per_mode_config_wins_over_flat_for_that_mode()
    {
        var options = new StripeOptions
        {
            SecretKey = "sk_test_flat",
            Live = new StripeModeConfig { SecretKey = "sk_live_1" },
        };

        Assert.Equal("sk_live_1", options.ForMode(StripeMode.Live).SecretKey); // per-mode
        Assert.Equal("sk_test_flat", options.ForMode(StripeMode.Test).SecretKey); // flat fallback
    }

    [Fact]
    public void IsConfigured_is_true_when_any_mode_or_flat_has_a_secret_key()
    {
        Assert.False(new StripeOptions().IsConfigured);
        Assert.True(new StripeOptions { SecretKey = "sk_test_flat" }.IsConfigured);
        Assert.True(new StripeOptions { Live = new StripeModeConfig { SecretKey = "sk_live_1" } }.IsConfigured);
        Assert.True(new StripeOptions { Test = new StripeModeConfig { SecretKey = "sk_test_1" } }.IsConfigured);
    }
}
