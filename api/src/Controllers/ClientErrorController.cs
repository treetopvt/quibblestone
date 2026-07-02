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
//  ANONYMOUS + NO AUTH + NO-CONTENT BY CONSTRUCTION (there are no accounts, README
//  section 3, and the audience is minors, section 6): the endpoint is
//  unauthenticated, so the message and stack the browser sends are UNTRUSTED free
//  text that could carry an interpolated nickname / word / story (e.g.
//  `throw new Error("bad word: " + word)`) or be forged outright. The PII scrubber
//  drops sensitive property KEYS, not free-text trace VALUES, so this controller is
//  the trust boundary: it records ONLY a sanitized, allowlisted JS error TYPE
//  (TypeError / ReferenceError / ...) plus the normalized top-level route - it NEVER
//  persists the raw message or the raw stack to telemetry. That keeps the beacon
//  useful (which error type, on which route) while carrying no content by
//  construction. Like the other telemetry seams here (TelemetryController,
//  ModerationController) it always accepts (202) and never lets a track failure
//  fault the response, so a beacon can never wedge or slow the web client.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.AspNetCore.Mvc;

namespace QuibbleStone.Api.Controllers;

/// <summary>
/// Request body for POST /api/client-errors (AC-06). The browser sends a message,
/// an optional stack, and the route path. Message and Stack are UNTRUSTED free text
/// (unauthenticated endpoint) - the server reduces Message to an allowlisted error
/// TYPE and does NOT persist Stack or the raw message to telemetry (AC-04); Path is
/// re-normalized server-side to its top-level route. So no field here can leak a
/// nickname, join code, session id, submitted word, or story text into telemetry.
/// </summary>
/// <param name="Message">The error message (e.g. "TypeError: x is undefined"); reduced server-side to an allowlisted error type, never persisted raw.</param>
/// <param name="Stack">The error stack, when available; accepted but NEVER recorded to telemetry (untrusted free text).</param>
/// <param name="Path">location.pathname; re-normalized server-side to the top-level route, never a query string.</param>
public sealed record ClientErrorRequest(
    string? Message,
    string? Stack,
    string? Path);

[ApiController]
[Route("api/client-errors")]
public sealed class ClientErrorController : ControllerBase
{
    // Custom-property keys for the anonymous client-error trace. Only the
    // normalized route + a source marker are recorded - both anonymous operational
    // data (no PII). The raw stack is NEVER recorded (untrusted free text).
    private const string PathPropertyKey = "clientPath";
    private const string SourcePropertyKey = "source";
    private const string SourceValue = "web-error-beacon";

    // The generic label recorded when the client message is not a recognized JS
    // error type - so no free-text message ever reaches telemetry.
    private const string GenericErrorType = "ClientError";

    // The ONLY error-type tokens we record (the built-in JS/DOM error names). Any
    // message whose leading token is not one of these collapses to
    // GenericErrorType, so an interpolated word / nickname / story can never ride
    // the trace message into telemetry (AC-04). Ordinal: these are exact identifiers.
    private static readonly HashSet<string> KnownErrorTypes = new(StringComparer.Ordinal)
    {
        "Error", "EvalError", "RangeError", "ReferenceError", "SyntaxError",
        "TypeError", "URIError", "AggregateError", "InternalError", "DOMException",
    };

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
            // Reduce the untrusted client message to an allowlisted error TYPE, so
            // the trace carries no free text (AC-04). The raw message and stack are
            // NEVER recorded. Only the sanitized type, the normalized route, and a
            // source marker leave the process - all anonymous operational data.
            var errorType = SanitizeErrorType(message);
            var trace = new TraceTelemetry(errorType, SeverityLevel.Error);
            trace.Properties[SourcePropertyKey] = SourceValue;
            if (!string.IsNullOrWhiteSpace(request!.Path))
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

    /// <summary>
    /// Reduces an untrusted client error message to an allowlisted JS error TYPE
    /// (AC-04). A browser Error stringifies as "TypeError: ...", so we take the
    /// leading token (up to the first ':' or space) and record it ONLY if it is a
    /// known built-in error type; anything else (a custom name, or an interpolated
    /// free-text message with no recognizable type) collapses to "ClientError". So
    /// no free text - which could carry a nickname, submitted word, or story - can
    /// ever ride the trace message into telemetry.
    /// </summary>
    private static string SanitizeErrorType(string message)
    {
        var token = message.AsSpan().Trim();
        var cut = token.IndexOfAny(':', ' ');
        if (cut >= 0)
        {
            token = token[..cut];
        }

        var candidate = token.ToString();
        return KnownErrorTypes.Contains(candidate) ? candidate : GenericErrorType;
    }
}
