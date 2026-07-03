// ----------------------------------------------------------------------------
//  RoomRegistryLeaveTests - unit tests for leave-detection (session-engine/03).
//
//  Story 03 rides the SignalR connection lifecycle to keep the roster live: when
//  a connection drops, the hub calls RoomRegistry.RemoveConnection so the
//  departed player's tile reverts to an empty slot on the remaining clients
//  (AC-04). These exercise the REAL RoomRegistry + Room (no mocks) to lock in the
//  removal contract the hub's OnDisconnectedAsync depends on:
//
//    1. Removing a seated connection drops that player from the roster and hands
//       the still-active room back so the hub can re-broadcast the trimmed list.
//    2. Removing the LAST player removes the room entirely (its code is freed at
//       once, ActiveRoomCount goes to 0) and returns null - there is no one left
//       to broadcast to.
//    3. Removing a connection that is not seated anywhere is a harmless no-op.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Rooms;

namespace QuibbleStone.Api.Tests;

public class RoomRegistryLeaveTests
{
    [Fact]
    public void RemoveConnection_drops_the_player_and_returns_the_still_active_room()
    {
        var registry = new RoomRegistry();
        var room = registry.CreateRoom("conn-host", "Mossy", "teal");
        // Seat a second player so the room is not emptied by the leave.
        Assert.True(room.TryAddPlayer("Maple", "gold", "conn-joiner"));

        var affected = registry.RemoveConnection("conn-joiner");

        // The room still exists (host remains), so it is handed back to broadcast.
        Assert.NotNull(affected);
        Assert.Equal(room.Code, affected!.Code);

        var players = affected.SnapshotPlayers();
        Assert.Single(players); // host only
        Assert.DoesNotContain(players, p => p.ConnectionId == "conn-joiner");
        Assert.Equal(1, registry.ActiveRoomCount);
    }

    [Fact]
    public void RemoveConnection_of_the_last_player_removes_the_room_and_returns_null()
    {
        var registry = new RoomRegistry();
        var room = registry.CreateRoom("conn-host", "Mossy", "teal"); // host is the only player
        Assert.Equal(1, registry.ActiveRoomCount);

        // Removing the sole (host) connection empties the room.
        var affected = registry.RemoveConnection("conn-host");

        // No one is left to broadcast to, so null - and the room is gone.
        Assert.Null(affected);
        Assert.Equal(0, registry.ActiveRoomCount);
        Assert.Null(registry.TryGet(room.Code));
    }

    [Fact]
    public void RemoveConnection_of_an_unknown_connection_is_a_no_op()
    {
        var registry = new RoomRegistry();
        var room = registry.CreateRoom("conn-host", "Mossy", "teal");

        var affected = registry.RemoveConnection("conn-nobody");

        Assert.Null(affected);
        // The room and its roster are untouched.
        Assert.Equal(1, registry.ActiveRoomCount);
        Assert.Single(registry.TryGet(room.Code)!.SnapshotPlayers());
    }

    [Fact]
    public void RemoveConnection_with_a_null_or_empty_connection_is_a_no_op()
    {
        var registry = new RoomRegistry();
        registry.CreateRoom("conn-host", "Mossy", "teal");

        Assert.Null(registry.RemoveConnection(string.Empty));
        Assert.Equal(1, registry.ActiveRoomCount);
    }

    [Fact]
    public void Room_RemovePlayer_reports_whether_a_player_was_removed()
    {
        var room = Room.CreateHosted("MOSS", "conn-host", "Mossy", "teal");
        Assert.True(room.TryAddPlayer("Wren", "teal", "conn-wren"));

        Assert.False(room.IsEmpty);
        Assert.True(room.RemovePlayer("conn-wren"));  // seated -> removed
        Assert.False(room.RemovePlayer("conn-wren")); // already gone -> no-op
        Assert.True(room.RemovePlayer("conn-host"));  // last one out
        Assert.True(room.IsEmpty);
    }

    // --- session-engine/07: the room-model grace primitives (AC-01/AC-03) --------

    [Fact]
    public void MarkDisconnected_holds_the_seat_flagged_not_connected_without_removing_it()
    {
        var room = Room.CreateHosted("MOSS", "conn-host", "Mossy", "teal");
        Assert.True(room.TryAddPlayer("Wren", "teal", "conn-wren"));

        var ticket = room.MarkDisconnected("conn-wren");

        // AC-01: the seat is HELD - still present, count unchanged, flagged not-connected.
        Assert.NotNull(ticket);
        Assert.Equal(2, room.PlayerCount);
        var wren = Assert.Single(room.SnapshotPlayers(), p => p.ConnectionId == "conn-wren");
        Assert.False(wren.Connected);

        // A second disconnect for the same still-held seat is a no-op (no double-schedule).
        Assert.Null(room.MarkDisconnected("conn-wren"));
        // An unseated connection has nothing to hold.
        Assert.Null(room.MarkDisconnected("conn-nobody"));
    }

    [Fact]
    public void TryReleaseSeat_evicts_only_for_the_matching_episode()
    {
        var room = Room.CreateHosted("MOSS", "conn-host", "Mossy", "teal");
        Assert.True(room.TryAddPlayer("Wren", "teal", "conn-wren"));
        var ticket = room.MarkDisconnected("conn-wren");
        Assert.NotNull(ticket);

        // A stale episode never evicts a live held seat.
        Assert.False(room.TryReleaseSeat("conn-wren", Guid.NewGuid()));
        Assert.Equal(2, room.PlayerCount);

        // The real episode evicts the seat (the deferred twin of RemovePlayer).
        Assert.True(room.TryReleaseSeat("conn-wren", ticket!.Episode));
        Assert.Equal(1, room.PlayerCount);
        Assert.DoesNotContain(room.SnapshotPlayers(), p => p.ConnectionId == "conn-wren");
    }

    [Fact]
    public void CancelGrace_stops_the_hold_so_a_later_release_is_a_no_op()
    {
        var room = Room.CreateHosted("MOSS", "conn-host", "Mossy", "teal");
        Assert.True(room.TryAddPlayer("Wren", "teal", "conn-wren"));
        var ticket = room.MarkDisconnected("conn-wren");
        Assert.NotNull(ticket);

        // Story 08's cancellation seam: cancelling the hold retires it.
        Assert.True(room.CancelGrace("conn-wren"));
        Assert.False(room.CancelGrace("conn-wren")); // already gone -> no-op

        // With the hold retired, the (would-be) expiry release evicts nothing.
        Assert.False(room.TryReleaseSeat("conn-wren", ticket!.Episode));
        Assert.Equal(2, room.PlayerCount); // seat kept
    }

    [Fact]
    public void GetReconnectToken_is_per_seat_and_null_for_an_unseated_connection()
    {
        var room = Room.CreateHosted("MOSS", "conn-host", "Mossy", "teal");
        Assert.True(room.TryAddPlayer("Wren", "teal", "conn-wren"));

        var hostToken = room.GetReconnectToken("conn-host");
        var wrenToken = room.GetReconnectToken("conn-wren");

        Assert.False(string.IsNullOrWhiteSpace(hostToken));
        Assert.False(string.IsNullOrWhiteSpace(wrenToken));
        Assert.NotEqual(hostToken, wrenToken);                 // distinct per seat
        Assert.Null(room.GetReconnectToken("conn-nobody"));    // not seated -> no token
    }
}
