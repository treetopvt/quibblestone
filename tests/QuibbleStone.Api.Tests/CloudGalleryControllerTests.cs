// ----------------------------------------------------------------------------
//  CloudGalleryControllerTests - controller-level tests for keepsake-gallery/05
//  (issue #154), the signed-in purchaser's cloud-synced keepsake gallery.
//
//  These exercise the REAL CloudGalleryController against the REAL
//  PurchaserCredentialService, the REAL ContentSafetyFilter, the working in-memory
//  account store, the real (default-unlocked) entitlement service, and the working
//  in-memory cloud-gallery store (no mocking framework, matching the harness). They
//  present the credential the way the SPA does (an Authorization: Bearer value,
//  mirroring RestoreViewTests). They lock in the story's guarantees:
//
//    - AC-02 AUTH: no credential -> 401 on read, save, delete, revoke-all.
//    - AC-01 ROUND-TRIP: a purchaser saves a tale and lists it back.
//    - AC-05 RE-VET: a save whose word / byline fails the filter is REJECTED (400)
//      and NOTHING is stored - a lying client cannot smuggle unfiltered content in.
//    - AC-06 DELETE-ONE: deleting a tale removes it from the gallery.
//    - AC-06 REVOKE-ALL: DELETE /api/account/gallery empties the whole gallery.
//    - OWNER ISOLATION: purchaser A never sees / deletes purchaser B's tales.
//    - AC-05 NO PII: the stored tale carries only the byline nickname(s) - never the
//      purchaser email or any other PII (the owner key is an opaque hash).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using QuibbleStone.Api.Accounts;
using QuibbleStone.Api.CloudGallery;
using QuibbleStone.Api.Entitlements;
using QuibbleStone.Api.Safety;

namespace QuibbleStone.Api.Tests;

public class CloudGalleryControllerTests
{
    private const string PurchaserA = "alice@example.com";
    private const string PurchaserB = "bob@example.com";

    private static readonly IContentSafetyFilter Safety = new ContentSafetyFilter();

    private sealed record Harness(
        CloudGalleryController Controller,
        PurchaserCredentialService Credential,
        InMemoryAccountStore Accounts);

    // A shared credential minter + account store + tale store can be passed in so two
    // harnesses (two purchasers) share the SAME backing state for the isolation test;
    // otherwise each harness gets its own fresh set.
    private static Harness NewHarness(
        PurchaserCredentialService? credential = null,
        InMemoryAccountStore? accounts = null,
        InMemoryCloudGalleryStore? store = null)
    {
        credential ??= new PurchaserCredentialService(new EphemeralDataProtectionProvider());
        accounts ??= new InMemoryAccountStore();
        store ??= new InMemoryCloudGalleryStore();
        var entitlements = new DefaultUnlockedEntitlementService();
        var controller = new CloudGalleryController(credential, accounts, entitlements, store, Safety)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        return new Harness(controller, credential, accounts);
    }

    // Present a credential the way the SPA does: an Authorization: Bearer value.
    private static void SignIn(Harness h, string email)
        => h.Controller.ControllerContext.HttpContext!.Request.Headers.Authorization = $"Bearer {h.Credential.Protect(email)}";

    private static SaveCloudTaleRequest CleanTale(string coralWord = "banana", string title = "The space llama saga") => new(
        Title: title,
        Parts:
        [
            new CloudTalePartRequest(IsWord: false, Text: "Once upon a time a "),
            new CloudTalePartRequest(IsWord: true, Text: coralWord),
            new CloudTalePartRequest(IsWord: false, Text: " danced."),
        ],
        BylineNames: "Sam & Mia");

    private static CloudGalleryResult ListOk(IActionResult action)
    {
        var ok = Assert.IsType<OkObjectResult>(action);
        return Assert.IsType<CloudGalleryResult>(ok.Value);
    }

    private static string SaveOk(IActionResult action)
    {
        var ok = Assert.IsType<OkObjectResult>(action);
        var saved = Assert.IsType<SavedCloudTaleResult>(ok.Value);
        return saved.TaleId;
    }

