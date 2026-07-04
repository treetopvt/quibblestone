// ----------------------------------------------------------------------------
//  IOperatorAllowlist - the SEPARATE, config-backed operator allowlist that
//  decides who may hold an operator (back-office) session (sysadmin-console/01,
//  issue #135).
//
//  WHY THIS EXISTS (and why it is a distinct contract, not a reuse of
//  IAccountStore): the purchaser account store answers "has this email bought
//  QuibbleStone?" - a customer question. This allowlist answers a COMPLETELY
//  DIFFERENT question: "is this email a trusted operator of the service?" The two
//  MUST never be conflated - a purchaser is not an operator (AC-03, the
//  load-bearing `purchaser == admin` bug this whole feature exists to prevent). So
//  operator membership lives in its OWN contract, sourced from OPERATOR-ONLY
//  configuration (App Service config backed by Key Vault, AC-05), and is NEVER
//  inferred from player, room, session, or purchaser data.
//
//  WHERE THE CHECK HAPPENS (AC-02, load-bearing): membership is consulted at
//  VERIFY time (and again at every authenticated request), NEVER at token-issue
//  time. Possessing a valid magic link alone therefore never grants operator
//  scope - the link only proves control of an inbox; THIS allowlist is the gate
//  that turns a verified inbox into an operator session, and only for an
//  allowlisted email.
//
//  CONFIG, NOT CODE (AC-05): the membership set is held in configuration
//  (Operator:AllowedEmails), so adding or removing an operator is a config change
//  (a Key Vault / App Service app-setting edit), never a code change and never a
//  redeploy of source. The emails are NEVER a VITE_* var (they must not ship to
//  any browser) and are NEVER committed to source.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Admin;

/// <summary>
/// Answers the single operator-authorization question "is this email an
/// allowlisted operator?" (sysadmin-console/01). Deliberately SEPARATE from the
/// purchaser account store so a purchaser credential can never satisfy an operator
/// check (AC-03). The check is consulted at magic-link VERIFY time and at every
/// authenticated admin request, never at token-issue time (AC-02). Membership is
/// held in operator-only configuration (Key Vault-backed when deployed), so
/// changing the operator set is a config change, not a code change (AC-05).
/// </summary>
public interface IOperatorAllowlist
{
    /// <summary>
    /// True when <paramref name="email"/> (after the SAME trim + invariant-lowercase
    /// normalization the account store uses) is a configured operator. A null,
    /// empty, or non-allowlisted email returns false - the fail-closed default, so
    /// an unknown email never resolves to operator scope (AC-02). Never throws.
    /// </summary>
    /// <param name="email">The candidate operator email (raw; normalized here).</param>
    /// <returns>True only for an allowlisted operator email; false otherwise.</returns>
    bool IsOperator(string? email);
}
