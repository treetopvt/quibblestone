// ----------------------------------------------------------------------------
//  GameHubRejoinTests - unit tests for GameHub.Rejoin (session-engine/08), the
//  method that SPENDS the reconnect token story 07 mints: a device reclaims its
//  held seat under a NEW SignalR connection and rehydrates enough round state to
//  pick up where it left off.
//
//  These drive the REAL GameHub against the REAL RoomRegistry (no mocking
//  framework in the harness), with the same hand-rolled SignalR fakes the other
//  GameHub*Tests use. TestSeatGrace.NoOp binds a grace service with a LONG window
//  so a hold's DEFERRED eviction never fires during these synchronous assertions;
//  the grace-expiry-vs-Rejoin RACE (which needs a real, cancellable timer) is
//  covered by its own test at the bottom with a real SeatGraceService.
//
//    AC-01: a valid token reclaims the seat under a new connection id and cancels
//           the pending grace eviction (the seat survives past the window).
//    AC-02: roster + host flag + phase come back for lobby / prompting / reveal.
//    AC-03: a "prompting" rejoin returns ONLY this seat's remaining blank indices
//           (never another seat's) plus the current progress counts.
//    AC-04: a "reveal" rejoin returns the ordered reveal words.
//    AC-05: an unknown / already-evicted / wrong-room token returns Ok=false and
//           mutates nothing.
//    AC-06: a successful reclaim broadcasts RosterChanged with Connected flipped true.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using QuibbleStone.Api.Content;
using QuibbleStone.Api.Entitlements;
using QuibbleStone.Api.Hubs;
using QuibbleStone.Api.Rooms;
using QuibbleStone.Api.Safety;

namespace QuibbleStone.Api.Tests;

public class GameHubRejoinTests
{
    // Builds a hub wired to the given registry + a real safety filter, with a fake
    // SignalR surface for the given connection id. Passing an explicit grace service
    // lets the race test inject a real, cancellable timer; otherwise NoOp is used.
    private static (GameHub Hub, RecordingClients Clients, RecordingGroups Groups) BuildHub(
        RoomRegistry registry,
        string connectionId,
        SeatGraceService? grace = null)
    {
        var hub = new GameHub(
            registry,
            new ContentSafetyFilter(),
            new TemplateCatalog(),
            new FamilySafeContentSelector(),
            new LengthContentSelector(),
            new FreshnessContentSelector(),
            new FakeTelemetrySink(),
            TestTelemetry.NoOp,
            new DefaultUnlockedEntitlementService(),
            grace ?? TestSeatGrace.NoOp(registry),
            NullLogger<GameHub>.Instance);
        var clients = new RecordingClients();
        var groups = new RecordingGroups();
        hub.Clients = clients;
        hub.Groups = groups;
        hub.Context = new FakeHubCallerContext(connectionId);
        return (hub, clients, groups);
    }

    // Seat two players (host + joiner) via the hub so each gets a real reconnect
    // token in its own envelope, and return both tokens + the room code.
    private static async Task<(string Code, string HostToken, string JoinerToken)> SeatTwoAsync(RoomRegistry registry)
    {
        var (hostHub, _, _) = BuildHub(registry, "conn-host");
        var create = await hostHub.CreateRoom("Mossy", "teal");
        var code = create.Room!.Code;

        var (joinHub, _, _) = BuildHub(registry, "conn-joiner");
        var join = await joinHub.JoinRoom(code, "Maple", "gold");

        return (code, create.ReconnectToken!, join.ReconnectToken!);
    }

    // --- AC-01: reclaim under a new connection + cancel the pending grace --------

