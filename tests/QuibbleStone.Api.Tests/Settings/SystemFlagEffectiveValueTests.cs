// ----------------------------------------------------------------------------
//  SystemFlagEffectiveValueTests - the EFFECTIVE value of a system-scope flag
//  (control-plane/02, issue #213, AC-01). The effective value a session evaluates /
//  an operator sees is (the *.enabled settings flag) AND (its config-presence floor).
//
//  The binding rail (ADR 0003 Layer 1, "the control plane cannot disable its own
//  safety rails"): a settings override can force a CONFIGURED capability OFF (a kill
//  switch) but can NEVER enable one whose underlying infrastructure is not configured
//  - config-presence is the floor. These pin every combination of the two inputs for
//  each of the three registered system keys (ai / publishing / email), driven through
//  the REAL RuntimeSettingsService over the in-memory store (no mocking framework,
//  matching the harness).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Entitlements;
using QuibbleStone.Api.Settings;

namespace QuibbleStone.Api.Tests.Settings;

public sealed class SystemFlagEffectiveValueTests
{
    private const string Operator = "ops@quibblestone.com";

    // An evaluator over the real settings service (in-memory store) with a chosen
    // config-presence floor. Overrides, when a test needs one, are written through the
    // returned store first.
    private static (SystemFlagEvaluator Evaluator, RuntimeSettingsService Settings) Build(
        bool aiConfigured, bool publishingConfigured, bool emailConfigured)
    {
        var settings = new RuntimeSettingsService(new InMemoryRuntimeSettingsStore());
        var evaluator = new SystemFlagEvaluator(
            settings,
            new SystemConfigPresence(aiConfigured, publishingConfigured, emailConfigured));
        return (evaluator, settings);
    }

    // Configured + code default (true, no override) -> effectively enabled. This is the
    // shipped state for a fully configured deployment: zero observed behavior change.
    [Theory]
    [InlineData(SettingsCatalog.AiEnabled)]
    [InlineData(SettingsCatalog.PublishingEnabled)]
    [InlineData(SettingsCatalog.EmailEnabled)]
    public async Task Configured_and_default_flag_is_effectively_enabled(string key)
    {
        var (evaluator, _) = Build(aiConfigured: true, publishingConfigured: true, emailConfigured: true);

        Assert.True(await evaluator.IsEffectivelyEnabledAsync(key));
    }

    // Configured + operator forces the flag OFF -> effectively disabled. The kill switch
    // works on a configured capability (this is the whole point of the system scope).
    [Theory]
    [InlineData(SettingsCatalog.AiEnabled)]
    [InlineData(SettingsCatalog.PublishingEnabled)]
    [InlineData(SettingsCatalog.EmailEnabled)]
    public async Task Configured_but_flag_forced_off_is_effectively_disabled(string key)
    {
        var (evaluator, settings) = Build(aiConfigured: true, publishingConfigured: true, emailConfigured: true);
        await settings.SetOverrideAsync(key, "false", Operator, DateTimeOffset.UtcNow);

        Assert.False(await evaluator.IsEffectivelyEnabledAsync(key));
    }

    // NOT configured + flag left at its true default -> STILL effectively disabled. The
    // config-presence floor holds: a default-true flag can never assert an unbuilt capability.
    [Theory]
    [InlineData(SettingsCatalog.AiEnabled)]
    [InlineData(SettingsCatalog.PublishingEnabled)]
    [InlineData(SettingsCatalog.EmailEnabled)]
    public async Task Unconfigured_with_default_flag_is_effectively_disabled(string key)
    {
        var (evaluator, _) = Build(aiConfigured: false, publishingConfigured: false, emailConfigured: false);

        Assert.False(await evaluator.IsEffectivelyEnabledAsync(key));
    }

    // NOT configured + operator explicitly sets the flag TRUE -> STILL effectively disabled.
    // The floor is never "true when the underlying infrastructure is not configured" (AC-05):
    // a settings override cannot enable an unconfigured capability.
    [Theory]
    [InlineData(SettingsCatalog.AiEnabled)]
    [InlineData(SettingsCatalog.PublishingEnabled)]
    [InlineData(SettingsCatalog.EmailEnabled)]
    public async Task Unconfigured_even_with_flag_forced_true_is_effectively_disabled(string key)
    {
        var (evaluator, settings) = Build(aiConfigured: false, publishingConfigured: false, emailConfigured: false);
        await settings.SetOverrideAsync(key, "true", Operator, DateTimeOffset.UtcNow);

        Assert.False(await evaluator.IsEffectivelyEnabledAsync(key));
    }

    // Each key ANDs against its OWN config-presence field, not another's. AI configured but
    // publishing / email not -> only ai.enabled is effectively enabled.
    [Fact]
    public async Task Each_key_reads_its_own_config_presence_field()
    {
        var (evaluator, _) = Build(aiConfigured: true, publishingConfigured: false, emailConfigured: false);

        Assert.True(await evaluator.IsEffectivelyEnabledAsync(SettingsCatalog.AiEnabled));
        Assert.False(await evaluator.IsEffectivelyEnabledAsync(SettingsCatalog.PublishingEnabled));
        Assert.False(await evaluator.IsEffectivelyEnabledAsync(SettingsCatalog.EmailEnabled));
    }

    // All three system keys are registered in the catalog as confirmation-gated Bool keys
    // with a true code default (the shape AC-01 asserts before any override is applied).
    [Theory]
    [InlineData(SettingsCatalog.AiEnabled)]
    [InlineData(SettingsCatalog.PublishingEnabled)]
    [InlineData(SettingsCatalog.EmailEnabled)]
    public void System_keys_are_registered_confirmation_gated_bool_defaults(string key)
    {
        var def = SettingsCatalog.TryGet(key);

        Assert.NotNull(def);
        Assert.Equal(SettingType.Bool, def!.Type);
        Assert.Equal(true, def.CodeDefault);
        Assert.True(def.RequiresConfirmation);
    }
}
