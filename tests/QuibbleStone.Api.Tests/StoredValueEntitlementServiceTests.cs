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
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Accounts;
using QuibbleStone.Api.Entitlements;

namespace QuibbleStone.Api.Tests;

public class StoredValueEntitlementServiceTests
{
    private const string Purchaser = "buyer@example.com";

    private static StoredValueEntitlementService NewService(
        IAccountStore accounts,
        IEntitlementGrantStore grants)
        => new(new DefaultUnlockedEntitlementService(), accounts, grants);

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
        // identity string, so the session-creation read resolves no account and applies it.
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
