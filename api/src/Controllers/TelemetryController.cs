// ----------------------------------------------------------------------------
//  TelemetryController - the REST seam onto the anonymous serve log for SOLO
//  rounds (story-selection/04, AC-02).
//
//  A GROUP round start records its serve event server-side, straight from
//  GameHub (it already has the room + roster in hand). A SOLO round has NO
//  SignalR round-trip - it is a single browser tab playing itself - so the web
//  client fire-and-forgets a tiny POST here on each solo round start, exactly
//  the way single-player reaches the safety filter through ModerationController.
//  This controller is the REST equivalent for the serve log.
//
//  Like ModerationController, this has NO logic of its own beyond shaping the
//  request: it validates the template id against the authoritative catalog,
//  DROPS junk SILENTLY (an unknown / invented id is never stored and never
//  surfaced as an error - AC-02), and hands a well-formed, anonymous ServeEvent
//  to the injected sink WITHOUT letting a sink failure fault the response (AC-03).
//
//  ANONYMOUS + NO AUTH: there are no accounts (README section 3), so this
//  endpoint is anonymous. The request DTO carries ONLY anonymous facts (template
//  id, mode, length class, family-safe flag, an opaque per-device session GUID) -
//  never a nickname, never PII (AC-04). The response says nothing an attacker
//  could probe with: it always accepts (202), whether the id was valid or junk.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.Mvc;
using QuibbleStone.Api.Content;
using QuibbleStone.Api.Telemetry;

namespace QuibbleStone.Api.Controllers;

/// <summary>
/// Request body for POST /telemetry/serve (a SOLO round start, AC-02). Carries
/// ONLY anonymous facts - never a nickname, join code, or any PII (AC-04). An
/// unknown <see cref="TemplateId"/> is dropped silently server-side.
/// </summary>
/// <param name="TemplateId">The served template's id; validated against the catalog and dropped silently if unknown.</param>
/// <param name="Mode">The play mode; solo always sends "solo" (defaulted server-side when absent).</param>
/// <param name="LengthClass">The derived length class ("quick" or "full") the client computed off the template's blank count.</param>
/// <param name="FamilySafe">The family-safe toggle position the solo round was played under.</param>
/// <param name="SessionId">An opaque, per-device session GUID (localStorage-minted). NOT an account and NOT tied to a person (AC-04).</param>
public sealed record ServeLogRequest(
    string? TemplateId,
    string? Mode,
    string? LengthClass,
    bool FamilySafe,
    string? SessionId);

[ApiController]
[Route("telemetry")]
public sealed class TelemetryController : ControllerBase
{
    // AC-02: solo serves are always "solo". A client that omits the mode still
    // records a correctly-attributed solo event rather than a blank mode.
    private const string SoloMode = "solo";

    // The two derived length classes (story-01). An unrecognized value from the
    // wire normalizes to "full" - the same default the solo UI starts on - so a
    // malformed client can never inject an arbitrary length label into the log.
    private const string QuickLengthClass = "quick";
    private const string FullLengthClass = "full";

    private readonly TemplateCatalog _catalog;
    private readonly ITelemetrySink _sink;

    public TelemetryController(TemplateCatalog catalog, ITelemetrySink sink)
    {
        _catalog = catalog;
        _sink = sink;
    }

    /// <summary>
    /// POST /telemetry/serve -> 202 Accepted (always). Records ONE anonymous
    /// "template served" event for a solo round (AC-02). Validates the template id
    /// against the authoritative catalog and DROPS an unknown / invented id
    /// silently (never stores it, never leaks an error). Fires the sink WITHOUT
    /// awaiting a fault onto the response: a sink failure is swallowed by the sink
    /// itself (AC-03), so this always returns success and never gates the solo flow.
    /// Anonymous, no auth (there are no accounts).
    /// </summary>
    [HttpPost("serve")]
    public IActionResult Serve([FromBody] ServeLogRequest? request)
    {
        // Validate the template id against the catalog and DROP junk silently
        // (AC-02): an unknown, empty, or invented id records NOTHING but still
        // returns 202 so the endpoint never leaks which ids are real. A null body
        // (a JSON `null` or binder edge case) is likewise dropped silently.
        var templateId = request?.TemplateId;
        var known = !string.IsNullOrWhiteSpace(templateId)
            && _catalog.Entries.Any(e => string.Equals(e.Id, templateId, StringComparison.Ordinal));
        if (!known || templateId is null)
        {
            return Accepted();
        }

        var serveEvent = new ServeEvent(
            TemplateId: templateId,
            TimestampUtc: DateTimeOffset.UtcNow,
            Mode: SoloMode,
            LengthClass: NormalizeLengthClass(request!.LengthClass),
            PlayerCount: 1, // a solo round is always one player (a COUNT, never identity).
            FamilySafe: request.FamilySafe,
            InstanceId: request.SessionId ?? string.Empty);

        // Fire-and-forget (AC-03): start the write but do NOT await it onto the
        // response path, and never let a sink failure fault the 202. The sink
        // swallows its own async failures, and this try/catch guards the defensive
        // case of a sink that throws synchronously - either way the solo flow gets
        // its immediate success and is never gated by telemetry.
        try
        {
            _ = _sink.RecordServeAsync(serveEvent, CancellationToken.None);
        }
        catch
        {
            // Swallowed: telemetry must never surface an error to a player (AC-03).
        }

        return Accepted();
    }

    /// <summary>
    /// Normalizes a client-supplied length class to "quick" or "full" (defaulting
    /// to "full", the solo UI's own default), so a malformed client can never
    /// inject an arbitrary label into the serve log.
    /// </summary>
    private static string NormalizeLengthClass(string? lengthClass) =>
        string.Equals(lengthClass, QuickLengthClass, StringComparison.OrdinalIgnoreCase)
            ? QuickLengthClass
            : FullLengthClass;
}
