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

public sealed class GameHub : Hub
{
    private readonly RoomRegistry _rooms;

    public GameHub(RoomRegistry rooms)
    {
        _rooms = rooms;
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

    // Map the in-memory Room to the wire DTO (drops the server-only connectionId).
    private static RoomStateDto ToRoomState(Room room)
    {
        var players = room.SnapshotPlayers()
            .Select(p => new PlayerDto(p.Nickname, p.Variant, p.IsHost))
            .ToArray();

        return new RoomStateDto(room.Code, players);
    }
}
