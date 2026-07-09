// ----------------------------------------------------------------------------
//  RateLimitPermitClampTests - control-plane/03 (#232) AC-08: the independent
//  read-site safety net every rate-limit-permit knob is clamped through
//  (Program.ClampedRateLimitPermits), belt AND suspenders on top of the catalog's
//  own Bounds. A settings value of zero, negative, or absurdly large must never
//  reach the limiter's PermitLimit unclamped - zero/negative would THROW inside the
//  partition-factory lambda (an outage), and an absurd value would silently disable
//  the guard. Also pins the fail-safe posture on a settings-read failure: it
//  degrades to the caller's code default, which is itself then clamped.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using QuibbleStone.Api.Settings;

namespace QuibbleStone.Api.Tests.Settings;

public sealed class RateLimitPermitClampTests
{
    private const string Key = SettingsCatalog.AiRateLimitPerIpPermitPerMinute;

    private static HttpContext ContextWithSettings(IRuntimeSettingsService settings)
    {
        var services = new ServiceCollection()
            .AddSingleton<IRuntimeSettingsService>(settings)
            .BuildServiceProvider();
        return new DefaultHttpContext { RequestServices = services };
    }

    [Fact]
    public void A_zero_settings_value_clamps_up_to_the_floor()
    {
        var httpContext = ContextWithSettings(TestRuntimeSettings.WithInt(Key, 0));

        var permits = Program.ClampedRateLimitPermits(httpContext, Key, codeDefault: 30);

        Assert.Equal(SettingsCatalog.RateLimitPermitClampMin, permits);
        Assert.Equal(1, permits);
    }

    [Fact]
    public void A_negative_settings_value_clamps_up_to_the_floor()
    {
        var httpContext = ContextWithSettings(TestRuntimeSettings.WithInt(Key, -5));

        var permits = Program.ClampedRateLimitPermits(httpContext, Key, codeDefault: 30);

        Assert.Equal(SettingsCatalog.RateLimitPermitClampMin, permits);
        Assert.Equal(1, permits);
    }

    [Fact]
    public void An_absurdly_large_settings_value_clamps_down_to_the_ceiling()
    {
        var httpContext = ContextWithSettings(TestRuntimeSettings.WithInt(Key, 1_000_000));

        var permits = Program.ClampedRateLimitPermits(httpContext, Key, codeDefault: 30);

        Assert.Equal(SettingsCatalog.RateLimitPermitClampMax, permits);
        Assert.Equal(10_000, permits);
    }

    [Fact]
    public void An_in_range_settings_value_passes_through_unchanged()
    {
        var httpContext = ContextWithSettings(TestRuntimeSettings.WithInt(Key, 30));

        var permits = Program.ClampedRateLimitPermits(httpContext, Key, codeDefault: 30);

        Assert.Equal(30, permits);
    }

    [Fact]
    public void A_settings_read_failure_falls_back_to_the_clamped_code_default()
    {
        // No IRuntimeSettingsService registered at all - GetRequiredService throws inside
        // the try, and the catch degrades to codeDefault (itself then clamped).
        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().BuildServiceProvider(),
        };

        var permits = Program.ClampedRateLimitPermits(httpContext, Key, codeDefault: 5);

        Assert.Equal(5, permits);
    }

    [Fact]
    public void A_settings_read_failure_still_clamps_an_out_of_range_code_default()
    {
        // Even the fallback code default is not trusted blindly - it still goes through
        // the clamp, so a bad literal at a call site can never bypass the floor/ceiling.
        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().BuildServiceProvider(),
        };

        var permits = Program.ClampedRateLimitPermits(httpContext, Key, codeDefault: 0);

        Assert.Equal(SettingsCatalog.RateLimitPermitClampMin, permits);
    }
}
