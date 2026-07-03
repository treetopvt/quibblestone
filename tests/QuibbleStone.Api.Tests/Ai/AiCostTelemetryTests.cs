// ----------------------------------------------------------------------------
//  AiCostTelemetryTests - proves the AI cost-attribution builder carries ONLY the
//  anonymous shape (feature / model / instanceId / hot + token/cost metrics) and
//  that every one of its keys SURVIVES the single PII scrubber
//  (PiiScrubbingTelemetryInitializer) - i.e. none of them is in the scrubber's
//  sensitive-key set (ai-cost-gate/04, #123, AC-05/AC-06). Asserting the shape here
//  asserts the no-PII guarantee for every attribution call site at once (mirrors
//  UsageTelemetryTests's intent).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.ApplicationInsights.DataContracts;
using QuibbleStone.Api.Telemetry;

namespace QuibbleStone.Api.Tests.Ai;

public class AiCostTelemetryTests
{
    // The ONLY property keys an attribution event may ever carry (AC-06). Anything
    // outside this set would be a leak.
    private static readonly HashSet<string> AllowedPropertyKeys = new()
    {
        AiCostTelemetry.FeatureProperty,
        AiCostTelemetry.ModelProperty,
        AiCostTelemetry.InstanceIdProperty,
        AiCostTelemetry.HotProperty,
    };

    // Identity / content keys that must NEVER appear on an attribution event.
    private static readonly string[] ForbiddenKeys =
    {
        "nickname", "name", "displayName", "code", "joinCode", "roomCode",
        "playerId", "sessionId", "connectionId", "word", "answer", "story", "ip",
    };

    [Fact]
    public void Properties_carry_only_feature_model_instance_and_hot()
    {
        var props = AiCostTelemetry.BuildProperties("jumble", "gpt-4o-mini", "room-abc", hot: false);

        Assert.Equal("jumble", props[AiCostTelemetry.FeatureProperty]);
        Assert.Equal("gpt-4o-mini", props[AiCostTelemetry.ModelProperty]);
        Assert.Equal("room-abc", props[AiCostTelemetry.InstanceIdProperty]);
        Assert.Equal("false", props[AiCostTelemetry.HotProperty]);
        Assert.All(props.Keys, key => Assert.Contains(key, AllowedPropertyKeys));
    }

    [Fact]
    public void The_hot_flag_is_a_queryable_true_false_string()
    {
        Assert.Equal("true", AiCostTelemetry.BuildProperties("jumble", "gpt-4o-mini", "r", hot: true)[AiCostTelemetry.HotProperty]);
        Assert.Equal("false", AiCostTelemetry.BuildProperties("jumble", "gpt-4o-mini", "r", hot: false)[AiCostTelemetry.HotProperty]);
    }

    [Fact]
    public void A_blank_instance_id_is_omitted_rather_than_recorded_empty()
    {
        var props = AiCostTelemetry.BuildProperties("jumble", "gpt-4o-mini", "   ", hot: false);
        Assert.False(props.ContainsKey(AiCostTelemetry.InstanceIdProperty));
    }

    [Fact]
    public void Metrics_carry_token_counts_and_estimated_cost()
    {
        var metrics = AiCostTelemetry.BuildMetrics(400, 30, 0.000078m);

        Assert.Equal(400d, metrics[AiCostTelemetry.InputTokensMetric]);
        Assert.Equal(30d, metrics[AiCostTelemetry.OutputTokensMetric]);
        Assert.Equal(0.000078d, metrics[AiCostTelemetry.EstCostUsdMetric], 12);
    }

    [Fact]
    public void Negative_token_counts_are_clamped_in_the_metric_bag()
    {
        var metrics = AiCostTelemetry.BuildMetrics(-1, -1, 0m);
        Assert.Equal(0d, metrics[AiCostTelemetry.InputTokensMetric]);
        Assert.Equal(0d, metrics[AiCostTelemetry.OutputTokensMetric]);
    }

    [Fact]
    public void No_property_key_is_a_pii_key()
    {
        var props = AiCostTelemetry.BuildProperties("jumble", "gpt-4o-mini", "room-abc", hot: true);
        foreach (var forbidden in ForbiddenKeys)
        {
            Assert.DoesNotContain(forbidden, props.Keys);
        }
    }

    [Fact]
    public void Every_attribution_key_survives_the_single_pii_scrubber()
    {
        // AC-06: the attribution event flows through PiiScrubbingTelemetryInitializer
        // like every App Insights item. Build the real event, run the real scrubber,
        // and assert nothing anonymous was dropped - i.e. none of our keys collides
        // with the scrubber's sensitive-key set (instanceId is NOT sessionId).
        var evt = new EventTelemetry(AiCostTelemetry.AiCallEvent);
        foreach (var (key, value) in AiCostTelemetry.BuildProperties("jumble", "gpt-4o-mini", "room-abc", hot: true))
        {
            evt.Properties[key] = value;
        }

        new PiiScrubbingTelemetryInitializer().Initialize(evt);

        Assert.Equal("jumble", evt.Properties[AiCostTelemetry.FeatureProperty]);
        Assert.Equal("gpt-4o-mini", evt.Properties[AiCostTelemetry.ModelProperty]);
        Assert.Equal("room-abc", evt.Properties[AiCostTelemetry.InstanceIdProperty]);
        Assert.Equal("true", evt.Properties[AiCostTelemetry.HotProperty]);
    }
}
