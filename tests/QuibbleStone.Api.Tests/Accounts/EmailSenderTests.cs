// ----------------------------------------------------------------------------
//  EmailSenderTests - tests for the magic-link email-delivery seam
//  (accounts-identity/04, issue #167). They drive the REAL AccountsController AND
//  the REAL OperatorLoginController against the REAL MagicLinkTokenService + a set
//  of test IEmailSender doubles (a recording sender, a throwing sender) + the real
//  NoOpEmailSender - no mocking framework, matching the rest of the harness and
//  mirroring SignInTests / OperatorLoginTests.
//
//  They pin the load-bearing guarantees of the delivery story:
//    - AC-02 (ONE seam, both flows): the purchaser sign-in AND the operator login
//      request endpoints both deliver through the SAME IEmailSender, each handing it
//      the requester's email + a link carrying the freshly-issued token, differing
//      ONLY in the purpose (copy) and the link path - there is no second transport.
//    - AC-03 (zero-config no-op): with the NoOpEmailSender (no provider configured)
//      the request endpoint still returns the neutral acknowledgement, the
//      Development dev-token echo is unchanged, and nothing throws.
//    - AC-04 (no enumeration / no failure oracle): the request response is identical
//      for a known vs an unknown email AND for a sender success vs a thrown failure.
//    - AC-08 (fail-safe): a sender that THROWS still yields the neutral 200, and the
//      failure log carries NO token / link / email / secret.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuibbleStone.Api.Accounts;
using QuibbleStone.Api.Admin;
using QuibbleStone.Api.Controllers;

namespace QuibbleStone.Api.Tests.Accounts;

public class EmailSenderTests
{
    private const string TestSigningKey = "test-signing-key-not-a-real-secret";
    private const string AllowlistedOperator = "ops@quibblestone.com";
    // A fixed link base so the delivered link is deterministic and parseable.
    private const string LinkBaseUrl = "https://test.example";

    // ---- AC-02: both endpoints deliver through the ONE seam ----------------------

    [Fact]
    public async Task PurchaserRequest_DeliversTheIssuedLinkThroughTheOneSeam()
    {
        var tokens = new MagicLinkTokenService(TestSigningKey);
        var sender = new RecordingEmailSender();
        var controller = NewAccountsController(tokens, sender, development: false);

        var action = await controller.RequestLink(new SignInRequestBody("Buyer@Example.com"));
        Assert.IsType<OkObjectResult>(action);

        // Exactly ONE send, to the entered address, tagged as the purchaser flow.
        var send = Assert.Single(sender.Sends);
        Assert.Equal("Buyer@Example.com", send.Email);
        Assert.Equal(MagicLinkPurpose.PurchaserSignIn, send.Purpose);
        Assert.StartsWith($"{LinkBaseUrl}{AccountsController.MagicLinkPath}?token=", send.Link);

        // The delivered link carries a REAL, verifiable magic-link token for the email.
        Assert.True(tokens.TryVerify(ExtractToken(send.Link), out var subject));
        Assert.Equal("Buyer@Example.com", subject);
    }

    [Fact]
    public async Task OperatorRequest_DeliversThroughTheSameSeam_DifferingOnlyInPurposeAndPath()
    {
        var tokens = new MagicLinkTokenService(TestSigningKey);
        var sender = new RecordingEmailSender();
        // A NON-operator email: the request endpoint never consults the allowlist, so
        // it still delivers (the gate is at verify, AC-02) - and it uses the SAME seam.
        var controller = NewOperatorLoginController(tokens, sender, development: false);

        var action = await controller.RequestLink(new OperatorLoginRequestBody("someone@example.com"));
        Assert.IsType<OkObjectResult>(action);

        var send = Assert.Single(sender.Sends);
        Assert.Equal("someone@example.com", send.Email);
        Assert.Equal(MagicLinkPurpose.OperatorLogin, send.Purpose);
        Assert.StartsWith($"{LinkBaseUrl}{OperatorLoginController.MagicLinkPath}?token=", send.Link);

        Assert.True(tokens.TryVerify(ExtractToken(send.Link), out var subject));
        Assert.Equal("someone@example.com", subject);
    }

    // ---- AC-03: no provider configured => the no-op sender, flow unchanged --------

