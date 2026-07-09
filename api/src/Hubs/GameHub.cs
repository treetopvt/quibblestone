// ----------------------------------------------------------------------------
//  GameHub - the single SignalR hub for QuibbleStone real-time play.
//
//  Real-time is first-class in QuibbleStone: lobby, presence, live session
//  state, and reveal broadcast all ride on SignalR (README section 4). This one
//  hub grows story by story on the SAME connection - never a second hub - so
//  every game feature (rooms, rosters, word collection, reveal) shares it.
//
//  What lives here today:
//    - CreateRoom  : session-engine/01 + build/host-identity. Mints an ephemeral
//                    in-memory room with a unique, human-friendly join code, adds
//                    the caller as the host (first player) WITH the display name +
//                    Guardian variant they picked on HostSetup (the free-text name
//                    is safety-filtered server-side, same gate as joiners), joins
//                    them to the room's SignalR group (so future roster broadcasts
//                    reach them), and returns a friendly result envelope to the
//                    caller (the created room's state on success, an inline error on
//                    a blocked/empty/too-long name).
//    - JoinRoom    : session-engine/02. Joins an existing room by code with a
//                    safety-checked display name (no PII), enforces in-room name
//                    uniqueness, and broadcasts the updated roster to the room
//                    group as "RosterChanged" so everyone sees the newcomer live.
//    - StartRound  : group-play/01 + /02. HOST-ONLY (server-enforced) round
//                    start: auto-selects a template from the minimal server
//                    catalog, filtered by the family-safe toggle the host sends
//                    (ALWAYS FIRST) then (story-selection/02) by the host's
//                    story-length choice - one more parameter on this SAME
//                    invoke, never a new hub method - sets the room's round
//                    state, broadcasts "RoundStarted" to the room group so ALL
//                    players move into word collection together, then
//                    (group-play/02) computes the ROUND-ROBIN blank
//                    distribution and sends EACH player "YourBlanks" - only its own
//                    blank indices, blind (prompt only), never another player's.
//    - BackToLobby : group-play/04. HOST-ONLY (server-enforced) return to the
//                    lobby: clears the room's round WITHOUT touching the roster or
//                    code (the room stays live, roster preserved) and broadcasts a
//                    bare "BackToLobby" to the room group so every player drops the
//                    round/reveal locally and lands back on the Lobby (AC-05). The
//                    replay counterpart is just StartRound again (same room, no
//                    re-join) - the Room increments the round number for the badge.
//    - PassHost    : replay-remix/03. HOST-ONLY (server-enforced, the SAME
//                    IsHost check StartRound/BackToLobby use) handoff of the host
//                    role to another roster player, BETWEEN ROUNDS only (rejected
//                    while the room's round phase is "prompting", AC-05). Flips
//                    IsHost on the room's player records (Room.PassHost, under the
//                    room lock) and broadcasts the EXISTING "RosterChanged" event -
//                    no new broadcast event type, since IsHost already rides on
//                    every PlayerDto - so the crown moves live for everyone.
//
//  Room state lives in the RoomRegistry singleton (injected below), NOT in this
//  hub instance - SignalR builds a fresh hub per invocation, so per-hub fields
//  would not persist. The registry is the process-wide, in-memory (no DB) home
//  for rooms (CLAUDE.md section 10 - a toy, not a system of record).
//
//  The DTOs returned to the client (RoomStateDto / PlayerDto) are the WIRE
//  CONTRACT the web client's useGameHub types mirror. They are deliberately
//  minimal and anonymous (nickname + variant + host flag, no PII - README
//  section 6). Later stories (02 join, 05 avatar, 03 roster) extend this same
//  shape and add joinRoom / roster-broadcast methods here.
// ----------------------------------------------------------------------------

using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using QuibbleStone.Api.Accounts;
using QuibbleStone.Api.Content;
using QuibbleStone.Api.Entitlements;
using QuibbleStone.Api.Rooms;
using QuibbleStone.Api.Safety;
using QuibbleStone.Api.Telemetry;

namespace QuibbleStone.Api.Hubs;

/// <summary>
/// One player as seen on the wire. Anonymous by design (no PII): an in-session
/// nickname, a Guardian variant, and whether they are the host. The
/// connectionId is intentionally NOT exposed to clients (it is a server-side
/// handle used for leave-detection in story 03).
/// </summary>
/// <param name="Nickname">In-session display name (the host now picks one on HostSetup before the room is minted).</param>
/// <param name="Variant">Guardian avatar variant (the host picks one on HostSetup; "teal" is the default selection).</param>
/// <param name="IsHost">True for the room creator.</param>
/// <param name="Connected">session-engine/07: false while this seat is being held through a disconnect grace window, true otherwise. Additive to the wire contract now; web story 10 renders the "reconnecting" tile from it (nothing reads it yet). The reconnect TOKEN is deliberately NOT here - it is caller-only (AC-06).</param>
public sealed record PlayerDto(string Nickname, string Variant, bool IsHost, bool Connected);

/// <summary>
/// The state of a room as returned to the caller of <see cref="GameHub.CreateRoom"/>
/// (and, in later stories, broadcast on roster changes): the join code plus the
/// current roster. This is the shape story 02's join method will also return.
/// </summary>
/// <param name="Code">The short, human-friendly join code (4 chars, unambiguous alphabet).</param>
/// <param name="Players">The current roster, host first.</param>
public sealed record RoomStateDto(string Code, IReadOnlyList<PlayerDto> Players);

/// <summary>
/// The outcome of <see cref="GameHub.JoinRoom"/> (session-engine/02). This is a
/// friendly result envelope, NOT an exception channel: every EXPECTED failure
/// (unknown/expired code, an empty or too-long name, a name the safety filter
/// rejects, a name already taken in the room) comes back as Ok=false with a
/// kid-readable Error the client shows inline so the player can simply try again
/// (AC-03, AC-04, AC-06). On success, Ok=true and Room carries the updated roster
/// (the same shape createRoom returns and RosterChanged broadcasts).
/// </summary>
/// <param name="Ok">True if the caller joined the room; false for an expected validation failure.</param>
/// <param name="Room">The room's state (code + roster) on success; null on failure.</param>
/// <param name="Error">A friendly, kid-readable message on failure; null on success.</param>
/// <param name="ReconnectToken">session-engine/07 (AC-06): the caller's own opaque, server-minted reconnect handle on success (null on failure). Returned ONLY here, in the joiner's own envelope - NEVER on <see cref="RoomStateDto"/>/<see cref="PlayerDto"/> (the whole-room roster), so no other player can see or spend it. Story 08's Rejoin is the only consumer.</param>
public sealed record JoinResultDto(bool Ok, RoomStateDto? Room, string? Error, string? ReconnectToken = null);

/// <summary>
/// The outcome of <see cref="GameHub.CreateRoom"/> (build/host-identity). Same
/// shape and semantics as <see cref="JoinResultDto"/>: a friendly result envelope,
/// NOT an exception channel. The host now supplies a free-text display name +
/// Guardian variant (picked on the HostSetup screen), so CreateRoom validates them
/// EXACTLY like <see cref="GameHub.JoinRoom"/> validates a joiner name - an empty
/// name, an over-14-char name, or a name the content-safety filter rejects (child
/// safety, non-negotiable) all come back as Ok=false with a kid-readable Error the
/// client shows inline so the host can simply fix it and try again. On success
/// Ok=true and Room carries the minted room's state (code + roster with the host).
/// </summary>
/// <param name="Ok">True if the room was created; false for an expected validation failure.</param>
/// <param name="Room">The minted room's state (code + roster) on success; null on failure.</param>
/// <param name="Error">A friendly, kid-readable message on failure; null on success.</param>
/// <param name="ReconnectToken">session-engine/07 (AC-06): the host's own opaque, server-minted reconnect handle on success (null on failure). Returned ONLY here, in the host's own envelope - NEVER on <see cref="RoomStateDto"/>/<see cref="PlayerDto"/> (the whole-room roster), so no other player can see or spend it. Story 08's Rejoin is the only consumer.</param>
public sealed record CreateRoomResultDto(bool Ok, RoomStateDto? Room, string? Error, string? ReconnectToken = null);

/// <summary>
/// The outcome of <see cref="GameHub.Rejoin"/> (session-engine/08): the rehydration
/// envelope a device gets when it reclaims its held seat under a new connection. Like
/// every other hub result here it is a friendly envelope, NOT an exception channel -
/// an unknown / already-evicted / wrong-room token comes back as Ok=false with a
/// kid-readable Error, never a throw (AC-05).
///
/// On success (Ok=true) it carries EXACTLY what the resuming client needs to pick up
/// where it left off, and NOTHING the room does not already expose elsewhere (AC-07):
///   - <see cref="Room"/>: the current roster (the same <see cref="RoomStateDto"/> shape
///     createRoom / joinRoom / RosterChanged carry) - feeds the client's setRoom (AC-02).
///   - <see cref="IsHost"/>: whether this seat is the host (AC-02).
///   - <see cref="Phase"/>: the round's phase - "lobby" | "prompting" | "reveal" (AC-02).
///   - <see cref="Round"/>: the round's public metadata (template id, mode, round number,
///     crown), the SAME <see cref="RoundStartedDto"/> shape the RoundStarted broadcast
///     carried - present for a live round (prompting or reveal), null in the lobby.
///   - <see cref="YourBlanks"/>: for a "prompting" round, THIS seat's own remaining
///     (not-yet-submitted) blank indices only - never another player's - reusing the
///     index-only, no-PII <see cref="YourBlanksDto"/> shape (AC-03). Null otherwise.
///   - <see cref="Progress"/>: for a "prompting" round, the room-wide "N of M done"
///     collection progress (the SAME <see cref="CollectProgressDto"/> the CollectProgress
///     broadcast carries), so the resumed Waiting/collection screen is current (AC-03).
///     Null otherwise.
///   - <see cref="Reveal"/>: for a "reveal" round, the shared ordered reveal words (the
///     SAME <see cref="RevealReadyDto"/> the RevealReady broadcast carries) so the resuming
///     client renders the exact reveal everyone else sees (AC-04). Null otherwise.
///
/// This is the wire contract story 09's useGameHub.ts consumes (there is no codegen step
/// - keep the two hand-in-sync); the setters it feeds (setRoom / setIsHost / setRound /
/// setAssignedBlankIndices / setCollectProgress / setReveal) map 1:1 onto these fields.
/// The reconnect TOKEN is deliberately NOT echoed back to anyone (AC-07): the caller
/// already holds it, and it must never travel on the wire to be readable.
/// </summary>
/// <param name="Ok">True if the seat was reclaimed; false for an expected failure (AC-05).</param>
/// <param name="Error">A friendly, kid-readable message on failure; null on success.</param>
/// <param name="Room">The current roster on success; null on failure.</param>
/// <param name="IsHost">True when the reclaimed seat is the room's host (false on failure).</param>
/// <param name="Phase">The round phase on success ("lobby" | "prompting" | "reveal"); null on failure.</param>
/// <param name="Round">The live round's public metadata on success (null in the lobby or on failure).</param>
/// <param name="YourBlanks">This seat's remaining blank indices for a "prompting" round; null otherwise.</param>
/// <param name="Progress">The room-wide collection progress for a "prompting" round; null otherwise.</param>
/// <param name="Reveal">The shared reveal payload for a "reveal" round; null otherwise.</param>
public sealed record RejoinResultDto(
    bool Ok,
    string? Error,
    RoomStateDto? Room,
    bool IsHost,
    string? Phase,
    RoundStartedDto? Round,
    YourBlanksDto? YourBlanks,
    CollectProgressDto? Progress,
    RevealReadyDto? Reveal);

/// <summary>
/// The outcome of <see cref="GameHub.StartRound"/> (group-play/01). Like
/// <see cref="JoinResultDto"/> this is a friendly result envelope, NOT an
/// exception channel: every EXPECTED failure (unknown/expired code, the caller is
/// not the host, too few players, or - defensively - no template survives the
/// family-safe filter) comes back as Ok=false with a kid-readable Error the host
/// can show inline. On success Ok=true and Error is null; the actual round detail
/// reaches EVERY player (host included) via the "RoundStarted" broadcast, not
/// through this envelope, so all clients transition together (AC-01, AC-02).
/// </summary>
/// <param name="Ok">True if the round started; false for an expected rejection.</param>
/// <param name="Error">A friendly, kid-readable message on failure; null on success.</param>
public sealed record StartRoundResultDto(bool Ok, string? Error);

/// <summary>
/// Broadcast to the whole room group as "RoundStarted" when the host starts a
/// round (group-play/01). This is the signal that moves every player from the
/// lobby into word collection in near-real-time (AC-01, AC-02). It carries only
/// the template's ID (each client resolves the full prose/body from its bundled
/// seedLibrary BY ID - the server never ships template content), the mode, and
/// the round number. group-play/02 adds per-player blank assignments via a
/// SEPARATE, per-connection "YourBlanks" message (see <see cref="YourBlanksDto"/>)
/// so each client learns only its own prompts.
/// </summary>
/// <param name="TemplateId">The selected template's id; the client resolves full content from seedLibrary.</param>
/// <param name="Mode">The play mode the HOST chose (group-play/05): one of the offered ids ("classic-blind", "word-bank", "progressive-reveal"). The client resolves it through the shared mode registry to render the right surfaces. Populated for real now - it was pinned to "classic-blind" through Slice 1.</param>
/// <param name="RoundNumber">1-based round number; group-play/04 increments it on replay (2, 3, ...), and it drives the Round Complete "ROUND N CARVED" badge.</param>
/// <param name="CrownedNickname">reveal-delight/03 (AC-04): the nickname wearing the Golden Guardian crown for THIS round (the previous round's funniest-word winner), or null when no crown applies. Server-tracked round state; the crown clears on the next round unless re-awarded.</param>
public sealed record RoundStartedDto(string TemplateId, string Mode, int RoundNumber, string? CrownedNickname);

