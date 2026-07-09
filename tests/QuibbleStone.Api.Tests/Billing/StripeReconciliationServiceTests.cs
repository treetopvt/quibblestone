// ----------------------------------------------------------------------------
//  StripeReconciliationServiceTests - the pure core of the per-account Stripe resync
//  (billing-entitlements/08, #215). Drives StripeReconciliationService against a FAKE
//  IStripeSubscriptionSource (normalized candidates - no live Stripe), the REAL
//  ActiveStripeContext over an in-memory mode store, and the REAL in-memory grant
//  store, so the two BINDING security rules are proven in tested code:
//    - AC-04 (metadata-matched identity, never email-steerable): given two candidate
//      subscriptions sharing an email - one with our qs_purchaser metadata, one without
//      (an attacker-created customer) - only the metadata-matching one is reconciled;
//      the other is never read for a write.
//    - AC-08 (mode-safety, symmetric): a stored Live grant is left byte-for-byte
//      untouched by a Test-mode resync, and vice versa.
//    - AC-05: a purchaser holding only a one-time pack (or an operator comp) is
//      unchanged by a resync run.
//    - AC-06 idempotency: running twice against the same state yields the same grants.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.Extensions.Logging.Abstractions;
using QuibbleStone.Api.Accounts;
using QuibbleStone.Api.Billing;
using QuibbleStone.Api.Entitlements;

namespace QuibbleStone.Api.Tests.Billing;

public class StripeReconciliationServiceTests
{
    private static readonly Account Purchaser =
        new(Guid.NewGuid(), "buyer@example.com", DateTimeOffset.UtcNow);

    // A fake Stripe edge: returns a fixed candidate set regardless of the email (the
    // service applies the metadata match, which is the point under test).
    private sealed class FakeSubscriptionSource(IReadOnlyList<ReconciliationCandidate> candidates, bool enabled = true)
        : IStripeSubscriptionSource
    {
        public bool IsEnabled => enabled;
        public string? LastEmail { get; private set; }

        public Task<IReadOnlyList<ReconciliationCandidate>> ListCandidatesAsync(string email, CancellationToken ct = default)
        {
            LastEmail = email;
            return Task.FromResult(candidates);
        }
    }

    private sealed record Harness(
        StripeReconciliationService Service,
        InMemoryEntitlementGrantStore Grants,
        FakeSubscriptionSource Source);

    private static async Task<Harness> NewHarness(
        IReadOnlyList<ReconciliationCandidate> candidates, StripeMode activeMode, bool sourceEnabled = true)
    {
        var options = new StripeOptions
        {
            PastDueGraceDays = 7,
            Test = new StripeModeConfig { SecretKey = "sk_test_x" },
            Live = new StripeModeConfig { SecretKey = "sk_live_x" },
        };
        var modeStore = new InMemoryActiveStripeModeStore();
        var context = new ActiveStripeContext(modeStore, options);
        await context.SetModeAsync(activeMode); // the ACTIVE mode for this run
        var grants = new InMemoryEntitlementGrantStore();
        var source = new FakeSubscriptionSource(candidates, sourceEnabled);
        var service = new StripeReconciliationService(
            source, context, grants, options, NullLogger<StripeReconciliationService>.Instance);
        return new Harness(service, grants, source);
    }

    private static ReconciliationCandidate Candidate(
        string subscriptionId, string? purchaser, IReadOnlyList<string> caps,
        string status = "active", string? product = "family-plan", DateTimeOffset? periodEnd = null) =>
        new(subscriptionId, purchaser, caps, product, status, periodEnd ?? DateTimeOffset.UtcNow.AddDays(30));

    // AC-04: two customers share an email; only the one carrying OUR qs_purchaser metadata
    // is reconciled. The attacker-created one (no matching metadata) is never granted.
    [Fact]
    public async Task Only_the_metadata_matching_subscription_is_reconciled()
    {
        var mine = Candidate("sub_mine", Purchaser.Email, [EntitlementCatalog.PlayRemote]);
        // An attacker's self-created Stripe customer under the victim's email: it never
        // went through our checkout, so it carries no matching qs_purchaser metadata.
        var attacker = Candidate("sub_attacker", purchaser: null, [EntitlementCatalog.LibraryFull]);
        var h = await NewHarness([mine, attacker], StripeMode.Test);

        var result = await h.Service.ResyncAccountAsync(Purchaser);

        var grants = await h.Grants.GetGrantsAsync(Purchaser.Id);
        // Exactly the metadata-matched capability was written; the attacker's was skipped.
        Assert.Equal(EntitlementCatalog.PlayRemote, Assert.Single(grants).CapabilityKey);
        Assert.Equal(1, result.Reconciled);
        Assert.Equal(1, result.SkippedUnmatchedIdentity);
    }

    // AC-04: a candidate whose qs_purchaser is a DIFFERENT email (someone else's real
    // subscription that happens to share a customer email row) is also skipped.
    [Fact]
    public async Task A_subscription_stamped_for_another_account_is_skipped()
    {
        var other = Candidate("sub_other", "someone.else@example.com", [EntitlementCatalog.PlayRemote]);
        var h = await NewHarness([other], StripeMode.Test);

        var result = await h.Service.ResyncAccountAsync(Purchaser);

        Assert.Empty(await h.Grants.GetGrantsAsync(Purchaser.Id));
        Assert.Equal(0, result.Reconciled);
        Assert.Equal(1, result.SkippedUnmatchedIdentity);
    }

