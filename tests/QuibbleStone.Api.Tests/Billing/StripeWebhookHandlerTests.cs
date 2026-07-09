// ----------------------------------------------------------------------------
//  StripeWebhookHandlerTests - the SDK-free billing webhook domain core (billing-
//  entitlements/03, #72). Drives StripeWebhookHandler against the real working
//  in-memory grant / account / processed-event stores (no mocking framework), and
//  reads results back through StoredValueEntitlementService (billing-01's gate) so
//  the tests prove the grant is visible exactly where the session-creation check
//  looks (AC-06).
//
//  Pins: idempotency (AC-05), tip-grants-nothing (story 02 AC-02), checkout writes a
//  permanent one-time grant, and the subscription lifecycle lease math (AC-08:
//  renew -> extend to period end; past_due -> extend by grace, never shorten;
//  canceled -> lapse at next read, no live revoke).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Accounts;
using QuibbleStone.Api.Billing;
using QuibbleStone.Api.Entitlements;

namespace QuibbleStone.Api.Tests.Billing;

public class StripeWebhookHandlerTests
{
    private const string Purchaser = "buyer@example.com";
    private const string EventId = "evt_test_1";

    private sealed record Harness(
        StripeWebhookHandler Handler,
        InMemoryAccountStore Accounts,
        InMemoryEntitlementGrantStore Grants,
        InMemoryProcessedEventStore Processed,
        StoredValueEntitlementService Gate);

    private static Harness NewHarness(int graceDays = 7)
    {
        var accounts = new InMemoryAccountStore();
        var grants = new InMemoryEntitlementGrantStore();
        var processed = new InMemoryProcessedEventStore();
        var options = new StripeOptions { PastDueGraceDays = graceDays };
        var handler = new StripeWebhookHandler(grants, accounts, processed, options);
        var gate = new StoredValueEntitlementService(new DefaultUnlockedEntitlementService(), accounts, grants, TestSystemFlags.AllEnabled());
        return new Harness(handler, accounts, grants, processed, gate);
    }

    private static BillingEvent Checkout(GrantSource source, string? purchaser, IReadOnlyList<string> caps, DateTimeOffset? periodEnd = null, string id = EventId)
        => new(id, BillingEventKind.CheckoutCompleted, source, purchaser, caps, periodEnd);

    // Read the purchaser's grants the way production does (accounts-identity/05): resolve
    // the account by email FIRST, then read grants keyed off its stable id. Returns empty
    // when no account exists (a tip / anonymous purchase never created one).
    private static async Task<IReadOnlyList<EntitlementGrant>> GrantsFor(Harness h)
    {
        var account = await h.Accounts.GetByIdentityAsync(Purchaser);
        return account is null ? [] : await h.Grants.GetGrantsAsync(account.Id);
    }

    // AC-03/AC-06: a one-time checkout writes a permanent grant, keyed to the purchaser,
    // readable through billing-01's session-creation gate. The account is auto-created.
    [Fact]
    public async Task Checkout_one_time_grants_a_permanent_capability_readable_via_the_gate()
    {
        var h = NewHarness();

        var outcome = await h.Handler.HandleAsync(Checkout(GrantSource.OneTime, Purchaser, [EntitlementCatalog.LibraryFull]));

        Assert.Equal(WebhookOutcome.Processed, outcome);
        // The account was created by checkout (story 04's "no separate sign-up").
        Assert.NotNull(await h.Accounts.GetByIdentityAsync(Purchaser));
        // The grant is permanent (null lease) and visible to the gate for that purchaser.
        var entitlements = await h.Gate.EvaluateForSession(Purchaser);
        Assert.True(entitlements.IsUnlocked(EntitlementCatalog.LibraryFull));
        var grant = Assert.Single(await GrantsFor(h));
        Assert.Null(grant.ValidThrough);
        Assert.Equal(GrantSource.OneTime, grant.Source);
    }

    // Story 02 AC-02: a tip (no capability keys) grants NOTHING but is acknowledged.
    [Fact]
    public async Task Tip_with_no_capabilities_grants_nothing()
    {
        var h = NewHarness();

        var outcome = await h.Handler.HandleAsync(Checkout(GrantSource.OneTime, Purchaser, []));

        Assert.Equal(WebhookOutcome.Ignored, outcome);
        Assert.Empty(await GrantsFor(h));
    }

