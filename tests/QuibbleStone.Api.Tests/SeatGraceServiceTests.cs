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
using QuibbleStone.Api.Settings;

namespace QuibbleStone.Api.Tests;

public class SeatGraceServiceTests
{
    private static readonly TimeSpan TinyWindow = TimeSpan.FromMilliseconds(30);

    private static SeatGraceService Service(RoomRegistry rooms, FakeGameHubContext ctx, TimeSpan window) =>
        new SeatGraceService(ctx, rooms, TestTelemetry.NoOp, NullLogger<SeatGraceService>.Instance, window);

    [Fact]
    public async Task Grace_expiry_evicts_the_held_seat_and_aborts_a_prompting_round()
    {
        // B3 (alpha-gate hardening) regression guard: the joiner here is dealt blanks
        // (round-robin over blankCount 4 / 2 players) but never submits any of them, so
        // it still OWES blanks at eviction time - the round must still abort exactly as
        // before this fix. Contrast
        // Grace_expiry_of_a_seat_that_already_submitted_everything_does_not_abort_a_prompting_round
        // below, where the evicted seat owed nothing.
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

        // AC-03 / B3: after the window the seat is evicted and, since it still owed
        // outstanding blanks, the round aborts - the exact pre-fix end state, just deferred.
        Assert.Equal(1, room.PlayerCount);
        Assert.DoesNotContain(room.SnapshotPlayers(), p => p.ConnectionId == "conn-joiner");
        Assert.Null(room.CurrentRound); // aborted back to the lobby
        Assert.Contains(ctx.Recorder.Sends, s => s.Method == "RoundAborted");
        Assert.Contains(ctx.Recorder.Sends, s => s.Method == "RosterChanged");
        Assert.Equal(1, registry.ActiveRoomCount); // the host remains, room still live
    }

    [Fact]
    public async Task Grace_expiry_of_a_seat_that_already_submitted_everything_does_not_abort_a_prompting_round()
    {
        // B3 (alpha-gate hardening): the whole point of the fix - a seat that turned in
        // every blank it owed BEFORE dropping leaves nothing incomplete behind it. The
        // round must keep collecting from whoever remains rather than aborting just
        // because this seat's grace window happened to expire.
        var registry = new RoomRegistry();
        var room = registry.CreateRoom("conn-host", "Mossy", "teal");
        Assert.True(room.TryAddPlayer("Maple", "gold", "conn-joiner"));
        room.StartRound("tmpl", "classic-blind", blankCount: 4);

        // Round-robin deal (blank k -> player k % 2, host first): the host owns 0/2,
        // the joiner owns 1/3. The joiner submits BOTH of its own blanks; the host's
        // stay outstanding, so the round is still "prompting" overall.
        Assert.Equal(Room.SubmitOutcome.Recorded, room.RecordSubmission("conn-joiner", 1, "wobbly"));
        Assert.Equal(Room.SubmitOutcome.Recorded, room.RecordSubmission("conn-joiner", 3, "noodle"));
        Assert.Equal("prompting", room.CurrentRound!.Phase);

        var ctx = new FakeGameHubContext();
        var svc = Service(registry, ctx, TinyWindow);

        var handle = registry.BeginGrace("conn-joiner");
        Assert.NotNull(handle);

        await svc.ScheduleEviction(handle!);

        // The seat is gone, but since it owed nothing further the round keeps going for
        // the host - no RoundAborted, and the round is still live and prompting.
        Assert.Equal(1, room.PlayerCount);
        Assert.NotNull(room.CurrentRound);
        Assert.Equal("prompting", room.CurrentRound!.Phase);
        Assert.DoesNotContain(ctx.Recorder.Sends, s => s.Method == "RoundAborted");
        Assert.Contains(ctx.Recorder.Sends, s => s.Method == "RosterChanged");
        Assert.Equal(1, registry.ActiveRoomCount);
    }

    [Fact]
    public void DefaultGraceWindow_is_three_minutes()
    {
        // B3 (alpha-gate hardening): raised from the original 30-second default, which
        // was too eager for a real phone-lock / elevator / brief tunnel drop and was
        // aborting rounds that a slightly longer wait would have let recover on their own.
        Assert.Equal(TimeSpan.FromSeconds(180), SeatGraceService.DefaultGraceWindow);
    }

    [Fact]
    public void The_DI_path_over_default_settings_resolves_the_same_default_window()
    {
        // control-plane/03 (#232) AC-03/AC-07: the DI constructor reads
        // `session.seatGraceWindowSeconds` live; over an unconfigured (default-only)
        // settings service its GraceWindow display value is bit-for-bit the code
        // default - a fresh clone with no override behaves exactly as before.
        var registry = new RoomRegistry();
        var ctx = new FakeGameHubContext();
        var svc = new SeatGraceService(
            ctx, registry, TestTelemetry.NoOp, NullLogger<SeatGraceService>.Instance, TestRuntimeSettings.Defaults());

        Assert.Equal(SeatGraceService.DefaultGraceWindow, svc.GraceWindow);
    }

    [Fact]
    public async Task An_overridden_window_governs_a_new_disconnects_scheduled_eviction()
    {
        // control-plane/03 (#232) AC-03: the DI path resolves the CURRENT settings
        // value when a NEW disconnect schedules its eviction. The catalog's floor is 1
        // second, so this drives a real (short, deterministic - not flaky) eviction
        // under a settings override rather than the 180s code default, proving the
        // live-read path (not just the fixed-window test constructor) actually governs.
        var registry = new RoomRegistry();
        var room = registry.CreateRoom("conn-host", "Mossy", "teal");
        Assert.True(room.TryAddPlayer("Maple", "gold", "conn-joiner"));

        var ctx = new FakeGameHubContext();
        var settings = TestRuntimeSettings.WithInt(SettingsCatalog.SessionSeatGraceWindowSeconds, 1);
        var svc = new SeatGraceService(ctx, registry, TestTelemetry.NoOp, NullLogger<SeatGraceService>.Instance, settings);

        var handle = registry.BeginGrace("conn-joiner");
        Assert.NotNull(handle);

        await svc.ScheduleEviction(handle!);

        // The overridden 1-second window elapsed and evicted the seat - the DI path
        // truly reads the settings key live, not just the code default.
        Assert.Equal(1, room.PlayerCount);
        Assert.DoesNotContain(room.SnapshotPlayers(), p => p.ConnectionId == "conn-joiner");
        Assert.Contains(ctx.Recorder.Sends, s => s.Method == "RosterChanged");
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
