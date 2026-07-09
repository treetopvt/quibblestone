// ----------------------------------------------------------------------------
//  OperatorLoginTests - controller-level tests for the operator back-office login
//  surface (sysadmin-console/01, issue #135). These drive the REAL
//  OperatorLoginController against the REAL, SHIPPED MagicLinkTokenService (a fixed
//  test signing key) + a fake config-backed allowlist + a real (ephemeral) Data
//  Protection provider - no mocking framework, matching the rest of the harness and
//  mirroring SignInTests.
//
//  They pin the load-bearing guarantees of the story:
//    - AC-01: an ALLOWLISTED operator email -> verify a fresh single-use token ->
//      establishes an operator session (a credential that unprotects, via the
//      DEDICATED operator purpose, back to the operator email).
//    - AC-02 (allowlist at VERIFY, not issue): the request endpoint issues a token
//      for ANY email WITHOUT consulting the allowlist (no enumeration, same neutral
//      shape), and a valid token for a NON-allowlisted email resolves to
//      "not-authorized" with NO credential - possessing a valid link alone never
//      grants operator scope.
//    - AC-07: the login flow collects nothing beyond the email (the credential
//      unprotects to just the operator email + an issued-at stamp).
//    - An invalid / garbage token resolves to "link-invalid" and mints no session.
//
//  The endpoint-level HTTP boundary (AC-03 negative test, AC-06) lives in
//  OperatorAuthorizationTests (WebApplicationFactory).
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
using QuibbleStone.Api.Admin;

namespace QuibbleStone.Api.Tests.Admin;

public class OperatorLoginTests
{
    private const string TestSigningKey = "test-operator-signing-key-not-a-real-secret";
    private const string AllowlistedOperator = "ops@quibblestone.com";

    private sealed record Harness(
        OperatorLoginController Controller,
        IMagicLinkTokenService Tokens,
        IDataProtectionProvider DataProtection);

