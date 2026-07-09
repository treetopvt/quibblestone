// ----------------------------------------------------------------------------
//  OperatorScopeTests - the scope-resolution + scope-policy tests
//  (sysadmin-console/05, issue #214, AC-06). Two layers are pinned here:
//
//    1. ConfigurationOperatorAllowlist.ScopesFor: the per-operator scope set is
//       resolved from the NEW Operator:Scopes config key, INDEX-ALIGNED with
//       Operator:AllowedEmails, in BOTH shapes (the indexed array used locally / in
//       tests, and the single delimited scalar one Key Vault secret takes). An
//       operator with NO entry defaults to all three scopes (the zero-config no-op
//       that keeps today's single operator unrestricted, AC-05); a restricted entry
//       resolves to exactly its subset; a non-operator resolves to the empty set.
//
//    2. OperatorScopeHandler: the requirement a scope policy carries is SUCCEEDED for
//       an operator that holds the scope and NOT succeeded (a 403) for one restricted
//       to a different scope - so "an operator with a configured 'support' entry is
//       rejected by a policy requiring Content or Ops and accepted by one requiring
//       Support" (AC-06), proving the config format is a REAL boundary, not theater.
//
//  The existing controller test suites (AdminEntitlementsControllerTests,
//  ReportedTalesControllerTests, StripeModeControllerTests) are DELIBERATELY left
//  unmodified (AC-05) - their single operator has no Operator:Scopes entry, so every
//  scope requirement succeeds and their behavior is unchanged. This file is the ONLY
//  new test surface the scope work adds.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using QuibbleStone.Api.Admin;

namespace QuibbleStone.Api.Tests.Admin;

public sealed class OperatorScopeTests
{
    private const string AllScopes = "support+content+ops";

    // ---- ScopesFor: the config-backed scope resolution (AC-06) -------------------

    [Fact]
    public void A_non_operator_resolves_to_no_scopes()
    {
        var allowlist = BuildArray(
            emails: ["ops@quibblestone.com"],
            scopes: []);

        // Membership fail-closed: a stranger holds no scopes at all, so no scope check
        // ever runs ahead of the membership check.
        Assert.Empty(allowlist.ScopesFor("stranger@example.com"));
        Assert.Empty(allowlist.ScopesFor(null));
        Assert.Empty(allowlist.ScopesFor(""));
    }

    [Fact]
    public void An_operator_with_no_scopes_entry_defaults_to_all_three()
    {
        // No Operator:Scopes key at all - the zero-config posture that keeps today's
        // single operator unrestricted (AC-05).
        var allowlist = BuildArray(
            emails: ["ops@quibblestone.com"],
            scopes: []);

        AssertAllThree(allowlist.ScopesFor("ops@quibblestone.com"));
    }

    [Fact]
    public void An_operator_missing_from_a_shorter_scopes_array_defaults_to_all_three()
    {
        // Two operators, only the first has a scope entry - the second's index is past
        // the end of the scopes array and defaults to all three.
        var allowlist = BuildArray(
            emails: ["first@quibblestone.com", "second@quibblestone.com"],
            scopes: ["support"]);

        Assert.Equal(new[] { OperatorScope.Support }, allowlist.ScopesFor("first@quibblestone.com").OrderBy(s => s));
        AssertAllThree(allowlist.ScopesFor("second@quibblestone.com"));
    }

    [Fact]
    public void The_all_shorthand_resolves_to_all_three()
    {
        var allowlist = BuildArray(
            emails: ["ops@quibblestone.com"],
            scopes: ["all"]);

        AssertAllThree(allowlist.ScopesFor("ops@quibblestone.com"));
    }

    [Fact]
    public void An_unparseable_entry_defaults_to_all_three()
    {
        // Fail-open on WIDTH: a garbage entry never silently locks an operator out.
        var allowlist = BuildArray(
            emails: ["ops@quibblestone.com"],
            scopes: ["nonsense"]);

        AssertAllThree(allowlist.ScopesFor("ops@quibblestone.com"));
    }

    [Fact]
    public void Array_shape_restricts_an_operator_to_a_subset()
    {
        // The ARRAY config shape (local / appsettings / tests): a comma-delimited list,
        // index-aligned with the emails array.
        var allowlist = BuildArray(
            emails: ["all@quibblestone.com", "support-only@quibblestone.com", "content-ops@quibblestone.com"],
            scopes: ["all", "support", "content,ops"]);

        AssertAllThree(allowlist.ScopesFor("all@quibblestone.com"));
        Assert.Equal(
            new[] { OperatorScope.Support },
            allowlist.ScopesFor("support-only@quibblestone.com").OrderBy(s => s));
        Assert.Equal(
            new[] { OperatorScope.Content, OperatorScope.Ops },
            allowlist.ScopesFor("content-ops@quibblestone.com").OrderBy(s => s));
    }

