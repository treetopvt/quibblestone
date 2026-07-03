// ----------------------------------------------------------------------------
//  DisabledPublishedTaleStore - the feature-OFF fallback for shareable tale
//  links (keepsake-gallery/04), used when NO storage connection string is
//  configured (local dev, CI, a fresh clone).
//
//  This is the exact analog of the telemetry feature's NoOp sink: with no Azure
//  Storage wired up, the public-link feature is simply switched OFF rather than
//  the app failing to start. Publishing reports "not available" (the controller
//  turns IsEnabled == false into a clear 503) and every read returns null (a
//  404), so a developer can run the whole app - and every OTHER feature - with
//  zero Azure setup. The moment PublishedTales:StorageConnectionString is
//  present (a deployed environment), Program.cs registers the real Table Storage
//  store instead and the feature turns on with no code change.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.PublishedTales;

/// <summary>
/// The disabled published-tale store: publishing is unavailable and every read
/// misses. Registered when no storage connection string is configured (AC-05
/// posture), mirroring the NoOp telemetry sink.
/// </summary>
public sealed class DisabledPublishedTaleStore : IPublishedTaleStore
{
    /// <inheritdoc />
    public bool IsEnabled => false;

    /// <inheritdoc />
    public Task PublishAsync(PublishedTale tale, CancellationToken cancellationToken = default) =>
        // Should never be reached: the controller checks IsEnabled first and short-
        // circuits with a "not available" response. Kept a harmless no-op rather
        // than a throw so a mis-wired caller degrades to "nothing published" (the
        // read then 404s) instead of a 500.
        Task.CompletedTask;

    /// <inheritdoc />
    public Task<PublishedTale?> GetAsync(string slug, CancellationToken cancellationToken = default) =>
        Task.FromResult<PublishedTale?>(null);

    /// <inheritdoc />
    public Task RevokeAsync(string slug, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
