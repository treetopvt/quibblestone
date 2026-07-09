// ----------------------------------------------------------------------------
//  OperatorScopeHandler - evaluates an OperatorScopeRequirement against the
//  authenticated operator's resolved scope set (sysadmin-console/05, issue #214).
//
//  HOW A SCOPED ENDPOINT AUTHORIZES (AC-05): a scope policy pins the Operator
//  authentication scheme (so only a valid operator credential authenticates at all -
//  the story 01 boundary, unchanged) AND adds an OperatorScopeRequirement. This
//  handler runs ONLY after the request has authenticated as an operator; it reads the
//  operator email from the principal's Name claim (the ONLY claim
//  OperatorAuthenticationHandler emits, AC-07 of story 01) and succeeds when
//  IOperatorAllowlist.ScopesFor(email) contains the required scope.
//
//  ZERO BEHAVIOR CHANGE TODAY (AC-05/AC-06): the current single operator has no
//  Operator:Scopes entry, so ScopesFor resolves them to ALL THREE scopes - every scope
//  requirement therefore succeeds, and the existing controller test suites pass
//  UNMODIFIED. The check only ever REJECTS once a real Operator:Scopes entry restricts
//  an email to a subset - at which point a request for a scope outside that subset
//  fails the requirement (a 403, distinct from the 401 an un-authenticated / non-operator
//  caller already gets from the scheme).
//
//  NO EMAIL ANYWHERE BUT THE CHECK: the handler reads the email only to resolve scopes
//  and never logs it, never attaches it elsewhere, never bridges it to any player /
//  room / session surface (the anonymity firewall - it imports nothing from Rooms/Hubs).
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace QuibbleStone.Api.Admin;

/// <summary>
/// The <see cref="AuthorizationHandler{OperatorScopeRequirement}"/> backing the three
/// scope policies (sysadmin-console/05). Resolves the authenticated operator's scope set
/// via <see cref="IOperatorAllowlist.ScopesFor"/> and succeeds only when it contains the
/// required scope. Registered as a singleton in Program.cs (it holds only the singleton
/// allowlist and is otherwise stateless).
/// </summary>
public sealed class OperatorScopeHandler : AuthorizationHandler<OperatorScopeRequirement>
{
    private readonly IOperatorAllowlist _allowlist;

    public OperatorScopeHandler(IOperatorAllowlist allowlist) => _allowlist = allowlist;

    /// <inheritdoc />
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OperatorScopeRequirement requirement)
    {
        // The email is the principal's Name claim (the only claim the operator scheme
        // emits). If it is somehow absent, or the scope set does not contain the
        // requirement, we simply do NOT call Succeed - authorization then fails (403).
        var email = context.User.FindFirstValue(ClaimTypes.Name);
        if (!string.IsNullOrEmpty(email) && _allowlist.ScopesFor(email).Contains(requirement.Scope))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
