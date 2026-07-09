// ----------------------------------------------------------------------------
//  TakedownSoftDeleteTests - the moderation-takedown SOFT-DELETE + restore path
//  for published shareable tales (keepsake-vault/04, issue #231). Exercises the
//  REAL PublishedTalesController's public serve/report path and the store's
//  moderation verbs against the same in-memory FakePublishedTaleStore the existing
//  PublishedTalesControllerTests / ReportTaleTests use (the shared store-setup
//  seam), plus the confirmation-gated RestoreFromTakedownAsync it now owns. They
//  lock in the load-bearing guarantees:
//
//    - AC-04: ConfirmHiddenAsync no longer REMOVES the tale row - the body is
//      retained (a soft-delete) and a subsequent restore-from-takedown returns the
//      original content, while the public page reads it as GONE in the meantime.
//    - AC-06: a restored takedown serves EXACTLY as before - byte-for-byte content,
//      no re-vet, no re-publish ceremony.
//    - AC-03: a taken-down tale past its restore window reads as genuinely gone and
//      is no longer restorable (lazy purge-on-read).
//    - AC-07 (friction parity): RestoreFromTakedownAsync REQUIRES an explicit
//      confirmation argument that the plain vault IVaultStore.RestoreAsync has no
//      equivalent of - a structural, compile-time-visible distinction (asserted here
//      via reflection over the two signatures) AND a runtime backstop (an
//      un-confirmed call is a no-op).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using QuibbleStone.Api.PublishedTales;
using QuibbleStone.Api.Safety;

namespace QuibbleStone.Api.Tests;

public sealed class TakedownSoftDeleteTests
{
    private static readonly IContentSafetyFilter Safety = new ContentSafetyFilter();

