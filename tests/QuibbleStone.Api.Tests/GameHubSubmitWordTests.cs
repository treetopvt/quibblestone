// ----------------------------------------------------------------------------
//  GameHubSubmitWordTests - unit tests for GameHub.SubmitWord (group-play/03),
//  the other half of the highest-value coverage gap flagged in review on PR #48
//  (Room.cs + GameHub.SubmitWord had NO coverage before this file).
//
//  The focus here is the ONE non-negotiable child-safety contract (README
//  section 6, AC-01/AC-06): EVERY submitted word must be routed through
//  IContentSafetyFilter BEFORE it is ever recorded. We use a hand-rolled SPY
//  filter (no mocking framework in the harness) that records every candidate it
//  is asked to check and can be told to allow or block, so a test can assert
//  BOTH that the check happened AND what it did (or did not) let through:
//
//    1. An ALLOWED word: the spy recorded the candidate, the word IS recorded
//       server-side (visible via Room.CurrentRound.Submissions / BuildReveal),
//       and the hub returns Ok=true.
//    2. A BLOCKED word: the spy still recorded the candidate (it WAS checked),
//       but nothing is recorded - the blank stays unfilled/absent from
//       Submissions - and the hub returns Ok=false with the filter's message.
//       No "RevealReady" broadcast or other record side effect follows.
//    3. The safety check demonstrably happens BEFORE RecordSubmission: with the
//       spy blocking, Room.CurrentRound.Submissions stays completely empty.
//
//  The SignalR surface (Clients / Groups / Context) is faked with the same
//  hand-rolled stubs used by GameHubJoinTests.cs / GameHubDisconnectTests.cs
//  (copied into this file, per that file's own convention of each test file
//  owning its fakes).
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

public class GameHubSubmitWordTests
{
    // Builds a hub wired to the given registry + a SPY safety filter, with a
    // fake SignalR surface for the given connection id.
    private static (GameHub Hub, RecordingClients Clients, RecordingGroups Groups) BuildHub(
        RoomRegistry registry,
        SpySafetyFilter safety,
        string connectionId)
    {
        var hub = new GameHub(registry, safety, new TemplateCatalog(), new FamilySafeContentSelector(), new LengthContentSelector(), new FreshnessContentSelector(), new FakeTelemetrySink(), TestTelemetry.NoOp, new DefaultUnlockedEntitlementService(), TestSeatGrace.NoOp(registry), new PurchaserCredentialService(new EphemeralDataProtectionProvider()), new ConnectionEntitlementStore(), new FamilyDeviceLinkService(new InMemoryFamilyLinkCodeStore(), new InMemoryFamilyDeviceTokenStore()), new InMemoryAccountStore(), new AdultSignalResolutionService(new PurchaserCredentialService(new EphemeralDataProtectionProvider()), new FamilyDeviceLinkService(new InMemoryFamilyLinkCodeStore(), new InMemoryFamilyDeviceTokenStore())), NullLogger<GameHub>.Instance);
        var clients = new RecordingClients();
        var groups = new RecordingGroups();
        hub.Clients = clients;
        hub.Groups = groups;
        hub.Context = new FakeHubCallerContext(connectionId);
        return (hub, clients, groups);
    }

    [Fact]
    public async Task SubmitWord_routes_the_candidate_through_the_safety_filter_before_recording()
    {
        var registry = new RoomRegistry();
        var room = registry.CreateRoom("conn-host", "Mossy", "teal");
        Assert.True(room.TryAddPlayer("Maple", "gold", "conn-joiner"));
        room.StartRound("wobbly-wizard", "classic-blind", 5);

        var safety = new SpySafetyFilter(allow: true);
        var (hub, _, _) = BuildHub(registry, safety, "conn-host");

        // Host (conn-host) owns blank 0.
        var result = await hub.SubmitWord(room.Code, 0, "banana");

        Assert.True(result.Ok);
        Assert.Contains("banana", safety.CheckedCandidates);
    }

