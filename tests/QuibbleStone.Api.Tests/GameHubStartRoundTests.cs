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
using Microsoft.Extensions.Logging.Abstractions;
using QuibbleStone.Api.Content;
using QuibbleStone.Api.Hubs;
using QuibbleStone.Api.Rooms;
using QuibbleStone.Api.Safety;
using QuibbleStone.Api.Telemetry;

namespace QuibbleStone.Api.Tests;

public class GameHubStartRoundTests
{
    // The catalog ids classified "quick" (BlankCount <= QuickMaxBlanks), per
    // TemplateCatalog.cs's current seed mirror. Kept as a small lookup set here
    // rather than re-deriving from the catalog, so a drift in the catalog's
    // BlankCount would show up as a naturally failing assertion below (built
    // from TemplateCatalog directly, not hand-duplicated ids - see BuildHub).
    private static readonly TemplateCatalog Catalog = new();

    private static (GameHub Hub, RoomRegistry Registry, RecordingClients Clients, RecordingGroups Groups, ITelemetrySink Telemetry)
        BuildHub(string connectionId, ITelemetrySink? telemetry = null)
    {
        var registry = new RoomRegistry();
        var sink = telemetry ?? new FakeTelemetrySink();
        var hub = new GameHub(registry, new ContentSafetyFilter(), Catalog, new FamilySafeContentSelector(), new LengthContentSelector(), new FreshnessContentSelector(), sink, TestTelemetry.NoOp, NullLogger<GameHub>.Instance);

        var clients = new RecordingClients();
        var groups = new RecordingGroups();
        hub.Clients = clients;
        hub.Groups = groups;
        hub.Context = new FakeHubCallerContext(connectionId);

        return (hub, registry, clients, groups, sink);
    }

    [Fact]
    public async Task StartRound_with_quick_lengthPref_always_picks_a_quick_catalog_entry()
    {
        var (hub, registry, _, _, _) = BuildHub("conn-host");
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
        var (hub, registry, _, _, _) = BuildHub("conn-host");
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
        var (hub, registry, _, _, _) = BuildHub("conn-host");
        var room = registry.CreateRoom("conn-host", "Mossy", "teal");
        Assert.True(room.TryAddPlayer("Maple", "gold", "conn-joiner"));

        var result = await hub.StartRound(room.Code, familySafe: true, lengthPref: malformed!);

        Assert.True(result.Ok, result.Error);
        Assert.NotNull(room.CurrentRound);
    }

    [Fact]
    public async Task StartRound_repeated_calls_never_repeat_a_template_until_the_eligible_pool_is_exhausted()
    {
        // story-selection/03, AC-02: repeated StartRound on ONE room, filtered to
        // the small "quick" length class, must serve every eligible template
        // exactly once before any of them repeats.
        var (hub, registry, _, _, _) = BuildHub("conn-host");
        var room = registry.CreateRoom("conn-host", "Mossy", "teal");
        Assert.True(room.TryAddPlayer("Maple", "gold", "conn-joiner"));

        var quickIds = Catalog.Entries
            .Where(e => e.BlankCount <= LengthContentSelector.QuickMaxBlanks)
            .Select(e => e.Id)
            .ToHashSet();
        Assert.True(quickIds.Count > 1); // sanity: need more than one to prove no-repeat

        var servedInFirstPass = new HashSet<string>();
        for (var i = 0; i < quickIds.Count; i++)
        {
            var result = await hub.StartRound(room.Code, familySafe: true, lengthPref: "quick");

            Assert.True(result.Ok, result.Error);
            var served = room.CurrentRound!.TemplateId;
            Assert.Contains(served, quickIds);
            Assert.True(servedInFirstPass.Add(served), $"template '{served}' repeated before the eligible pool ran dry");
        }

        // Every eligible template was served exactly once across the full pass.
        Assert.Equal(quickIds, servedInFirstPass);
    }

    [Fact]
    public async Task StartRound_recycles_after_the_eligible_pool_is_exhausted_without_error()
    {
        // story-selection/03, AC-03: once the eligible pool has been fully
        // played, further rounds must keep succeeding (recycle) rather than
        // erroring or getting stuck.
        var (hub, registry, _, _, _) = BuildHub("conn-host");
        var room = registry.CreateRoom("conn-host", "Mossy", "teal");
        Assert.True(room.TryAddPlayer("Maple", "gold", "conn-joiner"));

        var quickIds = Catalog.Entries
            .Where(e => e.BlankCount <= LengthContentSelector.QuickMaxBlanks)
            .Select(e => e.Id)
            .ToHashSet();

        // Run well past one full pass (exhaust, then recycle several times).
        for (var i = 0; i < quickIds.Count * 3; i++)
        {
            var result = await hub.StartRound(room.Code, familySafe: true, lengthPref: "quick");

            Assert.True(result.Ok, result.Error);
            Assert.Contains(room.CurrentRound!.TemplateId, quickIds);
        }

        // The room's played history never grows past the number of DISTINCT
        // eligible templates (MarkTemplatePlayed dedupes).
        Assert.True(room.PlayedTemplateIds.Count <= quickIds.Count);
    }