    // ---- AC-02: auth boundary (401 without a credential) ---------------------

    [Fact]
    public async Task List_without_a_credential_is_401()
    {
        var h = NewHarness();
        var result = await h.Controller.List(CancellationToken.None);
        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task Save_without_a_credential_is_401()
    {
        var h = NewHarness();
        var result = await h.Controller.Save(CleanTale(), CancellationToken.None);
        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task Delete_without_a_credential_is_401()
    {
        var h = NewHarness();
        var result = await h.Controller.Delete("anytale", CancellationToken.None);
        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task RevokeAll_without_a_credential_is_401()
    {
        var h = NewHarness();
        var result = await h.Controller.RevokeAll(CancellationToken.None);
        Assert.IsType<UnauthorizedResult>(result);
    }

    // ---- AC-01: save -> list round-trip for a purchaser ----------------------

    [Fact]
    public async Task Save_then_list_round_trips_for_a_signed_in_purchaser()
    {
        var h = NewHarness();
        await h.Accounts.CreateOrGetAsync(PurchaserA);
        SignIn(h, PurchaserA);

        var taleId = SaveOk(await h.Controller.Save(CleanTale(), CancellationToken.None));

        var list = ListOk(await h.Controller.List(CancellationToken.None));
        var tale = Assert.Single(list.Tales);
        Assert.Equal(taleId, tale.TaleId);
        Assert.Equal("The space llama saga", tale.Title);
        Assert.Equal("Sam & Mia", tale.BylineNames);
        // The coral word survives the round-trip, tagged as a word.
        Assert.Contains(tale.Parts, p => p.IsWord && p.Text == "banana");
    }

    // A valid credential but NO account row: list is a friendly empty gallery, and a
    // save is refused (a credential with no purchase behind it makes no cloud state).
    [Fact]
    public async Task Valid_credential_but_no_account_lists_empty_and_cannot_save()
    {
        var h = NewHarness();
        SignIn(h, PurchaserA); // credential valid, but no account created

        var list = ListOk(await h.Controller.List(CancellationToken.None));
        Assert.Empty(list.Tales);

        var save = await h.Controller.Save(CleanTale(), CancellationToken.None);
        Assert.IsType<UnauthorizedResult>(save);
        // Still an empty gallery (nothing was stored for a credential with no account).
        Assert.Empty(ListOk(await h.Controller.List(CancellationToken.None)).Tales);
    }

    // List a signed-in purchaser's tales THROUGH the controller (the public read
    // path) rather than reaching into the store by owner key, which is derived from
    // the internal AccountIdentity helper.
    private static async Task<IReadOnlyList<CloudTaleView>> ListTales(Harness h)
        => ListOk(await h.Controller.List(CancellationToken.None)).Tales;

    // ---- AC-05: server-side re-vet rejects the whole save --------------------

    [Fact]
    public async Task Save_rejects_a_coral_word_that_fails_the_safety_filter()
    {
        var h = NewHarness();
        await h.Accounts.CreateOrGetAsync(PurchaserA);
        SignIn(h, PurchaserA);

        var result = await h.Controller.Save(CleanTale(coralWord: "shit"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        // NOTHING may be stored when the re-vet fails.
        Assert.Empty(await ListTales(h));
    }

    [Fact]
    public async Task Save_rejects_an_unsafe_literal_part_a_lying_client_tags_as_not_a_word()
    {
        // The server must NOT trust the client's IsWord flag - unfiltered text marked
        // as a "literal" run must still be re-vetted and rejected (AC-05).
        var h = NewHarness();
        await h.Accounts.CreateOrGetAsync(PurchaserA);
        SignIn(h, PurchaserA);

        var request = new SaveCloudTaleRequest(
            Title: "x",
            Parts: [new CloudTalePartRequest(IsWord: false, Text: "shit")],
            BylineNames: string.Empty);

        var result = await h.Controller.Save(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(await ListTales(h));
    }

    [Fact]
    public async Task Save_rejects_a_byline_that_fails_the_safety_filter()
    {
        var h = NewHarness();
        await h.Accounts.CreateOrGetAsync(PurchaserA);
        SignIn(h, PurchaserA);

        var request = CleanTale() with { BylineNames = "fuck" };
        var result = await h.Controller.Save(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(await ListTales(h));
    }

    [Fact]
    public async Task Save_rejects_a_tale_whose_total_text_exceeds_the_size_cap()
    {
        // The per-part (500) and per-count (400) caps pass individually, but the TOTAL
        // stored text must stay bounded so the serialized PartsJson fits Azure Table
        // Storage's string-property limit rather than throwing a 500 on save.
        var h = NewHarness();
        await h.Accounts.CreateOrGetAsync(PurchaserA);
        SignIn(h, PurchaserA);

        var big = new string('a', 500); // clean text, exactly at the per-part cap
        var parts = new List<CloudTalePartRequest>();
        for (var i = 0; i < 40; i++)     // 40 x 500 = 20000 > the 16000 total cap
        {
            parts.Add(new CloudTalePartRequest(IsWord: false, Text: big));
        }
        var request = new SaveCloudTaleRequest(Title: "Too big", Parts: parts, BylineNames: string.Empty);

        var result = await h.Controller.Save(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(await ListTales(h));
    }

    [Fact]
    public async Task Save_drops_empty_coral_words_but_keeps_the_story()
    {
        var h = NewHarness();
        await h.Accounts.CreateOrGetAsync(PurchaserA);
        SignIn(h, PurchaserA);

        var request = new SaveCloudTaleRequest(
            Title: "Gappy tale",
            Parts:
            [
                new CloudTalePartRequest(false, "Start "),
                new CloudTalePartRequest(true, ""),   // an unfilled blank - dropped
                new CloudTalePartRequest(false, " end"),
            ],
            BylineNames: "");

        SaveOk(await h.Controller.Save(request, CancellationToken.None));

        var stored = Assert.Single(await ListTales(h));
        Assert.Equal(2, stored.Parts.Count);
        Assert.All(stored.Parts, p => Assert.False(p.IsWord));
    }

    [Fact]
    public async Task Save_rejects_an_empty_title()
    {
        var h = NewHarness();
        await h.Accounts.CreateOrGetAsync(PurchaserA);
        SignIn(h, PurchaserA);

        var request = CleanTale() with { Title = "   " };
        var result = await h.Controller.Save(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(await ListTales(h));
    }

    // ---- AC-06: delete-one ----------------------------------------------------

    [Fact]
    public async Task Delete_removes_one_tale_from_the_gallery()
    {
        var h = NewHarness();
        await h.Accounts.CreateOrGetAsync(PurchaserA);
        SignIn(h, PurchaserA);
        var taleId = SaveOk(await h.Controller.Save(CleanTale(), CancellationToken.None));
        SaveOk(await h.Controller.Save(CleanTale(title: "Second tale"), CancellationToken.None));

        var deleteResult = await h.Controller.Delete(taleId, CancellationToken.None);
        Assert.IsType<NoContentResult>(deleteResult);

        var list = ListOk(await h.Controller.List(CancellationToken.None));
        Assert.DoesNotContain(list.Tales, t => t.TaleId == taleId);
        Assert.Single(list.Tales); // the second tale survives
    }

    [Fact]
    public async Task Delete_is_idempotent_for_an_unknown_tale()
    {
        var h = NewHarness();
        await h.Accounts.CreateOrGetAsync(PurchaserA);
        SignIn(h, PurchaserA);

        var result = await h.Controller.Delete("NEVEREXISTED", CancellationToken.None);
        Assert.IsType<NoContentResult>(result);
    }

    // ---- AC-06: revoke-all empties the gallery -------------------------------

    [Fact]
    public async Task RevokeAll_empties_the_whole_gallery()
    {
        var h = NewHarness();
        await h.Accounts.CreateOrGetAsync(PurchaserA);
        SignIn(h, PurchaserA);
        SaveOk(await h.Controller.Save(CleanTale(), CancellationToken.None));
        SaveOk(await h.Controller.Save(CleanTale(title: "Another"), CancellationToken.None));

        var result = await h.Controller.RevokeAll(CancellationToken.None);
        Assert.IsType<NoContentResult>(result);

        var list = ListOk(await h.Controller.List(CancellationToken.None));
        Assert.Empty(list.Tales);
    }

    // ---- Owner isolation ------------------------------------------------------

    [Fact]
    public async Task Purchaser_A_never_sees_purchaser_B_tales()
    {
        // A shared credential minter + account store + tale store, two purchasers.
        var credential = new PurchaserCredentialService(new EphemeralDataProtectionProvider());
        var accounts = new InMemoryAccountStore();
        var store = new InMemoryCloudGalleryStore();
        await accounts.CreateOrGetAsync(PurchaserA);
        await accounts.CreateOrGetAsync(PurchaserB);

        var a = NewHarness(credential, accounts, store);
        SignIn(a, PurchaserA);
        var aTaleId = SaveOk(await a.Controller.Save(CleanTale(coralWord: "aardvark", title: "A tale"), CancellationToken.None));

        var b = NewHarness(credential, accounts, store);
        SignIn(b, PurchaserB);
        SaveOk(await b.Controller.Save(CleanTale(coralWord: "beetle", title: "B tale"), CancellationToken.None));

        // B lists only their OWN tale.
        var bList = ListOk(await b.Controller.List(CancellationToken.None));
        var bTale = Assert.Single(bList.Tales);
        Assert.Equal("B tale", bTale.Title);

        // A still lists only their own after B saved.
        var aList = ListOk(await a.Controller.List(CancellationToken.None));
        var aTale = Assert.Single(aList.Tales);
        Assert.Equal("A tale", aTale.Title);

        // B deleting A's tale id is a no-op scoped to B's partition - A keeps their tale.
        await b.Controller.Delete(aTaleId, CancellationToken.None);
        var aStillThere = ListOk(await a.Controller.List(CancellationToken.None));
        Assert.Contains(aStillThere.Tales, t => t.TaleId == aTaleId);

        // B revoking-all only clears B's gallery, never A's.
        await b.Controller.RevokeAll(CancellationToken.None);
        Assert.Empty(ListOk(await b.Controller.List(CancellationToken.None)).Tales);
        Assert.Single(ListOk(await a.Controller.List(CancellationToken.None)).Tales);
    }

    // ---- AC-05: only byline nickname(s) stored, no PII -----------------------

    [Fact]
    public async Task Stored_tale_carries_only_the_byline_nickname_no_purchaser_pii()
    {
        var h = NewHarness();
        await h.Accounts.CreateOrGetAsync(PurchaserA);
        SignIn(h, PurchaserA);
        SaveOk(await h.Controller.Save(CleanTale(), CancellationToken.None));

        var tale = Assert.Single(await ListTales(h));

        // The byline nickname is the only identity on the tale.
        Assert.Equal("Sam & Mia", tale.BylineNames);

        // Serialize the whole gallery view (what the web half consumes): the purchaser
        // email / real name must appear nowhere - only the byline nickname. The owner
        // key is not even exposed to the client (it stays a server-side partition key).
        var json = System.Text.Json.JsonSerializer.Serialize(tale);
        Assert.DoesNotContain(PurchaserA, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alice", json, StringComparison.OrdinalIgnoreCase);
    }

    // ---- AC-04: gallery.cloudSync is default-unlocked (the seam is read) ------

    [Fact]
    public void Catalog_reserves_the_gallery_cloud_sync_key_default_unlocked()
    {
        Assert.Equal("gallery.cloudSync", EntitlementCatalog.GalleryCloudSync);
        Assert.Contains(EntitlementCatalog.GalleryCloudSync, EntitlementCatalog.DefaultUnlockedCapabilities);
    }
}