    // AC-04: the match is case / whitespace insensitive (same normalization the account
    // store uses), so a legitimately-stamped subscription is not lost to casing.
    [Fact]
    public async Task Metadata_match_is_normalized()
    {
        var mine = Candidate("sub_mine", "  Buyer@Example.com  ", [EntitlementCatalog.PlayRemote]);
        var h = await NewHarness([mine], StripeMode.Test);

        var result = await h.Service.ResyncAccountAsync(Purchaser);

        Assert.Equal(1, result.Reconciled);
        Assert.Single(await h.Grants.GetGrantsAsync(Purchaser.Id));
    }

    // AC-08: a stored LIVE grant is left byte-for-byte untouched by a TEST-mode resync.
    [Fact]
    public async Task Test_mode_resync_never_overwrites_a_live_grant()
    {
        var liveLease = DateTimeOffset.UtcNow.AddDays(90);
        var h = await NewHarness(
            [Candidate("sub_mine", Purchaser.Email, [EntitlementCatalog.PlayRemote], periodEnd: DateTimeOffset.UtcNow.AddDays(30))],
            StripeMode.Test);
        // Seed a Live-derived subscription grant for the same capability.
        var liveGrant = new EntitlementGrant(
            EntitlementCatalog.PlayRemote, liveLease, GrantSource.Subscription,
            PlanId: "family-plan", StripeSubscriptionId: "sub_live", Mode: StripeMode.Live);
        await h.Grants.PutGrantAsync(Purchaser.Id, liveGrant);

        var result = await h.Service.ResyncAccountAsync(Purchaser);

        var grant = Assert.Single(await h.Grants.GetGrantsAsync(Purchaser.Id));
        // Untouched: same lease, same mode, same subscription id, same grant id.
        Assert.Equal(StripeMode.Live, grant.Mode);
        Assert.Equal(liveLease, grant.ValidThrough);
        Assert.Equal("sub_live", grant.StripeSubscriptionId);
        Assert.Equal(liveGrant.GrantId, grant.GrantId);
        Assert.Equal(0, result.Reconciled);
        Assert.Equal(1, result.SkippedModeGuard);
    }

    // AC-08 symmetric: a stored TEST grant is left untouched by a LIVE-mode resync.
    [Fact]
    public async Task Live_mode_resync_never_overwrites_a_test_grant()
    {
        var testLease = DateTimeOffset.UtcNow.AddDays(90);
        var h = await NewHarness(
            [Candidate("sub_mine", Purchaser.Email, [EntitlementCatalog.PlayRemote])],
            StripeMode.Live);
        var testGrant = new EntitlementGrant(
            EntitlementCatalog.PlayRemote, testLease, GrantSource.Subscription,
            PlanId: "family-plan", StripeSubscriptionId: "sub_test", Mode: StripeMode.Test);
        await h.Grants.PutGrantAsync(Purchaser.Id, testGrant);

        var result = await h.Service.ResyncAccountAsync(Purchaser);

        var grant = Assert.Single(await h.Grants.GetGrantsAsync(Purchaser.Id));
        Assert.Equal(StripeMode.Test, grant.Mode);
        Assert.Equal(testLease, grant.ValidThrough);
        Assert.Equal(testGrant.GrantId, grant.GrantId);
        Assert.Equal(1, result.SkippedModeGuard);
    }

    // AC-08: a grant already in the ACTIVE mode IS reconciled (the guard only blocks a
    // mismatch), so a genuine same-mode drift is repaired.
    [Fact]
    public async Task Same_mode_subscription_grant_is_reconciled()
    {
        var newEnd = DateTimeOffset.UtcNow.AddDays(30);
        var h = await NewHarness(
            [Candidate("sub_mine", Purchaser.Email, [EntitlementCatalog.PlayRemote], periodEnd: newEnd)],
            StripeMode.Test);
        // A stale Test grant with a shorter lease - resync should extend it to the period end.
        await h.Grants.PutGrantAsync(Purchaser.Id, new EntitlementGrant(
            EntitlementCatalog.PlayRemote, DateTimeOffset.UtcNow.AddDays(1), GrantSource.Subscription, Mode: StripeMode.Test));

        var result = await h.Service.ResyncAccountAsync(Purchaser);

        var grant = Assert.Single(await h.Grants.GetGrantsAsync(Purchaser.Id));
        Assert.Equal(newEnd, grant.ValidThrough);
        Assert.Equal(StripeMode.Test, grant.Mode);
        Assert.Equal("sub_mine", grant.StripeSubscriptionId);
        Assert.Equal(1, result.Reconciled);
    }

