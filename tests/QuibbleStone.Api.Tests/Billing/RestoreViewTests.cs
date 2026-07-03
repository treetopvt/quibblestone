// ----------------------------------------------------------------------------
//  RestoreViewTests - the read-only restore/manage endpoint (billing-entitlements/05,
//  #74). Drives the real EntitlementsController against the real
//  PurchaserCredentialService + working in-memory account/grant stores (no mocking),
//  presenting the credential the way the SPA does (an Authorization: Bearer value).
//
//  Pins: a signed-in purchaser sees their ACTIVE grants labeled (AC-01); an expired
//  grant is not shown; a signed-in purchaser with nothing gets a friendly empty list
//  (AC-03); an unauthenticated caller gets 401 with NO entitlement state (AC-06); the
//  payload carries no player/session reference (AC-05, structural).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using QuibbleStone.Api.Accounts;
using QuibbleStone.Api.Controllers;
using QuibbleStone.Api.Entitlements;

namespace QuibbleStone.Api.Tests.Billing;

public class RestoreViewTests
{
    private const string Purchaser = "buyer@example.com";

    private sealed record Harness(
        EntitlementsController Controller,
        PurchaserCredentialService Credential,
        InMemoryAccountStore Accounts,
        InMemoryEntitlementGrantStore Grants);

    private static Harness NewHarness()
    {
        var credential = new PurchaserCredentialService(new EphemeralDataProtectionProvider());
        var accounts = new InMemoryAccountStore();
        var grants = new InMemoryEntitlementGrantStore();
        var controller = new EntitlementsController(credential, grants, accounts)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        return new Harness(controller, credential, accounts, grants);
    }

    // Present a credential the way the SPA does: an Authorization: Bearer value.
    private static void SignIn(Harness h, string email)
        => h.Controller.ControllerContext.HttpContext!.Request.Headers.Authorization = $"Bearer {h.Credential.Protect(email)}";

    private static EntitlementsResult Ok(IActionResult action)
    {
        var ok = Assert.IsType<OkObjectResult>(action);
        return Assert.IsType<EntitlementsResult>(ok.Value);
    }

    // AC-01: a signed-in purchaser sees their active grant, labeled in plain language.
    [Fact]
    public async Task SignedIn_purchaser_sees_active_grants_labeled()
    {
        var h = NewHarness();
        await h.Accounts.CreateOrGetAsync(Purchaser);
        await h.Grants.PutGrantAsync(Purchaser, new EntitlementGrant(EntitlementCatalog.LibraryFull, null, GrantSource.OneTime));
        await h.Grants.PutGrantAsync(Purchaser, new EntitlementGrant(EntitlementCatalog.Pack("spooky"), null, GrantSource.OneTime));
        SignIn(h, Purchaser);

        var result = Ok(await h.Controller.Entitlements(CancellationToken.None));

        Assert.Contains(result.Entitlements, e => e.Key == EntitlementCatalog.LibraryFull && e.Label == "Full Library");
        Assert.Contains(result.Entitlements, e => e.Key == "pack.spooky" && e.Label == "Spooky Pack");
    }

    // AC-01/AC-08: an expired lease is not shown as unlocked.
    [Fact]
    public async Task Expired_grant_is_not_listed()
    {
        var h = NewHarness();
        await h.Accounts.CreateOrGetAsync(Purchaser);
        await h.Grants.PutGrantAsync(Purchaser, new EntitlementGrant(EntitlementCatalog.PlayRemote, DateTimeOffset.UtcNow.AddDays(-1), GrantSource.Subscription));
        SignIn(h, Purchaser);

        var result = Ok(await h.Controller.Entitlements(CancellationToken.None));

        Assert.DoesNotContain(result.Entitlements, e => e.Key == EntitlementCatalog.PlayRemote);
    }

    // AC-03: a signed-in purchaser with zero grants gets a friendly empty list, not an error.
    [Fact]
    public async Task SignedIn_purchaser_with_no_grants_gets_empty_list()
    {
        var h = NewHarness();
        await h.Accounts.CreateOrGetAsync(Purchaser);
        SignIn(h, Purchaser);

        var result = Ok(await h.Controller.Entitlements(CancellationToken.None));

        Assert.Empty(result.Entitlements);
    }

    // AC-03: even with no account at all, a valid credential yields an empty list (not an error).
    [Fact]
    public async Task Valid_credential_but_no_account_gets_empty_list()
    {
        var h = NewHarness();
        SignIn(h, Purchaser); // credential valid, but no account row created

        var result = Ok(await h.Controller.Entitlements(CancellationToken.None));

        Assert.Empty(result.Entitlements);
    }

    // AC-06: no credential -> 401, no entitlement state shown.
    [Fact]
    public async Task Unauthenticated_caller_gets_401()
    {
        var h = NewHarness();
        await h.Accounts.CreateOrGetAsync(Purchaser);
        await h.Grants.PutGrantAsync(Purchaser, new EntitlementGrant(EntitlementCatalog.LibraryFull, null, GrantSource.OneTime));
        // No Authorization header set.

        var action = await h.Controller.Entitlements(CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(action);
    }

    // AC-06: a tampered/garbage credential is treated as unauthenticated (401), never shows state.
    [Fact]
    public async Task Invalid_credential_gets_401()
    {
        var h = NewHarness();
        h.Controller.ControllerContext.HttpContext!.Request.Headers.Authorization = "Bearer not-a-real-credential";

        var action = await h.Controller.Entitlements(CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(action);
    }
}