/// <summary>
/// Sent to ONE player as "YourBlanks" right after the round starts
/// (group-play/02). This is the per-connection counterpart to the room-wide
/// "RoundStarted" broadcast: it tells a single player WHICH blanks it owes,
/// by blank INDEX into the template's ordered blanks (the client resolves each
/// index to its prompt via getBlanks(template)[index], Classic blind - prompt
/// only, no story context, AC-02).
///
/// A player is NEVER told another player's blanks: the hub sends this only to
/// that player's own connection (Clients.Client(connectionId)), and the payload
/// carries NO connection id, no nickname, no other player - just this player's
/// blank indices (no PII, README section 6). This is the wire contract
/// useGameHub mirrors (the TS type YourBlanks) - keep them in sync BY HAND.
/// </summary>
/// <param name="BlankIndices">The blank indices (into the round template's ordered blanks) THIS player owes; empty when fewer blanks than players left this player none.</param>
public sealed record YourBlanksDto(IReadOnlyList<int> BlankIndices);

/// <summary>
/// The outcome of <see cref="GameHub.SubmitWord"/> (group-play/03). Like the other
/// result envelopes here (<see cref="JoinResultDto"/>, <see cref="StartRoundResultDto"/>)
/// this is a friendly result envelope, NOT an exception channel: every EXPECTED
/// failure (no round / not in the prompting phase, the word failed the safety
/// filter, or the blank is not this connection's) comes back as Ok=false with a
/// kid-readable Error the client shows inline so the player can try again (AC-01).
/// On success Ok=true and Error is null; the progress and (when the round finishes)
/// the reveal reach every player via the "CollectProgress" / "RevealReady"
/// broadcasts, not through this envelope. It maps 1:1 to FillBlank's onSubmitWord
/// contract on the web side (Ok -> accepted, Error -> message).
/// </summary>
/// <param name="Ok">True if the word was recorded; false for an expected rejection.</param>
/// <param name="Error">A friendly, kid-readable message on failure; null on success.</param>
public sealed record SubmitWordResultDto(bool Ok, string? Error);

/// <summary>
/// One player's collection progress on the wire (group-play/03), mirroring
/// <see cref="Rooms.PlayerProgress"/>: an anonymous nickname + Guardian variant +
/// whether they have submitted ALL their assigned blanks. Carries NO submitted
/// words (AC-01: words are never shown to other players before the reveal) and no
/// connectionId (no PII, README section 6).
/// </summary>
/// <param name="Nickname">The player's in-session nickname (already filtered on join).</param>
/// <param name="Variant">The player's Guardian variant.</param>
/// <param name="Done">True once this player has submitted every blank it was assigned.</param>
public sealed record PlayerProgressDto(string Nickname, string Variant, bool Done);

/// <summary>
/// Broadcast to the whole room group as "CollectProgress" after each submission
/// (group-play/03): the per-player done/writing list plus the "[N] of [M]
/// quibblers done" counts the Waiting card shows. It carries NO submitted words
/// (AC-01) - only who is done and who is still writing - so a client can render
/// the Waiting progress row (done at full opacity + teal check, still-writing
/// dimmed + pulsing badge) without ever seeing another player's word.
/// </summary>
/// <param name="DoneCount">How many players have submitted all their assigned blanks.</param>
/// <param name="PlayerCount">The total number of players in the round.</param>
/// <param name="Players">Per-player progress, in roster/assignment order (host first).</param>
public sealed record CollectProgressDto(
    int DoneCount,
    int PlayerCount,
    IReadOnlyList<PlayerProgressDto> Players);

/// <summary>
/// One blank position on the wire for the reveal (group-play/03), mirroring
/// <see cref="Rooms.RevealWord"/>: the submitted word plus its owning player
/// (nickname + variant), in blank order. A blank with no submission (a player who
/// left) is an EMPTY word attributed to no one, so the client's assemble() keeps
/// alignment. Every word here already passed the safety filter (AC-06); no PII
/// beyond the already-filtered nickname + variant.
/// </summary>
/// <param name="Word">The submitted word for this blank position; empty for an unfilled blank.</param>
/// <param name="Nickname">The owning player's nickname, or empty for an unfilled blank.</param>
/// <param name="Variant">The owning player's Guardian variant, or empty for an unfilled blank.</param>
public sealed record RevealWordDto(string Word, string Nickname, string Variant);

/// <summary>
/// Broadcast to the whole room group as "RevealReady" the moment the LAST assigned
/// blank is submitted (group-play/03, AC-05): the template id plus the ordered
/// reveal words (blank order). This is the signal that moves EVERY player (done or
/// still on the Waiting screen) to the shared Reveal in near-real-time, no refresh.
/// The server does NOT assemble the story - it ships the id + the ordered words and
/// each client resolves the template from its bundled seedLibrary and assembles
/// locally via the web engine (the ONE place assembly lives, AC-05).
/// </summary>
/// <param name="TemplateId">The round's template id; the client resolves full content from seedLibrary.</param>
/// <param name="Words">The ordered reveal words (one per blank position, blank order).</param>
public sealed record RevealReadyDto(string TemplateId, IReadOnlyList<RevealWordDto> Words);

/// <summary>
/// Broadcast as "RoundAborted" when a player leaves (a dropped connection or a
/// deliberate leave) DURING collection, so the round can no longer complete. Carries
/// a friendly reason the remaining players see on the lobby they are dropped back to.
/// A small group-play recovery beyond Slice-1's parked reconnect handling.
/// </summary>
/// <param name="Reason">A friendly, kid-readable explanation shown on the lobby.</param>
public sealed record RoundAbortedDto(string Reason);

/// <summary>
/// Broadcast to the whole room group as "ReactionCountsChanged" whenever any
/// player reacts on the reveal (reveal-delight/01, AC-04): the FULL per-type
/// tally for the current reveal, so every client renders the same count in
/// near-real-time. Server-authoritative (no client double-counts). The UX
/// de-clutter (reactions v2) narrowed the set from four (laugh/heart/wow/star) to
/// THREE - Love / Wow / Didn't like - and made a reaction ONE-PER-USER (a player
/// holds at most one; see <see cref="Room.SetReaction"/>). The three fields
/// serialize to camelCase (love/wow/nope) - the EXACT shape the web's
/// ReactionCounts (Record&lt;ReactionType, number&gt;) mirrors, so the hook hands the
/// payload straight to the ReactionRow. A reaction carries no text and no identity
/// (AC-06, no PII) - only these counts.
/// </summary>
/// <param name="Love">The Love ("thumbs-up") tally.</param>
/// <param name="Wow">The Wow ("face-surprise") tally.</param>
/// <param name="Nope">The "Didn't like" ("thumbs-down") tally.</param>
public sealed record ReactionCountsDto(int Love, int Wow, int Nope);

/// <summary>
/// Broadcast to the whole room group as "GoldenGuardianVoteCast" whenever a player
/// casts (or moves) a funniest-word vote (reveal-delight/03, AC-02): just the live
/// "N of M voted" figures the Reveal shows as a low-key status. Deliberately carries
/// NO per-word tallies (those stay hidden mid-vote, AC-02) and no identity (AC-07,
/// no PII) - only the two counts.
/// </summary>
/// <param name="VotedCount">How many present players have voted (the "N").</param>
/// <param name="TotalVoters">How many players are present and can vote (the "M").</param>
public sealed record GoldenGuardianVoteCastDto(int VotedCount, int TotalVoters);

/// <summary>
/// Broadcast to the whole room group as "GoldenGuardianResolved" the moment the
/// funniest-word vote resolves (every present player voted, or the host closed it
/// early - reveal-delight/03, AC-03). Carries the winning blank token (the coral
/// word the Reveal rings in gold) and the winning contributor's nickname (the crown's
/// next-round wearer, AC-04). Both are null when the vote resolved with zero votes (a
/// friendly non-event - no winner, no crown, never a loser callout). The blank token
/// is an already-vetted, already-displayed word's position - no new text, no PII
/// (AC-07).
/// </summary>
/// <param name="WinningBlankId">The winning blank token, or null when there was no winner.</param>
/// <param name="PlayerSessionId">The winning contributor's nickname (the anonymous in-session id the web attributes words by), or null when there was no winner.</param>
public sealed record GoldenGuardianResolvedDto(string? WinningBlankId, string? PlayerSessionId);

public sealed class GameHub : Hub
{
    // The largest a display name may be (AC-03). Kept in sync with the web
    // client's "n/14" counter (web/src/pages/Join.tsx). Names are trimmed first.
    private const int MaxDisplayNameLength = 14;

    // session-engine/13 (AC-03/W1): the single source of truth for the "unknown
    // or expired room code" message every code-lookup hub method returns on a
    // miss. Was duplicated six times (JoinRoom, StartRound, BackToLobby, PassHost,
    // SubmitWord, RemixWord) with no shared constant - collapsed here so the web
    // client (useGameHub.ts's isRoomNotFoundError) can reliably recognize it and
    // reset local room state on a room the server no longer has.
    private const string RoomNotFoundMessage = "We couldn't find a game with that code - double-check and try again.";

    // session-engine/05: the only six Guardian variants the client can offer
    // (web/src/components/Guardian.tsx GuardianVariant). A malformed or
    // malicious client could send any string as `variant`, so this is the
    // server-side source of truth - never trust the wire value directly.
    private static readonly HashSet<string> KnownVariants = new(StringComparer.OrdinalIgnoreCase)
    {
        "purple", "gold", "coral", "teal", "sand", "plum",
    };

    // group-play/05: the mode a group round runs in is now the HOST's choice,
    // resolved + validated server-side against GameModeCatalog.Offered (Classic
    // Blind, Word Bank, Progressive Reveal) and broadcast on RoundStartedDto.Mode.
    // It was pinned to "classic-blind" through Slice 1 (group-play/01) - see
    // StartRound step 3b. The offered-mode list lives in GameModeCatalog, not here,
    // so an unoffered mode (e.g. progressive-story) can never leak in.

    // story-selection/02: the DEFENSIVE fallback for a malformed lengthPref -
    // null, empty, or any value the client sends that is not one of the three
    // known preferences. "any" means the length stage does not filter, so a
    // malformed client can only ever fall back to "no length filtering", never
    // to something that widens or bypasses the family-safe gate. Mirrors how
    // NormalizeVariant guards an unknown variant string.
    private const string DefaultLengthPreference = LengthContentSelector.Any;

    // story-selection/02: the length preferences a client may legitimately send
    // (mirrors the web's LengthPreference union). Anything else is treated as
    // DefaultLengthPreference by NormalizeLengthPreference below.
    private static readonly HashSet<string> KnownLengthPreferences = new(StringComparer.OrdinalIgnoreCase)
    {
        LengthContentSelector.Quick, LengthContentSelector.Full, LengthContentSelector.Any,
    };

    // reveal-delight/01 (AC-04) + reactions v2: the ONLY three reaction types a
    // client may send - Love ("love"), Wow ("wow"), Didn't like ("nope") - mirroring
    // the web's ReactionType union. A crafted client could send any string, so this
    // is the server-side source of truth - React ignores anything else, exactly like
    // NormalizeVariant guards an unknown variant. The payload is a TYPE ENUM only, so
    // this set is the entire contract (no free text - AC-06).
    private static readonly HashSet<string> KnownReactions = new(StringComparer.OrdinalIgnoreCase)
    {
        "love", "wow", "nope",
    };

    private readonly RoomRegistry _rooms;
    private readonly IContentSafetyFilter _safety;
    private readonly TemplateCatalog _catalog;
    private readonly FamilySafeContentSelector _familySafe;
    private readonly LengthContentSelector _length;
    private readonly FreshnessContentSelector _freshness;
    private readonly ITelemetrySink _telemetry;
    private readonly TelemetryClient _appInsights;
    private readonly IEntitlementService _entitlements;
    private readonly SeatGraceService _grace;
    private readonly PurchaserCredentialService _purchaserCredentials;
    private readonly IConnectionEntitlementStore _connectionEntitlements;
    private readonly ILogger<GameHub> _logger;

