// ----------------------------------------------------------------------------
//  Dtos - the WIRE CONTRACT the harness mirrors from the server's GameHub.
//
//  These records mirror (by field name + shape) the DTOs GameHub returns from its
//  invocable methods and the payloads it broadcasts to clients (see
//  api/src/Hubs/GameHub.cs). They are the client-side half of the same hand-kept
//  contract the web client's useGameHub.ts mirrors - there is no codegen, so if a
//  hub DTO changes, update the matching record here.
//
//  CASING: SignalR's default JSON protocol serializes to camelCase on the wire
//  (the web hook reads result.ok, payload.blankIndices, result.reconnectToken).
//  The harness configures the hub connection with PropertyNameCaseInsensitive =
//  true (see HubPlayer), so these PascalCase records bind to the camelCase wire
//  regardless. Method ARGUMENTS are sent positionally (not by name), so argument
//  casing never matters - only these RETURN / BROADCAST shapes do.
//
//  PARTIAL BY DESIGN: several records intentionally declare only the fields the
//  harness reads (e.g. RoomStateDto carries just the join code). System.Text.Json
//  ignores unmapped JSON properties, so a partial mirror is safe and keeps the
//  harness lean - it does not need the roster, the reveal words, or the progress
//  list to drive and measure a round.
//
//  Prose style: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.LoadTest;

/// <summary>Room state as returned by CreateRoom / JoinRoom. Partial: the harness only needs the join code (the roster is broadcast separately and unused here).</summary>
/// <param name="Code">The short human-friendly join code every joiner uses.</param>
public sealed record RoomStateDto(string Code);

/// <summary>Outcome of GameHub.CreateRoom (build/host-identity). Ok=false is an expected validation failure with a friendly Error, never an exception.</summary>
public sealed record CreateRoomResultDto(bool Ok, RoomStateDto? Room, string? Error, string? ReconnectToken);

/// <summary>Outcome of GameHub.JoinRoom (session-engine/02). ReconnectToken is the joiner's own seat handle (used by churn's Rejoin).</summary>
public sealed record JoinResultDto(bool Ok, RoomStateDto? Room, string? Error, string? ReconnectToken);

/// <summary>Outcome of GameHub.StartRound (group-play/01). The round detail arrives via the RoundStarted broadcast + per-connection YourBlanks, not this envelope.</summary>
public sealed record StartRoundResultDto(bool Ok, string? Error);

/// <summary>Outcome of GameHub.SubmitWord (group-play/03). Ok=false means the word was rejected (safety filter or not this player's blank); with benign words + correct indices it should always be Ok=true.</summary>
public sealed record SubmitWordResultDto(bool Ok, string? Error);

/// <summary>Outcome of GameHub.Rejoin (session-engine/08). Partial: churn only needs Ok + this seat's remaining blanks so the reconnected player can finish the round.</summary>
public sealed record RejoinResultDto(bool Ok, string? Error, YourBlanksDto? YourBlanks);

/// <summary>Broadcast "RoundStarted": the round the whole room just moved into. Full mirror (cheap) - the harness reports the template + mode fan-out.</summary>
public sealed record RoundStartedDto(string TemplateId, string Mode, int RoundNumber, string? CrownedNickname);

/// <summary>Per-connection "YourBlanks": THIS player's assigned blank indices (round-robin, blank k -&gt; player k % N). Empty when there were fewer blanks than players.</summary>
public sealed record YourBlanksDto(IReadOnlyList<int> BlankIndices);

/// <summary>Broadcast "CollectProgress" after each submission. Partial: the harness only counts the broadcast, so it reads the two counters and ignores the per-player list.</summary>
public sealed record CollectProgressDto(int DoneCount, int PlayerCount);

/// <summary>Broadcast "RevealReady": the signal the round completed (the last assigned blank landed). Partial: existence is the completion signal; the ordered words are not needed to measure load.</summary>
public sealed record RevealReadyDto(string TemplateId);

/// <summary>Broadcast "RoundAborted": a player left mid-collection so the round can no longer complete. The harness treats it as a non-completing end so a waiter never hangs.</summary>
public sealed record RoundAbortedDto(string Reason);

/// <summary>
/// Response of POST /api/ai/jumble (the opt-in --ai scenario). fellBack=true means
/// the gate DEGRADED (no AI configured, quota exhausted, breaker open, or too few
/// safe words) and the client would reshuffle for free - a HEALTHY, expected result
/// under load, not a harness error. fellBack=false with words is a real AI generation.
/// This is a plain-REST JSON body (not a SignalR payload); the harness deserializes
/// it with the web-standard camelCase names via case-insensitive binding.
/// </summary>
/// <param name="Words">The moderated AI words (empty when it fell back).</param>
/// <param name="RemainingQuota">The per-session Fresh Runes quota left (server-authoritative).</param>
/// <param name="FellBack">True when the gate degraded to the free reshuffle.</param>
public sealed record AiJumbleResponseDto(IReadOnlyList<string>? Words, int RemainingQuota, bool FellBack);
