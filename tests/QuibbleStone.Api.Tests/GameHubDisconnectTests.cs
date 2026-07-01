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

using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using QuibbleStone.Api.Content;
using QuibbleStone.Api.Hubs;
using QuibbleStone.Api.Rooms;
using QuibbleStone.Api.Safety;

namespace QuibbleStone.Api.Tests;

public class GameHubDisconnectTests
{
    private static (GameHub Hub, RoomRegistry Registry, RecordingClients Clients)
        BuildHub(string connectionId, RoomRegistry registry)
    {
        var hub = new GameHub(registry, new ContentSafetyFilter(), new TemplateCatalog(), new FamilySafeContentSelector());
        var clients = new RecordingClients();
        hub.Clients = clients;
        hub.Groups = new NoopGroups();
        hub.Context = new FakeHubCallerContext(connectionId);
        return (hub, registry, clients);
    }

    [Fact]
    public async Task OnDisconnected_broadcasts_the_trimmed_roster_when_members_remain()
    {
        var registry = new RoomRegistry();
        var room = registry.CreateRoom("conn-host");
        Assert.True(room.TryAddPlayer("Maple", "gold", "conn-joiner"));

        // The joiner's connection drops.
        var (hub, _, clients) = BuildHub("conn-joiner", registry);
        await hub.OnDisconnectedAsync(null);

        // Broadcast to the room with just the host left (AC-04).
        Assert.Equal(room.Code, clients.LastGroupName);
        Assert.Equal("RosterChanged", clients.LastMethod);
        var broadcast = Assert.IsType<RoomStateDto>(clients.LastArgs![0]);
        Assert.Single(broadcast.Players);
        Assert.DoesNotContain(broadcast.Players, p => p.Nickname == "Maple");
        Assert.Equal(1, registry.ActiveRoomCount);
    }

    [Fact]
    public async Task OnDisconnected_of_the_last_player_drops_the_room_and_does_not_broadcast()
    {
        var registry = new RoomRegistry();
        var room = registry.CreateRoom("conn-host"); // host is the only player

        var (hub, _, clients) = BuildHub("conn-host", registry);
        await hub.OnDisconnectedAsync(null);

        // No members left, so nothing to tell - and the room is swept immediately.
        Assert.Null(clients.LastMethod);
        Assert.Equal(0, registry.ActiveRoomCount);
        Assert.Null(registry.TryGet(room.Code));
    }

    [Fact]
    public async Task OnDisconnected_of_an_unseated_connection_is_a_no_op()
    {
        var registry = new RoomRegistry();
        registry.CreateRoom("conn-host");

        var (hub, _, clients) = BuildHub("conn-stranger", registry);
        await hub.OnDisconnectedAsync(null);

        Assert.Null(clients.LastMethod); // no broadcast
        Assert.Equal(1, registry.ActiveRoomCount);
    }

    [Fact]
    public async Task LeaveRoom_removes_the_caller_from_the_group_and_broadcasts_the_trimmed_roster()
    {
        var registry = new RoomRegistry();
        var room = registry.CreateRoom("conn-host");
        Assert.True(room.TryAddPlayer("Maple", "gold", "conn-joiner"));

        var hub = new GameHub(registry, new ContentSafetyFilter(), new TemplateCatalog(), new FamilySafeContentSelector());
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
    public async Task LeaveRoom_of_the_last_player_drops_the_room_and_does_not_broadcast()
    {
        var registry = new RoomRegistry();
        var room = registry.CreateRoom("conn-host");

        var hub = new GameHub(registry, new ContentSafetyFilter(), new TemplateCatalog(), new FamilySafeContentSelector());
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
        registry.CreateRoom("conn-host");

        var hub = new GameHub(registry, new ContentSafetyFilter(), new TemplateCatalog(), new FamilySafeContentSelector());
        var clients = new RecordingClients();
        hub.Clients = clients;
        hub.Groups = new RecordingGroups();
        hub.Context = new FakeHubCallerContext("conn-stranger");

        await hub.LeaveRoom("WXYZ");

        Assert.Null(clients.LastMethod); // no broadcast
        Assert.Equal(1, registry.ActiveRoomCount);
    }

    // --- Minimal SignalR fakes ------------------------------------------------

    // Records the last group broadcast so the test can assert on it.
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