    [Fact]
    public void Scalar_shape_restricts_an_operator_to_a_subset()
    {
        // The single delimited SCALAR shape (one Key Vault secret): ";"-separated
        // positions, each "+"-joined, positionally aligned with the ";"-separated emails
        // scalar. This is the shape a real deployment uses.
        var allowlist = BuildScalar(
            emails: "all@quibblestone.com; support-only@quibblestone.com; content-ops@quibblestone.com",
            scopes: $"{AllScopes};support;content+ops");

        AssertAllThree(allowlist.ScopesFor("all@quibblestone.com"));
        Assert.Equal(
            new[] { OperatorScope.Support },
            allowlist.ScopesFor("support-only@quibblestone.com").OrderBy(s => s));
        Assert.Equal(
            new[] { OperatorScope.Content, OperatorScope.Ops },
            allowlist.ScopesFor("content-ops@quibblestone.com").OrderBy(s => s));
    }

    [Fact]
    public void Scope_resolution_is_case_and_whitespace_insensitive_on_the_email()
    {
        var allowlist = BuildArray(
            emails: ["Ops@Quibblestone.com"],
            scopes: ["support"]);

        Assert.Equal(
            new[] { OperatorScope.Support },
            allowlist.ScopesFor("  OPS@quibblestone.com ").OrderBy(s => s));
    }

    // ---- OperatorScopeHandler: the policy layer honors the subset (AC-06) --------

    [Fact]
    public async Task A_support_only_operator_is_accepted_by_the_support_policy()
    {
        var allowlist = BuildArray(
            emails: ["support-only@quibblestone.com"],
            scopes: ["support"]);

        var succeeded = await EvaluateAsync(allowlist, "support-only@quibblestone.com", OperatorScope.Support);

        Assert.True(succeeded);
    }

    [Fact]
    public async Task A_support_only_operator_is_rejected_by_the_content_and_ops_policies()
    {
        var allowlist = BuildArray(
            emails: ["support-only@quibblestone.com"],
            scopes: ["support"]);

        Assert.False(await EvaluateAsync(allowlist, "support-only@quibblestone.com", OperatorScope.Content));
        Assert.False(await EvaluateAsync(allowlist, "support-only@quibblestone.com", OperatorScope.Ops));
    }

    [Fact]
    public async Task An_unconfigured_operator_is_accepted_by_every_scope_policy()
    {
        // The AC-05 proof at the policy layer: today's un-configured operator satisfies
        // Support, Content, AND Ops - so no existing scoped-controller test changes.
        var allowlist = BuildArray(
            emails: ["ops@quibblestone.com"],
            scopes: []);

        Assert.True(await EvaluateAsync(allowlist, "ops@quibblestone.com", OperatorScope.Support));
        Assert.True(await EvaluateAsync(allowlist, "ops@quibblestone.com", OperatorScope.Content));
        Assert.True(await EvaluateAsync(allowlist, "ops@quibblestone.com", OperatorScope.Ops));
    }

    // ---- helpers ----------------------------------------------------------------

    /// <summary>Runs the real OperatorScopeHandler against a principal carrying the operator email.</summary>
    private static async Task<bool> EvaluateAsync(IOperatorAllowlist allowlist, string email, OperatorScope scope)
    {
        var handler = new OperatorScopeHandler(allowlist);
        var requirement = new OperatorScopeRequirement(scope);
        // The operator scheme emits exactly one claim: the email as the Name claim.
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, email) }, "Operator");
        var user = new ClaimsPrincipal(identity);
        var context = new AuthorizationHandlerContext(new[] { requirement }, user, resource: null);

        await handler.HandleAsync(context);
        return context.HasSucceeded;
    }

    private static IOperatorAllowlist BuildArray(string[] emails, string[] scopes)
    {
        var values = new Dictionary<string, string?>();
        for (var i = 0; i < emails.Length; i++)
        {
            values[$"{ConfigurationOperatorAllowlist.ConfigKey}:{i}"] = emails[i];
        }

        for (var i = 0; i < scopes.Length; i++)
        {
            values[$"{ConfigurationOperatorAllowlist.ScopesConfigKey}:{i}"] = scopes[i];
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new ConfigurationOperatorAllowlist(configuration);
    }

    private static IOperatorAllowlist BuildScalar(string emails, string scopes)
    {
        var values = new Dictionary<string, string?>
        {
            [ConfigurationOperatorAllowlist.ConfigKey] = emails,
            [ConfigurationOperatorAllowlist.ScopesConfigKey] = scopes,
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new ConfigurationOperatorAllowlist(configuration);
    }

    private static void AssertAllThree(IReadOnlySet<OperatorScope> scopes)
    {
        Assert.Equal(
            new[] { OperatorScope.Support, OperatorScope.Content, OperatorScope.Ops },
            scopes.OrderBy(s => s));
    }
}
