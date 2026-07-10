// ----------------------------------------------------------------------------
//  AdultSignalTests - the SHARED adult-signal resolver + the read-only
//  GET /api/accounts/adult-signal endpoint that solo play calls on mount
//  (accounts-identity/10, issue #247).
//
//  These drive the REAL AdultSignalResolutionService and the REAL
//  AccountsController against the REAL in-memory stores + an ephemeral Data
//  Protection provider (no mocking framework, matching the rest of the harness),
//  pinning the story's load-bearing, child-safety guarantees:
//    - AC-01 (anonymous solo is safe): no credential -> false.
//    - AC-02 (an adult signal unlocks): a valid purchaser credential -> true;
//      a family-device token whose row is adult-confirmed -> true.
//    - AC-03 (a linked-but-unconfirmed device stays safe): a freshly redeemed
//      device (IsAdultConfirmedDevice = false, the AC-02-of-09 default) -> false.
//    - AC-04 (fail-safe): a garbage / unresolvable credential -> false, never a throw.
//    - AC-05 (no PII, one field): the endpoint's response body is EXACTLY one
//      boolean property and nothing else; the bool is resolved server-side and
//      cannot be asserted by any request field.
//    - AC-06 (one resolver): the endpoint routes through the SAME resolver TYPE
//      GameHub.OnConnectedAsync uses - proven by exercising that shared service
//      here directly (a code-level check, per the story's Tests table, confirms
//      the hub call site; GameHubEntitlementTests exercises the hub half).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using QuibbleStone.Api.Accounts;
using QuibbleStone.Api.Controllers;
using QuibbleStone.Api.Safety;

namespace QuibbleStone.Api.Tests.Accounts;

public class AdultSignalTests
{
    private const string TestSigningKey = "test-signing-key-not-a-real-secret";

    // The shared building blocks a resolver (and the controller) resolve against.
    // Everything is wired over the SAME instances so a purchaser token or a device
    // token minted in a test resolves the SAME adult signal the resolver returns.
    private sealed record Fixture(
        AdultSignalResolutionService Resolver,
        PurchaserCredentialService Credentials,
        FamilyDeviceLinkService DeviceLinks,
        InMemoryFamilyDeviceTokenStore DeviceTokens,
        InMemoryAccountStore Accounts);

    private static Fixture NewFixture()
    {
        var credentials = new PurchaserCredentialService(new EphemeralDataProtectionProvider());
        var deviceTokens = new InMemoryFamilyDeviceTokenStore();
        var deviceLinks = new FamilyDeviceLinkService(new InMemoryFamilyLinkCodeStore(), deviceTokens);
        var accounts = new InMemoryAccountStore();
        var resolver = new AdultSignalResolutionService(credentials, deviceLinks);
        return new Fixture(resolver, credentials, deviceLinks, deviceTokens, accounts);
    }

    // Mint a redeemed family-device token for a fresh family, returning the raw token
    // (defaults to IsAdultConfirmedDevice = false, story 09 AC-02's safe default).
    private static async Task<string> RedeemDeviceTokenAsync(Fixture f, string email)
    {
        var account = await f.Accounts.CreateOrGetAsync(email);
        var (code, _) = f.DeviceLinks.MintLinkCode(account.Id);
        var redeem = await f.DeviceLinks.RedeemAsync(code);
        Assert.True(redeem.Success);
        return redeem.RawToken!;
    }

    // Flip the (single) device row for a family to adult-confirmed = true.
    private static async Task ConfirmDeviceAsync(Fixture f, string email)
    {
        var account = await f.Accounts.GetByIdentityAsync(email);
        var row = (await f.DeviceTokens.ListByAccountAsync(account!.Id)).Single();
        Assert.True(await f.DeviceTokens.UpdateAsync(row with { IsAdultConfirmedDevice = true }));
    }

    // ---- The resolver (the shared decision GameHub also routes through, AC-06) ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Resolver_with_no_credential_resolves_false(string? credential)
    {
        // AC-01: the overwhelming common case - a kid's tablet / a fresh or incognito
        // browser carries no credential, so there is no adult signal at all.
        var f = NewFixture();
        Assert.False(await f.Resolver.ResolveAdultSignalAsync(credential));
    }

    [Theory]
    [InlineData("not-a-real-token")]
    [InlineData("bad token !!")]
    [InlineData("%%%not base64url%%%")]
    public async Task Resolver_with_a_garbage_credential_resolves_false_without_throwing(string badCredential)
    {
        // AC-04 (fail-safe): a credential that is neither a valid purchaser credential
        // nor a valid family-device token resolves to false - never a throw, never a
        // default-open. Covers a non-base64url token too (the purchaser decode throws a
        // FormatException the resolver must swallow, exactly like OnConnectedAsync).
        var f = NewFixture();
        Assert.False(await f.Resolver.ResolveAdultSignalAsync(badCredential));
    }

    [Fact]
    public async Task Resolver_with_a_valid_purchaser_credential_resolves_true()
    {
        // AC-02: a signed-in purchaser is adult-by-construction (only an adult completes
        // a magic-link sign-in, ADR 0002 Decision A) - their credential unlocks.
        var f = NewFixture();
        var credential = f.Credentials.Protect("buyer@example.com");

        Assert.True(await f.Resolver.ResolveAdultSignalAsync(credential));
    }

