// ----------------------------------------------------------------------------
//  FamilyDeviceToken - the stored shape of a linked family device (accounts-identity/09,
//  issue #229; ADR 0003 "How a child gets family entitlements" + "Security posture").
//
//  WHAT A LINKED DEVICE IS: a parent generates a short link code from the Account
//  page; a kid's device (never signed in - kids stay anonymous forever, README
//  section 6) redeems that code ONCE for a long-lived, individually revocable
//  family-device token. `CreateRoom` later resolves that token to the family's PAID
//  capabilities exactly like it resolves a signed-in purchaser (accounts-identity/06),
//  identity discarded at the boundary. This record is the server-side row that makes
//  a specific device revocable BY ROW (the reason the token is NOT a Data-Protection
//  payload, which can only expire, never be individually revoked before its TTL).
//
//  HANDLES ARE SECRETS (ADR 0003 security posture, AC-05): the raw token is a bearer
//  credential and is NEVER stored. Only a HASH of it (<see cref="TokenHash"/>) is
//  persisted, so a stored-data leak cannot be replayed directly. The raw value is
//  returned to the device exactly once, at redeem / refresh.
//
//  NO PII (AC-05): a row carries ONLY the AccountId it resolves to, an opaque
//  DeviceTokenId, the token hash, timestamps, a short NON-identifying label, the
//  revoked flag, a rolling ExpiresUtc, and the adult-confirm flag. No kid nickname,
//  birthdate, IP, or user agent is ever collected or stored (AC-04/AC-05).
//
//  TWO SEPARATE AXES, CAPTURED ON THE SAME ROW BUT NEVER CONFLATED (AC-03/AC-07):
//    - ENTITLEMENT (paid capabilities): a valid, non-revoked, non-expired token
//      ALWAYS resolves the family's WHOLE grant set, regardless of the flag below.
//    - CONTENT SAFETY (teen-plus): <see cref="IsAdultConfirmedDevice"/> is the
//      affirmative adult-unlock signal AC-07 requires - it defaults to FALSE on every
//      newly redeemed device (family-safe by default) and only an adult flips it true
//      from the Account page. A token with the flag false still carries full paid
//      capabilities; it simply cannot unlock teen-plus content.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Accounts;

/// <summary>
/// One linked family device (accounts-identity/09). Persisted keyed by
/// <see cref="AccountId"/> (partition) + <see cref="DeviceTokenId"/> (row); the raw
/// token is never stored, only <see cref="TokenHash"/> (AC-05). Carries the two
/// orthogonal axes on one row: it always resolves the family's PAID capabilities
/// (AC-03), while <see cref="IsAdultConfirmedDevice"/> is the SEPARATE content-safety
/// adult-unlock signal that defaults false on redeem (AC-02/AC-07).
/// </summary>
/// <param name="AccountId">The family account this device resolves to (the entitlement spine, accounts-identity/05). The only identity a token carries (AC-05).</param>
/// <param name="DeviceTokenId">An opaque, server-minted id for this device row - the revocation handle the Account page acts on (AC-04). Not PII.</param>
/// <param name="TokenHash">A SHA-256 hash of the RAW token value (never the raw secret, AC-05) - the "handle as a secret" discipline the ADR applies to vault ids and claim codes.</param>
/// <param name="Label">A short, random, NON-identifying two-word label minted at redeem time (AC-04) - enough to make revocation an actionable choice, never a device fingerprint.</param>
/// <param name="CreatedUtc">When the device was linked (redeem time).</param>
/// <param name="LastUsedUtc">When the token was last successfully used (a resolve at CreateRoom or a refresh) - null when never used since linking (AC-04's "never used" case).</param>
/// <param name="ExpiresUtc">The rolling expiry (AC / security posture): slides forward on each successful use so a device in regular use never re-links, while a copied/stolen token stays valid only until it lapses.</param>
/// <param name="IsAdultConfirmedDevice">AC-07's adult-unlock signal: FALSE on every newly redeemed device (family-safe default, AC-02); only an adult flips it true from the linked-devices list. Governs CONTENT safety only, never the paid capabilities (AC-03).</param>
/// <param name="Revoked">True once the account holder revokes this device (AC-04): a revoked token resolves nothing and a room created from it falls back to the default-unlocked, family-safe baseline.</param>
public sealed record FamilyDeviceToken(
    Guid AccountId,
    Guid DeviceTokenId,
    string TokenHash,
    string Label,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? LastUsedUtc,
    DateTimeOffset ExpiresUtc,
    bool IsAdultConfirmedDevice,
    bool Revoked)
{
    /// <summary>
    /// True when this row is a LIVE credential at <paramref name="now"/>: not revoked
    /// and not past its rolling expiry. A dead row (revoked or lapsed) resolves nothing
    /// - CreateRoom falls back to the default-unlocked, family-safe baseline (AC-04).
    /// </summary>
    public bool IsLiveAt(DateTimeOffset now) => !Revoked && ExpiresUtc > now;
}

/// <summary>
/// The NON-PII projection of a linked device the Account page's linked-devices list
/// renders (AC-04): just enough context - the random label, a coarse last-seen, the
/// adult-confirm toggle position, and the revocation handle - to make revocation an
/// actionable decision, and NOTHING device-identifying (no IP, no user agent, no raw
/// token / hash). Deliberately omits <see cref="FamilyDeviceToken.TokenHash"/> so a
/// list read can never leak the credential material.
/// </summary>
/// <param name="DeviceTokenId">The opaque revocation handle the Revoke / adult-confirm actions target.</param>
/// <param name="Label">The short, random, non-identifying label minted at redeem time (AC-04).</param>
/// <param name="CreatedUtc">When the device was linked.</param>
/// <param name="LastUsedUtc">When the token was last used, or null ("never used since linking", AC-04).</param>
/// <param name="IsAdultConfirmedDevice">The current adult-unlock toggle position (AC-07) the parent can flip.</param>
/// <param name="Revoked">Whether this device is already revoked (a revoked row still lists so the action reads as done).</param>
public sealed record LinkedDeviceSummary(
    Guid DeviceTokenId,
    string Label,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? LastUsedUtc,
    bool IsAdultConfirmedDevice,
    bool Revoked);
