// ----------------------------------------------------------------------------
//  VaultRateLimit - the per-IP rate-limit policies for the anonymous keepsake-
//  vault endpoints (keepsake-vault/01, ADR 0003 "Handles are secrets", issue
//  #196). Mirrors CloudGalleryRateLimit / PublishTalesRateLimit's fixed-window
//  per-IP pattern (ASP.NET Core's built-in limiter, no new dependency).
//
//  Why BOTH a read AND a write policy (AC-06, the gap this feature closes): the
//  vault's endpoints are an anonymous, unauthenticated-by-design surface reachable
//  by every device, gated only by possession of a bearer vault id. The existing
//  precedents (CloudGallery / PublishTales) rate-limit their WRITE endpoint only,
//  because their read is behind a purchaser credential or a single-slug lookup.
//  The vault's read is a per-vault PARTITION LIST behind a bearer id, so it needs
//  the same protection as the write: without it a scripted caller could
//  enumerate/scrape reads or flood writes. So both POST /api/vault/tales and
//  GET /api/vault/tales carry [EnableRateLimiting] - a distinct policy each.
//
//  The WRITE window is tighter (fewer permits) than the READ window: a device
//  auto-saves at most one tale per finished reveal (rare), while a legitimate read
//  (story 02's gallery merge) may refresh a little more often. Both partition on
//  the caller's IP and fail CLOSED to a shared "unknown" bucket (bounded, never
//  unlimited) when no IP is available. A rejected request gets 429 (see Program.cs
//  RejectionStatusCode); per-IP behind App Service is honored by the
//  ForwardedHeaders config already registered in Program.cs.
//
//  NOTE: the per-IP limiter alone is defeated by an attacker rotating source IPs
//  against ONE vault id - that residual is covered by the per-vault storage cap
//  (AC-07, IVaultStore.MaxTalesPerVault), which holds regardless of source IP.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.Http;

namespace QuibbleStone.Api.Vault;

/// <summary>
/// The per-IP fixed-window rate-limit policies applied to the vault's write
/// (POST /api/vault/tales) and read (GET /api/vault/tales) endpoints
/// (keepsake-vault/01, AC-06). Program.cs registers both policies and the
/// controller actions opt in via [EnableRateLimiting]. The tunables and the
/// partition key live here so they are one source of truth and unit-testable
/// without spinning up the middleware.
/// </summary>
public static class VaultRateLimit
{
    /// <summary>The named policy the vault SAVE action opts into (AC-06 write).</summary>
    public const string SavePolicyName = "VaultSave";

    /// <summary>The named policy the vault LIST action opts into (AC-06 read).</summary>
    public const string ReadPolicyName = "VaultRead";

    /// <summary>
    /// The named policy the claim-code REDEEM action opts into (keepsake-vault/03,
    /// AC-03.1). The FIRST of redemption's three anti-brute-force controls: a per-IP
    /// fixed-window limiter. On its own it is defeated by an attacker rotating source
    /// IPs - which is exactly why the global ceiling (ClaimRedemptionCeiling, AC-03.2)
    /// and the per-code failed-attempt burn (VaultClaim, AC-03.3) sit alongside it.
    /// </summary>
    public const string RedeemPolicyName = "VaultClaimRedeem";

    /// <summary>
    /// Permitted saves per <see cref="Window"/> per client IP. A device auto-saves
    /// one tale per finished reveal, so this is tight - generous for a real family's
    /// pace, low enough to blunt a scripted storage-bloat flood.
    /// </summary>
    public const int SavePermitLimit = 20;

    /// <summary>
    /// Permitted reads per <see cref="Window"/> per client IP. Looser than the save
    /// limit (a gallery view may refresh a little more often, story 02) but still
    /// bounded so a scripted caller cannot scrape reads unbounded.
    /// </summary>
    public const int ReadPermitLimit = 60;

    /// <summary>
    /// Permitted claim-code redemptions per <see cref="Window"/> per client IP
    /// (keepsake-vault/03, AC-03.1). Tight - a legitimate family recovers a vault a
    /// handful of times, so a single IP making many redemption attempts is a guesser.
    /// Bounds one source; the global ceiling bounds the whole endpoint across rotated
    /// IPs (AC-03.2).
    /// </summary>
    public const int RedeemPermitLimit = 10;

    /// <summary>The fixed window all three permit limits apply over.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The rate-limit partition key: the caller's IP (so the limit is per-client,
    /// not global). Falls back to a shared "unknown" bucket when no remote IP is
    /// available - fail-closed, so an IP-less caller is still bounded rather than
    /// unlimited. Anonymous by construction: no account, no vault id - just the IP.
    /// </summary>
    public static string PartitionKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
