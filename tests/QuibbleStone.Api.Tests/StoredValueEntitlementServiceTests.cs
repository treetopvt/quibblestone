// ----------------------------------------------------------------------------
//  StoredValueEntitlementServiceTests - the REAL stored-value entitlement seam
//  (billing-entitlements/01, #70). Drives StoredValueEntitlementService against the
//  real DefaultUnlockedEntitlementService baseline + the working in-memory account
//  and grant stores (no mocking framework, matching the harness).
//
//  Pins the load-bearing guarantees:
//    - AC-03: no purchaser (every alpha session) -> exactly the shipped
//      default-unlocked set (ai.* unlocked, paid keys locked). Zero regression.
//    - AC-04: an active grant (null / future validThrough) unlocks its key; an
//      expired grant reads locked; the baseline is untouched either way.
//    - AC-06: a purchaser is resolved via IAccountStore - a grant for an identity
//      with NO account row is NOT applied (the account lookup gates it), and the
//      lookup is actually consulted.
//
//  control-plane/02 (#213) adds the system-scope kill-switch filter that composes
//  AFTER the baseline + grant steps above. Its guarantees are pinned here too:
//    - AC-02: ai.enabled default (true) + AI configured -> ai.onDemand UNCHANGED
//      (still unlocked) - zero regression from the pre-story behavior.
//    - AC-03: an operator override of ai.enabled=false force-removes ai.onDemand for
//      a NEW session even when an active grant carries it - system force-off wins.
//    - AC-04: a SessionEntitlements captured BEFORE the flip is unaffected when read
//      again after (capture-once - only new sessions see the flip).
//    - AC-05: AI unconfigured + ai.enabled=true still evaluates ai.onDemand off -
//      config-presence is the floor, a flag can never enable unconfigured infra.
//  Every fixture now supplies the two new dependencies via a SystemFlagEvaluator
//  (a real RuntimeSettingsService over an in-memory store + a SystemConfigPresence),
//  since the service's constructor gained it.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Accounts;
using QuibbleStone.Api.Entitlements;
using QuibbleStone.Api.Settings;

namespace QuibbleStone.Api.Tests;

public class StoredValueEntitlementServiceTests
{
    private const string Purchaser = "buyer@example.com";

    // The default fixture: AI is configured and every system flag is at its true code
    // default (no override), so the system-scope filter is a NO-OP and the baseline +
    // grant behavior is exactly what billing-entitlements/01 shipped (AC-02). Tests that
    // exercise the kill switch pass an explicit presence / override instead.
    private static StoredValueEntitlementService NewService(
        IAccountStore accounts,
        IEntitlementGrantStore grants)
        => new(new DefaultUnlockedEntitlementService(), accounts, grants, AllConfiguredFlags());

    // A SystemFlagEvaluator over a real RuntimeSettingsService (in-memory store) with every
    // capability's infrastructure configured and no override applied - the shipped default.
    private static SystemFlagEvaluator AllConfiguredFlags()
        => new(
            new RuntimeSettingsService(new InMemoryRuntimeSettingsStore()),
            new SystemConfigPresence(AiConfigured: true, PublishingConfigured: true, EmailConfigured: true));

    // A SystemFlagEvaluator with a chosen AI config-presence and an optional ai.enabled
    // override already written - so a test can drive the configured-and-forced-off (AC-03)
    // and the unconfigured (AC-05) scenarios through the real settings composition.
    private static SystemFlagEvaluator Flags(bool aiConfigured, bool? aiEnabledOverride = null)
    {
        var store = new InMemoryRuntimeSettingsStore();
        if (aiEnabledOverride is { } value)
        {
            store.SetOverrideAsync(SettingsCatalog.AiEnabled, value ? "true" : "false", "op@example.com", DateTimeOffset.UtcNow)
                .GetAwaiter().GetResult();
        }

        return new SystemFlagEvaluator(
            new RuntimeSettingsService(store),
            new SystemConfigPresence(AiConfigured: aiConfigured, PublishingConfigured: true, EmailConfigured: true));
    }

    // AC-03: no purchaser -> the SAME set DefaultUnlockedEntitlementService returns:
    // the ai.* baseline unlocked, and a paid capability (with no grant) locked.
    [Fact]
    public async Task No_purchaser_returns_the_default_unlocked_baseline()
    {
        var service = NewService(new InMemoryAccountStore(), new InMemoryEntitlementGrantStore());

        var entitlements = await service.EvaluateForSession(purchaserIdentity: null);

        Assert.True(entitlements.IsUnlocked(EntitlementCatalog.AiOnDemand));
        Assert.False(entitlements.IsUnlocked(EntitlementCatalog.LibraryFull));
    }

