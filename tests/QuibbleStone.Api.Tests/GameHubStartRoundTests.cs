// ----------------------------------------------------------------------------
//  GameHubStartRoundTests - unit tests for GameHub.StartRound's story-length
//  parameter (story-selection/02).
//
//  These exercise the REAL GameHub.StartRound against the REAL RoomRegistry,
//  FamilySafeContentSelector, and LengthContentSelector (no mocking framework
//  is in the harness), locking in the server-authoritative posture the story's
//  acceptance criteria require:
//
//    - AC-03: when the host chooses "quick", the SERVER draws quick-first -
//      every template StartRound picks over many rounds is one of the catalog's
//      quick entries (BlankCount <= LengthContentSelector.QuickMaxBlanks), never
//      trusted from anything the client sends beyond the lengthPref argument
//      itself (there is no client-side template pick at all in Slice 1).
//    - AC-05: the length choice never weakens safety - with lengthPref "quick"
//      AND familySafe true, every template picked is also FamilySafe:true in the
//      catalog (the family-safe gate still runs FIRST, unconditionally).
//
//  The hub's SignalR surface (Clients / Groups / Context) is faked with the
//  same small hand-rolled stubs used by GameHubJoinTests.cs / GameHubSubmitWordTests.cs.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using QuibbleStone.Api.Content;
using QuibbleStone.Api.Hubs;
using QuibbleStone.Api.Rooms;
using QuibbleStone.Api.Safety;

namespace QuibbleStone.Api.Tests;

public class GameHubStartRoundTests
{
    // The catalog ids classified "quick" (BlankCount <= QuickMaxBlanks), per
    // TemplateCatalog.cs's current seed mirror. Kept as a small lookup set here
    // rather than re-deriving from the catalog, so a drift in the catalog's
    // BlankCount would show up as a naturally failing assertion below (built
    // from TemplateCatalog directly, not hand-duplicated ids - see BuildHub).
    private static readonly TemplateCatalog Catalog = new();

    private static (GameHub Hub, RoomRegistry Registry, RecordingClients Clients, RecordingGroups Groups)
        BuildHub(string connectionId)
    {
        var registry = new RoomRegistry();
        var hub = new GameHub(registry, new ContentSafetyFilter(), Catalog, new FamilySafeContentSelector(), new LengthContentSelector());

        var clients = new RecordingClients();
        var groups = new RecordingGroups();
        hub.Clients = clients;
        hub.Groups = groups;
        hub.Context = new FakeHubCallerContext(connectionId);

        return (hub, registry, clients, groups);
    }

    [Fact]
    public async Task StartRound_with_quick_lengthPref_always_picks_a_quick_catalog_entry()
    {
        var (hub, registry, _, _) = BuildHub("conn-host");
        var room = registry.CreateRoom("conn-host", "Mossy", "teal");
        Assert.True(room.TryAddPlayer("Maple", "gold", "conn-joiner"));

        var quickIds = Catalog.Entries
            .Where(e => e.BlankCount <= LengthContentSelector.QuickMaxBlanks)
            .Select(e => e.Id)
            .ToHashSet();
        Assert.NotEmpty(quickIds); // sanity: the seed catalog has quick entries today

        // Repeat StartRound (a fresh round each call, same room) many times so a
        // single lucky random draw cannot mask a broken filter (AC-03).
        for (var i = 0; i < 25; i++)
        {
            var result = await hub.StartRound(room.Code, familySafe: true, lengthPref: "quick");

            Assert.True(result.Ok, result.Error);
            Assert.Contains(room.CurrentRound!.TemplateId, quickIds);
        }
    }

    [Fact]
    public async Task StartRound_with_quick_and_family_safe_never_yields_a_non_family_safe_id()
    {
        var (hub, registry, _, _) = BuildHub("conn-host");
        var room = registry.CreateRoom("conn-host", "Mossy", "teal");
        Assert.True(room.TryAddPlayer("Maple", "gold", "conn-joiner"));

        var familySafeIds = Catalog.Entries.Where(e => e.FamilySafe).Select(e => e.Id).ToHashSet();

        for (var i = 0; i < 25; i++)
        {
            var result = await hub.StartRound(room.Code, familySafe: true, lengthPref: "quick");

            Assert.True(result.Ok, result.Error);
            // The family-safe gate runs FIRST, unconditionally (AC-05): the length
            // stage only ever sees what that gate already allowed.
            Assert.Contains(room.CurrentRound!.TemplateId, familySafeIds);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-real-preference")]
    public async Task StartRound_with_a_malformed_lengthPref_falls_back_to_any_rather_than_failing(string? malformed)
    {
        // A crafted/buggy client sending garbage must never break the round -
        // NormalizeLengthPreference degrades to "any" (no length filtering), the
        // SAME defensive posture as NormalizeVariant guarding an unknown variant.
        var (hub, registry, _, _) = BuildHub("conn-host");
        var room = registry.CreateRoom("conn-host", "Mossy", "teal");
        Assert.True(room.TryAddPlayer("Maple", "gold", "conn-joiner"));

        var result = await hub.StartRound(room.Code, familySafe: true, lengthPref: malformed!);

        Assert.True(result.Ok, result.Error);
        Assert.NotNull(room.CurrentRound);
    }

    // --- Minimal SignalR fakes (copied from GameHubJoinTests.cs / GameHubSubmitWordTests.cs) ---

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
