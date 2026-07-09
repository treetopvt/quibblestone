// ----------------------------------------------------------------------------
//  OperatorActionLogTests - the core seam / ordering / retention / validation suite
//  for sysadmin-console/06 (issue #233): the operator action log every money /
//  moderation / settings call site appends through.
//
//  WHAT THIS PROVES:
//    - AC-01: each of grant / revoke / confirm / restore / stripe-mode-flip writes
//      EXACTLY one row, with the right operator email, action verb, and target. The
//      action verb is a free-form string - no closed enum (a direct-seam check).
//    - AC-01a (log-before-act): a THROWING action log aborts the effectful write
//      before it ever runs - proving ordering, not "log after the effect succeeds".
//    - AC-02 (one seam): a single shared fake IOperatorActionLog captures rows from
//      every controller that appends - there is no second log store anywhere.
//    - AC-04 (retention floor): OperatorActionLogPolicy.ClampRetentionDays never
//      resolves below MinRetentionDays, and InMemoryOperatorActionLog.PruneAsync
//      never evicts a row still within the floor, regardless of a lower configured
//      value or volume.
//    - AC-07 (write-side validation): AppendAsync rejects a malformed / markup-
//      bearing email-shaped target before it is ever written; a non-email target
//      (a slug, "stripe-mode", a settings key) only needs the plain sanity bound.
//
//  ANONYMITY (AC-06): every row and assertion here is operator email + action verb +
//  target + note + timestamp. Nothing references a player nickname, room, or session.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using QuibbleStone.Api.Accounts;
using QuibbleStone.Api.Admin;
using QuibbleStone.Api.Billing;
using QuibbleStone.Api.Entitlements;
using QuibbleStone.Api.PublishedTales;
using QuibbleStone.Api.Settings;

namespace QuibbleStone.Api.Tests.Admin;

public sealed class OperatorActionLogTests
{
    private const string OperatorEmail = "ops@quibblestone.com";
    private const string Buyer = "buyer@example.com";

