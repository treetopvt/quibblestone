// ----------------------------------------------------------------------------
//  ClaimControllerTests - controller-level tests for keepsake-vault/03's claim
//  and recovery surface (issue #230): POST /api/vault/claim, GET /api/vault/claim,
//  POST /api/vault/claim-code/regenerate, and POST /api/vault/claim-code/redeem.
//
//  These exercise the REAL VaultController against the REAL
//  PurchaserCredentialService, the REAL InMemoryAccountStore, and the working
//  in-memory vault store (no mocking framework, matching the harness), presenting
//  the family credential the way the SPA does (an Authorization: Bearer value,
//  mirroring CloudGalleryControllerTests) and the vault id the way every vault
//  call does (the X-Vault-Id header, mirroring VaultControllerTests). They lock
//  in:
//
//    - AC-01 AUTH: claiming with no family credential is 401; with a credential
//      resolving to an EXISTING account it is 200 and returns a grouped code.
//    - AC-02 BODY, NOT ROUTE/QUERY: the redeem action's HTTP route carries no
//      route parameter - the code travels only in the JSON request body.
//    - AC-06 NOT AN ORACLE: redeeming a valid code and a bogus one both return
//      200 - only the boolean Redeemed differs, so the endpoint cannot be used
//      to enumerate valid codes.
//    - AC-07 REGENERATE 404: regenerating a never-claimed vault's code is 404
//      (there is nothing to regenerate).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Reflection;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using QuibbleStone.Api.Accounts;
using QuibbleStone.Api.Safety;
using QuibbleStone.Api.Vault;

namespace QuibbleStone.Api.Tests.Vault;

public sealed class ClaimControllerTests
{
    private const string FamilyEmail = "family@example.com";

    // Well-formed vault ids (a UUID shape - 36 chars, passes the AC-01 floor).
    private const string VaultA = "33333333-3333-4333-8333-333333333333";
    private const string VaultB = "44444444-4444-4444-8444-444444444444";

    private static readonly IContentSafetyFilter Safety = new ContentSafetyFilter();

    private sealed record Harness(
        VaultController Controller,
        InMemoryVaultStore Store,
        PurchaserCredentialService Credential,
        InMemoryAccountStore Accounts);

    // A shared store can be passed in so two harnesses (two devices) share the
    // SAME backing state - mirroring VaultControllerTests' NewHarness(store).
    private static Harness NewHarness(InMemoryVaultStore? store = null)
    {
        store ??= new InMemoryVaultStore();
        var credential = new PurchaserCredentialService(new EphemeralDataProtectionProvider());
        var accounts = new InMemoryAccountStore();
        var controller = new VaultController(store, Safety, credential, accounts, new ClaimRedemptionCeiling())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        return new Harness(controller, store, credential, accounts);
    }

    private static void PresentVaultId(Harness h, string vaultId)
        => h.Controller.ControllerContext.HttpContext!.Request.Headers[VaultController.VaultIdHeader] = vaultId;

    // Present the family credential the way the SPA does: an Authorization: Bearer value.
    private static void SignIn(Harness h, string email)
        => h.Controller.ControllerContext.HttpContext!.Request.Headers.Authorization = $"Bearer {h.Credential.Protect(email)}";

    // ---- AC-01: claim requires the family credential ---------------------------