    // A purchase with no purchaser email grants nothing (nothing to key a grant to).
    [Fact]
    public async Task Checkout_without_a_purchaser_grants_nothing()
    {
        var h = NewHarness();

        var outcome = await h.Handler.HandleAsync(Checkout(GrantSource.OneTime, purchaser: null, [EntitlementCatalog.LibraryFull]));

        Assert.Equal(WebhookOutcome.Ignored, outcome);
    }

    // AC-05: replaying the SAME event id is a no-op - no double-grant, store unchanged.
    [Fact]
    public async Task Replaying_the_same_event_id_is_idempotent()
    {
        var h = NewHarness();
        var evt = Checkout(GrantSource.Subscription, Purchaser, [EntitlementCatalog.PlayRemote], DateTimeOffset.UtcNow.AddDays(30));

        var first = await h.Handler.HandleAsync(evt);
        var firstGrant = Assert.Single(await GrantsFor(h));

        var second = await h.Handler.HandleAsync(evt);
        var afterGrant = Assert.Single(await GrantsFor(h));

        Assert.Equal(WebhookOutcome.Processed, first);
        Assert.Equal(WebhookOutcome.AlreadyProcessed, second);
        Assert.Equal(firstGrant.ValidThrough, afterGrant.ValidThrough); // unchanged
    }

    // AC-08: invoice.paid (renewal) extends the lease to the new period end.
    [Fact]
    public async Task Renewal_extends_the_lease_to_the_new_period_end()
    {
        var h = NewHarness();
        var firstEnd = DateTimeOffset.UtcNow.AddDays(30);
        var renewedEnd = DateTimeOffset.UtcNow.AddDays(60);
        await h.Handler.HandleAsync(Checkout(GrantSource.Subscription, Purchaser, [EntitlementCatalog.PlayRemote], firstEnd, id: "evt_checkout"));

        await h.Handler.HandleAsync(new BillingEvent(
            "evt_renew", BillingEventKind.SubscriptionRenewed, GrantSource.Subscription, Purchaser, [EntitlementCatalog.PlayRemote], renewedEnd));

        var grant = Assert.Single(await GrantsFor(h));
        Assert.Equal(renewedEnd, grant.ValidThrough);
    }

    // AC-08: past_due extends the lease by the grace window rather than expiring it,
    // and never SHORTENS an already-longer lease.
    [Fact]
    public async Task PastDue_extends_by_grace_and_never_shortens()
    {
        var h = NewHarness(graceDays: 7);
        // Grant near expiry, then past_due -> should extend to ~now+7d.
        await h.Handler.HandleAsync(Checkout(GrantSource.Subscription, Purchaser, [EntitlementCatalog.PlayRemote], DateTimeOffset.UtcNow.AddDays(1), id: "evt_c1"));
        await h.Handler.HandleAsync(new BillingEvent(
            "evt_pastdue", BillingEventKind.SubscriptionPastDue, GrantSource.Subscription, Purchaser, [EntitlementCatalog.PlayRemote], null));

        var grant = Assert.Single(await GrantsFor(h));
        Assert.NotNull(grant.ValidThrough);
        Assert.True(grant.ValidThrough! > DateTimeOffset.UtcNow.AddDays(6), "past_due should extend the lease into the grace window");
        // The family is still unlocked mid-ride.
        Assert.True((await h.Gate.EvaluateForSession(Purchaser)).IsUnlocked(EntitlementCatalog.PlayRemote));
    }

