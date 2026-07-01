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

using Microsoft.AspNetCore.SignalR;
using QuibbleStone.Api.Content;
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
public sealed record PlayerDto(string Nickname, string Variant, bool IsHost);

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
public sealed record JoinResultDto(bool Ok, RoomStateDto? Room, string? Error);

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
public sealed record CreateRoomResultDto(bool Ok, RoomStateDto? Room, string? Error);

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
/// <param name="Mode">The play mode ("classic-blind" for Slice 1).</param>
/// <param name="RoundNumber">1-based round number; group-play/04 increments it on replay (2, 3, ...), and it drives the Round Complete "ROUND N CARVED" badge.</param>
public sealed record RoundStartedDto(string TemplateId, string Mode, int RoundNumber);

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

public sealed class GameHub : Hub
{
    // The largest a display name may be (AC-03). Kept in sync with the web
    // client's "n/14" counter (web/src/pages/Join.tsx). Names are trimmed first.
    private const int MaxDisplayNameLength = 14;

    // session-engine/05: the only six Guardian variants the client can offer
    // (web/src/components/Guardian.tsx GuardianVariant). A malformed or
    // malicious client could send any string as `variant`, so this is the
    // server-side source of truth - never trust the wire value directly.
    private static readonly HashSet<string> KnownVariants = new(StringComparer.OrdinalIgnoreCase)
    {
        "purple", "gold", "coral", "teal", "sand", "plum",
    };

    // group-play/01: the only mode in Slice 1 (README section 7 - Classic blind
    // only). The wire value the round carries and the client resolves against its
    // Classic-blind mode config. A mode PICKER is out of scope here.
    private const string ClassicBlindMode = "classic-blind";

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

    private readonly RoomRegistry _rooms;
    private readonly IContentSafetyFilter _safety;
    private readonly TemplateCatalog _catalog;
    private readonly FamilySafeContentSelector _familySafe;
    private readonly LengthContentSelector _length;
    private readonly ITelemetrySink _telemetry;
    private readonly ILogger<GameHub> _logger;

    public GameHub(
        RoomRegistry rooms,
        IContentSafetyFilter safety,
        TemplateCatalog catalog,
        FamilySafeContentSelector familySafe,
        LengthContentSelector length,
        ITelemetrySink telemetry,
        ILogger<GameHub> logger)
    {
        _rooms = rooms;
        _safety = safety;
        _catalog = catalog;
        _familySafe = familySafe;
        _length = length;
        // story-selection/04: the anonymous serve-log sink. Fired fire-and-forget
        // at the END of a group round start (never awaited on the round-start path,
        // AC-03). NoOp locally, Table Storage in a configured environment (AC-05).
        _telemetry = telemetry;
        _logger = logger;
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

        // Subscribe the host's connection to the room group so later stories'
        // roster/round broadcasts (Clients.Group(room.Code)) reach them.
        await Groups.AddToGroupAsync(Context.ConnectionId, room.Code);

        return new CreateRoomResultDto(true, ToRoomState(room), null);
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
                "We couldn't find a game with that code - double-check and try again.");
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

        // 4. Add the (now-vetted) player under the room lock, which also enforces
        //    in-room name uniqueness case-insensitively (AC-06).
        if (!room.TryAddPlayer(name, chosenVariant, Context.ConnectionId))
        {
            return new JoinResultDto(false, null, "That name is taken in this room - try another.");
        }

        // Subscribe this connection to the room group so it receives future
        // roster/round broadcasts, then broadcast the new roster to everyone in
        // the room (host + existing players + the joiner) in near-real-time (AC-05).
        await Groups.AddToGroupAsync(Context.ConnectionId, room.Code);
        await Clients.Group(room.Code).SendAsync("RosterChanged", ToRoomState(room));

        return new JoinResultDto(true, ToRoomState(room), null);
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
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var room = _rooms.RemoveConnection(Context.ConnectionId);
        await HandlePlayerLeftAsync(room);

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

