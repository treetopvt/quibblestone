// ----------------------------------------------------------------------------
//  TelemetryController - the REST seam onto the anonymous serve log for SOLO
//  rounds (story-selection/04, AC-02) AND the anonymous per-tale thumbs
//  up/down curation vote (story-selection/05, AC-01/AC-02, issue #95).
//
//  A GROUP round start records its serve event server-side, straight from
//  GameHub (it already has the room + roster in hand). A SOLO round has NO
//  SignalR round-trip - it is a single browser tab playing itself - so the web
//  client fire-and-forgets a tiny POST here on each solo round start, exactly
//  the way single-player reaches the safety filter through ModerationController.
//  This controller is the REST equivalent for the serve log.
//
//  A THUMBS VOTE (story-selection/05) is ALWAYS a plain per-device REST write
//  here, for BOTH solo and group - there is no room state involved (contrast
//  the reveal-delight Reaction row, which lives on the hub). Every player votes
//  independently; one player's vote never touches another's (AC-03).
//
//  Like ModerationController, this has NO logic of its own beyond shaping each
//  request: it validates the template id against the authoritative catalog,
//  DROPS junk SILENTLY (an unknown / invented id, or - for feedback - a vote
//  that is not "up"/"down", is never stored and never surfaced as an error -
//  AC-02), and hands a well-formed, anonymous event to the injected sink
//  WITHOUT letting a sink failure fault the response (AC-03/AC-05).
//
//  ANONYMOUS + NO AUTH: there are no accounts (README section 3), so both
//  endpoints are anonymous. Every request DTO carries ONLY anonymous facts
//  (template id, mode, an opaque per-device session GUID, and - for feedback -
//  the vote + an opaque per-round vote id) - never a nickname, never PII
//  (AC-04). The response says nothing an attacker could probe with: it always
//  accepts (202), whether the input was valid or junk.
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

/// <summary>
/// Request body for POST /telemetry/feedback (a thumbs up/down vote on a tale,
/// story-selection/05, AC-01/AC-02). Carries ONLY anonymous facts - never a
/// nickname, join code, or any PII (AC-04). An unknown <see cref="TemplateId"/>
/// or an invalid <see cref="Vote"/> is dropped silently server-side.
/// </summary>
/// <param name="TemplateId">The voted-on template's id; validated against the catalog and dropped silently if unknown.</param>
/// <param name="Vote">Must be "up" or "down"; any other value is dropped silently.</param>
/// <param name="Mode">The play mode the tale was served under ("solo" or "classic-blind").</param>
/// <param name="SessionId">The SAME opaque per-device session GUID story-selection/04 uses (reused, never re-minted).</param>
/// <param name="VoteId">An opaque, per-round GUID minted client-side once per viewing of the reveal/recap screen; doubles as the storage RowKey so a changed vote overwrites (AC-02).</param>
public sealed record FeedbackRequest(
    string? TemplateId,
    string? Vote,
    string? Mode,
    string? SessionId,
    string? VoteId);

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

    // story-selection/05: the only two accepted thumbs values. Anything else is
    // junk and is dropped silently (AC-02).
    private const string UpVote = "up";
    private const string DownVote = "down";

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
    /// POST /telemetry/feedback -> 202 Accepted (always). Records ONE anonymous
    /// thumbs up/down curation vote on a tale's TEMPLATE (story-selection/05,
    /// AC-01/AC-02). Validates the template id against the authoritative catalog
    /// and the vote against the fixed "up"/"down" set; DROPS anything else
    /// silently (never stores it, never leaks an error). Fires the sink WITHOUT
    /// awaiting a fault onto the response: a sink failure is swallowed by the sink
    /// itself (AC-05), so this always returns success and never gates gameplay or
    /// nags the player (AC-07 - skipping the vote simply never calls this).
    /// Anonymous, no auth (there are no accounts).
    /// </summary>
    [HttpPost("feedback")]
    public IActionResult Feedback([FromBody] FeedbackRequest? request)
    {
        // Validate the template id + vote against the authoritative shapes and
        // DROP junk silently (AC-02): an unknown/empty id, an invalid vote, or a
        // missing vote id records NOTHING but still returns 202 so the endpoint
        // never leaks which ids are real. A null body is likewise dropped silently.
        var templateId = request?.TemplateId;
        var vote = request?.Vote;
        var voteId = request?.VoteId;
        var knownTemplate = !string.IsNullOrWhiteSpace(templateId)
            && _catalog.Entries.Any(e => string.Equals(e.Id, templateId, StringComparison.Ordinal));
        var validVote = string.Equals(vote, UpVote, StringComparison.Ordinal)
            || string.Equals(vote, DownVote, StringComparison.Ordinal);
        var hasVoteId = !string.IsNullOrWhiteSpace(voteId);

        if (!knownTemplate || !validVote || !hasVoteId || templateId is null || vote is null || voteId is null)
        {
            return Accepted();
        }

        var feedbackEvent = new FeedbackEvent(
            TemplateId: templateId,
            Vote: vote,
            TimestampUtc: DateTimeOffset.UtcNow,
            Mode: request!.Mode ?? string.Empty,
            SessionId: request.SessionId ?? string.Empty,
            VoteId: voteId);

        // Fire-and-forget (AC-05): start the write but do NOT await it onto the
        // response path, and never let a sink failure fault the 202. The sink
        // swallows its own async failures, and this try/catch guards the defensive
        // case of a sink that throws synchronously - either way the tap gets its
        // immediate acknowledgement and is never gated by telemetry.
        try
        {
            _ = _sink.RecordFeedbackAsync(feedbackEvent, CancellationToken.None);
        }
        catch
        {
            // Swallowed: telemetry must never surface an error to a player (AC-05).
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
