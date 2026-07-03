// ----------------------------------------------------------------------------
//  ConfigurationOperatorAllowlist - the config-backed IOperatorAllowlist
//  (sysadmin-console/01, issue #135).
//
//  SOURCE OF TRUTH (AC-05): the operator set is read LIVE from configuration under
//  the Operator:AllowedEmails key - a string array of operator emails. In a
//  deployed environment that key is an App Service application setting sourced from
//  Key Vault (never a committed literal, never a VITE_* var, never inferred from
//  player / purchaser data). Reading it live (rather than snapshotting at
//  construction) means adding or removing an operator is a pure config change that
//  takes effect on the next request - no code change, no redeploy of source.
//
//  NORMALIZATION (matches the account store, AC deliberately): each configured
//  email and each candidate email is normalized IDENTICALLY to the accounts store
//  (trim + ToLowerInvariant, see AccountIdentity.Normalize) before comparison, so
//  "Ops@Quibble.com" and "  ops@quibble.com " are the same operator regardless of
//  the case / whitespace typed. Blank / whitespace-only configured entries are
//  dropped so a stray empty array slot can never match an empty candidate.
//
//  FAIL CLOSED (AC-02): a null / empty candidate, an unconfigured key, or an
//  empty list all resolve to "not an operator" (false). The default posture is
//  DENY - operator scope is only ever granted to an email explicitly present in
//  the configured allowlist.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.Extensions.Configuration;

namespace QuibbleStone.Api.Admin;

/// <summary>
/// The configuration-backed <see cref="IOperatorAllowlist"/> (sysadmin-console/01).
/// Reads the operator emails LIVE from the Operator:AllowedEmails config key (a
/// Key Vault-backed App Service setting when deployed, AC-05), normalizes them the
/// same way the account store normalizes an identity (trim + invariant-lowercase),
/// and answers membership fail-closed (AC-02). Registered as a singleton in
/// Program.cs; holds only the injected <see cref="IConfiguration"/>.
/// </summary>
public sealed class ConfigurationOperatorAllowlist : IOperatorAllowlist
{
    /// <summary>
    /// The configuration key holding the operator email array. Bound as
    /// Operator:AllowedEmails:0, Operator:AllowedEmails:1, ... in App Service /
    /// environment config, sourced from Key Vault when deployed (AC-05).
    /// </summary>
    public const string ConfigKey = "Operator:AllowedEmails";

    private readonly IConfiguration _configuration;

    public ConfigurationOperatorAllowlist(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <inheritdoc />
    public bool IsOperator(string? email)
    {
        var candidate = Normalize(email);
        if (candidate.Length == 0)
        {
            // Fail closed: a blank candidate is never an operator, and must never
            // match a stray blank config slot (those are dropped below anyway).
            return false;
        }

        // Read LIVE each call so a config change (add / remove an operator) takes
        // effect without a restart (AC-05). The set is tiny (a handful of
        // operators), so rebuilding it per admin request is negligible - and admin
        // traffic is rare compared with the game path this never touches.
        var configured = _configuration.GetSection(ConfigKey).Get<string[]>();
        if (configured is null || configured.Length == 0)
        {
            return false;
        }

        foreach (var entry in configured)
        {
            var normalized = Normalize(entry);
            // Drop blank / whitespace-only entries so an empty config slot can never
            // authorize anyone, and compare on the normalized form (case / space
            // insensitive) - the same equality the account store uses.
            if (normalized.Length > 0 && string.Equals(normalized, candidate, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Normalizes an email to the SAME canonical form the account store uses
    /// (AccountIdentity.Normalize): trimmed of surrounding whitespace and lowercased
    /// with the INVARIANT culture. A null input normalizes to an empty string rather
    /// than throwing. Kept local (a one-liner) so this file needs no reach into the
    /// Accounts namespace - the rule is intentionally identical, not shared code.
    /// </summary>
    private static string Normalize(string? email) =>
        (email ?? string.Empty).Trim().ToLowerInvariant();
}
