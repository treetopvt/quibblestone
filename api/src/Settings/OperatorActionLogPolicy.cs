// ----------------------------------------------------------------------------
//  OperatorActionLogPolicy - the two TRUSTWORTHINESS rules the operator action log
//  enforces regardless of which store backs it (sysadmin-console/06, issue #233;
//  ADR 0003 "The action log is trustworthy dispute insurance").
//
//  These live NEXT TO the IOperatorActionLog seam (declared by control-plane/01,
//  #197) rather than inside any one store, because BOTH the durable
//  TableStorageOperatorActionLog and the in-memory stand-in must honour them
//  identically - a rule that lived in only one store would be a rule the other
//  could quietly break.
//
//  RULE 1 - THE RETENTION FLOOR (AC-04): retention is AGE-based with a HARD FLOOR
//  (MinRetentionDays, a COMPILED constant) that no runtime setting can lower below.
//  A future control-plane/03 knob may only ever RAISE retention above the floor;
//  ClampRetentionDays enforces that at the read site so a bad / hostile configured
//  value can never shorten retention. This is what stops the very party a dispute
//  is about from CONFIG-evicting (lowering the cap) the incriminating row before the
//  dispute surfaces. It is a MINIMUM-RETENTION guarantee, NOT immutability or
//  tamper-evidence (both explicitly out of scope, ADR 0003 Amendment 2).
//
//  RULE 2 - THE EMAIL-SHAPED TARGET CHECK (AC-07, write side): a log row's target is
//  operator-influenced free text. When a target CLAIMS to be an email address (it
//  contains an '@'), it must PARSE as one before the row is written - so a malformed
//  or markup-bearing "email" is rejected at WRITE time, never merely escaped at
//  render time. Non-email targets (a tale slug, a settings key, the literal
//  "stripe-mode") are not subject to the email-format check - only to a plain
//  non-empty / length sanity bound. The paired render-side rule (React default text
//  escaping, never dangerouslySetInnerHTML) lives in ActionLogView.tsx.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Net.Mail;

namespace QuibbleStone.Api.Settings;

/// <summary>
/// The store-independent trustworthiness rules for the operator action log
/// (sysadmin-console/06): the age-based retention floor (AC-04) and the write-side
/// email-shaped-target validation (AC-07). Pure, static, and shared by every
/// <see cref="IOperatorActionLog"/> implementation so neither store can diverge from
/// the other on a rule a dispute depends on.
/// </summary>
public static class OperatorActionLogPolicy
{
    /// <summary>
    /// The HARD retention floor in days (AC-04): a compiled constant no runtime setting
    /// can lower below. Rows younger than this are NEVER pruned - not by a lower configured
    /// value and not by log volume. 180 days (roughly two billing / dispute cycles) is the
    /// minimum a money / moderation dispute needs to still find its row. A future
    /// control-plane/03 knob may RAISE retention above this floor; it may never lower it,
    /// and the floor itself is never a runtime setting. Comment: control-plane/03
    /// knob-migration candidate for RAISING retention only.
    /// </summary>
    public const int MinRetentionDays = 180;

    /// <summary>A defensive upper bound on any target string, so a pathological payload cannot bloat a row.</summary>
    public const int MaxTargetLength = 320; // The RFC 5321 maximum email length; ample for a slug / settings key too.

    /// <summary>
    /// Validates the ACTOR of a row BEFORE it is written: an action-log row must always identify WHO
    /// performed the action (the dispute-insurance contract - a trail with no actor is a degraded
    /// trail). Rejects a null / empty / whitespace-only operator identity and an over-long one. In
    /// practice every operator endpoint runs behind the Operator policy, which always sets a Name
    /// claim, so this never rejects a legitimate action - it is defense-in-depth against a future
    /// auth / pipeline regression that produced a principal with no Name. Returns false rather than
    /// throwing so the caller (the store's AppendAsync) decides how to surface the rejection.
    /// </summary>
    public static bool IsValidOperatorIdentity(string? operatorEmail)
        => !string.IsNullOrWhiteSpace(operatorEmail) && operatorEmail.Length <= MaxTargetLength;

    /// <summary>
    /// Clamps a (possibly null, possibly hostile) configured retention value UP to
    /// <see cref="MinRetentionDays"/> (AC-04). A null / absent override, a zero, or any
    /// value below the floor all resolve to the floor; only a value ABOVE the floor is
    /// honoured as-is. This is the single enforcement point every prune path calls, so
    /// retention can only ever be RAISED above the floor at runtime, never lowered below it.
    /// </summary>
    /// <param name="configuredDays">A retention value from a future settings override, or null when none is configured.</param>
    /// <returns>The effective retention in days - always at least <see cref="MinRetentionDays"/>.</returns>
    public static int ClampRetentionDays(int? configuredDays)
        => configuredDays is int days && days > MinRetentionDays ? days : MinRetentionDays;

    /// <summary>
    /// Validates a log-row target BEFORE it is written (AC-07). Rejects an empty / over-long
    /// target, and - crucially - rejects a target that CLAIMS to be an email (contains '@')
    /// but does not parse as a single well-formed address (a markup-bearing or malformed
    /// "email"). A non-email target (slug / settings key / "stripe-mode") only has to be a
    /// non-empty, length-bounded string. Returns false rather than throwing so the caller
    /// (the store's AppendAsync) decides how to surface the rejection.
    /// </summary>
    public static bool IsValidTarget(string? target)
    {
        if (string.IsNullOrWhiteSpace(target) || target.Length > MaxTargetLength)
        {
            return false;
        }

        // Only targets claiming to be an email get the email-format gate; everything else
        // (a slug, a settings key, "stripe-mode") passes the plain sanity bound above.
        if (!target.Contains('@'))
        {
            return true;
        }

        // MailAddress is strict enough to reject markup / whitespace / multiple addresses.
        // Require the parsed address to round-trip the whole input so "a@b <script>" is out.
        return MailAddress.TryCreate(target, out var parsed) && parsed.Address == target;
    }
}

/// <summary>
/// Thrown by an <see cref="IOperatorActionLog"/> when an action's target fails the write-side
/// validation (AC-07). Because the log is written BEFORE the effectful action (log-before-act,
/// AC-01a), a rejected target ABORTS the action before any effect runs - a malformed / markup-
/// bearing email target can never be persisted, and the effect it would have logged never
/// commits. Derives from <see cref="ArgumentException"/> so a controller may map it to a 400.
/// </summary>
public sealed class InvalidOperatorActionTargetException : ArgumentException
{
    public InvalidOperatorActionTargetException(string message) : base(message) { }
}
