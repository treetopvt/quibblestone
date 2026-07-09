// ----------------------------------------------------------------------------
//  InMemoryVaultStore - the WORKING fallback keepsake-vault store used when NO
//  storage connection string is configured (keepsake-vault/01, local dev / CI /
//  a fresh clone).
//
//  This is DELIBERATELY NOT a no-op (unlike DisabledPublishedTaleStore, LIKE
//  InMemoryCloudGalleryStore). The vault is default-on for EVERY anonymous player,
//  so the whole save -> list -> claim -> recover flow this and later stories build
//  on must be exercisable END TO END on a laptop with zero Azure. The moment
//  Vault:StorageConnectionString is present (a deployed environment), Program.cs
//  registers TableStorageVaultStore instead and everything persists across restarts;
//  the OBSERVABLE semantics of BOTH stores match (same cap, same computed
//  TTL-on-list, same claim / alias / burn / rotation rules). The one deliberate
//  difference is concurrency isolation: this store serializes each claim
//  read-modify-write under a per-vault lock (below), so a burn increment + rotate is
//  atomic; the Table store does an unguarded read-then-upsert (last-write-wins), so
//  under simultaneous failed redemptions its burn count can accrue a hair slower.
//  That is a coarse-bound race on a family toy (README section 4), already bounded by
//  the global ceiling + the 7-day window - the same benign-race posture the per-vault
//  cap check already takes - not a correctness divergence in the redeem/recover path.
//
//  KEYING: tales are held in a nested map (vaultId -> taleId -> tale), exactly the
//  PartitionKey (vaultId) / RowKey (taleId) shape the Table store uses. Claim state
//  (keepsake-vault/03) is held in SEPARATE maps keyed by the same canonical vault id
//  - the in-memory analogue of the Table store's sentinel-keyed companion rows:
//    - _claims       : canonical vaultId -> its VaultClaim companion state.
//    - _codeToVault  : the CURRENT active claim code -> its canonical vaultId (the
//                      reverse index redemption resolves against).
//    - _alias        : a redeemed device's vault id -> the canonical claimed vault id
//                      (AC-02: redemption aliases the calling device to the vault).
//  A ConcurrentDictionary throughout makes concurrent save / claim / redeem safe
//  without an explicit lock, except the small claim mutate-in-place paths that use a
//  per-vault lock so a burn/rotate is atomic.
//
//  TIME (AC-05/AC-07): a single injected TimeProvider drives BOTH the tale TTL
//  (CreatedUtc + TtlDays) and the claim-code validity window, so both are
//  deterministic under test (a FakeTimeProvider advances past a window). Defaults to
//  the system clock - existing keepsake-vault/01 tests that construct the store with
//  no argument are unchanged.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Collections.Concurrent;

namespace QuibbleStone.Api.Vault;

/// <summary>
/// A thread-safe, in-memory <see cref="IVaultStore"/> (keepsake-vault/01, extended by
/// keepsake-vault/03), registered when no storage connection string is configured.
/// Fully functional - save with the per-vault cap, list with computed TTL expiry,
/// claim into a family account, mint / rotate / burn a recovery code, and alias a
/// redeeming device to a claimed vault - so the whole vault flow is testable with
/// zero Azure setup; it just does not survive a process restart. Holds only the byline
/// nickname(s), the already-filtered story, and the non-PII family AccountId on a claim
/// (AC-04/AC-06), keyed by opaque random vault ids so vaults are isolated by
/// construction.
/// </summary>
public sealed class InMemoryVaultStore : IVaultStore
{
    // vaultId -> (taleId -> tale). The outer partition is the vault (mirrors the
    // Table PartitionKey), the inner key is the tale id (the RowKey), so a vault's
    // tales are a self-contained partition and a list can never reach across
    // vaults. Both levels are ConcurrentDictionary for lock-free safety.
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, VaultTale>> _byVault =
        new(StringComparer.Ordinal);

    // keepsake-vault/03: the claim companion state per canonical vault id.
    private readonly ConcurrentDictionary<string, VaultClaim> _claims = new(StringComparer.Ordinal);

    // keepsake-vault/03: the reverse index from a vault's CURRENT active code to its
    // canonical vault id (redemption resolves the submitted code against this). The
    // code is compared case-sensitively - callers normalize to canonical (upper) form.
    private readonly ConcurrentDictionary<string, string> _codeToVault = new(StringComparer.Ordinal);

    // keepsake-vault/03: a redeemed device's own vault id -> the canonical claimed
    // vault id it now aliases (AC-02). Reads / saves / claim ops under the alias key
    // resolve to the canonical vault.
    private readonly ConcurrentDictionary<string, string> _alias = new(StringComparer.Ordinal);

    // Per-vault lock objects so a claim burn/rotate (read-modify-write on a VaultClaim)
    // is atomic without locking the whole store. Sized to the tiny claim population.
    private readonly ConcurrentDictionary<string, object> _claimLocks = new(StringComparer.Ordinal);

