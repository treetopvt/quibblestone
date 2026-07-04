// ----------------------------------------------------------------------------
//  ReportTalesRateLimit - the per-IP rate-limit policy for the PUBLIC, open,
//  anonymous "report this tale" endpoint (POST /api/tales/{slug}/report,
//  sysadmin-console/03, issue #137).
//
//  A SIBLING of PublishTalesRateLimit (keepsake-gallery/04) for the SAME reason on a
//  new surface: the report endpoint is unauthenticated and drives a Table Storage
//  write plus an auto-hide decision. Without a throttle a single actor could flood
//  reports to force-hide a legitimate tale past the small threshold N, or spam to
//  exhaust storage (AC-05). This limiter caps the report rate PER CLIENT IP - the
//  same "meter the compute, keep the caller anonymous" posture PublishTalesRateLimit
//  establishes (the partition is the caller's IP, never an account). A single actor
//  is bounded to N + this per-IP cap, so no one person can silence a tale alone; a
//  real "hey, this looks off" tap by a family member never hits the limit.
//
//  Policy: a fixed window keyed on the client IP (so one abuser cannot exhaust the
//  allowance for everyone). A rejected request gets 429 (Program.cs
//  RejectionStatusCode). Only POST /api/tales/{slug}/report opts in via
//  [EnableRateLimiting]; the rest of the API is untouched.
//
//  Behind App Service the true client IP arrives in X-Forwarded-For; the existing
//  ForwardedHeaders middleware makes Connection.RemoteIpAddress reflect it, and the
//  partition key degrades to a shared "unknown" bucket when no IP is available
//  (fail-closed: still bounded, just coarser).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.Http;

namespace QuibbleStone.Api.PublishedTales;

/// <summary>
/// The per-IP fixed-window rate-limit policy applied to POST
/// /api/tales/{slug}/report (sysadmin-console/03, AC-05). A sibling of
/// <see cref="PublishTalesRateLimit"/> for the SEPARATE report surface. Program.cs
/// registers the policy under <see cref="PolicyName"/>; the report action opts in
/// via [EnableRateLimiting]. Tunables + partition key live here as one source of
/// truth, unit-testable without the middleware.
/// </summary>
public static class ReportTalesRateLimit
{
    /// <summary>The named policy the report action opts into.</summary>
    public const string PolicyName = "ReportTales";

    /// <summary>
    /// Permitted reports per <see cref="Window"/> per client IP. Ample for a family
    /// member flagging a handful of tales, tight enough that one actor cannot flood
    /// reports to force-hide a tale or bloat storage (AC-05).
    /// </summary>
    public const int PermitLimit = 10;

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
