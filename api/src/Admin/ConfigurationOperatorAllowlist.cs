// ----------------------------------------------------------------------------
//  ConfigurationOperatorAllowlist - the config-backed IOperatorAllowlist
//  (sysadmin-console/01, issue #135).
//
//  SOURCE OF TRUTH (AC-05): the operator set is read LIVE from configuration under
//  the Operator:AllowedEmails key, in EITHER of two shapes so one code path serves
//  local dev, the tests, and a Key Vault-backed deployment:
//    - an indexed ARRAY (Operator:AllowedEmails:0, :1, ...) - how appsettings.json,
//      the tests, and per-email App Service settings express it; tried FIRST.
//    - a single DELIMITED SCALAR (Operator:AllowedEmails = "a@x.com; b@y.com") - the
//      shape ONE Key Vault secret takes, since a KV secret is a single string whose
//      NAME cannot carry the ":" / "__" array indexer. Split on ";" or ",". Holding a
//      handful of operators in one secret is the common case; see ReadConfiguredEmails.
//  In a deployed environment that key is an App Service application setting sourced
//  from Key Vault (never a committed literal, never a VITE_* var, never inferred from
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
    /// The configuration key holding the operator emails. Read as EITHER an indexed
    /// array (Operator:AllowedEmails:0, :1, ... - App Service / environment config) OR
    /// a single delimited scalar (Operator:AllowedEmails = "a@x.com; b@y.com") - the
    /// shape ONE Key Vault secret takes, since a KV secret name cannot contain the
    /// ":" / "__" array indexer. Sourced from Key Vault when deployed (AC-05); see
    /// <see cref="ReadConfiguredEmails"/>.
    /// </summary>
    public const string ConfigKey = "Operator:AllowedEmails";

    /// <summary>
    /// The configuration key holding the per-operator SCOPE lists (sysadmin-console/05,
    /// AC-06), read in the SAME dual shape as <see cref="ConfigKey"/> and INDEX-ALIGNED
    /// with it:
    /// <list type="bullet">
    /// <item>an indexed ARRAY (<c>Operator:Scopes:0</c>, <c>:1</c>, ...) - each entry a
    /// comma-delimited scope list (<c>"support,content,ops"</c>) or the shorthand
    /// <c>"all"</c>; entry <c>:n</c> is the scope list for <c>Operator:AllowedEmails:n</c>.</item>
    /// <item>a single DELIMITED SCALAR (one Key Vault secret) - semicolon-separated
    /// POSITIONS aligned with the semicolon-separated <see cref="ConfigKey"/> scalar,
    /// each position's scopes plus-joined (<c>"support+content+ops;support"</c>).</item>
    /// </list>
    /// A MISSING key, a missing index, or an unparseable entry defaults that operator to
    /// ALL THREE scopes (fail-open on WIDTH only - membership stays fail-closed). So an
    /// operator with no entry is unrestricted (today's zero-config no-op), and adding a
    /// restricted future entry is one config value, never a schema change. See
    /// <see cref="ScopesFor"/>.
    /// <para>
    /// SCALAR ALIGNMENT (important when hand-authoring a multi-operator secret): the
    /// scopes scalar aligns to the emails scalar BY POSITION, so BOTH scalars MUST use
    /// the SAME ";" position delimiter - e.g. emails <c>"a@x.com;b@x.com"</c> paired with
    /// scopes <c>"all;support"</c>. The emails scalar ALSO tolerates "," as a legacy
    /// separator, but "," positions have NO counterpart on the scopes side (scopes reserve
    /// "," for the array shape's internal scope list), so a ","-delimited emails scalar
    /// cannot be positionally scope-restricted - author both with ";" (the canonical form)
    /// whenever any entry restricts an operator. A position that cannot be aligned falls
    /// back to the all-three default (fail-open on width), never a silent narrowing.
    /// </para>
    /// </summary>
    public const string ScopesConfigKey = "Operator:Scopes";

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
        foreach (var entry in ReadConfiguredEmails())
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

    /// <inheritdoc />
    public IReadOnlySet<OperatorScope> ScopesFor(string? email)
    {
        var candidate = Normalize(email);
        if (candidate.Length == 0)
        {
            // Fail closed on MEMBERSHIP: a blank candidate is never an operator, so it
            // holds no scopes at all (the scope check never runs ahead of membership).
            return OperatorScopes.None;
        }

        // Find the operator's POSITION in the configured allowlist - scopes are aligned
        // by that index (Operator:Scopes:n is the scope list for AllowedEmails:n). The
        // raw ordered list (blanks preserved for the array shape) is the alignment basis.
        var emails = ReadConfiguredEmails().ToList();
        var index = -1;
        for (var i = 0; i < emails.Count; i++)
        {
            var normalized = Normalize(emails[i]);
            if (normalized.Length > 0 && string.Equals(normalized, candidate, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            // Not an allowlisted operator -> no scopes (membership fail-closed, AC-06).
            return OperatorScopes.None;
        }

        // Read the scope entry at that index. A missing key, a missing index, or an
        // unparseable entry defaults to ALL THREE scopes (fail-open on width, AC-06) -
        // which is why today's single, un-configured operator keeps every scope and
        // behavior is unchanged.
        var scopeEntries = ReadConfiguredScopeEntries();
        var entry = index < scopeEntries.Count ? scopeEntries[index] : null;
        return ParseScopeEntry(entry);
    }

    /// <summary>
    /// Reads the configured operator emails, supporting BOTH config shapes so one
    /// code path serves local dev, the tests, AND a Key Vault-backed deployment:
    /// <list type="bullet">
    /// <item>an indexed ARRAY (<c>Operator:AllowedEmails:0</c>, <c>:1</c>, ...) - how
    /// appsettings.json and the tests express it, and how per-email App Service
    /// settings bind; tried FIRST so a real array is used verbatim.</item>
    /// <item>a single DELIMITED SCALAR (<c>Operator:AllowedEmails = "a@x.com;
    /// b@y.com"</c>) - the shape ONE Key Vault secret takes, because a KV secret is a
    /// single string whose NAME cannot carry the ":" / "__" array indexer. Used only
    /// as a fallback when no array is bound; split on ";" or ",".</item>
    /// </list>
    /// Returns an empty sequence when nothing is configured (fail closed, AC-02);
    /// per-entry blank / whitespace dropping stays in <see cref="IsOperator"/>.
    /// </summary>
    private IEnumerable<string> ReadConfiguredEmails()
    {
        // Array shape first: a bound, non-empty string[] is used exactly as given.
        var array = _configuration.GetSection(ConfigKey).Get<string[]>();
        if (array is { Length: > 0 })
        {
            return array;
        }

        // Fallback: a single delimited scalar (one Key Vault secret). Split on ";" or
        // "," and trim; an unset / whitespace-only value yields nothing (fail closed).
        var scalar = _configuration[ConfigKey];
        if (string.IsNullOrWhiteSpace(scalar))
        {
            return [];
        }

        return scalar.Split(
            [';', ','],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// Reads the per-operator scope ENTRIES positionally (sysadmin-console/05, AC-06),
    /// supporting the SAME dual shape as the emails so one code path serves local dev,
    /// tests, and a Key Vault-backed deployment. Position <c>n</c> of the returned list
    /// is the raw scope entry for the operator at index <c>n</c> of
    /// <see cref="ReadConfiguredEmails"/> (a null / absent position -> the all-three
    /// default in <see cref="ParseScopeEntry"/>):
    /// <list type="bullet">
    /// <item>Array shape (<c>Operator:Scopes:0</c>, <c>:1</c>, ...): used verbatim, one
    /// entry per index.</item>
    /// <item>Scalar shape (one Key Vault secret): split on ";" ONLY, WITHOUT dropping
    /// empties, so a blank position is preserved and still aligns by index (an empty
    /// position defaults to all three). Internal scope joining (","/"+") is handled by
    /// <see cref="ParseScopeEntry"/>.</item>
    /// </list>
    /// Returns an empty list when the key is unset - every operator then defaults to all
    /// three scopes, the zero-config no-op.
    /// </summary>
    private IReadOnlyList<string?> ReadConfiguredScopeEntries()
    {
        // Array shape first, mirroring ReadConfiguredEmails so the two align by index.
        var array = _configuration.GetSection(ScopesConfigKey).Get<string[]>();
        if (array is { Length: > 0 })
        {
            return array;
        }

        // Fallback: a single delimited scalar (one Key Vault secret). Split on ";" only
        // and DO NOT remove empty entries - positions must stay aligned with the emails
        // scalar, and an empty position simply falls back to the all-three default.
        var scalar = _configuration[ScopesConfigKey];
        if (string.IsNullOrWhiteSpace(scalar))
        {
            return [];
        }

        return scalar.Split(';');
    }

    /// <summary>
    /// Parses one raw scope entry into a concrete scope set (sysadmin-console/05, AC-06).
    /// Tolerant of BOTH the array shape's comma delimiter and the scalar shape's plus
    /// delimiter (split on either). The rules, all defaulting toward ALL THREE (fail-open
    /// on width, never accidentally narrowing an operator to nothing):
    /// <list type="bullet">
    /// <item>null / blank / no recognizable tokens -> all three (the unconfigured /
    /// unparseable default).</item>
    /// <item>any token equal to "all" (case-insensitive) -> all three.</item>
    /// <item>otherwise the set of recognized tokens; if AT LEAST ONE token is a valid
    /// scope that restricted subset is honored (e.g. "support" -> just Support), and any
    /// unrecognized tokens are ignored.</item>
    /// </list>
    /// </summary>
    private static IReadOnlySet<OperatorScope> ParseScopeEntry(string? entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            return OperatorScopes.All;
        }

        var tokens = entry.Split(
            [',', '+'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var scopes = new HashSet<OperatorScope>();
        foreach (var token in tokens)
        {
            // The "all" shorthand short-circuits to every scope regardless of siblings.
            if (string.Equals(token, "all", StringComparison.OrdinalIgnoreCase))
            {
                return OperatorScopes.All;
            }

            if (OperatorScopes.TryParse(token, out var scope))
            {
                scopes.Add(scope);
            }
        }

        // A fully-unparseable entry (no recognized token) defaults to all three rather
        // than silently locking the operator out (fail-open on width, AC-06).
        return scopes.Count > 0 ? scopes : OperatorScopes.All;
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
