// ----------------------------------------------------------------------------
//  ISeatPresetStore - the storage contract for kid seat presets (accounts-identity/
//  08, issue #228). One preset store keyed by the family's stable AccountId
//  (accounts-identity/05's spine), holding the account-plane preset rows an adult
//  manages from the Account page.
//
//  Exactly TWO implementations, chosen once at startup by whether a storage
//  connection string is configured (see Program.cs), MIRRORING the account store's
//  config-presence split - and, like it, the "absent" half is a genuinely WORKING
//  in-memory store (not a no-op), because the presets manager + join-flow picker
//  must be exercisable end-to-end locally with ZERO Azure setup:
//    - TableStorageSeatPresetStore : Azure Table Storage, used when
//      Accounts:StorageConnectionString is present. One entity per preset,
//      PartitionKey = the AccountId, RowKey = the preset id.
//    - InMemorySeatPresetStore : the working fallback used with no connection
//      string (local dev / CI / a fresh clone).
//
//  KEYED BY AccountId, SCOPED TO THE OWNER (AC-01/AC-05): every method takes the
//  owning family AccountId so a preset is only ever created / listed / edited /
//  deleted under ONE account - an adult can never reach another family's presets.
//  A preset carries ONLY { Id, Nickname, Variant } (SeatPreset): no per-preset
//  history, gallery, entitlement, login, or PII (the kid-profile boundary). This
//  contract imports NOTHING from api/src/Rooms - presets live entirely on the
//  account plane, never crossing into gameplay (AC-03).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Accounts;

/// <summary>
/// Creates, lists, edits, and deletes kid seat presets under a family account
/// (accounts-identity/08), keyed by the stable <c>AccountId</c>. One implementation
/// writes to Azure Table Storage (deployed); the other is a working in-memory store
/// used when no storage connection string is configured. Every preset holds only
/// { Id, Nickname, Variant } and no room / player reference, so the store stays
/// entirely on the account plane (AC-03/AC-05).
/// </summary>
public interface ISeatPresetStore
{
    /// <summary>
    /// List every seat preset stored under <paramref name="accountId"/>, in a stable
    /// order (creation order). Returns an empty list when the account has none - a
    /// brand-new family account starts with zero presets (never an error).
    /// </summary>
    /// <param name="accountId">The owning family account id (accounts-identity/05).</param>
    /// <param name="ct">Cancellation for the (storage-bound) list.</param>
    Task<IReadOnlyList<SeatPreset>> ListAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>
    /// Create a new preset under <paramref name="accountId"/> holding the given
    /// (already validated + normalized) nickname and Guardian variant. Mints a fresh
    /// stable preset id and returns the stored record. The CALLER (AccountsController)
    /// is responsible for length-capping, safety-filtering, and variant-normalizing
    /// the inputs BEFORE calling this - the store persists exactly what it is given.
    /// </summary>
    /// <param name="accountId">The owning family account id.</param>
    /// <param name="nickname">The vetted nickname (already trimmed, capped, safety-passed).</param>
    /// <param name="variant">The normalized Guardian variant (one of the six known values).</param>
    /// <param name="ct">Cancellation for the (storage-bound) create.</param>
    Task<SeatPreset> CreateAsync(Guid accountId, string nickname, string variant, CancellationToken ct = default);

    /// <summary>
    /// Update the preset <paramref name="presetId"/> under <paramref name="accountId"/>
    /// to the given (already validated + normalized) nickname and variant. Returns the
    /// updated record, or null when no preset with that id exists under that account
    /// (a stale / cross-account id) - the caller maps null to a 404 and never mints a
    /// row here.
    /// </summary>
    /// <param name="accountId">The owning family account id (scopes the edit to this family).</param>
    /// <param name="presetId">The preset to update.</param>
    /// <param name="nickname">The vetted nickname (already trimmed, capped, safety-passed).</param>
    /// <param name="variant">The normalized Guardian variant.</param>
    /// <param name="ct">Cancellation for the (storage-bound) update.</param>
    Task<SeatPreset?> UpdateAsync(Guid accountId, Guid presetId, string nickname, string variant, CancellationToken ct = default);

    /// <summary>
    /// Delete the preset <paramref name="presetId"/> under <paramref name="accountId"/>.
    /// Returns true when a preset was removed, false when none existed under that
    /// account (already gone / cross-account) - an idempotent delete, never throwing
    /// on a miss.
    /// </summary>
    /// <param name="accountId">The owning family account id (scopes the delete to this family).</param>
    /// <param name="presetId">The preset to delete.</param>
    /// <param name="ct">Cancellation for the (storage-bound) delete.</param>
    Task<bool> DeleteAsync(Guid accountId, Guid presetId, CancellationToken ct = default);
}
