// ----------------------------------------------------------------------------
//  NoOpTelemetrySink - the DEFAULT serve-log sink for zero-setup local dev
//  (story-selection/04, AC-05).
//
//  QuibbleStone runs with ZERO Azure setup out of the box (CLAUDE.md section 10:
//  Storage is provisioned but unused by the skeleton). When no storage connection
//  string is configured (local dev, CI, a fresh clone), Program.cs registers THIS
//  sink instead of the Table Storage one, so the serve-log seam is always present
//  and the app behaves EXACTLY as it did before this story: a round starts, this
//  sink logs the event at Debug and returns a completed task, and nothing is
//  persisted anywhere.
//
//  It exists so every caller can depend on ITelemetrySink unconditionally - the
//  hub and the controller never branch on "is telemetry configured?"; they just
//  fire-and-forget at the one seam, and the no-op is what that seam does locally.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;

namespace QuibbleStone.Api.Telemetry;

/// <summary>
/// The no-op serve-log sink (story-selection/04, AC-05): logs the event at Debug
/// and drops it. The DEFAULT when no storage connection is configured, so local
/// dev / CI need no Azure at all. Never throws, never blocks - the whole point is
/// that gameplay is untouched whether or not a real sink is wired up (AC-03).
/// </summary>
public sealed class NoOpTelemetrySink : ITelemetrySink
{
    private readonly ILogger<NoOpTelemetrySink> _logger;

    public NoOpTelemetrySink(ILogger<NoOpTelemetrySink> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task RecordServeAsync(ServeEvent serveEvent, CancellationToken cancellationToken = default)
    {
        // Log the ANONYMOUS fields only (no PII exists on ServeEvent to leak) so a
        // developer can see the serve log working locally without any Azure setup.
        _logger.LogDebug(
            "Serve (no-op sink): template={TemplateId} mode={Mode} length={LengthClass} players={PlayerCount} familySafe={FamilySafe} instance={InstanceId} at={TimestampUtc:o}",
            serveEvent.TemplateId,
            serveEvent.Mode,
            serveEvent.LengthClass,
            serveEvent.PlayerCount,
            serveEvent.FamilySafe,
            serveEvent.InstanceId,
            serveEvent.TimestampUtc);

        return Task.CompletedTask;
    }
}
