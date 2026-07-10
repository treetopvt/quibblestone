// ----------------------------------------------------------------------------
//  GameHubDisconnectTests - unit tests for the two leave paths (session-engine/03):
//  OnDisconnectedAsync (the connection dropped) and LeaveRoom (the player tapped
//  "Leave" and went Home while the connection stays open).
//
//  Leave-detection rides the SignalR connection lifecycle (no heartbeat): when a
//  connection drops, the hub removes that player and re-broadcasts the trimmed
//  roster so the departed tile reverts to an empty slot on the remaining clients
//  (AC-04). These exercise the REAL GameHub against the REAL RoomRegistry with a
//  small hand-rolled SignalR surface (no mocking framework in the harness):
//
//    1. A disconnect from a room with other members broadcasts "RosterChanged"
//       to the room's group carrying the trimmed roster, and always chains to
//       base.OnDisconnectedAsync.
//    2. A disconnect that empties the room drops the room (nothing to broadcast).
//    3. A disconnect from a connection that was never in a room is a no-op.
//    4. LeaveRoom removes the caller from the room's group AND broadcasts the
//       trimmed roster to survivors; leaving as the last player drops the room
//       with no broadcast; leaving from an unseated connection is a no-op.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using QuibbleStone.Api.Accounts;
using QuibbleStone.Api.Content;
using QuibbleStone.Api.Entitlements;
using QuibbleStone.Api.Hubs;
using QuibbleStone.Api.Rooms;
using QuibbleStone.Api.Safety;

namespace QuibbleStone.Api.Tests;

public class GameHubDisconnectTests
{
    private static (GameHub Hub, RoomRegistry Registry, RecordingClients Clients)
        BuildHub(string connectionId, RoomRegistry registry)
    {
        // TestSeatGrace.NoOp binds a grace service with a LONG (5-minute) window to the
        // SAME registry, so an OnDisconnected schedules a hold whose deferred eviction
        // never fires during these synchronous assertions (the DEFERRED half is covered
        // by SeatGraceServiceTests with a tiny window).
        var hub = new GameHub(registry, new ContentSafetyFilter(), new TemplateCatalog(), new FamilySafeContentSelector(), new LengthContentSelector(), new FreshnessContentSelector(), new FakeTelemetrySink(), TestTelemetry.NoOp, new DefaultUnlockedEntitlementService(), TestSeatGrace.NoOp(registry), new PurchaserCredentialService(new EphemeralDataProtectionProvider()), new ConnectionEntitlementStore(), new FamilyDeviceLinkService(new InMemoryFamilyLinkCodeStore(), new InMemoryFamilyDeviceTokenStore()), new InMemoryAccountStore(), new AdultSignalResolutionService(new PurchaserCredentialService(new EphemeralDataProtectionProvider()), new FamilyDeviceLinkService(new InMemoryFamilyLinkCodeStore(), new InMemoryFamilyDeviceTokenStore())), NullLogger<GameHub>.Instance);
        var clients = new RecordingClients();
        hub.Clients = clients;
        hub.Groups = new NoopGroups();
        hub.Context = new FakeHubCallerContext(connectionId);
        return (hub, registry, clients);
    }

    [Fact]
    public async Task OnDisconnected_holds_the_seat_and_broadcasts_the_roster_with_the_seat_still_present()
    {
        var registry = new RoomRegistry();
        var room = registry.CreateRoom("conn-host", "Mossy", "teal");
        Assert.True(room.TryAddPlayer("Maple", "gold", "conn-joiner"));

        // The joiner's connection drops abnormally (session-engine/07: a transient blip,
        // not a deliberate leave).
        var (hub, _, clients) = BuildHub("conn-joiner", registry);
        await hub.OnDisconnectedAsync(new Exception("network blip"));

        // AC-01: the seat is HELD, not removed - the broadcast roster still reports BOTH
        // players, the dropped one flagged not-connected, and the room is not freed.
        Assert.Equal(room.Code, clients.LastGroupName);
        Assert.Equal("RosterChanged", clients.LastMethod);
        var broadcast = Assert.IsType<RoomStateDto>(clients.LastArgs![0]);
        Assert.Equal(2, broadcast.Players.Count);
        var maple = Assert.Single(broadcast.Players, p => p.Nickname == "Maple");
        Assert.False(maple.Connected);              // flagged not-connected (AC-01)
        Assert.Equal(2, room.PlayerCount);          // player count unchanged (AC-01)
        Assert.Equal(1, registry.ActiveRoomCount);  // room still live during grace
    }

