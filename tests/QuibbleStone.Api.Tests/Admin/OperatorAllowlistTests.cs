// ----------------------------------------------------------------------------
//  OperatorAllowlistTests - the config-backed operator allowlist
//  (sysadmin-console/01, issue #135). Pins the AC-05 / AC-02 guarantees that are
//  worth locking in:
//    - Membership comes from configuration (Operator:AllowedEmails) - adding /
//      removing an operator is a config change, reflected live (AC-05).
//    - The check normalizes IDENTICALLY to the account store (trim + invariant
//      lowercase), so case / whitespace variants resolve to the one operator.
//    - Fail closed (AC-02): a null / empty / non-listed candidate, an unconfigured
//      key, an empty list, and a stray blank config entry all resolve to false.
//    - The operator emails are ONLY ever read from server config, never inferred.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using QuibbleStone.Api.Admin;

namespace QuibbleStone.Api.Tests.Admin;

public sealed class OperatorAllowlistTests
{
    private static IOperatorAllowlist Build(params string[] operators)
    {
        var values = new Dictionary<string, string?>();
        for (var i = 0; i < operators.Length; i++)
        {
            values[$"{ConfigurationOperatorAllowlist.ConfigKey}:{i}"] = operators[i];
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new ConfigurationOperatorAllowlist(configuration);
    }

    [Fact]
    public void A_configured_email_is_an_operator()
    {
        var allowlist = Build("ops@quibblestone.com");
        Assert.True(allowlist.IsOperator("ops@quibblestone.com"));
    }

    [Fact]
    public void Membership_is_case_and_whitespace_insensitive()
    {
        var allowlist = Build("Ops@Quibblestone.com");
        Assert.True(allowlist.IsOperator("  ops@quibblestone.com "));
        Assert.True(allowlist.IsOperator("OPS@QUIBBLESTONE.COM"));
    }

    [Fact]
    public void A_non_listed_email_is_not_an_operator()
    {
        var allowlist = Build("ops@quibblestone.com");
        Assert.False(allowlist.IsOperator("someone-else@example.com"));
    }

    [Fact]
    public void Fails_closed_for_null_empty_and_no_config()
    {
        var populated = Build("ops@quibblestone.com");
        Assert.False(populated.IsOperator(null));
        Assert.False(populated.IsOperator(""));
        Assert.False(populated.IsOperator("   "));

        // No key configured at all -> nobody is an operator.
        var empty = new ConfigurationOperatorAllowlist(new ConfigurationBuilder().Build());
        Assert.False(empty.IsOperator("ops@quibblestone.com"));
    }

    [Fact]
    public void A_blank_config_entry_authorizes_no_one()
    {
        // A stray empty slot must never match a blank candidate (both fail closed).
        var allowlist = Build("", "   ");
        Assert.False(allowlist.IsOperator(""));
        Assert.False(allowlist.IsOperator("   "));
        Assert.False(allowlist.IsOperator("ops@quibblestone.com"));
    }

    [Fact]
    public void Multiple_operators_are_all_recognized()
    {
        var allowlist = Build("a@quibblestone.com", "b@quibblestone.com");
        Assert.True(allowlist.IsOperator("a@quibblestone.com"));
        Assert.True(allowlist.IsOperator("b@quibblestone.com"));
        Assert.False(allowlist.IsOperator("c@quibblestone.com"));
    }
}
