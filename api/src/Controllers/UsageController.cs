// ----------------------------------------------------------------------------
//  UsageController - the minimal server-side seam for the SOLO client's anonymous
//  product-usage beacon (platform-devops/05, AC-01/AC-02).
//
//  WHY SERVER-SIDE (mirrors ClientErrorController exactly): a GROUP round records
//  its usage events straight from GameHub (server-side, it has the round in hand).
//  A SOLO round is a single browser tab with NO SignalR round-trip, so the web
//  client fire-and-forgets a tiny anonymous payload here (see
//  web/src/telemetry/usageBeacon.ts) and THIS endpoint forwards it to App Insights
//  via the injected TelemetryClient. That keeps the App Insights connection string
//  entirely SERVER-SIDE (never a VITE_ var, never in the browser - README section 6,
//  AC-05) and routes every solo usage event through the SAME
//  PiiScrubbingTelemetryInitializer choke point as all other telemetry (AC-04). It
//  rides story 04's App Insights pipeline - NOT a third telemetry stack (AC-06):
//  the serve log (story-selection/04) is a separate Table Storage sink for content
//  curation; this is App Insights custom events for product usage.
//
//  ANONYMOUS + NO AUTH (there are no accounts, README section 3): the request
//  carries ONLY anonymous facts - the event type, a stable enum-ish mode id, an
//  optional duration, and an anonymous device-local id (AC-03, an approximate
//  DEVICE count, never a verified person - reset when the player clears storage).
//  There is NO nickname, join code, player/connection session id, submitted word,
//  or story text - the beacon sends none and this controller records none (AC-04).
//  The mode is normalized server-side to a known enum-ish id (UsageTelemetry.
//  NormalizeMode) so a crafted client cannot ride free text into the metrics. Like
//  the other telemetry seams here it always accepts (202) and never lets a track
//  failure fault the response, so a beacon can never wedge or slow the solo flow
//  (AC-08).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Mvc;
using QuibbleStone.Api.Telemetry;

namespace QuibbleStone.Api.Controllers;

/// <summary>
/// Request body for POST /api/usage (a SOLO round start or completion, AC-01/AC-02).
/// Carries ONLY anonymous facts - never a nickname, join code, player/connection
/// session id, submitted word, or story text (AC-04). An unknown
/// <see cref="EventType"/> is dropped silently server-side; an unknown
/// <see cref="Mode"/> is normalized to "unknown".
/// </summary>
/// <param name="EventType">"RoundStarted" or "RoundCompleted"; anything else is dropped silently.</param>
/// <param name="Mode">The stable, enum-ish mode id (e.g. "classic-blind"); normalized server-side, never free text.</param>
/// <param name="DurationMs">The round/session duration in ms (RoundCompleted only); null/absent for a start.</param>
/// <param name="DeviceId">An opaque, device-local anonymous id (localStorage-minted). NOT an account, NOT a person - an approximate device count (AC-03).</param>
public sealed record UsageEventRequest(
    string? EventType,
    string? Mode,
    double? DurationMs,
    string? DeviceId);

[ApiController]
[Route("api/usage")]
public sealed class UsageController : ControllerBase
{
    private readonly TelemetryClient _telemetryClient;

    public UsageController(TelemetryClient telemetryClient)
    {
        _telemetryClient = telemetryClient;
    }

    /// <summary>
    /// POST /api/usage -> 202 Accepted (always). Forwards ONE anonymous SOLO usage
    /// event to App Insights via TelemetryClient (AC-01/AC-02), so it flows through
    /// the same PII scrubber as all other telemetry (AC-04) and the AI connection
    /// string stays server-side (AC-05). Drops an unknown event type silently,
    /// always returns 202, and never lets a track failure fault the response (AC-08).
    /// Anonymous, no auth. When no connection string is configured the TelemetryClient
    /// track call is a clean no-op (AC-05/AC-08).
    /// </summary>
    [HttpPost]
    public IActionResult Report([FromBody] UsageEventRequest? request)
    {
        // Only the two known event types are recorded; anything else (a null body,
        // an invented type) is dropped silently but still returns 202 so the beacon
        // never observes an error.
        var eventType = request?.EventType;
        var isStart = string.Equals(eventType, UsageTelemetry.RoundStartedEvent, StringComparison.Ordinal);
        var isComplete = string.Equals(eventType, UsageTelemetry.RoundCompletedEvent, StringComparison.Ordinal);
        if (!isStart && !isComplete)
        {
            return Accepted();
        }

        try
        {
            // Solo context always; mode is normalized to a known enum-ish id; the
            // anonymous device id is attached as a property (it survives the scrubber
            // by design - it is a device count key, never a player session id, AC-03).
            var properties = UsageTelemetry.BuildProperties(
                request!.Mode ?? string.Empty,
                UsageTelemetry.SoloContext,
                deviceId: request.DeviceId);

            if (isComplete)
            {
                // A completion carries the anonymous session duration as a metric -
                // but ONLY when the client actually supplied one. A missing duration
                // is OMITTED (not recorded as 0ms), so absent-duration completions
                // never skew a median-session-length query with artificial zeroes.
                var metrics = request.DurationMs is double durationMs
                    ? UsageTelemetry.BuildDurationMetric(durationMs)
                    : null;
                _telemetryClient.TrackEvent(UsageTelemetry.RoundCompletedEvent, properties, metrics);
            }
            else
            {
                _telemetryClient.TrackEvent(UsageTelemetry.RoundStartedEvent, properties);
            }
        }
        catch
        {
            // Swallowed: telemetry must never surface an error to the client (AC-08).
        }

        return Accepted();
    }
}
