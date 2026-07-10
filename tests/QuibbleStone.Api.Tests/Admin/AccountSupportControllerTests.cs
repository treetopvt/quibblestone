// ----------------------------------------------------------------------------
//  AccountSupportControllerTests - the Support job's lookup + five verbs
//  (sysadmin-console/07, issue #243, ADR 0003 Layer 3). Direct controller tests over
//  the REAL in-memory stores plus tiny fakes for the seams that have no in-memory
//  impl, so every AC is exercised end to end with ZERO Azure:
//
//    - AC-01: lookup by email (and by AccountId) resolves the account shape; an
//      unknown email -> the clear not-found state; a claim-code-shaped / slug-shaped
//      value is NEVER resolved to an account (it is neither a GUID nor an email).
//    - AC-02: with a seam present, its section renders real data (the vault section is
//      a COUNT only, never a per-tale list); with a seam absent (the sentinel), its
//      section reports "unavailable" rather than throwing.
//    - AC-03: resend calls the IEmailSender/magic-link seam, writes ONE action-log row,
//      carries the SignInRateLimit per-IP policy, AND a burst of resends to the SAME
//      account is rejected past its per-account cap even from one operator/IP.
//    - AC-04: extend-TTL pushes the published store's expiry out, the response carries
//      only slug + new expiry (no byline field), and writes ONE action-log row.
//    - AC-05: self-delete restore resumes serving with a single confirmation and logs;
//      the vault id (a bearer secret) is NEVER the log target - the tale id is.
//    - AC-06: comp/extend reuses story 02's EXACT grant plumbing (the SAME
//      IEntitlementGrantStore) - a grant written through AdminEntitlementsController
//      surfaces in the support lookup; there is no second grant write path here.
//    - AC-07: resync invokes the service once per account per window and logs; a second
//      resync for the same account within the window is debounced (429).
//    - AC-08 (structural): the controller's constructor holds NO byline-bearing
//      dependency, its source imports nothing from Rooms/Hubs, and it contains zero
//      hits for IVaultStore.ListAsync or PublishedTale.Byline.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging.Abstractions;
using QuibbleStone.Api.Accounts;
using QuibbleStone.Api.Admin;
using QuibbleStone.Api.Billing;
using QuibbleStone.Api.Entitlements;
using QuibbleStone.Api.PublishedTales;
using QuibbleStone.Api.Settings;
using QuibbleStone.Api.Vault;

namespace QuibbleStone.Api.Tests.Admin;

public sealed class AccountSupportControllerTests
{
    private const string Buyer = "buyer@example.com";
    private const string OperatorEmail = "ops@quibblestone.com";

    /// <summary>The wired system-under-test plus the collaborators a test inspects.</summary>
    private sealed record Sut(
        AccountSupportController Controller,
        InMemoryAccountStore Accounts,
        InMemoryEntitlementGrantStore Grants,
        InMemoryVaultStore Vault,
        FakePublishedTaleStore Tales,
        FakeEmailSender Email,
        FakeReconciliation Reconciliation,
        InMemoryOperatorActionLog ActionLog);