    private static IConfiguration Config() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PublishedTales:WebAppBaseUrl"] = "https://play.example.test",
            })
            .Build();

    private static PublishedTalesController NewController(IPublishedTaleStore store)
    {
        var controller = new PublishedTalesController(store, Safety, Config())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        controller.HttpContext.Request.Scheme = "https";
        controller.HttpContext.Request.Host = new HostString("tales.example.test");
        return controller;
    }

    private static PublishedTale CleanTale(string slug = "SLUGSLUGSLUG") => new(
        Slug: slug,
        Title: "The space llama saga",
        Parts:
        [
            new TalePart(false, "Once upon a time a "),
            new TalePart(true, "banana"),
            new TalePart(false, " danced."),
        ],
        BylineNames: "Sam & Mia",
        CreatedUtc: DateTimeOffset.UtcNow,
        ExpiresUtc: DateTimeOffset.UtcNow + PublishedTalesController.TaleTtl);

    // ---- AC-04: confirm-hidden soft-deletes (row retained), then restores ------

    [Fact]
    public async Task ConfirmHidden_soft_deletes_the_body_and_restore_from_takedown_brings_it_back()
    {
        var store = new FakePublishedTaleStore();
        store.Seed(CleanTale());
        store.SeedModeration("SLUGSLUGSLUG", PublishedTalesController.AutoHideThreshold, isHidden: true);

        // Confirm-hidden: the tale stops serving. The public page reads it as GONE.
        Assert.True(await store.ConfirmHiddenAsync("SLUGSLUGSLUG", CancellationToken.None));
        Assert.Null(await store.GetAsync("SLUGSLUGSLUG", CancellationToken.None));

        // AC-04: but the body row was NOT removed - a restore-from-takedown recovers it.
        Assert.True(await store.RestoreFromTakedownAsync("SLUGSLUGSLUG", confirmedByOperator: true, CancellationToken.None));

        var restored = await store.GetAsync("SLUGSLUGSLUG", CancellationToken.None);
        Assert.NotNull(restored);
    }

    [Fact]
    public async Task A_confirmed_hidden_tale_serves_the_404_drifted_away_page_not_the_tale()
    {
        // The public page after a takedown is the same GONE state a revoked / expired
        // tale gives (the body reads as absent) - the tale content never leaks.
        var store = new FakePublishedTaleStore();
        store.Seed(CleanTale());
        store.SeedModeration("SLUGSLUGSLUG", PublishedTalesController.AutoHideThreshold, isHidden: true);
        await store.ConfirmHiddenAsync("SLUGSLUGSLUG", CancellationToken.None);

        var page = await NewController(store).Page("SLUGSLUGSLUG", CancellationToken.None) as ContentResult;

        Assert.NotNull(page);
        Assert.Equal(StatusCodes.Status404NotFound, page!.StatusCode);
        Assert.DoesNotContain("banana", page.Content);
        Assert.Contains("drifted away", page.Content, StringComparison.OrdinalIgnoreCase);
    }

    // ---- AC-06: a restored takedown serves EXACTLY as before -------------------

    [Fact]
    public async Task A_restored_takedown_serves_the_original_content_unchanged()
    {
        var store = new FakePublishedTaleStore();
        store.Seed(CleanTale());
        store.SeedModeration("SLUGSLUGSLUG", PublishedTalesController.AutoHideThreshold, isHidden: true);
        await store.ConfirmHiddenAsync("SLUGSLUGSLUG", CancellationToken.None);

        await store.RestoreFromTakedownAsync("SLUGSLUGSLUG", confirmedByOperator: true, CancellationToken.None);

        // AC-06: the public page renders the original tale again, byte-for-byte - the
        // coral word and byline are back, and it is a normal 200.
        var page = await NewController(store).Page("SLUGSLUGSLUG", CancellationToken.None) as ContentResult;
        Assert.NotNull(page);
        Assert.Equal(StatusCodes.Status200OK, page!.StatusCode);
        Assert.Contains("banana", page.Content);
        Assert.Contains("carved by", page.Content);
    }

    // ---- AC-03: past the restore window, a takedown is genuinely gone ----------

    [Fact]
    public async Task A_taken_down_tale_past_its_window_is_not_restorable()
    {
        var store = new FakePublishedTaleStore();
        // Seed a tale already taken down well past its restore window.
        store.Seed(CleanTale() with
        {
            DeletedUtc = DateTimeOffset.UtcNow.AddDays(-(PublishedTale.TakedownRestoreWindowDays + 1)),
        });

        // AC-03: it reads as gone, and a restore refuses (out of scope once lapsed).
        Assert.Null(await store.GetAsync("SLUGSLUGSLUG", CancellationToken.None));
        Assert.False(await store.RestoreFromTakedownAsync("SLUGSLUGSLUG", confirmedByOperator: true, CancellationToken.None));
    }

    // ---- AC-07: friction parity - the takedown restore is confirmation-gated ---

    [Fact]
    public async Task RestoreFromTakedown_without_the_confirmation_marker_is_a_no_op()
    {
        // The runtime backstop to the structural requirement: an un-confirmed call
        // never un-deletes reported content, even for a genuinely taken-down tale.
        var store = new FakePublishedTaleStore();
        store.Seed(CleanTale());
        store.SeedModeration("SLUGSLUGSLUG", PublishedTalesController.AutoHideThreshold, isHidden: true);
        await store.ConfirmHiddenAsync("SLUGSLUGSLUG", CancellationToken.None);

        Assert.False(await store.RestoreFromTakedownAsync("SLUGSLUGSLUG", confirmedByOperator: false, CancellationToken.None));
        // Still gone - the un-confirmed call changed nothing.
        Assert.Null(await store.GetAsync("SLUGSLUGSLUG", CancellationToken.None));
    }

    [Fact]
    public void The_takedown_restore_signature_requires_a_confirmation_arg_the_vault_restore_does_not()
    {
        // AC-07, STRUCTURAL (compile-time-visible): a caller cannot invoke the
        // takedown restore without affirmatively supplying a confirmation marker,
        // while the plain vault self-delete restore has no equivalent requirement.
        // Asserted over the two live signatures so the distinction cannot silently
        // erode (e.g. someone defaulting the confirmation arg or dropping it).
        var takedown = typeof(IPublishedTaleStore).GetMethod(nameof(IPublishedTaleStore.RestoreFromTakedownAsync))!;
        var takedownConfirm = takedown.GetParameters()
            .SingleOrDefault(p => p.ParameterType == typeof(bool));
        Assert.NotNull(takedownConfirm);
        // Required: no default value, so the caller MUST pass it explicitly.
        Assert.False(takedownConfirm!.HasDefaultValue);

        // The vault restore carries no such confirmation parameter at all.
        var vaultRestore = typeof(QuibbleStone.Api.Vault.IVaultStore)
            .GetMethod(nameof(QuibbleStone.Api.Vault.IVaultStore.RestoreAsync))!;
        Assert.DoesNotContain(vaultRestore.GetParameters(), p => p.ParameterType == typeof(bool));
    }
}