    [Fact]
    public async Task OnDisconnected_during_a_prompting_round_does_not_abort_it()
    {
        var registry = new RoomRegistry();
        var room = registry.CreateRoom("conn-host", "Mossy", "teal");
        Assert.True(room.TryAddPlayer("Maple", "gold", "conn-joiner"));
        room.StartRound("tmpl", "classic-blind", blankCount: 4);
        Assert.NotNull(room.CurrentRound);

        var (hub, _, clients) = BuildHub("conn-joiner", registry);
        await hub.OnDisconnectedAsync(new Exception("network blip"));

        // AC-02: the round keeps collecting - it is NOT aborted synchronously on the drop
        // (the other seated players keep going; the held seat's blanks just stay open). The
        // only broadcast is the roster refresh, never a RoundAborted.
        Assert.NotNull(room.CurrentRound);
        Assert.Equal("prompting", room.CurrentRound!.Phase);
        Assert.Equal("RosterChanged", clients.LastMethod);
        Assert.NotEqual("RoundAborted", clients.LastMethod);
        Assert.Equal(2, room.PlayerCount);
    }

    [Fact]
    public async Task OnDisconnected_of_the_last_player_holds_the_seat_and_keeps_the_room_during_grace()
    {
        var registry = new RoomRegistry();
        var room = registry.CreateRoom("conn-host", "Mossy", "teal"); // host is the only player

        var (hub, _, clients) = BuildHub("conn-host", registry);
        await hub.OnDisconnectedAsync(new Exception("network blip"));

        // AC-05: the seat is held for the grace window, so the room is NOT freed on the
        // spot (it frees only once the seat is evicted after grace). The roster still
        // reports the held seat.
        Assert.Equal("RosterChanged", clients.LastMethod);
        Assert.Equal(1, registry.ActiveRoomCount);
        Assert.NotNull(registry.TryGet(room.Code));
    }

    [Fact]
    public async Task OnDisconnected_of_an_unseated_connection_is_a_no_op()
    {
        var registry = new RoomRegistry();
        registry.CreateRoom("conn-host", "Mossy", "teal");

        var (hub, _, clients) = BuildHub("conn-stranger", registry);
        await hub.OnDisconnectedAsync(null);

        Assert.Null(clients.LastMethod); // no broadcast
        Assert.Equal(1, registry.ActiveRoomCount);
    }

    [Fact]
    public async Task LeaveRoom_removes_the_caller_from_the_group_and_broadcasts_the_trimmed_roster()
    {
        var registry = new RoomRegistry();
        var room = registry.CreateRoom("conn-host", "Mossy", "teal");
        Assert.True(room.TryAddPlayer("Maple", "gold", "conn-joiner"));

        var hub = new GameHub(registry, new ContentSafetyFilter(), new TemplateCatalog(), new FamilySafeContentSelector(), new LengthContentSelector(), new FreshnessContentSelector(), new FakeTelemetrySink(), TestTelemetry.NoOp, new DefaultUnlockedEntitlementService(), TestSeatGrace.NoOp(registry), new PurchaserCredentialService(new EphemeralDataProtectionProvider()), new ConnectionEntitlementStore(), new FamilyDeviceLinkService(new InMemoryFamilyLinkCodeStore(), new InMemoryFamilyDeviceTokenStore()), new InMemoryAccountStore(), new AdultSignalResolutionService(new PurchaserCredentialService(new EphemeralDataProtectionProvider()), new FamilyDeviceLinkService(new InMemoryFamilyLinkCodeStore(), new InMemoryFamilyDeviceTokenStore())), NullLogger<GameHub>.Instance);
        var clients = new RecordingClients();
        var groups = new RecordingGroups();
        hub.Clients = clients;
        hub.Groups = groups;
        hub.Context = new FakeHubCallerContext("conn-joiner");

        await hub.LeaveRoom(room.Code);

        // Removed from the room group so no further broadcast reaches the leaver.
        Assert.Equal(("conn-joiner", room.Code), groups.LastRemove);
        // Survivors (the host) get the trimmed roster (AC-04).
        Assert.Equal(room.Code, clients.LastGroupName);
        Assert.Equal("RosterChanged", clients.LastMethod);
        var broadcast = Assert.IsType<RoomStateDto>(clients.LastArgs![0]);
        Assert.Single(broadcast.Players);
        Assert.DoesNotContain(broadcast.Players, p => p.Nickname == "Maple");
    }