    // AC-08: cancel lets the lease pass so the NEXT session-creation falls back to free -
    // a read-time consequence, never a live mid-session revoke.
    [Fact]
    public async Task Cancel_lapses_the_lease_at_next_read()
    {
        var h = NewHarness();
        await h.Handler.HandleAsync(Checkout(GrantSource.Subscription, Purchaser, [EntitlementCatalog.PlayRemote], DateTimeOffset.UtcNow.AddDays(30), id: "evt_c2"));

        await h.Handler.HandleAsync(new BillingEvent(
            "evt_cancel", BillingEventKind.SubscriptionCanceled, GrantSource.Subscription, Purchaser, [EntitlementCatalog.PlayRemote], null));

        // Next session-creation read: the capability is locked (lease lapsed), while the
        // free baseline is untouched.
        var entitlements = await h.Gate.EvaluateForSession(Purchaser);
        Assert.False(entitlements.IsUnlocked(EntitlementCatalog.PlayRemote));
        Assert.True(entitlements.IsUnlocked(EntitlementCatalog.AiOnDemand));
    }

    // billing-entitlements/08 AC-02: a checkout event carrying a PlanId + StripeSubscriptionId
    // writes a grant with both populated, a non-empty GrantId, and the Mode that verified the
    // event - stamped from the mode PASSED to HandleAsync (the event's provenance), NOT inferred.
    [Fact]
    public async Task Grant_records_plan_subscription_and_the_verifying_mode()
    {
        var h = NewHarness();
        var evt = new BillingEvent(
            "evt_meta", BillingEventKind.CheckoutCompleted, GrantSource.Subscription, Purchaser,
            [EntitlementCatalog.PlayRemote], DateTimeOffset.UtcNow.AddDays(30),
            PlanId: "family-plan", StripeSubscriptionId: "sub_meta_1");

        // The event was verified under the LIVE secret - so the grant records Live even
        // though nothing here says "Live is the active mode".
        await h.Handler.HandleAsync(evt, StripeMode.Live);

        var grant = Assert.Single(await GrantsFor(h));
        Assert.Equal("family-plan", grant.PlanId);
        Assert.Equal("sub_meta_1", grant.StripeSubscriptionId);
        Assert.Equal(StripeMode.Live, grant.Mode);
        Assert.NotEqual(Guid.Empty, grant.GrantId);
    }

    // AC-02: the past_due and canceled write sites also stamp the metadata + mode.
    [Fact]
    public async Task PastDue_and_cancel_writes_also_record_metadata_and_mode()
    {
        var h = NewHarness();
        await h.Handler.HandleAsync(
            new BillingEvent("evt_pd_c", BillingEventKind.CheckoutCompleted, GrantSource.Subscription, Purchaser,
                [EntitlementCatalog.PlayRemote], DateTimeOffset.UtcNow.AddDays(1), PlanId: "family-plan", StripeSubscriptionId: "sub_x"),
            StripeMode.Test);

        await h.Handler.HandleAsync(
            new BillingEvent("evt_pd", BillingEventKind.SubscriptionPastDue, GrantSource.Subscription, Purchaser,
                [EntitlementCatalog.PlayRemote], null, PlanId: "family-plan", StripeSubscriptionId: "sub_x"),
            StripeMode.Test);

        var afterPastDue = Assert.Single(await GrantsFor(h));
        Assert.Equal("sub_x", afterPastDue.StripeSubscriptionId);
        Assert.Equal(StripeMode.Test, afterPastDue.Mode);

        await h.Handler.HandleAsync(
            new BillingEvent("evt_cancel2", BillingEventKind.SubscriptionCanceled, GrantSource.Subscription, Purchaser,
                [EntitlementCatalog.PlayRemote], null, PlanId: "family-plan", StripeSubscriptionId: "sub_x"),
            StripeMode.Test);

        var afterCancel = Assert.Single(await GrantsFor(h));
        Assert.Equal("sub_x", afterCancel.StripeSubscriptionId);
        Assert.Equal(StripeMode.Test, afterCancel.Mode);
    }

    // An Ignored event (unrecognized) is acknowledged and never recorded/applied.
    [Fact]
    public async Task Ignored_event_is_a_no_op()
    {
        var h = NewHarness();

        var outcome = await h.Handler.HandleAsync(new BillingEvent(
            "evt_other", BillingEventKind.Ignored, GrantSource.OneTime, Purchaser, [EntitlementCatalog.LibraryFull], null));

        Assert.Equal(WebhookOutcome.Ignored, outcome);
        Assert.Empty(await GrantsFor(h));
    }
}