    private static Harness NewHarness(bool development = true)
    {
        var tokens = new MagicLinkTokenService(TestSigningKey, new InMemoryConsumedNonceStore());
        IDataProtectionProvider dataProtection = new EphemeralDataProtectionProvider();
        var allowlist = new FakeOperatorAllowlist(AllowlistedOperator);
        var environment = new FakeWebHostEnvironment(development ? "Development" : "Production");
        // No email provider configured => the no-op sender (AC-03); these tests do not
        // assert on delivery (EmailSenderTests covers that seam), only login logic.
        IEmailSender email = new NoOpEmailSender(NullLogger<NoOpEmailSender>.Instance);

        var controller = new OperatorLoginController(
            tokens, allowlist, dataProtection, email, new EmailOptions(), environment, NullLogger<OperatorLoginController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        return new Harness(controller, tokens, dataProtection);
    }

    // ---- AC-01: an allowlisted operator establishes a session -------------------

    [Fact]
    public async Task Verify_AllowlistedOperator_EstablishesAnOperatorSession()
    {
        var harness = NewHarness();
        var token = harness.Tokens.Issue(AllowlistedOperator);

        var result = await Verify(harness, token);

        Assert.Equal("signed-in", result.Outcome);
        Assert.Equal(AllowlistedOperator, result.Email);
        Assert.NotNull(result.Credential);

        // AC-01/AC-07: the credential unprotects (via the DEDICATED operator purpose)
        // back to a payload carrying ONLY the operator email - no other identity.
        Assert.True(OperatorSession.TryUnprotect(harness.DataProtection, result.Credential, out var email));
        Assert.Equal(AllowlistedOperator, email);

        // The HttpOnly operator cookie is also set on a successful sign-in.
        var setCookie = harness.Controller.Response.Headers.SetCookie.ToString();
        Assert.Contains(OperatorSession.CookieName, setCookie);
    }

    [Fact]
    public async Task Verify_AllowlistedOperator_NormalizesCaseAndWhitespace()
    {
        var harness = NewHarness();
        // A link issued for a case / whitespace variant still resolves to the one
        // normalized operator identity (matches the account store's normalization).
        var token = harness.Tokens.Issue("  OPS@Quibblestone.com ");

        var result = await Verify(harness, token);

        Assert.Equal("signed-in", result.Outcome);
        Assert.Equal(AllowlistedOperator, result.Email);
    }

    // ---- AC-02: allowlist at VERIFY, not issue ----------------------------------

    [Fact]
    public async Task Verify_ValidTokenButNotAnOperator_IsRejectedWithNoCredential()
    {
        var harness = NewHarness();
        // A perfectly valid, single-use link - but for a non-operator email. Proof
        // of inbox control is NOT authorization (AC-02).
        var token = harness.Tokens.Issue("random.purchaser@example.com");

        var result = await Verify(harness, token);

        Assert.Equal("not-authorized", result.Outcome);
        Assert.Null(result.Credential);
        Assert.Null(result.Email);

        // No operator cookie is set for a non-operator.
        var setCookie = harness.Controller.Response.Headers.SetCookie.ToString();
        Assert.DoesNotContain(OperatorSession.CookieName, setCookie);
    }

    [Fact]
    public async Task RequestLink_IssuesATokenWithoutConsultingTheAllowlist_NeutralForAnyEmail()
    {
        // Dev env so a token IS echoed - proving a token is minted for a
        // NON-operator email too (the request never consults the allowlist, AC-02),
        // so the response cannot be used to enumerate who is an operator.
        var harness = NewHarness(development: true);

        var operatorResult = await RequestLink(harness, AllowlistedOperator);
        var strangerResult = await RequestLink(harness, "not-an-operator@example.com");

        // Same neutral message for both - no operator-status tell.
        Assert.Equal(operatorResult.Message, strangerResult.Message);
        // A walkable token is echoed for BOTH in dev (issue does not gate on the
        // allowlist); the gate is applied only later at verify.
        Assert.NotNull(operatorResult.DevToken);
        Assert.NotNull(strangerResult.DevToken);
    }

    [Fact]
    public async Task RequestLink_InProduction_DoesNotEchoAToken()
    {
        var harness = NewHarness(development: false);

        var result = await RequestLink(harness, AllowlistedOperator);

        // No token leaks outside the Development environment (it is delivered by
        // email in a later story).
        Assert.Null(result.DevToken);
        Assert.Null(result.DevVerifyPath);
    }

    // ---- invalid tokens ---------------------------------------------------------

    [Fact]
    public async Task Verify_InvalidToken_ReturnsLinkInvalidAndMintsNoSession()
    {
        var harness = NewHarness();

        var result = await Verify(harness, "not-a-real-token");

        Assert.Equal("link-invalid", result.Outcome);
        Assert.Null(result.Credential);
    }

    [Fact]
    public async Task Verify_OverLengthToken_ReturnsLinkInvalid()
    {
        var harness = NewHarness();
        var oversized = new string('x', OperatorLoginController.MaxTokenLength + 1);

        var result = await Verify(harness, oversized);

        Assert.Equal("link-invalid", result.Outcome);
        Assert.Null(result.Credential);
    }

    [Fact]
    public async Task Verify_ReplayedToken_IsRejected()
    {
        var harness = NewHarness();
        var token = harness.Tokens.Issue(AllowlistedOperator);

        // First use signs in; the single-use nonce is consumed.
        Assert.Equal("signed-in", (await Verify(harness, token)).Outcome);
        // A replay of the SAME token verifies false -> link-invalid.
        Assert.Equal("link-invalid", (await Verify(harness, token)).Outcome);
    }

    // ---- helpers ----------------------------------------------------------------

    private static async Task<OperatorLoginRequestResult> RequestLink(Harness harness, string email)
    {
        var action = await harness.Controller.RequestLink(new OperatorLoginRequestBody(email));
        var ok = Assert.IsType<OkObjectResult>(action);
        return Assert.IsType<OperatorLoginRequestResult>(ok.Value);
    }

    private static async Task<OperatorLoginVerifyResult> Verify(Harness harness, string token)
    {
        var action = await harness.Controller.Verify(new OperatorLoginVerifyBody(token), CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(action);
        return Assert.IsType<OperatorLoginVerifyResult>(ok.Value);
    }

    /// <summary>A fixed-set operator allowlist for the tests (normalizes like the real one).</summary>
    private sealed class FakeOperatorAllowlist : IOperatorAllowlist
    {
        private readonly HashSet<string> _operators;

        public FakeOperatorAllowlist(params string[] operators) =>
            _operators = operators
                .Select(o => o.Trim().ToLowerInvariant())
                .ToHashSet(StringComparer.Ordinal);

        public bool IsOperator(string? email)
        {
            var normalized = (email ?? string.Empty).Trim().ToLowerInvariant();
            return normalized.Length > 0 && _operators.Contains(normalized);
        }
    }

    /// <summary>
    /// Minimal IWebHostEnvironment so the controller can call IsDevelopment() in a
    /// unit test with no host (mirrors SignInTests.FakeWebHostEnvironment).
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