    // AC-05: a one-time pack grant is never rewritten by resync (Stripe has no ongoing
    // state to reconcile it against), even when a subscription candidate names the same key.
    [Fact]
    public async Task One_time_pack_grant_is_left_untouched()
    {
        var pack = new EntitlementGrant(EntitlementCatalog.Pack("spooky"), null, GrantSource.OneTime, Mode: StripeMode.Test);
        // No subscription candidate at all (the normal AC-05 case).
        var h = await NewHarness([], StripeMode.Test);
        await h.Grants.PutGrantAsync(Purchaser.Id, pack);

        var result = await h.Service.ResyncAccountAsync(Purchaser);

        var grant = Assert.Single(await h.Grants.GetGrantsAsync(Purchaser.Id));
        Assert.Null(grant.ValidThrough); // still permanent
        Assert.Equal(GrantSource.OneTime, grant.Source);
        Assert.Equal(pack.GrantId, grant.GrantId);
        Assert.Equal(0, result.Reconciled);
    }

    // AC-05: an operator comp (Mode null, Source Operator) is never overwritten, even when
    // a matching subscription candidate names the same capability key.
    [Fact]
    public async Task Operator_comp_is_left_untouched_even_when_a_subscription_names_the_key()
    {
        var comp = new EntitlementGrant(EntitlementCatalog.PlayRemote, null, GrantSource.Operator); // Mode null
        var h = await NewHarness(
            [Candidate("sub_mine", Purchaser.Email, [EntitlementCatalog.PlayRemote])],
            StripeMode.Test);
        await h.Grants.PutGrantAsync(Purchaser.Id, comp);

        var result = await h.Service.ResyncAccountAsync(Purchaser);

        var grant = Assert.Single(await h.Grants.GetGrantsAsync(Purchaser.Id));
        Assert.Equal(GrantSource.Operator, grant.Source);
        Assert.Null(grant.ValidThrough);
        Assert.Equal(comp.GrantId, grant.GrantId);
        Assert.Equal(1, result.SkippedModeGuard);
    }

    // A matched subscription carrying no capability metadata is skipped + counted (never guessed at).
    [Fact]
    public async Task Matched_subscription_without_capability_metadata_is_skipped()
    {
        var h = await NewHarness([Candidate("sub_mine", Purchaser.Email, [])], StripeMode.Test);

        var result = await h.Service.ResyncAccountAsync(Purchaser);

        Assert.Empty(await h.Grants.GetGrantsAsync(Purchaser.Id));
        Assert.Equal(1, result.SkippedNoMetadata);
    }

    // AC-06: running resync twice against the same Stripe state is idempotent - one row per
    // capability, identical the second time (the upsert-by-capability write path).
    [Fact]
    public async Task Resync_is_idempotent()
    {
        var end = DateTimeOffset.UtcNow.AddDays(30);
        var h = await NewHarness(
            [Candidate("sub_mine", Purchaser.Email, [EntitlementCatalog.PlayRemote, EntitlementCatalog.LibraryFull], periodEnd: end)],
            StripeMode.Test);

        await h.Service.ResyncAccountAsync(Purchaser);
        var afterFirst = await h.Grants.GetGrantsAsync(Purchaser.Id);
        await h.Service.ResyncAccountAsync(Purchaser);
        var afterSecond = await h.Grants.GetGrantsAsync(Purchaser.Id);

        Assert.Equal(2, afterFirst.Count);
        Assert.Equal(2, afterSecond.Count); // no duplicate rows
        foreach (var g in afterSecond)
        {
            Assert.Equal(end, g.ValidThrough);
            Assert.Equal(StripeMode.Test, g.Mode);
            Assert.Equal("family-plan", g.PlanId);
            Assert.Equal("sub_mine", g.StripeSubscriptionId);
        }
    }

    // A canceled subscription reconciles to a lapsed lease (its last paid period), so the
    // next session-creation read falls back to free - a read-time lapse, never a live revoke.
    [Fact]
    public async Task Canceled_subscription_reconciles_to_a_lapsed_lease()
    {
        var periodEnd = DateTimeOffset.UtcNow.AddDays(-2); // already past
        var h = await NewHarness(
            [Candidate("sub_mine", Purchaser.Email, [EntitlementCatalog.PlayRemote], status: "canceled", periodEnd: periodEnd)],
            StripeMode.Test);

        await h.Service.ResyncAccountAsync(Purchaser);

        var grant = Assert.Single(await h.Grants.GetGrantsAsync(Purchaser.Id));
        Assert.False(grant.IsActiveAt(DateTimeOffset.UtcNow)); // lapsed
    }

    // When the source is disabled (Stripe not configured), resync is a clean no-op and
    // never touches the grant store.
    [Fact]
    public async Task Disabled_source_is_a_clean_no_op()
    {
        var h = await NewHarness(
            [Candidate("sub_mine", Purchaser.Email, [EntitlementCatalog.PlayRemote])],
            StripeMode.Test, sourceEnabled: false);

        var result = await h.Service.ResyncAccountAsync(Purchaser);

        Assert.False(result.BillingConfigured);
        Assert.Null(result.ActiveMode);
        Assert.Empty(await h.Grants.GetGrantsAsync(Purchaser.Id));
        Assert.Null(h.Source.LastEmail); // never even asked Stripe
    }
}
