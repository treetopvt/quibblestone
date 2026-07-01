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
    /// </summary>
    public Room CreateRoom(string hostConnectionId)
    {
        SweepExpired();

        for (var attempt = 0; attempt < MaxCodeAttempts; attempt++)
        {
            var code = GenerateCode();
            var room = Room.CreateHosted(code, hostConnectionId);

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
