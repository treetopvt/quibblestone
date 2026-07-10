// ----------------------------------------------------------------------------
//  SignUpTests - controller-level tests for the FREE FAMILY ACCOUNT sign-up
//  entry point (accounts-identity/07, issue #211). These drive the REAL
//  AccountsController against the REAL MagicLinkTokenService (a fixed test signing
//  key) + the WORKING InMemoryAccountStore + a real (ephemeral) Data Protection
//  provider - no mocking framework, matching the rest of the harness (copied from
//  SignInTests, which covers the DEFAULT "signin" intent; this file covers the
//  "signup" intent added on top of the SAME endpoints).
//
//  They pin the load-bearing guarantees of the story:
//    - AC-01: verifying a "signup"-intent token for a BRAND-NEW email CREATES a
//      free family account (via the SAME idempotent IAccountStore.CreateOrGetAsync
//      story 02 already built) holding email + created-at and ZERO grants - the
//      harness wires no grant store at all, so "zero grants" is structural here,
//      not merely asserted; the entitlement regression (AC-05) lives in
//      StoredValueEntitlementServiceTests.cs and is not duplicated.
//    - AC-03 (no duplicate): a "signup" verify for an email that ALREADY has an
//      account (purchaser-created or free-created) resolves to that SAME account -
//      its created-at never moves, so no second row is minted.
//    - AC-03 (create-or-attach): the two orderings both resolve to ONE account -
//      free-signup-then-purchase, and purchase-then-free-signup.
//    - Case/space normalization mirrors SignInTests: the same identity however it
//      is typed.
//    - The request endpoint stays neutral and creates nothing on the signup intent
//      either, exactly like the default intent.
//    - Regression: the DEFAULT (null) intent still does not create on a miss -
//      SignInTests owns the full coverage of that path; this is one guard rail
//      confirming the new intent branch did not change the old one.
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
using QuibbleStone.Api.Safety;

namespace QuibbleStone.Api.Tests.Accounts;

public class SignUpTests
{
    // A fixed signing key keeps token issue/verify deterministic across the test
    // (the service also works with a null ephemeral key, but a fixed key is stable).
    private const string TestSigningKey = "test-signing-key-not-a-real-secret";

    private sealed record Harness(
        AccountsController Controller,
        InMemoryAccountStore Store,
        IMagicLinkTokenService Tokens,
        IDataProtectionProvider DataProtection,
        RecordingEmailSender Email);

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
        // A recording sender captures the delivered link + purpose so the intent
        // round-trip (the &intent=signup the followed link must carry) is assertable
        // (Copilot review). It does not otherwise send - behaves like the no-op sender.
        var email = new RecordingEmailSender();

