// ----------------------------------------------------------------------------
//  TestRuntimeSettings - a shared fixture helper for the runtime settings service
//  (control-plane/01 #197, extended by control-plane/03 #232's knob migration).
//
//  Story 03 moved seven hardcoded knobs onto settings keys read live through
//  IRuntimeSettingsService, so the services that read them (AiQuota, AiSpendBreaker,
//  SeatGraceService, PublishedTalesController) now take one in their constructor. Most
//  tests just want the CODE DEFAULTS (no override -> identical to before), so they use
//  <see cref="Defaults"/>; a knob-override test seeds one key with <see cref="With"/>.
//
//  Built over the REAL RuntimeSettingsService + InMemoryRuntimeSettingsStore (no mocking
//  framework, mirroring TestSystemFlags) - the exact in-memory fallback path story 01
//  AC-05 / story 03 AC-07 promise runs with zero storage configured.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Settings;

namespace QuibbleStone.Api.Tests;

internal static class TestRuntimeSettings
{
    /// <summary>
    /// A settings service over an empty in-memory store: every key resolves to its code
    /// default, so a consumer reads exactly the value it did before control-plane/03.
    /// </summary>
    public static RuntimeSettingsService Defaults() =>
        new(new InMemoryRuntimeSettingsStore());

    /// <summary>
    /// A settings service with a single integer override already stored (the operator-flip
    /// path, AC-02/03/05): the given key reads <paramref name="value"/>, every other key its
    /// code default. The store is the in-memory fallback (no Azure setup, AC-07).
    /// </summary>
    public static RuntimeSettingsService WithInt(string key, int value) =>
        Seed(key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>
    /// A settings service with a single decimal override already stored (e.g. the AI spend
    /// ceiling). The given key reads <paramref name="value"/>, every other key its default.
    /// </summary>
    public static RuntimeSettingsService WithDecimal(string key, decimal value) =>
        Seed(key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    // Seeds one override row (wire-string form) directly on the store, stamped by a test
    // operator, then hands back the service composed over it.
    private static RuntimeSettingsService Seed(string key, string wireValue)
    {
        var store = new InMemoryRuntimeSettingsStore();
        store.SetOverrideAsync(key, wireValue, "test-operator", DateTimeOffset.UtcNow)
            .GetAwaiter().GetResult();
        return new RuntimeSettingsService(store);
    }
}