    public GameHub(
        RoomRegistry rooms,
        IContentSafetyFilter safety,
        TemplateCatalog catalog,
        FamilySafeContentSelector familySafe,
        LengthContentSelector length,
        FreshnessContentSelector freshness,
        ITelemetrySink telemetry,
        TelemetryClient appInsights,
        IEntitlementService entitlements,
        SeatGraceService grace,
        PurchaserCredentialService purchaserCredentials,
        IConnectionEntitlementStore connectionEntitlements,
        ILogger<GameHub> logger)
    {
        _rooms = rooms;
        _safety = safety;
        _catalog = catalog;
        _familySafe = familySafe;
        _length = length;
        // story-selection/03: the freshness/recycle stage, LAST before the
        // random pick (see StartRound). Reads/writes the room's own
        // PlayedTemplateIds history - this selector itself stays pure/stateless.
        _freshness = freshness;
        // story-selection/04: the anonymous serve-log sink. Fired fire-and-forget
        // at the END of a group round start (never awaited on the round-start path,
        // AC-03). NoOp locally, Table Storage in a configured environment (AC-05).
        _telemetry = telemetry;
        // platform-devops/04 (AC-03): the OPERATIONAL App Insights client, used
        // ONLY to make abnormal disconnects observable in OnDisconnectedAsync
        // below (hub method exceptions are tracked by HubTelemetryFilter). This is
        // a DIFFERENT pipeline from _telemetry above (the content serve log): this
        // one is operational health. No-ops cleanly with no connection string
        // (AC-05); it is always registered so DI stays simple.
        _appInsights = appInsights;
        // ai-cost-gate/02 (#121): the thin, #70-shaped, DEFAULT-UNLOCKED entitlement
        // seam. CreateRoom evaluates it EXACTLY ONCE per session and captures the
        // result on the Room; nothing re-evaluates it per AI call (AC-01). In alpha
        // every ai.* capability is unlocked, so this changes zero behavior (AC-03).
        _entitlements = entitlements;
        // session-engine/07 (hold the seat): the singleton scheduler that runs the
        // one-shot delayed eviction when a dropped seat's grace window expires. It is
        // the ONLY scheduled timer in the codebase (every other lifecycle read is
        // lazy-on-access) - justified because other seated players actively wait on a
        // dropped seat's blanks, so eviction must be pushed even if nobody calls the
        // hub in the meantime (see SeatGraceService's header + feature.md Decisions).
        _grace = grace;
        // accounts-identity/06 (ADR 0002 Decision F, #210): the ALREADY-REGISTERED
        // purchaser-credential resolver (accounts-identity/03) - the SAME resolver
        // billing-entitlements/05's restore endpoint uses, never a second auth check.
        // OnConnectedAsync calls ResolvePurchaserEmail on the connection's access token
        // and discards the identity the moment EvaluateForSession consumes it.
        _purchaserCredentials = purchaserCredentials;
        // accounts-identity/06: the per-connection resolved-CAPABILITY bridge from
        // OnConnectedAsync to a later CreateRoom on the SAME connection (a fresh hub per
        // invocation cannot bridge them with an instance field - see the store's header).
        // Holds ONLY a SessionEntitlements + a reserved bool, never an identity (AC-04).
        _connectionEntitlements = connectionEntitlements;
        _logger = logger;
    }

    /// <summary>
    /// accounts-identity/06 (ADR 0002 Decision F, finally wired - #210): GameHub's
    /// FIRST <see cref="OnConnectedAsync"/> override (it had none before - only
    /// <see cref="OnDisconnectedAsync"/>). This is the ONE place a purchaser's session
    /// credential is turned into a capability set for the connection.
    ///
    /// A signed-in purchaser's web client supplies its EXISTING purchaser credential
    /// (accounts-identity/03's PurchaserSession) via SignalR's standard
    /// accessTokenFactory, which the transport carries on the negotiate/connect query
    /// string as <c>access_token</c> (AC-01). Here we:
    ///   1. Read that token (absent for every anonymous player and every signed-out
    ///      host - the overwhelming common case, which stores nothing and leaves
    ///      free play byte-for-byte unchanged, AC-05).
    ///   2. Resolve it to a purchaser email via the ALREADY-REGISTERED
    ///      <see cref="PurchaserCredentialService.ResolvePurchaserEmail"/> - the SAME
    ///      resolver billing-entitlements/05 uses, never a second credential check
    ///      (AC-02). A malformed / expired / tampered token resolves to null rather
    ///      than throwing, so a stale token can NEVER break a family's ability to play
    ///      (AC-06) - it simply falls through to the default-unlocked baseline.
    ///   3. IMMEDIATELY evaluate the session's capabilities from that identity
    ///      (<see cref="IEntitlementService.EvaluateForSession"/>) and store ONLY the
    ///      resulting <see cref="SessionEntitlements"/> (plus the reserved, always-false
    ///      AdultUnlocked bool story 09 populates) in the per-connection singleton,
    ///      keyed by <see cref="HubCallerContext.ConnectionId"/>.
    ///
    /// THE INVARIANT (AC-04, non-negotiable): the resolved identity string lives ONLY
    /// in the <c>purchaserIdentity</c> local for the duration of that single
    /// EvaluateForSession call. It is never stored on the connection, the room, a
    /// player, the singleton, a DTO, a broadcast, or a log line. CreateRoom later reads
    /// back the CAPABILITY set (never an identity) from the singleton.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        // SignalR's accessTokenFactory value rides the connect query string as
        // "access_token" (the transport's carrier when it cannot use an Authorization
        // header) - AC-01 / the story's Technical Notes. Absent for every anonymous
        // connection, which stores nothing (AC-05).
        var token = Context.GetHttpContext()?.Request.Query["access_token"].ToString();
        if (!string.IsNullOrEmpty(token))
        {
            // Resolve-and-discard (AC-02): the identity string exists only as this local
            // for the single EvaluateForSession call below. A bad/expired/tampered token
            // resolves to null (never throws, AC-06), which evaluates to the same
            // default-unlocked baseline an anonymous connection gets.
            //
            // Defense in depth: the token is attacker-controllable query input.
            // ResolvePurchaserEmail already swallows Unprotect's CryptographicException,
            // but the base64url decode AHEAD of it can throw FormatException on
            // non-base64url input - which would otherwise escape here and abort the
            // connection. AC-06 is absolute (a corrupt token must NEVER break a
            // family's play), so treat ANY resolve failure as "not signed in" and fall
            // through to the default-unlocked baseline, exactly as an empty token does.
            string? purchaserIdentity;
            try
            {
                purchaserIdentity = _purchaserCredentials.ResolvePurchaserEmail(token);
            }
            catch
            {
                // A bad token is an EXPECTED condition here (attacker-controllable query
                // input), not an error, so this stays a cheap, message-only Trace line -
                // no exception object (an attacker cannot inflate log volume / allocation)
                // and the lowest level (off by default). The catch stays BROAD rather than
                // enumerating the resolver's internal exception types (base64url
                // FormatException + crypto): AC-06 is absolute, so an unforeseen throw must
                // still fall through to anonymous, never abort the connection.
                _logger.LogTrace("Ignoring an unresolvable purchaser access token on connect (treating as anonymous).");
                purchaserIdentity = null;
            }

            var capabilities = await _entitlements.EvaluateForSession(
                purchaserIdentity, Context.ConnectionAborted);

            // Store ONLY the capability set + the reserved (always-false) AdultUnlocked
            // bool - never the identity (AC-04 / AC-08). purchaserIdentity goes out of
            // scope here and is never referenced again.
            _connectionEntitlements.Set(
                Context.ConnectionId,
                new ResolvedConnectionIdentity(capabilities, AdultUnlocked: false));
        }

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// session-engine/01 + build/host-identity: create a room and become its host,
    /// now WITH a chosen display name + Guardian variant.
    ///
    /// The host used to be seated with an EMPTY nickname + the default "teal"
    /// variant (there was no host name step - only joiners named themselves), so the
    /// host showed blank in the lobby, the reveal attribution, and the Round Complete
    /// recap. build/host-identity closes that gap: the host picks a name + Guardian on
    /// the HostSetup screen BEFORE the room is minted, and this method validates that
    /// free-text name the SAME way (same fixed order) <see cref="JoinRoom"/> validates
    /// a joiner name, returning a friendly <see cref="CreateRoomResultDto"/> for every
    /// EXPECTED failure rather than throwing:
    ///
    ///   1. Empty (after trim) name -> friendly "pick a display name" error (AC-03).
    ///   2. Over-14-char name -> the same friendly too-long message JoinRoom uses.
    ///   3. Content-safety filter rejects the name -> the filter's message. The host
    ///      name is free text, so it MUST pass the server filter BEFORE it is stored
    ///      or shown, exactly like a joiner name (child safety, README section 6).
    ///
    /// On success it normalizes the variant (unknown/empty -> "teal"), mints an
    /// ephemeral room with a unique, human-friendly join code (AC-02, AC-03) carrying
    /// the host with that vetted name + variant, subscribes the caller's connection to
    /// the room's SignalR group (named by the code) so future roster/round broadcasts
    /// reach them, and returns the created room's state (code + roster) so the web
    /// client can land the host in the lobby (AC-01, AC-04).
    /// </summary>
    /// <param name="displayName">The host's free-text in-session name (max 14 chars, safety-checked - same gate as joiners).</param>
    /// <param name="variant">The chosen Guardian variant; normalized server-side to one of the six known values, defaulting to "teal" when null/empty/unknown.</param>
    public async Task<CreateRoomResultDto> CreateRoom(string displayName, string variant)
    {
        // 1. Basic shape of the display name (AC-03), mirroring JoinRoom's fixed
        //    order. Trim first so " " is empty and trailing spaces do not count.
        var name = (displayName ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            return new CreateRoomResultDto(false, null, "Pick a display name so your crew knows who you are.");
        }
        if (name.Length > MaxDisplayNameLength)
        {
            return new CreateRoomResultDto(false, null, $"That name is a bit long - keep it to {MaxDisplayNameLength} characters.");
        }

        // 2. Child safety (README section 6, AC-03): vet the free-text host name
        //    server-side BEFORE it is stored or shown, exactly like a joiner name.
        //    Never seat an unfiltered name as the host. The verdict carries a
        //    friendly retry message the client shows inline.
        var verdict = await _safety.CheckAsync(name, Context.ConnectionAborted);
        if (!verdict.IsAllowed)
        {
            return new CreateRoomResultDto(false, null, verdict.Message);
        }

        // 3. Constrain the variant to the known set (null/empty/unknown -> "teal"),
        //    so a malformed client can never inject an arbitrary variant string.
        var chosenVariant = NormalizeVariant(variant);

        // 4. Mint the room with the host carrying the vetted name + variant.
        var room = _rooms.CreateRoom(Context.ConnectionId, name, chosenVariant);

        // 5. Capture the session's entitlements on the room for its lifetime
        //    (ai-cost-gate/02 AC-01: captured once at session-creation, never
        //    re-evaluated per tap/round/AI call). accounts-identity/06 (ADR 0002
        //    Decision F, #210) finally wires a REAL identity in: instead of the old
        //    hardcoded EvaluateForSession(purchaserIdentity: null), this now READS the
        //    capability set OnConnectedAsync ALREADY resolved for this connection (a
        //    signed-in purchaser's family-plan grant is applied there, once, and its
        //    identity discarded - so a family plan can unlock a session for the first
        //    time). CreateRoom itself makes NO EvaluateForSession call on this hit
        //    path (AC-03) - the evaluation happened exactly once, in OnConnectedAsync.
        //
        //    A MISS (no purchaser token supplied - every anonymous host - or a
        //    connection that never went through the resolve path, e.g. a direct unit
        //    test) falls back to the default-unlocked baseline via EvaluateForSession
        //    with a null identity, byte-for-byte the old behavior (AC-05) - anonymous,
        //    non-blocking, the AI jumble reachable by every session; the real runtime
        //    gate stays quota (story 03) + the spend breaker (story 04).
        var resolved = _connectionEntitlements.TryGet(Context.ConnectionId);
        var sessionEntitlements = resolved is { } capabilities
            ? capabilities.Capabilities
            : await _entitlements.EvaluateForSession(purchaserIdentity: null, Context.ConnectionAborted);
        room.CaptureEntitlements(sessionEntitlements);

        // Subscribe the host's connection to the room group so later stories'
        // roster/round broadcasts (Clients.Group(room.Code)) reach them.
        await Groups.AddToGroupAsync(Context.ConnectionId, room.Code);

        // session-engine/07 (AC-06): hand the host its OWN opaque reconnect token in
        // this envelope only - never on the roster the whole room receives. Story 08's
        // Rejoin spends it to reclaim this seat after a drop.
        var reconnectToken = room.GetReconnectToken(Context.ConnectionId);
        return new CreateRoomResultDto(true, ToRoomState(room), null, reconnectToken);
    }

