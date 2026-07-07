// ----------------------------------------------------------------------------
//  IOperatorGate - the authorization boundary for operator-only actions
//  (billing-entitlements/06 AC-06). Right now its only consumer is the Stripe
//  mode-toggle endpoint (StripeModeController).
//
//  DEPENDENCY-REALITY / TEMPORARY GATE: the real operator-auth boundary belongs to
//  sysadmin-console/01 (operator login + admin allowlist, #135), which is not built
//  yet (it in turn depends on the unbuilt accounts-identity/02 magic link). Rather
//  than block this feature on two other unbuilt features, the interim implementation
//  below is a single server-side operator SECRET (Key Vault-backed, header-presented).
//  It is deliberately behind this ONE-METHOD interface so that when #135 lands,
//  swapping to the real operator-session check is a one-file change here, not an
//  endpoint rewrite. This mirrors the "temporary, contract-stable stand-in" pattern
//  sysadmin-console/01 itself uses for its dependency on accounts-identity/02.
//
//  The secret is NEVER a VITE_* var, NEVER derived from any player/purchaser data,
//  and NEVER logged. Comparison is constant-time.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;

namespace QuibbleStone.Api.Billing;

/// <summary>
/// Authorizes an operator-only action (billing-entitlements/06 AC-06). One method so the
/// interim secret gate can be swapped for the real sysadmin-console/01 operator session
/// without touching callers.
/// </summary>
public interface IOperatorGate
{
    /// <summary>
    /// True when <paramref name="presentedCredential"/> authorizes the operator action.
    /// A missing/blank credential, or a gate with no operator secret configured, is
    /// never authorized (fail-safe).
    /// </summary>
    Task<bool> IsAuthorizedAsync(string? presentedCredential, CancellationToken ct = default);
}

/// <summary>
/// The INTERIM operator gate (billing-entitlements/06): a constant-time comparison of a
/// header-presented credential against a single Key Vault-backed operator secret
/// (config key <see cref="ConfigKeyName"/>). Temporary, pending sysadmin-console/01
/// (#135) - see the file header. When no secret is configured the gate denies
/// everything, so the toggle endpoint is inert rather than open.
/// </summary>
public sealed class InterimSecretOperatorGate : IOperatorGate
{
    /// <summary>The configuration key the operator secret binds from (Key Vault-backed, never a VITE_* var).</summary>
    public const string ConfigKeyName = "Admin:ModeToggleSecret";

    private readonly byte[]? _secretBytes;

    /// <summary>Constructs the gate over the configured operator secret (null/empty => the gate denies all).</summary>
    public InterimSecretOperatorGate(string? configuredSecret)
    {
        _secretBytes = string.IsNullOrEmpty(configuredSecret) ? null : Encoding.UTF8.GetBytes(configuredSecret);
    }

    /// <inheritdoc />
    public Task<bool> IsAuthorizedAsync(string? presentedCredential, CancellationToken ct = default)
    {
        // No secret configured => deny (fail-safe): the endpoint is inert, never open.
        if (_secretBytes is null || string.IsNullOrEmpty(presentedCredential))
        {
            return Task.FromResult(false);
        }

        var presented = Encoding.UTF8.GetBytes(presentedCredential);
        // FixedTimeEquals compares EQUAL-LENGTH inputs in constant time; a length mismatch
        // returns false quickly (so the secret's length is not fully hidden - acceptable
        // here, as the length of a random operator secret is not a meaningful secret and
        // this is a non-player operator surface). It never early-outs on content.
        return Task.FromResult(CryptographicOperations.FixedTimeEquals(presented, _secretBytes));
    }
}