    [Fact]
    public async Task LeaveRoom_by_the_host_promotes_a_survivor_and_nudges_them_with_HostGranted()
    {
        // room-start-duplicate-members: the exact "creator pressed back" case. The host
        // leaves a two-person room; the survivor must inherit the host flag (so the room
        // stays startable) AND be told directly, because the anonymous roster broadcast
        // cannot tell a specific client "you are the host now".
        var registry = new RoomRegistry();
        var room = registry.CreateRoom("conn-host", "Mossy", "teal");
        Assert.True(room.TryAddPlayer("Maple", "gold", "conn-joiner"));

        var (hub, _, clients) = BuildHub("conn-host", registry); // the HOST leaves
        await hub.LeaveRoom(room.Code);

        // Server-side: the lone survivor is now the host, so the room is not stranded.
        var survivor = Assert.Single(registry.TryGet(room.Code)!.SnapshotPlayers());
        Assert.Equal("conn-joiner", survivor.ConnectionId);
        Assert.True(survivor.IsHost);

        // Client-side nudge: the LAST send (after the roster refresh) is a "HostGranted"
        // addressed to exactly the promoted connection - a targeted Client(...) send, never
        // a group broadcast.
        Assert.Equal("HostGranted", clients.LastMethod);
        Assert.Null(clients.LastGroupName);
        Assert.Equal("conn-joiner", clients.LastClientId);
    }

    [Fact]
    public async Task LeaveRoom_by_a_non_host_sends_no_HostGranted_nudge()
    {
        // The complement: a NON-host leaving migrates nothing, so no one is nudged - the
        // last (and only) broadcast is the trimmed roster to the group.
        var registry = new RoomRegistry();
        var room = registry.CreateRoom("conn-host", "Mossy", "teal");
        Assert.True(room.TryAddPlayer("Maple", "gold", "conn-joiner"));

        var (hub, _, clients) = BuildHub("conn-joiner", registry); // a JOINER leaves
        await hub.LeaveRoom(room.Code);

        Assert.Equal("RosterChanged", clients.LastMethod); // not HostGranted
        Assert.Equal(room.Code, clients.LastGroupName);
        Assert.Null(clients.LastClientId);
        // The original host is untouched.
        var host = Assert.Single(registry.TryGet(room.Code)!.SnapshotPlayers());
        Assert.Equal("conn-host", host.ConnectionId);
        Assert.True(host.IsHost);
    }

    [Fact]
    public async Task LeaveRoom_during_a_prompting_round_evicts_immediately_and_aborts_no_grace()
    {
        // AC-04: a DELIBERATE leave mid-round is unaffected by the session-engine/07
        // grace window - the seat is evicted synchronously and the now-uncompletable
        // round aborts on the spot, exactly as before this story. This pins that
        // behavior right beside the new grace path (contrast
        // OnDisconnected_during_a_prompting_round_does_not_abort_it, where a transient
        // DROP holds the seat: PlayerCount stays 2 and the round keeps prompting).
        var registry = new RoomRegistry();
        var room = registry.CreateRoom("conn-host", "Mossy", "teal");
        Assert.True(room.TryAddPlayer("Maple", "gold", "conn-joiner"));
        room.StartRound("tmpl", "classic-blind", blankCount: 4);
        Assert.Equal("prompting", room.CurrentRound!.Phase);

        var hub = new GameHub(registry, new ContentSafetyFilter(), new TemplateCatalog(), new FamilySafeContentSelector(), new LengthContentSelector(), new FreshnessContentSelector(), new FakeTelemetrySink(), TestTelemetry.NoOp, new DefaultUnlockedEntitlementService(), TestSeatGrace.NoOp(registry), new PurchaserCredentialService(new EphemeralDataProtectionProvider()), new ConnectionEntitlementStore(), new FamilyDeviceLinkService(new InMemoryFamilyLinkCodeStore(), new InMemoryFamilyDeviceTokenStore()), new InMemoryAccountStore(), new AdultSignalResolutionService(new PurchaserCredentialService(new EphemeralDataProtectionProvider()), new FamilyDeviceLinkService(new InMemoryFamilyLinkCodeStore(), new InMemoryFamilyDeviceTokenStore())), NullLogger<GameHub>.Instance);
        hub.Clients = new RecordingClients();
        hub.Groups = new RecordingGroups();
        hub.Context = new FakeHubCallerContext("conn-joiner");

        await hub.LeaveRoom(room.Code);

        // The seat is gone immediately (no grace hold) and the round aborted synchronously.
        Assert.Equal(1, room.PlayerCount);
        Assert.Null(room.CurrentRound);
        Assert.NotNull(registry.TryGet(room.Code)); // the host's room survives
    }