    [Fact]
    public async Task Rejoin_reclaims_the_seat_under_a_new_connection_and_cancels_the_pending_grace()
    {
        var registry = new RoomRegistry();
        var (code, _, joinerToken) = await SeatTwoAsync(registry);
        var room = registry.TryGet(code)!;

        // The joiner drops abnormally -> its seat is HELD (grace window open, a real
        // cancellable timer via a real SeatGraceService). A LONG (30s) window: Rejoin
        // cancels the timer, so the wait is short-circuited by cancellation and never
        // actually elapses - a long window keeps this test off the clock (no flake on a
        // stalled CI box). The timing-critical ordering is covered deterministically by
        // the two dedicated grace-vs-Rejoin race tests below.
        var ctx = new FakeGameHubContext();
        var grace = new SeatGraceService(ctx, registry, TestTelemetry.NoOp, NullLogger<SeatGraceService>.Instance, TimeSpan.FromSeconds(30));
        var handle = registry.BeginGrace("conn-joiner");
        Assert.NotNull(handle);
        var evictionTask = grace.ScheduleEviction(handle!);

        // The device reconnects on a BRAND-NEW connection id and spends its token.
        var (rejoinHub, clients, groups) = BuildHub(registry, "conn-joiner-2");
        var result = await rejoinHub.Rejoin(code, joinerToken);

        Assert.True(result.Ok);
        Assert.Null(result.Error);

        // The seat moved to the new connection and is connected again (AC-01).
        var players = room.SnapshotPlayers();
        Assert.DoesNotContain(players, p => p.ConnectionId == "conn-joiner");   // old id gone
        var maple = Assert.Single(players, p => p.Nickname == "Maple");
        Assert.Equal("conn-joiner-2", maple.ConnectionId);
        Assert.True(maple.Connected);
        Assert.Equal(2, room.PlayerCount);

        // Re-subscribed to the room group under the new connection (AC-01).
        Assert.Contains((code, "conn-joiner-2"), groups.Added);

        // The pending grace eviction was cancelled: awaiting the scheduled task returns
        // WITHOUT evicting, even past what would have been the window (AC-01).
        await evictionTask;
        Assert.Equal(2, room.PlayerCount);
        Assert.Contains(room.SnapshotPlayers(), p => p.ConnectionId == "conn-joiner-2");
    }

    // --- AC-02: roster + host flag + phase for each phase -----------------------

    [Fact]
    public async Task Rejoin_in_the_lobby_returns_roster_host_flag_and_lobby_phase()
    {
        var registry = new RoomRegistry();
        var (code, hostToken, _) = await SeatTwoAsync(registry);

        // The host drops and reconnects while the room is still in the lobby.
        registry.BeginGrace("conn-host");
        var (hub, _, _) = BuildHub(registry, "conn-host-2");
        var result = await hub.Rejoin(code, hostToken);

        Assert.True(result.Ok);
        Assert.Equal("lobby", result.Phase);
        Assert.True(result.IsHost);                       // the host flag comes back (AC-02)
        Assert.NotNull(result.Room);
        Assert.Equal(2, result.Room!.Players.Count);      // full roster (AC-02)
        Assert.Null(result.Round);                        // no live round in the lobby
        Assert.Null(result.YourBlanks);
        Assert.Null(result.Reveal);
    }

    [Fact]
    public async Task Rejoin_in_a_prompting_round_returns_prompting_phase_and_round_metadata()
    {
        var registry = new RoomRegistry();
        var (code, _, joinerToken) = await SeatTwoAsync(registry);
        var room = registry.TryGet(code)!;
        room.StartRound("wobbly-wizard", "classic-blind", blankCount: 4);

        registry.BeginGrace("conn-joiner");
        var (hub, _, _) = BuildHub(registry, "conn-joiner-2");
        var result = await hub.Rejoin(code, joinerToken);

        Assert.True(result.Ok);
        Assert.Equal("prompting", result.Phase);          // phase (AC-02)
        Assert.False(result.IsHost);                      // the joiner is not the host (AC-02)
        Assert.NotNull(result.Room);
        Assert.NotNull(result.Round);
        Assert.Equal("wobbly-wizard", result.Round!.TemplateId);
        Assert.Equal("classic-blind", result.Round.Mode);
    }

    [Fact]
    public async Task Rejoin_in_a_reveal_round_returns_reveal_phase()
    {
        var registry = new RoomRegistry();
        var (code, hostToken, _) = await SeatTwoAsync(registry);
        var room = registry.TryGet(code)!;
        // 2 blanks, dealt round-robin: host owns blank 0, joiner owns blank 1. Fill both
        // so the round advances to "reveal".
        room.StartRound("wobbly-wizard", "classic-blind", blankCount: 2);
        Assert.Equal(Room.SubmitOutcome.Recorded, room.RecordSubmission("conn-host", 0, "banana"));
        Assert.Equal(Room.SubmitOutcome.RoundComplete, room.RecordSubmission("conn-joiner", 1, "wobbly"));
        Assert.Equal("reveal", room.CurrentRound!.Phase);

        registry.BeginGrace("conn-host");
        var (hub, _, _) = BuildHub(registry, "conn-host-2");
        var result = await hub.Rejoin(code, hostToken);

        Assert.True(result.Ok);
        Assert.Equal("reveal", result.Phase);             // phase (AC-02)
        Assert.True(result.IsHost);
        Assert.Null(result.YourBlanks);                   // no outstanding blanks in reveal
        Assert.Null(result.Progress);
    }

