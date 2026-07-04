// ----------------------------------------------------------------------------
//  IActiveStripeContext - the single front door every billing consumer uses to get
//  "the credentials for whatever mode is active right now" (billing-entitlements/06).
//  It resolves the persisted active mode (IActiveStripeModeStore) and projects the
//  matching credential set (StripeOptions.ForMode), so StripeCheckoutService,
//  BillingController, and StripeModeController never branch on mode themselves - they
//  ask for the active config and use it.
//
//  HOT-PATH CACHE (AC-02): the resolved mode is cached in-memory for a few seconds so
//  the checkout / products request paths do not take a storage round-trip on every
//  call. A flip through SetModeAsync writes through the store AND resets the cache, so
//  the change is visible within the short TTL (and immediately on the flipping node)
//  without an app restart.
//
//  WEBHOOK NOTE (AC-04): verification does NOT go through the active mode - a webhook
//  must verify against the mode the event actually came from, regardless of which mode
//  is active. ForMode exposes each mode's credentials directly for that dual-secret
//  verification (see StripeWebhookController).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Billing;

/// <summary>
/// Resolves the currently-active Stripe mode and its credentials (billing-entitlements/06),
/// caching the mode briefly for the checkout/products hot paths. The one place mode
/// selection happens so consumers stay mode-agnostic.
/// </summary>
public interface IActiveStripeContext
{
    /// <summary>The active mode + last-changed time (cached briefly). Defaults to Test (AC-05).</summary>
    Task<StripeModeState> GetStateAsync(CancellationToken ct = default);

    /// <summary>The credential set for the currently-active mode (used by checkout + the paywall).</summary>
    Task<StripeModeConfig> GetActiveConfigAsync(CancellationToken ct = default);

    /// <summary>Flips the active mode (operator action, story 07), stamping the change time, and resets the cache.</summary>
    Task SetModeAsync(StripeMode mode, CancellationToken ct = default);

    /// <summary>The credential set for a SPECIFIC mode, regardless of which is active (webhook dual-secret verify, AC-04).</summary>
    StripeModeConfig ForMode(StripeMode mode);

    /// <summary>True when billing is configured at all (any mode has a secret key). Free play is unaffected either way.</summary>
    bool IsBillingConfigured { get; }
}

/// <summary>
/// The default <see cref="IActiveStripeContext"/> (billing-entitlements/06): reads the
/// active mode from <see cref="IActiveStripeModeStore"/> (cached ~a few seconds) and
/// projects <see cref="StripeOptions.ForMode"/>. A singleton over the store + options.
/// </summary>
public sealed class ActiveStripeContext : IActiveStripeContext
{
    // A short cache so the hot paths (checkout, GET products) avoid a storage read per
    // request without noticeably delaying an operator's flip (AC-02). Seconds, not minutes.
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(5);

    private readonly IActiveStripeModeStore _store;
    private readonly StripeOptions _options;

    private readonly object _gate = new();
    private StripeModeState? _cached;
    private DateTime _cachedAtUtc;

    /// <summary>Constructs the context over the active-mode store and the bound Stripe options.</summary>
    public ActiveStripeContext(IActiveStripeModeStore store, StripeOptions options)
    {
        _store = store;
        _options = options;
    }

    /// <inheritdoc />
    public bool IsBillingConfigured => _options.IsConfigured;

    /// <inheritdoc />
    public StripeModeConfig ForMode(StripeMode mode) => _options.ForMode(mode);

    /// <inheritdoc />
    public async Task<StripeModeState> GetStateAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_cached is not null && DateTime.UtcNow - _cachedAtUtc < CacheTtl)
            {
                return _cached;
            }
        }

        var state = await _store.GetAsync(ct);
        lock (_gate)
        {
            _cached = state;
            _cachedAtUtc = DateTime.UtcNow;
        }
        return state;
    }

    /// <inheritdoc />
    public async Task<StripeModeConfig> GetActiveConfigAsync(CancellationToken ct = default)
    {
        var state = await GetStateAsync(ct);
        return _options.ForMode(state.Mode);
    }

    /// <inheritdoc />
    public async Task SetModeAsync(StripeMode mode, CancellationToken ct = default)
    {
        await _store.SetAsync(mode, DateTimeOffset.UtcNow, ct);
        // Write through the cache so the flipping node reflects it immediately; other
        // nodes (if scaled out) pick it up within the short TTL.
        lock (_gate)
        {
            _cached = new StripeModeState(mode, DateTimeOffset.UtcNow);
            _cachedAtUtc = DateTime.UtcNow;
        }
    }
}
