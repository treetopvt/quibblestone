// ----------------------------------------------------------------------------
//  CloudGalleryRateLimit - the per-IP rate-limit policy for the cloud-gallery
//  SAVE endpoint (POST /api/account/gallery, keepsake-gallery/05). Mirrors
//  PublishTalesRateLimit (the reference pattern).
//
//  Why this exists: POST /api/account/gallery is a signed-in WRITE endpoint that
//  persists a Table Storage row per save. Unlike the anonymous publish endpoint it
//  requires a valid purchaser credential, but a throttle is still cheap defense-in-
//  depth against a compromised / scripted credential flooding the store to bloat
//  it. The per-part content-safety re-vet stops UNSAFE content, not sheer VOLUME -
//  that is this limiter's job. Only the write path is limited: the gallery READ
//  (GET) and the delete paths are not decorated, so browsing a large gallery never
//  trips it.
//
//  Policy: a fixed window keyed on the client IP (the same posture as the publish
//  limiter, and partitioned identically so it works behind App Service via the
//  ForwardedHeaders config already registered in Program.cs). A rejected request
//  gets 429 (see Program.cs RejectionStatusCode).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.Http;

namespace QuibbleStone.Api.CloudGallery;

/// <summary>
/// The per-IP fixed-window rate-limit policy applied to POST /api/account/gallery
/// (keepsake-gallery/05). Program.cs registers the policy under
/// <see cref="PolicyName"/> and the controller's save action opts in via
/// [EnableRateLimiting]. The tunables and the partition key live here so they are
/// one source of truth and unit-testable without spinning up the middleware.
/// </summary>
public static class CloudGalleryRateLimit
{
    /// <summary>The named policy the cloud-gallery save action opts into.</summary>
    public const string PolicyName = "CloudGallerySave";

    /// <summary>
    /// Permitted saves per <see cref="Window"/> per client IP. Generous for a
    /// purchaser syncing a batch of tales in one sitting, tight enough to make a
    /// storage-bloat flood ineffective.
    /// </summary>
    public const int PermitLimit = 20;

    /// <summary>The fixed window the <see cref="PermitLimit"/> applies over.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The rate-limit partition key: the caller's IP (so the limit is per-client,
    /// not global). Falls back to a shared "unknown" bucket when no remote IP is
    /// available - fail-closed, so an IP-less caller is still bounded rather than
    /// unlimited.
    /// </summary>
    public static string PartitionKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
