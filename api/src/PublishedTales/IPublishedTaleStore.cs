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
    /// Operator action: confirm a hidden tale stays down (AC-03). SOFT-deletes the
    /// tale (keepsake-vault/04, AC-04): after this the slug stops serving (reads as
    /// GONE, exactly as when it was hard-deleted) and it drops off the review queue,
    /// BUT the tale body is retained and stays recoverable within the takedown
    /// restore window via <see cref="RestoreFromTakedownAsync"/> - a wrongly-hidden
    /// tale or an operator mistake is no longer unrecoverable. Past the restore
    /// window the body is reclaimed lazily on read (AC-03). Returns true when a
    /// hidden tale was found and taken down; false (idempotent no-op) for an unknown
    /// / not-hidden slug.
    /// </summary>
    Task<bool> ConfirmHiddenAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: UN-HIDE a reported-but-still-present tale (AC-03). This is the
    /// EXISTING moderation restore - it acts on a tale that was auto-hidden by reports
    /// but whose body was NEVER deleted: it resumes serving normally at its slug AND
    /// resets the report count to zero, so the same reports do not immediately re-hide
    /// it. Returns true when a hidden tale was found and un-hidden; false (idempotent
    /// no-op) for an unknown / not-hidden slug.
    ///
    /// DISTINCT from <see cref="RestoreFromTakedownAsync"/> (keepsake-vault/04): this
    /// un-hides a tale that was never body-deleted; that one un-DELETES a tale a
    /// confirm-hidden takedown soft-deleted. The two must never be confused in a
    /// console reader's eyes - hence the deliberately different names (AC-04) and the
    /// takedown path's required confirmation argument (AC-07).
    /// </summary>
    Task<bool> RestoreAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: UN-DELETE a tale a moderation takedown soft-deleted
    /// (keepsake-vault/04, AC-04/AC-06). Clears the takedown marker so the tale
    /// resumes serving at its slug EXACTLY as it was before the takedown - the same
    /// already-filtered content, byte-for-byte, with no re-vet, no content mutation,
    /// and no re-publish ceremony (AC-05/AC-06). Returns true when a taken-down tale
    /// (within its restore window) was found and restored; false for an unknown slug,
    /// a tale that was not taken down, or one already past its restore window
    /// (genuinely gone, AC-03).
    ///
    /// STRONGER FRICTION THAN A PLAYER'S OWN VAULT RESTORE (AC-07): a takedown restore
    /// re-exposes content that was reported and confirmed hidden by an operator for a
    /// reason - materially higher-risk than a family undoing its own delete of content
    /// only that family saw. So this signature REQUIRES an explicit
    /// <paramref name="confirmedByOperator"/> marker that the plain vault
    /// <see cref="Vault.IVaultStore.RestoreAsync"/> has no equivalent of: a caller
    /// cannot invoke this path without affirmatively supplying it (a compile-time,
    /// structural distinction, not a documented convention). The operator-facing
    /// confirmation UX (a type-to-confirm step or similar) is sysadmin-console/07's
    /// support verb, which passes this argument only AFTER its own confirmation step;
    /// there is no public endpoint for it in this story. A call with
    /// <paramref name="confirmedByOperator"/> false is refused (returns false) as a
    /// defensive backstop to the structural requirement.
    /// </summary>
    /// <param name="slug">The taken-down tale's slug.</param>
    /// <param name="confirmedByOperator">
    /// The required confirmation marker (AC-07). Must be true; the call is a no-op
    /// otherwise. Its very presence in the signature is the structural friction that
    /// sets a takedown restore apart from a player's own vault-delete restore.
    /// </param>
    Task<bool> RestoreFromTakedownAsync(string slug, bool confirmedByOperator, CancellationToken cancellationToken = default);
}
