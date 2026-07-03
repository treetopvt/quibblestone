// ----------------------------------------------------------------------------
//  GameHubEntitlementTests - the entitlement is evaluated ONCE at session-creation
//  and captured on the Room (ai-cost-gate/02, #121, AC-01).
//
//  These exercise the REAL GameHub.CreateRoom against the REAL RoomRegistry +
//  ContentSafetyFilter + DefaultUnlockedEntitlementService (no mocking framework
//  is in the harness), proving:
//    - AC-01: creating a room captures a SessionEntitlements on that room (the
//      entitlement is evaluated at session-creation, not later).
//    - AC-03: the captured set has the reserved ai.* capability UNLOCKED (alpha
//      default-unlocked; the jumble is reachable by the session).
//    - AC-01 (once-only): a room's entitlements are captured exactly once - a
//      second capture is rejected, so no code path can re-evaluate per call.
//    - AC-07: CreateRoom still succeeds exactly as before with the check present.
//
//  A spy entitlement service counts EvaluateForSession calls so the "exactly once
//  per CreateRoom" guarantee is asserted directly.
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

public class GameHubEntitlementTests
{
    private static (GameHub Hub, RoomRegistry Registry, CountingEntitlementService Entitlements)
        BuildHub(string connectionId)
    {
        var registry = new RoomRegistry();
        var entitlements = new CountingEntitlementService();
        var hub = new GameHub(
            registry,
            new ContentSafetyFilter(),
            new TemplateCatalog(),
            new FamilySafeContentSelector(),
            new LengthContentSelector(),
            new FreshnessContentSelector(),
            new FakeTelemetrySink(),
            TestTelemetry.NoOp,
            entitlements,
            NullLogger<GameHub>.Instance);

        hub.Groups = new NoOpGroups();
        hub.Context = new FakeHubCallerContext(connectionId);

        return (hub, registry, entitlements);
    }

    [Fact]
    public async Task CreateRoom_captures_unlocked_ai_entitlement_on_the_room()
    {
        var (hub, registry, entitlements) = BuildHub("conn-host");

        var result = await hub.CreateRoom("Mossy", "teal");

        // AC-07: the create still succeeds exactly as before.
        Assert.True(result.Ok);
        Assert.NotNull(result.Room);

        // AC-01: the entitlement was evaluated at session-creation and stashed.
        var room = registry.TryGet(result.Room!.Code)!;
        Assert.NotNull(room.Entitlements);

        // AC-03: the reserved ai.* capability is unlocked (default-unlocked alpha).
        Assert.True(room.Entitlements!.IsUnlocked(EntitlementCatalog.AiOnDemand));

        // AC-01: evaluated EXACTLY ONCE for the session.
        Assert.Equal(1, entitlements.EvaluateCalls);

        // AC-05: evaluated anonymously - no purchaser identity is passed (alpha has
        // no accounts).
        Assert.Null(entitlements.LastPurchaserIdentity);
    }

    [Fact]
    public async Task CreateRoom_evaluates_entitlement_once_per_session_not_per_room()
    {
        var (hub, _, entitlements) = BuildHub("conn-host");

        await hub.CreateRoom("Mossy", "teal");
        // A second, separate session mint evaluates once more (once PER session),
        // never more than once for a given session.
        hub.Context = new FakeHubCallerContext("conn-host-2");
        await hub.CreateRoom("Wren", "gold");

        Assert.Equal(2, entitlements.EvaluateCalls);
    }

    [Fact]
    public void Room_rejects_a_second_entitlement_capture()
    {
        // AC-01 (guard): the entitlement is captured once at creation; a second
        // capture (which would be a re-evaluation) is a programming error.
        var room = Room.CreateHosted("ABCD", "conn-host", "Mossy", "teal");
        room.CaptureEntitlements(new SessionEntitlements(EntitlementCatalog.AiCapabilities));

        Assert.Throws<InvalidOperationException>(() =>
            room.CaptureEntitlements(new SessionEntitlements(EntitlementCatalog.AiCapabilities)));
    }

    // --- Test doubles ---------------------------------------------------------

    // Counts EvaluateForSession calls (and records the purchaser identity) so the
    // "exactly once, anonymous" guarantees are asserted directly. Still returns the
    // real default-unlocked set.
    private sealed class CountingEntitlementService : IEntitlementService
    {
        public int EvaluateCalls { get; private set; }
        public string? LastPurchaserIdentity { get; private set; }

        public SessionEntitlements EvaluateForSession(string? purchaserIdentity = null)
        {
            EvaluateCalls += 1;
            LastPurchaserIdentity = purchaserIdentity;
            return new SessionEntitlements(EntitlementCatalog.AiCapabilities);
        }
    }

    // CreateRoom only subscribes the host to its room group; a no-op is enough.
    private sealed class NoOpGroups : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

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
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }
}