    [Fact]
    public async Task NoProvider_UsesNoOpSender_RequestStillNeutral_AndDevEchoUnchanged()
    {
        // EmailOptions.IsConfigured is false for a fresh clone, so Program.cs would
        // register the NoOpEmailSender; use it here directly.
        Assert.False(new EmailOptions().IsConfigured);
        IEmailSender noOp = new NoOpEmailSender(NullLogger<NoOpEmailSender>.Instance);
        var tokens = new MagicLinkTokenService(TestSigningKey);
        var controller = NewAccountsController(tokens, noOp, development: true);

        var action = await controller.RequestLink(new SignInRequestBody("buyer@example.com"));
        var ok = Assert.IsType<OkObjectResult>(action);
        var result = Assert.IsType<SignInRequestResult>(ok.Value);

        // The Development dev-token echo is UNCHANGED by the seam (walkable locally
        // with zero email setup), and nothing threw.
        Assert.NotNull(result.DevToken);
        Assert.True(tokens.TryVerify(result.DevToken!, out var email));
        Assert.Equal("buyer@example.com", email);
    }

    [Fact]
    public async Task NoOpSender_NeverThrows_AndLogsNoRecipientLinkOrToken()
    {
        // Capture the no-op sender's OWN logs: its contract is to send nothing AND to
        // log ONLY the purpose - never the recipient, link, or token (AC-08). Distinctive
        // values so the assertions cannot pass by coincidence.
        var logger = new CapturingLogger<NoOpEmailSender>();
        IEmailSender noOp = new NoOpEmailSender(logger);

        // A plain call completes without throwing (it simply does not send).
        await noOp.SendMagicLinkAsync(
            "anyone@example.com",
            "https://x/y?token=SECRET-TOKEN-DO-NOT-LOG",
            MagicLinkPurpose.PurchaserSignIn);

        // It logged its single purpose-only breadcrumb (so the checks below are not
        // vacuous), and that log carries NO recipient / link / token (AC-08).
        Assert.NotEmpty(logger.Messages);
        var everythingLogged = string.Join("\n", logger.Messages);
        Assert.DoesNotContain("anyone@example.com", everythingLogged);
        Assert.DoesNotContain("https://x/y?token=SECRET-TOKEN-DO-NOT-LOG", everythingLogged);
        Assert.DoesNotContain("SECRET-TOKEN-DO-NOT-LOG", everythingLogged);
    }

    // ---- AC-04: identical response, known vs unknown AND success vs throw ---------

    [Fact]
    public async Task Request_ResponseIsIdentical_ForKnownAndUnknownEmail()
    {
        // Even with a KNOWN account present, the request endpoint never reads the
        // store, so the response cannot differ from the unknown-email case.
        var tokens = new MagicLinkTokenService(TestSigningKey);
        var sender = new RecordingEmailSender();
        var store = new InMemoryAccountStore();
        await store.CreateOrGetAsync("known@example.com");
        var controller = NewAccountsController(tokens, sender, development: false, store: store);

        var known = await RequestPurchaser(controller, "known@example.com");
        var unknown = await RequestPurchaser(controller, "stranger@example.com");

        // Same neutral shape - no existence tell (AC-04).
        Assert.Equal(known, unknown);
    }

    [Fact]
    public async Task Request_ResponseIsIdentical_WhenTheSenderSucceedsVsThrows()
    {
        var tokens = new MagicLinkTokenService(TestSigningKey);

        var okController = NewAccountsController(tokens, new RecordingEmailSender(), development: false);
        var throwController = NewAccountsController(tokens, new ThrowingEmailSender(), development: false);

        var onSuccess = await RequestPurchaser(okController, "buyer@example.com");
        var onFailure = await RequestPurchaser(throwController, "buyer@example.com");

        // A delivery failure is invisible to the caller (AC-04/AC-08): same response.
        Assert.Equal(onSuccess, onFailure);
    }

    // ---- AC-08: a throwing sender still yields the neutral 200, no secret logged --

