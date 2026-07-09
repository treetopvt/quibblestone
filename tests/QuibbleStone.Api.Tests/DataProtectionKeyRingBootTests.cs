// ----------------------------------------------------------------------------
//  DataProtectionKeyRingBootTests - host-boot tests for the durable Data
//  Protection key ring's config-presence + FAIL-CLOSED startup guard
//  (platform-devops/08, AC-02 + AC-08). See Program.cs's AddDataProtection block
//  (around the `dpKeyRingConfigured` branch).
//
//  These boot the REAL app (WebApplicationFactory<Program>), exactly as
//  Admin/OperatorAuthorizationTests does, to prove the guard's actual runtime
//  behavior rather than just its shape:
//
//    - AC-02: Development, with NEITHER DataProtection:KeyRingBlobUri /
//      DataProtection:KeyVaultKeyUri NOR Accounts:StorageConnectionString
//      configured, boots successfully on the framework's default in-process key
//      ring - local dev / CI is unchanged by this story.
//    - AC-08: a NON-Development environment (Production) with the SAME durable
//      config absent refuses to start - the app throws at startup rather than
//      silently falling back to per-instance keys, and the failure names the
//      exact missing configuration so it is actionable.
//
//  WebApplicationFactory wraps a host-build-time exception (it can surface as a
//  TargetInvocationException or similar wrapper depending on the hosting path),
//  so the AC-08 assertion walks the InnerException chain rather than assuming
//  a single exact type, and only requires the message it finds to name the
//  missing DataProtection configuration.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace QuibbleStone.Api.Tests;

public class DataProtectionKeyRingBootTests
{
    // ---- AC-02: local dev / CI is unchanged ------------------------------------

    [Fact]
    public async Task Development_WithNoDurableBackingConfigured_BootsSuccessfully()
    {
        using var factory = new NoDurableBackingFactory(environment: "Development");

        // Accessing Services / creating a client forces the host to fully build; if
        // the fail-closed guard mistakenly fired here, this would throw.
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health");

        // The app came up on the framework's default in-process key ring (AC-02) -
        // proven by a live, working request completing rather than a startup throw.
        Assert.True(response.IsSuccessStatusCode);
    }

    // ---- AC-08: a deployed environment refuses to start without durable backing --

    [Fact]
    public void NonDevelopmentEnvironment_WithNoDurableBackingConfigured_FailsToStart()
    {
        using var factory = new NoDurableBackingFactory(environment: "Production");

        // Force the host to actually build. WebApplicationFactory may surface the
        // startup exception either directly or wrapped (e.g. inside a
        // TargetInvocationException / AggregateException from the hosting
        // internals), so capture whatever is thrown and walk the chain below rather
        // than asserting an exact exception type at the call site.
        var thrown = Record.Exception(() => factory.Server);

        Assert.NotNull(thrown);

        var invalidOperation = FindInvalidOperationException(thrown!);
        Assert.NotNull(invalidOperation);
        Assert.Contains("DataProtection:KeyRingBlobUri", invalidOperation!.Message);
        Assert.Contains("DataProtection:KeyVaultKeyUri", invalidOperation.Message);
        Assert.Contains("Accounts:StorageConnectionString", invalidOperation.Message);
    }

    // ---- helpers ----------------------------------------------------------------

    /// <summary>
    /// Walks an exception's chain (including AggregateException.InnerExceptions)
    /// looking for the InvalidOperationException Program.cs throws when the durable
    /// Data Protection backing is missing outside Development.
    /// </summary>
    private static InvalidOperationException? FindInvalidOperationException(Exception ex)
    {
        var current = ex;
        while (current is not null)
        {
            if (current is InvalidOperationException invalidOperation &&
                invalidOperation.Message.Contains("Durable Data Protection key ring is not configured", StringComparison.Ordinal))
            {
                return invalidOperation;
            }

            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    var found = FindInvalidOperationException(inner);
                    if (found is not null)
                    {
                        return found;
                    }
                }
            }

            current = current.InnerException;
        }

        return null;
    }

    /// <summary>
    /// Boots the real API with NO DataProtection:* config and NO
    /// Accounts:StorageConnectionString - the minimal, precise setup needed so the
    /// ONLY reason a non-Development boot could fail is the AC-08 guard itself (no
    /// other Program.cs branch is environment-gated ahead of it - see the guard's
    /// surrounding comment block). Never sets an environment other than what the
    /// test explicitly asks for.
    /// </summary>
    private sealed class NoDurableBackingFactory(string environment) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, config) =>
            {
                // Force the three durable-backing settings ABSENT - their absence is the
                // whole point of this test. This provider runs AFTER the app's own
                // appsettings / env sources, so setting each key to null here OVERRIDES
                // any value a future appsettings.json or environment variable might
                // introduce, keeping the "missing durable backing" path genuinely
                // exercised rather than silently passing on a stray configured value.
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DataProtection:KeyRingBlobUri"] = null,
                    ["DataProtection:KeyVaultKeyUri"] = null,
                    ["Accounts:StorageConnectionString"] = null,
                });
            });
        }
    }
}
