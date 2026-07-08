// ----------------------------------------------------------------------------
//  EmailInviteSenderTests - pins the game-invite half of the IEmailSender seam
//  (session-engine/12, issue #180, AC-03) at the sender level.
//
//  A game invite is delivered through the SAME IEmailSender the magic link uses, via
//  its OWN method (SendGameInviteAsync) - a distinct method with NO MagicLinkPurpose
//  and no token, so a game invite can never route through the sign-in path (that
//  distinction is enforced at compile time by the two separate signatures). Here we
//  pin the zero-config posture: the NoOpEmailSender's game-invite send never throws and
//  its neutral breadcrumb leaks no recipient / link / code, mirroring the magic-link
//  no-op's discipline (EmailSenderTests).
//
//  The real ACS transport is not unit-tested (it needs a live provider), exactly as the
//  magic-link AcsEmailSender is not - controller-level behavior is covered by
//  EmailInviteControllerTests against test doubles.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using QuibbleStone.Api.Accounts;

namespace QuibbleStone.Api.Tests.Accounts;

public sealed class EmailInviteSenderTests
{
    [Fact]
    public async Task NoOpSender_GameInvite_NeverThrows_AndLogsNoRecipientLinkOrCode()
    {
        // Capture the no-op sender's OWN logs: its contract is to send nothing AND to log
        // a neutral breadcrumb with none of the send's specifics. Distinctive values so
        // the assertions cannot pass by coincidence.
        var logger = new CapturingLogger<NoOpEmailSender>();
        IEmailSender noOp = new NoOpEmailSender(logger);

        // A plain call completes without throwing (it simply does not send).
        await noOp.SendGameInviteAsync(
            "friend@example.com",
            "https://x/join/MASS",
            "MASS");

        // It logged its single neutral breadcrumb (so the checks below are not vacuous),
        // and that log carries NO recipient / link / code.
        Assert.NotEmpty(logger.Messages);
        var everythingLogged = string.Join("\n", logger.Messages);
        Assert.DoesNotContain("friend@example.com", everythingLogged);
        Assert.DoesNotContain("https://x/join/MASS", everythingLogged);
        Assert.DoesNotContain("MASS", everythingLogged);
    }

    /// <summary>Captures every log line (formatted message + exception) so a test can assert on it.</summary>
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
}
