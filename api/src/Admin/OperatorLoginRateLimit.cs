// ----------------------------------------------------------------------------
//  OperatorLoginRateLimit - the per-IP rate-limit policy for the PUBLIC,
//  unauthenticated operator-login REQUEST endpoint (POST /api/admin/login/request,
//  sysadmin-console/01, issue #135).
//
//  A SIBLING of accounts-identity/03's SignInRateLimit (same rationale, separate
//  surface): the operator request endpoint mints a fresh HMAC token for ANY
//  well-formed email on every call, with no auth (the allowlist is checked at
//  VERIFY time, not here - AC-02). Once an email-delivery provider is wired, an
//  unthrottled endpoint would be both an email-bombing amplifier and a token-mint
//  CPU DoS. This limiter caps the request rate per client IP - the same "meter the
//  compute, keep the caller anonymous" posture as the purchaser sign-in limiter.
//  This is a sane, generous cap, NOT a full anti-abuse pass.
//
//  Policy: a fixed window keyed on the client IP (so one abuser cannot exhaust the
//  allowance for everyone, and a real operator retrying once or twice never hits
//  it). A rejected request gets 429 (Program.cs RejectionStatusCode). Only POST
//  /api/admin/login/request opts in via [EnableRateLimiting]; verify is bounded by
//  the single-use nonce + short token expiry, and the game path is untouched.
//
//  Behind App Service the true client IP arrives in X-Forwarded-For; the existing
//  ForwardedHeaders middleware makes Connection.RemoteIpAddress reflect it, and the
//  partition key degrades to a shared "unknown" bucket when no IP is available
//  (fail-closed: still bounded, just coarser).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.Http;

namespace QuibbleStone.Api.Admin;

/// <summary>
/// The per-IP fixed-window rate-limit policy applied to POST
/// /api/admin/login/request (sysadmin-console/01). A sibling of
/// SignInRateLimit for the SEPARATE operator surface. Program.cs registers the
/// policy under <see cref="PolicyName"/>; the request action opts in via
/// [EnableRateLimiting]. Tunables + partition key live here as one source of truth.
/// </summary>
public static class OperatorLoginRateLimit
{
    /// <summary>The named policy the operator-login request action opts into.</summary>
    public const string PolicyName = "OperatorLoginRequest";

    /// <summary>
    /// Permitted operator-login-link requests per <see cref="Window"/> per client IP.
    /// Ample for an operator requesting a link and retrying once or twice, tight
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
