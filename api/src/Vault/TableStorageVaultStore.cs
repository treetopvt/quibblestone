// ----------------------------------------------------------------------------
//  TableStorageVaultStore - the Azure Table Storage store for the anonymous
//  server-side keepsake vault (keepsake-vault/01, extended by keepsake-vault/03,
//  issues #196/#230). Mirrors the shape and posture of TableStorageCloudGalleryStore
//  (the reference pattern): Azure.Data.Tables (already a project dependency - NO new
//  NuGet), a CreateIfNotExists-once guard, and the same connection-string-at-startup
//  split (the "absent" half here is the WORKING InMemoryVaultStore, not a no-op).
//
//  KEY / SCHEMA DESIGN (AC-02/AC-04):
//    - TALE rows: PartitionKey = vaultId, RowKey = taleId (a 12-glyph slug). So a
//      list is a SINGLE-partition query. Vault isolation is structural.
//    - CLAIM row (keepsake-vault/03): PartitionKey = vaultId, RowKey = the reserved
//      ClaimRowKey sentinel ("__claim__"). It carries the vault's claim companion
//      state (AccountId, ClaimCode, expiry, failed-attempt count, ClaimedUtc) - the
//      TaleModeration "tiny companion row keyed by the same partition" scheme, so
//      claiming NEVER rewrites a tale row. The sentinel cannot collide with a real
//      taleId (a 12-glyph slug never contains '_'); ListAsync skips it explicitly.
//    - CLAIM-CODE INDEX row: PartitionKey = ClaimCodeIndexPartition ("__claimcode__"),
//      RowKey = the canonical code. Value = VaultId. A point read resolves a
//      submitted code to its vault on redemption (O(1), no scan).
//    - ALIAS row (keepsake-vault/03): PartitionKey = AliasPartition ("__alias__"),
//      RowKey = a redeemed device's vault id. Value = the canonical claimed vault id.
//      Reads / saves under the alias resolve to the canonical vault (AC-02).
//    The sentinel partitions are short strings; a real vaultId is >= 36 chars
//    (VaultId.MinLength), so a tale/claim partition query (PartitionKey = vaultId)
//    can never collide with them.
//
//  NO PII (AC-06): only anonymous, already-vetted fields land here. A claim adds ONLY
//  the non-PII family AccountId GUID (never an email / name) and the opaque claim code
//  - never a kid-profile / seat-preset id (AC-04). No property here is ever logged or
//  put in an exception message (the telemetry scrubber cannot clean message text).
//
//  TTL (AC-03/AC-05), COMPUTED NOT STORED: there is NO ExpiresUtc column. Expiry is
//  computed as CreatedUtc + TtlDays (VaultTale.IsExpired) while enumerating the
//  partition in ListAsync. A CLAIMED vault's tales are EXEMPT (AC-05): when a claim
//  row is present, the TTL filter is skipped entirely.
//
//  SOFT DELETE + RESTORE (keepsake-vault/04, issue #231): a deletion sets a nullable
//  DeletedUtc column on the tale's OWN row (the "rebuild the immutable record with a
//  flipped marker" pattern - the content fields are never touched, AC-05) rather
//  than removing the row. ListAsync omits soft-deleted rows (AC-01) but keeps them
//  within the restore window so RestoreAsync can clear the marker (AC-02/AC-06); a
//  soft-deleted row past its window is reclaimed lazily on read, the same purge-on-
//  read idiom the TTL uses (AC-03). SoftDeleteAsync / RestoreAsync point-read the
//  one (vaultId, taleId) row via ReadRowAsync (which, unlike ListAsync, sees a
//  soft-deleted row) and upsert the flipped marker. Both read the injected clock so
//  a restore-window elapse is deterministic under test.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;

namespace QuibbleStone.Api.Vault;

/// <summary>
/// The Azure Table Storage keepsake-vault store (keepsake-vault/01, extended by
/// keepsake-vault/03). Stores tale rows keyed PartitionKey = vaultId / RowKey =
/// taleId, plus sentinel-keyed claim, claim-code-index, and device-alias companion
/// rows for the claim + recovery flow. Carries NO PII beyond the byline nickname(s)
/// and the non-PII family AccountId on a claim (AC-04/AC-06). Enforces the per-vault
/// cap on save (AC-07), the computed TTL on list with a claimed-vault exemption
/// (AC-03/AC-05), and the recovery code's validity window / burn / rotation
/// (AC-03/AC-07). Used only when a storage connection string is configured (else
/// InMemoryVaultStore).
/// </summary>
public sealed class TableStorageVaultStore : IVaultStore
{
    /// <summary>The table name vault tales (and companion rows) land in (created on first write if absent).</summary>
    public const string TableName = "VaultTales";

