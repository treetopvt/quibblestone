// ----------------------------------------------------------------------------
//  AdminEntitlementsControllerTests - the OPERATOR grant / revoke of a purchaser
//  entitlement by email (sysadmin-console/02, issue #136). Two layers, EXACTLY
//  mirroring the story 01 / 03 admin test posture:
//
//    1. Direct controller tests over the REAL in-memory stores (InMemoryAccountStore
//       + InMemoryEntitlementGrantStore) plus the REAL StoredValueEntitlementService,
//       so story 02 is exercised end to end with ZERO Azure:
//         - AC-01: lookup a known email -> account + its grants; an unknown email ->
//           the clear not-found state (AccountExists = false), never an error.
//         - AC-02: granting a key writes an EntitlementGrant with source = Operator +
//           the given validThrough, READABLE by EvaluateForSession (same store).
//         - AC-03: revoking a key makes the NEXT EvaluateForSession read it locked,
//           while a SessionEntitlements captured BEFORE the revoke is unaffected.
//         - AC-06: granting the same key twice does NOT duplicate rows (upsert), and
//           there is no audit entity.
//    2. A WebApplicationFactory boundary walk (AC-05): an unauthenticated caller and a
//       genuine PURCHASER credential (minted on the app's own key ring under the
//       purchaser purpose) are BOTH rejected 401 by these Operator-policy endpoints;
//       an allowlisted operator credential is accepted.
//
//  ANONYMITY (AC-04): every assertion is on email + capability keys / leases. Nothing
//  here looks up, joins, or surfaces a player nickname, room code, or session.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuibbleStone.Api.Accounts;
using QuibbleStone.Api.Admin;
using QuibbleStone.Api.Controllers;
using QuibbleStone.Api.Entitlements;
using QuibbleStone.Api.Settings;

namespace QuibbleStone.Api.Tests.Admin;

public sealed class AdminEntitlementsControllerTests
{
    private const string Buyer = "buyer@example.com";
    private const string OperatorEmail = "ops@quibblestone.com";

