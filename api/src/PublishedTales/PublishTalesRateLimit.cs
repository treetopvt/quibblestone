// ----------------------------------------------------------------------------
//  PublishTalesRateLimit - the per-IP rate-limit policy for the PUBLIC, open,
//  anonymous publish endpoint (POST /api/tales, keepsake-gallery/04).
//
//  Why this exists (security review W-001): POST /api/tales is an unauthenticated
//  write endpoint that mints a public page and a Table Storage row. Without a
//  throttle a script could flood it to bloat storage and mass-create public
//  pages. The per-part content-safety re-vet stops UNSAFE content, but not sheer
//  VOLUME - that is this limiter's job. It is the same "meter the compute, keep
//  the player anonymous" posture the roadmap's AI cost gate applies (no identity
//  required - the partition is the caller's IP, not an account).
//
//  Policy: a fixed window keyed on the client IP, so one abuser cannot exhaust
//  the allowance for everyone, and a real family sharing a handful of tales never
//  hits it. A rejected request gets 429 (see Program.cs RejectionStatusCode).
//
//  Behind App Service the true client IP arrives in X-Forwarded-For; wiring
//  ForwardedHeaders middleware so Connection.RemoteIpAddress reflects it is a
//  deployment hardening step noted in the provisioning runbook - the partition
//  key reads RemoteIpAddress either way and degrades to a shared "unknown"
//  bucket if no IP is available (fail-closed: still bounded, just coarser).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.Http;

namespace QuibbleStone.Api.PublishedTales;

/// <summary>
/// The per-IP fixed-window rate-limit policy applied to POST /api/tales
/// (keepsake-gallery/04, security review W-001). Program.cs registers the policy
/// under <see cref="PolicyName"/> and the controller's publish action opts in via
/// [EnableRateLimiting]. The tunables and the partition key live here so they are
/// one source of truth and unit-testable without spinning up the middleware.
/// </summary>
public static class PublishTalesRateLimit
{
    /// <summary>The named policy the publish action opts into.</summary>
    public const string PolicyName = "PublishTales";

    /// <summary>
    /// Permitted publishes per <see cref="Window"/> per client IP. Generous for a
    /// family sharing a few tales in one sitting, tight enough to make a
    /// storage-bloat flood ineffective.
    /// </summary>
    public const int PermitLimit = 8;

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
