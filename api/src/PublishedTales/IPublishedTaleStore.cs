// ----------------------------------------------------------------------------
//  IPublishedTaleStore - the storage contract for published shareable tales
//  (keepsake-gallery/04).
//
//  There are exactly TWO implementations, chosen once at startup by whether a
//  storage connection string is configured (see Program.cs), EXACTLY mirroring
//  the telemetry sink's NoOp-when-absent posture:
//
//    - TableStoragePublishedTaleStore : the real Azure Table Storage impl, used
//      when PublishedTales:StorageConnectionString is present (deployed).
//    - DisabledPublishedTaleStore     : the feature-OFF fallback used with NO
//      connection string (local dev, CI, a fresh clone) - publish reports "not
//      available" and every read returns null, so the app builds and runs with
//      the public-link feature simply switched off and ZERO Azure setup.
//
//  Consumers branch on <see cref="IsEnabled"/> rather than catching a "disabled"
//  exception, so the OFF path is an ordinary, explicit code path (the controller
//  returns a clear 503 on publish and a 404 on read).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.PublishedTales;

/// <summary>
/// Stores and retrieves published public tales, keyed by their unguessable slug
/// (AC-05). One implementation writes to Azure Table Storage; the other is a
/// disabled no-op used when no storage connection string is configured.
/// </summary>
public interface IPublishedTaleStore
{
    /// <summary>
    /// Whether publishing is actually available (a storage connection string is
    /// configured). False for the disabled fallback - the controller checks this
    /// to return a clear "publishing not available" result instead of a 500.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Persists an already-vetted, already-filtered tale under its slug (AC-05).
    /// The caller mints the slug (see SlugGenerator) and re-vets the coral words
    /// (see PublishedTalesController) BEFORE calling this - the store only stores.
    /// </summary>
    Task PublishAsync(PublishedTale tale, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the tale for a slug, or null if it is unknown, revoked, or expired
    /// (lazy expiry-on-read, AC-05). A single-partition point read (PartitionKey =
    /// slug), so it never scans (AC-05).
    /// </summary>
    Task<PublishedTale?> GetAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the tale for a slug so the link stops resolving (AC-07). Idempotent:
    /// revoking an unknown / already-gone slug is a no-op, never an error.
    /// </summary>
    Task RevokeAsync(string slug, CancellationToken cancellationToken = default);
}
