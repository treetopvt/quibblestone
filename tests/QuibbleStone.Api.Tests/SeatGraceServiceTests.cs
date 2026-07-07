// ----------------------------------------------------------------------------
//  SeatGraceServiceTests - the DEFERRED eviction half of "hold the seat"
//  (session-engine/07, AC-03 / AC-05).
//
//  These drive the REAL SeatGraceService + RoomRegistry + Room with a TINY,
//  test-configured grace window (so a spec runs in milliseconds, not 30 real
//  seconds) and a fake IHubContext (FakeGameHubContext) that records the epilogue
//  broadcast. They lock in that once the window elapses with no reconnect the seat
//  is evicted exactly as before this story - just deferred - and that a reconnect
//  that cancels the grace keeps the seat (the cancellation seam story 08 spends).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.Extensions.Logging.Abstractions;
using QuibbleStone.Api.Rooms;

namespace QuibbleStone.Api.Tests;

public class SeatGraceServiceTests
{
    private static readonly TimeSpan TinyWindow = TimeSpan.FromMilliseconds(30);

    private static SeatGraceService Service(RoomRegistry rooms, FakeGameHubContext ctx, TimeSpan window) =>
        new SeatGraceService(ctx, rooms, TestTelemetry.NoOp, NullLogger<SeatGraceService>.Instance, window);

    [Fact]
    public async Task Grace_expiry_evicts_the_held_seat_and_aborts_a_prompting_round()
    {
        var registry = new RoomRegistry();
        var room = registry.CreateRoom("conn-host", "Mossy", "teal");
        Assert.True(room.TryAddPlayer("Maple", "gold", "conn-joiner"));
        // Open a prompting round so AC-03's "abort if still prompting" branch is exercised.
        room.StartRound("tmpl", "classic-blind", blankCount: 4);
        Assert.NotNull(room.CurrentRound);

        var ctx = new FakeGameHubContext();
        var svc = Service(registry, ctx, TinyWindow);

        var handle = registry.BeginGrace("conn-joiner");
        Assert.NotNull(handle);

        // Held (not yet evicted): the seat is still present and the round still prompting.
        Assert.Equal(2, room.PlayerCount);
        Assert.NotNull(room.CurrentRound);

        await svc.ScheduleEviction(handle!);

        // AC-03: after the window the seat is evicted and, since it was still prompting,
        // the round aborts - the exact pre-story end state, just deferred.
        Assert.Equal(1, room.PlayerCount);
        Assert.DoesNotContain(room.SnapshotPlayers(), p => p.ConnectionId == "conn-joiner");
        Assert.Null(room.CurrentRound); // aborted back to the lobby
        Assert.Contains(ctx.Recorder.Sends, s => s.Method == "RoundAborted");
        Assert.Contains(ctx.Recorder.Sends, s => s.Method == "RosterChanged");
        Assert.Equal(1, registry.ActiveRoomCount); // the host remains, room still live
    }

    [Fact]
    public async Task Grace_expiry_of_a_non_round_seat_evicts_without_aborting()
    {
        var registry = new RoomRegistry();
        var room = registry.CreateRoom("conn-host", "Mossy", "teal");
        Assert.True(room.TryAddPlayer("Maple", "gold", "conn-joiner"));
        // No round in progress (lobby) - eviction must NOT emit a RoundAborted.

        var ctx = new FakeGameHubContext();
        var svc = Service(registry, ctx, TinyWindow);

        var handle = registry.BeginGrace("conn-joiner");
        await svc.ScheduleEviction(handle!);

        Assert.Equal(1, room.PlayerCount);
        Assert.DoesNotContain(ctx.Recorder.Sends, s => s.Method == "RoundAborted");
        Assert.Contains(ctx.Recorder.Sends, s => s.Method == "RosterChanged");
    }

    [Fact]
    public async Task Grace_expiry_of_the_last_seat_frees_the_room_but_only_after_the_window()
    {
        var registry = new RoomRegistry();
        var room = registry.CreateRoom("conn-host", "Mossy", "teal"); // the sole seat

        var ctx = new FakeGameHubContext();
        var svc = Service(registry, ctx, TinyWindow);

        var handle = registry.BeginGrace("conn-host");
        Assert.NotNull(handle);

        // AC-05: a pending grace timer does NOT keep the room alive, but it also does
        // not free it EARLY - the room is still active while the seat is held.
        Assert.Equal(1, registry.ActiveRoomCount);
        Assert.NotNull(registry.TryGet(room.Code));

        await svc.ScheduleEviction(handle!);

        // Only once the last seat is evicted after grace is the room freed.
        Assert.Equal(0, registry.ActiveRoomCount);
        Assert.Null(registry.TryGet(room.Code));
        // No one left to tell, so no epilogue broadcast fires.
        Assert.DoesNotContain(ctx.Recorder.Sends, s => s.Method == "RosterChanged");
    }

    [Fact]
    public async Task A_reconnect_that_cancels_grace_keeps_the_seat()
    {
        var registry = new RoomRegistry();
        var room = registry.CreateRoom("conn-host", "Mossy", "teal");
        Assert.True(room.TryAddPlayer("Maple", "gold", "conn-joiner"));

        var ctx = new FakeGameHubContext();
        // A real 30s window: the seat must survive because grace is CANCELLED, not because
        // the window has not elapsed.
        var svc = Service(registry, ctx, TimeSpan.FromSeconds(30));

        var handle = registry.BeginGrace("conn-joiner");
        Assert.NotNull(handle);

        // The cancellation seam story 08's Rejoin spends: cancel the pending grace.
        Assert.True(room.CancelGrace("conn-joiner"));

        // The scheduled task returns promptly via cancellation - no eviction, no broadcast.
        await svc.ScheduleEviction(handle!);

        Assert.Equal(2, room.PlayerCount);
        Assert.Contains(room.SnapshotPlayers(), p => p.ConnectionId == "conn-joiner");
        Assert.Empty(ctx.Recorder.Sends);
    }
}
