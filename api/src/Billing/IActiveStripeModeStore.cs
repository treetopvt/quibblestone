// ----------------------------------------------------------------------------
//  IActiveStripeModeStore - the persisted "which Stripe mode is ACTIVE right now"
//  flag (billing-entitlements/06). There is exactly ONE active mode for the whole
//  app, changeable at RUNTIME (an operator flip, story 07) without a redeploy, and
//  it must SURVIVE an app restart / recycle - so it is stored, not an app setting
//  (an App Service setting change recycles the app, failing the "no redeploy"
//  requirement, AC-02).
//
//  Mirrors the IEntitlementGrantStore trio EXACTLY (billing-entitlements/01):
//    - TableStorageActiveStripeModeStore : the real store, used when a storage
//      connection string is configured (reuses the SAME storage account - no new
//      resource). A single fixed-key row (there is only one active-mode value).
//    - InMemoryActiveStripeModeStore : a working thread-safe store used when no
//      connection string is configured (local dev / CI / a fresh clone), so the
//      toggle is exercisable end to end with ZERO Azure setup.
//
//  SAFE DEFAULT (AC-05): with nothing yet persisted, the active mode reads as Test -
//  never Live. A fresh or unconfigured environment can never take a real charge by
//  default.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Billing;

/// <summary>
/// The active Stripe mode plus when it last changed (billing-entitlements/06 AC-07).
/// <see cref="LastChangedUtc"/> is null when the mode has never been explicitly set
/// (a fresh environment resolving to the Test default).
/// </summary>
/// <param name="Mode">The mode currently ACTIVE for new checkouts.</param>
/// <param name="LastChangedUtc">When the mode was last changed (null if never set).</param>
public sealed record StripeModeState(StripeMode Mode, DateTimeOffset? LastChangedUtc);

/// <summary>
/// Reads and writes the single app-wide active Stripe mode (billing-entitlements/06).
/// One implementation persists to Azure Table Storage (deployed); the other is a
/// working in-memory store used when no storage connection string is configured.
/// A fresh store resolves to <see cref="StripeMode.Test"/> (AC-05).
/// </summary>
public interface IActiveStripeModeStore
{
    /// <summary>
    /// Reads the current active mode + last-changed time. Never null; with nothing
    /// persisted it returns <c>(Test, null)</c> (the safe default, AC-05). A read is a
    /// read - it never creates or changes anything.
    /// </summary>
    Task<StripeModeState> GetAsync(CancellationToken ct = default);

    /// <summary>
    /// Persists <paramref name="mode"/> as the new active mode, stamping
    /// <paramref name="changedAtUtc"/> as the last-changed time (AC-07). Survives a
    /// restart. Upserts the single active-mode record.
    /// </summary>
    Task SetAsync(StripeMode mode, DateTimeOffset changedAtUtc, CancellationToken ct = default);
}

/// <summary>
/// The working in-memory <see cref="IActiveStripeModeStore"/> (billing-entitlements/06),
/// used when no storage connection string is configured (local dev / CI / a fresh
/// clone). Thread-safe; defaults to <see cref="StripeMode.Test"/> until set (AC-05).
/// Not durable across a process restart - that is what the Table Storage store is for.
/// </summary>
public sealed class InMemoryActiveStripeModeStore : IActiveStripeModeStore
{
    private readonly object _gate = new();
    private StripeModeState _state = new(StripeMode.Test, LastChangedUtc: null);

    /// <inheritdoc />
    public Task<StripeModeState> GetAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_state);
        }
    }

    /// <inheritdoc />
    public Task SetAsync(StripeMode mode, DateTimeOffset changedAtUtc, CancellationToken ct = default)
    {
        lock (_gate)
        {
            _state = new StripeModeState(mode, changedAtUtc);
        }
        return Task.CompletedTask;
    }
}