    /// <summary>
    /// Builds a controller over fresh in-memory stores plus the REAL stored-value
    /// entitlement service reading the SAME grant store - so a grant / revoke through
    /// the controller is observable through EvaluateForSession, proving they share one
    /// write path (AC-02/AC-03). Also wires a fresh InMemoryOperatorActionLog (sysadmin-
    /// console/06) and a ClaimsPrincipal ControllerContext so User.Identity?.Name is
    /// non-null when Grant / Revoke append their action-log row.
    /// </summary>
    private static (AdminEntitlementsController Controller, IAccountStore Accounts, IEntitlementGrantStore Grants, IEntitlementService Entitlements, InMemoryOperatorActionLog ActionLog) NewSut()
    {
        var accounts = new InMemoryAccountStore();
        var grants = new InMemoryEntitlementGrantStore();
        var entitlements = new StoredValueEntitlementService(
            new DefaultUnlockedEntitlementService(), accounts, grants, TestSystemFlags.AllEnabled());
        var actionLog = new InMemoryOperatorActionLog();
        var controller = new AdminEntitlementsController(accounts, grants, actionLog)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = OperatorPrincipal() } },
        };
        return (controller, accounts, grants, entitlements, actionLog);
    }

    /// <summary>A ClaimsPrincipal shaped like the real operator credential (ClaimTypes.Name = the operator email).</summary>
    private static ClaimsPrincipal OperatorPrincipal() =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, OperatorEmail)], "Operator"));

    private static T Body<T>(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return Assert.IsType<T>(ok.Value);
    }

    // ---- AC-01: lookup ----------------------------------------------------------

    [Fact]
    public async Task Lookup_unknown_email_returns_a_clear_not_found_state()
    {
        var (controller, _, _, _, _) = NewSut();

        var result = await controller.Lookup("nobody@example.com", CancellationToken.None);

        var lookup = Body<PurchaserLookupResult>(result);
        Assert.False(lookup.AccountExists);
        Assert.Empty(lookup.Grants);
        // The email is echoed in its canonical (normalized) form, never an error.
        Assert.Equal("nobody@example.com", lookup.Email);
    }

    [Fact]
    public async Task Lookup_known_email_returns_the_account_and_its_grants()
    {
        var (controller, accounts, grants, _, _) = NewSut();
        var account = await accounts.CreateOrGetAsync(Buyer, CancellationToken.None);
        await grants.PutGrantAsync(
            account.Id,
            new EntitlementGrant(EntitlementCatalog.LibraryFull, null, GrantSource.Operator),
            CancellationToken.None);

        var result = await controller.Lookup(Buyer, CancellationToken.None);

        var lookup = Body<PurchaserLookupResult>(result);
        Assert.True(lookup.AccountExists);
        Assert.Equal(account.Email, lookup.Email);
        var grant = Assert.Single(lookup.Grants);
        Assert.Equal(EntitlementCatalog.LibraryFull, grant.CapabilityKey);
        Assert.Equal("Full Library", grant.Label);
        Assert.Null(grant.ValidThrough);
        Assert.Equal(GrantSource.Operator, grant.Source);
        Assert.True(grant.Active);
    }

    // ---- AC-02: grant writes a lease readable at session-creation ----------------

    [Fact]
    public async Task Grant_writes_an_operator_grant_readable_by_the_session_gate()
    {
        var (controller, _, _, entitlements, _) = NewSut();

        var result = await controller.Grant(
            Buyer,
            new GrantEntitlementRequest(EntitlementCatalog.PlayRemote, null),
            CancellationToken.None);

        var action = Body<EntitlementActionResult>(result);
        var grant = Assert.Single(action.Purchaser.Grants);
        Assert.Equal(EntitlementCatalog.PlayRemote, grant.CapabilityKey);
        Assert.Equal(GrantSource.Operator, grant.Source);

        // The SAME store the session-creation gate reads now unlocks the capability.
        var session = await entitlements.EvaluateForSession(Buyer, CancellationToken.None);
        Assert.True(session.IsUnlocked(EntitlementCatalog.PlayRemote));
    }

    [Fact]
    public async Task Grant_honours_an_operator_set_validThrough_expiry()
    {
        var (controller, _, _, entitlements, _) = NewSut();
        var past = DateTimeOffset.UtcNow.AddDays(-1);

        await controller.Grant(
            Buyer,
            new GrantEntitlementRequest(EntitlementCatalog.PlayLargeGroup, past),
            CancellationToken.None);

        // A lease whose validThrough already passed reads as locked (the lease end is
        // exclusive) - proving validThrough is honoured, not ignored.
        var session = await entitlements.EvaluateForSession(Buyer, CancellationToken.None);
        Assert.False(session.IsUnlocked(EntitlementCatalog.PlayLargeGroup));
    }

    [Fact]
    public async Task Grant_rejects_a_capability_key_outside_the_catalog()
    {
        var (controller, _, _, _, _) = NewSut();

        var result = await controller.Grant(
            Buyer,
            new GrantEntitlementRequest("totally.madeup", null),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Grant_accepts_a_pack_key_from_the_open_ended_pack_family()
    {
        var (controller, _, _, entitlements, _) = NewSut();
        var spooky = EntitlementCatalog.Pack("spooky");

        await controller.Grant(Buyer, new GrantEntitlementRequest(spooky, null), CancellationToken.None);

        var session = await entitlements.EvaluateForSession(Buyer, CancellationToken.None);
        Assert.True(session.IsUnlocked(spooky));
    }

    // ---- AC-03: revoke lapses the lease for the NEXT session only ----------------

    [Fact]
    public async Task Revoke_locks_the_capability_for_the_next_session_but_not_an_open_one()
    {
        var (controller, _, _, entitlements, _) = NewSut();
        await controller.Grant(
            Buyer,
            new GrantEntitlementRequest(EntitlementCatalog.LibraryFull, null),
            CancellationToken.None);

        // A session captured BEFORE the revoke - an immutable snapshot (AC-03's
        // "already-open session unaffected").
        var openSession = await entitlements.EvaluateForSession(Buyer, CancellationToken.None);
        Assert.True(openSession.IsUnlocked(EntitlementCatalog.LibraryFull));

        var result = await controller.Revoke(Buyer, EntitlementCatalog.LibraryFull, CancellationToken.None);
        Body<EntitlementActionResult>(result);

        // The NEXT session-creation check reads the capability as locked.
        var nextSession = await entitlements.EvaluateForSession(Buyer, CancellationToken.None);
        Assert.False(nextSession.IsUnlocked(EntitlementCatalog.LibraryFull));

        // The session captured before the revoke is unchanged (immutable snapshot).
        Assert.True(openSession.IsUnlocked(EntitlementCatalog.LibraryFull));
    }

    [Fact]
    public async Task Revoke_for_an_unknown_email_is_a_harmless_not_found_no_op()
    {
        var (controller, accounts, _, _, actionLog) = NewSut();

        var result = await controller.Revoke(
            "nobody@example.com", EntitlementCatalog.PlayRemote, CancellationToken.None);

        var action = Body<EntitlementActionResult>(result);
        Assert.False(action.Purchaser.AccountExists);
        // A revoke must NEVER mint an account (no create on miss).
        Assert.Null(await accounts.GetByIdentityAsync("nobody@example.com", CancellationToken.None));
    }

    // ---- AC-05 (sysadmin-console/06): a no-op writes NO action-log row -----------

    [Fact]
    public async Task Revoke_for_an_unknown_email_writes_no_action_log_row()
    {
        var (controller, _, _, _, actionLog) = NewSut();

        await controller.Revoke("nobody@example.com", EntitlementCatalog.PlayRemote, CancellationToken.None);

        Assert.Empty(actionLog.Entries);
    }

    // ---- AC-06: idempotent, low-ceremony ----------------------------------------

    [Fact]
    public async Task Granting_the_same_key_twice_does_not_duplicate_rows()
    {
        var (controller, accounts, grants, _, _) = NewSut();

        await controller.Grant(Buyer, new GrantEntitlementRequest(EntitlementCatalog.AiOnDemand, null), CancellationToken.None);
        await controller.Grant(Buyer, new GrantEntitlementRequest(EntitlementCatalog.AiOnDemand, null), CancellationToken.None);

        var account = await accounts.GetByIdentityAsync(Buyer, CancellationToken.None);
        Assert.NotNull(account);
        var stored = await grants.GetGrantsAsync(account!.Id, CancellationToken.None);
        // Upsert by capability key - exactly one row, not a pile.
        Assert.Single(stored);
        Assert.Equal(EntitlementCatalog.AiOnDemand, Assert.Single(stored).CapabilityKey);
    }

    // ---- AC-05: the admin boundary (over the REAL app) --------------------------

    [Fact]
    public async Task Endpoints_are_401_when_unauthenticated()
    {
        using var factory = new AdminApiFactory();
        var client = factory.CreateClient();

        var lookup = await client.GetAsync($"/api/admin/purchasers/{Buyer}");
        var grant = await client.PostAsJsonAsync(
            $"/api/admin/purchasers/{Buyer}/entitlements",
            new { capabilityKey = EntitlementCatalog.LibraryFull, validThrough = (DateTimeOffset?)null });
        var revoke = await client.DeleteAsync($"/api/admin/purchasers/{Buyer}/entitlements/{EntitlementCatalog.LibraryFull}");

        Assert.Equal(HttpStatusCode.Unauthorized, lookup.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, grant.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, revoke.StatusCode);
    }

    [Fact]
    public async Task PurchaserCredential_IsRejected_ByTheOperatorEndpoints()
    {
        using var factory = new AdminApiFactory();
        // A GENUINE purchaser credential on the app's own key ring - signed in as a
        // purchaser, still locked out of the admin surface (purchaser != operator).
        var purchaserCredential = factory.ProtectUnder(AccountsController.PurchaserSessionPurpose, Buyer);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", purchaserCredential);

        var response = await client.GetAsync($"/api/admin/purchasers/{Buyer}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AllowlistedOperator_CanLookupGrantAndRevoke_OverHttp()
    {
        using var factory = new AdminApiFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", factory.OperatorCredential());

        // 1. Lookup an email with no account -> the clear not-found state, 200 (not 404).
        var lookup = await client.GetAsync($"/api/admin/purchasers/{Buyer}");
        Assert.Equal(HttpStatusCode.OK, lookup.StatusCode);
        var before = await lookup.Content.ReadFromJsonAsync<PurchaserLookupResult>();
        Assert.NotNull(before);
        Assert.False(before!.AccountExists);

        // 2. Grant a capability -> the refreshed view shows an operator grant.
        var grant = await client.PostAsJsonAsync(
            $"/api/admin/purchasers/{Buyer}/entitlements",
            new { capabilityKey = EntitlementCatalog.PlayRemote, validThrough = (DateTimeOffset?)null });
        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);
        var granted = await grant.Content.ReadFromJsonAsync<EntitlementActionResult>();
        Assert.NotNull(granted);
        Assert.True(granted!.Purchaser.AccountExists);
        var row = Assert.Single(granted.Purchaser.Grants);
        Assert.Equal(EntitlementCatalog.PlayRemote, row.CapabilityKey);
        Assert.Equal(GrantSource.Operator, row.Source);

        // 3. Revoke it -> 200, and the refreshed grant reads inactive.
        var revoke = await client.DeleteAsync(
            $"/api/admin/purchasers/{Buyer}/entitlements/{EntitlementCatalog.PlayRemote}");
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        var revoked = await revoke.Content.ReadFromJsonAsync<EntitlementActionResult>();
        Assert.NotNull(revoked);
        var lapsed = Assert.Single(revoked!.Purchaser.Grants);
        Assert.False(lapsed.Active);
    }

    /// <summary>
    /// Boots the real API in memory with a configured operator allowlist (Development
    /// environment, matching the story 01 / 03 boundary tests). The in-memory account +
    /// grant stores are the app defaults with no storage connection string, so the walk
    /// exercises the SAME stores the session gate reads. The allowlist is supplied via
    /// in-memory configuration exactly as an App Service setting would (AC-05).
    /// </summary>
    private sealed class AdminApiFactory : WebApplicationFactory<Program>
    {
        private const string AllowlistedOperator = "ops@quibblestone.com";

        /// <summary>A genuine operator credential on the running app's own key ring.</summary>
        public string OperatorCredential() =>
            ProtectUnder(OperatorSession.OperatorSessionPurpose, AllowlistedOperator);

        /// <summary>Protects "email|issuedAt" under a purpose using the app's real key ring.</summary>
        public string ProtectUnder(string purpose, string email)
        {
            var provider = Services.GetRequiredService<IDataProtectionProvider>();
            var protector = provider.CreateProtector(purpose).ToTimeLimitedDataProtector();
            var payload = $"{email}|{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            return protector.Protect(payload, TimeSpan.FromHours(1));
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Operator:AllowedEmails:0"] = AllowlistedOperator,
                });
            });
        }
    }
}
