// ----------------------------------------------------------------------------
//  RuntimeSettingsService - the default IRuntimeSettingsService (control-plane/01,
//  issue #197). Composes SettingsCatalog (defaults) + IRuntimeSettingsStore (overrides)
//  behind a short in-memory cache, mirroring ActiveStripeContext's cache precedent.
//
//  THE CACHE (AC-02): the full resolved override set is cached for a few seconds so a
//  hot read (a typed getter) avoids a storage round-trip per call. A PUT / DELETE
//  RESETS the cache (write-through invalidation), so the flipping node reflects its own
//  change immediately and other nodes pick it up within the short TTL - no app restart.
//  Seconds, not minutes, exactly like ActiveStripeContext.CacheTtl.
//
//  RESOLUTION (AC-01 / AC-07): a key resolves to its override value when one is stored
//  AND it parses against the declared type; otherwise it resolves to the code default.
//  An unknown key or a type-mismatched getter is a coding bug (throws) - the catalog is
//  the single source of truth, not a runtime branch.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Settings;

/// <summary>
/// The default <see cref="IRuntimeSettingsService"/> (control-plane/01): resolves catalog defaults
/// against persisted overrides, caching the resolved override set for a few seconds and resetting
/// the cache on every write. A singleton over the store so the cache is shared.
/// </summary>
public sealed class RuntimeSettingsService : IRuntimeSettingsService
{
    // A short cache so the hot read paths avoid a storage read per call without noticeably
    // delaying an operator's flip (AC-02). Seconds, not minutes - mirrors ActiveStripeContext.
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(5);

    private readonly IRuntimeSettingsStore _store;

    private readonly object _gate = new();
    private Dictionary<string, SettingOverride>? _cache;
    private DateTime _cachedAtUtc;

    /// <summary>Constructs the service over the override store. Defaults come from the static catalog.</summary>
    public RuntimeSettingsService(IRuntimeSettingsStore store)
    {
        _store = store;
    }

    /// <inheritdoc />
    public async ValueTask<bool> GetBoolAsync(string key, CancellationToken ct = default) =>
        (bool)await ResolveTypedAsync(key, SettingType.Bool, ct);

    /// <inheritdoc />
    public async ValueTask<int> GetIntAsync(string key, CancellationToken ct = default) =>
        (int)await ResolveTypedAsync(key, SettingType.Int, ct);

    /// <inheritdoc />
    public async ValueTask<decimal> GetDecimalAsync(string key, CancellationToken ct = default) =>
        (decimal)await ResolveTypedAsync(key, SettingType.Decimal, ct);

    /// <inheritdoc />
    public async ValueTask<string> GetStringAsync(string key, CancellationToken ct = default) =>
        (string)await ResolveTypedAsync(key, SettingType.String, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<RuntimeSettingView>> GetAllAsync(CancellationToken ct = default)
    {
        var overrides = await GetOverridesAsync(ct);
        return SettingsCatalog.All.Select(def => BuildView(def, Lookup(overrides, def.Key))).ToList();
    }

    /// <inheritdoc />
    public async Task<RuntimeSettingView?> GetViewAsync(string key, CancellationToken ct = default)
    {
        var def = SettingsCatalog.TryGet(key);
        if (def is null)
        {
            return null;
        }

        var overrides = await GetOverridesAsync(ct);
        return BuildView(def, Lookup(overrides, key));
    }

    /// <inheritdoc />
    public async Task SetOverrideAsync(string key, string wireValue, string changedBy, DateTimeOffset changedAtUtc, CancellationToken ct = default)
    {
        await _store.SetOverrideAsync(key, wireValue, changedBy, changedAtUtc, ct);
        InvalidateCache();
    }

    /// <inheritdoc />
    public async Task<bool> DeleteOverrideAsync(string key, string changedBy, DateTimeOffset changedAtUtc, CancellationToken ct = default)
    {
        // A DELETE against a key with no override is a no-op (no write, and the caller skips the
        // action-log row) - so check for an existing override first (AC-04 / AC-09).
        var existing = await _store.GetOverrideAsync(key, ct);
        if (existing is null)
        {
            return false;
        }

        await _store.DeleteOverrideAsync(key, changedBy, changedAtUtc, ct);
        InvalidateCache();
        return true;
    }

    // Resolves a key to its typed value, asserting the getter matches the declared type. Throws
    // on an unknown key or a type mismatch (a coding bug - the catalog is authoritative), never
    // on a missing / unparseable override (that degrades to the code default, AC-07).
    private async ValueTask<object> ResolveTypedAsync(string key, SettingType expected, CancellationToken ct)
    {
        var def = SettingsCatalog.TryGet(key)
            ?? throw new InvalidOperationException($"Unknown settings key '{key}' - not in the catalog.");
        if (def.Type != expected)
        {
            throw new InvalidOperationException(
                $"Settings key '{key}' is declared {def.Type}, not {expected} - use the matching typed getter.");
        }

        var overrides = await GetOverridesAsync(ct);
        return Resolve(def, Lookup(overrides, key));
    }

    // The effective typed value: the override when present AND parseable, else the code default.
    private static object Resolve(SettingDefinition def, SettingOverride? over)
    {
        if (over is not null && SettingValue.TryParse(def.Type, over.Value, out var parsed))
        {
            return parsed;
        }

        return def.CodeDefault;
    }

    // Builds the admin view for one key. The override stamp is present only when a stored override
    // parses (an unparseable / drifted row degrades to the default with NO stamp, AC-07) so the
    // view never surfaces a value it cannot type.
    private static RuntimeSettingView BuildView(SettingDefinition def, SettingOverride? over)
    {
        var effective = Resolve(def, over);
        SettingOverrideStamp? stamp = null;
        if (over is not null && SettingValue.TryParse(def.Type, over.Value, out var parsed))
        {
            stamp = new SettingOverrideStamp(parsed, over.ChangedBy, over.ChangedAtUtc);
        }

        return new RuntimeSettingView(
            def.Key, def.Type, def.Description, def.CodeDefault, effective, stamp, def.Bounds, def.RequiresConfirmation);
    }

    private static SettingOverride? Lookup(IReadOnlyDictionary<string, SettingOverride> overrides, string key) =>
        overrides.TryGetValue(key, out var over) ? over : null;

    // Returns the cached resolved override set, reloading from the store when the cache is stale
    // or was invalidated by a write. Load happens outside the lock; only the swap is guarded.
    private async ValueTask<IReadOnlyDictionary<string, SettingOverride>> GetOverridesAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            if (_cache is not null && DateTime.UtcNow - _cachedAtUtc < CacheTtl)
            {
                return _cache;
            }
        }

        var all = await _store.GetAllOverridesAsync(ct);
        var dict = all.ToDictionary(o => o.Key, StringComparer.Ordinal);
        lock (_gate)
        {
            _cache = dict;
            _cachedAtUtc = DateTime.UtcNow;
        }

        return dict;
    }

    // Drop the cache so the next read reloads from the store. Called after every write so the
    // flipping node sees its own change immediately (AC-02), the same write-through posture as
    // ActiveStripeContext.SetModeAsync.
    private void InvalidateCache()
    {
        lock (_gate)
        {
            _cache = null;
        }
    }
}