    [Fact]
    public async Task ThrowingSender_StillReturnsNeutral200_AndLogsNoTokenLinkOrEmail()
    {
        var tokens = new MagicLinkTokenService(TestSigningKey);
        var sender = new ThrowingEmailSender();
        var logger = new CapturingLogger<AccountsController>();
        var controller = NewAccountsController(tokens, sender, development: false, logger: logger);

        var action = await controller.RequestLink(new SignInRequestBody("buyer@example.com"));

        // Never a 500: the neutral acknowledgement (AC-08).
        var ok = Assert.IsType<OkObjectResult>(action);
        Assert.IsType<SignInRequestResult>(ok.Value);

        // Delivery was attempted (and failed) - the sender saw the link + email.
        Assert.NotNull(sender.LastLink);
        var token = ExtractToken(sender.LastLink!);

        // The failure WAS logged (a warning was recorded), but WITHOUT the token /
        // link / email / any secret material (AC-08).
        Assert.NotEmpty(logger.Messages);
        var everythingLogged = string.Join("\n", logger.Messages);
        Assert.DoesNotContain(token, everythingLogged);
        Assert.DoesNotContain(sender.LastLink!, everythingLogged);
        Assert.DoesNotContain("buyer@example.com", everythingLogged);
    }

    // ---- helpers ----------------------------------------------------------------

    private static async Task<SignInRequestResult> RequestPurchaser(AccountsController controller, string email)
    {
        var action = await controller.RequestLink(new SignInRequestBody(email));
        var ok = Assert.IsType<OkObjectResult>(action);
        return Assert.IsType<SignInRequestResult>(ok.Value);
    }

    /// <summary>Pulls the token query value out of a {base}{path}?token=... link.</summary>
    private static string ExtractToken(string link)
    {
        const string marker = "?token=";
        var index = link.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(index >= 0, "the delivered link must carry a ?token= query");
        return Uri.UnescapeDataString(link[(index + marker.Length)..]);
    }

    private static AccountsController NewAccountsController(
        IMagicLinkTokenService tokens,
        IEmailSender sender,
        bool development,
        InMemoryAccountStore? store = null,
        ILogger<AccountsController>? logger = null)
    {
        var credential = new PurchaserCredentialService(new EphemeralDataProtectionProvider());
        var environment = new FakeWebHostEnvironment(development ? "Development" : "Production");
        var options = new EmailOptions { LinkBaseUrl = LinkBaseUrl };
        return new AccountsController(
            tokens,
            store ?? new InMemoryAccountStore(),
            credential,
            sender,
            options,
            environment,
            logger ?? NullLogger<AccountsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    private static OperatorLoginController NewOperatorLoginController(
        IMagicLinkTokenService tokens,
        IEmailSender sender,
        bool development)
    {
        var environment = new FakeWebHostEnvironment(development ? "Development" : "Production");
        var options = new EmailOptions { LinkBaseUrl = LinkBaseUrl };
        return new OperatorLoginController(
            tokens,
            new FakeOperatorAllowlist(AllowlistedOperator),
            new EphemeralDataProtectionProvider(),
            sender,
            options,
            environment,
            NullLogger<OperatorLoginController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    // ---- test doubles -----------------------------------------------------------

    /// <summary>Records every send so a test can assert email / link / purpose (AC-02).</summary>
    private sealed class RecordingEmailSender : IEmailSender
    {
        public List<(string Email, string Link, MagicLinkPurpose Purpose)> Sends { get; } = new();

        public Task SendMagicLinkAsync(string toEmail, string link, MagicLinkPurpose purpose, CancellationToken cancellationToken = default)
        {
            Sends.Add((toEmail, link, purpose));
            return Task.CompletedTask;
        }
    }

    /// <summary>Captures the last email / link, then THROWS - to exercise the fail-safe path (AC-08).</summary>
    private sealed class ThrowingEmailSender : IEmailSender
    {
        public string? LastEmail { get; private set; }
        public string? LastLink { get; private set; }

        public Task SendMagicLinkAsync(string toEmail, string link, MagicLinkPurpose purpose, CancellationToken cancellationToken = default)
        {
            LastEmail = toEmail;
            LastLink = link;
            throw new InvalidOperationException("simulated email provider failure");
        }
    }

    /// <summary>Captures every log line (formatted message + exception) so a test can assert on it (AC-08).</summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = new();

        IDisposable? ILogger.BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var line = formatter(state, exception);
            if (exception is not null)
            {
                line += " " + exception;
            }
            Messages.Add(line);
        }
    }

    /// <summary>Minimal IOperatorAllowlist for the operator-login harness (one allowlisted email).</summary>
    private sealed class FakeOperatorAllowlist : IOperatorAllowlist
    {
        private readonly string _allowed;
        public FakeOperatorAllowlist(string allowed) => _allowed = allowed;
        public bool IsOperator(string? email) =>
            string.Equals(email?.Trim(), _allowed, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Minimal IWebHostEnvironment so a controller can call IsDevelopment() with no host.</summary>
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
