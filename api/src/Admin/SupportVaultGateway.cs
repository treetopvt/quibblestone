// ----------------------------------------------------------------------------
//  SupportVaultGateway - the NARROW, count-only / restore-only vault seams the
//  support console (sysadmin-console/07, issue #243) consumes, so the
//  AccountSupportController's constructor holds NO reference whose return type can
//  carry a byline or a per-tale list (AC-08, the structural cross-plane firewall).
//
//  WHY NARROW CONTRACTS, NOT IVaultStore (AC-08's "narrow the contract, don't just
//  avoid calling the wide one"): the keepsake vault store (IVaultStore) is a
//  byline-bearing surface - its ListAsync returns whole VaultTale rows (each with a
//  byline nickname), and RestoreAsync returns a VaultTale too. If the support
//  controller injected IVaultStore - even to read only a .Count today - a future
//  maintainer adding "just one more field" could leak who-authored-what to an
//  operator, exactly the bridge ADR 0003's firewall forbids. So the controller
//  injects these two count/enum-only contracts instead; the byline-bearing IVaultStore
//  is reached ONLY inside the adapters here, which discard every content field and
//  return an integer or a plain enum. The controller structurally CANNOT reach a
//  byline.
//
//  TWO SEAMS:
//    - IVaultAccountSummary.CountForAccountAsync(accountId) -> int? : the aggregate
//      vault/tale COUNT for an account (AC-02). DEPENDENCY-TOLERANT: keepsake-vault
//      does not yet expose an account -> vaults index (the vault is keyed by an
//      anonymous, device-held vault id, and a claim records accountId ON the vault,
//      but there is no reverse lookup from an account to its vaults). Until keepsake-
//      vault ships that projection, the registered implementation is the
//      UnavailableVaultAccountSummary sentinel, which returns null (the section reports
//      "not available yet", never an error or a fabricated zero) - mirroring
//      ReportedTalesController's disabled-fallback posture. When keepsake-vault exposes
//      a real count-by-account, the DI registration swaps with NO controller change.
//    - IVaultTaleRestorer.RestoreAsync(vaultId, taleId) -> VaultTaleRestoreOutcome :
//      the RESTORE of a user's OWN accidentally-deleted keepsake (AC-05), via keepsake-
//      vault/04's merged self-delete/restore seam (IVaultStore.RestoreAsync). This IS
//      wired to the real store (that seam exists), but the adapter DISCARDS the
//      returned VaultTale and hands back only a plain outcome enum, so the byline never
//      leaves this file. The vault id + tale id are DIRECT content-plane inputs to this
//      verb (a slug/handle acted on as content, AC-04/AC-05), NEVER a search key that
//      resolves back to an account - there is no account lookup on this path at all.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Vault;

namespace QuibbleStone.Api.Admin;

/// <summary>
/// A count-only projection of an account's keepsake vault (sysadmin-console/07, AC-02):
/// returns the aggregate tale COUNT for a family account and NOTHING else - never a tale,
/// a byline, a timestamp, or a per-tale list. Deliberately narrower than
/// <see cref="IVaultStore"/> so the support controller's constructor cannot reach a
/// byline (AC-08). Dependency-tolerant: an implementation returns null when the backing
/// count-by-account projection is not available yet, and the section renders a plain
/// "not available yet" placeholder.
/// </summary>
public interface IVaultAccountSummary
{
    /// <summary>
    /// The number of live keepsake tales the family <paramref name="accountId"/> holds in
    /// the vault (AC-02), or null when keepsake-vault does not yet expose an account-scoped
    /// count (the dependency-tolerant "unavailable" state). A bare integer - never a tale
    /// body, byline, or timestamp.
    /// </summary>
    /// <param name="accountId">The stable family account id (accounts-identity/05) - non-PII.</param>
    /// <param name="ct">Cancellation for a (possibly storage-bound) count.</param>
    /// <returns>The tale count, or null when the projection is unavailable.</returns>
    Task<int?> CountForAccountAsync(Guid accountId, CancellationToken ct = default);
}

