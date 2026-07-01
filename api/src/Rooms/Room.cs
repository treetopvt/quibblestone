// ----------------------------------------------------------------------------
//  Room + Player - the ephemeral in-memory room model (session-engine/01).
//
//  QuibbleStone is a toy, not a system of record (README section 4, CLAUDE.md
//  section 10): a room lives only in the memory of the in-process SignalR hub
//  for the duration of a play session. There is NO database and NO durable
//  persistence - when the process restarts, rooms are gone, and that is fine.
//
//  A Room carries:
//    - Code       : the short human-friendly join code (4 chars, unambiguous
//                   alphabet - generated + kept unique by RoomRegistry).
//    - Players    : the roster. The host is the first player (IsHost = true);
//                   later stories (02 join, 05 avatar) append joiners.
//    - LastActive : a sliding-window timestamp bumped on activity. RoomRegistry
//                   sweeps rooms idle past the inactivity window (AC-05).
//
//  A Player is the anonymous, minimal record the charter allows (README
//  sections 3 and 6 - no PII): a Nickname, a Guardian Variant, the owning
//  SignalR ConnectionId (so later stories can find the player on leave), and
//  the IsHost flag. Story 01 only ever creates the host player; story 02 fills
//  in real joiner nicknames (routed through the safety filter first), and story
//  05 lets joiners pick a Variant. The host defaults to the "teal" variant.
//
//  Concurrency: SignalR invokes can run concurrently across connections, so the
//  Players list is guarded by a per-room lock (see the mutation helpers). The
//  registry owns cross-room concurrency (the room dictionary).
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Rooms;

/// <summary>
/// One anonymous player in a room. Minimal by design (no PII, README section 6):
/// just an in-session nickname, a Guardian variant, the owning connection, and
/// whether this player is the host.
/// </summary>
/// <param name="Nickname">In-session display name (host has none yet in story 01 - see <see cref="Room.CreateHosted"/>).</param>
/// <param name="Variant">Guardian avatar variant (defaults to "teal" for the host; joiners pick in story 05).</param>
/// <param name="ConnectionId">The SignalR connection that owns this player - used for leave-detection in story 03.</param>
/// <param name="IsHost">True for the room creator; false for joiners.</param>
public sealed record Player(
    string Nickname,
    string Variant,
    string ConnectionId,
    bool IsHost);

/// <summary>
/// An ephemeral, in-memory game room. Not persisted anywhere (CLAUDE.md
/// section 10): it lives in the <see cref="RoomRegistry"/> only while active.
/// </summary>
public sealed class Room
{
    // Guards the mutable roster - SignalR invokes on different connections can
    // touch the same room concurrently.
    private readonly object _gate = new();
    private readonly List<Player> _players = [];

    private Room(string code)
    {
        Code = code;
        LastActiveUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>The short, human-friendly join code (4 chars, unambiguous alphabet).</summary>
    public string Code { get; }

    /// <summary>
    /// Last time this room saw activity. Bumped by <see cref="Touch"/>; the
    /// registry sweeps rooms idle past the inactivity window (AC-05).
    /// </summary>
    public DateTimeOffset LastActiveUtc { get; private set; }

    /// <summary>
    /// Creates a room with the given code and its host as the first player.
    /// The host has no nickname yet in story 01 (there is no name input until
    /// story 02) and defaults to the "teal" Guardian variant.
    /// </summary>
    public static Room CreateHosted(string code, string hostConnectionId)
    {
        var room = new Room(code);
        room._players.Add(new Player(
            Nickname: string.Empty,
            Variant: "teal",
            ConnectionId: hostConnectionId,
            IsHost: true));
        return room;
    }

    /// <summary>Bumps the sliding-window activity timestamp (AC-05).</summary>
    public void Touch()
    {
        lock (_gate)
        {
            LastActiveUtc = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Attempts to add a joiner to the roster (session-engine/02). The nickname
    /// must be unique within the room, compared case-insensitively (AC-06), so
    /// "Sam" and "sam" cannot both be in the same room. The uniqueness check and
    /// the append happen together under the room lock, so two joiners racing for
    /// the same name cannot both slip in.
    ///
    /// The caller (the hub) is responsible for having already routed the nickname
    /// through the content-safety filter (README section 6) BEFORE calling this -
    /// this method takes the name as-given and never vets it. It only enforces the
    /// in-room uniqueness invariant and appends the player.
    /// </summary>
    /// <param name="nickname">The vetted, trimmed in-session display name.</param>
    /// <param name="variant">The chosen Guardian variant.</param>
    /// <param name="connectionId">The joiner's SignalR connection.</param>
    /// <returns>True if the player was added; false if the name is already taken.</returns>
    public bool TryAddPlayer(string nickname, string variant, string connectionId)
    {
        lock (_gate)
        {
            var taken = _players.Any(p =>
                string.Equals(p.Nickname, nickname, StringComparison.OrdinalIgnoreCase));
            if (taken)
            {
                return false;
            }

            _players.Add(new Player(
                Nickname: nickname,
                Variant: variant,
                ConnectionId: connectionId,
                IsHost: false));

            LastActiveUtc = DateTimeOffset.UtcNow;
            return true;
        }
    }

    /// <summary>
    /// Removes the player owning the given connection from the roster
    /// (session-engine/03 leave-detection). Called when a connection drops
    /// (the app is closed, the tab navigates away, or the network dies) so the
    /// departed player's tile reverts to an empty slot on every remaining
    /// client (AC-04). The removal and the emptiness check the caller makes
    /// afterwards both run under the room lock via this method + <see
    /// cref="IsEmpty"/>, so a concurrent leave cannot corrupt the roster.
    ///
    /// A connection is only ever seated once in a room (Slice 1), so at most one
    /// player is removed. Removing a connection that is not in this room is a
    /// no-op that returns false.
    /// </summary>
    /// <param name="connectionId">The SignalR connection that dropped.</param>
    /// <returns>True if a player was removed; false if the connection was not seated here.</returns>
    public bool RemovePlayer(string connectionId)
    {
        lock (_gate)
        {
            var removed = _players.RemoveAll(p => p.ConnectionId == connectionId);
            if (removed == 0)
            {
                return false;
            }

            LastActiveUtc = DateTimeOffset.UtcNow;
            return true;
        }
    }

    /// <summary>
    /// True when the roster is empty (the last player has left). The registry
    /// uses this after a <see cref="RemovePlayer"/> to drop an abandoned room
    /// immediately rather than waiting for the idle sweep (session-engine/03).
    /// </summary>
    public bool IsEmpty
    {
        get
        {
            lock (_gate)
            {
                return _players.Count == 0;
            }
        }
    }

    /// <summary>A point-in-time snapshot of the roster (safe to hand out; a copy).</summary>
    public IReadOnlyList<Player> SnapshotPlayers()
    {
        lock (_gate)
        {
            return _players.ToArray();
        }
    }
}
