// ----------------------------------------------------------------------------
//  IEntitlementGrantStore - the storage contract for purchaser entitlement grants
//  (billing-entitlements/01, issue #70, AC-05).
//
//  Mirrors accounts-identity/02's IAccountStore posture EXACTLY - it is the same
//  shape of problem (a small per-purchaser record set in Azure Table Storage,
//  keyed by a hash of the purchaser identity so the raw email is never the key and
//  the key is never guessable):
//    - TableStorageEntitlementGrantStore : the real Azure Table Storage impl, used
//      when Entitlements:StorageConnectionString is configured (a deployed
//      environment). ALL of a purchaser's grants live in ONE partition (the
//      identity hash), so the session-creation read is a single partition query,
//      never a scan (AC-05).
//    - InMemoryEntitlementGrantStore : a genuinely WORKING thread-safe store used
//      when NO connection string is configured (local dev / CI / a fresh clone),
//      so stories 03-05 and the session-creation gate are exercisable end to end
//      with ZERO Azure setup. Same key scheme, same semantics - only durability
//      differs.
//
//  ONE ROW PER (purchaser, capability): grants upsert by capability key, because a
//  subscription renewal EXTENDS an existing capability's lease rather than piling
//  up rows (story 03's invoice.paid). That keeps GetGrantsAsync returning at most
//  one lease per capability and makes "extend the lease" a plain PutGrantAsync.
//
//  CONSUMABLE WITHOUT ROOMS (AC-04): this contract references only EntitlementGrant
//  (capability key + lease + source) and a purchaser identity string. It imports
//  NOTHING from api/src/Rooms, so the session-creation gate reads it without ever
//  touching player / room data.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Entitlements;

/// <summary>
/// Reads and writes purchaser <see cref="EntitlementGrant"/> leases (billing-
/// entitlements/01), keyed by the normalized purchaser identity. One implementation
/// writes to Azure Table Storage (deployed); the other is a working in-memory store
/// used when no storage connection string is configured (local dev / CI). Holds no
/// PII beyond what a grant carries and no room / player reference (AC-03/AC-04).
/// </summary>
public interface IEntitlementGrantStore
{
    /// <summary>
    /// Returns every grant held by <paramref name="purchaserIdentity"/> as a SINGLE
    /// partition read (AC-05). A purchaser with nothing returns an empty list - never
    /// null, and never creates anything (a read is a read). The caller
    /// (<see cref="StoredValueEntitlementService"/>) decides which leases are active.
    /// </summary>
    /// <param name="purchaserIdentity">The purchaser's email identity (normalized + hashed internally for the partition key).</param>
    /// <param name="ct">Cancellation for the (storage-bound) read.</param>
    /// <returns>The purchaser's grants (possibly empty), at most one per capability key.</returns>
    Task<IReadOnlyList<EntitlementGrant>> GetGrantsAsync(string purchaserIdentity, CancellationToken ct = default);

    /// <summary>
    /// Writes (UPSERTS) a grant for <paramref name="purchaserIdentity"/>, keyed by the
    /// grant's capability key - so re-granting the same capability EXTENDS / replaces
    /// its lease rather than adding a duplicate row (story 03's renewal path). This is
    /// the write seam stories 03-04 (and the future operator grant/revoke) call; story
    /// 01 does not itself grant anything.
    /// </summary>
    /// <param name="purchaserIdentity">
    /// The purchaser's email identity (normalized + hashed internally for the
    /// partition key). CONTRACT: pass the SAME identity the read side resolves - i.e.
    /// the purchaser's canonical <c>Account.Email</c> (resolve/create the account
    /// first, then key the grant off <c>account.Email</c>). Both paths funnel through
    /// <c>AccountIdentity.KeyFor</c>, so a write and the session-creation read always
    /// land in the same partition; keying a grant off a different identity field would
    /// silently make it unreadable at session-creation.
    /// </param>
    /// <param name="grant">The lease to persist (capability key, validThrough, source).</param>
    /// <param name="ct">Cancellation for the (storage-bound) write.</param>
    Task PutGrantAsync(string purchaserIdentity, EntitlementGrant grant, CancellationToken ct = default);
}