/// <summary>
/// The dependency-tolerant sentinel <see cref="IVaultAccountSummary"/> (sysadmin-console/07,
/// AC-02): keepsake-vault does not yet expose an account -> vaults index (the vault is keyed
/// by an anonymous device-held vault id, so there is no reverse lookup from an account to its
/// tales), so this always reports "unavailable" (null). Registered by default; when keepsake-
/// vault ships a real count-by-account projection, the Program.cs registration swaps to it with
/// NO controller change. This is the SAME "feature simply off" posture the published-tale
/// disabled store and the reported-tales disabled fallback take, NOT an error path.
/// </summary>
public sealed class UnavailableVaultAccountSummary : IVaultAccountSummary
{
    /// <inheritdoc />
    public Task<int?> CountForAccountAsync(Guid accountId, CancellationToken ct = default) =>
        Task.FromResult<int?>(null);
}

/// <summary>The outcome of a support-triggered keepsake restore (sysadmin-console/07, AC-05) - a plain enum, never a tale.</summary>
public enum VaultTaleRestoreOutcome
{
    /// <summary>The soft-deleted tale was restored within its window and resumes normal serving.</summary>
    Restored,

    /// <summary>No restorable tale was found for that vault id + tale id (unknown, TTL-expired, or past its restore window).</summary>
    NotFound,
}

/// <summary>
/// A restore-only seam over keepsake-vault/04's merged self-delete/restore path
/// (sysadmin-console/07, AC-05): restores a user's OWN accidentally-deleted keepsake by
/// its DIRECT (vaultId, taleId) content identifiers - never by an account lookup. Returns
/// only a <see cref="VaultTaleRestoreOutcome"/>, so the byline-bearing <see cref="VaultTale"/>
/// the underlying store hands back never crosses into the controller (AC-08).
/// </summary>
public interface IVaultTaleRestorer
{
    /// <summary>
    /// Restores the soft-deleted keepsake identified by <paramref name="vaultId"/> +
    /// <paramref name="taleId"/> within its restore window (AC-05), through keepsake-vault/04's
    /// <see cref="IVaultStore.RestoreAsync"/>. Returns <see cref="VaultTaleRestoreOutcome.Restored"/>
    /// on success or <see cref="VaultTaleRestoreOutcome.NotFound"/> when nothing restorable exists.
    /// This is a DISTINCT, lower-friction verb from the Content tab's moderation-takedown restore
    /// (which carries its own required confirmation marker) - the two are never merged.
    /// </summary>
    /// <param name="vaultId">The owning vault id (a device-held bearer handle) - a DIRECT content input, never a search key.</param>
    /// <param name="taleId">The tale id to restore within the vault.</param>
    /// <param name="ct">Cancellation for the (possibly storage-bound) restore.</param>
    Task<VaultTaleRestoreOutcome> RestoreAsync(string vaultId, string taleId, CancellationToken ct = default);
}

/// <summary>
/// The real <see cref="IVaultTaleRestorer"/> over keepsake-vault/04's merged
/// <see cref="IVaultStore.RestoreAsync"/> (sysadmin-console/07, AC-05). It calls the store,
/// then DISCARDS the returned <see cref="VaultTale"/> (byline and all) and maps null -> NotFound,
/// non-null -> Restored, so the support controller only ever sees the outcome enum. The
/// byline-bearing store reference lives HERE, never in the controller (AC-08).
/// </summary>
public sealed class VaultTaleRestorer : IVaultTaleRestorer
{
    private readonly IVaultStore _vault;

    /// <summary>Constructs the restorer over the merged keepsake-vault store (keepsake-vault/04).</summary>
    public VaultTaleRestorer(IVaultStore vault) => _vault = vault;

    /// <inheritdoc />
    public async Task<VaultTaleRestoreOutcome> RestoreAsync(string vaultId, string taleId, CancellationToken ct = default)
    {
        // The store returns the restored VaultTale on success or null when nothing
        // restorable exists. Map to a byline-free outcome and drop the tale entirely -
        // its content never leaves this adapter (AC-08).
        var restored = await _vault.RestoreAsync(vaultId, taleId, ct);
        return restored is null ? VaultTaleRestoreOutcome.NotFound : VaultTaleRestoreOutcome.Restored;
    }
}
