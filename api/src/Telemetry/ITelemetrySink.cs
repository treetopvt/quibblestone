// ----------------------------------------------------------------------------
//  ITelemetrySink + ServeEvent + FeedbackEvent - the anonymous "template
//  served" serve log (story-selection/04) AND the anonymous "did you like this
//  story" curation vote (story-selection/05, issue #95).
//
//  WHAT THIS IS: the ONE seam through which QuibbleStone records the smallest
//  honest, anonymous, fire-and-forget events about a TEMPLATE (never a person):
//    - ServeEvent    (story-selection/04): "a template was served" - one on
//      each GROUP round start (the server, from GameHub) and one on each SOLO
//      round start (the client, via TelemetryController).
//    - FeedbackEvent (story-selection/05): "a player thumbs-up/down'd the tale"
//      - a QUIET, per-player, per-round curation vote on the TEMPLATE, recorded
//      at the end of a tale (solo Reveal and group RoundComplete), via
//      TelemetryController. This is NOT the reveal-delight Reaction row: no
//      live room tally, no SignalR, no aggregate shown to players - it is a
//      plain per-device REST write, joinable against ServeEvent by TemplateId
//      to get a like-rate per serve (AC-06).
//  An engineer can later answer "how many times was space-llama served this
//  month, by mode?" and "what's its like-rate?" from the stored events - and
//  NOTHING about WHO played (AC-04).
//
//  WHY AN INTERFACE: the sink has two implementations behind this one contract:
//    - NoOpTelemetrySink        : the DEFAULT (local dev, no Azure). Logs and
//                                 drops the event, so the app runs with ZERO
//                                 setup (AC-05).
//    - TableStorageTelemetrySink: writes one entity to the provisioned Azure
//                                 Table when a storage connection is configured.
//  Program.cs picks one at startup based on whether a connection string exists.
//
//  NON-NEGOTIABLE POSTURE (the whole reason this file reads so carefully):
//    - Telemetry NEVER gates gameplay. Callers fire-and-forget; the write is
//      NEVER awaited on a round-start / vote-tap path, and a sink failure is
//      logged + swallowed, never surfaced to a player (AC-03, AC-05).
//    - NO PII, EVER. A ServeEvent / FeedbackEvent carries only anonymous facts -
//      template id, a UTC timestamp, mode, and (per event) a length class /
//      player count / family-safe flag, or a vote + opaque per-round vote id.
//      Both carry an OPAQUE instance/session GUID. There is deliberately NO
//      nickname, NO join code, NO connectionId, NO player session id from the
//      hub, and NO IP (AC-04, README section 6). The shape itself is the
//      guarantee: there is no field on either record that could carry a person.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Telemetry;

/// <summary>
/// One anonymous "a template was served" event (story-selection/04). This record
/// is the COMPLETE payload - every field it carries is listed here, and there is
/// intentionally NO field that could identify a person (AC-04, README section 6):
/// no nickname, no join code, no connectionId, no hub player-session id, no IP.
/// An engineer reading the stored events learns WHAT was served, WHEN, and how
/// often - never to WHOM by name.
/// </summary>
/// <param name="TemplateId">The served template's stable id (e.g. "space-llama"). The Table PartitionKey, so per-template reads are cheap (AC-06).</param>
/// <param name="TimestampUtc">When the template was served, in UTC.</param>
/// <param name="Mode">The play mode the template was served under ("classic-blind" for a group round, "solo" for a single-player round).</param>
/// <param name="LengthClass">The derived length class: "quick" or "full" (mirrors story-01's classification off the blank count).</param>
/// <param name="PlayerCount">How many players the round was served to (1 for a solo round). A COUNT only - never any player identity.</param>
/// <param name="FamilySafe">The family-safe toggle position the round was served under.</param>
/// <param name="InstanceId">An OPAQUE GUID: a per-room instance id for a group round, or a per-device session id for a solo round. NOT the join code and NOT tied to any person (AC-01, AC-04).</param>
public sealed record ServeEvent(
    string TemplateId,
    DateTimeOffset TimestampUtc,
    string Mode,
    string LengthClass,
    int PlayerCount,
    bool FamilySafe,
    string InstanceId);

/// <summary>
/// One anonymous "did you like this tale" curation vote (story-selection/05). This
/// record is the COMPLETE payload - every field it carries is listed here, and
/// there is intentionally NO field that could identify a person (AC-04): no
/// nickname, no join code, no connectionId, no IP. A vote is PER PLAYER, PER
/// ROUND, on the TEMPLATE - never room state, never a live tally (contrast the
/// reveal-delight Reaction row).
/// </summary>
/// <param name="TemplateId">The voted-on template's stable id. The Table PartitionKey, so per-template reads are cheap (AC-06).</param>
/// <param name="Vote">Either "up" or "down" - the player's thumbs choice.</param>
/// <param name="TimestampUtc">When the vote was recorded, in UTC.</param>
/// <param name="Mode">The play mode the tale was served under ("solo" or "classic-blind").</param>
/// <param name="SessionId">The SAME opaque per-device session GUID story-selection/04's ServeEvent uses (reused, never re-minted) - not an account, not tied to a person (AC-04).</param>
/// <param name="VoteId">An opaque, per-round GUID minted CLIENT-SIDE once per viewing of the reveal/recap screen. Doubles as the Table RowKey so a changed vote UPSERTS the same row - last write wins, never double-counted (AC-02, AC-06).</param>
public sealed record FeedbackEvent(
    string TemplateId,
    string Vote,
    DateTimeOffset TimestampUtc,
    string Mode,
    string SessionId,
    string VoteId);

/// <summary>
/// The sink that records a <see cref="ServeEvent"/> (story-selection/04) or a
/// <see cref="FeedbackEvent"/> (story-selection/05). Two methods, deliberately
/// small: record an event, best-effort. Implementations MUST treat a write
/// failure as a swallowed no-op (log server-side, never throw) so a caller can
/// fire-and-forget without ever wedging gameplay (AC-03/AC-05). Registered as a
/// singleton in Program.cs - NoOp locally (AC-05), Table Storage when a connection
/// string is configured.
/// </summary>
public interface ITelemetrySink
{
    /// <summary>
    /// Records one serve event, best-effort. This method MUST NOT throw for a sink
    /// failure (a down / slow / misconfigured backend) - it logs and swallows so a
    /// fire-and-forget caller never observes a fault (AC-03). Callers must NOT await
    /// this on a round-start path; it is an epilogue, never a gate.
    /// </summary>
    /// <param name="serveEvent">The anonymous event to record (no PII, AC-04).</param>
    /// <param name="cancellationToken">Cancellation for the write (best-effort; never fatal to the caller).</param>
    Task RecordServeAsync(ServeEvent serveEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records one thumbs up/down curation vote, best-effort (story-selection/05).
    /// This method MUST NOT throw for a sink failure - it logs and swallows so a
    /// fire-and-forget caller never observes a fault, and voting never gates
    /// gameplay (AC-05). Implementations MUST upsert on <see cref="FeedbackEvent.VoteId"/>
    /// so a changed vote overwrites rather than double-counting (AC-02).
    /// </summary>
    /// <param name="feedbackEvent">The anonymous vote to record (no PII, AC-04).</param>
    /// <param name="cancellationToken">Cancellation for the write (best-effort; never fatal to the caller).</param>
    Task RecordFeedbackAsync(FeedbackEvent feedbackEvent, CancellationToken cancellationToken = default);
}
