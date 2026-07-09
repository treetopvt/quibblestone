// ----------------------------------------------------------------------------
//  GameHubEntitlementTests - the entitlement is resolved ONCE per connection and
//  captured on the Room, now from a REAL (possibly purchaser) identity
//  (ai-cost-gate/02, #121, AC-01; accounts-identity/06, ADR 0002 Decision F, #210).
//
//  These exercise the REAL GameHub against the REAL RoomRegistry +
//  ContentSafetyFilter + PurchaserCredentialService (over an ephemeral Data
//  Protection key ring) + ConnectionEntitlementStore (no mocking framework is in
//  the harness), proving:
//    - AC-01 (ai-cost-gate/02): creating a room captures a SessionEntitlements on
//      that room (the capability set is resolved at session-creation, not later).
//    - AC-03 (ai-cost-gate/02): the captured set has the reserved ai.* capability
//      UNLOCKED (alpha default-unlocked; the jumble is reachable by the session).
//    - AC-01 (once-only): a room's entitlements are captured exactly once.
//    - accounts-identity/06 AC-02: a connection carrying a VALID purchaser
//      credential resolves the identity in OnConnectedAsync and calls
//      EvaluateForSession EXACTLY ONCE with that identity.
//    - accounts-identity/06 AC-03: CreateRoom then reads the ALREADY-RESOLVED
//      capability set (a purchaser-only unlock included) and makes NO
//      EvaluateForSession call of its own (verified via the counting fake); the
//      null-identity path still falls back to the default-unlocked baseline.
//    - accounts-identity/06 AC-04 / AC-08: the per-connection value type
//      (ResolvedConnectionIdentity) carries ONLY a SessionEntitlements + a bool -
//      no identity-shaped field can ever be keyed by a ConnectionId (structural).
//    - accounts-identity/06 AC-05: an anonymous connection (no access token)
//      stores NOTHING at connect time and CreateRoom falls back to the baseline,
//      byte-for-byte today's free play.
//    - accounts-identity/06 AC-06: a malformed / tampered credential resolves to
//      null (falls back to the baseline, stored as such) rather than throwing.
//
//  A spy entitlement service counts EvaluateForSession calls (and grants a
//  purchaser-only capability when an identity is present) so both the
//  "exactly once" and "the purchaser's grant actually unlocked something"
//  guarantees are asserted directly.
// ----------------------------------------------------------------------------

using System.Reflection;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections.Features;
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

public class GameHubEntitlementTests
{
    private static (
        GameHub Hub,
        RoomRegistry Registry,
        CountingEntitlementService Entitlements,
        PurchaserCredentialService Credentials,
        ConnectionEntitlementStore Store)
        BuildHub(string connectionId)
    {
        var registry = new RoomRegistry();
        var entitlements = new CountingEntitlementService();
        // The SAME resolver instance is injected into the hub AND returned to the
        // test, so a token the test mints here resolves back inside OnConnectedAsync.
        var credentials = new PurchaserCredentialService(new EphemeralDataProtectionProvider());
        var store = new ConnectionEntitlementStore();
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
            TestSeatGrace.NoOp(registry),
            credentials,
            store,
            NullLogger<GameHub>.Instance);

        hub.Groups = new NoOpGroups();
        hub.Context = new FakeHubCallerContext(connectionId);

