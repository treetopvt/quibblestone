// ----------------------------------------------------------------------------
//  SupportLinkedDeviceCounter - the NARROW, count-only linked-device seam the support
//  console (sysadmin-console/07, issue #243, AC-02) consumes for the "linked devices"
//  figure, so the AccountSupportController's constructor holds only a bare-int
//  reference - never IFamilyDeviceTokenStore, whose ListByAccountAsync returns whole
//  device rows with labels and last-seen timestamps (AC-08, the structural firewall).
//
//  WHY NARROW: AC-02's linked-device figure is a COUNT, nothing more - no device label,
//  no last-seen, no token, no identifier. Injecting IFamilyDeviceTokenStore (a
//  timestamp-bearing surface) into the support controller would hand it more than a
//  count and one "just read one more field" edit away from projecting device context to
//  an operator. So the controller injects this int-only contract; the row-bearing store
//  is reached ONLY inside the adapter, which counts the live (non-revoked) links and
//  returns an integer.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Accounts;

namespace QuibbleStone.Api.Admin;

/// <summary>
/// A count-only projection of a family's linked devices (sysadmin-console/07, AC-02): returns
/// the number of LIVE (non-revoked) device links for an account and NOTHING else - never a
/// device label, last-seen, or token. Deliberately narrower than
/// <see cref="IFamilyDeviceTokenStore"/> so the support controller cannot reach a device row
/// (AC-08). Dependency-tolerant: returns null when the backing store is not available (though
/// accounts-identity/09's store is always registered, so today this always resolves to a count).
/// </summary>
public interface ILinkedDeviceCounter
{
    /// <summary>
    /// The number of live (non-revoked) devices linked to the family <paramref name="accountId"/>
    /// (AC-02), or null when the count is unavailable. A bare integer - never a device identifier
    /// or name.
    /// </summary>
    /// <param name="accountId">The stable family account id (accounts-identity/05) - non-PII.</param>
    /// <param name="ct">Cancellation for the (possibly storage-bound) count.</param>
    Task<int?> CountForAccountAsync(Guid accountId, CancellationToken ct = default);
}

/// <summary>
/// The real <see cref="ILinkedDeviceCounter"/> over accounts-identity/09's merged
/// <see cref="IFamilyDeviceTokenStore"/> (sysadmin-console/07, AC-02). Lists the account's device
/// rows, counts the LIVE (non-revoked) ones, and returns only that integer - every device label /
/// timestamp / token stays inside this adapter (AC-08). A just-revoked device is excluded so the
/// figure reflects currently-active links.
/// </summary>
public sealed class LinkedDeviceCounter : ILinkedDeviceCounter
{
    private readonly IFamilyDeviceTokenStore _devices;

    /// <summary>Constructs the counter over the merged family-device-token store (accounts-identity/09).</summary>
    public LinkedDeviceCounter(IFamilyDeviceTokenStore devices) => _devices = devices;

    /// <inheritdoc />
    public async Task<int?> CountForAccountAsync(Guid accountId, CancellationToken ct = default)
    {
        // The store lists revoked rows too (so a just-revoked device still reads as handled on
        // the Account page); the support figure counts only the LIVE links. Every row field but
        // the count is discarded here - it never reaches the controller (AC-08).
        var devices = await _devices.ListByAccountAsync(accountId, ct);
        return devices.Count(d => !d.Revoked);
    }
}