    /// <summary>
    /// The reserved RowKey of a vault's CLAIM companion row (keepsake-vault/03), under
    /// PartitionKey = vaultId. A 12-glyph tale slug never contains '_', so it cannot
    /// collide with a real taleId; ListAsync skips it explicitly.
    /// </summary>
    public const string ClaimRowKey = "__claim__";

    /// <summary>The reserved PartitionKey of the claim-code -> vaultId reverse-index rows (keepsake-vault/03).</summary>
    public const string ClaimCodeIndexPartition = "__claimcode__";

    /// <summary>The reserved PartitionKey of the device-alias -> canonical-vaultId rows (keepsake-vault/03).</summary>
    public const string AliasPartition = "__alias__";

    private readonly TableClient _table;
    private readonly ILogger<TableStorageVaultStore> _logger;
    private readonly TimeProvider _clock;

    // Ensure-once guard (same rationale as the sibling Table stores): CreateIfNotExists
    // is a network round-trip we only need on the FIRST write. A benign race is harmless
    // (idempotent); a failed create leaves the flag false so the next write retries.
    private volatile bool _tableEnsured;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <summary>
    /// Constructs the store over a storage connection string (from configuration,
    /// NEVER a committed literal - see Program.cs). The connection is resolved once
    /// at startup; the table is created lazily on the first save.
    /// </summary>
    /// <param name="connectionString">The Azure Storage connection string (supplied per-environment).</param>
    /// <param name="logger">Logs list / delete failures server-side (never any PII, never the vault id / claim code).</param>
    /// <param name="clock">The time source for TTL / claim-code expiry; defaults to <see cref="TimeProvider.System"/>.</param>
    public TableStorageVaultStore(string connectionString, ILogger<TableStorageVaultStore> logger, TimeProvider? clock = null)
    {
        _table = new TableClient(connectionString, TableName);
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<VaultSaveOutcome> SaveAsync(VaultTale tale, CancellationToken ct = default)
    {
        await EnsureTableAsync(ct);

        // Resolve any alias so a recovered device's saves land in the canonical vault.
        var vaultId = await ResolveCanonicalAsync(tale.VaultId, ct);

        // AC-07: bound per-vault growth. Count the vault's TALE partition first and
        // reject at or above the cap (no eviction - a durable archive).
        if (await CountTalesAsync(vaultId, ct) >= IVaultStore.MaxTalesPerVault)
        {
            return VaultSaveOutcome.RejectedCapExceeded;
        }

        // Serialize under the CANONICAL vault id (an aliased device's tales join the
        // claimed vault, mirroring the in-memory store's `tale with { VaultId }`).
        // ToEntity also carries the soft-delete marker column (keepsake-vault/04), so a
        // re-save round-trips DeletedUtc rather than dropping it.
        var entity = ToEntity(tale with { VaultId = vaultId });

        await _table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
        return VaultSaveOutcome.Saved;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VaultTale>> ListAsync(string vaultId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(vaultId))
        {
            return [];
        }

        // keepsake-vault/03: resolve any device-alias to the canonical claimed vault
        // (AC-02) and read the claim row once so a claimed vault's TTL is exempt (AC-05).
        var canonicalId = await ResolveCanonicalAsync(vaultId, ct);
        var isClaimed = await ReadClaimEntityAsync(canonicalId, ct) is not null;

        var now = _clock.GetUtcNow();
        var tales = new List<VaultTale>();
        var expired = new List<string>();
        try
        {
            // Single-partition query (PartitionKey = canonicalId). The strongly-typed
            // predicate overload (not a raw OData string) is injection-proof by
            // construction, matching the sibling stores' precedent.
            var query = _table.QueryAsync<TableEntity>(
                e => e.PartitionKey == canonicalId,
                cancellationToken: ct);
            await foreach (var entity in query)
            {
                // Skip the claim companion row (RowKey = the sentinel) - it shares the
                // partition but is not a tale.
                if (entity.RowKey == ClaimRowKey)
                {
                    continue;
                }

                var tale = FromEntity(entity);
                // Apply the computed exclusions as the partition is enumerated:
                //   - AC-05/AC-03 TTL: a claimed vault's tales never expire; an unclaimed
                //     vault's row past CreatedUtc + TtlDays is omitted + reclaimed.
                //   - keepsake-vault/04 SOFT-DELETE: a soft-deleted row is omitted; if its
                //     restore window has ALSO elapsed (AC-03) it is reclaimed, otherwise it
                //     is kept (omitted, not deleted) so RestoreAsync can still recover it.
                //     An explicit delete is honored even in a claimed vault.
                if ((!isClaimed && tale.IsExpired(now)) || tale.IsRestoreWindowElapsed(now))
                {
                    expired.Add(tale.TaleId);
                    continue;
                }
                if (tale.IsDeleted)
                {
                    // Within the restore window: hidden from the listing but retained.
                    continue;
                }
                tales.Add(tale);
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // The table itself does not exist yet (nothing has ever been saved) - an
            // ordinary empty vault, not an error.
            _logger.LogDebug(ex, "Vault list returned 404 (table not yet created); treating as an empty vault.");
            return [];
        }
        // A genuine storage failure (non-404) PROPAGATES rather than degrading to an
        // empty list - the read endpoint then surfaces a non-2xx and the client can
        // retry, rather than silently reading as "no tales" on a transient fault.

        // Best-effort reclaim of the expired rows found above (AC-03). A failure to
        // delete never affects the read result.
        foreach (var taleId in expired)
        {
            await TryDeleteAsync(canonicalId, taleId, ct);
        }

        return tales;
    }

    /// <inheritdoc />
    public async Task<bool> SoftDeleteAsync(string vaultId, string taleId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(vaultId) || string.IsNullOrWhiteSpace(taleId))
        {
            return false;
        }

        await EnsureTableAsync(ct);
        var tale = await ReadRowAsync(vaultId, taleId, ct);
        if (tale is null)
        {
            return false;
        }

        var now = _clock.GetUtcNow();
        // Genuinely gone (TTL-expired or past the restore window): reclaim it and
        // report no soft-delete happened - there is nothing to delete (AC-03).
        if (tale.IsExpired(now) || tale.IsRestoreWindowElapsed(now))
        {
            await TryDeleteAsync(vaultId, taleId, ct);
            return false;
        }

        // Already soft-deleted within the window: idempotent no-op success (AC-01).
        if (tale.IsDeleted)
        {
            return true;
        }

        // Live -> stamp DeletedUtc (server-side) on a rebuilt record and persist it.
        // The content fields are never touched (AC-05) - only the marker flips.
        await _table.UpsertEntityAsync(ToEntity(tale with { DeletedUtc = now }), TableUpdateMode.Replace, ct);
        return true;
    }

    /// <inheritdoc />
    public async Task<VaultTale?> RestoreAsync(string vaultId, string taleId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(vaultId) || string.IsNullOrWhiteSpace(taleId))
        {
            return null;
        }

        await EnsureTableAsync(ct);
        var tale = await ReadRowAsync(vaultId, taleId, ct);
        if (tale is null)
        {
            return null;
        }

        var now = _clock.GetUtcNow();
        // Genuinely gone (TTL-expired or past the restore window): reclaim it and
        // return null - un-deleting a lapsed tale is out of scope (AC-03).
        if (tale.IsExpired(now) || tale.IsRestoreWindowElapsed(now))
        {
            await TryDeleteAsync(vaultId, taleId, ct);
            return null;
        }

        // Already live: harmless no-op, return it unchanged.
        if (!tale.IsDeleted)
        {
            return tale;
        }

        // Within the window: clear the marker (a pure undo, content untouched -
        // AC-05/AC-06) and persist, returning the now-live tale.
        var restored = tale with { DeletedUtc = null };
        await _table.UpsertEntityAsync(ToEntity(restored), TableUpdateMode.Replace, ct);
        return restored;
    }

