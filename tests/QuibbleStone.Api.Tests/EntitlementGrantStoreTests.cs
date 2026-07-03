// ----------------------------------------------------------------------------
//  EntitlementGrantStoreTests - the purchaser grant store (billing-entitlements/01,
//  #70, AC-05). Exercises the WORKING in-memory store (the CI-friendly half of the
//  config-presence split); the Table store shares the same semantics + key scheme.
//
//  Pins: a grant round-trips with capability key + validThrough + source intact via
//  a single per-purchaser read; a re-grant of the same capability UPSERTS (extends
//  the lease, one row); the store is case / whitespace insensitive (same hashed key
//  as the account store); an unknown purchaser reads empty, never null, never created.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Entitlements;

namespace QuibbleStone.Api.Tests;

public class EntitlementGrantStoreTests
{
    private const string Purchaser = "buyer@example.com";

    [Fact]
    public async Task Grant_round_trips_with_all_fields_intact()
    {
        var store = new InMemoryEntitlementGrantStore();
        var validThrough = DateTimeOffset.UtcNow.AddDays(30);
        await store.PutGrantAsync(Purchaser, new EntitlementGrant(EntitlementCatalog.PlayRemote, validThrough, GrantSource.Subscription));

        var grants = await store.GetGrantsAsync(Purchaser);

        var grant = Assert.Single(grants);
        Assert.Equal(EntitlementCatalog.PlayRemote, grant.CapabilityKey);
        Assert.Equal(validThrough, grant.ValidThrough);
        Assert.Equal(GrantSource.Subscription, grant.Source);
    }

    [Fact]
    public async Task Re_granting_the_same_capability_upserts_one_row()
    {
        var store = new InMemoryEntitlementGrantStore();
        var first = DateTimeOffset.UtcNow.AddDays(30);
        var renewed = DateTimeOffset.UtcNow.AddDays(60);
        await store.PutGrantAsync(Purchaser, new EntitlementGrant(EntitlementCatalog.PlayRemote, first, GrantSource.Subscription));
        await store.PutGrantAsync(Purchaser, new EntitlementGrant(EntitlementCatalog.PlayRemote, renewed, GrantSource.Subscription));

        var grants = await store.GetGrantsAsync(Purchaser);

        // One row for the capability, lease extended to the renewed value.
        var grant = Assert.Single(grants);
        Assert.Equal(renewed, grant.ValidThrough);
    }

    [Fact]
    public async Task Distinct_capabilities_are_separate_rows_in_one_partition()
    {
        var store = new InMemoryEntitlementGrantStore();
        await store.PutGrantAsync(Purchaser, new EntitlementGrant(EntitlementCatalog.PlayRemote, null, GrantSource.OneTime));
        await store.PutGrantAsync(Purchaser, new EntitlementGrant(EntitlementCatalog.LibraryFull, null, GrantSource.OneTime));

        var grants = await store.GetGrantsAsync(Purchaser);

        Assert.Equal(2, grants.Count);
    }

    [Fact]
    public async Task Lookup_is_case_and_whitespace_insensitive()
    {
        var store = new InMemoryEntitlementGrantStore();
        await store.PutGrantAsync("Buyer@Example.com", new EntitlementGrant(EntitlementCatalog.LibraryFull, null, GrantSource.OneTime));

        var grants = await store.GetGrantsAsync("  buyer@example.com ");

        Assert.Single(grants);
    }

    [Fact]
    public async Task Unknown_purchaser_reads_empty_never_null()
    {
        var store = new InMemoryEntitlementGrantStore();

        var grants = await store.GetGrantsAsync("nobody@example.com");

        Assert.NotNull(grants);
        Assert.Empty(grants);
    }
}
