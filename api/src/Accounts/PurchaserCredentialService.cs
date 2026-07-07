// ----------------------------------------------------------------------------
//  PurchaserCredentialService - the ONE place the short-lived purchaser sign-in
//  credential is minted and resolved (accounts-identity/03 + billing-entitlements/05).
//
//  WHY IT EXISTS: accounts-identity/03's AccountsController MINTS this credential on a
//  successful magic-link verify; billing-entitlements/05's restore/manage read endpoint
//  RESOLVES it to know who is asking. Both must use the SAME ASP.NET Core Data
//  Protection purpose + lifetime, or a credential minted by one would not be readable by
//  the other. Rather than duplicate the purpose string and the protector wiring in two
//  controllers (a drift risk, and the "write a second auth check" smell story 05 AC-06
//  warns against), that logic lives here exactly once and both inject this service.
//
//  WHAT THE CREDENTIAL IS: a time-limited, Data-Protection-protected token carrying only
//  the purchaser email + an issued-at stamp (no PII beyond the one identity, no room /
//  player reference). It is NEVER required by, nor checked in, GameHub or any player-
//  facing endpoint - free play stays 100% login-free (AC-03/AC-04 of story 03).
//
//  KEY RING (deliberately the framework DEFAULT for now, carried from accounts-identity/03):
//  the default key ring is per-instance and non-durable. Fine for this slice (short TTL,
//  re-sign-in is a cheap magic link). A durable, Key Vault-backed shared key ring
//  (.PersistKeysToAzureBlobStorage + .ProtectKeysWithAzureKeyVault) is the
//  billing-entitlements DEPLOYMENT follow-up - see Program.cs's AddDataProtection note.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace QuibbleStone.Api.Accounts;

/// <summary>
/// Mints and resolves the short-lived purchaser sign-in credential (accounts-identity/03
/// + billing-entitlements/05). A singleton over a time-limited data protector scoped to
/// <see cref="Purpose"/>; the ONE shared owner of the credential's purpose + lifetime.
/// </summary>
public sealed class PurchaserCredentialService
{
    /// <summary>The Data Protection purpose string that scopes the purchaser credential.</summary>
    public const string Purpose = "QuibbleStone.PurchaserSession";

    /// <summary>The HttpOnly cookie name the credential is mirrored into for a same-site deployment.</summary>
    public const string CookieName = "qs_purchaser";

    /// <summary>How long a credential stays valid - deliberately short (a toy); re-signing-in is a cheap magic link.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);

    private readonly ITimeLimitedDataProtector _protector;

    /// <summary>Constructs the service over the framework's Data Protection provider (no committed key material, AC-06).</summary>
    public PurchaserCredentialService(IDataProtectionProvider dataProtection)
    {
        _protector = dataProtection.CreateProtector(Purpose).ToTimeLimitedDataProtector();
    }

    /// <summary>
    /// Mints a credential for a signed-in purchaser: protects the email + an issued-at
    /// stamp, expiring after <see cref="Lifetime"/>. The payload carries no PII beyond
    /// the one email and no room / player reference.
    /// </summary>
    /// <param name="purchaserEmail">The verified purchaser email (the magic-link subject).</param>
    /// <returns>The protected, time-limited credential string.</returns>
    public string Protect(string purchaserEmail)
    {
        var payload = $"{purchaserEmail}|{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        return _protector.Protect(payload, Lifetime);
    }

    /// <summary>
    /// Resolves a credential back to its purchaser email, or null if the credential is
    /// absent, tampered, or expired (Unprotect throws, which is caught). This is the
    /// READ side billing-entitlements/05's restore endpoint uses to know who is asking -
    /// the reused guard, not a second auth check (story 05 AC-06).
    /// </summary>
    /// <param name="credential">The credential string (from the cookie or a bearer value).</param>
    /// <returns>The purchaser email, or null when the credential is missing / invalid / expired.</returns>
    public string? ResolvePurchaserEmail(string? credential)
    {
        if (string.IsNullOrEmpty(credential))
        {
            return null;
        }

        try
        {
            // Unprotect enforces expiry (throws past Lifetime) and integrity (throws on tamper).
            var payload = _protector.Unprotect(credential);
            var separator = payload.IndexOf('|');
            var email = separator >= 0 ? payload[..separator] : payload;
            return string.IsNullOrWhiteSpace(email) ? null : email;
        }
        catch (CryptographicException)
        {
            // Invalid / tampered / expired credential - treat as not-signed-in (never throw).
            return null;
        }
    }
}
