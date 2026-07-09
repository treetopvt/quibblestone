// ----------------------------------------------------------------------------
//  VaultId - the vault-id bearer-credential primitive (keepsake-vault/01,
//  ADR 0003 "Handles are secrets", issue #196).
//
//  A vault id is a BEARER CREDENTIAL: anyone holding it can read and write that
//  vault (the deliberate, minimal-friction design - it mirrors a room join code's
//  possession-based trust model, not a purchaser credential's). It is anonymous by
//  construction (never derived from or joined to an email, device fingerprint, IP,
//  or any identity, AC-04). Two things live here:
//
//    - Mint(): the SERVER-SIDE minter for the AC-01 fallback path. The client
//      mints its own id with crypto.randomUUID() and ONLY that (no Math.random
//      fallback - a weak id would be a forgeable credential); a device that lacks
//      crypto.randomUUID calls POST /api/vault/mint, which returns this. It uses
//      System.Security.Cryptography.RandomNumberGenerator - the SAME primitive
//      Room.NewReconnectToken() uses for the per-seat reconnect handle - so a
//      server-minted id has a real CSPRNG entropy floor.
//
//    - IsWellFormed(): the SERVER-SIDE length/format floor enforced on EVERY vault
//      endpoint (AC-01). The server independently rejects any client-presented
//      vault id shorter than a UUID's 36 characters or failing a basic
//      random-looking-token shape check - a weak, client-forged id is never
//      accepted as a bearer credential regardless of what the client sent. This
//      accepts both a crypto.randomUUID() (36 chars, hex + hyphens) and a
//      server-minted hex token.
//
//  Pure, static, and dependency-light so it is trivially unit-tested.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Security.Cryptography;

namespace QuibbleStone.Api.Vault;

/// <summary>
/// Mints and validates vault ids (keepsake-vault/01). A vault id is an opaque,
/// cryptographically random bearer credential (AC-01). Pure and static - no state,
/// no DI.
/// </summary>
public static class VaultId
{
    /// <summary>
    /// The minimum accepted vault-id length (AC-01): a UUID is 36 characters, so a
    /// well-formed client id (crypto.randomUUID()) meets this and a short, forged id
    /// is rejected with 400. The server-minted <see cref="Mint"/> id is longer still.
    /// </summary>
    public const int MinLength = 36;

    /// <summary>
    /// A generous upper bound so an oversized value cannot be used to bloat a
    /// partition key or a log line. Well above any legitimate id length.
    /// </summary>
    public const int MaxLength = 200;

    /// <summary>
    /// Mints a fresh, server-side, CSPRNG-backed vault id for the no-crypto.randomUUID
    /// fallback path (AC-01). 32 random bytes hex-encoded (64 characters), mirroring
    /// Room.NewReconnectToken()'s server-side primitive - comfortably past
    /// <see cref="MinLength"/> and unguessable.
    /// </summary>
    public static string Mint() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// The server-side length/format floor applied to any client-presented vault id
    /// on every vault endpoint (AC-01). Rejects null / whitespace, anything shorter
    /// than <see cref="MinLength"/> or longer than <see cref="MaxLength"/>, and any
    /// value carrying a character outside the random-token shape (ASCII letters,
    /// digits, and hyphen - covering both a UUID and a hex token). A weak or forged
    /// id never passes as a bearer credential.
    /// </summary>
    /// <param name="candidate">The raw X-Vault-Id header value.</param>
    /// <returns>True when the id meets the floor and may be used as a partition key.</returns>
    public static bool IsWellFormed(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }
        if (candidate.Length < MinLength || candidate.Length > MaxLength)
        {
            return false;
        }
        foreach (var c in candidate)
        {
            var isAllowed =
                c is >= 'A' and <= 'Z' ||
                c is >= 'a' and <= 'z' ||
                c is >= '0' and <= '9' ||
                c == '-';
            if (!isAllowed)
            {
                return false;
            }
        }
        return true;
    }
}
