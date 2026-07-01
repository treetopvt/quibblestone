// ----------------------------------------------------------------------------
//  ITelemetrySink + ServeEvent - the anonymous "template served" serve log
//  (story-selection/04, the anonymous serve log).
//
//  WHAT THIS IS: the ONE seam through which QuibbleStone records the smallest
//  honest, anonymous, fire-and-forget "a template was served" event - one on
//  each GROUP round start (the server, from GameHub) and one on each SOLO round
//  start (the client, via TelemetryController). An engineer can later answer
//  "how many times was space-llama served this month, by mode?" from the stored
//  events (AC-06) - and NOTHING about WHO played (AC-04).
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
//      NEVER awaited on a round-start path, and a sink failure is logged +
//      swallowed, never surfaced to a player (AC-03).
//    - NO PII, EVER. A ServeEvent carries only what is listed below - template
//      id, a UTC timestamp, mode, length class, a player COUNT, the family-safe
//      flag, and an OPAQUE instance/session GUID. There is deliberately NO
//      nickname, NO join code, NO connectionId, NO player session id from the
//      hub, and NO IP (AC-04, README section 6). The shape itself is the
//      guarantee: there is no field on this record that could carry a person.
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
/// The sink that records a <see cref="ServeEvent"/> (story-selection/04). One
/// method, deliberately: record an event, best-effort. Implementations MUST treat
/// a write failure as a swallowed no-op (log server-side, never throw) so a caller
/// can fire-and-forget without ever wedging gameplay (AC-03). Registered as a
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
}