    /// <summary>
    /// session-engine/02: join an existing room with a code and a display name.
    ///
    /// Anonymous by design (AC-02): the ONLY inputs are a room code, a free-text
    /// display name, and a Guardian variant - never an account, email, or any PII.
    /// Validation runs in a fixed order and every EXPECTED failure returns a
    /// friendly JoinResultDto (Ok=false, Error=...) rather than throwing, so the
    /// client can show the message inline and let the player try again:
    ///
    ///   1. Unknown / expired code -> not joined (AC-04).
    ///   2. Empty or over-long (>14) name -> friendly error (AC-03).
    ///   3. Content-safety filter rejects the name -> the filter's message. The
    ///      name is vetted server-side BEFORE it is ever stored or broadcast, so
    ///      an unfiltered name never reaches another player (README section 6,
    ///      AC-03). This server check is authoritative even if the client
    ///      pre-validates.
    ///   4. Name already taken in this room (case-insensitive) -> friendly error
    ///      (AC-06).
    ///
    /// On success the joiner is added to the room, subscribed to the room's
    /// SignalR group, and the updated roster is broadcast to everyone in the room
    /// as "RosterChanged" so the host and other players see the newcomer appear in
    /// near-real-time (AC-05). The caller also gets the room state back to land in
    /// the lobby (AC-01).
    /// </summary>
    /// <param name="code">The room's join code (case-insensitive).</param>
    /// <param name="displayName">The player's free-text in-session name (max 14 chars, safety-checked).</param>
    /// <param name="variant">The chosen Guardian variant; normalized server-side to one of the six known values, defaulting to "teal" when null/empty/unknown (session-engine/05).</param>
    public async Task<JoinResultDto> JoinRoom(string code, string displayName, string variant)
    {
        // 1. Look the room up first (AC-04). An unknown or expired code means
        //    there is nothing to join - fail before touching the name.
        var room = _rooms.TryGet(code);
        if (room is null)
        {
            return new JoinResultDto(
                false,
                null,
                RoomNotFoundMessage);
        }

        // 1b. session-engine/13 (AC-02/W4): a round already live (either
        //     "prompting" or "reveal" - RoomStateDto carries no phase field, so a
        //     joining client cannot detect a live round on its own) blocks a
        //     BRAND-NEW seat. Checked here, before the name-length check and the
        //     async safety filter, so a blocked joiner never pays for that
        //     round-trip on a join that was always going to fail, and is never
        //     partially seated. Does NOT affect Rejoin (a resume of an ALREADY-held
        //     seat, session-engine/08) - that stays working mid-round by design.
        if (room.CurrentRound is not null)
        {
            return new JoinResultDto(
                false,
                null,
                "This crew's mid-tale right now - hang tight and you'll be seated for the next round.");
        }

        // 2. Basic shape of the display name (AC-03). Trim first so " " is empty
        //    and trailing spaces do not count toward the length or defeat the
        //    uniqueness check.
        var name = (displayName ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            return new JoinResultDto(false, null, "Pick a display name so your crew knows who you are.");
        }
        if (name.Length > MaxDisplayNameLength)
        {
            return new JoinResultDto(false, null, $"That name is a bit long - keep it to {MaxDisplayNameLength} characters.");
        }

        // 3. Child safety (README section 6, AC-03): vet the free-text name
        //    server-side BEFORE it is stored or shown. Never broadcast an
        //    unfiltered name. The verdict carries a friendly retry message.
        var verdict = await _safety.CheckAsync(name, Context.ConnectionAborted);
        if (!verdict.IsAllowed)
        {
            return new JoinResultDto(false, null, verdict.Message);
        }

        // Constrain the variant to the known set (session-engine/05, AC-03):
        // null/empty/unknown all normalize to the default "teal" so a
        // malformed client can never inject an arbitrary variant string into
        // room state that every other player then sees rendered.
        var chosenVariant = NormalizeVariant(variant);

        // 4. Seat the (now-vetted) player under the room lock, which enforces BOTH
        //    the capacity cap (W2 - family-sized rooms top out at Room.MaxPlayers,
        //    host included) and case-insensitive name uniqueness (AC-06), atomically
        //    so a concurrent join-storm can violate neither.
        var seat = room.AddPlayer(name, chosenVariant, Context.ConnectionId);
        if (seat == AddPlayerResult.RoomFull)
        {
            return new JoinResultDto(
                false,
                null,
                $"This room is full ({Room.MaxPlayers} players). Ask the host to start, or set up your own game.");
        }
        if (seat == AddPlayerResult.NameTaken)
        {
            return new JoinResultDto(false, null, "That name is taken in this room - try another.");
        }

        // Subscribe this connection to the room group so it receives future
        // roster/round broadcasts, then broadcast the new roster to everyone in
        // the room (host + existing players + the joiner) in near-real-time (AC-05).
        await Groups.AddToGroupAsync(Context.ConnectionId, room.Code);
        await Clients.Group(room.Code).SendAsync("RosterChanged", ToRoomState(room));

        // session-engine/07 (AC-06): hand the joiner its OWN opaque reconnect token in
        // this envelope only - never broadcast on the roster (that would let another
        // player hijack the seat). Story 08's Rejoin spends it to reclaim this seat.
        var reconnectToken = room.GetReconnectToken(Context.ConnectionId);
        return new JoinResultDto(true, ToRoomState(room), null, reconnectToken);
    }

    /// <summary>
    /// session-engine/08: reclaim a held seat and resume the round.
    ///
    /// Story 07 holds a dropped seat open for a grace window and mints a per-seat
    /// reconnect token; this method SPENDS that token. When a device's SignalR
    /// connection drops (a car dead zone, a phone lock, a page reload) it reconnects on
    /// a BRAND-NEW connection id (SignalR's own <c>withAutomaticReconnect</c> always gets
    /// a fresh one), then calls <c>Rejoin(code, token)</c> to prove it owns the seat and
    /// move it to the new connection - rather than looking like it re-joined a fresh
    /// game. Validation runs in a fixed order and every EXPECTED failure returns a
    /// friendly <see cref="RejoinResultDto"/> (Ok=false, kid-readable Error) rather than
    /// throwing, mirroring <see cref="JoinRoom"/>'s envelope style:
    ///
    ///   1. Unknown / expired room code -> friendly fail (nothing to rejoin).
    ///   2. <see cref="Room.ReclaimSeat"/> finds no seat holding the token (unknown,
    ///      already evicted when its grace expired, or a token minted for a DIFFERENT
    ///      room than <paramref name="code"/>) -> friendly fail, mutates nothing (AC-05).
    ///
    /// On success the seat is reclaimed ATOMICALLY under the room lock: the caller's new
    /// connection id is swapped in, the seat is marked connected again, and the pending
    /// grace-expiry eviction is cancelled (a race between "grace expires" and "Rejoin
    /// lands" resolves deterministically under that same lock - whichever wins, the other
    /// is a no-op). This method then (AC-01) re-subscribes the new connection to the
    /// room's SignalR group (the old connection's membership tore down on disconnect),
    /// (AC-06) re-broadcasts the roster so every OTHER player sees this seat flip back to
    /// Connected in near-real-time, and builds the rehydration envelope (AC-02 to AC-04):
    /// the roster, the host flag, the phase, and - for a "prompting" round - THIS seat's
    /// own remaining blank indices (via the reclaim result) plus the room-wide collection
    /// progress from the EXISTING <see cref="Room.GetProgressCounts"/> / <see cref="Room.GetProgress"/>,
    /// or - for a "reveal" round - the shared ordered words from the EXISTING
    /// <see cref="Room.BuildReveal"/>. No second, parallel round projection is built, and
    /// nothing beyond what the room already exposes to every player travels back (AC-07).
    /// </summary>
    /// <param name="code">The room's join code (case-insensitive), from the caller's stored handle.</param>
    /// <param name="token">The caller's own opaque reconnect token, from its create/join result envelope.</param>
    public async Task<RejoinResultDto> Rejoin(string code, string token)
    {
        // 1. Look the room up first (an unknown / expired code has nothing to rejoin).
        var room = _rooms.TryGet(code);
        if (room is null)
        {
            return new RejoinResultDto(
                false,
                "We couldn't find that game - it may have wrapped up. Head back and start or join again.",
                Room: null, IsHost: false, Phase: null, Round: null, YourBlanks: null, Progress: null, Reveal: null);
        }

        // 2. Reclaim the seat by token under the room lock (AC-01/AC-05). A miss (unknown,
        //    already-evicted, or a token for a different room) mutates nothing and is a
        //    friendly failure - never a throw.
        var reclaim = room.ReclaimSeat(token, Context.ConnectionId);
        if (reclaim.Status == ReclaimStatus.NotFound)
        {
            return new RejoinResultDto(
                false,
                "We couldn't get you back into that game - your seat may have timed out. Head back and join again.",
                Room: null, IsHost: false, Phase: null, Round: null, YourBlanks: null, Progress: null, Reveal: null);
        }

        // 3. Re-subscribe the NEW connection to the room's SignalR group (the OLD
        //    connection's membership already tore down via SignalR's disconnect handling),
        //    so future roster/round broadcasts reach the resumed device (AC-01).
        await Groups.AddToGroupAsync(Context.ConnectionId, room.Code);

        // 4. Re-broadcast the roster so EVERY player sees this seat flip back to Connected
        //    in near-real-time (AC-06). ToRoomState carries the now-true Connected flag.
        await Clients.Group(room.Code).SendAsync("RosterChanged", ToRoomState(room));

        // 5. Build the rehydration envelope (AC-02 to AC-04). The roster + host flag +
        //    phase always come back; the phase decides what round data rides along, all
        //    from the EXISTING projections (no parallel bookkeeping).
        RoundStartedDto? roundDto = null;
        YourBlanksDto? blanksDto = null;
        CollectProgressDto? progressDto = null;
        RevealReadyDto? revealDto = null;

        var round = room.CurrentRound;
        if (round is not null)
        {
            // The same RoundStarted metadata every player received (template id + mode +
            // round number + CrownedNickname) - carried through exactly as the RoundStarted
            // broadcast does. CrownedNickname here is already-public, decided round metadata,
            // distinct from the parked LIVE vote/reaction-tally rehydration (which may show
            // reset counters until the next broadcast - see 08's Out of Scope).
            roundDto = new RoundStartedDto(round.TemplateId, round.Mode, round.RoundNumber, round.CrownedNickname);

            // Note: reclaim.Phase is a point-in-time snapshot captured atomically under the
            // room lock during ReclaimSeat; the projection reads below re-acquire the lock, so
            // a round flipping prompting -> reveal in between yields a momentarily
            // phase-inconsistent envelope. That is benign: the new connection is already in the
            // room's group (re-added above), so the live RevealReady / CollectProgress
            // broadcast reconciles it - the same cross-lock pattern SubmitWord already relies on.
            if (string.Equals(reclaim.Phase, "prompting", StringComparison.Ordinal))
            {
                // AC-03: ONLY this seat's own outstanding blank indices (from the atomic
                // reclaim), plus the room-wide "N of M done" progress the Waiting card shows.
                blanksDto = new YourBlanksDto(reclaim.RemainingBlankIndices);

                var (doneCount, playerCount) = room.GetProgressCounts();
                var progress = room.GetProgress()
                    .Select(p => new PlayerProgressDto(p.Nickname, p.Variant, p.Done))
                    .ToArray();
                progressDto = new CollectProgressDto(doneCount, playerCount, progress);
            }
            else if (string.Equals(reclaim.Phase, "reveal", StringComparison.Ordinal))
            {
                // AC-04: the shared ordered reveal words, so the resuming client renders the
                // exact reveal everyone else is looking at (built by the SAME BuildReveal).
                var words = room.BuildReveal()
                    .Select(w => new RevealWordDto(w.Word, w.Nickname, w.Variant))
                    .ToArray();
                revealDto = new RevealReadyDto(round.TemplateId, words);
            }
        }

        return new RejoinResultDto(
            true,
            Error: null,
            Room: ToRoomState(room),
            IsHost: reclaim.IsHost,
            Phase: reclaim.Phase,
            Round: roundDto,
            YourBlanks: blanksDto,
            Progress: progressDto,
            Reveal: revealDto);
    }

    /// <summary>
    /// session-engine/03: leave-detection rides the SignalR connection lifecycle.
    ///
    /// When a connection drops (the app is closed, the tab navigates away, or the
    /// network dies) SignalR calls this override. We remove that connection's
    /// player from whichever room it was seated in via the registry, which also
    /// drops the room if it is now empty. If the room still has members, we
    /// broadcast the trimmed roster as "RosterChanged" so the departed player's
    /// tile reverts to an empty slot on every remaining client within a short
    /// window (AC-04) - no heartbeat is needed for Slice 1.
    ///
    /// SignalR auto-removes the connection from its groups on disconnect, so the
    /// broadcast below reaches exactly the remaining members. We always chain to
    /// base.OnDisconnectedAsync so the framework's own teardown still runs.
    ///
    /// platform-devops/04 (AC-03): an ABNORMAL close (a non-null exception - a
    /// dropped network, a transport error, a client crash, as opposed to a clean
    /// LeaveRoom / tab close where exception is null) is tracked in App Insights so
    /// a disconnect STORM is diagnosable rather than invisible. The tracked event
    /// carries NO room code, nickname, or connectionId (AC-04) - just the fact of
    /// an abnormal close plus the transport exception's type/stack, which the PII
    /// scrubber's allowed shape permits. No-ops cleanly with no connection string
    /// configured (AC-05).
    ///
    /// session-engine/07 (hold the seat): a dropped connection (ANY OnDisconnectedAsync
    /// - the connection-lifecycle drop, as opposed to a deliberate LeaveRoom) no longer
    /// evicts the seat on the spot. Instead it HOLDS the seat for a grace window: mark
    /// it disconnected (kept on the roster, PlayerCount unchanged, a "prompting" round
    /// NOT aborted - AC-01/AC-02) and schedule ONE one-shot delayed eviction that runs
    /// today's eviction + conditional RoundAborted + RosterChanged ONLY if the window
    /// elapses with no reconnect (AC-03). A deliberate LeaveRoom still evicts
    /// immediately (AC-04). This is the car dead-zone / phone-lock tolerance README
    /// section 1 calls out - a brief blip must not tear down the room.
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // accounts-identity/06 (AC-03): the physical connection is ending, so drop its
        // resolved-capability entry from the per-connection singleton. Keyed by
        // ConnectionId and tied to the CONNECTION's lifecycle (not the room's) - a
        // deliberate LeaveRoom keeps the shared connection open and does NOT clear it;
        // only the connection truly ending does. A no-op for an anonymous connection
        // that stored nothing.
        _connectionEntitlements.Remove(Context.ConnectionId);

