// ----------------------------------------------------------------------------
//  EmailInviteControllerTests - controller-level tests for the email-a-game-invite
//  surface (session-engine/12, issue #180). They drive the REAL EmailInviteController
//  against test IEmailSender doubles (a recording sender, a throwing sender) + a real
//  EmailOptions - no mocking framework, mirroring EmailSenderTests / SignInTests.
//
//  They pin the load-bearing guarantees of the story:
//    - AC-01: a valid { roomCode, toEmail } POST delivers, through the ONE seam's
//      game-invite method, the recipient + the SERVER-BUILT /join/<code> link + the
//      room code (uppercased) - not a client-supplied URL.
//    - AC-02: the controller takes NO hub / room / registry dependency (structural).
//    - AC-06: with no provider configured, a POST returns a friendly not-available
//      result and never sends; the availability probe reflects IsConfigured.
//    - Shape guards: a malformed code or email returns 400 and never sends.
//    - Fail-safe: a throwing provider yields a friendly could-not-send, never a 500.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using QuibbleStone.Api.Accounts;
using QuibbleStone.Api.Controllers;

namespace QuibbleStone.Api.Tests;

public sealed class EmailInviteControllerTests
{
    // A fixed link base so the server-built join link is deterministic and assertable.
    private const string LinkBaseUrl = "https://test.example";

    // ---- AC-01: a valid request delivers the server-built payload ----------------

    [Fact]
    public async Task ValidRequest_SendsInviteWithServerBuiltLinkAndCode()
    {
        var sender = new RecordingGameInviteSender();
        var controller = NewController(sender, Configured());

        var action = await controller.SendInvite(new EmailInviteRequestBody("MASS", "friend@example.com"));

        var ok = Assert.IsType<OkObjectResult>(action);
        var result = Assert.IsType<EmailInviteResult>(ok.Value);
        Assert.True(result.Sent);

        // Exactly ONE send, to the entered address, carrying the server-built /join/<code>
        // link and the room code - the SAME payload Copy/Share produce (AC-01).
        var invite = Assert.Single(sender.Invites);
        Assert.Equal("friend@example.com", invite.Email);
        Assert.Equal("MASS", invite.RoomCode);
        Assert.Equal($"{LinkBaseUrl}/join/MASS", invite.JoinLink);
    }

    [Fact]
    public async Task RoomCode_IsNormalizedToUppercaseInTheLink()
    {
        var sender = new RecordingGameInviteSender();
        var controller = NewController(sender, Configured());

        // A lowercase code is normalized to the uppercase alphabet the codes are minted
        // from, so the link and the readable code match what the Lobby displays.
        await controller.SendInvite(new EmailInviteRequestBody("mass", "friend@example.com"));

        var invite = Assert.Single(sender.Invites);
        Assert.Equal("MASS", invite.RoomCode);
        Assert.Equal($"{LinkBaseUrl}/join/MASS", invite.JoinLink);
    }

    // ---- AC-06: degrade cleanly when no provider is configured -------------------

    [Fact]
    public async Task NoProvider_ReturnsNotAvailable_AndNeverSends()
    {
        var sender = new RecordingGameInviteSender();
        // A default EmailOptions is NOT configured (a fresh clone / today's footprint).
        var controller = NewController(sender, new EmailOptions());

        var action = await controller.SendInvite(new EmailInviteRequestBody("MASS", "friend@example.com"));

        var ok = Assert.IsType<OkObjectResult>(action);
        var result = Assert.IsType<EmailInviteResult>(ok.Value);
        Assert.False(result.Sent);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        Assert.Empty(sender.Invites);
    }

    [Fact]
    public void Availability_IsTrueOnlyWhenEmailIsConfigured()
    {
        var onOk = Assert.IsType<OkObjectResult>(NewController(new RecordingGameInviteSender(), Configured()).Availability());
        Assert.True(Assert.IsType<EmailInviteAvailabilityResult>(onOk.Value).Available);

        var offOk = Assert.IsType<OkObjectResult>(NewController(new RecordingGameInviteSender(), new EmailOptions()).Availability());
        Assert.False(Assert.IsType<EmailInviteAvailabilityResult>(offOk.Value).Available);
    }

    // ---- shape guards: bad code / email => 400, no send -------------------------

