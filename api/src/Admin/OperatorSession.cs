// ----------------------------------------------------------------------------
//  OperatorSession - the SHARED constants + credential (un)protection for the
//  operator back-office session (sysadmin-console/01, issue #135).
//
//  THE LOAD-BEARING SEPARATION (AC-03): the operator session credential is minted
//  under a DEDICATED Data Protection purpose string (OperatorSessionPurpose) that
//  is DISTINCT from AccountsController.PurchaserSessionPurpose. Data Protection
//  derives a different key per purpose, so a payload protected for the purchaser
//  purpose CANNOT be unprotected for the operator purpose (and vice versa). That is
//  what makes `purchaser == admin` STRUCTURALLY impossible here: a purchaser
//  credential presented to an admin endpoint fails to unprotect and never even
//  reaches the allowlist check - it is rejected by construction, not by a runtime
//  string compare that could be forgotten. This helper is the ONE place the
//  operator purpose, the payload shape, and the (un)protect calls live, so the
//  login controller (which mints) and the authentication handler (which reads) can
//  never drift apart.
//
//  WHAT THE CREDENTIAL CARRIES (AC-07): only the operator's own normalized email
//  and an issued-at stamp - no name, no player / room / session cross-reference,
//  no purchaser link. It is short-lived (re-signing-in is a cheap magic link) and
//  time-limited by Data Protection itself, so a leaked bearer is not a lasting key.
//
//  NO HAND-ROLLED CRYPTO / NO SECRET IN SOURCE (AC-05): protection uses the
//  framework's ITimeLimitedDataProtector (AddDataProtection is already registered);
//  the key material is framework-managed and is NEVER a committed literal and NEVER
//  a VITE_* var. The credential is NEVER logged.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.DataProtection;

namespace QuibbleStone.Api.Admin;

/// <summary>
/// Shared names + credential (un)protection for the operator back-office session
/// (sysadmin-console/01). The credential is protected under
/// <see cref="OperatorSessionPurpose"/> - DISTINCT from the purchaser session
/// purpose - so a purchaser credential can never be unprotected as an operator one
/// (AC-03). Carries only the operator email + issued-at (AC-07); short-lived and
/// time-limited by Data Protection (AC-05). Used by the login controller (mint) and
/// the authentication handler (read).
/// </summary>
public static class OperatorSession
{
    /// <summary>
    /// The Data Protection purpose string scoping the operator session credential.
    /// DELIBERATELY DISTINCT from AccountsController.PurchaserSessionPurpose so the
    /// two credential families derive different keys and can NEVER be interchanged
    /// (AC-03, the structural `purchaser != admin` guarantee).
    /// </summary>
    public const string OperatorSessionPurpose = "QuibbleStone.OperatorSession";

    /// <summary>The authentication scheme name the admin authorization policy binds to.</summary>
    public const string AuthenticationScheme = "Operator";

    /// <summary>
    /// The authorization policy name admin endpoints require via
    /// [Authorize(Policy = OperatorSession.PolicyName)] - NEVER bare [Authorize],
    /// which would accept any authenticated principal.
    /// </summary>
    public const string PolicyName = "Operator";

    /// <summary>The HttpOnly cookie name mirroring the operator credential for a same-site deployment.</summary>
    public const string CookieName = "qs_operator";

    /// <summary>
    /// How long an operator session credential stays valid. Deliberately SHORT (a
    /// back office is high-trust and used in bursts): long enough for an operator to
    /// do a task, short enough that a leaked bearer expires quickly. Re-signing-in is
    /// a cheap magic link.
    /// </summary>
    public static readonly TimeSpan CredentialLifetime = TimeSpan.FromHours(2);

    /// <summary>
    /// Protects an operator session payload (normalized email + issued-at) with a
    /// time-limited protector scoped to <see cref="OperatorSessionPurpose"/>,
    /// expiring after <see cref="CredentialLifetime"/>. Returns the opaque bearer
    /// value. The key is framework-managed - never a committed literal (AC-05).
    /// </summary>
    public static string Protect(IDataProtectionProvider dataProtection, string operatorEmail)
    {
        var protector = dataProtection.CreateProtector(OperatorSessionPurpose).ToTimeLimitedDataProtector();
        var payload = $"{operatorEmail}|{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        return protector.Protect(payload, CredentialLifetime);
    }

    /// <summary>
    /// Attempts to unprotect an operator credential under
    /// <see cref="OperatorSessionPurpose"/>, recovering the operator email. Returns
    /// false (never throws) on ANY tampering, wrong-purpose payload (e.g. a
    /// purchaser credential, AC-03), expiry, or garbage, with
    /// <paramref name="operatorEmail"/> set to empty in that case. A wrong-purpose
    /// or expired credential is rejected here BEFORE the allowlist is ever consulted.
    /// </summary>
    public static bool TryUnprotect(IDataProtectionProvider dataProtection, string? credential, out string operatorEmail)
    {
        operatorEmail = string.Empty;
        if (string.IsNullOrEmpty(credential))
        {
            return false;
        }

        try
        {
            var protector = dataProtection.CreateProtector(OperatorSessionPurpose).ToTimeLimitedDataProtector();
            var payload = protector.Unprotect(credential);
            // Payload is "email|issuedAtMs"; the email is everything before the last
            // separator (an email never contains '|', so a simple split is safe).
            var separator = payload.LastIndexOf('|');
            var email = separator >= 0 ? payload[..separator] : payload;
            if (email.Length == 0)
            {
                return false;
            }

            operatorEmail = email;
            return true;
        }
        catch
        {
            // A purchaser credential (different purpose), a tampered / expired / junk
            // value - all land here and are rejected. Never throw, never log the
            // credential (AC-05).
            return false;
        }
    }
}
