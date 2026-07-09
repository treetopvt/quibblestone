// ----------------------------------------------------------------------------
//  TestSystemFlags - a shared fixture helper for the system-scope flag evaluator
//  (control-plane/02, #213). StoredValueEntitlementService's constructor gained a
//  SystemFlagEvaluator; most tests only care that the system filter is a NO-OP (every
//  capability's infrastructure configured, every *.enabled flag at its true default),
//  so they can keep asserting the pre-story baseline + grant behavior unchanged.
//
//  Tests that DRIVE the kill switch (StoredValueEntitlementServiceTests,
//  SystemFlagEffectiveValueTests) build their own evaluator with an explicit presence /
//  override instead of using this helper.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Entitlements;
using QuibbleStone.Api.Settings;

namespace QuibbleStone.Api.Tests;

internal static class TestSystemFlags
{
    /// <summary>
    /// A <see cref="SystemFlagEvaluator"/> where every capability's infrastructure is configured and
    /// no override is applied - so the system-scope filter never subtracts anything and the
    /// baseline + grant composition reads exactly as it did before control-plane/02.
    /// </summary>
    public static SystemFlagEvaluator AllEnabled() => new(
        new RuntimeSettingsService(new InMemoryRuntimeSettingsStore()),
        new SystemConfigPresence(AiConfigured: true, PublishingConfigured: true, EmailConfigured: true));
}
