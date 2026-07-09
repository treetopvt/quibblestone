// ----------------------------------------------------------------------------
//  IRuntimeSettingsStore - the persistence seam for runtime settings OVERRIDES
//  (control-plane/01, issue #197). Generalizes IActiveStripeModeStore's single-fixed-
//  row shape into ONE ROW PER KEY: an override is a stored value + a changed-by/at
//  stamp for exactly the keys an operator has retuned. A key with no row reads as the
//  code default (the catalog owns defaults, not the store).
//
//  Mirrors the IActiveStripeModeStore / IEntitlementGrantStore trio EXACTLY:
//    - TableStorageRuntimeSettingsStore : the real store, used when a storage
//      connection string is configured (reuses the SAME Entitlements storage account -
//      NO new resource). One RowKey per settings key, so a read is a point lookup.
//    - InMemoryRuntimeSettingsStore : a working thread-safe store used when no
//      connection string is configured (local dev / CI / a fresh clone), so every AC
//      is exercisable with ZERO Azure setup (AC-05) - a working store, not a no-op.
//
//  The store never knows about types or bounds - it round-trips a wire STRING keyed by
//  the settings key. The service (RuntimeSettingsService) is the one that resolves a
//  stored string against the catalog's declared type, degrading to the code default on
//  a missing row or an unparseable value (AC-07).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Settings;

/// <summary>
/// One stored settings override (control-plane/01): the key, its wire-string value, and the
/// operator / timestamp stamp (AC-03). The stamp is a display convenience overwritten by the
/// next PUT - NOT the audit trail (that is the append-only operator action log, AC-09). A key
/// with no override has no row and no stamp.
/// </summary>
/// <param name="Key">The settings key (matches a <see cref="SettingDefinition.Key"/>).</param>
/// <param name="Value">The override value in its wire-string form (parsed against the catalog type on read).</param>
/// <param name="ChangedBy">The operator identity that last wrote this override (from the operator session credential).</param>
/// <param name="ChangedAtUtc">When the override was last written (UTC).</param>
public sealed record SettingOverride(string Key, string Value, string ChangedBy, DateTimeOffset ChangedAtUtc);

/// <summary>
/// Reads and writes runtime settings OVERRIDES (control-plane/01). One implementation persists
/// to Azure Table Storage (deployed); the other is a working in-memory store used when no storage
/// connection string is configured. Only keys an operator has retuned have a row; everything else
/// resolves to the code default at the service layer.
/// </summary>
public interface IRuntimeSettingsStore
{
    /// <summary>
    /// Reads every stored override (the keys an operator has retuned). Never null; an empty list
    /// means no override has ever been written (every key resolves to its code default). Used by
    /// the service to build the full resolved set for its short read cache.
    /// </summary>
    Task<IReadOnlyList<SettingOverride>> GetAllOverridesAsync(CancellationToken ct = default);

    /// <summary>
    /// Reads the single override for <paramref name="key"/>, or null when none is stored (the key
    /// resolves to its code default). A point lookup (RowKey = the key). A missing row / table is
    /// null, never an error (AC-07).
    /// </summary>
    Task<SettingOverride?> GetOverrideAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Upserts the override for <paramref name="key"/> to <paramref name="value"/> (wire-string form),
    /// stamping <paramref name="changedBy"/> / <paramref name="changedAtUtc"/> (AC-03). Survives a
    /// restart (Table Storage). Overwrites any existing override for the key (AC-02).
    /// </summary>
    Task SetOverrideAsync(string key, string value, string changedBy, DateTimeOffset changedAtUtc, CancellationToken ct = default);

    /// <summary>
    /// Deletes the override for <paramref name="key"/>, reverting it to the code default (AC-04).
    /// A delete against a key with no row is a harmless no-op. <paramref name="changedBy"/> /
    /// <paramref name="changedAtUtc"/> are part of the seam's symmetry (a future soft-delete /
    /// history could record who cleared it); the current stores drop the row, so they do not
    /// persist them - the append-only action log (AC-09) is what records the clear.
    /// </summary>
    Task DeleteOverrideAsync(string key, string changedBy, DateTimeOffset changedAtUtc, CancellationToken ct = default);
}

/// <summary>
/// The working in-memory <see cref="IRuntimeSettingsStore"/> (control-plane/01), used when no
/// storage connection string is configured (local dev / CI / a fresh clone). Thread-safe; every
/// AC holds against it - only durability across a process restart is lost (AC-05). Not a no-op.
/// </summary>
public sealed class InMemoryRuntimeSettingsStore : IRuntimeSettingsStore
{
    // One entry per overridden key. Ordinal keys (catalog constants, not user text).
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SettingOverride> _overrides =
        new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<IReadOnlyList<SettingOverride>> GetAllOverridesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SettingOverride>>(_overrides.Values.ToList());

    /// <inheritdoc />
    public Task<SettingOverride?> GetOverrideAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(_overrides.TryGetValue(key, out var value) ? value : null);

    /// <inheritdoc />
    public Task SetOverrideAsync(string key, string value, string changedBy, DateTimeOffset changedAtUtc, CancellationToken ct = default)
    {
        _overrides[key] = new SettingOverride(key, value, changedBy, changedAtUtc);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteOverrideAsync(string key, string changedBy, DateTimeOffset changedAtUtc, CancellationToken ct = default)
    {
        _overrides.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
