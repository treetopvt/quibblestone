// ----------------------------------------------------------------------------
//  InMemorySeatPresetStore - the WORKING fallback seat-preset store used when NO
//  storage connection string is configured (accounts-identity/08, local dev / CI /
//  a fresh clone).
//
//  Like InMemoryAccountStore (and UNLIKE a disabled no-op), this is a genuinely
//  functional store: the presets manager on the Account page and the join-flow
//  picker must be exercisable end to end on a laptop with zero Azure, so this
//  creates, lists, edits, and deletes presets - just in process memory instead of
//  Azure Table Storage. The moment Accounts:StorageConnectionString is present,
//  Program.cs registers TableStorageSeatPresetStore instead and presets persist
//  across restarts; the semantics of BOTH stores are identical, only durability
//  differs.
//
//  It holds presets partitioned by the owning family AccountId (AC-01/AC-05): an
//  outer map AccountId -> (presetId -> SeatPreset). Every method is scoped to one
//  account, so an adult can only ever reach their own family's presets. A preset
//  carries ONLY { Id, Nickname, Variant } - no history, gallery, entitlement, or
//  PII (the kid-profile boundary), and no room / player reference (AC-03).
//  ConcurrentDictionaries make reads lock-free; the per-account inner map is created
//  atomically via GetOrAdd, and creation order is preserved by an insertion counter
//  so List returns a stable order.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Collections.Concurrent;

namespace QuibbleStone.Api.Accounts;

/// <summary>
/// A thread-safe, in-memory <see cref="ISeatPresetStore"/> (accounts-identity/08),
/// registered when no storage connection string is configured. Fully functional
/// (list / create / update / delete, all scoped to the owning AccountId) so the
/// presets manager + join picker are testable with zero Azure setup - it just does
/// not survive a process restart. Persists only { Id, Nickname, Variant } per preset.
/// </summary>
public sealed class InMemorySeatPresetStore : ISeatPresetStore
{
    // One inner map per family account: AccountId -> (presetId -> stored preset).
    // Every method resolves the inner map for the given account first, so a preset
    // is only ever reachable under the account it was created in.
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, StoredPreset>> _byAccount = new();

    // A monotonically-increasing sequence stamped on each stored preset, so List can
    // return presets in a STABLE creation order (a plain dictionary enumeration order
    // is not guaranteed). Interlocked keeps the stamp allocation race-free.
    private long _sequence;

    // The stored preset plus its creation-order stamp (the stamp is a store-internal
    // ordering detail, never part of the SeatPreset domain record the caller sees).
    private sealed record StoredPreset(SeatPreset Preset, long Order);

    /// <inheritdoc />
    public Task<IReadOnlyList<SeatPreset>> ListAsync(Guid accountId, CancellationToken ct = default)
    {
        if (!_byAccount.TryGetValue(accountId, out var presets))
        {
            return Task.FromResult<IReadOnlyList<SeatPreset>>([]);
        }

        // Snapshot + order by the creation stamp so the manager UI lists presets in a
        // stable, predictable order across reads.
        var ordered = presets.Values
            .OrderBy(stored => stored.Order)
            .Select(stored => stored.Preset)
            .ToList();
        return Task.FromResult<IReadOnlyList<SeatPreset>>(ordered);
    }

    /// <inheritdoc />
    public Task<SeatPreset> CreateAsync(Guid accountId, string nickname, string variant, CancellationToken ct = default)
    {
        var preset = new SeatPreset(Guid.NewGuid(), nickname, variant);
        var stored = new StoredPreset(preset, Interlocked.Increment(ref _sequence));
        var presets = _byAccount.GetOrAdd(accountId, _ => new ConcurrentDictionary<Guid, StoredPreset>());
        presets[preset.Id] = stored;
        return Task.FromResult(preset);
    }

    /// <inheritdoc />
    public Task<SeatPreset?> UpdateAsync(Guid accountId, Guid presetId, string nickname, string variant, CancellationToken ct = default)
    {
        // Scoped to the owning account: a preset id that belongs to another family (or
        // is stale) simply misses here and maps to a 404 at the caller - never a create.
        if (!_byAccount.TryGetValue(accountId, out var presets) ||
            !presets.TryGetValue(presetId, out var existing))
        {
            return Task.FromResult<SeatPreset?>(null);
        }

        // Preserve the id AND the original creation order (an edit keeps the preset in
        // its place in the list), replacing only the nickname + variant.
        var updated = existing.Preset with { Nickname = nickname, Variant = variant };
        presets[presetId] = existing with { Preset = updated };
        return Task.FromResult<SeatPreset?>(updated);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(Guid accountId, Guid presetId, CancellationToken ct = default)
    {
        if (!_byAccount.TryGetValue(accountId, out var presets))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(presets.TryRemove(presetId, out _));
    }
}
