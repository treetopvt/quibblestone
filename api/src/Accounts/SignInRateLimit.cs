// ----------------------------------------------------------------------------
//  SignInRateLimit - the per-IP rate-limit policy for the PUBLIC, open,
//  unauthenticated magic-link request endpoint (POST /api/accounts/signin/request,
//  accounts-identity/03).
//
//  Why this exists (mirrors keepsake-gallery/04's PublishTalesRateLimit, security
//  review W-001): the request endpoint mints a fresh HMAC token for ANY well-formed
//  email on every call, with no auth. Once an email-delivery provider is wired
//  (the next story), an unthrottled endpoint becomes an email-bombing amplifier
//  (send mail to an arbitrary inbox) AND a token-minting CPU DoS. The no-enumeration
//  design keeps it SILENT about accounts, but silence is not a volume throttle -
//  that is this limiter's job. Same "meter the compute, keep the player anonymous"
//  posture as the AI cost gate: the partition is the caller's IP, never an account.
//
//  Policy: a fixed window keyed on the client IP, so one abuser cannot exhaust the
//  allowance for everyone, and a real purchaser requesting a link (even retrying a
//  couple of times) never hits it. A rejected request gets 429 (see Program.cs
//  RejectionStatusCode). Only POST /api/accounts/signin/request opts in via
//  [EnableRateLimiting]; verify is naturally bounded by the single-use nonce +
//  short token expiry, and nothing on the game path is touched.
//
//  Behind App Service the true client IP arrives in X-Forwarded-For; the same
//  ForwardedHeaders middleware the publish limiter relies on makes
//  Connection.RemoteIpAddress reflect it. The partition key reads RemoteIpAddress
//  either way and degrades to a shared "unknown" bucket if no IP is available
//  (fail-closed: still bounded, just coarser).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.Http;

namespace QuibbleStone.Api.Accounts;

/// <summary>
/// The per-IP fixed-window rate-limit policy applied to POST
/// /api/accounts/signin/request (accounts-identity/03). Program.cs registers the
/// policy under <see cref="PolicyName"/> and the controller's request action opts
/// in via [EnableRateLimiting]. The tunables and the partition key live here so
/// they are one source of truth and unit-testable without the middleware.
/// </summary>
public static class SignInRateLimit
{
    /// <summary>The named policy the sign-in request action opts into.</summary>
    public const string PolicyName = "SignInRequest";

    /// <summary>
    /// Permitted sign-in-link requests per <see cref="Window"/> per client IP.
    /// Ample for a purchaser requesting a link and retrying once or twice, tight
    /// enough that an unthrottled email-bomb / token-mint flood is ineffective.
    /// </summary>
    public const int PermitLimit = 5;

    /// <summary>The fixed window the <see cref="PermitLimit"/> applies over.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The rate-limit partition key: the caller's IP (so the limit is per-client,
    /// not global). Falls back to a shared "unknown" bucket when no remote IP is
    /// available - fail-closed, so an IP-less caller is still bounded rather than
    /// unlimited. Anonymous by construction: no account, no identity - just the IP.
    /// </summary>
    public static string PartitionKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
