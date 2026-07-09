// ----------------------------------------------------------------------------
//  KnobMigrationRegressionTests - control-plane/03 (#232) AC-01 / AC-07: the seven
//  hardcoded operational knobs migrated onto runtime settings keys must each have a
//  CATALOG CODE DEFAULT that is bit-for-bit the FORMER hardcoded constant, and reading
//  each key through the in-memory-fallback path (TestRuntimeSettings.Defaults, zero
//  storage configured) must resolve to that same default (AC-07: the "no Azure setup"
//  posture). A fresh clone with no override behaves exactly as it did before this
//  story (AC-01).
//
//  Also pins the SHAPE guarantee every numeric knob here must carry (ADR 0003 "cannot
//  disable its own safety rails"): a Bounds is present, and the AI spend ceiling - the
//  one spend rail among the seven - is confirmation-gated.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Ai;
using QuibbleStone.Api.Admin;
using QuibbleStone.Api.PublishedTales;
using QuibbleStone.Api.Rooms;
using QuibbleStone.Api.Settings;

namespace QuibbleStone.Api.Tests.Settings;

public sealed class KnobMigrationRegressionTests
{
    // ---- AC-01 / AC-07: catalog code default == the former hardcoded constant ----

    [Fact]
    public async Task Moderation_auto_hide_threshold_default_matches_the_former_constant()
    {
        var settings = TestRuntimeSettings.Defaults();

        var effective = await settings.GetIntAsync(SettingsCatalog.ModerationTaleAutoHideThreshold);

        Assert.Equal(PublishedTalesController.AutoHideThreshold, effective);
        Assert.Equal(3, effective);
    }

    [Fact]
    public async Task Ai_per_ip_rate_limit_default_matches_the_former_program_literal()
    {
        // The per-IP permit default was only ever a Program.cs literal (30) - there is no
        // surviving named constant to compare against, so this pins the literal itself.
        var settings = TestRuntimeSettings.Defaults();

        var effective = await settings.GetIntAsync(SettingsCatalog.AiRateLimitPerIpPermitPerMinute);

        Assert.Equal(30, effective);
    }

    [Fact]
    public async Task Ai_quota_per_session_default_matches_AiOptions_QuotaPerSession()
    {
        var settings = TestRuntimeSettings.Defaults();

        var effective = await settings.GetIntAsync(SettingsCatalog.AiQuotaPerSession);

        Assert.Equal(new AiOptions().QuotaPerSession, effective);
        Assert.Equal(20, effective);
    }

    [Fact]
    public async Task Ai_spend_monthly_ceiling_default_matches_AiOptions_MonthlyCeilingUsd()
    {
        var settings = TestRuntimeSettings.Defaults();

        var effective = await settings.GetDecimalAsync(SettingsCatalog.AiSpendMonthlyCeilingUsd);

        Assert.Equal(new AiOptions().MonthlyCeilingUsd, effective);
        Assert.Equal(20m, effective);
    }

    [Fact]
    public async Task Seat_grace_window_default_matches_SeatGraceService_DefaultGraceWindowSeconds()
    {
        var settings = TestRuntimeSettings.Defaults();

        var effective = await settings.GetIntAsync(SettingsCatalog.SessionSeatGraceWindowSeconds);

        Assert.Equal(SeatGraceService.DefaultGraceWindowSeconds, effective);
        Assert.Equal(180, effective);
    }

    [Fact]
    public async Task Tales_ttl_days_default_matches_PublishedTalesController_TaleTtlDays()
    {
        var settings = TestRuntimeSettings.Defaults();

        var effective = await settings.GetIntAsync(SettingsCatalog.TalesTtlDays);

        Assert.Equal(PublishedTalesController.TaleTtlDays, effective);
        Assert.Equal(30, effective);
    }

    [Fact]
    public async Task Operator_login_rate_limit_default_matches_OperatorLoginRateLimit_PermitLimit()
    {
        var settings = TestRuntimeSettings.Defaults();

        var effective = await settings.GetIntAsync(SettingsCatalog.AdminOperatorLoginRateLimitPermitPerMinute);

        Assert.Equal(OperatorLoginRateLimit.PermitLimit, effective);
        Assert.Equal(5, effective);
    }

    // ---- Catalog shape: every numeric knob is bounded; the spend rail is gated ----

    [Theory]
    [InlineData(SettingsCatalog.ModerationTaleAutoHideThreshold)]
    [InlineData(SettingsCatalog.AiRateLimitPerIpPermitPerMinute)]
    [InlineData(SettingsCatalog.AiQuotaPerSession)]
    [InlineData(SettingsCatalog.AiSpendMonthlyCeilingUsd)]
    [InlineData(SettingsCatalog.SessionSeatGraceWindowSeconds)]
    [InlineData(SettingsCatalog.TalesTtlDays)]
    [InlineData(SettingsCatalog.AdminOperatorLoginRateLimitPermitPerMinute)]
    public void Every_migrated_numeric_knob_carries_bounds(string key)
    {
        var definition = SettingsCatalog.TryGet(key);

        Assert.NotNull(definition);
        Assert.NotNull(definition!.Bounds);
    }

    [Fact]
    public void Ai_spend_monthly_ceiling_requires_confirmation()
    {
        // The AI spend ceiling is the one true spend rail among the seven - never an
        // accidental one-field PUT (ADR 0003).
        var definition = SettingsCatalog.TryGet(SettingsCatalog.AiSpendMonthlyCeilingUsd);

        Assert.NotNull(definition);
        Assert.True(definition!.RequiresConfirmation);
    }

    [Theory]
    [InlineData(SettingsCatalog.ModerationTaleAutoHideThreshold)]
    [InlineData(SettingsCatalog.AiRateLimitPerIpPermitPerMinute)]
    [InlineData(SettingsCatalog.AiQuotaPerSession)]
    [InlineData(SettingsCatalog.SessionSeatGraceWindowSeconds)]
    [InlineData(SettingsCatalog.TalesTtlDays)]
    [InlineData(SettingsCatalog.AdminOperatorLoginRateLimitPermitPerMinute)]
    public void The_other_six_migrated_knobs_do_not_require_confirmation(string key)
    {
        // Only the spend ceiling is confirmation-gated - the other six are ordinary
        // operator tuning knobs, not safety-rail kill switches.
        var definition = SettingsCatalog.TryGet(key);

        Assert.NotNull(definition);
        Assert.False(definition!.RequiresConfirmation);
    }
}
