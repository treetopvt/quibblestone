// ----------------------------------------------------------------------------
//  EntitlementGrantTableMappingTests - the Table Storage schema round-trip for the
//  grant store (billing-entitlements/08, #215, AC-01/AC-03). Exercises the public
//  static ToEntity / FromEntity mapping directly (no Azure connection needed, the same
//  testable-static seam as StripeCheckoutService.BuildSessionOptions), so the story's
//  back-compat guarantee is unit-pinned:
//    - AC-01: a full grant (with GrantId / PlanId / StripeSubscriptionId / Mode)
//      round-trips through the TableEntity unchanged.
//    - AC-03: a row written by the ALREADY-SHIPPED code (no new columns) deserializes
//      WITHOUT error - GrantId mints fresh, PlanId / StripeSubscriptionId read null,
//      Mode defaults to Test, and IsActiveAt is byte-for-byte unchanged.
//    - An operator comp (Mode intentionally null) round-trips back to null via the
//      sentinel, NOT to the legacy Test default.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Azure.Data.Tables;
using Microsoft.Extensions.Logging.Abstractions;
using QuibbleStone.Api.Billing;
using QuibbleStone.Api.Entitlements;

namespace QuibbleStone.Api.Tests;

public class EntitlementGrantTableMappingTests
{
    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly NullLogger<TableStorageEntitlementGrantStore> Logger = new();

    // AC-01: a full subscription grant round-trips through the stored entity unchanged.
    [Fact]
    public void Full_grant_round_trips_through_the_entity()
    {
        var validThrough = DateTimeOffset.UtcNow.AddDays(30);
        var written = new EntitlementGrant(
            EntitlementCatalog.PlayRemote, validThrough, GrantSource.Subscription,
            PlanId: "family-plan", StripeSubscriptionId: "sub_123", Mode: StripeMode.Live);

        var entity = TableStorageEntitlementGrantStore.ToEntity(AccountId.ToString(), written);
        var read = TableStorageEntitlementGrantStore.FromEntity(entity, Logger);

        Assert.Equal(EntitlementCatalog.PlayRemote, read.CapabilityKey);
        Assert.Equal(validThrough, read.ValidThrough);
        Assert.Equal(GrantSource.Subscription, read.Source);
        Assert.Equal(written.GrantId, read.GrantId);
        Assert.Equal("family-plan", read.PlanId);
        Assert.Equal("sub_123", read.StripeSubscriptionId);
        Assert.Equal(StripeMode.Live, read.Mode);
    }

    // AC-01: an operator comp (Mode intentionally null) round-trips back to null - the
    // sentinel keeps it distinct from a legacy "missing column" row (which defaults to Test).
    [Fact]
    public void Operator_comp_mode_round_trips_to_null()
    {
        var comp = new EntitlementGrant(EntitlementCatalog.LibraryFull, null, GrantSource.Operator); // Mode null

        var entity = TableStorageEntitlementGrantStore.ToEntity(AccountId.ToString(), comp);
        var read = TableStorageEntitlementGrantStore.FromEntity(entity, Logger);

        Assert.Null(read.Mode);
        Assert.Equal(GrantSource.Operator, read.Source);
        Assert.Null(read.PlanId);
        Assert.Null(read.StripeSubscriptionId);
    }

    // AC-03: a pre-story row (only the original ValidThrough + Source columns, none of the
    // new metadata) deserializes without error and degrades to sane defaults.
    [Fact]
    public void Legacy_row_without_the_new_columns_degrades_to_sane_defaults()
    {
        var validThrough = DateTimeOffset.UtcNow.AddDays(10);
        // Exactly the shape the already-shipped code wrote: two columns, no metadata.
        var legacy = new TableEntity(AccountId.ToString(), EntitlementCatalog.PlayRemote)
        {
            ["ValidThrough"] = validThrough,
            ["Source"] = nameof(GrantSource.Subscription),
        };

        var read = TableStorageEntitlementGrantStore.FromEntity(legacy, Logger);

        Assert.NotEqual(Guid.Empty, read.GrantId);          // minted fresh, not thrown
        Assert.Null(read.PlanId);                           // absent -> null
        Assert.Null(read.StripeSubscriptionId);             // absent -> null
        Assert.Equal(StripeMode.Test, read.Mode);           // the factual default (AC-03)
        Assert.Equal(GrantSource.Subscription, read.Source);
        // IsActiveAt is byte-for-byte unchanged: active before the lease end, expired at it.
        Assert.True(read.IsActiveAt(validThrough.AddSeconds(-1)));
        Assert.False(read.IsActiveAt(validThrough));
    }

    // AC-03: a permanent legacy pack row (no ValidThrough, no new columns) still reads as a
    // permanent, always-active grant.
    [Fact]
    public void Legacy_permanent_row_reads_as_permanent()
    {
        var legacy = new TableEntity(AccountId.ToString(), EntitlementCatalog.Pack("spooky"))
        {
            ["Source"] = nameof(GrantSource.OneTime),
            // No ValidThrough column at all (a permanent one-time pack).
        };

        var read = TableStorageEntitlementGrantStore.FromEntity(legacy, Logger);

        Assert.Null(read.ValidThrough);
        Assert.True(read.IsActiveAt(DateTimeOffset.UtcNow.AddYears(5)));
        Assert.Equal(StripeMode.Test, read.Mode);
    }

    // A fully empty / drifted row (not even a Source column) still does not throw: Source
    // degrades to OneTime, Mode to Test - the existing defensive posture, extended.
    [Fact]
    public void Row_with_no_recognizable_columns_does_not_throw()
    {
        var drifted = new TableEntity(AccountId.ToString(), EntitlementCatalog.LibraryFull);

        var read = TableStorageEntitlementGrantStore.FromEntity(drifted, Logger);

        Assert.Equal(GrantSource.OneTime, read.Source);
        Assert.Equal(StripeMode.Test, read.Mode);
        Assert.NotEqual(Guid.Empty, read.GrantId);
    }
}
