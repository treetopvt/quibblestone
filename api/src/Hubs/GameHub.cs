// ----------------------------------------------------------------------------
//  GameHub - the single SignalR hub for QuibbleStone real-time play.
//
//  Real-time is first-class in QuibbleStone: lobby, presence, live session
//  state, and reveal broadcast all ride on SignalR (README section 4). This one
//  hub grows story by story on the SAME connection - never a second hub - so
//  every game feature (rooms, rosters, word collection, reveal) shares it.
//
//  What lives here today:
//    - Ping        : the original walking-skeleton round-trip echo.
//    - CreateRoom  : session-engine/01. Mints an ephemeral in-memory room with
//                    a unique, human-friendly join code, adds the caller as the
//                    host (first player), joins them to the room's SignalR group
//                    (so future roster broadcasts reach them), and returns the
//                    created room's state to the caller.
//    - JoinRoom    : session-engine/02. Joins an existing room by code with a
//                    safety-checked display name (no PII), enforces in-room name
//                    uniqueness, and broadcasts the updated roster to the room
//                    group as "RosterChanged" so everyone sees the newcomer live.
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
using QuibbleStone.Api.Rooms;
using QuibbleStone.Api.Safety;

namespace QuibbleStone.Api.Hubs;

/// <summary>
/// One player as seen on the wire. Anonymous by design (no PII): an in-session
/// nickname, a Guardian variant, and whether they are the host. The
/// connectionId is intentionally NOT exposed to clients (it is a server-side
/// handle used for leave-detection in story 03).
/// </summary>
/// <param name="Nickname">In-session display name (empty for the host until story 02 adds a name step).</param>
/// <param name="Variant">Guardian avatar variant ("teal" default for the host).</param>
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

    private readonly RoomRegistry _rooms;
    private readonly IContentSafetyFilter _safety;

    public GameHub(RoomRegistry rooms, IContentSafetyFilter safety)
    {
        _rooms = rooms;
        _safety = safety;
    }

    // Invoked by the client; returns the echoed message to the calling client.
    public Task<string> Ping(string message)
    {
        return Task.FromResult($"pong: {message}");
    }

    /// <summary>
    /// session-engine/01: create a room and become its host.
    ///
    /// Mints an ephemeral room with a unique, human-friendly join code (AC-02,
    /// AC-03), adds the caller as the host / first player (AC-01), subscribes
    /// the caller's connection to the room's SignalR group (named by the code)
    /// so future roster broadcasts reach them, and returns the created room's
    /// state (code + roster) to the caller so the web client can land the host
    /// in the lobby (AC-01, AC-04).
    /// </summary>
    public async Task<RoomStateDto> CreateRoom()
    {
        var room = _rooms.CreateRoom(Context.ConnectionId);

        // Subscribe the host's connection to the room group so later stories'
        // roster/round broadcasts (Clients.Group(room.Code)) reach them.
        await Groups.AddToGroupAsync(Context.ConnectionId, room.Code);

        return ToRoomState(room);
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

    // Map the in-memory Room to the wire DTO (drops the server-only connectionId).
    private static RoomStateDto ToRoomState(Room room)
    {
        var players = room.SnapshotPlayers()
            .Select(p => new PlayerDto(p.Nickname, p.Variant, p.IsHost))
            .ToArray();

        return new RoomStateDto(room.Code, players);
    }
}
