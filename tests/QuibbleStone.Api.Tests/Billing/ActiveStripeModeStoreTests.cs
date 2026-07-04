// ----------------------------------------------------------------------------
//  ActiveStripeModeStoreTests - the runtime-mutable active-mode flag + the resolver
//  context (billing-entitlements/06). Exercises the in-memory store + ActiveStripeContext
//  (the durable-across-restart guarantee is the Table Storage store's job, integration-
//  verified). Covers: safe default (AC-05), a runtime flip is visible (AC-02), and
//  last-changed is recorded (AC-07).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Billing;

namespace QuibbleStone.Api.Tests.Billing;

public class ActiveStripeModeStoreTests
{
    // AC-05: a fresh store (nothing persisted) resolves to Test, never Live, with no
    // last-changed time.
    [Fact]
    public async Task Fresh_store_defaults_to_test()
    {
        var store = new InMemoryActiveStripeModeStore();

        var state = await store.GetAsync();

        Assert.Equal(StripeMode.Test, state.Mode);
        Assert.Null(state.LastChangedUtc);
    }

    // AC-02 + AC-07: a flip is visible on the next read (no restart needed) and records
    // the new mode + a last-changed timestamp.
    [Fact]
    public async Task Set_is_visible_on_next_read_with_a_timestamp()
    {
        var store = new InMemoryActiveStripeModeStore();
        var at = DateTimeOffset.UtcNow;

        await store.SetAsync(StripeMode.Live, at);
        var state = await store.GetAsync();

        Assert.Equal(StripeMode.Live, state.Mode);
        Assert.Equal(at, state.LastChangedUtc);
    }

    // AC-03: the context resolves the ACTIVE mode's credentials - Test by default, Live
    // after a flip - never a mixed pair.
    [Fact]
    public async Task Context_resolves_the_active_modes_config()
    {
        var options = new StripeOptions
        {
            Live = new StripeModeConfig { SecretKey = "sk_live_1", PriceIds = { ["tip"] = "price_live" } },
            Test = new StripeModeConfig { SecretKey = "sk_test_1", PriceIds = { ["tip"] = "price_test" } },
        };
        var context = new ActiveStripeContext(new InMemoryActiveStripeModeStore(), options);

        // Default active mode is Test.
        var beforeConfig = await context.GetActiveConfigAsync();
        Assert.Equal("sk_test_1", beforeConfig.SecretKey);
        Assert.Equal("price_test", beforeConfig.PriceIds["tip"]);

        // Flip to Live -> the resolved config follows.
        await context.SetModeAsync(StripeMode.Live);
        var afterState = await context.GetStateAsync();
        var afterConfig = await context.GetActiveConfigAsync();

        Assert.Equal(StripeMode.Live, afterState.Mode);
        Assert.NotNull(afterState.LastChangedUtc);
        Assert.Equal("sk_live_1", afterConfig.SecretKey);
        Assert.Equal("price_live", afterConfig.PriceIds["tip"]); // never a live/test mix
    }
}
