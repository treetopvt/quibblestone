// ----------------------------------------------------------------------------
//  RoomRegistry - the ephemeral in-memory home for all active rooms
//  (session-engine/01).
//
//  This is the process-wide store the SignalR hub uses to create and look up
//  rooms. It is registered as a SINGLETON in Program.cs so every transient
//  GameHub instance (SignalR builds a new hub per invocation) shares the SAME
//  registry and therefore the same set of rooms. There is NO database: rooms
//  live only here, in memory, for the length of a play session (README section
//  4, CLAUDE.md section 10 - QuibbleStone is a toy, not a system of record).
//
//  Responsibilities:
//    - CreateRoom  : mint a room with a fresh, unique join code and its host as
//                    the first player (AC-01).
//    - Code gen    : 4 characters from an UNAMBIGUOUS alphabet - excludes the
//                    look-alike glyphs O, 0, I, 1, l - regenerated on collision
//                    so every active room's code is unique (AC-02, AC-03).
//    - Lookup      : find a room by code (story 02 join builds on this).
//    - Expiry      : rooms idle past an inactivity window are swept out so the
//                    registry does not grow without bound (AC-05). The sweep is
//                    lazy (runs on access) - no background timer needed for the
//                    toy scale, which keeps this simple.
//
//  Thread-safety: SignalR can invoke hub methods concurrently across
//  connections, so the underlying store is a ConcurrentDictionary and code
//  minting retries on the rare collision.
// ----------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace QuibbleStone.Api.Rooms;

/// <summary>
/// session-engine/07 (hold the seat): the handle a grace-holding disconnect hands to
/// the grace scheduler so it can run (and later resolve) the one-shot delayed
/// eviction. Carries the affected <see cref="Room"/>, the dropped connection id, the
/// disconnect <see cref="Episode"/> (so a stale timer cannot evict a since-reconnected
/// seat), and the <see cref="Token"/> the delay waits on (story 08's Rejoin cancels
/// its source to keep the seat). It is a transient scheduling handle, never persisted
/// or broadcast.
/// </summary>
public sealed record SeatGraceHandle(Room Room, string ConnectionId, Guid Episode, CancellationToken Token);

public sealed class RoomRegistry
{
    // Unambiguous code alphabet: A-Z and 2-9 with the look-alike glyphs removed
    // (O, 0, I, 1, and lowercase l) so a code is easy to read aloud and type
    // (AC-02). All uppercase - codes are compared and displayed uppercase.
    private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 4;

    // How long a room may sit idle before it is eligible for expiry (AC-05). A
    // sliding ~30 minute window: generous enough for a lull between rounds, but
    // bounded so abandoned rooms do not accumulate. This is a toy, so a simple
    // lazy sweep on access is sufficient - no background service.
    private static readonly TimeSpan InactivityWindow = TimeSpan.FromMinutes(30);

    // Bound the collision-retry loop. With a 31-char alphabet ^ 4 (~923k codes)
    // this is astronomically more than enough headroom for a family word game;
    // the guard just prevents an unbounded loop in a pathological case.
    private const int MaxCodeAttempts = 50;

    // Key = uppercase room code. ConcurrentDictionary because hub invocations on
    // different connections can create/look up rooms at the same time.
    private readonly ConcurrentDictionary<string, Room> _rooms = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a new room with a fresh unique code and the given connection as
    /// its host (the first player). Sweeps expired rooms first so a stale code
    /// can be reused and the store stays bounded (AC-05).
    ///
    /// build/host-identity: the host's display name + Guardian variant are threaded
    /// through so the host player is seated with the REAL name + variant it picked
    /// on HostSetup (mirroring the joiner name step) instead of an empty nickname +
    /// the default "teal". The name has ALREADY been trimmed, length-checked, and
    /// vetted by the content-safety filter by the caller (the hub's CreateRoom), and
    /// the variant already normalized to a known value - this method just passes both
    /// through to <see cref="Room.CreateHosted"/>.
    /// </summary>
    /// <param name="hostConnectionId">The host's SignalR connection.</param>
    /// <param name="nickname">The vetted, trimmed host display name.</param>
    /// <param name="variant">The host's already-normalized Guardian variant.</param>
    public Room CreateRoom(string hostConnectionId, string nickname, string variant)
    {
        SweepExpired();

        for (var attempt = 0; attempt < MaxCodeAttempts; attempt++)
        {
            var code = GenerateCode();
            var room = Room.CreateHosted(code, hostConnectionId, nickname, variant);

            // TryAdd fails only if this exact code is already an active room,
            // in which case we regenerate (AC-03 - unique among active rooms).
            if (_rooms.TryAdd(code, room))
            {
                return room;
            }
        }

        // Practically unreachable at family-game scale (see MaxCodeAttempts).
        throw new InvalidOperationException(
            "Unable to allocate a unique room code after multiple attempts.");
    }

    /// <summary>
    /// Looks up an active room by code (case-insensitive). Returns null if the
    /// code is unknown or its room has expired. Used by story 02 (join).
    /// </summary>
    public Room? TryGet(string code)
    {
        SweepExpired();

        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        return _rooms.TryGetValue(code.Trim(), out var room) ? room : null;
    }

