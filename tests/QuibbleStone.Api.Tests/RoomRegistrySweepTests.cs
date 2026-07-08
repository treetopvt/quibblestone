// ----------------------------------------------------------------------------
//  RoomRegistrySweepTests - unit tests for the idle-sweep connected-seat
//  exemption (session-engine/13, AC-03/W1).
//
//  RoomRegistryTests.cs's own header comment used to disclaim sweep/expiry
//  coverage as "left to a manual/integration check... wiring a fake clock is
//  out of scope for Slice 1." This story puts it back in scope: it narrows
//  RoomRegistry.SweepExpired()'s cull to ALSO require no connected seat
//  (`!SnapshotPlayers().Any(p => p.Connected)`), on top of the existing
//  LastActiveUtc-past-window check - so a long, chatty session's room is never
//  pulled out from under still-connected players, no matter how stale
//  LastActiveUtc gets, while a room every seat has abandoned is still swept
//  exactly as before.
//
//  Mirroring SeatGraceServiceTests.cs's posture for its own timer, these use
//  RoomRegistry's NEW test constructor (a millisecond-scale explicit
//  inactivityWindow) so the sweep can be verified deterministically with a
//  short sleep past the window, rather than waiting the real 30 minutes.
//
//  Exercises the REAL RoomRegistry + Room (no mocks), the same style
//  RoomRegistryLeaveTests.cs and RoomCapacityTests.cs use - including calling
//  Room.MarkDisconnected(connectionId) straight from a test, the precedent
//  RoomCapacityTests.cs's A_held_disconnected_seat_still_counts_toward_the_cap
//  test already set.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Rooms;

namespace QuibbleStone.Api.Tests;

public class RoomRegistrySweepTests
{
    // A tiny window so the tests sleep milliseconds, not the real 30 minutes.
    private static readonly TimeSpan TinyWindow = TimeSpan.FromMilliseconds(20);

    [Fact]
    public async Task A_room_with_a_connected_seat_survives_the_sweep_past_the_window()
    {
        var registry = new RoomRegistry(TinyWindow);
        var room = registry.CreateRoom("conn-host", "Mossy", "teal");

        // Give LastActiveUtc time to fall behind the (tiny) inactivity window
        // while the host's seat stays connected the whole time.
        await Task.Delay(TinyWindow * 5);

        // TryGet itself triggers the lazy sweep - the connected-seat exemption
        // must keep this room reachable no matter how stale LastActiveUtc is.
        Assert.NotNull(registry.TryGet(room.Code));
        Assert.Equal(1, registry.ActiveRoomCount);
    }

    [Fact]
    public async Task A_room_where_every_seat_has_disconnected_is_still_swept()
    {
        var registry = new RoomRegistry(TinyWindow);
        var room = registry.CreateRoom("conn-host", "Mossy", "teal");
        Assert.True(room.TryAddPlayer("Maple", "gold", "conn-joiner"));

        // Every seat drops (held for grace, session-engine/07) - MarkDisconnected
        // deliberately does NOT bump LastActiveUtc, so once the window elapses
        // this room has neither recent activity NOR a connected seat.
        Assert.NotNull(room.MarkDisconnected("conn-host"));
        Assert.NotNull(room.MarkDisconnected("conn-joiner"));

        await Task.Delay(TinyWindow * 5);

        // No connected seat is left to exempt it, so the sweep still culls it -
        // this story narrows WHEN the sweep fires, it does not remove it.
        Assert.Null(registry.TryGet(room.Code));
        Assert.Equal(0, registry.ActiveRoomCount);
    }

    [Fact]
    public void DefaultInactivityWindow_is_the_thirty_minute_default_every_other_call_site_keeps()
    {
        // The parameterless constructor (every existing `new RoomRegistry()` call
        // site, including DI) must keep today's 30-minute default, untouched.
        var registry = new RoomRegistry();

        Assert.Equal(TimeSpan.FromMinutes(30), RoomRegistry.DefaultInactivityWindow);
        Assert.Equal(RoomRegistry.DefaultInactivityWindow, registry.InactivityWindow);
    }
}
