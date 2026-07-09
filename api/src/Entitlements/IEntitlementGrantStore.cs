// ----------------------------------------------------------------------------
//  IEntitlementGrantStore - the storage contract for purchaser entitlement grants
//  (billing-entitlements/01, issue #70, AC-05).
//
//  Mirrors accounts-identity/02's IAccountStore posture EXACTLY - it is the same
//  shape of problem (a small per-account record set in Azure Table Storage):
//    - TableStorageEntitlementGrantStore : the real Azure Table Storage impl, used
//      when Entitlements:StorageConnectionString is configured (a deployed
//      environment). ALL of an account's grants live in ONE partition (the stable
//      AccountId), so the session-creation read is a single partition query, never
//      a scan (AC-05).
//    - InMemoryEntitlementGrantStore : a genuinely WORKING thread-safe store used
//      when NO connection string is configured (local dev / CI / a fresh clone),
//      so stories 03-05 and the session-creation gate are exercisable end to end
//      with ZERO Azure setup. Same key scheme, same semantics - only durability
//      differs.
//
//  KEYED BY THE STABLE AccountId, NOT A HASH OF EMAIL (accounts-identity/05, #195,
//  ADR 0003 Layer 0): the partition key is the account's durable Guid id (Account.Id),
//  passed by the caller after it resolves the account. The pre-ADR-0003 code keyed
//  these off AccountIdentity.KeyFor(email) - a SHA-256 of the (mutable) email - which
//  meant an email change orphaned a purchaser's own grants. An AccountId is already a
//  random, unguessable GUID, so it is the partition key DIRECTLY - no further hashing
//  needed (unlike a raw email). Everything downstream (control-plane, keepsake-vault)
//  keys off this same AccountId.
//
//  ONE ROW PER (account, capability): grants upsert by capability key, because a
//  subscription renewal EXTENDS an existing capability's lease rather than piling
//  up rows (story 03's invoice.paid). That keeps GetGrantsAsync returning at most
//  one lease per capability and makes "extend the lease" a plain PutGrantAsync.
//
//  CONSUMABLE WITHOUT ROOMS (AC-04): this contract references only EntitlementGrant
//  (capability key + lease + source) and an AccountId. It imports NOTHING from
//  api/src/Rooms, so the session-creation gate reads it without ever touching player
//  / room data.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Entitlements;

/// <summary>
/// Reads and writes account <see cref="EntitlementGrant"/> leases (billing-
/// entitlements/01), keyed by the stable <c>AccountId</c> (accounts-identity/05).
/// One implementation writes to Azure Table Storage (deployed); the other is a
/// working in-memory store used when no storage connection string is configured
/// (local dev / CI). Holds no PII beyond what a grant carries and no room / player
/// reference (AC-03/AC-04).
/// </summary>
public interface IEntitlementGrantStore
{
    /// <summary>
    /// Returns every grant held by the account <paramref name="accountId"/> as a
    /// SINGLE partition read (AC-05). An account with nothing returns an empty list -
    /// never null, and never creates anything (a read is a read). The caller
    /// (<see cref="StoredValueEntitlementService"/>) decides which leases are active.
    /// </summary>
    /// <param name="accountId">The account's stable id (Account.Id) - the partition key directly (no hashing; a GUID is already unguessable).</param>
    /// <param name="ct">Cancellation for the (storage-bound) read.</param>
    /// <returns>The account's grants (possibly empty), at most one per capability key.</returns>
    Task<IReadOnlyList<EntitlementGrant>> GetGrantsAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>
    /// Writes (UPSERTS) a grant for the account <paramref name="accountId"/>, keyed by
    /// the grant's capability key - so re-granting the same capability EXTENDS /
    /// replaces its lease rather than adding a duplicate row (story 03's renewal path).
    /// This is the write seam stories 03-04 (and the operator grant/revoke) call.
    /// </summary>
    /// <param name="accountId">
    /// The account's stable id (Account.Id). CONTRACT (accounts-identity/05): resolve
    /// the account FIRST (via IAccountStore), then key the grant off <c>account.Id</c>
    /// - the SAME id the session-creation read uses. Because the id never changes (an
    /// email change does not move it, AC-02), a write and the read always land in the
    /// same partition; keying a grant off any other value would silently make it
    /// unreadable at session-creation.
    /// </param>
    /// <param name="grant">The lease to persist (capability key, validThrough, source).</param>
    /// <param name="ct">Cancellation for the (storage-bound) write.</param>
    Task PutGrantAsync(Guid accountId, EntitlementGrant grant, CancellationToken ct = default);
}