    // Point-read one (vault, tale) row REGARDLESS of its soft-delete / expiry state
    // (used by SoftDeleteAsync / RestoreAsync, which must see a soft-deleted row that
    // ListAsync omits). Returns null when the row - or the table - does not exist.
    private async Task<VaultTale?> ReadRowAsync(string vaultId, string taleId, CancellationToken ct)
    {
        try
        {
            var response = await _table.GetEntityIfExistsAsync<TableEntity>(vaultId, taleId, cancellationToken: ct);
            return response.HasValue && response.Value is not null ? FromEntity(response.Value) : null;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // The table itself does not exist yet - nothing has ever been saved.
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<VaultClaim> ClaimAsync(string vaultId, Guid accountId, CancellationToken ct = default)
    {
        await EnsureTableAsync(ct);
        var canonicalId = await ResolveCanonicalAsync(vaultId, ct);

        var now = _clock.GetUtcNow();
        var existing = await ReadClaimAsync(canonicalId, ct);

        // Re-claiming preserves the original ClaimedUtc; a first claim stamps it now.
        var claimedUtc = existing?.ClaimedUtc ?? now;
        var baseClaim = new VaultClaim(
            VaultId: canonicalId,
            AccountId: accountId,
            ClaimCode: string.Empty,   // replaced by MintFreshCodeAsync
            ClaimCodeExpiresUtc: now,  // replaced by MintFreshCodeAsync
            ClaimCodeFailedAttempts: 0,
            ClaimedUtc: claimedUtc);

        return await MintFreshCodeAsync(baseClaim, existing?.ClaimCode, now, ct);
    }

    /// <inheritdoc />
    public async Task<VaultClaim?> RegenerateClaimCodeAsync(string vaultId, CancellationToken ct = default)
    {
        var canonicalId = await ResolveCanonicalAsync(vaultId, ct);
        var existing = await ReadClaimAsync(canonicalId, ct);
        if (existing is null)
        {
            // Nothing to regenerate - the vault was never claimed.
            return null;
        }

        return await MintFreshCodeAsync(existing, existing.ClaimCode, _clock.GetUtcNow(), ct);
    }

    /// <inheritdoc />
    public async Task<VaultRedeemOutcome> RedeemClaimCodeAsync(string claimCode, string callingDeviceVaultId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(claimCode))
        {
            return VaultRedeemOutcome.InvalidOrExpired;
        }

        // Resolve the submitted code to a vault via the reverse-index point read.
        var canonicalId = await ReadCodeIndexAsync(claimCode, ct);
        if (canonicalId is null)
        {
            // A blind miss - not attributable to any one code (AC-03.3); bounded by the
            // per-IP limiter + the global ceiling (AC-03.1/AC-03.2).
            return VaultRedeemOutcome.InvalidOrExpired;
        }

        var claim = await ReadClaimAsync(canonicalId, ct);
        if (claim is null || !string.Equals(claim.ClaimCode, claimCode, StringComparison.Ordinal))
        {
            // The index pointed at a code the claim no longer holds (a concurrent
            // rotate) - treat as a miss.
            return VaultRedeemOutcome.InvalidOrExpired;
        }

        var now = _clock.GetUtcNow();
        if (claim.IsClaimCodeExpired(now) || claim.IsClaimCodeBurned)
        {
            // Resolves to this vault but unusable: an ATTRIBUTABLE failed attempt
            // (AC-03.3). Count it and burn + rotate at the threshold.
            await RegisterFailedAttemptAsync(claim, now, ct);
            return VaultRedeemOutcome.InvalidOrExpired;
        }

        // Valid: alias the calling device to this vault (AC-02) and reset the count.
        if (!string.IsNullOrEmpty(callingDeviceVaultId) &&
            !string.Equals(callingDeviceVaultId, canonicalId, StringComparison.Ordinal))
        {
            await WriteAliasAsync(callingDeviceVaultId, canonicalId, ct);
        }
        if (claim.ClaimCodeFailedAttempts != 0)
        {
            await WriteClaimAsync(claim with { ClaimCodeFailedAttempts = 0 }, ct);
        }
        return VaultRedeemOutcome.Redeemed;
    }

    /// <inheritdoc />
    public async Task<VaultClaim?> GetClaimAsync(string vaultId, CancellationToken ct = default)
    {
        var canonicalId = await ResolveCanonicalAsync(vaultId, ct);
        var claim = await ReadClaimAsync(canonicalId, ct);
        if (claim is null)
        {
            return null;
        }

        // AC-07 auto-rotation: refresh an expired / burned code on read so the gallery
        // always shows a live, working code.
        var now = _clock.GetUtcNow();
        if (claim.IsClaimCodeExpired(now) || claim.IsClaimCodeBurned)
        {
            claim = await MintFreshCodeAsync(claim, claim.ClaimCode, now, ct);
        }

        return claim;
    }

    // ---- claim helpers --------------------------------------------------------

    // Increment a claim's failed-attempt count and, at the burn threshold (AC-03.3),
    // invalidate the current code and mint a fresh one (which resets the count).
    // NOTE (store-parity caveat): this is an unguarded read-then-upsert, so under
    // SIMULTANEOUS failed redemptions increments are last-write-wins and the burn
    // count can accrue a hair slower than the lock-serialized InMemoryVaultStore. That
    // is a coarse-bound race on a family toy, already bounded by the global ceiling +
    // the 7-day window (the same benign-race posture SaveAsync's cap check takes), not
    // a correctness gap - a per-code burn is defence-in-depth behind two hard bounds.
    private async Task RegisterFailedAttemptAsync(VaultClaim claim, DateTimeOffset now, CancellationToken ct)
    {
        var bumped = claim with { ClaimCodeFailedAttempts = claim.ClaimCodeFailedAttempts + 1 };
        if (bumped.IsClaimCodeBurned)
        {
            await MintFreshCodeAsync(bumped, bumped.ClaimCode, now, ct);
        }
        else
        {
            await WriteClaimAsync(bumped, ct);
        }
    }

    // Mint a fresh code for a claim: draw a unique code, drop the old code-index row,
    // add the new one, reset the count + expiry window, and persist the claim row.
    private async Task<VaultClaim> MintFreshCodeAsync(VaultClaim claim, string? oldCode, DateTimeOffset now, CancellationToken ct)
    {
        await EnsureTableAsync(ct);

        // Draw a code not already indexed (astronomically rare collision).
        string code;
        while (true)
        {
            code = ClaimCodeGenerator.Generate();
            if (await ReadCodeIndexAsync(code, ct) is null)
            {
                break;
            }
        }

        var updated = claim with
        {
            ClaimCode = code,
            ClaimCodeExpiresUtc = now.AddDays(VaultClaim.ClaimCodeValidityDays),
            ClaimCodeFailedAttempts = 0,
        };

        await WriteClaimAsync(updated, ct);
        await WriteCodeIndexAsync(code, claim.VaultId, ct);
        if (!string.IsNullOrEmpty(oldCode))
        {
            await TryDeleteAsync(ClaimCodeIndexPartition, oldCode, ct);
        }

        return updated;
    }

    private async Task<VaultClaim?> ReadClaimAsync(string vaultId, CancellationToken ct)
    {
        var entity = await ReadClaimEntityAsync(vaultId, ct);
        return entity is null ? null : ClaimFromEntity(entity);
    }

    private async Task<TableEntity?> ReadClaimEntityAsync(string vaultId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(vaultId))
        {
            return null;
        }
        try
        {
            var response = await _table.GetEntityAsync<TableEntity>(vaultId, ClaimRowKey, cancellationToken: ct);
            return response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private async Task WriteClaimAsync(VaultClaim claim, CancellationToken ct)
    {
        await EnsureTableAsync(ct);
        var entity = new TableEntity(claim.VaultId, ClaimRowKey)
        {
            ["AccountId"] = claim.AccountId.ToString(),
            ["ClaimCode"] = claim.ClaimCode,
            ["ClaimCodeExpiresUtc"] = claim.ClaimCodeExpiresUtc,
            ["ClaimCodeFailedAttempts"] = claim.ClaimCodeFailedAttempts,
            ["ClaimedUtc"] = claim.ClaimedUtc,
        };
        await _table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
    }

    private static VaultClaim ClaimFromEntity(TableEntity entity)
    {
        _ = Guid.TryParse(entity.GetString("AccountId"), out var accountId);
        return new VaultClaim(
            VaultId: entity.PartitionKey,
            AccountId: accountId,
            ClaimCode: entity.GetString("ClaimCode") ?? string.Empty,
            ClaimCodeExpiresUtc: entity.GetDateTimeOffset("ClaimCodeExpiresUtc") ?? DateTimeOffset.MinValue,
            ClaimCodeFailedAttempts: entity.GetInt32("ClaimCodeFailedAttempts") ?? 0,
            ClaimedUtc: entity.GetDateTimeOffset("ClaimedUtc") ?? DateTimeOffset.MinValue);
    }

    // ---- code-index + alias helpers ------------------------------------------

    private async Task<string?> ReadCodeIndexAsync(string code, CancellationToken ct)
    {
        try
        {
            var response = await _table.GetEntityAsync<TableEntity>(ClaimCodeIndexPartition, code, cancellationToken: ct);
            var vaultId = response.Value.GetString("VaultId");
            return string.IsNullOrWhiteSpace(vaultId) ? null : vaultId;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private async Task WriteCodeIndexAsync(string code, string vaultId, CancellationToken ct)
    {
        var entity = new TableEntity(ClaimCodeIndexPartition, code) { ["VaultId"] = vaultId };
        await _table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
    }

    // Resolve any device-alias link to the canonical claimed vault id (AC-02). One hop
    // (aliases are never chained): a single point read suffices.
    private async Task<string> ResolveCanonicalAsync(string vaultId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(vaultId))
        {
            return vaultId;
        }
        try
        {
            var response = await _table.GetEntityAsync<TableEntity>(AliasPartition, vaultId, cancellationToken: ct);
            var canonical = response.Value.GetString("CanonicalVaultId");
            return string.IsNullOrWhiteSpace(canonical) ? vaultId : canonical;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return vaultId;
        }
    }

    private async Task WriteAliasAsync(string aliasVaultId, string canonicalVaultId, CancellationToken ct)
    {
        var entity = new TableEntity(AliasPartition, aliasVaultId) { ["CanonicalVaultId"] = canonicalVaultId };
        await _table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
    }

    // ---- tale helpers (keepsake-vault/01) ------------------------------------

    // Count the TALE rows in one vault partition (RowKey only) for the per-vault cap
    // check (AC-07), excluding the claim sentinel row. A missing table is zero rows.
    private async Task<int> CountTalesAsync(string vaultId, CancellationToken ct)
    {
        var count = 0;
        try
        {
            var query = _table.QueryAsync<TableEntity>(
                e => e.PartitionKey == vaultId,
                select: ["RowKey"],
                cancellationToken: ct);
            await foreach (var entity in query)
            {
                if (entity.RowKey != ClaimRowKey)
                {
                    count++;
                }
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return 0;
        }
        return count;
    }

    // Delete one (partition, row) entity, swallowing a not-found (idempotent) and
    // logging any other failure - never throws to the caller.
    private async Task TryDeleteAsync(string partitionKey, string rowKey, CancellationToken ct)
    {
        try
        {
            await _table.DeleteEntityAsync(partitionKey, rowKey, ETag.All, ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Already gone - the delete is idempotent.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Vault companion-row delete failed (swallowed).");
        }
    }

    // Create the table ONCE (lazy); after the first success the guard skips the
    // extra round-trip on every subsequent write.
    private async Task EnsureTableAsync(CancellationToken ct)
    {
        if (!_tableEnsured)
        {
            await _table.CreateIfNotExistsAsync(cancellationToken: ct);
            _tableEnsured = true;
        }
    }

    // Serialize a tale to its stored entity. Anonymous, already-vetted fields only -
    // no PII beyond the byline (AC-04). No ExpiresUtc column (AC-03): TTL expiry is
    // computed from CreatedUtc at read time, never stored. DeletedUtc is the
    // soft-delete marker (keepsake-vault/04): written only when set, and explicitly
    // cleared (set null) on restore so a stale marker never survives a round-trip.
    private static TableEntity ToEntity(VaultTale tale) => new(tale.VaultId, tale.TaleId)
    {
        ["Title"] = tale.Title,
        ["PartsJson"] = JsonSerializer.Serialize(tale.Parts),
        ["BylineNames"] = tale.BylineNames,
        ["CreatedUtc"] = tale.CreatedUtc,
        // Nullable: a live tale stores an explicit null (a Replace upsert then clears
        // any prior marker), a soft-deleted tale stores the deletion instant.
        ["DeletedUtc"] = tale.DeletedUtc,
    };

    // Rebuild the domain record from a stored tale entity. Defensive on the parts
    // JSON: a malformed / empty blob yields an empty body rather than throwing.
    private static VaultTale FromEntity(TableEntity entity)
    {
        var partsJson = entity.GetString("PartsJson");
        IReadOnlyList<VaultTalePart> parts;
        try
        {
            parts = string.IsNullOrWhiteSpace(partsJson)
                ? []
                : JsonSerializer.Deserialize<List<VaultTalePart>>(partsJson) ?? [];
        }
        catch (JsonException)
        {
            parts = [];
        }

        return new VaultTale(
            VaultId: entity.PartitionKey,
            TaleId: entity.RowKey,
            Title: entity.GetString("Title") ?? string.Empty,
            Parts: parts,
            BylineNames: entity.GetString("BylineNames") ?? string.Empty,
            // CreatedUtc is always present on a real row; the fallback only guards a
            // corrupt row and never keys a real TTL decision.
            CreatedUtc: entity.GetDateTimeOffset("CreatedUtc") ?? DateTimeOffset.UtcNow,
            // Absent column (a tale saved before story 04, or a live tale) -> null.
            DeletedUtc: entity.GetDateTimeOffset("DeletedUtc"));
    }
}
