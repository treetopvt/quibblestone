// ----------------------------------------------------------------------------
//  NoOpEmailSender - the DEFAULT magic-link email transport for zero-config runs
//  (accounts-identity/04 AC-03).
//
//  QuibbleStone builds and runs with ZERO email configuration (local dev, CI, a
//  fresh clone, and today's deployed footprint before ACS is provisioned). When the
//  Email section is not configured, Program.cs registers THIS sender instead of the
//  ACS-backed one - EXACTLY mirroring the ITelemetrySink / IAiCompletionClient
//  config-presence branch. It does NOT send anything; it logs a single neutral line
//  so the flow is observable in dev without an email provider.
//
//  IT DOES NOT ECHO THE TOKEN. The Development-only dev-token echo lives in the
//  controllers (AccountsController / OperatorLoginController, gated on
//  IsDevelopment()), and is deliberately UNCHANGED by this seam - so a local
//  walkthrough still completes via that echo with no provider wired (AC-03). This
//  sender's job is only to be a safe, silent-by-default stand-in.
//
//  NEVER LOGS A SECRET (AC-08): it logs only the purpose (a copy selector, not
//  PII), never the recipient address, the token, the link, or any body - so even
//  the no-op path cannot leak a link or become an existence oracle.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;

namespace QuibbleStone.Api.Accounts;

/// <summary>
/// The no-op magic-link email transport (AC-03): the DEFAULT when no email provider
/// is configured. It never sends and never throws; it logs one neutral line (the
/// purpose only, no recipient / token / link / body) so the app runs with zero email
/// setup and the controllers' Development dev-token echo keeps local walkthroughs
/// working unchanged.
/// </summary>
public sealed class NoOpEmailSender : IEmailSender
{
    private readonly ILogger<NoOpEmailSender> _logger;

    public NoOpEmailSender(ILogger<NoOpEmailSender> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task SendMagicLinkAsync(
        string toEmail,
        string link,
        MagicLinkPurpose purpose,
        CancellationToken cancellationToken = default)
    {
        // No email provider is configured. Log at Debug with the purpose ONLY - never
        // the recipient, the token, the link, or any body (AC-08) - so this is safe
        // and cannot leak. Return a completed task; the controllers still return the
        // neutral acknowledgement (and, in Development, echo the token themselves).
        _logger.LogDebug(
            "Magic-link email (no-op sender): no email provider configured; not sending (purpose={Purpose}).",
            purpose);

        return Task.CompletedTask;
    }
}
