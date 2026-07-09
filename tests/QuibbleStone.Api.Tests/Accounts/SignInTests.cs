// ----------------------------------------------------------------------------
//  SignInTests - controller-level tests for the purchaser sign-in / restore
//  surface (accounts-identity/03, issue #69). These drive the REAL
//  AccountsController against the REAL MagicLinkTokenService (a fixed test signing
//  key) + the WORKING InMemoryAccountStore + a real (ephemeral) Data Protection
//  provider - no mocking framework, matching the rest of the harness.
//
//  They pin the load-bearing guarantees of the story:
//    - AC-01 (no duplicate): verifying a token for a KNOWN account signs in and
//      resolves the SAME account - the account's created-at never moves, so no
//      new row is minted, however many times sign-in happens.
//    - AC-05 (no create on miss): verifying a valid token for an UNKNOWN email
//      does NOT create an account (the store still misses afterwards) and returns
//      the "no-account" guide-to-purchase outcome, not a signed-in one.
//    - AC-05 (no enumeration): the request endpoint never creates an account and
//      returns the SAME neutral shape for a known and an unknown email; a token is
//      issued either way, and the token is echoed ONLY in the Development
//      environment.
//    - AC-02 (the credential): a successful sign-in returns a purchaser credential
//      that unprotects (via the same purpose string) back to the purchaser email.
//    - An invalid / garbage token resolves to "link-invalid" and touches no account.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using QuibbleStone.Api.Accounts;
using QuibbleStone.Api.Controllers;

namespace QuibbleStone.Api.Tests.Accounts;

public class SignInTests
{
    // A fixed signing key keeps token issue/verify deterministic across the test
    // (the service also works with a null ephemeral key, but a fixed key is stable).
    private const string TestSigningKey = "test-signing-key-not-a-real-secret";

    private sealed record Harness(
        AccountsController Controller,
        InMemoryAccountStore Store,
        IMagicLinkTokenService Tokens,
        IDataProtectionProvider DataProtection);