    [Fact]
    public async Task Claim_without_a_family_credential_is_401()
    {
        var h = NewHarness();
        PresentVaultId(h, VaultA);

        var result = await h.Controller.Claim(CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task Claim_with_a_credential_for_an_account_that_does_not_exist_is_401()
    {
        // A credential can resolve to an EMAIL, but claiming still requires the
        // canonical AccountId behind it (accounts-identity/05) - a credential with
        // no backing account is treated the same as no credential at all.
        var h = NewHarness();
        PresentVaultId(h, VaultA);
        SignIn(h, "nobody-has-signed-up-yet@example.com");

        var result = await h.Controller.Claim(CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task Claim_with_a_valid_family_credential_returns_200_with_a_grouped_code()
    {
        var h = NewHarness();
        await h.Accounts.CreateOrGetAsync(FamilyEmail);
        PresentVaultId(h, VaultA);
        SignIn(h, FamilyEmail);

        var result = await h.Controller.Claim(CancellationToken.None);

        var view = Assert.IsType<VaultClaimCodeView>(Assert.IsType<OkObjectResult>(result).Value);
        // AC-02: displayed GROUPED into three 3-character blocks.
        Assert.Matches("^[A-HJ-NP-Z2-9]{3}-[A-HJ-NP-Z2-9]{3}-[A-HJ-NP-Z2-9]{3}$", view.ClaimCode);
        Assert.True(view.ClaimCodeExpiresUtc > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Claim_without_a_vault_id_header_is_400()
    {
        var h = NewHarness();
        await h.Accounts.CreateOrGetAsync(FamilyEmail);
        SignIn(h, FamilyEmail);

        var result = await h.Controller.Claim(CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    // ---- GET /api/vault/claim: surfaces the live code once claimed ------------

    [Fact]
    public async Task GetClaim_for_an_unclaimed_vault_reports_not_claimed()
    {
        var h = NewHarness();
        PresentVaultId(h, VaultA);

        var result = await h.Controller.GetClaim(CancellationToken.None);

        var status = Assert.IsType<VaultClaimStatusResult>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.False(status.Claimed);
        Assert.Null(status.Code);
    }

    [Fact]
    public async Task GetClaim_after_claiming_reports_claimed_with_the_live_code()
    {
        var h = NewHarness();
        await h.Accounts.CreateOrGetAsync(FamilyEmail);
        PresentVaultId(h, VaultA);
        SignIn(h, FamilyEmail);
        await h.Controller.Claim(CancellationToken.None);

        var result = await h.Controller.GetClaim(CancellationToken.None);

        var status = Assert.IsType<VaultClaimStatusResult>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.True(status.Claimed);
        Assert.NotNull(status.Code);
    }

    // ---- AC-07: regenerate --------------------------------------------------

    [Fact]
    public async Task Regenerate_for_a_never_claimed_vault_is_404()
    {
        var h = NewHarness();
        PresentVaultId(h, VaultA);

        var result = await h.Controller.RegenerateClaimCode(CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Regenerate_requires_no_account_only_the_vault_id()
    {
        // AC-07: the account-free recovery path's own revoke action - any device
        // already holding the vault id can regenerate, no credential needed.
        var h = NewHarness();
        await h.Accounts.CreateOrGetAsync(FamilyEmail);
        PresentVaultId(h, VaultA);
        SignIn(h, FamilyEmail);
        var claimed = Assert.IsType<VaultClaimCodeView>(Assert.IsType<OkObjectResult>(await h.Controller.Claim(CancellationToken.None)).Value);

        // A fresh, credential-less harness over the SAME store, presenting only the
        // vault id, can still regenerate.
        var anon = NewHarness(h.Store);
        PresentVaultId(anon, VaultA);
        var result = await anon.Controller.RegenerateClaimCode(CancellationToken.None);

        var regenerated = Assert.IsType<VaultClaimCodeView>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.NotEqual(claimed.ClaimCode, regenerated.ClaimCode);
    }

    // ---- AC-06: redeem is not an oracle - both outcomes are 200 ----------------

    [Fact]
    public async Task Redeem_a_valid_code_returns_redeemed_true_as_200()
    {
        var h = NewHarness();
        await h.Accounts.CreateOrGetAsync(FamilyEmail);
        PresentVaultId(h, VaultA);
        SignIn(h, FamilyEmail);
        var claimed = Assert.IsType<VaultClaimCodeView>(Assert.IsType<OkObjectResult>(await h.Controller.Claim(CancellationToken.None)).Value);

        // A second device, over the SAME store, redeems the human-displayed
        // (grouped) code exactly as the gallery's "recover a vault" affordance
        // would present it.
        var device = NewHarness(h.Store);
        PresentVaultId(device, VaultB);
        var result = await device.Controller.RedeemClaimCode(new RedeemClaimCodeRequest(claimed.ClaimCode), CancellationToken.None);

        var body = Assert.IsType<RedeemClaimCodeResult>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.True(body.Redeemed);
    }

    [Fact]
    public async Task Redeem_a_bogus_code_returns_redeemed_false_as_200_not_a_404_or_400()
    {
        var h = NewHarness();
        PresentVaultId(h, VaultB);

        var result = await h.Controller.RedeemClaimCode(new RedeemClaimCodeRequest("NOT-REA-LLY"), CancellationToken.None);

        // AC-06: a uniform 200 either way - only the boolean differs, so the
        // endpoint is never a code-guessing oracle via HTTP status.
        var body = Assert.IsType<RedeemClaimCodeResult>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.False(body.Redeemed);
    }

    [Fact]
    public async Task Redeem_without_a_vault_id_header_is_400()
    {
        var h = NewHarness();
        var result = await h.Controller.RedeemClaimCode(new RedeemClaimCodeRequest("ANYCODE12"), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    // ---- AC-02: the code travels ONLY in the JSON body, never a route/query ---

    [Fact]
    public void Redeem_route_carries_no_route_or_query_parameter_the_code_is_body_only()
    {
        var method = typeof(VaultController).GetMethod(nameof(VaultController.RedeemClaimCode))!;

        var httpPost = method.GetCustomAttribute<Microsoft.AspNetCore.Mvc.HttpPostAttribute>();
        Assert.NotNull(httpPost);
        Assert.DoesNotContain("{", httpPost!.Template ?? string.Empty);

        // The code arrives as a [FromBody] parameter of RedeemClaimCodeRequest -
        // there is no [FromQuery] / [FromRoute] parameter carrying it anywhere.
        var bodyParameter = Assert.Single(method.GetParameters(), p => p.ParameterType == typeof(RedeemClaimCodeRequest));
        Assert.NotNull(bodyParameter.GetCustomAttribute<FromBodyAttribute>());
        Assert.Null(bodyParameter.GetCustomAttribute<FromQueryAttribute>());
        Assert.Null(bodyParameter.GetCustomAttribute<FromRouteAttribute>());
    }

    // ---- AC-06: the response carries no AccountId / PII ------------------------

    [Fact]
    public async Task Claim_response_carries_no_account_id_or_email()
    {
        var h = NewHarness();
        var account = await h.Accounts.CreateOrGetAsync(FamilyEmail);
        PresentVaultId(h, VaultA);
        SignIn(h, FamilyEmail);

        var result = await h.Controller.Claim(CancellationToken.None);
        var view = Assert.IsType<VaultClaimCodeView>(Assert.IsType<OkObjectResult>(result).Value);

        var json = System.Text.Json.JsonSerializer.Serialize(view);
        Assert.DoesNotContain(account.Id.ToString(), json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(FamilyEmail, json, StringComparison.OrdinalIgnoreCase);
    }
}