    // --- AC-03: only this seat's own remaining blanks + progress counts ---------

    [Fact]
    public async Task Rejoin_in_prompting_returns_only_this_seats_remaining_blanks_and_progress()
    {
        var registry = new RoomRegistry();
        var (code, _, joinerToken) = await SeatTwoAsync(registry);
        var room = registry.TryGet(code)!;
        // 4 blanks round-robin over [host, joiner]: host owns {0, 2}, joiner owns {1, 3}.
        room.StartRound("wobbly-wizard", "classic-blind", blankCount: 4);

        // The joiner has already submitted ONE of its two blanks (index 1); index 3 is
        // still outstanding. The host has submitted nothing.
        Assert.Equal(Room.SubmitOutcome.Recorded, room.RecordSubmission("conn-joiner", 1, "banana"));

        registry.BeginGrace("conn-joiner");
        var (hub, _, _) = BuildHub(registry, "conn-joiner-2");
        var result = await hub.Rejoin(code, joinerToken);

        Assert.True(result.Ok);
        Assert.Equal("prompting", result.Phase);

        // AC-03: ONLY this seat's own still-outstanding blank (index 3) - never the
        // already-submitted one (1) and never the host's blanks (0, 2).
        Assert.NotNull(result.YourBlanks);
        Assert.Equal(new[] { 3 }, result.YourBlanks!.BlankIndices);

        // AC-03: the room-wide progress counts - nobody has finished ALL their blanks yet.
        Assert.NotNull(result.Progress);
        Assert.Equal(0, result.Progress!.DoneCount);
        Assert.Equal(2, result.Progress.PlayerCount);
    }

    [Fact]
    public async Task Rejoin_can_still_submit_its_remaining_blanks_after_reclaim()
    {
        // Not a numbered AC, but the point of resuming: after reclaim the seat's
        // assignment is rekeyed to the new connection, so SubmitWord under the NEW
        // connection is accepted (it would be Rejected if the assignment key had not moved).
        var registry = new RoomRegistry();
        var (code, _, joinerToken) = await SeatTwoAsync(registry);
        var room = registry.TryGet(code)!;
        room.StartRound("wobbly-wizard", "classic-blind", blankCount: 4); // joiner owns {1, 3}

        registry.BeginGrace("conn-joiner");
        var (hub, _, _) = BuildHub(registry, "conn-joiner-2");
        await hub.Rejoin(code, joinerToken);

        // The reclaimed seat (new connection) submits one of its own blanks - accepted.
        var submit = await hub.SubmitWord(code, 1, "banana");
        Assert.True(submit.Ok);
        Assert.Equal("banana", room.CurrentRound!.Submissions[1].Word);
    }

    // --- AC-04: reveal words -----------------------------------------------------

    [Fact]
    public async Task Rejoin_in_reveal_returns_the_ordered_reveal_words()
    {
        var registry = new RoomRegistry();
        var (code, hostToken, _) = await SeatTwoAsync(registry);
        var room = registry.TryGet(code)!;
        room.StartRound("wobbly-wizard", "classic-blind", blankCount: 2);
        room.RecordSubmission("conn-host", 0, "banana");
        room.RecordSubmission("conn-joiner", 1, "wobbly");
        Assert.Equal("reveal", room.CurrentRound!.Phase);

        registry.BeginGrace("conn-host");
        var (hub, _, _) = BuildHub(registry, "conn-host-2");
        var result = await hub.Rejoin(code, hostToken);

        Assert.True(result.Ok);
        Assert.Equal("reveal", result.Phase);
        Assert.NotNull(result.Reveal);
        Assert.Equal("wobbly-wizard", result.Reveal!.TemplateId);
        Assert.Equal(2, result.Reveal.Words.Count);
        // Ordered by blank position (AC-04): blank 0 = "banana", blank 1 = "wobbly".
        Assert.Equal("banana", result.Reveal.Words[0].Word);
        Assert.Equal("wobbly", result.Reveal.Words[1].Word);
    }

