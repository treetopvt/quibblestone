// ----------------------------------------------------------------------------
//  VaultControllerTests - controller-level tests for keepsake-vault/01 (issue
//  #196), the anonymous server-side keepsake vault's write / read / mint surface.
//
//  These exercise the REAL VaultController against the REAL ContentSafetyFilter
//  and the working in-memory vault store (no mocking framework, matching the
//  harness). They present the vault id the way the web client does - an X-Vault-Id
//  request HEADER, never a route segment (AC-02). They lock in the story's
//  guarantees:
//
//    - AC-01 FLOOR: a missing / too-short / malformed X-Vault-Id is rejected 400
//      on both write and read; mint returns a well-formed, floor-passing id.
//    - AC-02 HEADER + SERVER STAMP: the vault id is read from the header (no route
//      param exists); a client-supplied createdUtc is ignored and the server
//      stamps its own; a save with no header cannot store anything.
//    - AC-04 RE-VET / NO PII: a save whose word / literal-tagged word / byline
//      fails the filter is REJECTED (400) and NOTHING is stored - a lying client
//      cannot smuggle unfiltered content in; the stored tale carries only the
//      byline nickname.
//    - AC-05 ROUND-TRIP: with the in-memory store (no connection string), a
//      save -> list round-trips.
//    - AC-07 CAP: a save past MaxTalesPerVault for one vault id is rejected while a
//      save under the cap for a DIFFERENT vault id succeeds.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using QuibbleStone.Api.Accounts;
using QuibbleStone.Api.Safety;
using QuibbleStone.Api.Vault;

namespace QuibbleStone.Api.Tests.Vault;

public class VaultControllerTests
{
    private static readonly IContentSafetyFilter Safety = new ContentSafetyFilter();

    // A well-formed vault id (a UUID shape - 36 chars, passes the AC-01 floor).
    private const string VaultA = "11111111-1111-4111-8111-111111111111";
    private const string VaultB = "22222222-2222-4222-8222-222222222222";

    private sealed record Harness(VaultController Controller, InMemoryVaultStore Store);

    private static Harness NewHarness(InMemoryVaultStore? store = null)
    {
        store ??= new InMemoryVaultStore();
        var credential = new PurchaserCredentialService(new EphemeralDataProtectionProvider());
        var controller = new VaultController(store, Safety, credential, new InMemoryAccountStore(), new ClaimRedemptionCeiling())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        return new Harness(controller, store);
    }

    private static void PresentVaultId(Harness h, string vaultId)
        => h.Controller.ControllerContext.HttpContext!.Request.Headers[VaultController.VaultIdHeader] = vaultId;

    private static SaveVaultTaleRequest CleanTale(string coralWord = "banana", string title = "The space llama saga") => new(
        Title: title,
        Parts:
        [
            new VaultTalePartRequest(IsWord: false, Text: "Once upon a time a "),
            new VaultTalePartRequest(IsWord: true, Text: coralWord),
            new VaultTalePartRequest(IsWord: false, Text: " danced."),
        ],
        BylineNames: "Sam & Mia");

    private static VaultListResult ListOk(IActionResult action)
        => Assert.IsType<VaultListResult>(Assert.IsType<OkObjectResult>(action).Value);

    private static string SaveOk(IActionResult action)
        => Assert.IsType<SavedVaultTaleResult>(Assert.IsType<OkObjectResult>(action).Value).TaleId;

    // ---- AC-01: the server-side length/format floor ---------------------------

