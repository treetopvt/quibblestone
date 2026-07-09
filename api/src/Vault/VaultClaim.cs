// ----------------------------------------------------------------------------
//  VaultClaim - the tiny mutable companion state behind a keepsake vault's
//  claim into a family account and its recovery claim code (keepsake-vault/03,
//  ADR 0003 Decision 2 / Layer 2, issue #230).
//
//  WHY THIS IS A SEPARATE RECORD, NOT A FIELD ON VaultTale (load-bearing, mirrors
//  TaleModeration's precedent): a VaultTale is an IMMUTABLE keepsake fact
//  serialized WHOLE into one Table row. Claim state (which account owns the vault,
//  the current recovery code, its expiry, its accumulated failed-attempt count) is
//  MUTABLE and belongs to the VAULT as a whole, not to any one tale. If it lived on
//  the tales, claiming a vault or rotating its code would have to rewrite EVERY tale
//  row. Instead claim state is ONE companion row keyed by the same vault id (a
//  sentinel row key in the store), mutated on its own, never touching a tale body -
//  exactly TaleModeration's "tiny companion row keyed by the same partition" scheme
//  (api/src/PublishedTales/TaleModeration.cs).
//
//  NO PII (AC-06, ADR 0003 "no PII on the play plane"): the only identity here is
//  the stable, non-PII AccountId GUID (never an email, never a raw name) - and it is
//  ACCOUNT-PLANE household data, present only because an adult signed in and claimed
//  (ADR 0003's carve-out). The claim code is an opaque, unguessable handle that
//  carries no identity of its own (AC-06): redeeming it only re-links a vault id to
//  a device. There is NO kid-profile / seat-preset reference anywhere - a claimed
//  vault is tied to the FAMILY account only, never to an individual kid (AC-04, the
//  firm ADR 0003 Decision 1 boundary).
//
//  THE CLAIM CODE IS A BEARER SECRET (AC-02, ADR 0003 "Handles are secrets"), given
//  the SAME treatment as the vault id: it travels in a request BODY on redemption
//  (never a URL path/query), is minted with a real CSPRNG (ClaimCodeGenerator), and
//  is bounded by a validity window (AC-07) plus three anti-brute-force controls
//  (AC-03: a per-IP limiter, a global redemption ceiling, and this record's per-code
//  failed-attempt burn). It is never logged and never put in an exception message
//  (the telemetry scrubber cannot clean message text).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Vault;

/// <summary>
/// The claim companion state for one keepsake vault, keyed by its vault id
/// (keepsake-vault/03). Records which family <see cref="AccountId"/> claimed the
/// vault, the current human-friendly recovery <see cref="ClaimCode"/> and its
/// <see cref="ClaimCodeExpiresUtc"/> validity window (AC-07), the accumulated
/// <see cref="ClaimCodeFailedAttempts"/> against the current code (AC-03's per-code
/// burn), and when the vault was first <see cref="ClaimedUtc"/>. A DISTINCT, MUTABLE
/// signal from the immutable <see cref="VaultTale"/> content rows - mirrors
/// TaleModeration's companion-row pattern so claiming never rewrites a tale. Carries
/// NO PII beyond the non-PII AccountId GUID (AC-06) and NO kid-profile reference
/// (AC-04).
/// </summary>
/// <param name="VaultId">The vault this claim state is keyed to (a random device handle, never PII).</param>
/// <param name="AccountId">
/// The stable family <see cref="Accounts.Account.Id"/> GUID that claimed the vault
/// (accounts-identity/05) - the durability upgrade path (AC-01). Non-PII by itself,
/// account-plane household data, NEVER a kid-profile or seat-preset id (AC-04).
/// </param>
/// <param name="ClaimCode">
/// The current active recovery code - the canonical, ungrouped 9-glyph value
/// (ClaimCodeGenerator; the gallery displays it grouped). A bearer secret (AC-02).
/// </param>
/// <param name="ClaimCodeExpiresUtc">
/// When the current code stops working (AC-07): minted-at + <see cref="ClaimCodeValidityDays"/>.
/// Past this, redemption is rejected and a fresh code is auto-minted the next time
/// the gallery is opened (GetClaimAsync).
/// </param>
/// <param name="ClaimCodeFailedAttempts">
/// How many failed redemption attempts have accumulated against the CURRENT code
/// (AC-03.3). At <see cref="ClaimCodeFailedAttemptBurnThreshold"/> the code
/// auto-invalidates and a fresh one is minted, regardless of source IP. Reset to 0
/// whenever a fresh code is minted (claim / regenerate / burn-rotate) or a
/// redemption succeeds.
/// </param>
/// <param name="ClaimedUtc">When the vault was first claimed into the family account (AC-01). Not PII.</param>
public sealed record VaultClaim(
    string VaultId,
    Guid AccountId,
    string ClaimCode,
    DateTimeOffset ClaimCodeExpiresUtc,
    int ClaimCodeFailedAttempts,
    DateTimeOffset ClaimedUtc)
{
    /// <summary>
    /// The recovery code's validity window in days (AC-07, default 7): a minted code
    /// stops working <see cref="ClaimCodeValidityDays"/> days after it was minted and
    /// is auto-rotated on the next gallery open. A settings-key candidate (ADR 0003
    /// control plane) shipped as a code constant until control-plane/01's catalog
    /// exists - this story is not blocked on it.
    /// </summary>
    public const int ClaimCodeValidityDays = 7;

    /// <summary>
    /// The per-code failed-attempt burn threshold (AC-03.3, a named constant): once
    /// <see cref="ClaimCodeFailedAttempts"/> reaches this many cumulative failed
    /// redemptions against the current code, the code auto-invalidates and a fresh
    /// one is minted - regardless of which IP(s) the attempts came from. This is the
    /// per-code control that, together with the per-IP limiter and the global
    /// redemption ceiling (AC-03.1/AC-03.2), makes a typeable-length code infeasible
    /// to brute-force. A settings-key candidate shipped as a code constant.
    /// </summary>
    public const int ClaimCodeFailedAttemptBurnThreshold = 20;

    /// <summary>
    /// True when the current code is at or past its validity window
    /// (<see cref="ClaimCodeExpiresUtc"/> &lt;= <paramref name="now"/>) and must be
    /// rejected on redemption / auto-rotated on the next gallery open (AC-07). Pure
    /// and computed so it is deterministic under test.
    /// </summary>
    /// <param name="now">The current instant (injected so tests are deterministic).</param>
    public bool IsClaimCodeExpired(DateTimeOffset now) => ClaimCodeExpiresUtc <= now;

    /// <summary>
    /// True when the current code has accumulated enough failed redemption attempts
    /// to burn (AC-03.3): <see cref="ClaimCodeFailedAttempts"/> at or past
    /// <see cref="ClaimCodeFailedAttemptBurnThreshold"/>. When this trips the store
    /// invalidates the code and mints a fresh one.
    /// </summary>
    public bool IsClaimCodeBurned => ClaimCodeFailedAttempts >= ClaimCodeFailedAttemptBurnThreshold;
}
