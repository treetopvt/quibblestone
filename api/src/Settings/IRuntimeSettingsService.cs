// ----------------------------------------------------------------------------
//  IRuntimeSettingsService - the ONE front door every runtime-knob consumer uses
//  (control-plane/01, issue #197). Composes SettingsCatalog (code defaults) +
//  IRuntimeSettingsStore (persisted overrides), with the SAME short in-memory cache
//  precedent as ActiveStripeContext: the resolved override set is cached for a few
//  seconds so a hot read path does not take a storage round-trip per call, and a
//  PUT / DELETE resets the cache so the flipping node sees its own change immediately
//  (AC-02) and other nodes within the short TTL.
//
//  TYPED GETTERS (the consumer surface story 02 / 03 inject): GetBoolAsync /
//  GetIntAsync / GetDecimalAsync / GetStringAsync resolve override-or-default for a key
//  and return the typed value. A getter whose type does not match the key's declared
//  SettingType is a CODING BUG (throws) - the catalog is the single source of truth.
//
//  DEFAULT / DEGRADE POSTURE (AC-01 / AC-07): no override -> code default; an override
//  that fails to parse against its declared type -> code default (never a throw), the
//  same "a storage hiccup never crashes the app" stance as the Stripe-mode store.
//
//  GetAllAsync is the admin GET's shape: every catalog key with its type, description,
//  code default, current override (with its changed-by/at stamp, if any) and effective
//  value. SetOverrideAsync / DeleteOverrideAsync are the write path the controller calls
//  AFTER it has validated type / bounds / confirmation - the service owns the cache
//  write-through, not the validation.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Settings;

/// <summary>
/// The changed-by/at stamp on an override as the admin view carries it (control-plane/01 AC-03):
/// the typed override value plus who wrote it and when. Null on a key with no override. A display
/// convenience overwritten by the next PUT - NOT the audit trail (that is the action log, AC-09).
/// </summary>
/// <param name="Value">The override value, typed (bool / int / decimal / string).</param>
/// <param name="ChangedBy">The operator who last wrote the override.</param>
/// <param name="ChangedAtUtc">When the override was last written (UTC).</param>
public sealed record SettingOverrideStamp(object Value, string ChangedBy, DateTimeOffset ChangedAtUtc);

/// <summary>
/// One catalog key as the admin GET serializes it (control-plane/01, AC-01 / AC-03): the key,
/// its type and description, the code default, the effective value (override if present else the
/// default), the override stamp (null when never overridden), and the guard rails (bounds /
/// confirmation) so the console can render the right edit affordance.
/// </summary>
/// <param name="Key">The settings key.</param>
/// <param name="Type">The declared value shape.</param>
/// <param name="Description">A short operator-facing description.</param>
/// <param name="CodeDefault">The code default (typed).</param>
/// <param name="EffectiveValue">The value a consumer reads right now (override or default, typed).</param>
/// <param name="Override">The override stamp, or null when no override is stored.</param>
/// <param name="Bounds">The numeric bounds (null for Bool / String keys).</param>
/// <param name="RequiresConfirmation">Whether a PUT to this key needs an explicit confirm:true (AC-10).</param>
public sealed record RuntimeSettingView(
    string Key,
    SettingType Type,
    string Description,
    object CodeDefault,
    object EffectiveValue,
    SettingOverrideStamp? Override,
    SettingBounds? Bounds,
    bool RequiresConfirmation);

/// <summary>
/// Resolves runtime settings (control-plane/01): typed getters for consumers, the full view for
/// the admin GET, and the write path (override / clear) for the admin PUT / DELETE. Composes the
/// static catalog with the persisted override store behind a short cache. Registered as a
/// singleton so the cache is shared.
/// </summary>
public interface IRuntimeSettingsService
{
    /// <summary>Reads a <see cref="SettingType.Bool"/> key (override or code default). Throws if the key is unknown or not Bool.</summary>
    ValueTask<bool> GetBoolAsync(string key, CancellationToken ct = default);

    /// <summary>Reads a <see cref="SettingType.Int"/> key (override or code default). Throws if the key is unknown or not Int.</summary>
    ValueTask<int> GetIntAsync(string key, CancellationToken ct = default);

    /// <summary>Reads a <see cref="SettingType.Decimal"/> key (override or code default). Throws if the key is unknown or not Decimal.</summary>
    ValueTask<decimal> GetDecimalAsync(string key, CancellationToken ct = default);

    /// <summary>Reads a <see cref="SettingType.String"/> key (override or code default). Throws if the key is unknown or not String.</summary>
    ValueTask<string> GetStringAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// The full catalog with defaults + overrides + effective values (AC-01 / AC-03 / AC-04) -
    /// the shape the admin GET serializes directly. Every catalog key appears; a key with no
    /// override has a null <see cref="RuntimeSettingView.Override"/> stamp.
    /// </summary>
    Task<IReadOnlyList<RuntimeSettingView>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// The single view for one key, or null when the key is not in the catalog. Used by the
    /// controller to read the OLD effective value for the action-log note before it writes.
    /// </summary>
    Task<RuntimeSettingView?> GetViewAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Writes (upserts) the override for <paramref name="key"/> to <paramref name="wireValue"/>
    /// and resets the cache so the flip is visible immediately on this node (AC-02). The caller
    /// (the controller) has ALREADY validated type / bounds / confirmation - this is the
    /// persistence + cache step only. <paramref name="wireValue"/> is the already-parsed value's
    /// wire-string form.
    /// </summary>
    Task SetOverrideAsync(string key, string wireValue, string changedBy, DateTimeOffset changedAtUtc, CancellationToken ct = default);

    /// <summary>
    /// Clears the override for <paramref name="key"/>, reverting it to the code default (AC-04),
    /// and resets the cache. Returns true when there WAS an override to clear, false when the key
    /// already had none - so the controller can skip the action-log row on a no-op DELETE (AC-09).
    /// </summary>
    Task<bool> DeleteOverrideAsync(string key, string changedBy, DateTimeOffset changedAtUtc, CancellationToken ct = default);
}