    /// <summary>
    /// Removes the player owning the given connection from whichever room it is
    /// seated in (session-engine/03 leave-detection), and drops the room
    /// entirely if that leaves it empty. Called by the hub on disconnect.
    ///
    /// A connection is only ever in one room in Slice 1, so a scan of the active
    /// rooms is fine at toy scale (no connectionId -> code index to maintain).
    /// The room is removed from the store the instant its last player leaves, so
    /// its code is freed immediately rather than lingering until the idle sweep.
    ///
    /// Returns the affected room so the hub can broadcast the updated roster to
    /// the remaining members - but ONLY when the room still exists (has players
    /// left). Returns null when the connection was not seated anywhere OR when
    /// removing it emptied and dropped the room (there is then no one to tell).
    /// </summary>
    /// <param name="connectionId">The SignalR connection that dropped.</param>
    /// <returns>The still-active room to re-broadcast, or null if none / the room was emptied.</returns>
    public Room? RemoveConnection(string connectionId)
    {
        if (string.IsNullOrEmpty(connectionId))
        {
            return null;
        }

        foreach (var pair in _rooms)
        {
            var room = pair.Value;
            if (!room.RemovePlayer(connectionId))
            {
                continue;
            }

            // The connection was seated here (a connection is only in one room),
            // so we can stop scanning. If that was the last player, drop the room
            // and signal "no one to broadcast to" with null; otherwise hand the
            // room back so the hub re-broadcasts the trimmed roster (AC-04).
            //
            // Micro-race (acceptable at Slice-1 toy scale): RemovePlayer, IsEmpty,
            // and TryRemove are not one atomic step, so a joiner calling TryAddPlayer
            // in the exact window between RemovePlayer and IsEmpty could add a player
            // to a room that is then dropped here. The probability is negligible (a
            // join must land in a sub-microsecond window as the last member drops)
            // and it self-heals - that joiner simply creates/joins again. If room
            // churn ever needs to be airtight (Phase 2 reconnect hardening), gate
            // this remove-if-empty behind a per-registry lock or a connection->code
            // index instead of a scan.
            if (room.IsEmpty)
            {
                _rooms.TryRemove(pair.Key, out _);
                return null;
            }

            return room;
        }

        return null;
    }

    /// <summary>
    /// session-engine/07 (AC-01/AC-02): begin holding a dropped connection's seat for
    /// the grace window instead of evicting it. Scans the active rooms (a connection is
    /// only ever in one room in Slice 1, same scan <see cref="RemoveConnection"/> uses)
    /// for the seat owning <paramref name="connectionId"/> and marks it disconnected via
    /// <see cref="Room.MarkDisconnected"/> - the seat stays on the roster (now flagged
    /// not-connected) and the room is NOT freed. Returns a <see cref="SeatGraceHandle"/>
    /// the caller hands to the grace scheduler, or null when the connection was not
    /// seated anywhere (or its seat is already being held) - the "nothing to hold" case
    /// the hub treats as a no-op.
    /// </summary>
    /// <param name="connectionId">The dropped SignalR connection.</param>
    /// <returns>The handle to schedule the deferred eviction, or null if there is no live seat to hold.</returns>
    public SeatGraceHandle? BeginGrace(string connectionId)
    {
        if (string.IsNullOrEmpty(connectionId))
        {
            return null;
        }

        foreach (var pair in _rooms)
        {
            var ticket = pair.Value.MarkDisconnected(connectionId);
            if (ticket is not null)
            {
                return new SeatGraceHandle(pair.Value, connectionId, ticket.Episode, ticket.Token);
            }
        }

        return null;
    }

    /// <summary>
    /// session-engine/07 (AC-03/AC-05): resolve a grace window that elapsed with no
    /// reconnect - the deferred twin of <see cref="RemoveConnection"/>. Releases the
    /// held seat via <see cref="Room.TryReleaseSeat"/> ONLY if the same disconnect
    /// <paramref name="episode"/> is still pending (a reconnect or a newer drop makes
    /// this a no-op), then, if that emptied the room, drops it from the store so its
    /// code frees at once (AC-05 - the room lives through the grace window but not past
    /// eviction). Mirrors <see cref="RemoveConnection"/>'s return contract: the
    /// still-active room to re-broadcast to when members remain, or null when there is
    /// nothing to do (reconnected / superseded) OR the room was emptied and dropped
    /// (no one left to tell).
    /// </summary>
    /// <param name="room">The room the held seat lives in (from the grace handle).</param>
    /// <param name="connectionId">The held (dropped) connection to evict.</param>
    /// <param name="episode">The disconnect episode the expiring timer was started for.</param>
    /// <returns>The still-active room to re-broadcast, or null if nothing was evicted or the room was emptied.</returns>
    public Room? ReleaseGraceSeat(Room room, string connectionId, Guid episode)
    {
        ArgumentNullException.ThrowIfNull(room);

        if (!room.TryReleaseSeat(connectionId, episode))
        {
            // Reconnected within grace, or superseded by a newer drop - keep the seat.
            return null;
        }

        if (room.IsEmpty)
        {
            _rooms.TryRemove(room.Code, out _);
            return null;
        }

        return room;
    }

    /// <summary>The number of currently active rooms (after sweeping expired ones).</summary>
    public int ActiveRoomCount
    {
        get
        {
            SweepExpired();
            return _rooms.Count;
        }
    }

    // Generate one candidate code from the unambiguous alphabet using a
    // cryptographic RNG (unbiased selection across the alphabet).
    private static string GenerateCode()
    {
        Span<char> code = stackalloc char[CodeLength];
        for (var i = 0; i < CodeLength; i++)
        {
            code[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
        }
        return new string(code);
    }

    // Lazily remove rooms idle past the inactivity window (AC-05). Cheap enough
    // to run on each create/lookup at toy scale; no background timer needed.
    private void SweepExpired()
    {
        var cutoff = DateTimeOffset.UtcNow - InactivityWindow;
        foreach (var pair in _rooms)
        {
            if (pair.Value.LastActiveUtc < cutoff)
            {
                _rooms.TryRemove(pair.Key, out _);
            }
        }
    }
}