    [Fact]
    public async Task LeaveRoom_of_the_last_player_drops_the_room_and_does_not_broadcast()
    {
        var registry = new RoomRegistry();
        var room = registry.CreateRoom("conn-host", "Mossy", "teal");

        var hub = new GameHub(registry, new ContentSafetyFilter(), new TemplateCatalog(), new FamilySafeContentSelector(), new LengthContentSelector(), new FreshnessContentSelector(), new FakeTelemetrySink(), TestTelemetry.NoOp, new DefaultUnlockedEntitlementService(), TestSeatGrace.NoOp(registry), new PurchaserCredentialService(new EphemeralDataProtectionProvider()), new ConnectionEntitlementStore(), new FamilyDeviceLinkService(new InMemoryFamilyLinkCodeStore(), new InMemoryFamilyDeviceTokenStore()), new InMemoryAccountStore(), new AdultSignalResolutionService(new PurchaserCredentialService(new EphemeralDataProtectionProvider()), new FamilyDeviceLinkService(new InMemoryFamilyLinkCodeStore(), new InMemoryFamilyDeviceTokenStore())), NullLogger<GameHub>.Instance);
        var clients = new RecordingClients();
        hub.Clients = clients;
        hub.Groups = new RecordingGroups();
        hub.Context = new FakeHubCallerContext("conn-host");

        await hub.LeaveRoom(room.Code);

        Assert.Null(clients.LastMethod);
        Assert.Equal(0, registry.ActiveRoomCount);
        Assert.Null(registry.TryGet(room.Code));
    }

    [Fact]
    public async Task LeaveRoom_of_an_unseated_connection_is_a_no_op()
    {
        var registry = new RoomRegistry();
        registry.CreateRoom("conn-host", "Mossy", "teal");

        var hub = new GameHub(registry, new ContentSafetyFilter(), new TemplateCatalog(), new FamilySafeContentSelector(), new LengthContentSelector(), new FreshnessContentSelector(), new FakeTelemetrySink(), TestTelemetry.NoOp, new DefaultUnlockedEntitlementService(), TestSeatGrace.NoOp(registry), new PurchaserCredentialService(new EphemeralDataProtectionProvider()), new ConnectionEntitlementStore(), new FamilyDeviceLinkService(new InMemoryFamilyLinkCodeStore(), new InMemoryFamilyDeviceTokenStore()), new InMemoryAccountStore(), new AdultSignalResolutionService(new PurchaserCredentialService(new EphemeralDataProtectionProvider()), new FamilyDeviceLinkService(new InMemoryFamilyLinkCodeStore(), new InMemoryFamilyDeviceTokenStore())), NullLogger<GameHub>.Instance);
        var clients = new RecordingClients();
        hub.Clients = clients;
        hub.Groups = new RecordingGroups();
        hub.Context = new FakeHubCallerContext("conn-stranger");

        await hub.LeaveRoom("WXYZ");

        Assert.Null(clients.LastMethod); // no broadcast
        Assert.Equal(1, registry.ActiveRoomCount);
    }

    // --- session-engine/07: the reconnect token (AC-06) --------------------------