    private static ClaimsPrincipal OperatorPrincipal(string email = OperatorEmail) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, email)], "Operator"));

    private static ControllerContext ContextFor(ClaimsPrincipal principal) =>
        new() { HttpContext = new DefaultHttpContext { User = principal } };

    private static PublishedTale SampleTale(string slug) => new(
        Slug: slug,
        Title: "The flagged saga",
        Parts: [new TalePart(false, "Once a "), new TalePart(true, "wombat"), new TalePart(false, " sang.")],
        BylineNames: "Sam & Mia",
        CreatedUtc: DateTimeOffset.UtcNow,
        ExpiresUtc: DateTimeOffset.UtcNow + PublishedTalesController.TaleTtl);

    // ---- AC-01: one row per successful action, right email / action / target ------

    [Fact]
    public async Task Grant_writes_exactly_one_row_with_the_operator_email_action_and_target()
    {
        var actionLog = new InMemoryOperatorActionLog();
        var accounts = new InMemoryAccountStore();
        var grants = new InMemoryEntitlementGrantStore();
        var controller = new AdminEntitlementsController(accounts, grants, actionLog)
        {
            ControllerContext = ContextFor(OperatorPrincipal()),
        };

        await controller.Grant(Buyer, new GrantEntitlementRequest(EntitlementCatalog.LibraryFull, null), CancellationToken.None);

        var row = Assert.Single(actionLog.Entries);
        Assert.Equal(OperatorEmail, row.OperatorEmail);
        Assert.Equal("entitlement.grant", row.Action);
        Assert.Equal(Buyer, row.Target);
    }

    [Fact]
    public async Task Revoke_writes_exactly_one_row_with_the_operator_email_action_and_target()
    {
        var actionLog = new InMemoryOperatorActionLog();
        var accounts = new InMemoryAccountStore();
        var grants = new InMemoryEntitlementGrantStore();
        var controller = new AdminEntitlementsController(accounts, grants, actionLog)
        {
            ControllerContext = ContextFor(OperatorPrincipal()),
        };
        await controller.Grant(Buyer, new GrantEntitlementRequest(EntitlementCatalog.LibraryFull, null), CancellationToken.None);

        await controller.Revoke(Buyer, EntitlementCatalog.LibraryFull, CancellationToken.None);

        Assert.Equal(2, actionLog.Entries.Count); // the grant row + the revoke row
        var revokeRow = actionLog.Entries[^1];
        Assert.Equal(OperatorEmail, revokeRow.OperatorEmail);
        Assert.Equal("entitlement.revoke", revokeRow.Action);
        Assert.Equal(Buyer, revokeRow.Target);
    }

    [Fact]
    public async Task Confirm_writes_exactly_one_row_with_the_operator_email_action_and_target()
    {
        var actionLog = new InMemoryOperatorActionLog();
        var store = new FakePublishedTaleStore();
        store.Seed(SampleTale("HIDDENSLUG12"));
        store.SeedModeration("HIDDENSLUG12", reportCount: 3, isHidden: true);
        var controller = new ReportedTalesController(store, actionLog)
        {
            ControllerContext = ContextFor(OperatorPrincipal()),
        };

        await controller.Confirm("HIDDENSLUG12", CancellationToken.None);

        var row = Assert.Single(actionLog.Entries);
        Assert.Equal(OperatorEmail, row.OperatorEmail);
        Assert.Equal("tale.takedown", row.Action);
        Assert.Equal("HIDDENSLUG12", row.Target);
    }

    [Fact]
    public async Task Restore_writes_exactly_one_row_with_the_operator_email_action_and_target()
    {
        var actionLog = new InMemoryOperatorActionLog();
        var store = new FakePublishedTaleStore();
        store.Seed(SampleTale("HIDDENSLUG12"));
        store.SeedModeration("HIDDENSLUG12", reportCount: 3, isHidden: true);
        var controller = new ReportedTalesController(store, actionLog)
        {
            ControllerContext = ContextFor(OperatorPrincipal()),
        };

        await controller.Restore("HIDDENSLUG12", CancellationToken.None);

        var row = Assert.Single(actionLog.Entries);
        Assert.Equal(OperatorEmail, row.OperatorEmail);
        Assert.Equal("tale.restore", row.Action);
        Assert.Equal("HIDDENSLUG12", row.Target);
    }

    [Fact]
    public async Task StripeModeFlip_writes_exactly_one_row_with_the_operator_email_action_and_target()
    {
        var actionLog = new InMemoryOperatorActionLog();
        var context = new ActiveStripeContext(new InMemoryActiveStripeModeStore(), new StripeOptions());
        var controller = new StripeModeController(context, actionLog, NullLoggerFor<StripeModeController>())
        {
            ControllerContext = ContextFor(OperatorPrincipal()),
        };

        await controller.Set(new StripeModeChangeBody("live"), CancellationToken.None);

        var row = Assert.Single(actionLog.Entries);
        Assert.Equal(OperatorEmail, row.OperatorEmail);
        Assert.Equal("stripe-mode.flip", row.Action);
        Assert.Equal("stripe-mode", row.Target);
        Assert.Equal("live", row.Note);
    }

    [Fact]
    public async Task The_action_verb_accepts_an_arbitrary_free_form_string_no_closed_enum()
    {
        // A direct-seam check (no controller involved): a future call site (story 07's
        // support verbs) can start appending its own action verb with zero change here.
        var actionLog = new InMemoryOperatorActionLog();

        await actionLog.AppendAsync(OperatorEmail, "future.support.verb", "some-target", "a free-text note", CancellationToken.None);

        var row = Assert.Single(actionLog.Entries);
        Assert.Equal("future.support.verb", row.Action);
    }

    // ---- AC-01a: log-before-act -----------------------------------------------------

    /// <summary>An IOperatorActionLog whose AppendAsync always throws, to prove ordering (log-before-act).</summary>
    private sealed class ThrowingActionLog : IOperatorActionLog
    {
        public Task AppendAsync(string operatorEmail, string action, string target, string note, CancellationToken ct = default)
            => throw new InvalidOperationException("The action log is unavailable.");

        public Task<IReadOnlyList<OperatorActionLogEntry>> ListRecentAsync(int maxItems, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<OperatorActionLogEntry>>([]);

        public Task<int> PruneAsync(int? configuredRetentionDays, CancellationToken ct = default) => Task.FromResult(0);
    }

    /// <summary>Wraps a real IEntitlementGrantStore and counts how many times PutGrantAsync was called.</summary>
    private sealed class SpyEntitlementGrantStore : IEntitlementGrantStore
    {
        private readonly IEntitlementGrantStore _inner = new InMemoryEntitlementGrantStore();
        public int PutGrantCallCount { get; private set; }
        public bool PutGrantCalled => PutGrantCallCount > 0;

        public Task<IReadOnlyList<EntitlementGrant>> GetGrantsAsync(Guid accountId, CancellationToken ct = default)
            => _inner.GetGrantsAsync(accountId, ct);

        public Task PutGrantAsync(Guid accountId, EntitlementGrant grant, CancellationToken ct = default)
        {
            PutGrantCallCount++;
            return _inner.PutGrantAsync(accountId, grant, ct);
        }
    }

    /// <summary>Wraps a FakePublishedTaleStore and records whether the moderation effect ran.</summary>
    private sealed class SpyPublishedTaleStore : IPublishedTaleStore
    {
        private readonly FakePublishedTaleStore _inner;
        public bool ConfirmHiddenCalled { get; private set; }
        public bool RestoreCalled { get; private set; }

        public SpyPublishedTaleStore(FakePublishedTaleStore inner) => _inner = inner;

        public bool IsEnabled => _inner.IsEnabled;
        public Task PublishAsync(PublishedTale tale, CancellationToken cancellationToken = default) => _inner.PublishAsync(tale, cancellationToken);
        public Task<PublishedTale?> GetAsync(string slug, CancellationToken cancellationToken = default) => _inner.GetAsync(slug, cancellationToken);
        public Task RevokeAsync(string slug, CancellationToken cancellationToken = default) => _inner.RevokeAsync(slug, cancellationToken);
        public Task<TaleModerationState?> ReportAsync(string slug, int autoHideThreshold, CancellationToken cancellationToken = default) =>
            _inner.ReportAsync(slug, autoHideThreshold, cancellationToken);
        public Task<TaleModerationState> GetModerationAsync(string slug, CancellationToken cancellationToken = default) => _inner.GetModerationAsync(slug, cancellationToken);
        public Task<IReadOnlyList<ReportedTaleView>> ListHiddenAsync(CancellationToken cancellationToken = default) => _inner.ListHiddenAsync(cancellationToken);

        public Task<bool> ConfirmHiddenAsync(string slug, CancellationToken cancellationToken = default)
        {
            ConfirmHiddenCalled = true;
            return _inner.ConfirmHiddenAsync(slug, cancellationToken);
        }

        public Task<bool> RestoreAsync(string slug, CancellationToken cancellationToken = default)
        {
            RestoreCalled = true;
            return _inner.RestoreAsync(slug, cancellationToken);
        }
    }

    /// <summary>Wraps a real ActiveStripeContext (over an in-memory mode store) and records whether SetModeAsync ran.</summary>
    private sealed class SpyActiveStripeContext : IActiveStripeContext
    {
        private readonly IActiveStripeContext _inner = new ActiveStripeContext(new InMemoryActiveStripeModeStore(), new StripeOptions());
        public bool SetModeCalled { get; private set; }
        public bool IsBillingConfigured => _inner.IsBillingConfigured;

        public Task<StripeModeState> GetStateAsync(CancellationToken ct = default) => _inner.GetStateAsync(ct);
        public Task<StripeModeConfig> GetActiveConfigAsync(CancellationToken ct = default) => _inner.GetActiveConfigAsync(ct);
        public StripeModeConfig ForMode(StripeMode mode) => _inner.ForMode(mode);

        public Task SetModeAsync(StripeMode mode, CancellationToken ct = default)
        {
            SetModeCalled = true;
            return _inner.SetModeAsync(mode, ct);
        }
    }

    [Fact]
    public async Task Grant_never_writes_the_entitlement_when_the_action_log_throws()
    {
        var accounts = new InMemoryAccountStore();
        var grants = new SpyEntitlementGrantStore();
        var controller = new AdminEntitlementsController(accounts, grants, new ThrowingActionLog())
        {
            ControllerContext = ContextFor(OperatorPrincipal()),
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            controller.Grant(Buyer, new GrantEntitlementRequest(EntitlementCatalog.LibraryFull, null), CancellationToken.None));

        Assert.False(grants.PutGrantCalled);
    }

    [Fact]
    public async Task Revoke_never_writes_the_lapse_when_the_action_log_throws()
    {
        var accounts = new InMemoryAccountStore();
        var grants = new SpyEntitlementGrantStore();
        // Grant first (through a WORKING log) so the account exists and the revoke below reaches its effect.
        var setupController = new AdminEntitlementsController(accounts, grants, new InMemoryOperatorActionLog())
        {
            ControllerContext = ContextFor(OperatorPrincipal()),
        };
        await setupController.Grant(Buyer, new GrantEntitlementRequest(EntitlementCatalog.LibraryFull, null), CancellationToken.None);
        var callsBeforeRevoke = grants.PutGrantCallCount;

        var controller = new AdminEntitlementsController(accounts, grants, new ThrowingActionLog())
        {
            ControllerContext = ContextFor(OperatorPrincipal()),
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            controller.Revoke(Buyer, EntitlementCatalog.LibraryFull, CancellationToken.None));

        // No NEW PutGrantAsync call happened during the revoke attempt - the log threw first.
        Assert.Equal(callsBeforeRevoke, grants.PutGrantCallCount);
    }

    [Fact]
    public async Task Confirm_never_takes_down_the_tale_when_the_action_log_throws()
    {
        var inner = new FakePublishedTaleStore();
        inner.Seed(SampleTale("HIDDENSLUG12"));
        inner.SeedModeration("HIDDENSLUG12", reportCount: 3, isHidden: true);
        var store = new SpyPublishedTaleStore(inner);
        var controller = new ReportedTalesController(store, new ThrowingActionLog())
        {
            ControllerContext = ContextFor(OperatorPrincipal()),
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.Confirm("HIDDENSLUG12", CancellationToken.None));

        Assert.False(store.ConfirmHiddenCalled);
        // The tale is still there and still hidden (the takedown never ran).
        Assert.NotNull(await inner.GetAsync("HIDDENSLUG12", CancellationToken.None));
    }

    [Fact]
    public async Task Restore_never_resumes_serving_when_the_action_log_throws()
    {
        var inner = new FakePublishedTaleStore();
        inner.Seed(SampleTale("HIDDENSLUG12"));
        inner.SeedModeration("HIDDENSLUG12", reportCount: 3, isHidden: true);
        var store = new SpyPublishedTaleStore(inner);
        var controller = new ReportedTalesController(store, new ThrowingActionLog())
        {
            ControllerContext = ContextFor(OperatorPrincipal()),
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.Restore("HIDDENSLUG12", CancellationToken.None));

        Assert.False(store.RestoreCalled);
        var state = await inner.GetModerationAsync("HIDDENSLUG12", CancellationToken.None);
        Assert.True(state.IsHidden); // still hidden - the restore effect never ran
    }

    [Fact]
    public async Task StripeModeSet_never_flips_the_mode_when_the_action_log_throws()
    {
        var context = new SpyActiveStripeContext();
        var controller = new StripeModeController(context, new ThrowingActionLog(), NullLoggerFor<StripeModeController>())
        {
            ControllerContext = ContextFor(OperatorPrincipal()),
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            controller.Set(new StripeModeChangeBody("live"), CancellationToken.None));

        Assert.False(context.SetModeCalled);
    }

    // ---- AC-02: one seam, shared by every controller -------------------------------

    [Fact]
    public async Task One_shared_action_log_captures_rows_from_every_controller()
    {
        var actionLog = new InMemoryOperatorActionLog();
        var principal = OperatorPrincipal();

        var entitlementsController = new AdminEntitlementsController(
            new InMemoryAccountStore(), new InMemoryEntitlementGrantStore(), actionLog)
        { ControllerContext = ContextFor(principal) };
        await entitlementsController.Grant(Buyer, new GrantEntitlementRequest(EntitlementCatalog.LibraryFull, null), CancellationToken.None);

        var taleStore = new FakePublishedTaleStore();
        taleStore.Seed(SampleTale("HIDDENSLUG12"));
        taleStore.SeedModeration("HIDDENSLUG12", reportCount: 3, isHidden: true);
        var talesController = new ReportedTalesController(taleStore, actionLog) { ControllerContext = ContextFor(principal) };
        await talesController.Confirm("HIDDENSLUG12", CancellationToken.None);

        var stripeController = new StripeModeController(
            new ActiveStripeContext(new InMemoryActiveStripeModeStore(), new StripeOptions()), actionLog, NullLoggerFor<StripeModeController>())
        { ControllerContext = ContextFor(principal) };
        await stripeController.Set(new StripeModeChangeBody("live"), CancellationToken.None);

        Assert.Equal(3, actionLog.Entries.Count);
        Assert.Contains(actionLog.Entries, e => e.Action == "entitlement.grant");
        Assert.Contains(actionLog.Entries, e => e.Action == "tale.takedown");
        Assert.Contains(actionLog.Entries, e => e.Action == "stripe-mode.flip");
    }

    // ---- AC-04: the retention floor -------------------------------------------------

    [Theory]
    [InlineData(null, 180)]
    [InlineData(30, 180)]
    [InlineData(0, 180)]
    [InlineData(-5, 180)]
    [InlineData(180, 180)]
    [InlineData(400, 400)]
    public void ClampRetentionDays_never_resolves_below_the_hard_floor(int? configured, int expected)
    {
        Assert.Equal(expected, OperatorActionLogPolicy.ClampRetentionDays(configured));
    }

    [Fact]
    public async Task PruneAsync_never_evicts_a_row_still_within_the_floor_even_with_a_below_floor_configured_value()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var actionLog = new InMemoryOperatorActionLog(() => now);
        await actionLog.AppendAsync(OperatorEmail, "settings.put", "some-key", "note", CancellationToken.None);

        // Advance 90 days: below the 180-day floor. A below-floor configured value (30) must not evict it.
        now = now.AddDays(90);
        var removedAt90 = await actionLog.PruneAsync(30, CancellationToken.None);
        Assert.Equal(0, removedAt90);
        Assert.Single(actionLog.Entries);

        // Advance to 200 days old: past the floor. The same below-floor configured value now prunes it.
        now = now.AddDays(110); // 90 + 110 = 200 days since the append
        var removedAt200 = await actionLog.PruneAsync(30, CancellationToken.None);
        Assert.Equal(1, removedAt200);
        Assert.Empty(actionLog.Entries);
    }

    // ---- AC-07: write-side target validation ----------------------------------------

    [Theory]
    [InlineData("not an email <script>@x")]
    [InlineData("a@b <img>")]
    [InlineData("a@b@c")]
    public async Task AppendAsync_rejects_a_malformed_email_shaped_target(string target)
    {
        var actionLog = new InMemoryOperatorActionLog();

        await Assert.ThrowsAsync<InvalidOperatorActionTargetException>(() =>
            actionLog.AppendAsync(OperatorEmail, "settings.put", target, "note", CancellationToken.None));
        Assert.Empty(actionLog.Entries);
    }

    [Theory]
    [InlineData("buyer@example.com")]
    [InlineData("the-flagged-saga-slug")]
    [InlineData("stripe-mode")]
    [InlineData("ai.dailyBudgetUsd")]
    public async Task AppendAsync_accepts_a_valid_email_or_a_non_email_target(string target)
    {
        var actionLog = new InMemoryOperatorActionLog();

        await actionLog.AppendAsync(OperatorEmail, "settings.put", target, "note", CancellationToken.None);

        var row = Assert.Single(actionLog.Entries);
        Assert.Equal(target, row.Target);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsValidTarget_rejects_an_empty_or_whitespace_target(string? target)
    {
        Assert.False(OperatorActionLogPolicy.IsValidTarget(target));
    }

    [Fact]
    public void IsValidTarget_rejects_a_target_over_the_max_length()
    {
        Assert.False(OperatorActionLogPolicy.IsValidTarget(new string('a', OperatorActionLogPolicy.MaxTargetLength + 1)));
    }

    [Fact]
    public void IsValidTarget_accepts_a_well_formed_email()
    {
        Assert.True(OperatorActionLogPolicy.IsValidTarget("buyer@example.com"));
    }

    [Fact]
    public void IsValidTarget_accepts_a_non_email_target_like_a_slug_or_stripe_mode()
    {
        Assert.True(OperatorActionLogPolicy.IsValidTarget("some-tale-slug"));
        Assert.True(OperatorActionLogPolicy.IsValidTarget("stripe-mode"));
    }

    [Fact]
    public void IsValidTarget_rejects_a_markup_bearing_email_shaped_target()
    {
        Assert.False(OperatorActionLogPolicy.IsValidTarget("a@b <script>alert(1)</script>"));
    }

    // ---- helpers ---------------------------------------------------------------------

    private static Microsoft.Extensions.Logging.ILogger<T> NullLoggerFor<T>() =>
        Microsoft.Extensions.Logging.Abstractions.NullLogger<T>.Instance;
}
