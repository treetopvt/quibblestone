// ----------------------------------------------------------------------------
//  AiSpendBreakerTests - the REAL enforcer's behaviour (ai-cost-gate/04, #123):
//  the breaker opens at 100% of the ceiling, resets on a new UTC month, fails to
//  the SAFE side when the total is unreadable, records the estimate into the
//  persisted total, and never faults the call on a failed write (AC-02/03/09). Also
//  proves EXACTLY ONE anonymous attribution event per call with the expected keys
//  and the hot concentration flag (AC-05/AC-08).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Extensions.Logging.Abstractions;
using QuibbleStone.Api.Ai;
using QuibbleStone.Api.Settings;
using QuibbleStone.Api.Telemetry;

namespace QuibbleStone.Api.Tests.Ai;

public class AiSpendBreakerTests
{
    // A small test ceiling keeps the arithmetic obvious. control-plane/03 (#232) moved
    // the ceiling onto the `ai.spend.monthlyCeilingUsd` settings key read live, so the
    // Build helper seeds that key (not AiOptions.MonthlyCeilingUsd) with this value.
    private const decimal TestCeiling = 10m;

    private static readonly DateTimeOffset July = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
    private const string JulyKey = "2026-07";
    private static readonly DateTimeOffset August = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
    private const string AugustKey = "2026-08";

    private static AiOptions Options() => new()
    {
        Deployment = "gpt-4o-mini",
        InputCostPerMillion = 0.15m,
        OutputCostPerMillion = 0.60m,
        MonthlyCeilingUsd = TestCeiling,
        HotSessionThresholdUsd = 1m,
    };

    private static AiSpendBreaker Build(
        FakeMonthlySpendStore store,
        TimeProvider clock,
        Microsoft.ApplicationInsights.TelemetryClient? telemetry = null,
        IRuntimeSettingsService? settings = null) =>
        new(
            store,
            Options(),
            // control-plane/03 (#232): the ceiling is read live from settings now - seed
            // the key with TestCeiling so the existing arithmetic holds unless a test
            // passes its own service (e.g. to prove an override takes effect).
            settings ?? TestRuntimeSettings.WithDecimal(SettingsCatalog.AiSpendMonthlyCeilingUsd, TestCeiling),
            telemetry ?? TestTelemetry.NoOp,
            NullLogger<AiSpendBreaker>.Instance,
            clock);

    [Fact]
    public async Task Under_the_ceiling_the_breaker_is_closed()
    {
        var store = new FakeMonthlySpendStore().Seed(JulyKey, 5m);
        var breaker = Build(store, new FixedTimeProvider(July));

        Assert.True(await breaker.IsUnderCeilingAsync());
    }

    [Fact]
    public async Task At_one_hundred_percent_of_the_ceiling_the_breaker_opens()
    {
        // >= 100% opens the breaker (AC-03): exactly at the ceiling counts as reached.
        var store = new FakeMonthlySpendStore().Seed(JulyKey, TestCeiling);
        var breaker = Build(store, new FixedTimeProvider(July));

        Assert.False(await breaker.IsUnderCeilingAsync());
    }

    [Fact]
    public async Task Over_the_ceiling_the_breaker_stays_open()
    {
        var store = new FakeMonthlySpendStore().Seed(JulyKey, TestCeiling + 4m);
        var breaker = Build(store, new FixedTimeProvider(July));

        Assert.False(await breaker.IsUnderCeilingAsync());
    }

    [Fact]
    public async Task An_overridden_lower_ceiling_opens_the_breaker_where_the_default_would_stay_closed()
    {
        // control-plane/03 (#232) AC-05: the ceiling is read live from the settings
        // service, not the captured AiOptions.MonthlyCeilingUsd. Spend of 8 sits UNDER
        // Build()'s default TestCeiling (10) - the breaker would stay CLOSED with no
        // override. A settings override of 5 (below the 8 already spent) governs
        // instead, opening the breaker - proving the override, not AiOptions, wins.
        var store = new FakeMonthlySpendStore().Seed(JulyKey, 8m);
        var defaultCeilingBreaker = Build(store, new FixedTimeProvider(July));
        Assert.True(await defaultCeilingBreaker.IsUnderCeilingAsync()); // sanity: default stays closed

        var lowCeilingSettings = TestRuntimeSettings.WithDecimal(SettingsCatalog.AiSpendMonthlyCeilingUsd, 5m);
        var overriddenBreaker = Build(store, new FixedTimeProvider(July), settings: lowCeilingSettings);

        Assert.False(await overriddenBreaker.IsUnderCeilingAsync());
    }

    [Fact]
    public async Task A_new_utc_month_resets_the_breaker()
    {
        // July is over the ceiling (breaker open); rolling to August (a fresh row key,
        // nothing spent) closes it and AI resumes - no sweep job (AC-03).
        var store = new FakeMonthlySpendStore().Seed(JulyKey, TestCeiling + 5m);
        var clock = new FixedTimeProvider(July);
        var breaker = Build(store, clock);

        Assert.False(await breaker.IsUnderCeilingAsync());

        clock.Set(August);
        Assert.True(await breaker.IsUnderCeilingAsync());
        Assert.Equal(0m, store.TotalFor(AugustKey));
    }

