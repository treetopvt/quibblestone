// ----------------------------------------------------------------------------
//  GameHubJoinTests - unit tests for the JoinRoom hub method (session-engine/02).
//
//  These exercise the REAL GameHub.JoinRoom against the REAL RoomRegistry and
//  ContentSafetyFilter (no mocking framework is in the harness), locking in the
//  join contract the web client and later stories depend on:
//
//    1. AC-04: an unknown / expired code is not joined - Ok=false, friendly Error.
//    2. AC-03: an empty or over-14-char display name is rejected before storage.
//    3. AC-03 (child safety): a name the content-safety filter blocks is rejected
//       with the filter's friendly message and NEVER added to the roster.
//    4. AC-06: a name already in the room (case-insensitive) is rejected.
//    5. AC-01 / AC-05: a valid join is added to the roster, subscribed to the
//       room's SignalR group, and triggers a "RosterChanged" broadcast to the
//       room carrying the updated roster.
//
//  The hub's SignalR surface (Clients / Groups / Context) is faked with small
//  hand-rolled stubs below - just enough to observe the group subscription and
//  the broadcast without standing up a real SignalR pipeline.
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

public class GameHubJoinTests
{
    // Builds a hub wired to a fresh registry + the real safety filter, with a
    // fake SignalR surface for the given connection id. Returns the hub and the
    // recorder so tests can assert on the broadcast / group subscription.
    private static (GameHub Hub, RoomRegistry Registry, RecordingClients Clients, RecordingGroups Groups)
        BuildHub(string connectionId)
    {
        var registry = new RoomRegistry();
        var hub = new GameHub(registry, new ContentSafetyFilter(), new TemplateCatalog(), new FamilySafeContentSelector(), new LengthContentSelector(), new FreshnessContentSelector(), new FakeTelemetrySink(), TestTelemetry.NoOp, new DefaultUnlockedEntitlementService(), TestSeatGrace.NoOp(registry), new PurchaserCredentialService(new EphemeralDataProtectionProvider()), new ConnectionEntitlementStore(), new FamilyDeviceLinkService(new InMemoryFamilyLinkCodeStore(), new InMemoryFamilyDeviceTokenStore()), new InMemoryAccountStore(), new AdultSignalResolutionService(new PurchaserCredentialService(new EphemeralDataProtectionProvider()), new FamilyDeviceLinkService(new InMemoryFamilyLinkCodeStore(), new InMemoryFamilyDeviceTokenStore())), NullLogger<GameHub>.Instance);

        var clients = new RecordingClients();
        var groups = new RecordingGroups();
        hub.Clients = clients;
        hub.Groups = groups;
        hub.Context = new FakeHubCallerContext(connectionId);

        return (hub, registry, clients, groups);
    }

    [Fact]
    public async Task JoinRoom_with_unknown_code_is_rejected_and_not_joined()
    {
        var (hub, _, _, groups) = BuildHub("conn-joiner");

        var result = await hub.JoinRoom("ZZZZ", "Maple", "teal");

        Assert.False(result.Ok);
        Assert.Null(result.Room);
        Assert.NotNull(result.Error);
        Assert.Empty(groups.Added); // never subscribed to a group
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("thisnameiswaytoolong")] // > 14 chars
    public async Task JoinRoom_with_bad_length_name_is_rejected(string badName)
    {
        var (hub, registry, _, _) = BuildHub("conn-joiner");
        var code = registry.CreateRoom("conn-host", "Mossy", "teal").Code;

        var result = await hub.JoinRoom(code, badName, "teal");

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
        // The roster still has only the host - no partial add.
        Assert.Single(registry.TryGet(code)!.SnapshotPlayers());
    }