        var deviceTokens = new InMemoryFamilyDeviceTokenStore();
        var controller = new AccountsController(
            tokens, store, credential, email, new EmailOptions(), environment,
            new FamilyDeviceLinkService(new InMemoryFamilyLinkCodeStore(), deviceTokens),
            deviceTokens,
            new AdultSignalResolutionService(credential, new FamilyDeviceLinkService(new InMemoryFamilyLinkCodeStore(), deviceTokens)),
            new FamilyDeviceRedeemGlobalThrottle(),
            NullLogger<AccountsController>.Instance,
            new InMemorySeatPresetStore(), new ContentSafetyFilter())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        return new Harness(controller, store, tokens, dataProtection, email);
    }

    // ---- AC-01: brand-new email, signup intent creates exactly one account ------

    [Fact]
    public async Task Verify_SignUpIntent_NewEmail_CreatesAFreeAccountAndSignsIn()
    {
        var harness = NewHarness();

        // Nothing exists for this email yet.
        Assert.Null(await harness.Store.GetByIdentityAsync("newfamily@example.com"));

        var token = harness.Tokens.Issue("newfamily@example.com");
        var result = await Verify(harness, token, AccountsController.SignUpIntent);

        Assert.Equal("signed-in", result.Outcome);
        Assert.NotNull(result.Credential);
        Assert.Equal("newfamily@example.com", result.Email);

        // Exactly one account now exists, created by this call.
        var account = await harness.Store.GetByIdentityAsync("newfamily@example.com");
        Assert.NotNull(account);
        Assert.Equal("newfamily@example.com", account!.Email);

        // Zero-grants is structural here: the harness wires no grant/entitlement
        // store at all, and CreateOrGetAsync (accounts-identity/02) only ever writes
        // the Account row (email + created-at) - it has no code path that could
        // touch a grant. The entitlement-level regression (a fresh free account
        // resolving to the default-unlocked baseline) lives in
        // StoredValueEntitlementServiceTests.cs and is not duplicated here.
    }

    // ---- AC-03: no duplicate for an existing purchaser ---------------------------

    [Fact]
    public async Task Verify_SignUpIntent_ExistingPurchaserAccount_ResolvesToTheSameAccount()
    {
        var harness = NewHarness();
        // The email already has an account, created via the purchase path.
        var existing = await harness.Store.CreateOrGetAsync("buyer@example.com");

        var token = harness.Tokens.Issue("buyer@example.com");
        var result = await Verify(harness, token, AccountsController.SignUpIntent);

        Assert.Equal("signed-in", result.Outcome);
        Assert.Equal("buyer@example.com", result.Email);

        // No duplicate: the SAME single record - created-at has not moved, which it
        // would if a second row had been minted.
        var stillThere = await harness.Store.GetByIdentityAsync("buyer@example.com");
        Assert.NotNull(stillThere);
        Assert.Equal(existing.Id, stillThere!.Id);
        Assert.Equal(existing.CreatedUtc, stillThere.CreatedUtc);
    }

    // ---- AC-03: create-or-attach, free-then-purchase ------------------------------

    [Fact]
    public async Task FreeSignUpThenLaterPurchase_AttachesToTheSameAccount()
    {
        var harness = NewHarness();

        // A free family sign-up creates the account first.
        var token = harness.Tokens.Issue("laterbuyer@example.com");
        var signUpResult = await Verify(harness, token, AccountsController.SignUpIntent);
        Assert.Equal("signed-in", signUpResult.Outcome);

        var freeAccount = await harness.Store.GetByIdentityAsync("laterbuyer@example.com");
        Assert.NotNull(freeAccount);

        // A subsequent purchase for the SAME email attaches to the existing free
        // account rather than minting a second one (accounts-identity/02's
        // CreateOrGetAsync idempotency, exercised here as create-or-attach).
        var purchaseAccount = await harness.Store.CreateOrGetAsync("laterbuyer@example.com");

        Assert.Equal(freeAccount!.Id, purchaseAccount.Id);
        Assert.Equal(freeAccount.CreatedUtc, purchaseAccount.CreatedUtc);

        // Exactly one account, still.
        var afterwards = await harness.Store.GetByIdentityAsync("laterbuyer@example.com");
        Assert.Equal(freeAccount.Id, afterwards!.Id);
    }

    // ---- case / space normalization ------------------------------------------------

    [Fact]
    public async Task Verify_SignUpIntent_NormalizesCaseAndWhitespaceToOneAccount()
    {
        var harness = NewHarness();

        // Sign up with a casing/whitespace variant.
        var token = harness.Tokens.Issue("  Buyer@Example.com ");
        var signUpResult = await Verify(harness, token, AccountsController.SignUpIntent);
        Assert.Equal("signed-in", signUpResult.Outcome);
        Assert.Equal("buyer@example.com", signUpResult.Email);

        // A later purchase-path create-or-get for the plain lowercase form resolves
        // to the SAME account - one row, not two.
        var purchaseAccount = await harness.Store.CreateOrGetAsync("buyer@example.com");
        var byIdentity = await harness.Store.GetByIdentityAsync("buyer@example.com");

        Assert.NotNull(byIdentity);
        Assert.Equal(byIdentity!.Id, purchaseAccount.Id);
        Assert.Equal(byIdentity.CreatedUtc, purchaseAccount.CreatedUtc);
    }

    // ---- request endpoint stays neutral on the signup intent ----------------------

    [Fact]
    public async Task RequestLink_SignUpIntent_IsNeutralAndCreatesNoAccount()
    {
        // Production env so the token is NOT echoed - the shape a real client sees.
        var harness = NewHarness(development: false);

        var unknown = await RequestLink(harness, "nobody@example.com", AccountsController.SignUpIntent);
        var alsoUnknown = await RequestLink(harness, "someone-else@example.com", AccountsController.SignUpIntent);

        // Same neutral shape for both - no existence tell, and no token leaks in a
        // non-dev environment (AC-02, mirroring the default sign-in intent).
        Assert.Equal(unknown.Message, alsoUnknown.Message);
        Assert.Null(unknown.DevToken);
        Assert.Null(alsoUnknown.DevToken);

        // The request endpoint NEVER creates an account on either intent.
        Assert.Null(await harness.Store.GetByIdentityAsync("nobody@example.com"));
    }

    [Fact]
    public async Task RequestLink_SignUpIntent_InDevelopment_EchoesAWalkableToken()
    {
        var harness = NewHarness(development: true);

        var result = await RequestLink(harness, "newfamily@example.com", AccountsController.SignUpIntent);

        // Dev-only affordance: a token is echoed so the flow is walkable with no
        // email provider. It verifies to the same email (the flow works), and it
        // still creates NO account by itself (only Verify creates, on follow).
        Assert.NotNull(result.DevToken);
        var verification = await harness.Tokens.TryVerifyAsync(result.DevToken!);
        Assert.True(verification.Succeeded);
        Assert.Equal("newfamily@example.com", verification.Subject);
        Assert.Null(await harness.Store.GetByIdentityAsync("newfamily@example.com"));
    }

    // ---- the emailed link carries the intent across the round-trip ----------------

    [Fact]
    public async Task RequestLink_SignUpIntent_DeliversALinkCarryingIntentSignup()
    {
        // The followed link must keep the signup intent so a family sign-up still
        // creates on verify AFTER the email round-trip (the token itself is intent-
        // agnostic). Assert the delivered link embeds the intent query param.
        var harness = NewHarness();

        await RequestLink(harness, "newfamily@example.com", AccountsController.SignUpIntent);

        var send = Assert.Single(harness.Email.Sends);
        Assert.Equal(MagicLinkPurpose.FamilySignUp, send.Purpose);
        Assert.Contains($"intent={AccountsController.SignUpIntent}", send.Link);
    }

    [Fact]
    public async Task RequestLink_DefaultIntent_DeliversALinkWithoutTheSignupIntent()
    {
        // The default (purchaser sign-in) link must NOT carry intent=signup, so a
        // followed default link keeps the story-03 no-create-on-miss behavior.
        var harness = NewHarness();

        await RequestLink(harness, "buyer@example.com", intent: null);

        var send = Assert.Single(harness.Email.Sends);
        Assert.Equal(MagicLinkPurpose.PurchaserSignIn, send.Purpose);
        Assert.DoesNotContain($"intent={AccountsController.SignUpIntent}", send.Link);
    }

    // ---- regression: default intent still does not create on a miss --------------

    [Fact]
    public async Task Verify_DefaultIntent_UnknownEmail_StillReturnsNoAccountAndCreatesNothing()
    {
        // The signup intent's new create-on-miss branch must not have leaked into
        // the default (null-intent) sign-in path - full coverage of that path lives
        // in SignInTests; this is a one-shot guard against a regression here.
        var harness = NewHarness();
        var token = harness.Tokens.Issue("ghost@example.com");

        var result = await Verify(harness, token, intent: null);

        Assert.Equal("no-account", result.Outcome);
        Assert.Null(result.Credential);
        Assert.Null(await harness.Store.GetByIdentityAsync("ghost@example.com"));
    }

    // ---- helpers ----------------------------------------------------------------

    private static async Task<SignInRequestResult> RequestLink(Harness harness, string email, string? intent)
    {
        var action = await harness.Controller.RequestLink(new SignInRequestBody(email, intent));
        var ok = Assert.IsType<OkObjectResult>(action);
        return Assert.IsType<SignInRequestResult>(ok.Value);
    }

    private static async Task<SignInVerifyResult> Verify(Harness harness, string token, string? intent)
    {
        var action = await harness.Controller.Verify(new SignInVerifyBody(token, intent), CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(action);
        return Assert.IsType<SignInVerifyResult>(ok.Value);
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

    /// <summary>
    /// A capturing <see cref="IEmailSender"/> that records each delivered magic link
    /// (and its purpose) instead of sending, so a test can assert the intent query
    /// param the followed link must carry. Game invites are irrelevant here and are
    /// captured-but-ignored. Never throws (mirrors the no-op sender's fail-safe shape).
    /// </summary>
    private sealed class RecordingEmailSender : IEmailSender
    {
        public List<(string Email, string Link, MagicLinkPurpose Purpose)> Sends { get; } = new();

        public Task SendMagicLinkAsync(
            string toEmail,
            string link,
            MagicLinkPurpose purpose,
            CancellationToken cancellationToken = default)
        {
            Sends.Add((toEmail, link, purpose));
            return Task.CompletedTask;
        }

        public Task SendGameInviteAsync(
            string toEmail,
            string joinLink,
            string roomCode,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
