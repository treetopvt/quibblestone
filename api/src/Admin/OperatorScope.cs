// ----------------------------------------------------------------------------
//  OperatorScope - the SCOPE metadata every admin endpoint carries
//  (sysadmin-console/05, issue #214; ADR 0003 Layer 3 "Scoped authz now, RBAC
//  later").
//
//  WHY THIS EXISTS: ADR 0003 asks every admin endpoint to declare WHICH operator
//  JOB it belongs to - Support (find a person, fix their problem), Content
//  (moderation), or Operations (settings / flags / Stripe mode) - so that a FUTURE
//  restricted moderator is an allowlist entry with a scope subset, NOT a rework of
//  every controller. TODAY there is a single operator who holds ALL THREE scopes
//  (see ConfigurationOperatorAllowlist.ScopesFor's all-three default), so this is
//  PURE METADATA with ZERO behavior change: the existing controller test suites pass
//  unmodified (AC-05). The scope becomes a real boundary the moment an
//  Operator:Scopes entry restricts an email (AC-06).
//
//  THE THREE PIECES IN THIS FILE:
//    - OperatorScope: the closed enum of the three jobs, with stable wire strings.
//    - OperatorScopes: parsing / formatting helpers + the canonical all-three and
//      empty sets (the all-three default is what keeps today's operator a no-op).
//    - OperatorScopePolicy: the three named authorization-policy strings admin
//      controllers require via [Authorize(Policy = ...)], registered in Program.cs.
//    - OperatorScopeRequirement: the IAuthorizationRequirement each named policy
//      carries; OperatorScopeHandler (separate file) evaluates it against the
//      operator's resolved scope set.
//
//  NOT A ROLE SYSTEM (AC-07): there is no role hierarchy, no per-operator scope
//  editor, no UI - this is authorization plumbing only. Role management stays parked
//  in feature.md.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Collections.Frozen;
using Microsoft.AspNetCore.Authorization;

namespace QuibbleStone.Api.Admin;

/// <summary>
/// The three operator JOBS an admin endpoint can belong to (sysadmin-console/05).
/// Support = purchaser lookup + grant/revoke; Content = moderation (the reported-tales
/// queue); Ops = settings / flags / Stripe mode. A closed enum: a future fourth scope
/// is a deliberate change here, not a free-form string.
/// </summary>
public enum OperatorScope
{
    /// <summary>Find a person, fix their problem: purchaser lookup + grant/revoke.</summary>
    Support,

    /// <summary>Moderation: the reported-tales review queue (confirm / restore).</summary>
    Content,

    /// <summary>Operations: runtime settings / flags and the Stripe live/test mode.</summary>
    Ops,
}

/// <summary>
/// Parsing / formatting helpers plus the canonical scope sets for
/// <see cref="OperatorScope"/> (sysadmin-console/05). The wire strings ("support" /
/// "content" / "ops") are the tokens the Operator:Scopes config uses (AC-06) - stable,
/// lowercase, and never localized.
/// </summary>
public static class OperatorScopes
{
    // FROZEN, not a plain HashSet: these are shared, authorization-critical statics, so
    // they are truly immutable - a cast to ISet + Add throws rather than silently
    // mutating the set every scope decision reads. FrozenSet also gives the fastest
    // Contains for a tiny read-only set (the exact access pattern the handler uses).

    /// <summary>The all-three scope set - the backward-compatible default for an operator with no Operator:Scopes entry (AC-06).</summary>
    public static readonly IReadOnlySet<OperatorScope> All =
        new[] { OperatorScope.Support, OperatorScope.Content, OperatorScope.Ops }.ToFrozenSet();

    /// <summary>The empty scope set - what a NON-operator resolves to (the scope check never runs ahead of the membership check).</summary>
    public static readonly IReadOnlySet<OperatorScope> None = FrozenSet<OperatorScope>.Empty;

    /// <summary>The stable, lowercase wire token for a scope (used by config parsing and any diagnostics).</summary>
    public static string ToWire(OperatorScope scope) => scope switch
    {
        OperatorScope.Support => "support",
        OperatorScope.Content => "content",
        OperatorScope.Ops => "ops",
        _ => scope.ToString().ToLowerInvariant(),
    };

    /// <summary>
    /// Parses a single scope token (case / whitespace insensitive) to an
    /// <see cref="OperatorScope"/>. Returns false for null / blank / unknown tokens so
    /// an unrecognized token is simply ignored by the caller (never a throw). The
    /// shorthand "all" is NOT a scope token - callers handle it separately as "every
    /// scope" before reaching here.
    /// </summary>
    public static bool TryParse(string? token, out OperatorScope scope)
    {
        scope = default;
        var normalized = (token ?? string.Empty).Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "support":
                scope = OperatorScope.Support;
                return true;
            case "content":
                scope = OperatorScope.Content;
                return true;
            case "ops":
                scope = OperatorScope.Ops;
                return true;
            default:
                return false;
        }
    }
}

/// <summary>
/// The named authorization-policy strings admin controllers require via
/// [Authorize(Policy = OperatorScopePolicy.Support)] etc. (sysadmin-console/05).
/// Each policy pins the Operator authentication scheme AND adds an
/// <see cref="OperatorScopeRequirement"/> for its scope, so it enforces BOTH "is a
/// valid operator credential" (the story 01 boundary) AND "holds this scope" - a
/// de-scoped future operator is rejected at the policy layer, not by convention.
/// Registered in Program.cs alongside the base <see cref="OperatorSession.PolicyName"/>.
/// </summary>
public static class OperatorScopePolicy
{
    /// <summary>Policy requiring the Support scope (AdminEntitlementsController).</summary>
    public const string Support = "Operator:Support";

    /// <summary>Policy requiring the Content scope (ReportedTalesController).</summary>
    public const string Content = "Operator:Content";

    /// <summary>Policy requiring the Ops scope (StripeModeController).</summary>
    public const string Ops = "Operator:Ops";

    /// <summary>The named policy string that requires <paramref name="scope"/>.</summary>
    public static string For(OperatorScope scope) => scope switch
    {
        OperatorScope.Support => Support,
        OperatorScope.Content => Content,
        OperatorScope.Ops => Ops,
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown operator scope."),
    };
}

/// <summary>
/// The authorization requirement carried by each scope policy (sysadmin-console/05):
/// "the authenticated operator must hold <see cref="Scope"/>". Evaluated by
/// <see cref="OperatorScopeHandler"/>, which resolves the operator's scope set from
/// <see cref="IOperatorAllowlist.ScopesFor"/>. The base membership + credential check
/// still comes from the Operator scheme the policy also pins (AC-05).
/// </summary>
public sealed class OperatorScopeRequirement : IAuthorizationRequirement
{
    public OperatorScopeRequirement(OperatorScope scope) => Scope = scope;

    /// <summary>The scope the endpoint requires.</summary>
    public OperatorScope Scope { get; }
}