    /// <summary>
    /// Builds a controller over the REAL in-memory account / grant / vault stores plus tiny fakes
    /// for the seams with no in-memory impl (the published-tale store, the Stripe reconciliation
    /// service, the email sender). <paramref name="vaultSummary"/> defaults to the dependency-tolerant
    /// sentinel (the "unavailable" state); a test passes a fake to exercise the "seam present" path.
    /// </summary>
    private static Sut NewSut(IVaultAccountSummary? vaultSummary = null)
    {
        var accounts = new InMemoryAccountStore();
        var grants = new InMemoryEntitlementGrantStore();
        var vault = new InMemoryVaultStore();
        var tales = new FakePublishedTaleStore();
        var email = new FakeEmailSender();
        var reconciliation = new FakeReconciliation();
        var actionLog = new InMemoryOperatorActionLog();
        var devices = new InMemoryFamilyDeviceTokenStore();

        var resend = new SupportMagicLinkResend(
            new FakeTokenService(), email, new EmailOptions(), NullLogger<SupportMagicLinkResend>.Instance);

        var controller = new AccountSupportController(
            accounts,
            grants,
            vaultSummary ?? new UnavailableVaultAccountSummary(),
            new LinkedDeviceCounter(devices),
            new PublishedTaleTtlExtender(tales),
            new VaultTaleRestorer(vault),
            reconciliation,
            resend,
            new SupportResendAccountThrottle(),
            new SupportResyncAccountThrottle(),
            actionLog)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = OperatorPrincipal() },
            },
        };
        return new Sut(controller, accounts, grants, vault, tales, email, reconciliation, actionLog);
    }

    /// <summary>A ClaimsPrincipal shaped like the real operator credential (ClaimTypes.Name = the operator email).</summary>
    private static ClaimsPrincipal OperatorPrincipal() =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, OperatorEmail)], "Operator"));

    private static T Body<T>(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return Assert.IsType<T>(ok.Value);
    }

    // ---- AC-01: lookup by email / AccountId, not-found, no slug/claim-code oracle ----

    [Fact]
    public async Task Lookup_unknown_email_returns_a_clear_not_found_state()
    {
        var sut = NewSut();

        var result = await sut.Controller.Lookup("nobody@example.com", CancellationToken.None);

        var summary = Body<SupportAccountSummary>(result);
        Assert.False(summary.AccountExists);
        Assert.Null(summary.AccountId);
        Assert.Empty(summary.Grants);
        Assert.Equal("nobody@example.com", summary.Email);
    }

    [Fact]
    public async Task Lookup_by_email_resolves_the_account_shape()
    {
        var sut = NewSut();
        var account = await sut.Accounts.CreateOrGetAsync(Buyer, CancellationToken.None);

        var summary = Body<SupportAccountSummary>(await sut.Controller.Lookup(Buyer, CancellationToken.None));

        Assert.True(summary.AccountExists);
        Assert.Equal(account.Id, summary.AccountId);
        Assert.Equal(account.Email, summary.Email);
        Assert.Equal(account.CreatedUtc, summary.CreatedUtc);
    }

    [Fact]
    public async Task Lookup_by_account_id_resolves_the_same_account()
    {
        var sut = NewSut();
        var account = await sut.Accounts.CreateOrGetAsync(Buyer, CancellationToken.None);

        var summary = Body<SupportAccountSummary>(
            await sut.Controller.Lookup(account.Id.ToString(), CancellationToken.None));

        Assert.True(summary.AccountExists);
        Assert.Equal(account.Id, summary.AccountId);
        Assert.Equal(account.Email, summary.Email);
    }

    [Theory]
    [InlineData("QW3RT-9KZ2P")]        // a claim-code shape
    [InlineData("aB3xK9mZ")]           // a public-tale slug shape
    [InlineData("not an email or guid")]
    public async Task Lookup_never_resolves_a_claim_code_or_slug_to_an_account(string bridgeInput)
    {
        var sut = NewSut();
        // Even with a real account present, a non-email / non-GUID search input NEVER resolves to it -
        // the firewall is structural (AC-01/AC-08), not a filtered special case.
        await sut.Accounts.CreateOrGetAsync(Buyer, CancellationToken.None);

        var summary = Body<SupportAccountSummary>(await sut.Controller.Lookup(bridgeInput, CancellationToken.None));

        Assert.False(summary.AccountExists);
        Assert.Null(summary.AccountId);
    }

    // ---- AC-02: dependency-tolerant sections (count-only, seam present vs absent) ----

    [Fact]
    public async Task Lookup_vault_count_reports_unavailable_when_the_seam_is_absent()
    {
        // The default sentinel (no keepsake-vault account-count projection yet).
        var sut = NewSut();
        await sut.Accounts.CreateOrGetAsync(Buyer, CancellationToken.None);

        var summary = Body<SupportAccountSummary>(await sut.Controller.Lookup(Buyer, CancellationToken.None));

        Assert.False(summary.VaultTales.Available);
        Assert.Null(summary.VaultTales.Count);
    }

    [Fact]
    public async Task Lookup_vault_count_renders_a_bare_count_when_the_seam_is_present()
    {
        // A present seam returns ONLY an integer - the section has no per-tale list to render.
        var sut = NewSut(new FakeVaultAccountSummary(7));
        await sut.Accounts.CreateOrGetAsync(Buyer, CancellationToken.None);

        var summary = Body<SupportAccountSummary>(await sut.Controller.Lookup(Buyer, CancellationToken.None));

        Assert.True(summary.VaultTales.Available);
        Assert.Equal(7, summary.VaultTales.Count);
    }

    [Fact]
    public async Task Lookup_linked_device_count_is_available_with_no_devices()
    {
        var sut = NewSut();
        await sut.Accounts.CreateOrGetAsync(Buyer, CancellationToken.None);

        var summary = Body<SupportAccountSummary>(await sut.Controller.Lookup(Buyer, CancellationToken.None));

        Assert.True(summary.LinkedDevices.Available);
        Assert.Equal(0, summary.LinkedDevices.Count);
    }

    [Fact]
    public async Task Lookup_surfaces_subscription_state_from_grant_metadata()
    {
        var sut = NewSut();
        var account = await sut.Accounts.CreateOrGetAsync(Buyer, CancellationToken.None);
        var through = DateTimeOffset.UtcNow.AddDays(20);
        await sut.Grants.PutGrantAsync(
            account.Id,
            new EntitlementGrant(
                EntitlementCatalog.LibraryFull, through, GrantSource.Subscription,
                PlanId: "family-plan", StripeSubscriptionId: "sub_123", Mode: StripeMode.Test),
            CancellationToken.None);

        var summary = Body<SupportAccountSummary>(await sut.Controller.Lookup(Buyer, CancellationToken.None));

        Assert.True(summary.Subscription.HasSubscription);
        Assert.Equal("family-plan", summary.Subscription.Plan);
        Assert.Equal("sub_123", summary.Subscription.StripeSubscriptionId);
        Assert.Equal("test", summary.Subscription.Mode);
        Assert.Equal("active", summary.Subscription.Status);
    }

    // ---- AC-03: resend magic link -------------------------------------------------

    [Fact]
    public async Task Resend_sends_one_link_and_writes_one_action_log_row()
    {
        var sut = NewSut();
        await sut.Accounts.CreateOrGetAsync(Buyer, CancellationToken.None);

        var response = Body<ResendLinkResponse>(
            await sut.Controller.ResendLink(new ResendLinkRequest(Buyer), CancellationToken.None));

        Assert.True(response.Ok);
        // The link went through the ONE accounts-identity/04 email transport (via the resend seam).
        Assert.Equal(1, sut.Email.MagicLinkSends);
        // Exactly one action-log row, targeting the account email (account-plane fact).
        var row = Assert.Single(sut.ActionLog.Entries);
        Assert.Equal("account.resend-link", row.Action);
        Assert.Equal(Buyer, row.Target);
        Assert.Equal(OperatorEmail, row.OperatorEmail);
    }

    [Fact]
    public async Task Resend_to_an_unknown_email_sends_nothing_and_logs_nothing()
    {
        var sut = NewSut();

        var response = Body<ResendLinkResponse>(
            await sut.Controller.ResendLink(new ResendLinkRequest("nobody@example.com"), CancellationToken.None));

        Assert.False(response.Ok);
        Assert.Equal(0, sut.Email.MagicLinkSends);
        Assert.Empty(sut.ActionLog.Entries);
    }

    [Fact]
    public async Task Resend_burst_to_the_same_account_is_capped_even_from_one_operator()
    {
        var sut = NewSut();
        await sut.Accounts.CreateOrGetAsync(Buyer, CancellationToken.None);

        // The per-account cap admits a handful within the window...
        for (var i = 0; i < SupportResendAccountThrottle.PermitLimit; i++)
        {
            var ok = await sut.Controller.ResendLink(new ResendLinkRequest(Buyer), CancellationToken.None);
            Assert.Equal(1, ((ResendLinkResponse)Assert.IsType<OkObjectResult>(ok).Value!).Ok ? 1 : 0);
        }

        // ...and rejects the next one with 429, WITHOUT another send or log row - even though every
        // call came from the SAME operator/IP (the per-IP limiter alone would not bound this).
        var capped = await sut.Controller.ResendLink(new ResendLinkRequest(Buyer), CancellationToken.None);
        var status = Assert.IsType<ObjectResult>(capped);
        Assert.Equal(StatusCodes.Status429TooManyRequests, status.StatusCode);
        Assert.Equal(SupportResendAccountThrottle.PermitLimit, sut.Email.MagicLinkSends);
        Assert.Equal(SupportResendAccountThrottle.PermitLimit, sut.ActionLog.Entries.Count);
    }

    [Fact]
    public void Resend_action_carries_the_public_SignInRateLimit_per_ip_policy()
    {
        // AC-03a: the resend rides the SAME per-IP policy the public request endpoint uses.
        var method = typeof(AccountSupportController).GetMethod(nameof(AccountSupportController.ResendLink))!;
        var attribute = method.GetCustomAttribute<EnableRateLimitingAttribute>();
        Assert.NotNull(attribute);
        Assert.Equal(SignInRateLimit.PolicyName, attribute!.PolicyName);
    }

    // ---- AC-04: extend a public tale's TTL ----------------------------------------

    [Fact]
    public async Task ExtendTtl_pushes_the_expiry_out_and_logs_one_row()
    {
        var sut = NewSut();
        var soon = DateTimeOffset.UtcNow.AddHours(1);
        sut.Tales.Seed(new PublishedTale(
            "slug123", "A tale", [], "carved by nobody", DateTimeOffset.UtcNow.AddDays(-29), soon));

        var response = Body<ExtendTaleTtlResponse>(
            await sut.Controller.ExtendTaleTtl(new ExtendTaleTtlRequest("slug123"), CancellationToken.None));

        Assert.Equal("extended", response.Outcome);
        Assert.Equal("slug123", response.Slug);
        Assert.NotNull(response.NewExpiryUtc);
        // The persisted expiry actually moved out past the original.
        Assert.True(sut.Tales.Stored["slug123"].ExpiresUtc > soon);
        // One action-log row, targeting the slug (a content-plane fact).
        var row = Assert.Single(sut.ActionLog.Entries);
        Assert.Equal("tale.extend-ttl", row.Action);
        Assert.Equal("slug123", row.Target);
    }

    [Fact]
    public void ExtendTtl_response_shape_carries_no_byline_field()
    {
        // AC-04/AC-08 structural: the response type exposes ONLY slug + expiry (+ outcome/message),
        // never a byline / author field.
        var names = typeof(ExtendTaleTtlResponse).GetProperties().Select(p => p.Name.ToLowerInvariant());
        Assert.DoesNotContain("byline", names);
        Assert.DoesNotContain("bylinenames", names);
        Assert.DoesNotContain("author", names);
    }

    [Fact]
    public async Task ExtendTtl_unknown_slug_is_a_clear_not_found()
    {
        var sut = NewSut();

        var response = Body<ExtendTaleTtlResponse>(
            await sut.Controller.ExtendTaleTtl(new ExtendTaleTtlRequest("ghost"), CancellationToken.None));

        Assert.Equal("not-found", response.Outcome);
    }

    // ---- AC-05: restore a user's own deleted keepsake -----------------------------

    [Fact]
    public async Task RestoreKeepsake_restores_a_soft_deleted_tale_and_logs_the_tale_id_not_the_vault_id()
    {
        var sut = NewSut();
        const string vaultId = "vault-abc";
        const string taleId = "tale-xyz";
        await sut.Vault.SaveAsync(
            new VaultTale(vaultId, taleId, "Keepsake", [], "carved by nobody", DateTimeOffset.UtcNow),
            CancellationToken.None);
        Assert.True(await sut.Vault.SoftDeleteAsync(vaultId, taleId, CancellationToken.None));

        var response = Body<RestoreKeepsakeResponse>(await sut.Controller.RestoreKeepsake(
            new RestoreKeepsakeRequest(vaultId, taleId, Confirm: true), CancellationToken.None));

        Assert.Equal("restored", response.Outcome);
        // The tale serves again.
        var live = await sut.Vault.ListAsync(vaultId, CancellationToken.None);
        Assert.Single(live);
        // The log targets the TALE ID, never the vault id (a bearer secret).
        var row = Assert.Single(sut.ActionLog.Entries);
        Assert.Equal("vault.restore", row.Action);
        Assert.Equal(taleId, row.Target);
        Assert.DoesNotContain(vaultId, row.Target);
        Assert.DoesNotContain(vaultId, row.Note);
    }

    [Fact]
    public async Task RestoreKeepsake_requires_the_confirmation()
    {
        var sut = NewSut();

        var result = await sut.Controller.RestoreKeepsake(
            new RestoreKeepsakeRequest("v", "t", Confirm: false), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(sut.ActionLog.Entries);
    }

    // ---- AC-06: comp/extend reuses story 02's EXACT grant plumbing ----------------

    [Fact]
    public async Task Support_lookup_reflects_a_grant_written_through_story_02s_grant_endpoint()
    {
        var sut = NewSut();
        // AC-06: the comp/extend verb reuses AdminEntitlementsController's grant plumbing (the SAME
        // IEntitlementGrantStore) - there is no second write path. A grant written through story 02's
        // controller surfaces verbatim in the support lookup.
        var entitlements = new AdminEntitlementsController(sut.Accounts, sut.Grants, sut.ActionLog)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = OperatorPrincipal() },
            },
        };
        await entitlements.Grant(Buyer, new GrantEntitlementRequest(EntitlementCatalog.LibraryFull, null), CancellationToken.None);

        var summary = Body<SupportAccountSummary>(await sut.Controller.Lookup(Buyer, CancellationToken.None));

        var grant = Assert.Single(summary.Grants);
        Assert.Equal(EntitlementCatalog.LibraryFull, grant.CapabilityKey);
        Assert.Equal(GrantSource.Operator, grant.Source);
    }

    // ---- AC-07: resync a subscription from Stripe ---------------------------------

    [Fact]
    public async Task Resync_invokes_the_service_once_and_logs()
    {
        var sut = NewSut();
        var account = await sut.Accounts.CreateOrGetAsync(Buyer, CancellationToken.None);

        var response = Body<SupportResyncResponse>(await sut.Controller.Resync(
            new SupportResyncRequest(account.Id), CancellationToken.None));

        Assert.True(response.AccountFound);
        Assert.Equal(1, sut.Reconciliation.Calls);
        var row = Assert.Single(sut.ActionLog.Entries);
        Assert.Equal("subscription.resync", row.Action);
        Assert.Equal(Buyer, row.Target);
    }

    [Fact]
    public async Task Resync_a_second_time_for_the_same_account_is_debounced()
    {
        var sut = NewSut();
        var account = await sut.Accounts.CreateOrGetAsync(Buyer, CancellationToken.None);

        await sut.Controller.Resync(new SupportResyncRequest(account.Id), CancellationToken.None);
        var second = await sut.Controller.Resync(new SupportResyncRequest(account.Id), CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(second);
        Assert.Equal(StatusCodes.Status429TooManyRequests, status.StatusCode);
        // The service was called ONCE (the debounce blocked the second click before Stripe), and only
        // the first resync logged.
        Assert.Equal(1, sut.Reconciliation.Calls);
        Assert.Single(sut.ActionLog.Entries);
    }

    [Fact]
    public async Task Resync_unknown_account_is_a_clear_not_found()
    {
        var sut = NewSut();

        var response = Body<SupportResyncResponse>(await sut.Controller.Resync(
            new SupportResyncRequest(Guid.NewGuid()), CancellationToken.None));

        Assert.False(response.AccountFound);
        Assert.Equal(0, sut.Reconciliation.Calls);
    }

    // ---- AC-08: the firewall is STRUCTURAL, not asserted --------------------------

    [Fact]
    public void Controller_constructor_holds_no_byline_bearing_dependency()
    {
        // The constructor injects only narrow count/enum/instant seams - never the byline-bearing
        // IVaultStore / IFamilyDeviceTokenStore / IPublishedTaleStore / IEmailSender.
        var ctor = Assert.Single(typeof(AccountSupportController).GetConstructors());
        var paramTypes = ctor.GetParameters().Select(p => p.ParameterType).ToHashSet();

        Assert.DoesNotContain(typeof(IVaultStore), paramTypes);
        Assert.DoesNotContain(typeof(IFamilyDeviceTokenStore), paramTypes);
        Assert.DoesNotContain(typeof(IPublishedTaleStore), paramTypes);
        Assert.DoesNotContain(typeof(IEmailSender), paramTypes);

        // ...and nothing from the Rooms / Hub plane.
        Assert.DoesNotContain(paramTypes, t => (t.Namespace ?? string.Empty).Contains("Rooms"));
        Assert.DoesNotContain(paramTypes, t => (t.Name).Contains("Hub"));
    }

    [Fact]
    public void Controller_source_has_zero_hits_for_the_forbidden_bridge_tokens()
    {
        // The AC-08 grep guard, run as a real assertion: the controller source contains no
        // IVaultStore.ListAsync, no PublishedTale.Byline / .BylineNames, and no Rooms/Hub import.
        var source = File.ReadAllText(ControllerSourcePath());

        Assert.DoesNotContain("IVaultStore.ListAsync", source);
        Assert.DoesNotContain(".Byline", source);
        Assert.DoesNotContain("PublishedTale.Byline", source);
        Assert.DoesNotContain("using QuibbleStone.Api.Rooms", source);
        Assert.DoesNotContain("GameHub", source);
    }

    /// <summary>Walks up from the test assembly to the repo root and returns the controller's source path.</summary>
    private static string ControllerSourcePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "QuibbleStone.slnx")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "api", "src", "Admin", "AccountSupportController.cs");
    }

    // ---- tiny fakes for the seams with no in-memory impl --------------------------

    /// <summary>A present vault-count seam returning a fixed integer (AC-02 "seam present").</summary>
    private sealed class FakeVaultAccountSummary(int count) : IVaultAccountSummary
    {
        public Task<int?> CountForAccountAsync(Guid accountId, CancellationToken ct = default) =>
            Task.FromResult<int?>(count);
    }

    /// <summary>An email sender that records how many magic links it delivered (AC-03).</summary>
    private sealed class FakeEmailSender : IEmailSender
    {
        public int MagicLinkSends { get; private set; }

        public Task SendMagicLinkAsync(string toEmail, string link, MagicLinkPurpose purpose, CancellationToken cancellationToken = default)
        {
            MagicLinkSends++;
            return Task.CompletedTask;
        }

        public Task SendGameInviteAsync(string toEmail, string joinLink, string roomCode, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    /// <summary>A token issuer that returns a fixed opaque token (the resend does not verify it here).</summary>
    private sealed class FakeTokenService : IMagicLinkTokenService
    {
        public string Issue(string subject, TimeSpan? lifetime = null) => $"token-for-{subject}";

        public Task<TokenVerification> TryVerifyAsync(string token, CancellationToken ct = default) =>
            Task.FromResult(TokenVerification.Failure);
    }

    /// <summary>A reconciliation service that records call count and returns a canned result (AC-07).</summary>
    private sealed class FakeReconciliation : IStripeReconciliationService
    {
        public int Calls { get; private set; }

        public Task<ResyncResult> ResyncAccountAsync(Account account, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new ResyncResult(
                BillingConfigured: true, ActiveMode: StripeMode.Test,
                Reconciled: 1, SkippedUnmatchedIdentity: 0, SkippedModeGuard: 0, SkippedNoMetadata: 0));
        }
    }

    /// <summary>
    /// A minimal in-memory published-tale store (there is no shipped in-memory impl - the real ones
    /// are Table Storage or the disabled no-op). Supports only what the TTL extender touches: IsEnabled,
    /// GetAsync, PublishAsync (an upsert). The moderation members throw - they are never reached here.
    /// </summary>
    private sealed class FakePublishedTaleStore : IPublishedTaleStore
    {
        public Dictionary<string, PublishedTale> Stored { get; } = new(StringComparer.Ordinal);

        public bool IsEnabled => true;

        public void Seed(PublishedTale tale) => Stored[tale.Slug] = tale;

        public Task PublishAsync(PublishedTale tale, CancellationToken cancellationToken = default)
        {
            Stored[tale.Slug] = tale;
            return Task.CompletedTask;
        }

        public Task<PublishedTale?> GetAsync(string slug, CancellationToken cancellationToken = default) =>
            Task.FromResult(Stored.TryGetValue(slug, out var tale) ? tale : null);

        public Task RevokeAsync(string slug, CancellationToken cancellationToken = default)
        {
            Stored.Remove(slug);
            return Task.CompletedTask;
        }

        public Task<TaleModerationState?> ReportAsync(string slug, int autoHideThreshold, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TaleModerationState> GetModerationAsync(string slug, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ReportedTaleView>> ListHiddenAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> ConfirmHiddenAsync(string slug, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> RestoreAsync(string slug, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> RestoreFromTakedownAsync(string slug, bool confirmedByOperator, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