    // AC-06: a purchaser identity string with NO account row unlocks nothing paid,
    // even if a stray grant row exists - the account lookup is the gate.
    [Fact]
    public async Task Purchaser_identity_without_an_account_gets_only_the_baseline()
    {
        var grants = new InMemoryEntitlementGrantStore();
        // A grant exists under SOME account id, but no account was ever created for the
        // identity string. The session-creation read resolves NO account, so it does NOT
        // apply that stray grant (the account lookup is the gate, AC-06).
        await grants.PutGrantAsync(Guid.NewGuid(), new EntitlementGrant(EntitlementCatalog.LibraryFull, null, GrantSource.OneTime));

        var service = NewService(new InMemoryAccountStore(), grants);

        var entitlements = await service.EvaluateForSession(Purchaser);

        Assert.True(entitlements.IsUnlocked(EntitlementCatalog.AiOnDemand)); // baseline intact
        Assert.False(entitlements.IsUnlocked(EntitlementCatalog.LibraryFull)); // no account -> not applied
    }

    // AC-04: an active grant (permanent / null validThrough) for a real purchaser
    // unlocks its capability, on top of the baseline. The grant is keyed off the
    // account's STABLE id (accounts-identity/05), the same value the read resolves.
    [Fact]
    public async Task Active_permanent_grant_unlocks_its_capability()
    {
        var accounts = new InMemoryAccountStore();
        var account = await accounts.CreateOrGetAsync(Purchaser);
        var grants = new InMemoryEntitlementGrantStore();
        await grants.PutGrantAsync(account.Id, new EntitlementGrant(EntitlementCatalog.LibraryFull, null, GrantSource.OneTime));

        var service = NewService(accounts, grants);
        var entitlements = await service.EvaluateForSession(Purchaser);

        Assert.True(entitlements.IsUnlocked(EntitlementCatalog.LibraryFull));
        Assert.True(entitlements.IsUnlocked(EntitlementCatalog.AiOnDemand)); // baseline still there
    }

    // AC-04: a future validThrough is active (unlocked); a past one is expired (locked).
    [Fact]
    public async Task Grant_lease_window_governs_unlock()
    {
        var accounts = new InMemoryAccountStore();
        var account = await accounts.CreateOrGetAsync(Purchaser);
        var grants = new InMemoryEntitlementGrantStore();
        await grants.PutGrantAsync(account.Id, new EntitlementGrant(
            EntitlementCatalog.PlayRemote, DateTimeOffset.UtcNow.AddDays(30), GrantSource.Subscription));
        await grants.PutGrantAsync(account.Id, new EntitlementGrant(
            EntitlementCatalog.PlayLargeGroup, DateTimeOffset.UtcNow.AddDays(-1), GrantSource.Subscription));

        var service = NewService(accounts, grants);
        var entitlements = await service.EvaluateForSession(Purchaser);

        Assert.True(entitlements.IsUnlocked(EntitlementCatalog.PlayRemote)); // future lease -> active
        Assert.False(entitlements.IsUnlocked(EntitlementCatalog.PlayLargeGroup)); // past lease -> expired
        Assert.True(entitlements.IsUnlocked(EntitlementCatalog.AiOnDemand)); // baseline unaffected
    }

    // AC-06: the account lookup is consulted (a counting store proves it is not bypassed).
    [Fact]
    public async Task Purchaser_resolution_consults_the_account_store()
    {
        var accounts = new CountingAccountStore();
        await accounts.CreateOrGetAsync(Purchaser);
        var service = NewService(accounts, new InMemoryEntitlementGrantStore());

        await service.EvaluateForSession(Purchaser);

        Assert.True(accounts.GetByIdentityCalls >= 1);
    }

    // --- control-plane/02 (#213): the system-scope kill-switch filter -----------

    // AC-02: ai.enabled at its true default + AI configured -> ai.onDemand is UNCHANGED
    // (still unlocked). The system filter is a no-op; zero regression from before the story.
    [Fact]
    public async Task Ai_enabled_default_with_ai_configured_leaves_ai_onDemand_unlocked()
    {
        var service = new StoredValueEntitlementService(
            new DefaultUnlockedEntitlementService(),
            new InMemoryAccountStore(),
            new InMemoryEntitlementGrantStore(),
            Flags(aiConfigured: true)); // no override -> code default true

        var entitlements = await service.EvaluateForSession(purchaserIdentity: null);

        Assert.True(entitlements.IsUnlocked(EntitlementCatalog.AiOnDemand));
    }

