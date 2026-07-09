// ----------------------------------------------------------------------------
//  SupportMagicLinkResend - the small delivery seam the support console's
//  "resend magic link" verb (sysadmin-console/07, issue #243, AC-03) issues a fresh
//  sign-in link through, so the AccountSupportController NEVER calls IEmailSender
//  directly.
//
//  WHY A DEDICATED SEAM (AC-03, "no new email-delivery path"): the resend reuses the
//  EXACT accounts-identity/04 transport the purchaser sign-in flow already uses - the
//  ONE IMagicLinkTokenService (issue) plus the ONE IEmailSender (deliver) - never a
//  second implementation. Wrapping that here (rather than inlining IEmailSender in the
//  controller) keeps the controller off the email transport entirely: it calls this
//  seam, this seam calls the sender. The action ITSELF carries the SAME per-IP
//  [EnableRateLimiting(SignInRateLimit.PolicyName)] the public request endpoint uses,
//  and a per-target-account cap (SupportResendAccountThrottle) - so this seam is only
//  ever reached AFTER both throttles pass, and never bypasses them.
//
//  FAIL-SAFE (mirrors AccountsController.DeliverMagicLinkAsync): a provider error is
//  caught, logged WITHOUT the token / link / email, and swallowed - so a delivery
//  failure never becomes a 500. With no provider configured this is the NoOpEmailSender
//  (a no-op), so the app builds + runs with zero email setup.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Accounts;
using QuibbleStone.Api.Controllers;

namespace QuibbleStone.Api.Admin;

/// <summary>
/// Issues + delivers a fresh purchaser magic-link email to a resolved account's address
/// (sysadmin-console/07, AC-03), reusing accounts-identity/04's ONE token issuer + ONE email
/// transport. The support controller calls this instead of touching <see cref="IEmailSender"/>
/// directly. Fail-safe: a delivery failure is swallowed (logged without secrets), never surfaced.
/// </summary>
public interface ISupportMagicLinkResend
{
    /// <summary>
    /// Issues a single-use magic-link token for <paramref name="email"/> and delivers it through the
    /// SAME email seam the purchaser sign-in flow uses (AC-03). <paramref name="linkBaseFallback"/> is
    /// the request origin used to build the link when no public LinkBaseUrl is configured (local dev,
    /// where the sender is a no-op anyway). Never throws - a provider failure is swallowed.
    /// </summary>
    /// <param name="email">The resolved account's canonical email (the ONLY recipient).</param>
    /// <param name="linkBaseFallback">The request origin to fall back to when no LinkBaseUrl is set.</param>
    /// <param name="ct">Cancellation for the send.</param>
    Task ResendAsync(string email, string linkBaseFallback, CancellationToken ct = default);
}

/// <summary>
/// The default <see cref="ISupportMagicLinkResend"/> over accounts-identity/02's
/// <see cref="IMagicLinkTokenService"/> and accounts-identity/04's <see cref="IEmailSender"/> - the
/// SAME registered singletons the purchaser sign-in flow reuses (AC-03, no new email-delivery path).
/// Builds the purchaser sign-in link exactly like AccountsController does and delivers it fail-safe.
/// </summary>
public sealed class SupportMagicLinkResend : ISupportMagicLinkResend
{
    private readonly IMagicLinkTokenService _tokens;
    private readonly IEmailSender _email;
    private readonly EmailOptions _emailOptions;
    private readonly ILogger<SupportMagicLinkResend> _logger;

    /// <summary>Constructs the seam over the shared token issuer, email transport, email options, and a logger.</summary>
    public SupportMagicLinkResend(
        IMagicLinkTokenService tokens,
        IEmailSender email,
        EmailOptions emailOptions,
        ILogger<SupportMagicLinkResend> logger)
    {
        _tokens = tokens;
        _email = email;
        _emailOptions = emailOptions;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task ResendAsync(string email, string linkBaseFallback, CancellationToken ct = default)
    {
        try
        {
            // Issue a fresh single-use token bound to the account email (the SAME issuer the
            // purchaser request path uses) and build the purchaser sign-in link. The token is
            // never logged (AC-06 posture, ADR 0003 telemetry rule).
            var token = _tokens.Issue(email);
            var link = BuildMagicLink(token, linkBaseFallback);

            // Deliver through the ONE email seam with the purchaser sign-in copy. With no provider
            // configured this is the NoOpEmailSender (a no-op); with one, it emails the link.
            await _email.SendMagicLinkAsync(email, link, MagicLinkPurpose.PurchaserSignIn, ct);
        }
        catch (Exception ex)
        {
            // Fail-safe (AC-03): never surface a delivery failure. Log the exception only (no token /
            // link / email) and return - the verb still reports a friendly "on its way" outcome.
            _logger.LogWarning(ex, "Support resend of a purchaser magic link failed; the operator sees the neutral acknowledgement.");
        }
    }

    /// <summary>
    /// Builds {LinkBaseUrl-or-fallback}{MagicLinkPath}?token=... (the token URL-escaped), matching the
    /// purchaser sign-in link AccountsController.BuildMagicLink produces - the same web route the
    /// Account page's deep-link handler verifies against.
    /// </summary>
    private string BuildMagicLink(string token, string linkBaseFallback)
    {
        var linkBase = (_emailOptions.LinkBaseUrl ?? string.Empty).Trim();
        if (linkBase.Length == 0)
        {
            linkBase = (linkBaseFallback ?? string.Empty).Trim();
        }

        return $"{linkBase.TrimEnd('/')}{AccountsController.MagicLinkPath}?token={Uri.EscapeDataString(token)}";
    }
}
