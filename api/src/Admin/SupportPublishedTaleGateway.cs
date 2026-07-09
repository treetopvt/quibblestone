// ----------------------------------------------------------------------------
//  SupportPublishedTaleGateway - the NARROW extend-TTL seam the support console
//  (sysadmin-console/07, issue #243, AC-04) consumes to push out a public tale's
//  expiry WITHOUT the support controller ever holding a byline-bearing reference
//  (AC-08, the structural cross-plane firewall).
//
//  WHY NARROW, NOT IPublishedTaleStore: the published-tale store's GetAsync returns a
//  whole PublishedTale (which carries a byline). AC-04 requires the extend-TTL verb to
//  respond with ONLY the slug + the new expiry, never a byline - and AC-08 requires the
//  controller's constructor to hold no reference that CAN return a byline. So the
//  controller injects this count/enum/instant-only contract; the byline-bearing
//  IPublishedTaleStore is reached ONLY inside the adapter, which reads the tale, bumps
//  its ExpiresUtc through the SAME existing write path (PublishAsync, an upsert keyed by
//  slug - keepsake-gallery/04, no parallel store), and hands back only { outcome, slug,
//  newExpiry }. The slug is a DIRECT content-plane input to this verb (AC-04/AC-05),
//  never a search key that resolves back to an owning account or its byline.
//
//  DEPENDENCY-TOLERANT: when publishing is not configured (the DisabledPublishedTaleStore
//  fallback - local dev / CI / a footprint with no storage), the store's IsEnabled is
//  false and every read returns null; the adapter reports Unavailable so the verb's
//  control renders "not available yet" rather than erroring.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.PublishedTales;

namespace QuibbleStone.Api.Admin;

/// <summary>The outcome of a support extend-TTL action (sysadmin-console/07, AC-04).</summary>
public enum ExtendTaleTtlOutcome
{
    /// <summary>The tale's expiry was pushed out and re-persisted through the existing write path.</summary>
    Extended,

    /// <summary>No live tale resolves for that slug (unknown, revoked, expired, or taken down) - nothing to extend.</summary>
    NotFound,

    /// <summary>Publishing is not configured (the disabled fallback) - the verb reports itself unavailable.</summary>
    Unavailable,
}

/// <summary>
/// The result of a support extend-TTL action (sysadmin-console/07, AC-04): the outcome, the
/// slug it acted on, and the NEW expiry instant - and NOTHING else (never a byline, a tale
/// body, or an owning account). Content-plane facts only.
/// </summary>
/// <param name="Outcome">Whether the TTL was extended, no live tale was found, or the feature is unavailable.</param>
/// <param name="Slug">The slug acted on (echoed back for the operator), or null when unavailable.</param>
/// <param name="NewExpiryUtc">The new expiry instant on success; null otherwise.</param>
public sealed record ExtendTaleTtlResult(ExtendTaleTtlOutcome Outcome, string? Slug, DateTimeOffset? NewExpiryUtc);

/// <summary>
/// A narrow extend-TTL seam over the published-tale store (sysadmin-console/07, AC-04): pushes
/// a public tale's expiry out by a fixed extension through the store's EXISTING write path, and
/// returns ONLY { outcome, slug, new expiry }. Deliberately narrower than
/// <see cref="IPublishedTaleStore"/> so the support controller cannot reach a byline (AC-08).
/// </summary>
public interface IPublishedTaleTtlExtender
{
    /// <summary>
    /// Extends the public tale <paramref name="slug"/>'s TTL (AC-04). Reads the live tale, bumps
    /// its expiry to <paramref name="now"/> + the extension window, and re-persists it through the
    /// SAME existing write path (no parallel store). Returns the outcome + slug + new expiry ONLY.
    /// A slug that resolves to no live tale returns <see cref="ExtendTaleTtlOutcome.NotFound"/>; a
    /// disabled store returns <see cref="ExtendTaleTtlOutcome.Unavailable"/>.
    /// </summary>
    /// <param name="slug">The tale slug - a DIRECT content input to this verb, never an account search key.</param>
    /// <param name="now">The current instant (injected so tests are deterministic).</param>
    /// <param name="ct">Cancellation for the read + write.</param>
    Task<ExtendTaleTtlResult> ExtendTtlAsync(string slug, DateTimeOffset now, CancellationToken ct = default);
}

/// <summary>
/// The real <see cref="IPublishedTaleTtlExtender"/> over the merged
/// <see cref="IPublishedTaleStore"/> (sysadmin-console/07, AC-04). Reads the tale, applies a
/// bumped <c>ExpiresUtc</c> via the SAME <see cref="IPublishedTaleStore.PublishAsync"/> upsert
/// keepsake-gallery/04 already uses (no parallel store), and returns only slug + new expiry - the
/// byline-bearing tale never crosses into the controller (AC-08).
/// </summary>
public sealed class PublishedTaleTtlExtender : IPublishedTaleTtlExtender
{
    /// <summary>
    /// How far a support extend-TTL pushes a tale's expiry out from now (AC-04): a fresh full
    /// window, matching the publish-time TTL default (<see cref="PublishedTalesController.TaleTtlDays"/>).
    /// A named constant, not a magic number - a settings-key candidate under the control plane.
    /// </summary>
    public static readonly TimeSpan Extension = TimeSpan.FromDays(PublishedTalesController.TaleTtlDays);

    private readonly IPublishedTaleStore _tales;

    /// <summary>Constructs the extender over the merged published-tale store (keepsake-gallery/04).</summary>
    public PublishedTaleTtlExtender(IPublishedTaleStore tales) => _tales = tales;

    /// <inheritdoc />
    public async Task<ExtendTaleTtlResult> ExtendTtlAsync(string slug, DateTimeOffset now, CancellationToken ct = default)
    {
        // Publishing off (the disabled fallback): report unavailable so the verb's control
        // renders "not available yet" rather than erroring (dependency-tolerant).
        if (!_tales.IsEnabled)
        {
            return new ExtendTaleTtlResult(ExtendTaleTtlOutcome.Unavailable, null, null);
        }

        // A GetAsync miss (unknown / revoked / expired / taken-down) means there is no live
        // tale to extend - a clear not-found, never an error.
        var tale = await _tales.GetAsync(slug, ct);
        if (tale is null)
        {
            return new ExtendTaleTtlResult(ExtendTaleTtlOutcome.NotFound, slug, null);
        }

        // Bump the expiry a fresh full window out and re-persist through the SAME upsert write
        // path (PublishAsync is keyed by slug). Only ExpiresUtc changes - the immutable content,
        // the byline, and every other field are carried through unmodified. The byline-bearing
        // record is used here and NEVER returned to the controller.
        var newExpiry = now + Extension;
        await _tales.PublishAsync(tale with { ExpiresUtc = newExpiry }, ct);
        return new ExtendTaleTtlResult(ExtendTaleTtlOutcome.Extended, tale.Slug, newExpiry);
    }
}