    [Theory]
    [InlineData("MAS")]      // too short
    [InlineData("MASSY")]    // too long
    [InlineData("MOSS")]     // contains 'O', which the code alphabet excludes
    [InlineData("MA S")]     // contains a space
    [InlineData("")]         // empty
    public async Task MalformedRoomCode_ReturnsBadRequest_AndNeverSends(string code)
    {
        var sender = new RecordingGameInviteSender();
        var controller = NewController(sender, Configured());

        var action = await controller.SendInvite(new EmailInviteRequestBody(code, "friend@example.com"));

        var bad = Assert.IsType<BadRequestObjectResult>(action);
        Assert.False(Assert.IsType<EmailInviteResult>(bad.Value).Sent);
        Assert.Empty(sender.Invites);
    }

    [Theory]
    [InlineData("not-an-email")]      // no '@'
    [InlineData("nobody@")]           // nothing after '@'
    [InlineData("@example.com")]      // nothing before '@'
    [InlineData("a b@example.com")]   // contains a space
    [InlineData("a\tb@example.com")]  // embedded tab
    [InlineData("a\r\nb@example.com")]// embedded CR/LF (email-header injection vector)
    [InlineData("")]                  // empty
    public async Task MalformedEmail_ReturnsBadRequest_AndNeverSends(string email)
    {
        var sender = new RecordingGameInviteSender();
        var controller = NewController(sender, Configured());

        var action = await controller.SendInvite(new EmailInviteRequestBody("MASS", email));

        var bad = Assert.IsType<BadRequestObjectResult>(action);
        Assert.False(Assert.IsType<EmailInviteResult>(bad.Value).Sent);
        Assert.Empty(sender.Invites);
    }

    // ---- fail-safe: a throwing provider yields a friendly result, not a 500 ------

    [Fact]
    public async Task ProviderThrows_ReturnsFriendlyCouldNotSend_NotAnError()
    {
        var controller = NewController(new ThrowingGameInviteSender(), Configured());

        var action = await controller.SendInvite(new EmailInviteRequestBody("MASS", "friend@example.com"));

        // Never a 500: a friendly could-not-send (Sent=false + message).
        var ok = Assert.IsType<OkObjectResult>(action);
        var result = Assert.IsType<EmailInviteResult>(ok.Value);
        Assert.False(result.Sent);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    // ---- AC-02: no hub / room dependency (structural) ---------------------------

    [Fact]
    public void Controller_TakesNoHubOrRoomDependency()
    {
        var ctor = Assert.Single(typeof(EmailInviteController).GetConstructors());
        var paramTypes = ctor.GetParameters().Select(p => p.ParameterType).ToList();

        // Exactly the three stateless deps: the email seam, its options, and a logger.
        Assert.Equal(3, paramTypes.Count);
        Assert.Contains(typeof(IEmailSender), paramTypes);
        Assert.Contains(typeof(EmailOptions), paramTypes);

        // AC-02: nothing hub- or room-shaped is injected (no IHubContext, RoomRegistry, Room).
        Assert.DoesNotContain(paramTypes, t =>
            t.Name.Contains("Hub") || t.Name.Contains("Room") || t.Name.Contains("Registry"));
    }

    // ---- helpers ----------------------------------------------------------------

    private static EmailOptions Configured() => new()
    {
        FromAddress = "no-reply@quibblestone.com",
        Endpoint = "https://acs.example",
        LinkBaseUrl = LinkBaseUrl,
    };

    private static EmailInviteController NewController(IEmailSender sender, EmailOptions options) =>
        new(sender, options, NullLogger<EmailInviteController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    // ---- test doubles -----------------------------------------------------------

    /// <summary>Records every game invite so a test can assert recipient / link / code (AC-01).</summary>
    private sealed class RecordingGameInviteSender : IEmailSender
    {
        public List<(string Email, string JoinLink, string RoomCode)> Invites { get; } = new();

        public Task SendMagicLinkAsync(string toEmail, string link, MagicLinkPurpose purpose, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SendGameInviteAsync(string toEmail, string joinLink, string roomCode, CancellationToken cancellationToken = default)
        {
            Invites.Add((toEmail, joinLink, roomCode));
            return Task.CompletedTask;
        }
    }

    /// <summary>Throws on a game-invite send - to exercise the controller's fail-safe path.</summary>
    private sealed class ThrowingGameInviteSender : IEmailSender
    {
        public Task SendMagicLinkAsync(string toEmail, string link, MagicLinkPurpose purpose, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SendGameInviteAsync(string toEmail, string joinLink, string roomCode, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("simulated email provider failure");
    }
}