    [Fact]
    public async Task SubmitWord_with_an_allowed_word_is_recorded_and_visible_in_the_reveal()
    {
        var registry = new RoomRegistry();
        var room = registry.CreateRoom("conn-host", "Mossy", "teal");
        Assert.True(room.TryAddPlayer("Maple", "gold", "conn-joiner"));
        room.StartRound("wobbly-wizard", "classic-blind", 5);

        var safety = new SpySafetyFilter(allow: true);
        var (hub, clients, _) = BuildHub(registry, safety, "conn-host");

        var result = await hub.SubmitWord(room.Code, 0, "banana");

        Assert.True(result.Ok);
        Assert.Null(result.Error);
        Assert.Contains("banana", safety.CheckedCandidates);

        // Recorded server-side: visible via CurrentRound.Submissions.
        var round = room.CurrentRound!;
        Assert.True(round.Submissions.ContainsKey(0));
        Assert.Equal("banana", round.Submissions[0].Word);

        // "CollectProgress" broadcast happened (progress side effect of a
        // successful record); the round is not yet complete, so no RevealReady.
        Assert.Equal("CollectProgress", clients.LastMethod);
    }

    [Fact]
    public async Task SubmitWord_with_a_blocked_word_is_never_recorded_and_returns_the_filters_message()
    {
        var registry = new RoomRegistry();
        var room = registry.CreateRoom("conn-host", "Mossy", "teal");
        Assert.True(room.TryAddPlayer("Maple", "gold", "conn-joiner"));
        room.StartRound("wobbly-wizard", "classic-blind", 5);

        const string blockedMessage = "Let's try a different word - that one is not allowed here. Have another go!";
        var safety = new SpySafetyFilter(allow: false, message: blockedMessage);
        var (hub, clients, _) = BuildHub(registry, safety, "conn-host");

        var result = await hub.SubmitWord(room.Code, 0, "badword");

        Assert.False(result.Ok);
        Assert.Equal(blockedMessage, result.Error);

        // The candidate WAS checked...
        Assert.Contains("badword", safety.CheckedCandidates);

        // ...but nothing was recorded - the blank stays absent from Submissions.
        var round = room.CurrentRound!;
        Assert.False(round.Submissions.ContainsKey(0));
        Assert.Empty(round.Submissions);
        Assert.Equal("prompting", round.Phase);

        // No broadcast side effects at all (no CollectProgress, no RevealReady).
        Assert.Null(clients.LastMethod);
    }

    [Fact]
    public async Task SubmitWord_safety_check_happens_before_RecordSubmission_blocked_word_leaves_submissions_empty()
    {
        // A single-player room where the one blank would otherwise complete the
        // round immediately - if the safety gate ran AFTER recording (or not at
        // all), this would show a RoundComplete / RevealReady. It must not.
        var registry = new RoomRegistry();
        var room = registry.CreateRoom("conn-host", "Mossy", "teal");
        room.StartRound("wobbly-wizard", "classic-blind", 1);

        var safety = new SpySafetyFilter(allow: false, message: "blocked");
        var (hub, clients, _) = BuildHub(registry, safety, "conn-host");

        var result = await hub.SubmitWord(room.Code, 0, "nope");

        Assert.False(result.Ok);
        Assert.Empty(room.CurrentRound!.Submissions);
        Assert.Equal("prompting", room.CurrentRound!.Phase); // never advanced to reveal
        Assert.DoesNotContain(clients.LastMethod, new[] { "RevealReady", "CollectProgress" });
    }

    // --- Spy safety filter -------------------------------------------------------

    // Records every candidate it is asked to check, and returns a configurable
    // allow/block verdict. No mocking framework - a small hand-rolled fake,
    // matching the harness convention (see RoomRegistryTests / GameHubJoinTests).
    private sealed class SpySafetyFilter(bool allow, string message = "blocked") : IContentSafetyFilter
    {
        public List<string> CheckedCandidates { get; } = [];

        public ValueTask<ContentSafetyResult> CheckAsync(string? candidate, CancellationToken cancellationToken = default)
        {
            CheckedCandidates.Add(candidate ?? string.Empty);
            var verdict = allow ? ContentSafetyResult.Allowed : ContentSafetyResult.Blocked(message);
            return new ValueTask<ContentSafetyResult>(verdict);
        }
    }

    // --- Minimal SignalR fakes (copied from GameHubJoinTests.cs) ----------------

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
