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

using System.Linq;
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
        ConnectionEntitlementStore Store,
        FamilyDeviceLinkService DeviceLinks,
        InMemoryFamilyLinkCodeStore LinkCodes,
        InMemoryFamilyDeviceTokenStore DeviceTokens,
        InMemoryAccountStore Accounts)
        BuildHub(string connectionId)
    {
        var registry = new RoomRegistry();
        var entitlements = new CountingEntitlementService();
        // The SAME resolver instance is injected into the hub AND returned to the
        // test, so a token the test mints here resolves back inside OnConnectedAsync.
        var credentials = new PurchaserCredentialService(new EphemeralDataProtectionProvider());
        var store = new ConnectionEntitlementStore();
        // accounts-identity/09: the SAME link-code + device-token stores (and account
        // store) are constructed as locals and returned, so a test can seed an account
        // and mint/redeem a device token against the EXACT instances the hub resolves
        // against at OnConnectedAsync, rather than a disconnected second set.
        var linkCodes = new InMemoryFamilyLinkCodeStore();
        var deviceTokens = new InMemoryFamilyDeviceTokenStore();
        var deviceLinks = new FamilyDeviceLinkService(linkCodes, deviceTokens);
        var accounts = new InMemoryAccountStore();
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
            deviceLinks,
            accounts,
            NullLogger<GameHub>.Instance);

        hub.Groups = new NoOpGroups();
        // accounts-identity/09's StartRound tests below need Clients.Group(...) to
        // resolve to something - a no-op proxy is enough (these tests assert on
        // Room/result state, never on the broadcast payload).
        hub.Clients = new NoOpClients();
        hub.Context = new FakeHubCallerContext(connectionId);

        return (hub, registry, entitlements, credentials, store, deviceLinks, linkCodes, deviceTokens, accounts);
    }

    [Fact]
    public async Task CreateRoom_captures_unlocked_ai_entitlement_on_the_room()
    {
        var (hub, registry, entitlements, _, _, _, _, _, _) = BuildHub("conn-host");

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
        var (hub, _, entitlements, _, _, _, _, _, _) = BuildHub("conn-host");

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
        var (hub, _, entitlements, credentials, store, _, _, _, _) = BuildHub("conn-host");
        var token = credentials.Protect("buyer@example.com");
        hub.Context = new FakeHubCallerContext("conn-host", token);

        await hub.OnConnectedAsync();

        Assert.Equal(1, entitlements.EvaluateCalls);
        Assert.Equal("buyer@example.com", entitlements.LastPurchaserIdentity);

        // The resolved CAPABILITY set is stored (never the identity) for CreateRoom.
        var stored = store.TryGet("conn-host");
        Assert.NotNull(stored);
        Assert.True(stored!.Value.Capabilities.IsUnlocked(EntitlementCatalog.LibraryFull));
        // accounts-identity/09 AC-07a: a signed-in purchaser is adult-by-construction
        // (only an adult completes a magic-link sign-in, ADR 0002 Decision A) - the
        // slot story 06 reserved is now REALLY populated, and a purchaser resolves
        // AdultUnlocked = TRUE (superseding story 06's "always false" placeholder).
        Assert.True(stored.Value.AdultUnlocked);
    }

    [Fact]
    public async Task CreateRoom_uses_the_connection_resolved_capabilities_without_re_evaluating()
    {
        // AC-03: a purchaser whose OnConnectedAsync resolved an extra grant sees that
        // pre-resolved capability set on Room.Entitlements after CreateRoom, and
        // CreateRoom itself makes NO EvaluateForSession call (the counting fake proves
        // the total stays at the one connect-time evaluation).
        var (hub, registry, entitlements, credentials, _, _, _, _, _) = BuildHub("conn-host");
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
        var (hub, registry, entitlements, _, store, _, _, _, _) = BuildHub("conn-host");

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

    // AC-06: a malformed / tampered / expired credential resolves to a null identity
    // (never throws) - a stale token can never break a family's play. Covers BOTH a
    // token that fails inside the crypto (valid base64url, wrong bytes) AND a
    // genuinely non-base64url token (spaces / '!'), which the base64url decode ahead
    // of the crypto rejects with a FormatException - the OnConnectedAsync guard must
    // swallow that too, or the connection would abort.
    //
    // accounts-identity/09 UPDATE: a token that fails as a purchaser credential is now
    // ALSO tried as a family-device token (the resolver's step 2) before falling back
    // to "store nothing" (step 3) - none of these strings parse as either shape, so
    // OnConnectedAsync makes NO EvaluateForSession call at connect time at all (it
    // never had a real identity to evaluate); CreateRoom is what falls back to the
    // default-unlocked baseline, exactly as the no-token case does (AC-05).
    [Theory]
    [InlineData("not-a-real-token")]
    [InlineData("bad token !!")]
    [InlineData("%%%not base64url%%%")]
    public async Task OnConnectedAsync_with_bad_credential_falls_back_to_baseline_without_throwing(string badToken)
    {
        var (hub, registry, entitlements, _, store, _, _, _, _) = BuildHub("conn-host");
        hub.Context = new FakeHubCallerContext("conn-host", badToken);

        await hub.OnConnectedAsync();

        // Neither a purchaser nor a device token resolved - nothing is stored, and no
        // evaluation has happened yet (AC-06 / the "neither" branch, AC-05 semantics).
        Assert.Equal(0, entitlements.EvaluateCalls);
        Assert.Null(store.TryGet("conn-host"));

        // CreateRoom then falls back to the default-unlocked baseline, exactly as the
        // anonymous (no-token) case does.
        var result = await hub.CreateRoom("Mossy", "teal");
        Assert.True(result.Ok);
        Assert.Equal(1, entitlements.EvaluateCalls);
        Assert.Null(entitlements.LastPurchaserIdentity);
        var room = registry.TryGet(result.Room!.Code)!;
        Assert.True(room.Entitlements!.IsUnlocked(EntitlementCatalog.AiOnDemand));
        Assert.False(room.Entitlements!.IsUnlocked(EntitlementCatalog.LibraryFull));
    }

    [Fact]
    public async Task OnDisconnectedAsync_clears_the_connection_entry()
    {
        // AC-03: the per-connection entry is removed when the physical connection ends.
        var (hub, _, _, credentials, store, _, _, _, _) = BuildHub("conn-host");
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

    // --- accounts-identity/09 (family device link, #229) ----------------------

    [Fact]
    public async Task OnConnectedAsync_with_family_device_token_resolves_family_entitlements_independent_of_adult_flag()
    {
        // AC-03: a connection carrying a valid family-device token resolves the
        // family's entitlements on CreateRoom - the SAME way accounts-identity/06
        // resolves a purchaser credential - and the token/identity is discarded at
        // that boundary (no new Room/Player field). This holds whether or not the
        // device's SEPARATE adult-unlock signal (AC-07) is set: entitlement resolution
        // is orthogonal to content safety.
        var (hub, registry, entitlements, _, _, deviceLinks, _, deviceTokens, accounts) = BuildHub("conn-host");

        // Case 1: a freshly redeemed device (IsAdultConfirmedDevice = false, AC-02's
        // safe default) still carries the family's full paid capabilities.
        var accountA = await accounts.CreateOrGetAsync("family-a@example.com");
        var (codeA, _) = deviceLinks.MintLinkCode(accountA.Id);
        var redeemA = await deviceLinks.RedeemAsync(codeA);
        Assert.True(redeemA.Success);

        hub.Context = new FakeHubCallerContext("conn-host", redeemA.RawToken);
        await hub.OnConnectedAsync();
        var resultA = await hub.CreateRoom("Mossy", "teal");

        Assert.True(resultA.Ok);
        var roomA = registry.TryGet(resultA.Room!.Code)!;
        Assert.True(roomA.Entitlements!.IsUnlocked(EntitlementCatalog.LibraryFull));
        Assert.Equal("family-a@example.com", entitlements.LastPurchaserIdentity);

        // Case 2: an adult-confirmed device resolves the IDENTICAL family entitlement -
        // proving the paid-capability axis never reads the adult-unlock flag at all.
        var accountB = await accounts.CreateOrGetAsync("family-b@example.com");
        var (codeB, _) = deviceLinks.MintLinkCode(accountB.Id);
        var redeemB = await deviceLinks.RedeemAsync(codeB);
        Assert.True(redeemB.Success);
        var rowB = (await deviceTokens.ListByAccountAsync(accountB.Id)).Single();
        Assert.True(await deviceTokens.UpdateAsync(rowB with { IsAdultConfirmedDevice = true }));

        hub.Context = new FakeHubCallerContext("conn-host-b", redeemB.RawToken);
        await hub.OnConnectedAsync();
        var resultB = await hub.CreateRoom("Wren", "gold");

        Assert.True(resultB.Ok);
        var roomB = registry.TryGet(resultB.Room!.Code)!;
        Assert.True(roomB.Entitlements!.IsUnlocked(EntitlementCatalog.LibraryFull));
        Assert.Equal("family-b@example.com", entitlements.LastPurchaserIdentity);
    }

    [Fact]
    public async Task StartRound_with_no_adult_unlock_signal_forces_family_safe_regardless_of_client_value()
    {
        // AC-07 - the load-bearing bypass test: an anonymous CreateRoom captures
        // Room.AdultUnlocked = false, and StartRound - called here as a REAL hub
        // method, exactly as a modified/raw client could call it - still serves
        // family-safe content EVEN THOUGH the client sends familySafe:false. The
        // server, not the client, is the boundary.
        var (hub, registry, _, _, _, _, _, _, _) = BuildHub("conn-host");

        var created = await hub.CreateRoom("Mossy", "teal");
        Assert.True(created.Ok);
        var room = registry.TryGet(created.Room!.Code)!;
        Assert.False(room.AdultUnlocked);
        Assert.True(room.TryAddPlayer("Wren", "gold", "conn-joiner"));

        var result = await hub.StartRound(room.Code, familySafe: false, lengthPref: "any");

        Assert.True(result.Ok, result.Error);
        var templateId = room.CurrentRound!.TemplateId;
        var chosen = new TemplateCatalog().Entries.Single(e => e.Id == templateId);
        Assert.True(chosen.FamilySafe);
    }

    [Fact]
    public async Task CreateRoom_from_a_signed_in_purchaser_captures_AdultUnlocked_true()
    {
        // AC-07b (the positive path): a room created from a signed-in purchaser
        // credential (adult-by-construction, ADR 0002 Decision A) captures
        // AdultUnlocked = true, so StartRound will honor the client's OWN familySafe
        // value for this room. Asserting the actual teen-plus DRAW would be a flaky
        // loop over random template selection (explicitly avoided per the story's
        // Technical Notes) - Room.AdultUnlocked is the authoritative signal
        // StartRound's effective-familySafe formula reads, and that is what this
        // test proves is captured correctly.
        var (hub, registry, _, credentials, _, _, _, _, _) = BuildHub("conn-host");
        var token = credentials.Protect("buyer@example.com");
        hub.Context = new FakeHubCallerContext("conn-host", token);

        await hub.OnConnectedAsync();
        var result = await hub.CreateRoom("Mossy", "teal");

        Assert.True(result.Ok);
        var room = registry.TryGet(result.Room!.Code)!;
        Assert.True(room.AdultUnlocked);
    }

    [Fact]
    public void Room_CaptureAdultUnlocked_records_the_value_and_is_idempotent_for_a_repeat_same_value()
    {
        var room = Room.CreateHosted("ABCD", "conn-host", "Mossy", "teal");
        Assert.False(room.AdultUnlocked);

        room.CaptureAdultUnlocked(false);
        Assert.False(room.AdultUnlocked);
        // Idempotent: a repeat capture of the SAME value is a harmless no-op, not a throw.
        room.CaptureAdultUnlocked(false);
        Assert.False(room.AdultUnlocked);
    }

    [Fact]
    public void Room_CaptureAdultUnlocked_true_then_true_again_does_not_throw()
    {
        var room = Room.CreateHosted("ABCD", "conn-host", "Mossy", "teal");

        room.CaptureAdultUnlocked(true);
        Assert.True(room.AdultUnlocked);
        room.CaptureAdultUnlocked(true);
        Assert.True(room.AdultUnlocked);
    }

    [Fact]
    public void Room_CaptureAdultUnlocked_rejects_a_changed_value_after_capture()
    {
        var room = Room.CreateHosted("ABCD", "conn-host", "Mossy", "teal");
        room.CaptureAdultUnlocked(false);

        Assert.Throws<InvalidOperationException>(() => room.CaptureAdultUnlocked(true));
    }

    [Fact]
    public async Task Host_migration_never_flips_AdultUnlocked_and_StartRound_still_forces_family_safe_after()
    {
        // AC-08: host migration (PassHost / RemovePlayer's EnsureHostLocked promoting
        // a different, possibly kid, player) never reads or writes Room.AdultUnlocked -
        // it is a property of the room's ORIGINAL captured session, not of whoever
        // currently holds the host role. Structurally, no host-migration code path
        // calls CaptureAdultUnlocked or touches the backing field - this test confirms
        // the OBSERVABLE guarantee that follows: the value survives migration unchanged
        // and the promoted host still cannot reach teen-plus content. Uses PassHost
        // (rather than RemovePlayer) so the room keeps its required 2 players for
        // StartRound - RemovePlayer's own EnsureHostLocked promotion path is the SAME
        // "never touches AdultUnlocked" guarantee, just via a different call site.
        var (hub, registry, _, _, _, _, _, _, _) = BuildHub("conn-host");

        var created = await hub.CreateRoom("Mossy", "teal");
        Assert.True(created.Ok);
        var room = registry.TryGet(created.Room!.Code)!;
        Assert.False(room.AdultUnlocked);
        Assert.True(room.TryAddPlayer("Wren", "gold", "conn-joiner"));

        // Host migration: "Pass the chisel" hands the host flag to the (possibly kid)
        // joiner, WITHOUT the room ever losing its second player.
        var passed = room.PassHost("conn-host", "Wren");
        Assert.True(passed);
        Assert.True(room.IsHost("conn-joiner"));
        Assert.False(room.IsHost("conn-host"));

        Assert.False(room.AdultUnlocked);

        // The promoted (possibly kid) host cannot bypass the gate via a raw StartRound
        // call either: familySafe:false still serves family-safe content.
        hub.Context = new FakeHubCallerContext("conn-joiner");
        var result = await hub.StartRound(room.Code, familySafe: false, lengthPref: "any");

        Assert.True(result.Ok, result.Error);
        var templateId = room.CurrentRound!.TemplateId;
        var chosen = new TemplateCatalog().Entries.Single(e => e.Id == templateId);
        Assert.True(chosen.FamilySafe);
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

    // StartRound broadcasts via Clients.Group(...).SendAsync - a no-op proxy is enough
    // for the accounts-identity/09 tests above, which assert on Room/result state, not
    // on the broadcast payload.
    private sealed class NoOpClients : IHubCallerClients
    {
        private static readonly IClientProxy Proxy = new NoOpProxy();

        public IClientProxy Group(string groupName) => Proxy;
        public IClientProxy All => Proxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Caller => Proxy;
        public IClientProxy Client(string connectionId) => Proxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => Proxy;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => Proxy;
        public IClientProxy OthersInGroup(string groupName) => Proxy;
        public IClientProxy Others => Proxy;
        public IClientProxy User(string userId) => Proxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => Proxy;

        private sealed class NoOpProxy : IClientProxy
        {
            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
                => Task.CompletedTask;
        }
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