    [Fact]
    public async Task Resolver_with_a_freshly_redeemed_device_resolves_false()
    {
        // AC-03: a linked device defaults to IsAdultConfirmedDevice = false (story 09
        // AC-02's safe default), so it stays family-safe until an adult opts it in -
        // the SAME "redeemed device defaults to SAFE" posture group play's AC-07b sets.
        var f = NewFixture();
        var token = await RedeemDeviceTokenAsync(f, "family@example.com");

        Assert.False(await f.Resolver.ResolveAdultSignalAsync(token));
    }

    [Fact]
    public async Task Resolver_with_an_adult_confirmed_device_resolves_true()
    {
        // AC-02: an adult flipped this device's adult-confirm toggle from the Account
        // page (story 09 AC-07b), so the device now carries a real adult signal.
        var f = NewFixture();
        var token = await RedeemDeviceTokenAsync(f, "family@example.com");
        await ConfirmDeviceAsync(f, "family@example.com");

        Assert.True(await f.Resolver.ResolveAdultSignalAsync(token));
    }

    [Fact]
    public async Task Resolver_with_a_revoked_device_resolves_false()
    {
        // Fail-safe defence in depth: even an adult-confirmed device stops resolving the
        // moment it is revoked - a dead token is no adult signal (AC-04), family-safe.
        var f = NewFixture();
        var token = await RedeemDeviceTokenAsync(f, "family@example.com");
        await ConfirmDeviceAsync(f, "family@example.com");
        var account = await f.Accounts.GetByIdentityAsync("family@example.com");
        var row = (await f.DeviceTokens.ListByAccountAsync(account!.Id)).Single();
        Assert.True(await f.DeviceTokens.UpdateAsync(row with { Revoked = true }));

        Assert.False(await f.Resolver.ResolveAdultSignalAsync(token));
    }

    // ---- The endpoint (GET /api/accounts/adult-signal) --------------------------

    private static AccountsController NewController(Fixture f, string? bearer)
    {
        var httpContext = new DefaultHttpContext();
        if (bearer is not null)
        {
            httpContext.Request.Headers.Authorization = $"Bearer {bearer}";
        }

        return new AccountsController(
            new MagicLinkTokenService(TestSigningKey, new InMemoryConsumedNonceStore()),
            f.Accounts,
            f.Credentials,
            new NoOpEmailSender(NullLogger<NoOpEmailSender>.Instance),
            new EmailOptions(),
            new FakeWebHostEnvironment("Development"),
            f.DeviceLinks,
            f.DeviceTokens,
            f.Resolver,
            new FamilyDeviceRedeemGlobalThrottle(),
            NullLogger<AccountsController>.Instance,
            new InMemorySeatPresetStore(),
            new ContentSafetyFilter())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };
    }

    private static AdultSignalResult GetSignal(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return Assert.IsType<AdultSignalResult>(ok.Value);
    }

    [Fact]
    public async Task Endpoint_with_no_credential_returns_adultUnlocked_false()
    {
        // AC-01: an anonymous solo device (no bearer, no cookie) is family-safe.
        var f = NewFixture();
        var controller = NewController(f, bearer: null);

        var result = await controller.GetAdultSignal(default);

        Assert.False(GetSignal(result).AdultUnlocked);
    }

    [Fact]
    public async Task Endpoint_with_a_purchaser_bearer_returns_adultUnlocked_true()
    {
        // AC-02: the endpoint resolves the purchaser bearer through the SAME resolver
        // (AC-06) and reports the unlock.
        var f = NewFixture();
        var controller = NewController(f, bearer: f.Credentials.Protect("buyer@example.com"));

        var result = await controller.GetAdultSignal(default);

        Assert.True(GetSignal(result).AdultUnlocked);
    }

    [Fact]
    public async Task Endpoint_with_an_adult_confirmed_device_bearer_returns_adultUnlocked_true()
    {
        var f = NewFixture();
        var token = await RedeemDeviceTokenAsync(f, "family@example.com");
        await ConfirmDeviceAsync(f, "family@example.com");
        var controller = NewController(f, bearer: token);

        var result = await controller.GetAdultSignal(default);

        Assert.True(GetSignal(result).AdultUnlocked);
    }

    [Fact]
    public async Task Endpoint_with_an_unconfirmed_device_bearer_returns_adultUnlocked_false()
    {
        // AC-03: a linked-but-unconfirmed device is family-safe through the endpoint too.
        var f = NewFixture();
        var token = await RedeemDeviceTokenAsync(f, "family@example.com");
        var controller = NewController(f, bearer: token);

        var result = await controller.GetAdultSignal(default);

        Assert.False(GetSignal(result).AdultUnlocked);
    }

    [Fact]
    public void Response_body_carries_exactly_one_boolean_field_and_no_PII()
    {
        // AC-05 (structural): the response shape is EXACTLY { adultUnlocked: bool } -
        // no account id, email, device-token id, or capability list could ride along,
        // because the type has no other property to put one in. (A record's protected
        // EqualityContract is not a public property, so it is not counted.)
        var properties = typeof(AdultSignalResult)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance);

        var single = Assert.Single(properties);
        Assert.Equal("AdultUnlocked", single.Name);
        Assert.Equal(typeof(bool), single.PropertyType);
    }

    private sealed class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public FakeWebHostEnvironment(string environmentName) => EnvironmentName = environmentName;

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "QuibbleStone.Api.Tests";
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
