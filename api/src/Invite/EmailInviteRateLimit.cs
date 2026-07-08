// ----------------------------------------------------------------------------
//  EmailInviteRateLimit - the per-IP rate-limit policy for the PUBLIC, open,
//  anonymous email-game-invite endpoint (POST /api/invite/email, session-engine/12,
//  issue #180).
//
//  Why this exists (mirrors SignInRateLimit / PublishTalesRateLimit, security review
//  W-001's posture): the invite endpoint hands an arbitrary recipient address to the
//  email provider on every call, with no auth. Unthrottled, that is an email-bombing
//  amplifier - official-looking "join my game" mail to any inbox, on repeat. The
//  fixed template (no sender free text) blunts CONTENT abuse; this limiter blunts
//  VOLUME. Same "meter the compute, keep the player anonymous" posture as the rest of
//  the app: the partition is the caller's IP, never an account.
//
//  Policy: a fixed window keyed on the client IP, so one abuser cannot exhaust the
//  allowance for everyone, and a real family inviting a handful of relatives in one
//  sitting never hits it. A rejected request gets 429 (see Program.cs
//  RejectionStatusCode). Only POST /api/invite/email opts in via [EnableRateLimiting];
//  the GET availability probe is a cheap read and is deliberately not limited.
//
//  Behind App Service the true client IP arrives in X-Forwarded-For; the same
//  ForwardedHeaders middleware the other limiters rely on makes
//  Connection.RemoteIpAddress reflect it. The partition key reads RemoteIpAddress
//  either way and degrades to a shared "unknown" bucket if no IP is available
//  (fail-closed: still bounded, just coarser).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.Http;

namespace QuibbleStone.Api.Invite;

/// <summary>
/// The per-IP fixed-window rate-limit policy applied to POST /api/invite/email
/// (session-engine/12, #180). Program.cs registers the policy under
/// <see cref="PolicyName"/> and the controller's send action opts in via
/// [EnableRateLimiting]. The tunables and the partition key live here so they are one
/// source of truth and unit-testable without spinning up the middleware.
/// </summary>
public static class EmailInviteRateLimit
{
    /// <summary>The named policy the email-invite send action opts into.</summary>
    public const string PolicyName = "EmailInvite";

    /// <summary>
    /// Permitted invite sends per <see cref="Window"/> per client IP. Generous for a
    /// family inviting several relatives to a room in one sitting, tight enough that an
    /// unthrottled email-bomb relay is ineffective.
    /// </summary>
    public const int PermitLimit = 8;

    /// <summary>The fixed window the <see cref="PermitLimit"/> applies over.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The rate-limit partition key: the caller's IP (so the limit is per-client, not
    /// global). Falls back to a shared "unknown" bucket when no remote IP is available -
    /// fail-closed, so an IP-less caller is still bounded rather than unlimited.
    /// Anonymous by construction: no account, no identity - just the IP.
    /// </summary>
    public static string PartitionKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