    // AC-03: an operator forces ai.enabled=false. A NEW session excludes ai.onDemand even
    // when an active purchaser grant carries it - the system force-off PRECEDES (wins over)
    // the account grant, implemented as the post-compose filter (grant evaluation still ran).
    [Fact]
    public async Task Ai_enabled_forced_off_removes_ai_onDemand_despite_an_active_grant()
    {
        var accounts = new InMemoryAccountStore();
        var account = await accounts.CreateOrGetAsync(Purchaser);
        var grants = new InMemoryEntitlementGrantStore();
        // An active grant explicitly carrying ai.onDemand - the kill switch must beat it.
        await grants.PutGrantAsync(account.Id, new EntitlementGrant(EntitlementCatalog.AiOnDemand, null, GrantSource.Operator));
        // A DIFFERENT paid capability proves the filter subtracts ONLY the ai.* keys, not
        // the whole composed set - the baseline+grant composition still ran in full.
        await grants.PutGrantAsync(account.Id, new EntitlementGrant(EntitlementCatalog.LibraryFull, null, GrantSource.OneTime));

        var service = new StoredValueEntitlementService(
            new DefaultUnlockedEntitlementService(),
            accounts,
            grants,
            Flags(aiConfigured: true, aiEnabledOverride: false));

        var entitlements = await service.EvaluateForSession(Purchaser);

        Assert.False(entitlements.IsUnlocked(EntitlementCatalog.AiOnDemand)); // forced off, beats the grant
        Assert.True(entitlements.IsUnlocked(EntitlementCatalog.LibraryFull)); // other grants untouched
    }

    // AC-04: a session captured BEFORE the flip is unaffected when read again after the
    // operator forces ai.enabled=false - only sessions created after the change see it
    // (capture-once, unchanged). SessionEntitlements is an immutable snapshot.
    [Fact]
    public async Task A_session_captured_before_the_flip_is_unaffected_after_it()
    {
        // Flip through the SAME RuntimeSettingsService the evaluator reads, so its
        // write-through cache reset applies (mirrors how the admin PUT flips it live).
        var settings = new RuntimeSettingsService(new InMemoryRuntimeSettingsStore());
        var evaluator = new SystemFlagEvaluator(
            settings,
            new SystemConfigPresence(AiConfigured: true, PublishingConfigured: true, EmailConfigured: true));
        var service = new StoredValueEntitlementService(
            new DefaultUnlockedEntitlementService(),
            new InMemoryAccountStore(),
            new InMemoryEntitlementGrantStore(),
            evaluator);

        // Capture a session while ai.enabled is at its true default.
        var captured = await service.EvaluateForSession(purchaserIdentity: null);
        Assert.True(captured.IsUnlocked(EntitlementCatalog.AiOnDemand));

        // The operator now forces ai.enabled=false (through the service, so the cache resets).
        await settings.SetOverrideAsync(SettingsCatalog.AiEnabled, "false", "op@example.com", DateTimeOffset.UtcNow);

        // The already-captured snapshot is UNAFFECTED (capture-once - it reflects what was
        // captured at its own creation, never re-evaluated).
        Assert.True(captured.IsUnlocked(EntitlementCatalog.AiOnDemand));

        // A session created AFTER the flip DOES see it - the flip only changes new sessions.
        var afterFlip = await service.EvaluateForSession(purchaserIdentity: null);
        Assert.False(afterFlip.IsUnlocked(EntitlementCatalog.AiOnDemand));
    }

    // AC-05: AI is NOT configured at all. Even with ai.enabled left true, ai.onDemand
    // evaluates off - config-presence is the FLOOR, a settings flag can never enable a
    // capability whose infrastructure is not wired up.
    [Fact]
    public async Task Ai_unconfigured_removes_ai_onDemand_even_when_the_flag_is_true()
    {
        var service = new StoredValueEntitlementService(
            new DefaultUnlockedEntitlementService(),
            new InMemoryAccountStore(),
            new InMemoryEntitlementGrantStore(),
            Flags(aiConfigured: false, aiEnabledOverride: true)); // flag true, but infra absent

        var entitlements = await service.EvaluateForSession(purchaserIdentity: null);

        Assert.False(entitlements.IsUnlocked(EntitlementCatalog.AiOnDemand));
    }

    // A minimal IAccountStore that counts read lookups, wrapping the working in-memory
    // store so behavior is real (AC-06: delegates, no duplicate identity logic).
    private sealed class CountingAccountStore : IAccountStore
    {
        private readonly InMemoryAccountStore _inner = new();
        public int GetByIdentityCalls { get; private set; }

        public Task<Account> CreateOrGetAsync(string emailIdentity, CancellationToken ct = default)
            => _inner.CreateOrGetAsync(emailIdentity, ct);

        public Task<Account?> GetByIdentityAsync(string emailIdentity, CancellationToken ct = default)
        {
            GetByIdentityCalls++;
            return _inner.GetByIdentityAsync(emailIdentity, ct);
        }

        public Task<Account?> GetByIdAsync(Guid accountId, CancellationToken ct = default)
            => _inner.GetByIdAsync(accountId, ct);
    }
}