    private readonly TimeProvider _clock;

    /// <summary>
    /// Constructs the store over a clock (defaulting to the system clock). Tests pass
    /// a fake <see cref="TimeProvider"/> to advance past the tale TTL or a claim
    /// code's validity window deterministically. The no-argument form keeps the
    /// keepsake-vault/01 store tests unchanged.
    /// </summary>
    /// <param name="clock">The time source; defaults to <see cref="TimeProvider.System"/>.</param>
    public InMemoryVaultStore(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public Task<VaultSaveOutcome> SaveAsync(VaultTale tale, CancellationToken ct = default)
    {
        // Resolve any alias so a recovered device's saves land in the canonical vault.
        var vaultId = ResolveCanonical(tale.VaultId);
        var partition = _byVault.GetOrAdd(vaultId, _ => new ConcurrentDictionary<string, VaultTale>(StringComparer.Ordinal));

        // AC-07: reject a save that would push the vault past the cap. A fresh
        // taleId is not yet present, so Count is the current stored total; at or
        // above the cap, refuse without storing (no eviction - a durable archive).
        // The tiny check-then-add race under concurrency can only ever admit a
        // handful of extra rows far below any storage concern, so an explicit lock
        // is not warranted for a family toy (AC-07's intent is a coarse bound).
        if (partition.Count >= IVaultStore.MaxTalesPerVault)
        {
            return Task.FromResult(VaultSaveOutcome.RejectedCapExceeded);
        }

        // Store under the CANONICAL vault id (an aliased device's tales join the
        // claimed vault): both the partition KEY and the stored record's VaultId are the
        // resolved canonical id, so a later read returns the tale under the vault it
        // actually lives in - mirroring the Table store, whose PartitionKey is likewise
        // the canonical id.
        partition[tale.TaleId] = tale with { VaultId = vaultId };
        return Task.FromResult(VaultSaveOutcome.Saved);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<VaultTale>> ListAsync(string vaultId, CancellationToken ct = default)
    {
        // keepsake-vault/03: resolve any device-alias to the canonical claimed vault
        // first (AC-02), so a recovered device reading under its own id sees the
        // claimed vault's tales.
        var canonicalId = ResolveCanonical(vaultId);

        if (string.IsNullOrWhiteSpace(canonicalId) || !_byVault.TryGetValue(canonicalId, out var partition))
        {
            // A miss (vault with no tales) is an ordinary empty list, never an error.
            return Task.FromResult<IReadOnlyList<VaultTale>>([]);
        }

        // AC-05: a CLAIMED vault's tales never expire - skip the TTL filter entirely
        // when the vault is claimed (claiming is the durability upgrade). An unclaimed
        // vault keeps the computed TTL applied per row (AC-03).
        var isClaimed = _claims.ContainsKey(canonicalId);

        var now = _clock.GetUtcNow();
        var live = new List<VaultTale>();
        foreach (var (taleId, tale) in partition)
        {
            if (!isClaimed && tale.IsExpired(now))
            {
                partition.TryRemove(taleId, out _);
                continue;
            }
            live.Add(tale);
        }

        return Task.FromResult<IReadOnlyList<VaultTale>>(live);
    }

    /// <inheritdoc />
    public Task<VaultClaim> ClaimAsync(string vaultId, Guid accountId, CancellationToken ct = default)
    {
        var canonicalId = ResolveCanonical(vaultId);
        lock (LockFor(canonicalId))
        {
            var now = _clock.GetUtcNow();
            _claims.TryGetValue(canonicalId, out var existing);

            // Re-claiming by the same (or any) account rotates the code and preserves
            // the original ClaimedUtc; a first claim stamps ClaimedUtc now. Transfer to
            // a DIFFERENT account is out of scope - this simply re-associates.
            var claimedUtc = existing?.ClaimedUtc ?? now;
            var claim = MintFreshCode(new VaultClaim(
                VaultId: canonicalId,
                AccountId: accountId,
                ClaimCode: string.Empty,     // replaced by MintFreshCode
                ClaimCodeExpiresUtc: now,    // replaced by MintFreshCode
                ClaimCodeFailedAttempts: 0,
                ClaimedUtc: claimedUtc), existing?.ClaimCode, now);

            return Task.FromResult(claim);
        }
    }

    /// <inheritdoc />
    public Task<VaultClaim?> RegenerateClaimCodeAsync(string vaultId, CancellationToken ct = default)
    {
        var canonicalId = ResolveCanonical(vaultId);
        lock (LockFor(canonicalId))
        {
            if (!_claims.TryGetValue(canonicalId, out var existing))
            {
                // Nothing to regenerate - the vault was never claimed.
                return Task.FromResult<VaultClaim?>(null);
            }

            var claim = MintFreshCode(existing, existing.ClaimCode, _clock.GetUtcNow());
            return Task.FromResult<VaultClaim?>(claim);
        }
    }

    /// <inheritdoc />
    public Task<VaultRedeemOutcome> RedeemClaimCodeAsync(string claimCode, string callingDeviceVaultId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(claimCode) || !_codeToVault.TryGetValue(claimCode, out var canonicalId))
        {
            // A blind miss - the code resolves to no vault. Not attributable to any one
            // code (AC-03.3); bounded by the per-IP limiter + the global ceiling.
            return Task.FromResult(VaultRedeemOutcome.InvalidOrExpired);
        }

        lock (LockFor(canonicalId))
        {
            // Re-read under the lock: a concurrent burn/rotate may have moved the code.
            if (!_codeToVault.TryGetValue(claimCode, out canonicalId) ||
                !_claims.TryGetValue(canonicalId, out var claim) ||
                !string.Equals(claim.ClaimCode, claimCode, StringComparison.Ordinal))
            {
                return Task.FromResult(VaultRedeemOutcome.InvalidOrExpired);
            }

            var now = _clock.GetUtcNow();
            if (claim.IsClaimCodeExpired(now) || claim.IsClaimCodeBurned)
            {
                // The code resolves to this vault but is unusable (expired). An
                // ATTRIBUTABLE failed attempt against this code (AC-03.3): count it and
                // burn + rotate at the threshold so a hammered dead code cannot linger.
                RegisterFailedAttempt(claim, now);
                return Task.FromResult(VaultRedeemOutcome.InvalidOrExpired);
            }

            // Valid, unexpired, non-burned: alias the calling device to this vault
            // (AC-02) and reset the failed-attempt count (a success clears the streak).
            if (!string.IsNullOrEmpty(callingDeviceVaultId) &&
                !string.Equals(callingDeviceVaultId, canonicalId, StringComparison.Ordinal))
            {
                _alias[callingDeviceVaultId] = canonicalId;
            }
            if (claim.ClaimCodeFailedAttempts != 0)
            {
                _claims[canonicalId] = claim with { ClaimCodeFailedAttempts = 0 };
            }
            return Task.FromResult(VaultRedeemOutcome.Redeemed);
        }
    }

    /// <inheritdoc />
    public Task<VaultClaim?> GetClaimAsync(string vaultId, CancellationToken ct = default)
    {
        var canonicalId = ResolveCanonical(vaultId);
        lock (LockFor(canonicalId))
        {
            if (!_claims.TryGetValue(canonicalId, out var claim))
            {
                return Task.FromResult<VaultClaim?>(null);
            }

            // AC-07 auto-rotation: an expired or burned code is refreshed on read, so
            // the gallery always shows a live, working code.
            var now = _clock.GetUtcNow();
            if (claim.IsClaimCodeExpired(now) || claim.IsClaimCodeBurned)
            {
                claim = MintFreshCode(claim, claim.ClaimCode, now);
            }

            return Task.FromResult<VaultClaim?>(claim);
        }
    }

    // Increment a claim's failed-attempt count and, at the burn threshold (AC-03.3),
    // invalidate the current code and mint a fresh one (which resets the count). Must
    // be called under the per-vault lock.
    private void RegisterFailedAttempt(VaultClaim claim, DateTimeOffset now)
    {
        var bumped = claim with { ClaimCodeFailedAttempts = claim.ClaimCodeFailedAttempts + 1 };
        if (bumped.IsClaimCodeBurned)
        {
            MintFreshCode(bumped, bumped.ClaimCode, now);
        }
        else
        {
            _claims[claim.VaultId] = bumped;
        }
    }

    // Mint a fresh code for a claim, updating the reverse index (drop the old code,
    // add the new), resetting the failed-attempt count and expiry window, and
    // persisting the claim. Returns the updated claim. Must be called under the
    // per-vault lock. A brand-new code that (astronomically) collides with a live one
    // is re-drawn so the reverse index stays 1:1.
    private VaultClaim MintFreshCode(VaultClaim claim, string? oldCode, DateTimeOffset now)
    {
        string code;
        do
        {
            code = ClaimCodeGenerator.Generate();
        }
        while (_codeToVault.ContainsKey(code));

        if (!string.IsNullOrEmpty(oldCode))
        {
            _codeToVault.TryRemove(oldCode, out _);
        }

        var updated = claim with
        {
            ClaimCode = code,
            ClaimCodeExpiresUtc = now.AddDays(VaultClaim.ClaimCodeValidityDays),
            ClaimCodeFailedAttempts = 0,
        };
        _claims[claim.VaultId] = updated;
        _codeToVault[code] = claim.VaultId;
        return updated;
    }

    // Resolve any device-alias link to the canonical claimed vault id (AC-02). One
    // hop: an alias always points directly at a canonical vault (aliases are never
    // chained), so a single lookup suffices.
    private string ResolveCanonical(string vaultId) =>
        _alias.TryGetValue(vaultId, out var canonical) ? canonical : vaultId;

    private object LockFor(string vaultId) => _claimLocks.GetOrAdd(vaultId, _ => new object());
}
