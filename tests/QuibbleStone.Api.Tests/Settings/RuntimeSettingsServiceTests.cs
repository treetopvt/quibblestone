// ----------------------------------------------------------------------------
//  RuntimeSettingsServiceTests - the runtime settings service over the working
//  in-memory store (control-plane/01, issue #197). Exercises the composition of the
//  static catalog (defaults) + the override store, with ZERO Azure (AC-05). Covers:
//  default read with no override (AC-01), an override read after a PUT (AC-02), the
//  changed-by/at stamp present on an override and absent on a never-overridden key
//  (AC-03), a DELETE reverting to the code default and dropping from GET-all (AC-04),
//  and the degrade-to-default posture for a type-mismatched getter (a coding-bug guard).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Settings;

namespace QuibbleStone.Api.Tests.Settings;

public sealed class RuntimeSettingsServiceTests
{
    private const string Operator = "ops@quibblestone.com";

    // A fresh service over the working in-memory store (AC-05) - identical behavior to the
    // Table-backed store, only durability across a restart differs.
    private static RuntimeSettingsService NewSut() => new(new InMemoryRuntimeSettingsStore());

    // ---- AC-01: default read when no override was ever written -------------------

    [Fact]
    public async Task Reads_the_code_default_when_no_override_exists()
    {
        var sut = NewSut();

        // Every typed getter returns the catalog's code default for a never-overridden key.
        Assert.Equal(3, await sut.GetIntAsync(SettingsCatalog.ExampleThreshold));
        Assert.Equal(1.5m, await sut.GetDecimalAsync(SettingsCatalog.ExampleRate));
        Assert.True(await sut.GetBoolAsync(SettingsCatalog.ExampleEnabled));
        Assert.Equal("hello", await sut.GetStringAsync(SettingsCatalog.ExampleLabel));
    }

    [Fact]
    public async Task GetAll_lists_every_catalog_key_with_no_stamp_when_never_overridden()
    {
        var sut = NewSut();

        var all = await sut.GetAllAsync();

        Assert.Equal(SettingsCatalog.All.Count, all.Count);
        var threshold = all.Single(v => v.Key == SettingsCatalog.ExampleThreshold);
        Assert.Null(threshold.Override); // AC-03: no override -> no stamp
        Assert.Equal(3, threshold.EffectiveValue);
        Assert.Equal(3, threshold.CodeDefault);
    }

    // ---- AC-02: an override is visible after a PUT -------------------------------

    [Fact]
    public async Task Override_is_visible_immediately_on_the_writing_node()
    {
        var sut = NewSut();

        // The cache resets on the write-through, so the flipping node sees its own change at once
        // (no waiting for the short TTL) - AC-02's "immediately on the node that wrote it".
        await sut.SetOverrideAsync(SettingsCatalog.ExampleThreshold, "7", Operator, DateTimeOffset.UtcNow);

        Assert.Equal(7, await sut.GetIntAsync(SettingsCatalog.ExampleThreshold));
    }

    [Fact]
    public async Task Override_is_visible_even_after_the_read_cache_warmed_on_the_default()
    {
        var sut = NewSut();

        // Warm the cache on the default first, THEN write - the write-through reset means the new
        // value is not masked by a still-warm cache entry (AC-02).
        Assert.Equal(3, await sut.GetIntAsync(SettingsCatalog.ExampleThreshold));
        await sut.SetOverrideAsync(SettingsCatalog.ExampleThreshold, "42", Operator, DateTimeOffset.UtcNow);

        Assert.Equal(42, await sut.GetIntAsync(SettingsCatalog.ExampleThreshold));
    }

    // ---- AC-03: the changed-by/at stamp -----------------------------------------

    [Fact]
    public async Task Override_carries_a_changedBy_and_changedAt_stamp()
    {
        var sut = NewSut();
        var at = DateTimeOffset.UtcNow;

        await sut.SetOverrideAsync(SettingsCatalog.ExampleThreshold, "9", Operator, at);
        var view = await sut.GetViewAsync(SettingsCatalog.ExampleThreshold);

        Assert.NotNull(view);
        Assert.NotNull(view!.Override);
        Assert.Equal(9, view.Override!.Value);
        Assert.Equal(Operator, view.Override.ChangedBy);
        Assert.Equal(at, view.Override.ChangedAtUtc);
    }

    // ---- AC-04: DELETE reverts to the default and drops from GET-all -------------

    [Fact]
    public async Task Delete_reverts_to_the_code_default_and_omits_the_override()
    {
        var sut = NewSut();
        await sut.SetOverrideAsync(SettingsCatalog.ExampleThreshold, "50", Operator, DateTimeOffset.UtcNow);
        Assert.Equal(50, await sut.GetIntAsync(SettingsCatalog.ExampleThreshold));

        var cleared = await sut.DeleteOverrideAsync(SettingsCatalog.ExampleThreshold, Operator, DateTimeOffset.UtcNow);

        Assert.True(cleared); // there WAS an override to clear
        Assert.Equal(3, await sut.GetIntAsync(SettingsCatalog.ExampleThreshold)); // back to the code default
        var view = await sut.GetViewAsync(SettingsCatalog.ExampleThreshold);
        Assert.Null(view!.Override); // GET-all no longer lists an override
    }

    [Fact]
    public async Task Delete_of_a_key_with_no_override_is_a_no_op()
    {
        var sut = NewSut();

        // No override was ever written - a clear reports false so the controller skips the log row.
        var cleared = await sut.DeleteOverrideAsync(SettingsCatalog.ExampleThreshold, Operator, DateTimeOffset.UtcNow);

        Assert.False(cleared);
    }

    // ---- AC-07: an unparseable stored override degrades to the code default ------

    [Fact]
    public async Task An_unparseable_stored_override_degrades_to_the_code_default()
    {
        var store = new InMemoryRuntimeSettingsStore();
        // Simulate schema drift / a hand-edited row: a non-integer value under an Int key.
        await store.SetOverrideAsync(SettingsCatalog.ExampleThreshold, "not-a-number", Operator, DateTimeOffset.UtcNow);
        var sut = new RuntimeSettingsService(store);

        // The getter degrades to the code default rather than throwing (AC-07), and the view shows
        // no stamp (it never surfaces a value it cannot type).
        Assert.Equal(3, await sut.GetIntAsync(SettingsCatalog.ExampleThreshold));
        var view = await sut.GetViewAsync(SettingsCatalog.ExampleThreshold);
        Assert.Null(view!.Override);
        Assert.Equal(3, view.EffectiveValue);
    }

    // ---- Coding-bug guards: the catalog is the single source of truth -----------

    [Fact]
    public async Task A_type_mismatched_getter_throws()
    {
        var sut = NewSut();

        // ExampleThreshold is an Int key - asking for it as a Bool is a coding bug, not a runtime
        // branch. It throws rather than silently coercing.
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await sut.GetBoolAsync(SettingsCatalog.ExampleThreshold));
    }

    [Fact]
    public async Task An_unknown_key_throws()
    {
        var sut = NewSut();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await sut.GetIntAsync("no.such.key"));
    }
}