    [Fact]
    public async Task An_unreadable_total_fails_to_the_safe_side()
    {
        // AC-09: if the running total cannot be read, treat spend as at-ceiling and
        // degrade rather than call AI blind.
        var store = new FakeMonthlySpendStore { Unreadable = true };
        var breaker = Build(store, new FixedTimeProvider(July));

        Assert.False(await breaker.IsUnderCeilingAsync());
    }

    [Fact]
    public async Task Record_accrues_the_estimate_into_the_persisted_month_total()
    {
        var store = new FakeMonthlySpendStore();
        var breaker = Build(store, new FixedTimeProvider(July));

        // (1_000_000 * 0.15 + 0) / 1e6 = 0.15
        var result = new AiCompletionResult("moss", InputTokens: 1_000_000, OutputTokens: 0, ModelId: "gpt-4o-mini", IsAvailable: true);
        await breaker.RecordAsync(result, feature: "jumble", instanceId: "room-abc");

        Assert.Equal(0.15m, store.TotalFor(JulyKey));
    }

    [Fact]
    public async Task A_failed_total_write_never_faults_the_call()
    {
        // AC-09: a Table write failure must never block or error the (already
        // successful) AI call - the breaker swallows it.
        var store = new FakeMonthlySpendStore { ThrowOnWrite = true };
        var breaker = Build(store, new FixedTimeProvider(July));

        var result = new AiCompletionResult("moss", InputTokens: 400, OutputTokens: 30, ModelId: "gpt-4o-mini", IsAvailable: true);

        // Must complete without throwing.
        await breaker.RecordAsync(result, feature: "jumble", instanceId: "room-abc");
    }

    [Fact]
    public async Task Record_emits_exactly_one_attribution_event_with_the_anonymous_shape()
    {
        var (client, channel) = RecordingTelemetryChannel.CreateClient();
        var store = new FakeMonthlySpendStore();
        var breaker = Build(store, new FixedTimeProvider(July), client);

        var result = new AiCompletionResult("moss\nember", InputTokens: 400, OutputTokens: 30, ModelId: "gpt-4o-mini", IsAvailable: true);
        await breaker.RecordAsync(result, feature: "jumble", instanceId: "room-abc");

        // EXACTLY ONE event (AC-05).
        var events = channel.Sent.OfType<EventTelemetry>().ToArray();
        var evt = Assert.Single(events);
        Assert.Equal(AiCostTelemetry.AiCallEvent, evt.Name);

        // Anonymous properties: feature + model + instanceId + hot (AC-05/AC-06).
        Assert.Equal("jumble", evt.Properties[AiCostTelemetry.FeatureProperty]);
        Assert.Equal("gpt-4o-mini", evt.Properties[AiCostTelemetry.ModelProperty]);
        Assert.Equal("room-abc", evt.Properties[AiCostTelemetry.InstanceIdProperty]);
        Assert.Equal("false", evt.Properties[AiCostTelemetry.HotProperty]);

        // Metrics: token counts + estimated cost (AC-05).
        Assert.Equal(400d, evt.Metrics[AiCostTelemetry.InputTokensMetric]);
        Assert.Equal(30d, evt.Metrics[AiCostTelemetry.OutputTokensMetric]);
        // (400 * 0.15 + 30 * 0.60) / 1e6 = 0.000078
        Assert.Equal(0.000078d, evt.Metrics[AiCostTelemetry.EstCostUsdMetric], 12);
    }

    [Fact]
    public async Task A_disproportionately_spending_session_is_flagged_hot()
    {
        // AC-08: concentration over the anonymous instanceId ONLY. One big call (1M
        // output tokens = $0.60) does not cross the $1 threshold; two do.
        var (client, channel) = RecordingTelemetryChannel.CreateClient();
        var store = new FakeMonthlySpendStore();
        var breaker = Build(store, new FixedTimeProvider(July), client);

        var bigCall = new AiCompletionResult("x", InputTokens: 0, OutputTokens: 1_000_000, ModelId: "gpt-4o-mini", IsAvailable: true);
        await breaker.RecordAsync(bigCall, feature: "jumble", instanceId: "hot-room");
        await breaker.RecordAsync(bigCall, feature: "jumble", instanceId: "hot-room");

        var events = channel.Sent.OfType<EventTelemetry>().ToArray();
        Assert.Equal(2, events.Length);
        Assert.Equal("false", events[0].Properties[AiCostTelemetry.HotProperty]); // 0.60 < 1.00
        Assert.Equal("true", events[1].Properties[AiCostTelemetry.HotProperty]);  // 1.20 >= 1.00
    }
}
