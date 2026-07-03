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

    // ---- Moderation (sysadmin-console/03, issue #137) ------------------------
    // These extend the ONE store with the report -> auto-hide -> operator-review
    // path. They mutate ONLY the tiny companion moderation state (a count + a
    // Hidden flag keyed by slug, see TaleModerationState), never the immutable tale
    // body, and never touch any reporter identity / player / room / session (AC-06).

    /// <summary>
    /// Records ONE anonymous report against a published slug and returns the
    /// resulting moderation state (AC-01/AC-02). Auto-hides the tale (sets
    /// <see cref="TaleModerationState.IsHidden"/>) once the accumulated count reaches
    /// <paramref name="autoHideThreshold"/>. A report against an unknown / expired /
    /// revoked slug is a no-op that returns null (there is nothing to moderate). This
    /// is a HUMAN signal only - it NEVER re-runs the content-safety filter (AC-04).
    /// </summary>
    Task<TaleModerationState?> ReportAsync(string slug, int autoHideThreshold, CancellationToken cancellationToken = default);

    /// <summary>
    /// The current moderation state for a slug (count + hidden), or the unreported
    /// default (count 0, not hidden) when the slug has never been reported. The
    /// serve path (GET /t/{slug}) reads this to decide the neutral "under review"
    /// page vs serving the tale - a DISTINCT decision from lazy expiry (AC-02).
    /// </summary>
    Task<TaleModerationState> GetModerationAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>
    /// The operator review queue (AC-03): every CURRENTLY hidden tale with its stored
    /// content and report count, so an operator can review the content (never a
    /// person, AC-06) and decide. Ordered most-reported-first is a nicety, not a
    /// contract.
    /// </summary>
    Task<IReadOnlyList<ReportedTaleView>> ListHiddenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: confirm a hidden tale stays gone (AC-03). After this the slug
    /// NEVER serves again (hard-deleted here) and drops off the review queue. Returns
    /// true when a hidden tale was found and confirmed; false (idempotent no-op) for
    /// an unknown / not-hidden slug.
    /// </summary>
    Task<bool> ConfirmHiddenAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: restore a hidden tale (AC-03). It resumes serving normally at
    /// its slug AND its report count is RESET to zero, so the same reports do not
    /// immediately re-hide it. Returns true when a hidden tale was found and restored;
    /// false (idempotent no-op) for an unknown / not-hidden slug.
    /// </summary>
    Task<bool> RestoreAsync(string slug, CancellationToken cancellationToken = default);
}