    [Fact]
    public async Task JoinRoom_with_a_blocked_name_is_rejected_and_never_stored()
    {
        var (hub, registry, _, _) = BuildHub("conn-joiner");
        var code = registry.CreateRoom("conn-host", "Mossy", "teal").Code;

        // A term on the shipped baseline blocklist.
        var result = await hub.JoinRoom(code, "fuck", "teal");

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
        // The unfiltered name must never reach the roster (child safety).
        var players = registry.TryGet(code)!.SnapshotPlayers();
        Assert.Single(players); // host only
        Assert.DoesNotContain(players, p => p.Nickname.Equals("fuck", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task JoinRoom_with_a_duplicate_name_is_rejected_case_insensitively()
    {
        var (hub, registry, _, _) = BuildHub("conn-a");
        var code = registry.CreateRoom("conn-host", "Mossy", "teal").Code;

        var first = await hub.JoinRoom(code, "Maple", "teal");
        Assert.True(first.Ok);

        // A second joiner (different connection) tries the same name, different case.
        hub.Context = new FakeHubCallerContext("conn-b");
        var second = await hub.JoinRoom(code, "maple", "teal");

        Assert.False(second.Ok);
        Assert.NotNull(second.Error);
        Assert.Equal(2, registry.TryGet(code)!.SnapshotPlayers().Count); // host + first only
    }

    [Fact]
    public async Task JoinRoom_success_adds_player_subscribes_group_and_broadcasts_roster()
    {
        var (hub, registry, clients, groups) = BuildHub("conn-joiner");
        var code = registry.CreateRoom("conn-host", "Mossy", "teal").Code;

        var result = await hub.JoinRoom(code, "  Maple  ", "gold");

        Assert.True(result.Ok);
        Assert.NotNull(result.Room);
        Assert.Null(result.Error);

        // Name is trimmed and stored; variant is honored; not marked host (AC-01).
        var joiner = Assert.Single(result.Room!.Players, p => p.Nickname == "Maple");
        Assert.Equal("gold", joiner.Variant);
        Assert.False(joiner.IsHost);

        // Subscribed to the room's SignalR group (AC-05).
        Assert.Contains((code, "conn-joiner"), groups.Added);

        // Broadcast "RosterChanged" to the room's group with the updated roster.
        Assert.Equal(code, clients.LastGroupName);
        Assert.Equal("RosterChanged", clients.LastMethod);
        var broadcast = Assert.IsType<RoomStateDto>(clients.LastArgs![0]);
        Assert.Contains(broadcast.Players, p => p.Nickname == "Maple");
    }

    [Fact]
    public async Task JoinRoom_defaults_variant_to_teal_when_missing()
    {
        var (hub, registry, _, _) = BuildHub("conn-joiner");
        var code = registry.CreateRoom("conn-host", "Mossy", "teal").Code;

        var result = await hub.JoinRoom(code, "Wren", "");

        Assert.True(result.Ok);
        var joiner = Assert.Single(result.Room!.Players, p => p.Nickname == "Wren");
        Assert.Equal("teal", joiner.Variant);
    }

    // session-engine/05, AC-03: the server is the source of truth for the
    // variant whitelist - an unknown/malformed value must never reach room
    // state, and a known value must be preserved (not silently mangled).
    [Theory]
    [InlineData("not-a-real-variant")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("PURPLE-ish")]
    public async Task JoinRoom_normalizes_unknown_variant_to_teal(string unknownVariant)
    {
        var (hub, registry, _, _) = BuildHub("conn-joiner");
        var code = registry.CreateRoom("conn-host", "Mossy", "teal").Code;

        var result = await hub.JoinRoom(code, "Bramble", unknownVariant);

        Assert.True(result.Ok);
        var joiner = Assert.Single(result.Room!.Players, p => p.Nickname == "Bramble");
        Assert.Equal("teal", joiner.Variant);
    }

    [Fact]
    public async Task JoinRoom_preserves_a_known_variant()
    {
        var (hub, registry, _, _) = BuildHub("conn-joiner");
        var code = registry.CreateRoom("conn-host", "Mossy", "teal").Code;

        var result = await hub.JoinRoom(code, "Flint", "coral");

        Assert.True(result.Ok);
        var joiner = Assert.Single(result.Room!.Players, p => p.Nickname == "Flint");
        Assert.Equal("coral", joiner.Variant);
    }

    // W2/F2: the room fills to Room.MaxPlayers (host included), then the next joiner
    // is turned away with a friendly "room's full" message - no partial add.
    [Fact]
    public async Task JoinRoom_fills_to_the_cap_then_rejects_the_next_with_a_friendly_full_message()
    {
        var (hub, registry, _, _) = BuildHub("conn-seed");
        var code = registry.CreateRoom("conn-host", "Host", "teal").Code; // host = seat 1

        // Seat joiners up to the cap, reusing the one hub by swapping the connection
        // id per joiner (the same pattern the duplicate-name test uses).
        for (var i = 1; i < Room.MaxPlayers; i += 1)
        {
            hub.Context = new FakeHubCallerContext($"conn-{i}");
            var join = await hub.JoinRoom(code, $"P{i}", "gold");
            Assert.True(join.Ok, $"joiner {i} within the cap should be seated");
        }
        Assert.Equal(Room.MaxPlayers, registry.TryGet(code)!.PlayerCount);

        // The next joiner (would be MaxPlayers + 1) is refused: Ok=false, no room, a
        // friendly message that says the room is full, and no partial add.
        hub.Context = new FakeHubCallerContext("conn-overflow");
        var overflow = await hub.JoinRoom(code, "OneTooMany", "coral");

        Assert.False(overflow.Ok);
        Assert.Null(overflow.Room);
        Assert.NotNull(overflow.Error);
        Assert.Contains("full", overflow.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Room.MaxPlayers, registry.TryGet(code)!.PlayerCount);
        Assert.DoesNotContain(registry.TryGet(code)!.SnapshotPlayers(), p => p.Nickname == "OneTooMany");
    }

    // --- session-engine/13 (AC-02/W4): JoinRoom's mid-round guard -------------

    [Fact]
    public async Task JoinRoom_rejects_a_join_while_the_round_is_prompting()
    {
        var (hub, registry, clients, groups) = BuildHub("conn-newcomer");
        var room = registry.CreateRoom("conn-host", "Mossy", "teal");
        Assert.True(room.TryAddPlayer("Maple", "gold", "conn-joiner"));
        room.StartRound("wobbly-wizard", "classic-blind", 2);

        var result = await hub.JoinRoom(room.Code, "Newcomer", "teal");

        Assert.False(result.Ok);
        Assert.Null(result.Room);
        Assert.NotNull(result.Error);
        Assert.Equal(2, room.PlayerCount); // no partial seat
        Assert.Empty(groups.Added);        // never subscribed
        Assert.Null(clients.LastMethod);   // no RosterChanged broadcast
    }

    [Fact]
    public async Task JoinRoom_rejects_a_join_while_the_round_is_in_reveal()
    {
        var (hub, registry, clients, groups) = BuildHub("conn-newcomer");
        var room = registry.CreateRoom("conn-host", "Mossy", "teal");
        Assert.True(room.TryAddPlayer("Maple", "gold", "conn-joiner"));
        room.StartRound("wobbly-wizard", "classic-blind", 2);
        Assert.Equal(Room.SubmitOutcome.Recorded, room.RecordSubmission("conn-host", 0, "banana"));
        Assert.Equal(Room.SubmitOutcome.RoundComplete, room.RecordSubmission("conn-joiner", 1, "wobbly"));
        Assert.Equal("reveal", room.CurrentRound!.Phase);

        var result = await hub.JoinRoom(room.Code, "Newcomer", "teal");

        Assert.False(result.Ok);
        Assert.Null(result.Room);
        Assert.NotNull(result.Error);
        Assert.Equal(2, room.PlayerCount);
        Assert.Empty(groups.Added);
        Assert.Null(clients.LastMethod);
    }

    [Fact]
    public async Task JoinRoom_succeeds_again_once_the_round_returns_to_lobby()
    {
        var (hub, registry, _, _) = BuildHub("conn-newcomer");
        var room = registry.CreateRoom("conn-host", "Mossy", "teal");
        Assert.True(room.TryAddPlayer("Maple", "gold", "conn-joiner"));
        room.StartRound("wobbly-wizard", "classic-blind", 2);
        room.BackToLobby();

        var result = await hub.JoinRoom(room.Code, "Newcomer", "teal");

        Assert.True(result.Ok);
        Assert.NotNull(result.Room);
        Assert.Null(result.Error);
        Assert.Equal(3, room.PlayerCount);
    }

    // --- Minimal SignalR fakes ------------------------------------------------

    // Records group broadcasts. Every Group(...)/All/etc. returns the same proxy
    // so the last SendCoreAsync call is captured for assertions.
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

    // Records group add/remove calls so the test can assert the subscription.
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

    // A hub context that only carries the connection id the hub reads.
    private sealed class FakeHubCallerContext(string connectionId) : HubCallerContext
    {
        public override string ConnectionId { get; } = connectionId;
        public override string? UserIdentifier => null;
        public override System.Security.Claims.ClaimsPrincipal? User => null;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new Microsoft.AspNetCore.Http.Features.FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }
}