    [Fact]
    public async Task StartRound_records_the_served_template_in_the_rooms_played_history()
    {
        var (hub, registry, _, _, _) = BuildHub("conn-host");
        var room = registry.CreateRoom("conn-host", "Mossy", "teal");
        Assert.True(room.TryAddPlayer("Maple", "gold", "conn-joiner"));

        Assert.Empty(room.PlayedTemplateIds);

        var result = await hub.StartRound(room.Code, familySafe: true, lengthPref: "quick");

        Assert.True(result.Ok, result.Error);
        Assert.Contains(room.CurrentRound!.TemplateId, room.PlayedTemplateIds);
    }

    [Fact]
    public async Task StartRound_records_exactly_one_anonymous_serve_event_with_the_specified_fields()
    {
        // AC-01: a GROUP round start writes ONE serve event carrying template id,
        // UTC timestamp, mode, length class, player count, family-safe flag, and the
        // room's OPAQUE instance id - and nothing else.
        var fake = new FakeTelemetrySink();
        var (hub, registry, _, _, _) = BuildHub("conn-host", fake);
        var room = registry.CreateRoom("conn-host", "Mossy", "teal");
        Assert.True(room.TryAddPlayer("Maple", "gold", "conn-joiner"));

        var before = DateTimeOffset.UtcNow;
        var result = await hub.StartRound(room.Code, familySafe: true, lengthPref: "quick");
        var after = DateTimeOffset.UtcNow;

        Assert.True(result.Ok, result.Error);

        var evt = Assert.Single(fake.Events);
        // The serve event's template is the one the round actually served.
        Assert.Equal(room.CurrentRound!.TemplateId, evt.TemplateId);
        Assert.Equal("classic-blind", evt.Mode);
        // "quick" pref served a quick template, so the derived length class is quick.
        Assert.Equal("quick", evt.LengthClass);
        Assert.Equal(2, evt.PlayerCount);
        Assert.True(evt.FamilySafe);
        Assert.InRange(evt.TimestampUtc, before, after);

        // AC-01/AC-04: the instance id is an OPAQUE GUID, NOT the join code.
        Assert.Equal(room.InstanceId, evt.InstanceId);
        Assert.True(Guid.TryParse(evt.InstanceId, out _), "InstanceId should be a GUID");
        Assert.NotEqual(room.Code, evt.InstanceId);
    }

    [Fact]
    public async Task StartRound_derives_full_length_class_for_a_full_story()
    {
        // AC-01: the length class is DERIVED from the chosen template's blank count
        // (story-01's threshold), not from the request - a "full" pick logs "full".
        var fake = new FakeTelemetrySink();
        var (hub, registry, _, _, _) = BuildHub("conn-host", fake);
        var room = registry.CreateRoom("conn-host", "Mossy", "teal");
        Assert.True(room.TryAddPlayer("Maple", "gold", "conn-joiner"));

        var result = await hub.StartRound(room.Code, familySafe: true, lengthPref: "full");

        Assert.True(result.Ok, result.Error);
        var evt = Assert.Single(fake.Events);
        Assert.Equal("full", evt.LengthClass);
        // The served template really is a full (> QuickMaxBlanks) catalog entry.
        var served = Catalog.Entries.Single(e => e.Id == evt.TemplateId);
        Assert.True(served.BlankCount > LengthContentSelector.QuickMaxBlanks);
    }

    [Fact]
    public async Task StartRound_still_starts_when_the_serve_log_sink_throws()
    {
        // AC-03: a down / throwing sink must NEVER fault the round. The round still
        // starts (Ok=true) and the round state is set - the broadcast on the path
        // before the fire-and-forget epilogue therefore ran too.
        var (hub, registry, clients, _, _) = BuildHub("conn-host", new ThrowingTelemetrySink());
        var room = registry.CreateRoom("conn-host", "Mossy", "teal");
        Assert.True(room.TryAddPlayer("Maple", "gold", "conn-joiner"));

        var result = await hub.StartRound(room.Code, familySafe: true, lengthPref: "any");

        Assert.True(result.Ok, result.Error);
        Assert.NotNull(room.CurrentRound);
        // A broadcast/deal demonstrably happened (the hub reached its sends before
        // the epilogue) - the recorder captured a send from this StartRound.
        Assert.NotNull(clients.LastMethod);
    }

