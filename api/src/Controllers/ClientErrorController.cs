// ----------------------------------------------------------------------------
//  ClientErrorController - the minimal server-side seam for the web client's
//  anonymous unhandled-error beacon (platform-devops/04, AC-06).
//
//  WHY SERVER-SIDE: the web client reports unhandled JS errors, but we
//  deliberately do NOT ship the Application Insights JS SDK to the browser (PWA
//  bundle cost, CLAUDE.md section 10) and we NEVER put the App Insights connection
//  string in a VITE_ var (it would ship to the browser - README section 6 / AC-05,
//  secrets stay server-side). So the browser fire-and-forgets a tiny anonymous
//  payload here (see web/src/telemetry/errorBeacon.ts), and THIS endpoint forwards
//  it to App Insights via the injected TelemetryClient. That means the client error
//  flows through the SAME PiiScrubbingTelemetryInitializer choke point as all other
//  telemetry (AC-04), and the AI connection string stays entirely server-side.
//
//  ANONYMOUS + NO AUTH (there are no accounts, README section 3): the request
//  carries ONLY a message, a stack, and location.pathname (NO query string, NO
//  nickname, NO room code, NO PII - the beacon strips all of that client-side and
//  this controller records only these three anonymous fields). Like the other
//  telemetry seams here (TelemetryController, ModerationController) it always
//  accepts (202) and never lets a track failure fault the response, so a beacon
//  can never wedge or slow the web client.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.AspNetCore.Mvc;

namespace QuibbleStone.Api.Controllers;

/// <summary>
/// Request body for POST /api/client-errors (AC-06). Carries ONLY anonymous
/// operational facts about an unhandled client-side error - a message, a stack,
/// and the pathname the error happened on. There is intentionally NO field that
/// could carry PII (no nickname, no room/join code, no query string, no session
/// id) - the beacon sends only these three, and the server records only these
/// three (AC-04).
/// </summary>
/// <param name="Message">The error message (e.g. "TypeError: x is undefined").</param>
/// <param name="Stack">The error stack, when available; may be null.</param>
/// <param name="Path">location.pathname only - the route the error occurred on, never a query string.</param>
public sealed record ClientErrorRequest(
    string? Message,
    string? Stack,
    string? Path);

[ApiController]
[Route("api/client-errors")]
public sealed class ClientErrorController : ControllerBase
{
    // Custom-property keys for the anonymous client-error trace. All three are
    // anonymous operational data (no PII); the scrubber is the backstop, but by
    // construction there is nothing sensitive to scrub here.
    private const string StackPropertyKey = "clientStack";
    private const string PathPropertyKey = "clientPath";
    private const string SourcePropertyKey = "source";
    private const string SourceValue = "web-error-beacon";

    private readonly TelemetryClient _telemetryClient;

    public ClientErrorController(TelemetryClient telemetryClient)
    {
        _telemetryClient = telemetryClient;
    }

    /// <summary>
    /// POST /api/client-errors -> 202 Accepted (always). Forwards ONE anonymous
    /// client-side error to App Insights via TelemetryClient (AC-06), so it flows
    /// through the same PII scrubber as all other telemetry (AC-04) and the AI
    /// connection string stays server-side (AC-05). Drops an empty message
    /// silently, always returns 202, and never lets a track failure fault the
    /// response. Anonymous, no auth. When no connection string is configured the
    /// TelemetryClient track call is a clean no-op (AC-05).
    /// </summary>
    [HttpPost]
    public IActionResult Report([FromBody] ClientErrorRequest? request)
    {
        // Drop an empty/missing message silently (nothing useful to record) but
        // still return 202 so the beacon never observes an error.
        var message = request?.Message;
        if (string.IsNullOrWhiteSpace(message))
        {
            return Accepted();
        }

        try
        {
            // Track as a trace carrying ONLY the three anonymous fields. The
            // scrubber zeroes the IP and drops any sensitive property; by
            // construction we attach none here.
            var trace = new TraceTelemetry(message, SeverityLevel.Error);
            trace.Properties[SourcePropertyKey] = SourceValue;
            if (!string.IsNullOrWhiteSpace(request!.Stack))
            {
                trace.Properties[StackPropertyKey] = request.Stack;
            }
            if (!string.IsNullOrWhiteSpace(request.Path))
            {
                // Defense-in-depth (AC-04): never trust the client path. Reduce it
                // to its top-level route segment server-side too, so even a
                // misbehaving client cannot land a deep-link join code (the
                // "/join/:code" route) in telemetry. The scrubber allowlist does
                // not cover clientPath by design, so this is the backstop.
                trace.Properties[PathPropertyKey] = NormalizeRoutePath(request.Path);
            }

            _telemetryClient.TrackTrace(trace);
        }
        catch
        {
            // Swallowed: telemetry must never surface an error to the client (AC-06).
        }

        return Accepted();
    }

    /// <summary>
    /// Reduce a client-supplied pathname to its TOP-LEVEL route segment (AC-04
    /// backstop, mirrors web/src/telemetry/errorBeacon.ts normalizeRoutePath): a
    /// deep-link route like "/join/ABCD" carries the join CODE in its tail, so we
    /// keep only the first segment ("/join") and drop anything after it. Null /
    /// empty / "/" all map to "/". This never trusts the client value.
    /// </summary>
    private static string NormalizeRoutePath(string path)
    {
        var firstSegment = path
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        return firstSegment is null ? "/" : $"/{firstSegment}";
    }
}