        if (exception is not null)
        {
            // Abnormal close only (a clean disconnect passes null). Track the
            // anonymous fact + the transport exception - never any room/player payload.
            // Wrapped so an unexpected telemetry failure can NEVER interfere with the
            // disconnect cleanup / grace scheduling below (AC-08 posture, matching
            // TrackUsageRoundStarted/Completed and FireServeEvent).
            try
            {
                _appInsights.TrackEvent("HubAbnormalDisconnect");
                _appInsights.TrackException(exception);
            }
            catch
            {
                // Swallowed: telemetry must never break hub teardown.
            }
        }

        // session-engine/07: hold the dropped seat instead of evicting it now. BeginGrace
        // marks the seat disconnected (still on the roster) and returns a handle, or null
        // when the connection was not seated anywhere (the no-op case).
        var handle = _rooms.BeginGrace(Context.ConnectionId);
        if (handle is not null)
        {
            // AC-01: the roster still reports the seat, now flagged not-connected.
            // Broadcast it so every remaining client's roster reflects the held seat.
            // Nothing renders the Connected flag until web story 10; the round is NOT
            // aborted here (AC-02) - only the deferred eviction can abort it (AC-03).
            await Clients.Group(handle.Room.Code).SendAsync("RosterChanged", ToRoomState(handle.Room));

            // Optional anonymous operational signal (no room / nickname payload), mirroring
            // HubAbnormalDisconnect above - "a grace window began". Never breaks teardown.
            try
            {
                _appInsights.TrackEvent("HubGraceStarted");
            }
            catch
            {
                // Swallowed: telemetry must never break hub teardown.
            }

            // Fire-and-forget the one-shot delayed eviction. The seat's CancellationToken
            // lives on the Room, so story 08's Rejoin can cancel it to keep the seat;
            // SeatGraceService wraps the run so a fault is logged, never left unobserved.
            _ = _grace.ScheduleEviction(handle);
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// session-engine/03: leave a room explicitly WITHOUT dropping the SignalR
    /// connection (the player tapped "Leave" and returned Home, but the one shared
    /// connection stays open for their next game). This is the deliberate-leave
    /// counterpart to OnDisconnectedAsync's connection-drop path.
    ///
    /// It removes this connection from the room's SignalR group FIRST - so a
    /// roster broadcast that races this call cannot resurrect the room on a client
    /// that has already gone Home - then removes the player from room state via the
    /// registry (which drops the room if it is now empty) and, when members remain,
    /// broadcasts the trimmed roster so the leaver's tile reverts to an empty slot
    /// for everyone else (AC-04). Idempotent: a second call (or a leave that races
    /// the disconnect handler) simply finds nothing to remove and no-ops.
    /// </summary>
    /// <param name="code">The code of the room being left (used to leave its group).</param>
    public async Task LeaveRoom(string code)
    {
        if (!string.IsNullOrWhiteSpace(code))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, code);
        }

