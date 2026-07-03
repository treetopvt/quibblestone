// ----------------------------------------------------------------------------
//  EntitlementServiceTests - the thin, default-unlocked entitlement seam consumed
//  at session-creation (ai-cost-gate/02, #121).
//
//  These lock in the CONTRACT + the alpha default (ADR 0001 decision C):
//    - AC-02: the reserved ai.* capability key exists in the catalog.
//    - AC-03: EvaluateForSession with NO purchaser returns the ai.* key UNLOCKED
//      (so the jumble is reachable by every session; shipping changes zero
//      behavior).
//    - AC-05: the check needs no player identity - a null purchaser (the alpha
//      norm) still returns the default-unlocked set; a (future) purchaser is
//      likewise unlocked in alpha.
//
//  When billing-entitlements/01 (#70) lands it replaces the implementation behind
//  this SAME IEntitlementService contract; these tests then pin the alpha
//  default-unlocked behavior specifically.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Entitlements;

namespace QuibbleStone.Api.Tests;

public class EntitlementServiceTests
{
    // AC-02: the reserved AI word-bank capability key exists in the catalog (a
    // reservation the jumble consumes, not a rival catalog).
    [Fact]
    public void Catalog_reserves_the_ai_capability_key()
    {
        Assert.Equal("ai.onDemand", EntitlementCatalog.AiOnDemand);
        Assert.Contains(EntitlementCatalog.AiOnDemand, EntitlementCatalog.AiCapabilities);
    }

    // billing-entitlements/01 AC-01: the catalog is extended beyond the ai.* reservation
    // to the full product key set - still one string-keyed catalog, with an open-ended
    // pack.<id> family built from a stable id.
    [Fact]
    public void Catalog_contains_the_full_capability_key_set()
    {
        Assert.Equal("library.full", EntitlementCatalog.LibraryFull);
        Assert.Equal("play.remote", EntitlementCatalog.PlayRemote);
        Assert.Equal("play.largeGroup", EntitlementCatalog.PlayLargeGroup);
        Assert.Equal("pack.", EntitlementCatalog.PackPrefix);
        Assert.Equal("pack.spooky", EntitlementCatalog.Pack("spooky"));
    }

    // AC-03 / AC-05: no purchaser (every alpha session) -> the ai.* capability is
    // UNLOCKED. Default-unlocked, non-blocking, anonymous.
    [Fact]
    public async Task EvaluateForSession_with_no_purchaser_returns_ai_capability_unlocked()
    {
        var service = new DefaultUnlockedEntitlementService();

        var entitlements = await service.EvaluateForSession();

        Assert.True(entitlements.IsUnlocked(EntitlementCatalog.AiOnDemand));
        Assert.Contains(EntitlementCatalog.AiOnDemand, entitlements.UnlockedCapabilities);
    }

    // AC-05: passing null explicitly (the anonymous, no-accounts norm) behaves the
    // same as the default - the seam never needs a player identity.
    [Fact]
    public async Task EvaluateForSession_with_null_purchaser_is_unlocked()
    {
        var service = new DefaultUnlockedEntitlementService();

        var entitlements = await service.EvaluateForSession(purchaserIdentity: null);

        Assert.True(entitlements.IsUnlocked(EntitlementCatalog.AiOnDemand));
    }

    // The contract accepts an optional purchaser identity so #70 can key a real
    // grant off it later WITHOUT changing this signature; in alpha it is still
    // unlocked (default-unlocked ignores the identity).
    [Fact]
    public async Task EvaluateForSession_with_a_purchaser_is_still_unlocked_in_alpha()
    {
        var service = new DefaultUnlockedEntitlementService();

        var entitlements = await service.EvaluateForSession(purchaserIdentity: "some-future-purchaser");

        Assert.True(entitlements.IsUnlocked(EntitlementCatalog.AiOnDemand));
    }

    // An unknown/unreserved capability key reads as locked - the set is explicit,
    // so a stray key is never accidentally granted (this matters once #70 turns
    // some keys entitlement-required).
    [Fact]
    public async Task Unknown_capability_reads_as_locked()
    {
        var service = new DefaultUnlockedEntitlementService();

        var entitlements = await service.EvaluateForSession();

        Assert.False(entitlements.IsUnlocked("ai.someUnreservedCapability"));
    }
}
