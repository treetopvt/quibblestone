// ----------------------------------------------------------------------------
//  UsageTelemetryTests - proves the anonymous product-usage property builders
//  carry NO PII (platform-devops/05, AC-04) and normalize the mode to a stable
//  enum-ish id (AC-01). UsageTelemetry is the single place both the group path
//  (GameHub) and the solo path (UsageController) shape their custom-event
//  properties, so asserting the shape here asserts the no-PII guarantee for every
//  usage call site at once (mirrors errorBeacon.test.ts's intent server-side).
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Telemetry;

namespace QuibbleStone.Api.Tests;

public class UsageTelemetryTests
{
    // The ONLY property keys a usage event may ever carry (AC-04). Anything outside
    // this set would be a leak; the tests below assert the builders never exceed it.
    private static readonly HashSet<string> AllowedKeys = new()
    {
        UsageTelemetry.ModeProperty,
        UsageTelemetry.ContextProperty,
        UsageTelemetry.PlayerCountProperty,
        UsageTelemetry.DeviceIdProperty,
    };

    // Identity / content keys that must NEVER appear on a usage event.
    private static readonly string[] ForbiddenKeys =
    {
        "nickname", "name", "displayName", "code", "joinCode", "roomCode",
        "playerId", "sessionId", "connectionId", "word", "answer", "story", "ip",
    };

    [Fact]
    public void Group_round_properties_carry_only_mode_context_and_count()
    {
        var props = UsageTelemetry.BuildProperties("classic-blind", UsageTelemetry.GroupContext, playerCount: 3);

        Assert.Equal("classic-blind", props[UsageTelemetry.ModeProperty]);
        Assert.Equal("group", props[UsageTelemetry.ContextProperty]);
        Assert.Equal("3", props[UsageTelemetry.PlayerCountProperty]);

        // No device id on a group event, and no key outside the allowed anonymous set.
        Assert.False(props.ContainsKey(UsageTelemetry.DeviceIdProperty));
        Assert.All(props.Keys, key => Assert.Contains(key, AllowedKeys));
    }

    [Fact]
    public void Solo_round_properties_carry_only_mode_context_and_the_anonymous_device_id()
    {
        var props = UsageTelemetry.BuildProperties("word-bank", UsageTelemetry.SoloContext, deviceId: "device-abc");

        Assert.Equal("word-bank", props[UsageTelemetry.ModeProperty]);
        Assert.Equal("solo", props[UsageTelemetry.ContextProperty]);
        Assert.Equal("device-abc", props[UsageTelemetry.DeviceIdProperty]);
        Assert.False(props.ContainsKey(UsageTelemetry.PlayerCountProperty));
        Assert.All(props.Keys, key => Assert.Contains(key, AllowedKeys));
    }

    [Fact]
    public void No_usage_property_bag_ever_contains_a_pii_key()
    {
        // Try both shapes with values that could tempt a leak; assert the forbidden
        // keys never appear regardless of inputs.
        var group = UsageTelemetry.BuildProperties("classic-blind", UsageTelemetry.GroupContext, playerCount: 5);
        var solo = UsageTelemetry.BuildProperties("progressive-story", UsageTelemetry.SoloContext, deviceId: "d1");

        foreach (var props in new[] { group, solo })
        {
            foreach (var forbidden in ForbiddenKeys)
            {
                Assert.DoesNotContain(forbidden, props.Keys);
            }
        }
    }

    [Fact]
    public void A_blank_device_id_is_omitted_rather_than_recorded_empty()
    {
        var props = UsageTelemetry.BuildProperties("classic-blind", UsageTelemetry.SoloContext, deviceId: "   ");
        Assert.False(props.ContainsKey(UsageTelemetry.DeviceIdProperty));
    }

    [Theory]
    [InlineData("classic-blind", "classic-blind")]
    [InlineData("word-bank", "word-bank")]
    [InlineData("progressive-story", "progressive-story")]
    [InlineData("progressive-reveal", "progressive-reveal")]
    [InlineData("CLASSIC-BLIND", "classic-blind")]
    public void NormalizeMode_keeps_known_ids_lowercased(string input, string expected)
    {
        Assert.Equal(expected, UsageTelemetry.NormalizeMode(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nickname: sam")]
    [InlineData("some-invented-mode")]
    public void NormalizeMode_collapses_unknown_or_free_text_to_unknown(string? input)
    {
        // A crafted client cannot ride arbitrary free text into the mode metric.
        Assert.Equal(UsageTelemetry.UnknownMode, UsageTelemetry.NormalizeMode(input));
    }

    [Fact]
    public void Duration_metric_is_clamped_to_non_negative()
    {
        Assert.Equal(0, UsageTelemetry.BuildDurationMetric(-100)[UsageTelemetry.DurationMsMetric]);
        Assert.Equal(4200, UsageTelemetry.BuildDurationMetric(4200)[UsageTelemetry.DurationMsMetric]);
    }
}