        return (hub, registry, entitlements, credentials, store);
    }

    [Fact]
    public async Task CreateRoom_captures_unlocked_ai_entitlement_on_the_room()
    {
        var (hub, registry, entitlements, _, _) = BuildHub("conn-host");

        var result = await hub.CreateRoom("Mossy", "teal");

        // ai-cost-gate/02 AC-07: the create still succeeds exactly as before.
        Assert.True(result.Ok);
        Assert.NotNull(result.Room);

        // AC-01: the entitlement was resolved at session-creation and stashed.
        var room = registry.TryGet(result.Room!.Code)!;
        Assert.NotNull(room.Entitlements);

        // AC-03: the reserved ai.* capability is unlocked (default-unlocked alpha).
        Assert.True(room.Entitlements!.IsUnlocked(EntitlementCatalog.AiOnDemand));

        // accounts-identity/06 AC-05: with no OnConnectedAsync resolve (the store is
        // empty), CreateRoom falls back to EvaluateForSession(null) EXACTLY ONCE,
        // anonymously - byte-for-byte the pre-story behavior.
        Assert.Equal(1, entitlements.EvaluateCalls);
        Assert.Null(entitlements.LastPurchaserIdentity);
        // The anonymous fallback grants only the baseline - no purchaser-only unlock.
        Assert.False(room.Entitlements!.IsUnlocked(EntitlementCatalog.LibraryFull));
    }

    [Fact]
    public async Task CreateRoom_evaluates_entitlement_once_per_session_not_per_room()
    {
        var (hub, _, entitlements, _, _) = BuildHub("conn-host");

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

    // --- accounts-identity/06 (ADR 0002 Decision F, #210) ---------------------

    [Fact]
    public async Task OnConnectedAsync_with_valid_credential_resolves_identity_and_evaluates_once()
    {
        // AC-02: a connection carrying a valid purchaser credential resolves the
        // identity in OnConnectedAsync and calls EvaluateForSession exactly once
        // with THAT identity - the same resolver billing-entitlements/05 uses.
        var (hub, _, entitlements, credentials, store) = BuildHub("conn-host");
        var token = credentials.Protect("buyer@example.com");
        hub.Context = new FakeHubCallerContext("conn-host", token);

        await hub.OnConnectedAsync();

        Assert.Equal(1, entitlements.EvaluateCalls);
        Assert.Equal("buyer@example.com", entitlements.LastPurchaserIdentity);

        // The resolved CAPABILITY set is stored (never the identity) for CreateRoom.
        var stored = store.TryGet("conn-host");
        Assert.NotNull(stored);
        Assert.True(stored!.Value.Capabilities.IsUnlocked(EntitlementCatalog.LibraryFull));
        // AC-03: the AdultUnlocked slot is reserved and ALWAYS false in this story.
        Assert.False(stored.Value.AdultUnlocked);
    }

    [Fact]
    public async Task CreateRoom_uses_the_connection_resolved_capabilities_without_re_evaluating()
    {
        // AC-03: a purchaser whose OnConnectedAsync resolved an extra grant sees that
        // pre-resolved capability set on Room.Entitlements after CreateRoom, and
        // CreateRoom itself makes NO EvaluateForSession call (the counting fake proves
        // the total stays at the one connect-time evaluation).
        var (hub, registry, entitlements, credentials, _) = BuildHub("conn-host");
        var token = credentials.Protect("buyer@example.com");
        hub.Context = new FakeHubCallerContext("conn-host", token);

        await hub.OnConnectedAsync();
        Assert.Equal(1, entitlements.EvaluateCalls);

        var result = await hub.CreateRoom("Mossy", "teal");

        Assert.True(result.Ok);
        // AC-03: CreateRoom read the stored set - it did NOT evaluate again.
        Assert.Equal(1, entitlements.EvaluateCalls);

        var room = registry.TryGet(result.Room!.Code)!;
        Assert.NotNull(room.Entitlements);
        // The family-plan grant actually unlocked the session for the first time.
        Assert.True(room.Entitlements!.IsUnlocked(EntitlementCatalog.LibraryFull));
        Assert.True(room.Entitlements!.IsUnlocked(EntitlementCatalog.AiOnDemand));
    }

    [Fact]
    public async Task OnConnectedAsync_with_no_token_stores_nothing_and_CreateRoom_falls_back_to_baseline()
    {
        // AC-05: an anonymous connection supplies no access token - OnConnectedAsync
        // stores NOTHING and evaluates nothing at connect time; CreateRoom then falls
        // back to the default-unlocked baseline, exactly as free play does today.
        var (hub, registry, entitlements, _, store) = BuildHub("conn-host");

        await hub.OnConnectedAsync();
        Assert.Equal(0, entitlements.EvaluateCalls);
        Assert.Null(store.TryGet("conn-host"));

        var result = await hub.CreateRoom("Mossy", "teal");

        Assert.True(result.Ok);
        Assert.Equal(1, entitlements.EvaluateCalls);
        Assert.Null(entitlements.LastPurchaserIdentity);
        var room = registry.TryGet(result.Room!.Code)!;
        Assert.True(room.Entitlements!.IsUnlocked(EntitlementCatalog.AiOnDemand));
        Assert.False(room.Entitlements!.IsUnlocked(EntitlementCatalog.LibraryFull));
    }

    [Fact]
    public async Task OnConnectedAsync_with_bad_credential_falls_back_to_baseline_without_throwing()
    {
        // AC-06: a malformed / tampered / expired credential resolves to a null
        // identity (never throws), so it evaluates the default-unlocked baseline and
        // stores it as such - a stale token can never break a family's play.
        var (hub, _, entitlements, _, store) = BuildHub("conn-host");
        hub.Context = new FakeHubCallerContext("conn-host", "not-a-real-token");

        await hub.OnConnectedAsync();

        Assert.Equal(1, entitlements.EvaluateCalls);
        Assert.Null(entitlements.LastPurchaserIdentity);
        var stored = store.TryGet("conn-host");
        Assert.NotNull(stored);
        Assert.True(stored!.Value.Capabilities.IsUnlocked(EntitlementCatalog.AiOnDemand));
        Assert.False(stored.Value.Capabilities.IsUnlocked(EntitlementCatalog.LibraryFull));
    }

    [Fact]
    public async Task OnDisconnectedAsync_clears_the_connection_entry()
    {
        // AC-03: the per-connection entry is removed when the physical connection ends.
        var (hub, _, _, credentials, store) = BuildHub("conn-host");
        var token = credentials.Protect("buyer@example.com");
        hub.Context = new FakeHubCallerContext("conn-host", token);

        await hub.OnConnectedAsync();
        Assert.NotNull(store.TryGet("conn-host"));

        await hub.OnDisconnectedAsync(null);
        Assert.Null(store.TryGet("conn-host"));
    }

    [Fact]
    public void ResolvedConnectionIdentity_carries_no_identity_shaped_field()
    {
        // AC-04 / AC-08 (structural): the per-connection value type holds ONLY a
        // SessionEntitlements + a bool - it has NO field an email / AccountId /
        // device-token id could ever be stored in, keyed by a ConnectionId.
        var declared = typeof(ResolvedConnectionIdentity)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.DeclaringType == typeof(ResolvedConnectionIdentity))
            .ToArray();

        Assert.Equal(2, declared.Length);
        Assert.Contains(declared, p => p.Name == "Capabilities" && p.PropertyType == typeof(SessionEntitlements));
        Assert.Contains(declared, p => p.Name == "AdultUnlocked" && p.PropertyType == typeof(bool));
        // No string-typed member could hold an identity string.
        Assert.DoesNotContain(declared, p => p.PropertyType == typeof(string));
    }

    // --- Test doubles ---------------------------------------------------------

    // Counts EvaluateForSession calls (and records the purchaser identity) so the
    // "exactly once, anonymous unless a purchaser resolved" guarantees are asserted
    // directly. Grants a purchaser-ONLY capability (LibraryFull) when an identity is
    // present, so a test can prove the resolved grant actually reached the room.
    private sealed class CountingEntitlementService : IEntitlementService
    {
        public int EvaluateCalls { get; private set; }
        public string? LastPurchaserIdentity { get; private set; }

        public ValueTask<SessionEntitlements> EvaluateForSession(
            string? purchaserIdentity = null,
            CancellationToken cancellationToken = default)
        {
            EvaluateCalls += 1;
            LastPurchaserIdentity = purchaserIdentity;
            var keys = purchaserIdentity is null
                ? EntitlementCatalog.AiCapabilities
                : [.. EntitlementCatalog.AiCapabilities, EntitlementCatalog.LibraryFull];
            return new ValueTask<SessionEntitlements>(new SessionEntitlements(keys));
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

    // A hub context that carries the connection id the hub reads and, when an access
    // token is supplied, an IHttpContextFeature whose DefaultHttpContext exposes it on
    // the query string as "access_token" - SignalR's carrier for accessTokenFactory,
    // the ONLY way OnConnectedAsync can read a token in a unit test (the story's
    // Technical Notes call this fixture out explicitly). No token => no
    // IHttpContextFeature, so Context.GetHttpContext() resolves to null (the anonymous
    // connection), exactly as the pre-story fixture behaved.
    private sealed class FakeHubCallerContext : HubCallerContext
    {
        private readonly IFeatureCollection _features;

        public FakeHubCallerContext(string connectionId, string? accessToken = null)
        {
            ConnectionId = connectionId;
            var features = new FeatureCollection();
            if (accessToken is not null)
            {
                var httpContext = new DefaultHttpContext
                {
                    Request = { QueryString = new QueryString("?access_token=" + Uri.EscapeDataString(accessToken)) },
                };
                features.Set<IHttpContextFeature>(new FakeHttpContextFeature(httpContext));
            }

            _features = features;
        }

        public override string ConnectionId { get; }
        public override string? UserIdentifier => null;
        public override System.Security.Claims.ClaimsPrincipal? User => null;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features => _features;
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }

    // The minimal IHttpContextFeature SignalR's Context.GetHttpContext() reads through
    // (the SignalR-connection feature, Microsoft.AspNetCore.Http.Connections.Features).
    private sealed class FakeHttpContextFeature(HttpContext httpContext) : IHttpContextFeature
    {
        public HttpContext? HttpContext { get; set; } = httpContext;
    }
}