    [Fact]
    public void ServeEvent_carries_no_PII_fields()
    {
        // AC-04: a shape assertion - the serve event has ONLY anonymous fields and
        // NOTHING that could carry a person (no nickname, code, connectionId, IP,
        // or hub player-session id). If someone adds such a field, this fails.
        var propertyNames = typeof(ServeEvent)
            .GetProperties()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        string[] forbidden =
        [
            "Nickname", "Name", "DisplayName", "Code", "JoinCode", "RoomCode",
            "ConnectionId", "Connection", "Ip", "IpAddress", "PlayerSessionId",
            "SessionId", "UserId", "Email",
        ];
        foreach (var banned in forbidden)
        {
            Assert.DoesNotContain(banned, propertyNames);
        }

        // And it carries exactly the seven anonymous fields the story specifies.
        Assert.Equal(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "TemplateId", "TimestampUtc", "Mode", "LengthClass",
                "PlayerCount", "FamilySafe", "InstanceId",
            },
            propertyNames);
    }

    // --- group-play/05: host-chosen mode selection ---

    [Fact]
    public async Task StartRound_broadcasts_the_hosts_chosen_mode_for_real()
    {
        // AC-02: the mode the host sends is the mode the round runs in and is
        // broadcast on RoundStartedDto.Mode - no longer pinned to "classic-blind".
        var (hub, registry, clients, _, _) = BuildHub("conn-host");
        var room = registry.CreateRoom("conn-host", "Mossy", "teal");
        Assert.True(room.TryAddPlayer("Maple", "gold", "conn-joiner"));

        var result = await hub.StartRound(room.Code, familySafe: true, lengthPref: "any", mode: "progressive-reveal");

        Assert.True(result.Ok, result.Error);
        Assert.Equal("progressive-reveal", room.CurrentRound!.Mode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task StartRound_defaults_a_null_or_empty_mode_to_classic_blind(string? mode)
    {
        // AC-02 (defensive): a null/empty mode (a legacy 3-arg caller or a malformed
        // client) falls back to Classic Blind - the pre-05 behavior - never fails.
        var (hub, registry, _, _, _) = BuildHub("conn-host");
        var room = registry.CreateRoom("conn-host", "Mossy", "teal");
        Assert.True(room.TryAddPlayer("Maple", "gold", "conn-joiner"));

        var result = await hub.StartRound(room.Code, familySafe: true, lengthPref: "any", mode: mode);

        Assert.True(result.Ok, result.Error);
        Assert.Equal("classic-blind", room.CurrentRound!.Mode);
    }

    [Fact]
    public async Task StartRound_word_bank_mode_only_ever_picks_a_word_bank_template()
    {
        // AC-06: Word Bank draws ONLY templates that carry a curated word bank, so it
        // can never land on a bank-less tale and render an empty tap list. Repeated
        // many times so a single lucky draw cannot mask a broken per-mode filter.
        var (hub, registry, _, _, _) = BuildHub("conn-host");
        var room = registry.CreateRoom("conn-host", "Mossy", "teal");
        Assert.True(room.TryAddPlayer("Maple", "gold", "conn-joiner"));

        var bankIds = Catalog.Entries.Where(e => e.HasWordBank).Select(e => e.Id).ToHashSet();
        Assert.True(bankIds.Count > 0); // sanity: the seed catalog has word-bank tales

        for (var i = 0; i < 25; i++)
        {
            var result = await hub.StartRound(room.Code, familySafe: true, lengthPref: "any", mode: "word-bank");

            Assert.True(result.Ok, result.Error);
            Assert.Equal("word-bank", room.CurrentRound!.Mode);
            Assert.Contains(room.CurrentRound!.TemplateId, bankIds);
        }
    }

    [Theory]
    [InlineData("progressive-story")]
    [InlineData("not-a-real-mode")]
    public async Task StartRound_rejects_an_unoffered_or_unknown_mode_without_starting(string mode)
    {
        // AC-05: Progressive Story is deferred (not offered for group), and any unknown
        // mode is rejected - server-authoritative, so a crafted client cannot start one.
        var (hub, registry, _, _, _) = BuildHub("conn-host");
        var room = registry.CreateRoom("conn-host", "Mossy", "teal");
        Assert.True(room.TryAddPlayer("Maple", "gold", "conn-joiner"));

        var result = await hub.StartRound(room.Code, familySafe: true, lengthPref: "any", mode: mode);

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
        Assert.Null(room.CurrentRound); // no round began
    }

    [Fact]
    public async Task StartRound_word_bank_favorite_that_lacks_a_bank_is_rejected()
    {
        // AC-06: an explicit favorite must still be eligible for the chosen mode. A
        // bank-less tale picked under Word Bank is rejected rather than started into
        // an empty tap list (the server is authoritative even for the favorite seam).
        var (hub, registry, _, _, _) = BuildHub("conn-host");
        var room = registry.CreateRoom("conn-host", "Mossy", "teal");
        Assert.True(room.TryAddPlayer("Maple", "gold", "conn-joiner"));

        var bankless = Catalog.Entries.First(e => !e.HasWordBank).Id;

        var result = await hub.StartRound(
            room.Code, familySafe: true, lengthPref: "any", mode: "word-bank", templateId: bankless);

        Assert.False(result.Ok);
        Assert.Null(room.CurrentRound);
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