    [Fact]
    public async Task CreateRoom_returns_a_reconnect_token_to_the_caller()
    {
        var registry = new RoomRegistry();
        var (hub, _, _) = BuildHub("conn-host", registry);

        var result = await hub.CreateRoom("Mossy", "teal");

        // AC-06: the host gets its OWN opaque reconnect token in its own envelope.
        Assert.True(result.Ok);
        Assert.False(string.IsNullOrWhiteSpace(result.ReconnectToken));
    }

    [Fact]
    public async Task JoinRoom_returns_a_reconnect_token_to_the_joiner()
    {
        var registry = new RoomRegistry();
        var room = registry.CreateRoom("conn-host", "Mossy", "teal");
        var (hub, _, _) = BuildHub("conn-joiner", registry);

        var result = await hub.JoinRoom(room.Code, "Maple", "gold");

        // AC-06: the joiner gets its OWN token in its own envelope.
        Assert.True(result.Ok);
        Assert.False(string.IsNullOrWhiteSpace(result.ReconnectToken));
    }

    [Fact]
    public void The_reconnect_token_is_present_on_the_caller_envelopes_and_absent_from_the_broadcast_roster()
    {
        // AC-06 (seat-hijack guard): the token is CALLER-ONLY. It must never appear on
        // RoomStateDto / PlayerDto (the roster shape every player in the room receives),
        // or another player could read it and hijack the seat.
        Assert.Null(typeof(PlayerDto).GetProperty("ReconnectToken"));
        Assert.Null(typeof(PlayerDto).GetProperty("Token"));
        Assert.Null(typeof(RoomStateDto).GetProperty("ReconnectToken"));

        // ...but the create/join result envelopes DO carry it for the owning caller.
        Assert.NotNull(typeof(CreateRoomResultDto).GetProperty("ReconnectToken"));
        Assert.NotNull(typeof(JoinResultDto).GetProperty("ReconnectToken"));

        // The Connected flag, by contrast, IS on the roster shape (web story 10 renders
        // the "reconnecting" tile from it).
        Assert.NotNull(typeof(PlayerDto).GetProperty("Connected"));
    }

    // --- Minimal SignalR fakes ------------------------------------------------

    // Records the last group broadcast so the test can assert on it.
    private sealed class RecordingClients : IHubCallerClients
    {
        public string? LastGroupName { get; private set; }
        public string? LastMethod { get; private set; }
        public object?[]? LastArgs { get; private set; }
        // room-start-duplicate-members: the connection id of the last targeted Client(...)
        // send (null for a group broadcast), so a test can assert the "HostGranted" nudge
        // reached exactly the promoted connection rather than the whole room.
        public string? LastClientId { get; private set; }

        private IClientProxy MakeProxy(string? groupName, string? clientId = null) =>
            new RecordingProxy(this, groupName, clientId);

        public IClientProxy Group(string groupName) => MakeProxy(groupName);
        public IClientProxy All => MakeProxy(null);
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => MakeProxy(null);
        public IClientProxy Caller => MakeProxy(null);
        public IClientProxy Client(string connectionId) => MakeProxy(null, connectionId);
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => MakeProxy(null);
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => MakeProxy(groupName);
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => MakeProxy(null);
        public IClientProxy OthersInGroup(string groupName) => MakeProxy(groupName);
        public IClientProxy Others => MakeProxy(null);
        public IClientProxy User(string userId) => MakeProxy(null);
        public IClientProxy Users(IReadOnlyList<string> userIds) => MakeProxy(null);

        private sealed class RecordingProxy(RecordingClients owner, string? groupName, string? clientId) : IClientProxy, ISingleClientProxy
        {
            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
            {
                owner.LastGroupName = groupName;
                owner.LastClientId = clientId;
                owner.LastMethod = method;
                owner.LastArgs = args;
                return Task.CompletedTask;
            }

            public Task<T> InvokeCoreAsync<T>(string method, object?[] args, CancellationToken cancellationToken = default)
                => Task.FromResult(default(T)!);
        }
    }

    private sealed class NoopGroups : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    // Records the last RemoveFromGroup call so a LeaveRoom test can assert the
    // caller was taken out of the room's group.
    private sealed class RecordingGroups : IGroupManager
    {
        public (string ConnectionId, string GroupName)? LastRemove { get; private set; }

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            LastRemove = (connectionId, groupName);
            return Task.CompletedTask;
        }
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