        var room = _rooms.RemoveConnection(Context.ConnectionId, out var promotedHostConnectionId);
        await HandlePlayerLeftAsync(room, Context.ConnectionId, promotedHostConnectionId);
    }

    /// <summary>
    /// group-play/01: the HOST starts a round.
    ///
    /// HOST-ONLY and SERVER-ENFORCED (AC-03): the UI hides the "Start game" CTA
    /// from non-hosts, but this method is the authoritative gate - it verifies the
    /// calling connection actually owns the room's host player before doing
    /// anything, so a crafted client cannot start someone else's round.
    ///
    /// Validation runs in a FIXED order and every EXPECTED failure returns a
    /// friendly <see cref="StartRoundResultDto"/> (Ok=false, kid-readable Error)
    /// rather than throwing, mirroring <see cref="JoinRoom"/>'s envelope style:
    ///
    ///   1. Unknown / expired code -> friendly fail (nothing to start).
    ///   2. Caller is not the host -> reject (AC-03). Server-authoritative.
    ///   3. Fewer than 2 players -> "you need at least one more carver" (AC-01
    ///      requires the host plus at least one other player).
    ///   4. Select a template: filter the server catalog through the family-safe
    ///      selector using the host's toggle value (AC-04), then (story-selection/02,
    ///      AC-03) narrow by the host's story-length choice through
    ///      <see cref="LengthContentSelector"/>, then (story-selection/03, AC-02)
    ///      narrow by this ROOM's played-template history through
    ///      <see cref="FreshnessContentSelector"/> (recycling the whole pool once it
    ///      runs dry, AC-03), then auto-pick one at random from the final subset (no
    ///      picker UI in Slice 1). If somehow nothing is allowed after the
    ///      family-safe gate, friendly fail rather than throw; an empty LENGTH pool
    ///      instead degrades to the family-safe pool (AC-06 - see Stage 2 below), and
    ///      an exhausted FRESHNESS pool recycles rather than failing (Stage 3 below).
    ///
    /// On success it sets the room's round state (round 1, the chosen template, the
    /// host's chosen mode, "prompting") and broadcasts "RoundStarted" to the whole room
    /// group so EVERY player (host included) transitions into word collection
    /// together in near-real-time (AC-01, AC-02), then returns Ok=true.
    ///
    /// story-selection/02, AC-03: <paramref name="lengthPref"/> is the host's
    /// story-length choice, sent as ONE MORE PARAMETER on this SAME invoke (never a
    /// new hub method) - the SERVER enforces it, exactly like the family-safe
    /// toggle; the client's pick is never trusted directly. A null/empty/unrecognized
    /// value defensively falls back to "any" (<see cref="NormalizeLengthPreference"/>)
    /// so a malformed client can only ever WIDEN toward no filtering, never bypass or
    /// weaken the family-safe gate that always runs first (AC-05).
    /// </summary>
    /// <param name="code">The room's join code (case-insensitive).</param>
    /// <param name="familySafe">The host's family-safe toggle position; the SERVER filters the catalog by it (authoritative, AC-04).</param>
    /// <param name="lengthPref">The host's story-length choice ("quick" | "full" | "any"); the SERVER filters the family-safe pool by it (authoritative, story-selection/02 AC-03). Null/empty/unrecognized falls back to "any".</param>
    /// <param name="mode">group-play/05 (host picks the mode, AC-02): the mode id the host chose. The SERVER validates it against the OFFERED set (<see cref="GameModeCatalog"/>) and rejects an unknown or deferred mode (e.g. progressive-story, AC-05); it then draws the template from THAT mode's eligible pool (Word Bank needs a word bank, AC-06). Null/empty/legacy 3-arg callers default to Classic Blind. The chosen mode is broadcast on <see cref="RoundStartedDto.Mode"/> for real (it was pinned before this story).</param>
    /// <param name="templateId">story-selection/06 (favorite a story, AC-03/AC-04): an OPTIONAL explicit template id. When the host starts a round from their device-local favorites, this names the exact tale to play. It still passes the family-safe gate first (AC-06) but SKIPS the length + freshness stages and is neither re-stamped into the room's played history nor logged to the serve log (a private replay, not a curation signal). Null/empty means the normal random pick (the pre-06 behavior, unchanged). It must still be eligible for the chosen mode (group-play/05, AC-06).</param>
    public async Task<StartRoundResultDto> StartRound(string code, bool familySafe, string lengthPref, string? mode = null, string? templateId = null)
    {
        // 1. Look up the room first (an unknown / expired code has nothing to start).
        var room = _rooms.TryGet(code);
        if (room is null)
        {
            return new StartRoundResultDto(
                false,
                RoomNotFoundMessage);
        }

        // 2. Server-enforced host check (AC-03): while the room HAS a host, only the
        //    connection that owns that host player may start a round (the one exception
        //    is the hostless case below). This is authoritative even though the client
        //    also hides the CTA from non-hosts.
        //
        //    room-start-duplicate-members (belt-and-suspenders): host migration keeps a
        //    non-empty room always hosted, so this normally rejects every non-host. The
        //    `room.HasHost` guard only relaxes it if a room ever ends up with NO host at
        //    all - then any remaining player may start it rather than the room being
        //    permanently unstartable. Mirrors the client CTA, which likewise shows Start
        //    to everyone when the roster carries no host.
        if (!room.IsHost(Context.ConnectionId) && room.HasHost)
        {
            return new StartRoundResultDto(
                false,
                "Only the host can start the game.");
        }

        // 2b. session-engine/13 (AC-01/W3): reject a re-start while a round is
        //     still "prompting" (a host double-tap, or any other caller) BEFORE the
        //     player-count check, mode resolution, the template-selection pipeline,
        //     or dealing - nothing already collected is discarded and nobody is
        //     re-dealt. Mirrors PassHost's exact phase-check style. Guards
        //     "prompting" ONLY: a "reveal"-phase restart is the shipped "Play
        //     another round" flow (BackToLobby's own doc comment: "the replay
        //     counterpart is just StartRound again on the same room") and a lobby
        //     (no round at all) must both keep starting a round normally.
        var currentRound = room.CurrentRound;
        if (currentRound is not null && string.Equals(currentRound.Phase, "prompting", StringComparison.Ordinal))
        {
            return new StartRoundResultDto(
                false,
                "This tale's already being carved - wait for the reveal before starting a new one.");
        }

        // 3. Need the host plus at least one other player (AC-01).
        if (room.PlayerCount < 2)
        {
            return new StartRoundResultDto(
                false,
                "You need at least one more carver before you can start.");
        }

        // 3b. group-play/05 (AC-02): resolve the host's chosen mode against the
        //     OFFERED set, server-authoritatively. An unknown or DEFERRED mode
        //     (progressive-story is known but not offered for group, AC-05) resolves
        //     to null and is rejected here, so it can never begin a round no matter
        //     what the client sends. A null/empty/legacy value defaults to Classic
        //     Blind (the pre-05 behavior), mirroring the length/variant normalizers.
        var resolvedMode = GameModeCatalog.ResolveOffered(mode);
        if (resolvedMode is null)
        {
            return new StartRoundResultDto(
                false,
                "That game mode isn't ready for group play yet - pick another one.");
        }

        // 4. Select a template through the ONE explicit selection pipeline
        //    (story-selection/01 AC-03). The stages run in a FIXED order and the
        //    family-safe gate is ALWAYS FIRST - no path around it:
        //
        //    Stage 1 - FAMILY-SAFE GATE (always first, AC-04): the family-safe
        //    toggle the host sent decides which catalog entries are allowed.
        //    Filtering happens HERE, server-side, so the toggle is authoritative -
        //    the client never sends a template id.
        var familySafePool = _familySafe.SelectAllowed(_catalog.Entries, familySafe);
        if (familySafePool.Count == 0)
        {
            // Defensive: every current seed template is family-safe, so this is
            // structurally unreachable today - but fail friendly rather than throw
            // if the catalog is ever emptied or fully non-family-safe under a
            // family-safe round. This is the ONLY empty-pool that fails the round;
            // an empty LENGTH pool degrades instead (Stage 2), never errors.
            return new StartRoundResultDto(
                false,
                "No tales are available right now - please try again.");
        }

        //    Stage 1b - PER-MODE ELIGIBILITY (group-play/05, AC-06): narrow the
        //    family-safe pool to the templates the CHOSEN mode may draw. Word Bank
        //    keeps only templates that carry a curated word bank (so it never picks
        //    a bank-less tale and renders an empty tap list); every other offered
        //    mode is bank-agnostic and this is a no-op. The gate lives in
        //    GameModeCatalog (mirroring the web's offerWordBankTemplates), never
        //    inlined here. If no template survives (e.g. Word Bank under a family-safe
        //    round with no safe word-bank tale), fail friendly rather than throw -
        //    the web disables an unstartable mode, so this is defensive.
        var modePool = familySafePool
            .Where(entry => GameModeCatalog.IsEligible(entry, resolvedMode))
            .ToList();
        if (modePool.Count == 0)
        {
            return new StartRoundResultDto(
                false,
                "No tales are available for that mode right now - try another mode.");
        }

        //    story-selection/06 (favorite a story, AC-03/AC-04) - the EXPLICIT
        //    pinned-template branch, the AC-04 bypass seam Stage 3 reserved. When
        //    the host started this round from their device-local favorites,
        //    templateId names the chosen tale directly. It still had to clear the
        //    family-safe gate FIRST (it must be present in familySafePool above -
        //    AC-06: a non-family-safe favorite is not playable in a family-safe
        //    game), but as an EXPLICIT replay it SKIPS the length + freshness stages
        //    below and, per AC-04, is neither re-stamped into this room's played
        //    history (Step 5a) NOR logged to the serve log (Step 8) - a star is a
        //    private device-local shortcut, not a curation signal.
        var explicitPick = !string.IsNullOrWhiteSpace(templateId);
        TemplateCatalogEntry chosen;
        if (explicitPick)
        {
            var favorite = modePool.FirstOrDefault(
                entry => string.Equals(entry.Id, templateId, StringComparison.Ordinal));
            if (favorite is null)
            {
                // Unknown id, or a favorite the family-safe gate OR the chosen mode's
                // eligibility excludes in this game (group-play/05, AC-06 - e.g. a
                // bank-less favorite under Word Bank) - fail friendly rather than throw
                // or silently fall back to a random tale the host did not ask for. The
                // message hints at the mode, since a favorite now plays in the host's
                // chosen mode and the likeliest miss is a mode-ineligible tale.
                return new StartRoundResultDto(
                    false,
                    "That favorite tale isn't available in this mode right now - try another mode or tale.");
            }

            chosen = favorite;
        }
        else
        {
            //    Stage 2 - LENGTH FILTER + empty-pool fallback (AC-06): narrow the
            //    safety+mode pool to the requested length class using the host's
            //    story-length choice (story-selection/02, AC-03). A malformed/unknown
            //    lengthPref is normalized to "any" first so a crafted client cannot
            //    break the pick. If the requested length would leave an EMPTY pool, the
            //    selector DEGRADES to the mode pool rather than failing the round
            //    (a longer story, never an error) - the fallback lives in THIS pipeline,
            //    not in callers.
            var pool = _length.SelectByLengthOrFallback(modePool, NormalizeLengthPreference(lengthPref));

            //    Stage 3 - FRESHNESS FILTER + recycle (story-selection/03, AC-02/AC-03):
            //    narrow the safety+length-filtered pool to templates this ROOM has not
            //    yet played (Room.PlayedTemplateIds - ephemeral, in-memory, dies with the
            //    room, AC-06 - ids only, no PII). If every template in the pool has
            //    already been played, SelectFreshOrRecycle reopens the WHOLE pool
            //    (least-recently-played first) rather than failing the round - a repeat
            //    is a fine outcome once the pool runs dry, an errored round is not.
            var freshPool = _freshness.SelectFreshOrRecycle(pool, room.PlayedTemplateIds);

            //    Stage 4 - RANDOM PICK from the final pool (no picker UI in Slice 1).
            chosen = freshPool[Random.Shared.Next(freshPool.Count)];
        }

        // 5. Set the room's round state (round 1, the host's chosen mode, "prompting") AND
        //    compute the round-robin blank assignment (group-play/02). The deal
        //    happens under the room lock inside StartRound, using the template's
        //    BlankCount from the catalog, so it is atomic with the roster snapshot
        //    it deals to (a join/leave racing this lands fully before or after the
        //    deal, never mid-deal). The C# deal MIRRORS web/src/engine/distribute.ts.
        var round = room.StartRound(chosen.Id, resolvedMode, chosen.BlankCount);

        // 5a. story-selection/03 (AC-02): record the chosen id in THIS room's
        //     played-template history, AFTER the round has opened, so the NEXT
        //     StartRound's freshness stage (Stage 3 above) excludes it. This is
        //     the RANDOM-pick path's bookkeeping - an EXPLICIT favorite replay
        //     (story-selection/06, AC-04) SKIPS it, so replaying a favorite never
        //     makes the random pick "forget" the other tales this room has not seen.
        if (!explicitPick)
        {
            room.MarkTemplatePlayed(chosen.Id);
        }

        // 6. Broadcast to the WHOLE room group (host included) so all players
        //    transition into word collection together (AC-01, AC-02). Full
        //    template content is resolved client-side from seedLibrary by id - the
        //    server ships only the id + mode + round number.
        await Clients.Group(room.Code).SendAsync(
            "RoundStarted",
            new RoundStartedDto(round.TemplateId, round.Mode, round.RoundNumber, round.CrownedNickname));

        // 7. Deal each player ONLY its own blanks (group-play/02, AC-02). This is a
        //    per-connection send (NOT a group broadcast): a player never learns
        //    another player's blanks, and the payload carries only blank indices -
        //    no connection id, no nickname, no PII (README section 6). Each client
        //    resolves an index to its prompt via getBlanks(template)[index] (Classic
        //    blind - prompt only). ConnectionId stays server-side; it is the handle
        //    we address, never a value we send.
        foreach (var assignment in round.Assignments)
        {
            await Clients.Client(assignment.ConnectionId).SendAsync(
                "YourBlanks",
                new YourBlanksDto(assignment.BlankIndices));
        }

        // 8. story-selection/04 (anonymous serve log, AC-01): record ONE serve
        //    event as a FIRE-AND-FORGET epilogue. This is deliberately the LAST
        //    thing StartRound does and is NOT awaited on the round-start path -
        //    the round has already started and every player has already been dealt
        //    their blanks above, so a slow / down / misconfigured sink can never
        //    delay or block the round (AC-03). The event carries ONLY anonymous
        //    facts: the template id, mode, derived length class, player COUNT, the
        //    family-safe flag, and the room's OPAQUE instance id - never a
        //    connectionId, nickname, or the join code (AC-04).
        //
        //    An EXPLICIT favorite replay (story-selection/06) is deliberately NOT
        //    logged here: a star is a private, device-local shortcut, not a curation
        //    signal, so it must not skew per-template serve counts (feature.md /
        //    story 06 tech note). Only the RANDOM-pick path emits a serve event.
        if (!explicitPick)
        {
            var serveEvent = new ServeEvent(
                TemplateId: chosen.Id,
                TimestampUtc: DateTimeOffset.UtcNow,
                Mode: round.Mode,
                // Derive the length class from the chosen template's blank count using
                // story-01's single threshold (mirrors the web's classifyLength).
                LengthClass: chosen.BlankCount <= LengthContentSelector.QuickMaxBlanks ? "quick" : "full",
                PlayerCount: room.PlayerCount,
                FamilySafe: familySafe,
                InstanceId: room.InstanceId);
            FireServeEvent(serveEvent);
        }

        // 9. platform-devops/05 (anonymous product-usage, AC-01): record ONE
        //    "RoundStarted" App Insights CUSTOM EVENT with the MODE + group context
        //    (and an anonymous player COUNT), so "which modes get played, solo vs
        //    group" is answerable. This rides story 04's App Insights pipeline (the
        //    injected TelemetryClient + the single PII scrubber) - it is a DIFFERENT
        //    surface from the serve log above (story-selection/04 -> Table Storage,
        //    content curation) and from 04's operational health (exceptions /
        //    disconnects); coordinated, never a third stack (AC-06). Fire-and-forget
        //    and non-throwing - it NEVER gates the round (AC-08). Anonymous by
        //    construction: no code / nickname / connectionId (AC-04).
        TrackUsageRoundStarted(round, room.PlayerCount);

        return new StartRoundResultDto(true, null);
    }

    /// <summary>
    /// platform-devops/05 (AC-01): fire the anonymous "RoundStarted" product-usage
    /// custom event on story 04's App Insights pipeline. Fire-and-forget and
    /// non-throwing exactly like <see cref="FireServeEvent"/>: TrackEvent only
    /// enqueues (no network on this path) and no-ops cleanly with no connection
    /// string (AC-08/AC-05), but we still swallow any fault so telemetry can NEVER
    /// delay or error a round. Carries ONLY the anonymous mode + group context + a
    /// player count, all routed through the single PII scrubber (AC-04).
    /// </summary>
    private void TrackUsageRoundStarted(RoundState round, int playerCount)
    {
        try
        {
            _appInsights.TrackEvent(
                UsageTelemetry.RoundStartedEvent,
                UsageTelemetry.BuildProperties(round.Mode, UsageTelemetry.GroupContext, playerCount: playerCount));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Usage RoundStarted event failed (swallowed - telemetry never gates gameplay).");
        }
    }

    /// <summary>
    /// platform-devops/05 (AC-02): fire the anonymous "RoundCompleted" product-usage
    /// custom event carrying the round DURATION (ms) as a metric, plus the mode +
    /// group context. Same fire-and-forget, non-throwing posture as
    /// <see cref="TrackUsageRoundStarted"/> - it never blocks or faults the reveal
    /// (AC-08). No per-person identity is attached (AC-04): a duration + a mode +
    /// the group context only.
    /// </summary>
    private void TrackUsageRoundCompleted(RoundState round, double durationMs)
    {
        try
        {
            _appInsights.TrackEvent(
                UsageTelemetry.RoundCompletedEvent,
                UsageTelemetry.BuildProperties(round.Mode, UsageTelemetry.GroupContext),
                UsageTelemetry.BuildDurationMetric(durationMs));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Usage RoundCompleted event failed (swallowed - telemetry never gates gameplay).");
        }
    }

    /// <summary>
    /// story-selection/04: fire the serve event WITHOUT awaiting it on the
    /// round-start path (AC-03). The sink already swallows its own write failures,
    /// but a fire-and-forget task must ALSO never surface an unobserved exception,
    /// so this wraps the call and logs anything that somehow escapes. Gameplay is
    /// completely unaffected whether the sink succeeds, fails, or hangs.
    /// </summary>
    private void FireServeEvent(ServeEvent serveEvent)
    {
        try
        {
            // Start the write but do NOT await it on the round-start path (AC-03).
            var writeTask = _telemetry.RecordServeAsync(serveEvent, CancellationToken.None);

            // Observe an ASYNCHRONOUS fault so a fire-and-forget task never becomes
            // an unobserved throw, still without awaiting it here. RecordServeAsync
            // is contractually non-throwing, so this is belt-and-braces.
            _ = writeTask.ContinueWith(
                faulted => _logger.LogWarning(
                    faulted.Exception,
                    "Serve-log epilogue faulted for template {TemplateId} (swallowed - telemetry never gates gameplay).",
                    serveEvent.TemplateId),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            // A sink that throws SYNCHRONOUSLY (before its first await) must not
            // fault StartRound either - the round has already started (AC-03).
            _logger.LogWarning(
                ex,
                "Serve-log epilogue threw for template {TemplateId} (swallowed - telemetry never gates gameplay).",
                serveEvent.TemplateId);
        }
    }

    /// <summary>
    /// group-play/04: the HOST ends the round and sends everyone back to the LOBBY
    /// (AC-05).
    ///
    /// HOST-ONLY and SERVER-ENFORCED, mirroring <see cref="StartRound"/>'s host gate:
    /// the UI only shows "Back to lobby" to the host, but this method is the
    /// authoritative check - it verifies the calling connection owns the room's host
    /// player before doing anything, so a crafted non-host client cannot yank the
    /// whole group out of the round. It reuses the same friendly
    /// <see cref="StartRoundResultDto"/> envelope (never throws for an expected
    /// failure):
    ///
    ///   1. Unknown / expired code -> friendly fail (nothing to return to a lobby).
    ///   2. Caller is not the host -> reject (server-authoritative).
    ///
    /// On success it clears the room's round via <see cref="Room.BackToLobby"/> (which
    /// preserves the roster and code - the room stays live, AC-05) and broadcasts a
    /// BARE "BackToLobby" to the whole room group. The roster is unchanged, so no
    /// payload is needed: each client's handler simply drops its round/reveal state
    /// and falls back to the Lobby it already holds (code + roster intact). The
    /// replay counterpart ("Play another round") is just <see cref="StartRound"/>
    /// again on the same room - no separate restart method.
    /// </summary>
    /// <param name="code">The room's join code (case-insensitive).</param>
    public async Task<StartRoundResultDto> BackToLobby(string code)
    {
        // 1. Look up the room first (an unknown / expired code has no lobby to return to).
        var room = _rooms.TryGet(code);
        if (room is null)
        {
            return new StartRoundResultDto(
                false,
                RoomNotFoundMessage);
        }

        // 2. Server-enforced host check (AC-05): only the host may move the whole
        //    group back to the lobby, matching group-play/01's host-driven start.
        if (!room.IsHost(Context.ConnectionId))
        {
            return new StartRoundResultDto(
                false,
                "Only the host can head back to the lobby.");
        }

        // 3. Clear the round (roster + code preserved - the room stays live, AC-05).
        room.BackToLobby();

        // 4. Broadcast a bare signal to the whole room group so EVERY player drops
        //    the round/reveal locally and lands back on the Lobby. The roster has not
        //    changed, so there is nothing to carry - the client already holds the
        //    lobby state (RosterChanged keeps it current).
        await Clients.Group(room.Code).SendAsync("BackToLobby");

        return new StartRoundResultDto(true, null);
    }

    /// <summary>
    /// replay-remix/03: the HOST hands the host role to another roster player,
    /// BETWEEN ROUNDS only ("Pass the chisel", AC-01/AC-02).
    ///
    /// HOST-ONLY and SERVER-ENFORCED, reusing the EXACT SAME authorization check
    /// <see cref="StartRound"/>/<see cref="BackToLobby"/> already perform
    /// (<see cref="Room.IsHost"/>) - not a second authorization mechanism (AC-04).
    /// Reuses the friendly <see cref="StartRoundResultDto"/> envelope, exactly as
    /// <see cref="BackToLobby"/> does, rather than a throw:
    ///
    ///   1. Unknown / expired code -> friendly fail (nothing to hand off).
    ///   2. Caller is not the host -> reject (AC-04). Server-authoritative even
    ///      though the client only shows the action to the host.
    ///   3. The room is mid-round (a live round whose phase is "prompting") ->
    ///      reject (AC-05) - a handoff is deliberately between-rounds only. A
    ///      "reveal" phase round (Round Complete's underlying phase) and the
    ///      lobby (no round at all) are both allowed.
    ///   4. <see cref="Room.PassHost"/> flips the flag under the room lock,
    ///      re-verifying the host check atomically and requiring the target to be
    ///      a live roster member; a false result (a race, or an unknown/departed
    ///      target) is a friendly fail.
    ///
    /// On success it broadcasts the EXISTING "RosterChanged" event with the
    /// updated roster (no new broadcast event type, AC-02/AC-03) - the moved
    /// IsHost flag already rides on every PlayerDto, so every client (the new
    /// host, the outgoing host, and everyone else) sees the crown move live.
    /// </summary>
    /// <param name="code">The room's join code (case-insensitive).</param>
    /// <param name="targetNickname">The roster member (by nickname) to become the new host.</param>
    public async Task<StartRoundResultDto> PassHost(string code, string targetNickname)
    {
        // 1. Look up the room first (an unknown / expired code has nothing to hand off).
        var room = _rooms.TryGet(code);
        if (room is null)
        {
            return new StartRoundResultDto(
                false,
                RoomNotFoundMessage);
        }

        // 2. Server-enforced host check (AC-04): only the connection that owns the
        //    room's host player may hand it off. Authoritative even though the
        //    client only shows "Pass the chisel" to the host.
        if (!room.IsHost(Context.ConnectionId))
        {
            return new StartRoundResultDto(
                false,
                "Only the host can pass the chisel.");
        }

        // 3. Between-rounds only (AC-05): a live round still in "prompting" blocks
        //    the handoff. No round (lobby) or a "reveal" round (Round Complete's
        //    underlying phase) are both fine.
        var round = room.CurrentRound;
        if (round is not null && string.Equals(round.Phase, "prompting", StringComparison.Ordinal))
        {
            return new StartRoundResultDto(
                false,
                "The chisel can only pass between rounds, not mid-round.");
        }

        // 4. Flip the flag under the room lock (re-verifies the host + target
        //    atomically). A false result is a race or an unknown/departed target.
        if (!room.PassHost(Context.ConnectionId, targetNickname))
        {
            return new StartRoundResultDto(
                false,
                "That player isn't available to become host right now.");
        }

        // 5. Broadcast the EXISTING RosterChanged event so every client's roster -
        //    including the crown - updates live (AC-02/AC-03). No new event type.
        await Clients.Group(room.Code).SendAsync("RosterChanged", ToRoomState(room));

        return new StartRoundResultDto(true, null);
    }

    /// <summary>
    /// reveal-delight/01 + reactions v2: a player reacts on the reveal (AC-04).
    ///
    /// The lightest-weight room-wide real-time surface: a player taps one of the
    /// THREE reaction pills (Love / Wow / Didn't like) and every player in the room
    /// sees the tally update in near-real-time, over the SAME one connection the
    /// roster and reveal already use. This is FIRE-AND-FORGET from the client's side
    /// (it returns void, not a result envelope) - the tapper's perceived
    /// responsiveness comes from an instant local selection highlight, and the
    /// authoritative counts arrive for everyone (the tapper included) via the
    /// "ReactionCountsChanged" broadcast.
    ///
    /// The UX de-clutter made reactions ONE-PER-USER, which is what fixes the old
    /// count-inflation bug (a player could previously tap a pill repeatedly to run
    /// its count up). The SERVER is now authoritative for that de-dupe: the tapping
    /// connection is passed to <see cref="Room.SetReaction"/>, which SELECTS the
    /// reaction (a first tap), MOVES it (tapping a different pill decrements the old
    /// + increments the new), or TOGGLES it off (tapping the pill you already hold
    /// removes it). So React is idempotent-by-design - a repeat of the same tap
    /// toggles rather than inflates.
    ///
    /// A reaction is a TYPE ENUM only (AC-06): no text and no player identity travel
    /// on the wire, so there is nothing for the safety filter to check and no PII is
    /// collected - the payload is just "someone in this room reacted with X" (the
    /// connection id used for de-dupe is a server-side handle, never broadcast). The
    /// type is validated server-side against the three allowed values (a crafted
    /// client sending anything else is simply ignored, no throw), then the room's
    /// ephemeral per-reveal tally + per-connection hold are updated and the full
    /// updated tally is broadcast to the whole room group. The counts + holds reset
    /// when a new round starts (see <see cref="Room.StartRound"/>) - they are
    /// per-reveal, not persisted.
    ///
    /// An unknown/expired code, or a type outside the allowed set, is a silent no-op
    /// (a stray reaction is harmless) rather than a friendly error envelope - unlike
    /// the word/round methods there is nothing the player needs to retry.
    /// </summary>
    /// <param name="code">The room's join code (case-insensitive).</param>
    /// <param name="reactionType">One of love/wow/nope; anything else is ignored server-side.</param>
    public async Task React(string code, string reactionType)
    {
        // An unknown / expired room has nothing to react to - silently ignore.
        var room = _rooms.TryGet(code);
        if (room is null)
        {
            return;
        }

        // Validate the type against the three allowed values (AC-06). A crafted
        // client sending anything else is ignored - never trust the wire value.
        if (string.IsNullOrWhiteSpace(reactionType) || !KnownReactions.Contains(reactionType))
        {
            return;
        }

        // Apply this connection's single reaction under the room's lock (select /
        // move / toggle off - the server-authoritative one-per-user de-dupe) and get
        // a detached snapshot to broadcast. Lowercase to the canonical key the tally
        // uses (the client already sends lowercase, but normalize defensively).
        var tally = room.SetReaction(Context.ConnectionId, reactionType.ToLowerInvariant());

        // Broadcast the full updated tally to the whole room group so every player
        // (the tapper included) sees the count update in near-real-time (AC-04).
        await Clients.Group(room.Code).SendAsync(
            "ReactionCountsChanged",
            new ReactionCountsDto(tally["love"], tally["wow"], tally["nope"]));
    }

    /// <summary>
    /// reveal-delight/03: a player casts (or MOVES) their Golden Guardian vote for the
    /// funniest coral word on the reveal (AC-01/AC-02).
    ///
    /// Rides the SAME one connection the roster/reveal/reactions use. The payload is
    /// an already-vetted, already-displayed word's blank TOKEN (its body-order
    /// position) - no free text, no identity (AC-07, no PII). Vote state is ephemeral
    /// per-round in-memory on the <see cref="Room"/> (discarded when the round moves
    /// on). Like <see cref="React"/> this is fire-and-forget from the client (no result
    /// envelope): an unknown/expired code, a vote outside the reveal, an already-
    /// resolved vote, or a token that is not one of the room's filled coral words is a
    /// silent no-op (a stray vote needs no retry).
    ///
    /// On a recorded cast it broadcasts "GoldenGuardianVoteCast" (the live "N of M
    /// voted" figures) to the whole room group. When that cast RESOLVES the vote (every
    /// present player has voted) it ALSO broadcasts "GoldenGuardianResolved" with the
    /// winning blank token + the winning contributor's nickname, so every player sees
    /// the same gold winner (AC-03) and the crown is set for the next round (AC-04).
    /// </summary>
    /// <param name="code">The room's join code (case-insensitive).</param>
    /// <param name="blankId">The chosen coral word's blank token (body-order position); anything outside the filled-word set is ignored.</param>
    public async Task CastGoldenGuardianVote(string code, string blankId)
    {
        // An unknown / expired room has nothing to vote on - silently ignore.
        var room = _rooms.TryGet(code);
        if (room is null)
        {
            return;
        }

        var result = room.CastGoldenGuardianVote(Context.ConnectionId, blankId);
        if (!result.Accepted)
        {
            // Not in the reveal, already resolved, not a seated player, or an unknown
            // token - nothing recorded, nothing to broadcast (a stray vote is harmless).
            return;
        }

        // Broadcast the live "N of M voted" status to the whole room group (AC-02).
        await Clients.Group(room.Code).SendAsync(
            "GoldenGuardianVoteCast",
            new GoldenGuardianVoteCastDto(result.VotedCount, result.TotalVoters));

        // If this cast completed the vote, announce the single winner to everyone
        // (AC-03) - the winning word + its contributor (the crown's next-round wearer).
        if (result.Resolved)
        {
            await Clients.Group(room.Code).SendAsync(
                "GoldenGuardianResolved",
                new GoldenGuardianResolvedDto(result.WinningBlankId, result.WinnerNickname));
        }
    }

    /// <summary>
    /// reveal-delight/03: the HOST closes Golden Guardian voting early via the
    /// low-pressure "Reveal the winner" affordance (AC-03), mirroring the "no rush,
    /// but the host can move things along" posture group play already establishes.
    ///
    /// HOST-ONLY and SERVER-ENFORCED: the UI only shows the affordance to the host,
    /// but this method is the authoritative check - a non-host caller is a silent
    /// no-op. Resolves the vote with whatever votes are in (zero votes -> no winner,
    /// no crown) and broadcasts the final "GoldenGuardianVoteCast" + "Golden
    /// GuardianResolved" to the whole room group so every player lands on the same
    /// result. Fire-and-forget from the client (no result envelope); an unknown/expired
    /// code or a non-reveal round is a silent no-op.
    /// </summary>
    /// <param name="code">The room's join code (case-insensitive).</param>
    public async Task CloseGoldenGuardianVoting(string code)
    {
        var room = _rooms.TryGet(code);
        if (room is null)
        {
            return;
        }

        // Server-enforced host check (AC-03): only the host may close voting early.
        if (!room.IsHost(Context.ConnectionId))
        {
            return;
        }

        var result = room.CloseGoldenGuardian();
        if (!result.Accepted)
        {
            // Not in the reveal (nothing to close) - silent no-op.
            return;
        }

        // Push the final "N of M" figures, then the resolved winner, to everyone.
        await Clients.Group(room.Code).SendAsync(
            "GoldenGuardianVoteCast",
            new GoldenGuardianVoteCastDto(result.VotedCount, result.TotalVoters));
        await Clients.Group(room.Code).SendAsync(
            "GoldenGuardianResolved",
            new GoldenGuardianResolvedDto(result.WinningBlankId, result.WinnerNickname));
    }

    /// <summary>
    /// group-play/03: a player submits ONE word for ONE of ITS OWN assigned blanks.
    ///
    /// This is the crux of group play: server-authoritative collection with the
    /// child-safety filter as the ONE gate, plus the reveal broadcast. Validation
    /// runs in a FIXED order and every EXPECTED failure returns a friendly
    /// <see cref="SubmitWordResultDto"/> (Ok=false, kid-readable Error) rather than
    /// throwing, mirroring the other hub envelopes:
    ///
    ///   1. Unknown / expired code, or no round / not in the prompting phase ->
    ///      friendly fail (nothing to collect into).
    ///   2. SAFETY FILTER FIRST (AC-01, AC-06): vet the word server-side BEFORE it
    ///      is recorded. A blocked word returns the filter's friendly message and is
    ///      NEVER recorded. An empty/whitespace word (a SKIP) is allowed by the
    ///      filter and records an empty placeholder, preserving reveal alignment
    ///      (matching the web engine's skipBlank rule).
    ///   3. Record via <see cref="Room.RecordSubmission"/>, which rejects if the
    ///      blank is not THIS connection's (a crafted client cannot fill another
    ///      player's blank, AC-01) -> friendly fail.
    ///   4. Broadcast "CollectProgress" to the whole room group: the per-player
    ///      done/writing list + counts. NEVER the submitted words (AC-01).
    ///   5. If the round is now complete: advance the phase to "reveal", build the
    ///      ORDERED reveal payload, and broadcast "RevealReady" to the room group so
    ///      EVERY player moves to the shared Reveal in near-real-time (AC-05). The
    ///      server does NOT assemble - it ships the ordered words; clients assemble
    ///      locally via the web engine.
    ///
    /// The word is never echoed to other players before the reveal (AC-01): the
    /// only broadcast before completion is progress (done/writing), which carries no
    /// words. On success returns Ok=true / Error=null.
    /// </summary>
    /// <param name="code">The room's join code (case-insensitive).</param>
    /// <param name="blankIndex">The blank index (into the template's ordered blanks) this word fills; must be assigned to this connection.</param>
    /// <param name="word">The player's free-text word (empty for a skip); vetted server-side before recording.</param>
    public async Task<SubmitWordResultDto> SubmitWord(string code, int blankIndex, string word)
    {
        // 1. Look up the room + round. An unknown / expired code, or a room not in a
        //    prompting round, has nothing to collect into - friendly fail.
        var room = _rooms.TryGet(code);
        if (room is null)
        {
            return new SubmitWordResultDto(
                false,
                RoomNotFoundMessage);
        }

        var round = room.CurrentRound;
        if (round is null || !string.Equals(round.Phase, "prompting", StringComparison.Ordinal))
        {
            return new SubmitWordResultDto(
                false,
                "This round isn't taking words right now - hang tight.");
        }

        // 2. SAFETY FILTER FIRST (AC-01, AC-06): vet the word BEFORE recording. A
        //    blocked word is never stored or shown. An empty/whitespace word (a
        //    skip) passes the filter and records an empty placeholder to keep the
        //    reveal aligned (the web engine's skipBlank rule, server-authoritative).
        var candidate = word ?? string.Empty;
        var verdict = await _safety.CheckAsync(candidate, Context.ConnectionAborted);
        if (!verdict.IsAllowed)
        {
            // Never record; hand back the filter's friendly retry message so the
            // player can try another word (FillBlank shows it inline).
            return new SubmitWordResultDto(false, verdict.Message);
        }

        // 3. Record AUTHORITATIVELY under the room lock. RecordSubmission rejects if
        //    the blank is not this connection's own (AC-01) or the round moved past
        //    prompting since our snapshot above (a late submission racing the
        //    reveal).
        var outcome = room.RecordSubmission(Context.ConnectionId, blankIndex, candidate);
        if (outcome == Room.SubmitOutcome.Rejected)
        {
            return new SubmitWordResultDto(
                false,
                "That word can't be placed here - please try again.");
        }

        // 4. Broadcast progress to the whole room group (AC-03). NEVER the words
        //    (AC-01) - only who is done and who is still writing.
        var (doneCount, playerCount) = room.GetProgressCounts();
        var progress = room.GetProgress()
            .Select(p => new PlayerProgressDto(p.Nickname, p.Variant, p.Done))
            .ToArray();
        await Clients.Group(room.Code).SendAsync(
            "CollectProgress",
            new CollectProgressDto(doneCount, playerCount, progress));

        // 5. If that was the last outstanding blank, transition to the reveal for
        //    EVERYONE (AC-05). Build the ordered payload and broadcast it; clients
        //    resolve the template + assemble locally (the server never assembles).
        if (outcome == Room.SubmitOutcome.RoundComplete)
        {
            // RecordSubmission already advanced the phase to "reveal" atomically
            // under the room lock when it saw the last blank land - so a late
            // submission racing this reveal is rejected by the prompting-phase
            // guard, and only this one call builds/broadcasts the reveal.
            var words = room.BuildReveal()
                .Select(w => new RevealWordDto(w.Word, w.Nickname, w.Variant))
                .ToArray();
            await Clients.Group(room.Code).SendAsync(
                "RevealReady",
                new RevealReadyDto(round.TemplateId, words));

            // platform-devops/05 (AC-02): the reveal just fired, so the round is
            // complete - record the anonymous "RoundCompleted" usage event with the
            // round DURATION (now minus the round's StartedUtc, captured under the
            // lock in StartRound). Fire-and-forget on 04's App Insights pipeline;
            // never blocks or faults the reveal (AC-08), no per-person identity (AC-04).
            var durationMs = (DateTimeOffset.UtcNow - round.StartedUtc).TotalMilliseconds;
            TrackUsageRoundCompleted(round, durationMs);
        }

        return new SubmitWordResultDto(true, null);
    }

    /// <summary>
    /// replay-remix/02: re-reveal the SAME finished tale with ONE blank re-collected
    /// (issue #61, "One-blank remix of a finished tale"), then broadcast the freshly
    /// re-assembled reveal to EVERYONE (AC-07, a shared moment - not a private edit
    /// only the remixer sees). This is a thin SIBLING of <see cref="SubmitWord"/>,
    /// reusing the SAME two-step shape (safety check, then an authoritative room
    /// mutation) rather than a new subsystem:
    ///
    ///   1. SAFETY FILTER FIRST (AC-06), exactly like <see cref="SubmitWord"/>: a
    ///      blocked word never gets recorded or shown, and the caller gets the
    ///      filter's friendly retry message back.
    ///   2. <see cref="Room.RemixSubmission"/> is the ONE authoritative mutation:
    ///      it validates the round is in the "reveal" phase (not mid-collection),
    ///      that <paramref name="blankIndex"/> is in range, and that the caller is a
    ///      LIVE ROOM MEMBER - deliberately NO host guard here (2026-07-04
    ///      Decisions-log call: ANY player may remix, since the whole point is
    ///      "swap the one word that made YOU laugh").
    ///   3. On success, rebuild the ORDERED reveal payload (the SAME
    ///      <see cref="Room.BuildReveal"/> projection <see cref="SubmitWord"/>
    ///      already uses) and re-broadcast the EXISTING "RevealReady" event - every
    ///      client's already-wired RevealReady handler updates its `reveal` state
    ///      and re-renders through the SAME unmodified Reveal screen, no new client
    ///      event needed.
    ///
    /// Never changes the round's template, blank count, or phase - a remix is a
    /// same-tale, one-word swap only.
    /// </summary>
    /// <param name="code">The room's join code (case-insensitive).</param>
    /// <param name="blankIndex">The blank index (into the template's ordered blanks) to remix.</param>
    /// <param name="word">The new free-text word for that one blank; vetted server-side before recording.</param>
    public async Task<SubmitWordResultDto> RemixWord(string code, int blankIndex, string word)
    {
        var room = _rooms.TryGet(code);
        if (room is null)
        {
            return new SubmitWordResultDto(
                false,
                RoomNotFoundMessage);
        }

        // SAFETY FILTER FIRST (AC-06), exactly like SubmitWord: a blocked word is
        // never recorded or shown, regardless of who is remixing.
        var candidate = word ?? string.Empty;
        var verdict = await _safety.CheckAsync(candidate, Context.ConnectionAborted);
        if (!verdict.IsAllowed)
        {
            return new SubmitWordResultDto(false, verdict.Message);
        }

        var outcome = room.RemixSubmission(Context.ConnectionId, blankIndex, candidate);
        if (outcome != Room.RemixOutcome.Recorded)
        {
            // Both expected-rejection cases (no live reveal to remix into, or a
            // caller who is not a seated room member) get the same friendly retry
            // message - neither leaks which case it was, mirroring the other hub
            // envelopes' posture of not distinguishing "why" beyond kid-readable copy.
            return new SubmitWordResultDto(
                false,
                "That word can't be remixed right now - please try again.");
        }

        // Rebuild + re-broadcast the SAME "RevealReady" event SubmitWord uses (AC-07):
        // every client's already-wired handler updates `reveal` and re-renders through
        // the unmodified Reveal screen - no new client event, no second broadcast type.
        var round = room.CurrentRound;
        if (round is null)
        {
            // Defensive: RemixSubmission only recorded because a "reveal"-phase round
            // existed a moment ago under the same lock, but re-read it fresh here in
            // case something raced it away (e.g. BackToLobby) between the two calls.
            return new SubmitWordResultDto(
                false,
                "That word can't be remixed right now - please try again.");
        }

        var words = room.BuildReveal()
            .Select(w => new RevealWordDto(w.Word, w.Nickname, w.Variant))
            .ToArray();
        await Clients.Group(room.Code).SendAsync(
            "RevealReady",
            new RevealReadyDto(round.TemplateId, words));

        return new SubmitWordResultDto(true, null);
    }

    /// <summary>
    /// group-play recovery: shared teardown when a player leaves a room. The
    /// instance wrapper for the LeaveRoom (deliberate) path - it broadcasts through
    /// this invocation's own <see cref="Hub.Clients"/>. A null room (the leaver was
    /// not seated, or leaving emptied and dropped the room) is a no-op.
    /// </summary>
    /// <param name="room">The room the leaver was seated in, or null if it was a no-op.</param>
    /// <param name="leavingConnectionId">
    /// B3 (alpha-gate hardening): the connection that just left, so
    /// <see cref="BroadcastPlayerLeftAsync"/> can tell whether it still owed any
    /// blanks before deciding whether a prompting round must abort.
    /// </param>
    /// <param name="promotedHostConnectionId">The connection promoted to host by this departure, if any.</param>
    private Task HandlePlayerLeftAsync(Room? room, string leavingConnectionId, string? promotedHostConnectionId = null) =>
        room is null ? Task.CompletedTask : BroadcastPlayerLeftAsync(Clients, room, leavingConnectionId, promotedHostConnectionId);

    /// <summary>
    /// group-play recovery: the SHARED player-left epilogue, broadcast to
    /// <paramref name="clients"/>. If the room still has members and its round is
    /// mid-collection ("prompting"), the departed player's OWN assigned blanks decide
    /// whether that round can still complete:
    ///   - B3 (alpha-gate hardening): if <paramref name="leavingConnectionId"/> had
    ///     already submitted everything it owed (or was dealt no blanks at all), its
    ///     words are already recorded and the remaining players can still fill out
    ///     every other blank - so the round is left running, exactly as if this
    ///     departure had never happened.
    ///   - Otherwise it still owed at least one blank that will now never arrive, so
    ///     the round can no longer complete and the reveal would never fire - reset
    ///     the round and broadcast "RoundAborted" so every remaining player drops
    ///     back to the still-live lobby with a friendly notice, exactly as before
    ///     this fix.
    /// Either way the trimmed roster is always re-broadcast afterward. A round already
    /// at "reveal" is effectively done (everyone is on the reveal / recap), so it is
    /// left untouched regardless.
    ///
    /// Taking <see cref="IHubClients"/> (rather than reading this hub's own
    /// <see cref="Hub.Clients"/>) is what lets BOTH the live LeaveRoom path (passing
    /// this invocation's <c>Clients</c>) AND the session-engine/07 grace-expiry path
    /// (passing <c>IHubContext&lt;GameHub&gt;.Clients</c>, since the originating hub
    /// invocation has long since ended) run the EXACT same eviction epilogue - no
    /// forked, mode-specific reconnect logic ("one engine, many thin modes"). The same
    /// sharing is why the B3 conditional-abort check lives HERE rather than being
    /// duplicated per call site.
    ///
    /// room-start-duplicate-members: host migration now runs INSIDE the seat removal
    /// (Room.EnsureHostLocked, on both the RemovePlayer and TryReleaseSeat paths that
    /// feed this epilogue), so if the departing seat was the host a remaining seat has
    /// already inherited the flag by the time we broadcast. The targeted "HostGranted"
    /// below tells that promoted connection so its client can start.
    /// </summary>
    internal static async Task BroadcastPlayerLeftAsync(
        IHubClients<IClientProxy> clients,
        Room room,
        string leavingConnectionId,
        string? promotedHostConnectionId = null)
    {
        var round = room.CurrentRound;
        if (round is not null &&
            string.Equals(round.Phase, "prompting", StringComparison.Ordinal) &&
            RoundHasOutstandingBlanksFor(round, leavingConnectionId))
        {
            room.BackToLobby();
            await clients.Group(room.Code).SendAsync(
                "RoundAborted",
                new RoundAbortedDto("A carver left, so we headed back to the lobby. Start a fresh round when your crew's ready."));
        }

        await clients.Group(room.Code).SendAsync("RosterChanged", ToRoomState(room));

        // room-start-duplicate-members: if this leave migrated the host flag to a remaining
        // seat (Room.EnsureHostLocked runs inside the seat removal on BOTH the deliberate-
        // leave and grace-eviction paths that land here), nudge ONLY that promoted
        // connection so its client flips its host-only Start CTA on. The roster DTO is
        // anonymous - it carries no connection identity - so it can never tell a specific
        // client "you are the host"; without this targeted message the promoted player
        // would sit in a hosted room with no way to start (the dead-end this story fixes).
        // Null (a non-host left, so the host is unchanged) sends nothing.
        if (promotedHostConnectionId is not null)
        {
            await clients.Client(promotedHostConnectionId).SendAsync("HostGranted");
        }
    }

    /// <summary>
    /// B3 (alpha-gate hardening): whether the departing connection's OWN assigned
    /// blanks - if it was dealt any - still had at least one unsubmitted at the
    /// moment it left <paramref name="round"/>. Mirrors the exact per-player "done"
    /// rule <see cref="Room.GetProgress"/> uses (an assignment's blanks are ALL
    /// present in the submission store), just narrowed to the one departing
    /// connection rather than every player. A connection with no assignment on
    /// record (dealt zero blanks - fewer blanks than players) owed nothing, so it
    /// reports false, same as one that had already submitted everything.
    /// </summary>
    /// <param name="round">The room's current round snapshot (assignments + submissions).</param>
    /// <param name="leavingConnectionId">The connection that just left.</param>
    /// <returns>True if that connection still owed at least one unsubmitted blank.</returns>
    private static bool RoundHasOutstandingBlanksFor(RoundState round, string leavingConnectionId)
    {
        var assignment = round.Assignments.FirstOrDefault(a => a.ConnectionId == leavingConnectionId);
        return assignment is not null && !assignment.BlankIndices.All(round.Submissions.ContainsKey);
    }

    /// <summary>
    /// session-engine/05: normalize a client-supplied Guardian variant string
    /// to one of the six known values (case-insensitive), defaulting to
    /// "teal" for null, empty, or unrecognized input. Keeps the lowercase
    /// canonical form on the wire regardless of how the client cased it.
    /// </summary>
    private static string NormalizeVariant(string? variant)
    {
        if (string.IsNullOrWhiteSpace(variant) || !KnownVariants.Contains(variant))
        {
            return "teal";
        }

        return variant.ToLowerInvariant();
    }

    /// <summary>
    /// story-selection/02: normalize a client-supplied length preference string to
    /// one of the three known values (case-insensitive), defaulting to
    /// <see cref="DefaultLengthPreference"/> ("any") for null, empty, or
    /// unrecognized input. Mirrors <see cref="NormalizeVariant"/>'s defensive
    /// posture: a malformed client can only ever widen toward "no length
    /// filtering", never toward anything that bypasses the family-safe gate
    /// (that gate always runs first, unconditionally - AC-05).
    /// </summary>
    private static string NormalizeLengthPreference(string? lengthPref)
    {
        if (string.IsNullOrWhiteSpace(lengthPref) || !KnownLengthPreferences.Contains(lengthPref))
        {
            return DefaultLengthPreference;
        }

        return lengthPref.ToLowerInvariant();
    }

    // Map the in-memory Room to the wire DTO (drops the server-only connectionId AND
    // the server-only reconnect token - session-engine/07 AC-06: the token is NEVER on
    // the roster the whole room receives). The Connected flag DOES ride along (web
    // story 10 renders the "reconnecting" tile from it).
    private static RoomStateDto ToRoomState(Room room)
    {
        var players = room.SnapshotPlayers()
            .Select(p => new PlayerDto(p.Nickname, p.Variant, p.IsHost, p.Connected))
            .ToArray();

        return new RoomStateDto(room.Code, players);
    }
}