        var room = _rooms.RemoveConnection(Context.ConnectionId);
        await HandlePlayerLeftAsync(room);
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
    ///      <see cref="LengthContentSelector"/>, then auto-pick one at random from
    ///      the allowed subset (no picker UI in Slice 1). If somehow nothing is
    ///      allowed after the family-safe gate, friendly fail rather than throw; an
    ///      empty LENGTH pool instead degrades to the family-safe pool (AC-06 - see
    ///      Stage 2 below).
    ///
    /// On success it sets the room's round state (round 1, the chosen template,
    /// Classic blind, "prompting") and broadcasts "RoundStarted" to the whole room
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
    public async Task<StartRoundResultDto> StartRound(string code, bool familySafe, string lengthPref)
    {
        // 1. Look up the room first (an unknown / expired code has nothing to start).
        var room = _rooms.TryGet(code);
        if (room is null)
        {
            return new StartRoundResultDto(
                false,
                "We couldn't find a game with that code - double-check and try again.");
        }

        // 2. Server-enforced host check (AC-03): only the connection that owns the
        //    room's host player may start a round. This is authoritative even
        //    though the client also hides the CTA from non-hosts.
        if (!room.IsHost(Context.ConnectionId))
        {
            return new StartRoundResultDto(
                false,
                "Only the host can start the game.");
        }

        // 3. Need the host plus at least one other player (AC-01).
        if (room.PlayerCount < 2)
        {
            return new StartRoundResultDto(
                false,
                "You need at least one more carver before you can start.");
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

        //    Stage 2 - LENGTH FILTER + empty-pool fallback (AC-06): narrow the
        //    family-safe pool to the requested length class using the host's
        //    story-length choice (story-selection/02, AC-03). A malformed/unknown
        //    lengthPref is normalized to "any" first so a crafted client cannot
        //    break the pick. If the requested length would leave an EMPTY pool, the
        //    selector DEGRADES to the family-safe pool rather than failing the round
        //    (a longer story, never an error) - the fallback lives in THIS pipeline,
        //    not in callers.
        var pool = _length.SelectByLengthOrFallback(familySafePool, NormalizeLengthPreference(lengthPref));

        //    Stage 3 - RANDOM PICK from the final pool (no picker UI in Slice 1).
        var chosen = pool[Random.Shared.Next(pool.Count)];

        // 5. Set the room's round state (round 1, Classic blind, "prompting") AND
        //    compute the round-robin blank assignment (group-play/02). The deal
        //    happens under the room lock inside StartRound, using the template's
        //    BlankCount from the catalog, so it is atomic with the roster snapshot
        //    it deals to (a join/leave racing this lands fully before or after the
        //    deal, never mid-deal). The C# deal MIRRORS web/src/engine/distribute.ts.
        var round = room.StartRound(chosen.Id, ClassicBlindMode, chosen.BlankCount);

        // 6. Broadcast to the WHOLE room group (host included) so all players
        //    transition into word collection together (AC-01, AC-02). Full
        //    template content is resolved client-side from seedLibrary by id - the
        //    server ships only the id + mode + round number.
        await Clients.Group(room.Code).SendAsync(
            "RoundStarted",
            new RoundStartedDto(round.TemplateId, round.Mode, round.RoundNumber));

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

        return new StartRoundResultDto(true, null);
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
                "We couldn't find a game with that code - double-check and try again.");
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
                "We couldn't find a game with that code - double-check and try again.");
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
        }

        return new SubmitWordResultDto(true, null);
    }

    /// <summary>
    /// group-play recovery: shared teardown when a player leaves a room (via a
    /// dropped connection or a deliberate LeaveRoom). If the room still has members
    /// and its round is mid-collection ("prompting"), that round can no longer
    /// complete - the departed player's assigned blanks will never be submitted, so
    /// the reveal would never fire and the survivors would hang. Reset the round and
    /// broadcast "RoundAborted" so every remaining player drops back to the still-live
    /// lobby with a friendly notice, then always re-broadcast the trimmed roster. A
    /// round already at "reveal" is effectively done (everyone is on the reveal /
    /// recap), so it is left untouched. Small recovery beyond Slice-1's parked
    /// reconnect handling; host migration (if the HOST leaves) is still parked.
    /// </summary>
    private async Task HandlePlayerLeftAsync(Room? room)
    {
        if (room is null)
        {
            return;
        }

        var round = room.CurrentRound;
        if (round is not null && string.Equals(round.Phase, "prompting", StringComparison.Ordinal))
        {
            room.BackToLobby();
            await Clients.Group(room.Code).SendAsync(
                "RoundAborted",
                new RoundAbortedDto("A carver left, so we headed back to the lobby. Start a fresh round when your crew's ready."));
        }

        await Clients.Group(room.Code).SendAsync("RosterChanged", ToRoomState(room));
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

    // Map the in-memory Room to the wire DTO (drops the server-only connectionId).
    private static RoomStateDto ToRoomState(Room room)
    {
        var players = room.SnapshotPlayers()
            .Select(p => new PlayerDto(p.Nickname, p.Variant, p.IsHost))
            .ToArray();

        return new RoomStateDto(room.Code, players);
    }
}