    // --- AC-05: unknown / already-evicted / wrong-room token --------------------

    [Fact]
    public async Task Rejoin_with_an_unknown_token_fails_friendly_and_mutates_nothing()
    {
        var registry = new RoomRegistry();
        var (code, _, _) = await SeatTwoAsync(registry);
        var room = registry.TryGet(code)!;
        var before = room.SnapshotPlayers().Select(p => p.ConnectionId).OrderBy(x => x).ToArray();

        var (hub, clients, groups) = BuildHub(registry, "conn-stranger");
        var result = await hub.Rejoin(code, "DEADBEEFNOTATOKEN");

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
        Assert.Null(result.Room);
        // Nothing mutated: same roster, no group subscription, no broadcast.
        var after = room.SnapshotPlayers().Select(p => p.ConnectionId).OrderBy(x => x).ToArray();
        Assert.Equal(before, after);
        Assert.Empty(groups.Added);
        Assert.Null(clients.LastMethod);
    }

    [Fact]
    public async Task Rejoin_with_an_unknown_room_code_fails_friendly()
    {
        var registry = new RoomRegistry();
        var (_, _, joinerToken) = await SeatTwoAsync(registry);

        var (hub, _, groups) = BuildHub(registry, "conn-joiner-2");
        var result = await hub.Rejoin("ZZZZ", joinerToken);

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
        Assert.Empty(groups.Added);
    }

    [Fact]
    public async Task Rejoin_with_a_token_for_a_different_room_fails_friendly()
    {
        var registry = new RoomRegistry();
        var (codeA, _, _) = await SeatTwoAsync(registry);

        // A second, unrelated room B; its host holds a token that is NOT valid in room A.
        var (hostHubB, _, _) = BuildHub(registry, "conn-host-b");
        var createB = await hostHubB.CreateRoom("Birch", "plum");
        var roomBToken = createB.ReconnectToken!;

        var (hub, _, groups) = BuildHub(registry, "conn-host-b-2");
        var result = await hub.Rejoin(codeA, roomBToken); // right token, WRONG room

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
        Assert.Empty(groups.Added);
    }

    [Fact]
    public async Task Rejoin_after_the_seat_was_already_evicted_fails_friendly()
    {
        var registry = new RoomRegistry();
        var (code, _, joinerToken) = await SeatTwoAsync(registry);
        var room = registry.TryGet(code)!;

        // Simulate grace expiry evicting the joiner's held seat (the deferred eviction ran).
        var handle = registry.BeginGrace("conn-joiner");
        Assert.True(room.TryReleaseSeat("conn-joiner", handle!.Episode));
        Assert.Equal(1, room.PlayerCount); // only the host remains

        var (hub, _, _) = BuildHub(registry, "conn-joiner-2");
        var result = await hub.Rejoin(code, joinerToken);

        // The token now names no live seat (grace expired) -> friendly failure (AC-05).
        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
        Assert.Equal(1, room.PlayerCount);
    }

    // --- AC-06: broadcast RosterChanged with Connected flipped true -------------

    [Fact]
    public async Task Rejoin_broadcasts_RosterChanged_with_the_seat_reconnected()
    {
        var registry = new RoomRegistry();
        var (code, _, joinerToken) = await SeatTwoAsync(registry);
        var room = registry.TryGet(code)!;

        // The joiner drops (seat held, flagged not-connected), then reconnects.
        registry.BeginGrace("conn-joiner");
        Assert.False(Assert.Single(room.SnapshotPlayers(), p => p.Nickname == "Maple").Connected);

        var (hub, clients, _) = BuildHub(registry, "conn-joiner-2");
        await hub.Rejoin(code, joinerToken);

        // AC-06: the room group gets a RosterChanged whose Maple tile is Connected again.
        Assert.Equal(code, clients.LastGroupName);
        Assert.Equal("RosterChanged", clients.LastMethod);
        var broadcast = Assert.IsType<RoomStateDto>(clients.LastArgs![0]);
        var maple = Assert.Single(broadcast.Players, p => p.Nickname == "Maple");
        Assert.True(maple.Connected);
    }

    // --- the grace-expiry-vs-Rejoin RACE resolves deterministically -------------