    [Fact]
    public async Task Save_without_a_vault_id_header_is_400()
    {
        var h = NewHarness();
        var result = await h.Controller.Save(CleanTale(), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Save_with_a_too_short_vault_id_is_400_and_stores_nothing()
    {
        var h = NewHarness();
        PresentVaultId(h, "too-short");
        var result = await h.Controller.Save(CleanTale(), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);

        // Nothing was stored under the (rejected) id.
        PresentVaultId(h, VaultA);
        Assert.Empty(ListOk(await h.Controller.List(CancellationToken.None)).Tales);
    }

    [Fact]
    public async Task List_without_a_vault_id_header_is_400()
    {
        var h = NewHarness();
        var result = await h.Controller.List(CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void Mint_returns_a_floor_passing_vault_id()
    {
        var h = NewHarness();
        var minted = Assert.IsType<MintVaultIdResult>(Assert.IsType<OkObjectResult>(h.Controller.Mint()).Value);
        Assert.True(VaultId.IsWellFormed(minted.VaultId));
    }

    // ---- AC-05 + AC-02: save -> list round-trip via the header ----------------

    [Fact]
    public async Task Save_then_list_round_trips_via_the_header()
    {
        var h = NewHarness();
        PresentVaultId(h, VaultA);

        var taleId = SaveOk(await h.Controller.Save(CleanTale(), CancellationToken.None));

        var list = ListOk(await h.Controller.List(CancellationToken.None));
        var tale = Assert.Single(list.Tales);
        Assert.Equal(taleId, tale.TaleId);
        Assert.Equal("The space llama saga", tale.Title);
        Assert.Equal("Sam & Mia", tale.BylineNames);
        Assert.Contains(tale.Parts, p => p.IsWord && p.Text == "banana");
    }

    // ---- AC-02: CreatedUtc is server-stamped, never from the client -----------

    [Fact]
    public async Task Save_server_stamps_createdUtc_near_now()
    {
        var h = NewHarness();
        PresentVaultId(h, VaultA);

        var before = DateTimeOffset.UtcNow.AddSeconds(-5);
        SaveOk(await h.Controller.Save(CleanTale(), CancellationToken.None));
        var after = DateTimeOffset.UtcNow.AddSeconds(5);

        var tale = Assert.Single(ListOk(await h.Controller.List(CancellationToken.None)).Tales);
        Assert.InRange(tale.CreatedUtc, before, after);
    }

    // ---- AC-04: server-side re-vet rejects the whole save ---------------------

    [Fact]
    public async Task Save_rejects_a_coral_word_that_fails_the_safety_filter()
    {
        var h = NewHarness();
        PresentVaultId(h, VaultA);

        var result = await h.Controller.Save(CleanTale(coralWord: "shit"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(ListOk(await h.Controller.List(CancellationToken.None)).Tales);
    }

    [Fact]
    public async Task Save_rejects_an_unsafe_literal_part_a_lying_client_tags_as_not_a_word()
    {
        // The server must NOT trust the client's IsWord flag - unfiltered text
        // marked as a "literal" run must still be re-vetted and rejected (AC-04).
        var h = NewHarness();
        PresentVaultId(h, VaultA);

        var request = new SaveVaultTaleRequest(
            Title: "x",
            Parts: [new VaultTalePartRequest(IsWord: false, Text: "shit")],
            BylineNames: string.Empty);

        var result = await h.Controller.Save(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(ListOk(await h.Controller.List(CancellationToken.None)).Tales);
    }

    [Fact]
    public async Task Save_rejects_a_byline_that_fails_the_safety_filter()
    {
        var h = NewHarness();
        PresentVaultId(h, VaultA);

        var request = CleanTale() with { BylineNames = "fuck" };
        var result = await h.Controller.Save(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(ListOk(await h.Controller.List(CancellationToken.None)).Tales);
    }

    [Fact]
    public async Task Save_drops_empty_coral_words_but_keeps_the_story()
    {
        var h = NewHarness();
        PresentVaultId(h, VaultA);

        var request = new SaveVaultTaleRequest(
            Title: "Gappy tale",
            Parts:
            [
                new VaultTalePartRequest(false, "Start "),
                new VaultTalePartRequest(true, ""),   // an unfilled blank - dropped
                new VaultTalePartRequest(false, " end"),
            ],
            BylineNames: "");

        SaveOk(await h.Controller.Save(request, CancellationToken.None));

        var stored = Assert.Single(ListOk(await h.Controller.List(CancellationToken.None)).Tales);
        Assert.Equal(2, stored.Parts.Count);
        Assert.All(stored.Parts, p => Assert.False(p.IsWord));
    }

    [Fact]
    public async Task Save_rejects_an_empty_title()
    {
        var h = NewHarness();
        PresentVaultId(h, VaultA);

        var request = CleanTale() with { Title = "   " };
        var result = await h.Controller.Save(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(ListOk(await h.Controller.List(CancellationToken.None)).Tales);
    }

    // ---- AC-04: only the byline nickname is stored, no PII / no identity ------

    [Fact]
    public async Task Stored_tale_carries_only_the_byline_nickname_no_pii()
    {
        var h = NewHarness();
        PresentVaultId(h, VaultA);
        SaveOk(await h.Controller.Save(CleanTale(), CancellationToken.None));

        var tale = Assert.Single(ListOk(await h.Controller.List(CancellationToken.None)).Tales);
        Assert.Equal("Sam & Mia", tale.BylineNames);

        // The vault id (the only handle) is never projected back onto the view - it
        // stays a server-side partition key.
        var json = System.Text.Json.JsonSerializer.Serialize(tale);
        Assert.DoesNotContain(VaultA, json, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Vault isolation ------------------------------------------------------

    [Fact]
    public async Task One_vault_never_sees_another_vaults_tales()
    {
        var store = new InMemoryVaultStore();

        var a = NewHarness(store);
        PresentVaultId(a, VaultA);
        SaveOk(await a.Controller.Save(CleanTale(coralWord: "aardvark", title: "A tale"), CancellationToken.None));

        var b = NewHarness(store);
        PresentVaultId(b, VaultB);
        SaveOk(await b.Controller.Save(CleanTale(coralWord: "beetle", title: "B tale"), CancellationToken.None));

        var bTale = Assert.Single(ListOk(await b.Controller.List(CancellationToken.None)).Tales);
        Assert.Equal("B tale", bTale.Title);

        var aTale = Assert.Single(ListOk(await a.Controller.List(CancellationToken.None)).Tales);
        Assert.Equal("A tale", aTale.Title);
    }

    // ---- AC-07: per-vault cap -------------------------------------------------

    [Fact]
    public async Task Save_past_the_cap_is_rejected_but_a_different_vault_still_saves()
    {
        // Pre-fill vault A to the cap directly through the store (cheap), then prove
        // one more save via the controller is rejected (409) while vault B - under
        // the cap - still succeeds.
        var store = new InMemoryVaultStore();
        for (var i = 0; i < IVaultStore.MaxTalesPerVault; i++)
        {
            var outcome = await store.SaveAsync(
                new VaultTale(VaultA, $"seed-{i}", "seed", [new VaultTalePart(false, "x")], "", DateTimeOffset.UtcNow),
                CancellationToken.None);
            Assert.Equal(VaultSaveOutcome.Saved, outcome);
        }

        var a = NewHarness(store);
        PresentVaultId(a, VaultA);
        var overCap = await a.Controller.Save(CleanTale(), CancellationToken.None);
        Assert.IsType<ConflictObjectResult>(overCap);

        var b = NewHarness(store);
        PresentVaultId(b, VaultB);
        var underCap = await b.Controller.Save(CleanTale(), CancellationToken.None);
        Assert.IsType<OkObjectResult>(underCap);
    }
}
