// ----------------------------------------------------------------------------
//  PublishedTalesKnobOverrideTests - control-plane/03 (#232) AC-02/AC-04: the two
//  PublishedTalesController knobs migrated onto settings keys - the report auto-hide
//  threshold and the tale TTL - each governed by an operator override, not the
//  former hardcoded constants (AutoHideThreshold = 3 / TaleTtlDays = 30). Mirrors the
//  existing ReportTaleTests / PublishedTalesControllerTests harness (the REAL
//  controller + REAL ContentSafetyFilter + FakePublishedTaleStore).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using QuibbleStone.Api.PublishedTales;
using QuibbleStone.Api.Safety;
using QuibbleStone.Api.Settings;

namespace QuibbleStone.Api.Tests;

public sealed class PublishedTalesKnobOverrideTests
{
    private static readonly IContentSafetyFilter Safety = new ContentSafetyFilter();

    private static IConfiguration Config() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PublishedTales:WebAppBaseUrl"] = "https://play.example.test",
            })
            .Build();

    private static PublishedTalesController NewController(IPublishedTaleStore store, IRuntimeSettingsService settings)
    {
        var controller = new PublishedTalesController(store, Safety, settings, Config())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };
        controller.HttpContext.Request.Scheme = "https";
        controller.HttpContext.Request.Host = new HostString("tales.example.test");
        return controller;
    }

    private static PublishTaleRequest CleanTale() => new(
        Title: "The space llama saga",
        Parts:
        [
            new PublishTalePartRequest(IsWord: false, Text: "Once upon a time a "),
            new PublishTalePartRequest(IsWord: true, Text: "banana"),
            new PublishTalePartRequest(IsWord: false, Text: " danced."),
        ],
        BylineNames: "Sam & Mia");

    // ---- AC-02: an overridden auto-hide threshold governs, not the constant 3 -----

    [Fact]
    public async Task An_overridden_lower_threshold_hides_the_tale_before_the_code_default_would()
    {
        var store = new FakePublishedTaleStore();
        var overriddenSettings = TestRuntimeSettings.WithInt(SettingsCatalog.ModerationTaleAutoHideThreshold, 1);
        var controller = NewController(store, overriddenSettings);
        await controller.Publish(CleanTale(), CancellationToken.None);
        var slug = store.Tales.Single().Slug;

        // A single report hides the tale under the override of 1 - the code default (3)
        // would have left it visible after only one report.
        await controller.Report(slug, CancellationToken.None);

        var state = await store.GetModerationAsync(slug, CancellationToken.None);
        Assert.True(state.IsHidden);
        Assert.Equal(1, state.ReportCount);
    }

    [Fact]
    public async Task An_overridden_higher_threshold_keeps_the_tale_visible_past_the_code_default_count()
    {
        var store = new FakePublishedTaleStore();
        var overriddenSettings = TestRuntimeSettings.WithInt(SettingsCatalog.ModerationTaleAutoHideThreshold, 5);
        var controller = NewController(store, overriddenSettings);
        await controller.Publish(CleanTale(), CancellationToken.None);
        var slug = store.Tales.Single().Slug;

        // Three reports (the CODE DEFAULT threshold) would have hidden it under the
        // default, but the overridden threshold of 5 keeps it visible.
        await controller.Report(slug, CancellationToken.None);
        await controller.Report(slug, CancellationToken.None);
        await controller.Report(slug, CancellationToken.None);

        var state = await store.GetModerationAsync(slug, CancellationToken.None);
        Assert.False(state.IsHidden);
        Assert.Equal(3, state.ReportCount);

        // A 4th and 5th report cross the OVERRIDDEN threshold and hide it.
        await controller.Report(slug, CancellationToken.None);
        await controller.Report(slug, CancellationToken.None);
        state = await store.GetModerationAsync(slug, CancellationToken.None);
        Assert.True(state.IsHidden);
        Assert.Equal(5, state.ReportCount);
    }

    // ---- AC-04: an overridden TTL stamps the tale, not the code-default 30 days --

    [Fact]
    public async Task An_overridden_ttl_stamps_a_new_tale_with_the_overridden_day_count()
    {
        var store = new FakePublishedTaleStore();
        var overriddenSettings = TestRuntimeSettings.WithInt(SettingsCatalog.TalesTtlDays, 7);
        var controller = NewController(store, overriddenSettings);

        await controller.Publish(CleanTale(), CancellationToken.None);

        var stored = Assert.Single(store.Tales);
        Assert.Equal(TimeSpan.FromDays(7), stored.ExpiresUtc - stored.CreatedUtc);
        Assert.NotEqual(PublishedTalesController.TaleTtl, stored.ExpiresUtc - stored.CreatedUtc);
    }

    [Fact]
    public async Task With_no_override_a_new_tale_still_stamps_the_code_default_thirty_days()
    {
        // AC-01 regression guard specific to this controller: no override -> unchanged.
        var store = new FakePublishedTaleStore();
        var controller = NewController(store, TestRuntimeSettings.Defaults());

        await controller.Publish(CleanTale(), CancellationToken.None);

        var stored = Assert.Single(store.Tales);
        Assert.Equal(TimeSpan.FromDays(30), stored.ExpiresUtc - stored.CreatedUtc);
        Assert.Equal(PublishedTalesController.TaleTtl, stored.ExpiresUtc - stored.CreatedUtc);
    }
}