    [Fact]
    public async Task Rejoin_that_lands_before_grace_expiry_wins_and_the_timer_no_ops()
    {
        // Rejoin wins the lock first: it cancels the hold, so the later grace-expiry
        // eviction finds no hold and is a clean no-op (the seat survives).
        var registry = new RoomRegistry();
        var (code, _, joinerToken) = await SeatTwoAsync(registry);
        var room = registry.TryGet(code)!;

        var ctx = new FakeGameHubContext();
        var grace = new SeatGraceService(ctx, registry, TestTelemetry.NoOp, NullLogger<SeatGraceService>.Instance, TimeSpan.FromSeconds(30));
        var handle = registry.BeginGrace("conn-joiner");
        var evictionTask = grace.ScheduleEviction(handle!);

        var (hub, _, _) = BuildHub(registry, "conn-joiner-2");
        var result = await hub.Rejoin(code, joinerToken);
        Assert.True(result.Ok);

        // The eviction task returns via cancellation - no eviction, no epilogue broadcast.
        await evictionTask;
        Assert.Equal(2, room.PlayerCount);
        Assert.DoesNotContain(ctx.Recorder.Sends, s => s.Method == "RoundAborted");
    }

    [Fact]
    public async Task Rejoin_that_lands_after_grace_expiry_is_a_clean_failure()
    {
        // Grace-expiry wins the lock first: it evicts the seat, so the later Rejoin finds
        // no seat holding the token and is a clean AC-05 failure (the other, no-op side).
        var registry = new RoomRegistry();
        var (code, _, joinerToken) = await SeatTwoAsync(registry);
        var room = registry.TryGet(code)!;

        var ctx = new FakeGameHubContext();
        var grace = new SeatGraceService(ctx, registry, TestTelemetry.NoOp, NullLogger<SeatGraceService>.Instance, TimeSpan.FromMilliseconds(20));
        var handle = registry.BeginGrace("conn-joiner");

        // Let the grace window elapse and evict FIRST.
        await grace.ScheduleEviction(handle!);
        Assert.Equal(1, room.PlayerCount);

        var (hub, _, groups) = BuildHub(registry, "conn-joiner-2");
        var result = await hub.Rejoin(code, joinerToken);

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
        Assert.Empty(groups.Added);
        Assert.Equal(1, room.PlayerCount); // still just the host
    }

    // --- Minimal SignalR fakes (mirrors GameHubJoinTests) -----------------------

    private sealed class RecordingClients : IHubCallerClients
    {
        public string? LastGroupName { get; private set; }
        public string? LastMethod { get; private set; }
        public object?[]? LastArgs { get; private set; }

        private IClientProxy MakeProxy(string? groupName) => new RecordingProxy(this, groupName);

        public IClientProxy Group(string groupName) => MakeProxy(groupName);
        public IClientProxy All => MakeProxy(null);
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => MakeProxy(null);
        public IClientProxy Caller => MakeProxy(null);
        public IClientProxy Client(string connectionId) => MakeProxy(null);
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => MakeProxy(null);
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => MakeProxy(groupName);
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => MakeProxy(null);
        public IClientProxy OthersInGroup(string groupName) => MakeProxy(groupName);
        public IClientProxy Others => MakeProxy(null);
        public IClientProxy User(string userId) => MakeProxy(null);
        public IClientProxy Users(IReadOnlyList<string> userIds) => MakeProxy(null);

        private sealed class RecordingProxy(RecordingClients owner, string? groupName) : IClientProxy, ISingleClientProxy
        {
            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
            {
                owner.LastGroupName = groupName;
                owner.LastMethod = method;
                owner.LastArgs = args;
                return Task.CompletedTask;
            }

            public Task<T> InvokeCoreAsync<T>(string method, object?[] args, CancellationToken cancellationToken = default)
                => Task.FromResult(default(T)!);
        }
    }

    private sealed class RecordingGroups : IGroupManager
    {
        public List<(string Group, string Connection)> Added { get; } = [];

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            Added.Add((groupName, connectionId));
            return Task.CompletedTask;
        }

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeHubCallerContext(string connectionId) : HubCallerContext
    {
        public override string ConnectionId { get; } = connectionId;
        public override string? UserIdentifier => null;
        public override System.Security.Claims.ClaimsPrincipal? User => null;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }
}
