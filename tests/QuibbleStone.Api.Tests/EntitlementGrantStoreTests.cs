// ----------------------------------------------------------------------------
//  EntitlementGrantStoreTests - the account grant store (billing-entitlements/01,
//  #70, AC-05; re-keyed onto the stable AccountId by accounts-identity/05, #195).
//  Exercises the WORKING in-memory store (the CI-friendly half of the config-presence
//  split); the Table store shares the same semantics + key scheme.
//
//  Pins: a grant round-trips with capability key + validThrough + source intact via
//  a single per-account read; a re-grant of the same capability UPSERTS (extends the
//  lease, one row); the partition key is the stable AccountId (accounts-identity/05,
//  AC-03) so two accounts NEVER collide - even if a future email were reused after a
//  hypothetical change; an unknown account reads empty, never null, never created.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Billing;
using QuibbleStone.Api.Entitlements;

namespace QuibbleStone.Api.Tests;

public class EntitlementGrantStoreTests
{
    // The store keys off the stable AccountId directly (a GUID, unguessable already -
    // accounts-identity/05), NOT a hash of the email. A fixed id stands in for one
    // account's stable id across a test.
    private static readonly Guid AccountId = Guid.NewGuid();

    [Fact]
    public async Task Grant_round_trips_with_all_fields_intact()
    {
        var store = new InMemoryEntitlementGrantStore();
        var validThrough = DateTimeOffset.UtcNow.AddDays(30);
        await store.PutGrantAsync(AccountId, new EntitlementGrant(EntitlementCatalog.PlayRemote, validThrough, GrantSource.Subscription));

        var grants = await store.GetGrantsAsync(AccountId);

        var grant = Assert.Single(grants);
        Assert.Equal(EntitlementCatalog.PlayRemote, grant.CapabilityKey);
        Assert.Equal(validThrough, grant.ValidThrough);
        Assert.Equal(GrantSource.Subscription, grant.Source);
    }

    // billing-entitlements/08 AC-01: the recovery / support metadata (GrantId, PlanId,
    // StripeSubscriptionId, Mode) round-trips through the store unchanged.
    [Fact]
    public async Task Grant_round_trips_with_the_recovery_metadata_intact()
    {
        var store = new InMemoryEntitlementGrantStore();
        var validThrough = DateTimeOffset.UtcNow.AddDays(30);
        var written = new EntitlementGrant(
            EntitlementCatalog.PlayRemote, validThrough, GrantSource.Subscription,
            PlanId: "family-plan", StripeSubscriptionId: "sub_123", Mode: StripeMode.Live);

        await store.PutGrantAsync(AccountId, written);

        var grant = Assert.Single(await store.GetGrantsAsync(AccountId));
        Assert.Equal(written.GrantId, grant.GrantId);
        Assert.Equal("family-plan", grant.PlanId);
        Assert.Equal("sub_123", grant.StripeSubscriptionId);
        Assert.Equal(StripeMode.Live, grant.Mode);
    }

    // AC-01: a fresh GrantId is minted per construction (identifies THIS write) - two
    // grants built the same way still get distinct, non-empty ids.
    [Fact]
    public void GrantId_is_freshly_minted_per_construction()
    {
        var a = new EntitlementGrant(EntitlementCatalog.PlayRemote, null, GrantSource.OneTime);
        var b = new EntitlementGrant(EntitlementCatalog.PlayRemote, null, GrantSource.OneTime);

        Assert.NotEqual(Guid.Empty, a.GrantId);
        Assert.NotEqual(Guid.Empty, b.GrantId);
        Assert.NotEqual(a.GrantId, b.GrantId);
    }

    // AC-01: a defaulted grant (the shape the operator grant/revoke + tip paths build)
    // carries null plan / subscription / mode - metadata is opt-in, not required.
    [Fact]
    public void Metadata_defaults_are_null_for_a_bare_grant()
    {
        var grant = new EntitlementGrant(EntitlementCatalog.LibraryFull, null, GrantSource.Operator);

        Assert.Null(grant.PlanId);
        Assert.Null(grant.StripeSubscriptionId);
        Assert.Null(grant.Mode);
    }

    [Fact]
    public async Task Re_granting_the_same_capability_upserts_one_row()
    {
        var store = new InMemoryEntitlementGrantStore();
        var first = DateTimeOffset.UtcNow.AddDays(30);
        var renewed = DateTimeOffset.UtcNow.AddDays(60);
        await store.PutGrantAsync(AccountId, new EntitlementGrant(EntitlementCatalog.PlayRemote, first, GrantSource.Subscription));
        await store.PutGrantAsync(AccountId, new EntitlementGrant(EntitlementCatalog.PlayRemote, renewed, GrantSource.Subscription));

        var grants = await store.GetGrantsAsync(AccountId);

        // One row for the capability, lease extended to the renewed value.
        var grant = Assert.Single(grants);
        Assert.Equal(renewed, grant.ValidThrough);
    }

    [Fact]
    public async Task Distinct_capabilities_are_separate_rows_in_one_partition()
    {
        var store = new InMemoryEntitlementGrantStore();
        await store.PutGrantAsync(AccountId, new EntitlementGrant(EntitlementCatalog.PlayRemote, null, GrantSource.OneTime));
        await store.PutGrantAsync(AccountId, new EntitlementGrant(EntitlementCatalog.LibraryFull, null, GrantSource.OneTime));

        var grants = await store.GetGrantsAsync(AccountId);

        Assert.Equal(2, grants.Count);
    }

    // AC-03 (accounts-identity/05): two accounts NEVER collide - the partition key is
    // each account's stable id, so one account's grants never leak into another's, even
    // if their emails were ever the same string (an email change / reuse cannot alias
    // the durable keys the way the old email-hash scheme could).
    [Fact]
    public async Task Two_accounts_never_collide()
    {
        var store = new InMemoryEntitlementGrantStore();
        var accountA = Guid.NewGuid();
        var accountB = Guid.NewGuid();
        await store.PutGrantAsync(accountA, new EntitlementGrant(EntitlementCatalog.LibraryFull, null, GrantSource.OneTime));

        // B holds nothing of A's, and granting to B never touches A.
        Assert.Empty(await store.GetGrantsAsync(accountB));
        await store.PutGrantAsync(accountB, new EntitlementGrant(EntitlementCatalog.PlayRemote, null, GrantSource.OneTime));

        var aGrants = await store.GetGrantsAsync(accountA);
        var bGrants = await store.GetGrantsAsync(accountB);
        Assert.Equal(EntitlementCatalog.LibraryFull, Assert.Single(aGrants).CapabilityKey);
        Assert.Equal(EntitlementCatalog.PlayRemote, Assert.Single(bGrants).CapabilityKey);
    }

    [Fact]
    public async Task Unknown_account_reads_empty_never_null()
    {
        var store = new InMemoryEntitlementGrantStore();

        var grants = await store.GetGrantsAsync(Guid.NewGuid());

        Assert.NotNull(grants);
        Assert.Empty(grants);
    }
}