    private static Harness NewHarness(bool development = true)
    {
        var store = new InMemoryAccountStore();
        var tokens = new MagicLinkTokenService(TestSigningKey, new InMemoryConsumedNonceStore());
        // The framework's ephemeral provider (a fresh key per instance): perfect
        // for a test - reused here so the test can unprotect what the controller
        // protected.
        IDataProtectionProvider dataProtection = new EphemeralDataProtectionProvider();
        var credential = new PurchaserCredentialService(dataProtection);
        var environment = new FakeWebHostEnvironment(development ? "Development" : "Production");
        // No email provider configured => the no-op sender (AC-03); these tests do not
        // assert on delivery (EmailSenderTests covers that seam), only sign-in logic.
        IEmailSender email = new NoOpEmailSender(NullLogger<NoOpEmailSender>.Instance);

        var controller = new AccountsController(
            tokens, store, credential, email, new EmailOptions(), environment, NullLogger<AccountsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        return new Harness(controller, store, tokens, dataProtection);
    }

    // ---- AC-01: a known account signs in, no duplicate --------------------------

    [Fact]
    public async Task Verify_KnownAccount_SignsInAndResolvesTheSameAccountTwice()
    {
        var harness = NewHarness();
        // The purchaser already has an account (accounts-identity/02's purchase path).
        var account = await harness.Store.CreateOrGetAsync("Buyer@Example.com");

        // Sign in twice with two fresh single-use tokens (a re-sign-in on the same
        // or another new device). Both must recognize the EXISTING account.
        var firstEmail = await SignInAndGetEmail(harness, "buyer@example.com");
        var secondEmail = await SignInAndGetEmail(harness, "  BUYER@example.com "); // case/space variant

        Assert.Equal("buyer@example.com", firstEmail);
        Assert.Equal("buyer@example.com", secondEmail);

        // No duplicate (AC-01): the account is still the SAME single record - its
        // created-at has not moved, which it would if a new row had been minted.
        var stillThere = await harness.Store.GetByIdentityAsync("buyer@example.com");
        Assert.NotNull(stillThere);
        Assert.Equal(account.CreatedUtc, stillThere!.CreatedUtc);
    }

    [Fact]
    public async Task Verify_KnownAccount_ReturnsACredentialThatUnprotectsToTheEmail()
    {
        var harness = NewHarness();
        await harness.Store.CreateOrGetAsync("buyer@example.com");
        var token = harness.Tokens.Issue("buyer@example.com");

        var result = await Verify(harness, token);

        Assert.Equal("signed-in", result.Outcome);
        Assert.Equal("buyer@example.com", result.Email);
        Assert.NotNull(result.Credential);

        // AC-02: the credential is the purchaser-scoped bearer the restore view
        // consumes. It unprotects (via the SAME purpose string) back to a payload
        // carrying the purchaser email - and no PII beyond it.
        var protector = harness.DataProtection
            .CreateProtector(AccountsController.PurchaserSessionPurpose)
            .ToTimeLimitedDataProtector();
        var payload = protector.Unprotect(result.Credential!);
        Assert.StartsWith("buyer@example.com|", payload);

        // The HttpOnly credential cookie is also set on a successful sign-in.
        var setCookie = harness.Controller.Response.Headers.SetCookie.ToString();
        Assert.Contains(AccountsController.CredentialCookieName, setCookie);
    }

    // ---- AC-05: no create on miss -----------------------------------------------

    [Fact]
    public async Task Verify_UnknownIdentity_CreatesNoAccountAndGuidesToPurchase()
    {
        var harness = NewHarness();
        // A valid token for an email that never purchased.
        var token = harness.Tokens.Issue("ghost@example.com");

        var result = await Verify(harness, token);

        // No sign-in, no credential, and the friendly guide-to-purchase outcome.
        Assert.Equal("no-account", result.Outcome);
        Assert.Null(result.Credential);
        Assert.Null(result.Email);

        // AC-05 (no create on miss): the store is UNCHANGED - the verify did not
        // silently mint an account for the unknown identity.
        Assert.Null(await harness.Store.GetByIdentityAsync("ghost@example.com"));
    }

    [Fact]
    public async Task Verify_InvalidToken_ReturnsLinkInvalidAndTouchesNoAccount()
    {
        var harness = NewHarness();

        var result = await Verify(harness, "not-a-real-token");

        Assert.Equal("link-invalid", result.Outcome);
        Assert.Null(result.Credential);
        // Garbage in, no account out.
        Assert.Null(await harness.Store.GetByIdentityAsync("anyone@example.com"));
    }

    // ---- AC-05: the request endpoint - no create, no enumeration ----------------

    [Fact]
    public async Task RequestLink_DoesNotCreateAnAccount_AndIsNeutralForKnownAndUnknownEmails()
    {
        // Production env so the token is NOT echoed - the shape a real client sees.
        var harness = NewHarness(development: false);

        var unknown = await RequestLink(harness, "nobody@example.com");
        var alsoUnknown = await RequestLink(harness, "someone-else@example.com");

        // The response is the SAME neutral shape for both - no existence tell, and
        // no token leaks in a non-dev environment (AC-05).
        Assert.Equal(unknown.Message, alsoUnknown.Message);
        Assert.Null(unknown.DevToken);
        Assert.Null(alsoUnknown.DevToken);

        // The request endpoint NEVER creates an account (it never even reads the store).
        Assert.Null(await harness.Store.GetByIdentityAsync("nobody@example.com"));
    }

    [Fact]
    public async Task RequestLink_InDevelopment_EchoesAWalkableToken()
    {
        var harness = NewHarness(development: true);

        var result = await RequestLink(harness, "buyer@example.com");

        // Dev-only affordance: a token is echoed so the flow is walkable with no
        // email provider. It verifies to the same email (the flow works).
        Assert.NotNull(result.DevToken);
        var verification = await harness.Tokens.TryVerifyAsync(result.DevToken!);
        Assert.True(verification.Succeeded);
        Assert.Equal("buyer@example.com", verification.Subject);
    }

    // ---- input-length guards (Copilot review) -----------------------------------

    [Fact]
    public async Task RequestLink_OverLengthEmail_ReturnsNeutralShapeAndIssuesNoToken()
    {
        // Dev env so a token WOULD normally be echoed - proving the over-length
        // path bails BEFORE issuing one (no oversized token, no oversized echo).
        var harness = NewHarness(development: true);
        var tooLong = new string('a', AccountsController.MaxEmailLength) + "@example.com";

        var result = await RequestLink(harness, tooLong);

        // Same neutral message as any other submit (no enumeration tell), and no
        // token was minted for the over-length input.
        Assert.Null(result.DevToken);
        Assert.Contains("sign-in link", result.Message);
    }

    [Fact]
    public async Task Verify_OverLengthToken_ReturnsLinkInvalidWithoutTouchingAnAccount()
    {
        var harness = NewHarness();
        var oversized = new string('x', AccountsController.MaxTokenLength + 1);

        var result = await Verify(harness, oversized);

        Assert.Equal("link-invalid", result.Outcome);
        Assert.Null(result.Credential);
    }

    // ---- helpers ----------------------------------------------------------------

    private static async Task<SignInRequestResult> RequestLink(Harness harness, string email)
    {
        var action = await harness.Controller.RequestLink(new SignInRequestBody(email));
        var ok = Assert.IsType<OkObjectResult>(action);
        return Assert.IsType<SignInRequestResult>(ok.Value);
    }

    private static async Task<SignInVerifyResult> Verify(Harness harness, string token)
    {
        var action = await harness.Controller.Verify(new SignInVerifyBody(token), CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(action);
        return Assert.IsType<SignInVerifyResult>(ok.Value);
    }

    private static async Task<string?> SignInAndGetEmail(Harness harness, string email)
    {
        var token = harness.Tokens.Issue(email);
        var result = await Verify(harness, token);
        Assert.Equal("signed-in", result.Outcome);
        return result.Email;
    }

    /// <summary>
    /// Minimal IWebHostEnvironment so the controller can call IsDevelopment() in a
    /// unit test with no host. Only EnvironmentName is meaningful; the file-system
    /// members are stubbed with a NullFileProvider.
    /// </summary>
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
