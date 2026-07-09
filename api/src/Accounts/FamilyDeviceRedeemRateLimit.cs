// ----------------------------------------------------------------------------
//  FamilyDeviceRedeemRateLimit - the rate-limit policy constants for the PUBLIC,
//  unauthenticated family-device redeem + refresh endpoints (accounts-identity/09,
//  issue #229; ADR 0003 "Security posture": handles are secrets).
//
//  The redeem endpoint mints a long-lived bearer credential from a short, enumerable
//  link code, with no auth. The ADR requires THREE layers, because any one alone is
//  defeated:
//    - PER-IP (this file, PerIpPolicyName): a fixed window keyed on the client IP, so
//      one abuser cannot exhaust the allowance for everyone. Mirrors SignInRateLimit.
//    - GLOBAL ceiling (this file, GlobalPolicyName): a fixed window on a CONSTANT
//      partition, so an attacker rotating IPs (which defeats a per-IP-only limiter)
//      still hits an aggregate cap on redeem/refresh volume across the whole process.
//    - PER-CODE attempt burn (InMemoryFamilyLinkCodeStore): a specific code
//      self-invalidates after a small number of attempts, independent of the IP.
//
//  Both middleware policies are applied to the redeem + refresh actions; the per-code
//  burn lives in the code store. Behind App Service the true client IP arrives in
//  X-Forwarded-For, which the ForwardedHeaders middleware (Program.cs) already honors,
//  so RemoteIpAddress reflects it.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.Http;

namespace QuibbleStone.Api.Accounts;

/// <summary>
/// Rate-limit tunables + partition key for the family-device redeem / refresh
/// endpoints (accounts-identity/09). Program.cs registers the PER-IP policy
/// (<see cref="PerIpPolicyName"/>) and the two actions opt into it via
/// [EnableRateLimiting]; the GLOBAL ceiling (<see cref="GlobalPermitLimit"/>) is
/// enforced IN-CODE by <see cref="FamilyDeviceRedeemGlobalThrottle"/> rather than a
/// second policy, because ASP.NET allows only one [EnableRateLimiting] per endpoint.
/// Kept here as one source of truth, unit-testable without the middleware.
/// </summary>
public static class FamilyDeviceRedeemRateLimit
{
    /// <summary>The per-IP fixed-window policy name (the one [EnableRateLimiting] policy).</summary>
    public const string PerIpPolicyName = "FamilyDeviceRedeemPerIp";

    /// <summary>Permitted redeem/refresh attempts per <see cref="Window"/> per client IP.</summary>
    public const int PerIpPermitLimit = 8;

    /// <summary>
    /// Permitted redeem/refresh attempts per <see cref="Window"/> ACROSS ALL callers
    /// (the global ceiling an IP-rotating attacker still hits, enforced in-code by
    /// <see cref="FamilyDeviceRedeemGlobalThrottle"/>). Generous for a family or two
    /// linking devices in a sitting, tight enough to blunt a distributed enumeration of
    /// the code space before the short codes even expire.
    /// </summary>
    public const int GlobalPermitLimit = 60;

    /// <summary>The fixed window both limits apply over.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The per-IP partition key: the caller's IP, falling back to a shared "unknown"
    /// bucket when none is available (fail-closed - still bounded). Anonymous by
    /// construction: no account, no identity, just the IP.
    /// </summary>
    public static string PartitionKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
