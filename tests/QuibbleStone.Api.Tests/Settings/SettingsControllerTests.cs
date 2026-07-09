// ----------------------------------------------------------------------------
//  SettingsControllerTests - the OPERATOR runtime-settings admin API (control-plane/01,
//  issue #197). Two layers, mirroring the AdminEntitlementsController test posture:
//
//    1. Direct controller tests over the REAL in-memory store + service + action log,
//       with a ClaimsPrincipal carrying the operator email (as OperatorAuthentication-
//       Handler sets it), so the write path is exercised end to end with ZERO Azure:
//         - AC-02/AC-03: a PUT writes an override readable by the service, stamped with
//           changedBy (the operator) + changedAt.
//         - AC-08: a value that type-parses but falls outside Bounds is a 400, writes no
//           override, logs no row.
//         - AC-09: exactly one action-log row per successful PUT / DELETE; zero on a
//           rejected PUT (bounds / confirmation) and zero on a no-op DELETE.
//         - AC-10: a RequiresConfirmation key without confirm:true is a 400 (no write, no
//           log); with confirm:true AND an in-bounds value the write + log proceed.
//    2. A WebApplicationFactory boundary walk (AC-06): an unauthenticated caller is
//       rejected 401 on GET / PUT / DELETE; an allowlisted operator is accepted, proving
//       a full PUT/GET/DELETE round-trip over HTTP.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuibbleStone.Api.Admin;
using QuibbleStone.Api.Settings;

namespace QuibbleStone.Api.Tests.Settings;

public sealed class SettingsControllerTests
{
    private const string Operator = "ops@quibblestone.com";

    /// <summary>
    /// Builds a controller over a fresh in-memory store + real service + a capturing action log,
    /// with the operator identity on User (as the auth handler sets ClaimTypes.Name), so a PUT /
    /// DELETE is observable through both the service (the override) and the log (the AC-09 row).
    /// </summary>
    private static (SettingsController Controller, IRuntimeSettingsService Settings, InMemoryOperatorActionLog Log) NewSut()
    {
        var settings = new RuntimeSettingsService(new InMemoryRuntimeSettingsStore());
        var log = new InMemoryOperatorActionLog();
        var controller = new SettingsController(settings, log)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.Name, Operator) }, "Operator")),
                },
            },
        };
        return (controller, settings, log);
    }

    // Builds a PUT request body with a JSON value of the given kind (number / bool / string).
    private static UpdateSettingRequest Request(object value, bool? confirm = null)
    {
        var json = JsonSerializer.SerializeToElement(value);
        return new UpdateSettingRequest(json, confirm);
    }

    // ---- AC-02 / AC-03: a PUT writes a stamped override --------------------------

    [Fact]
    public async Task Put_writes_an_override_readable_by_the_service_with_a_stamp()
    {
        var (controller, settings, log) = NewSut();

        var result = await controller.Put(SettingsCatalog.ExampleThreshold, Request(7), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(7, await settings.GetIntAsync(SettingsCatalog.ExampleThreshold));
        var view = await settings.GetViewAsync(SettingsCatalog.ExampleThreshold);
        Assert.Equal(Operator, view!.Override!.ChangedBy); // AC-03: changedBy is the operator
        // AC-09: exactly one row, noting the old -> new value.
        var row = Assert.Single(log.Entries);
        Assert.Equal("settings.put", row.Action);
        Assert.Equal(SettingsCatalog.ExampleThreshold, row.Target);
        Assert.Equal("3 -> 7", row.Note);
    }

    [Fact]
    public async Task Put_accepts_a_json_string_value_for_a_numeric_key()
    {
        var (controller, settings, _) = NewSut();

        // A JSON string "12" coerces to the Int key just as a JSON number 12 would.
        var result = await controller.Put(SettingsCatalog.ExampleThreshold, Request("12"), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(12, await settings.GetIntAsync(SettingsCatalog.ExampleThreshold));
    }

    [Fact]
    public async Task Put_rejects_an_unknown_key()
    {
        var (controller, _, log) = NewSut();

        var result = await controller.Put("no.such.key", Request(1), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(log.Entries);
    }

    [Fact]
    public async Task Put_accepts_a_plain_string_value()
    {
        var (controller, settings, _) = NewSut();

        var result = await controller.Put(SettingsCatalog.ExampleLabel, Request("world"), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("world", await settings.GetStringAsync(SettingsCatalog.ExampleLabel));
    }

    [Fact]
    public async Task Put_rejects_a_json_object_value_even_for_a_string_key()
    {
        var (controller, settings, log) = NewSut();
        // A structural JSON value is not a settings value - it must not persist as raw JSON text
        // under a String key (whose parse always succeeds).
        var obj = JsonSerializer.SerializeToElement(new { nested = "x" });

        var result = await controller.Put(SettingsCatalog.ExampleLabel, new UpdateSettingRequest(obj, null), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Null((await settings.GetViewAsync(SettingsCatalog.ExampleLabel))!.Override);
        Assert.Empty(log.Entries);
    }

    [Fact]
    public async Task Put_rejects_a_json_array_value()
    {
        var (controller, settings, log) = NewSut();
        var arr = JsonSerializer.SerializeToElement(new[] { 1, 2, 3 });

        var result = await controller.Put(SettingsCatalog.ExampleLabel, new UpdateSettingRequest(arr, null), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Null((await settings.GetViewAsync(SettingsCatalog.ExampleLabel))!.Override);
        Assert.Empty(log.Entries);
    }

    [Fact]
    public async Task Put_rejects_a_json_null_value_even_for_a_string_key()
    {
        var (controller, settings, log) = NewSut();
        // A JSON null literal arrives as ValueKind.Null - not a value, a 400.
        var nullEl = JsonSerializer.SerializeToElement<object?>(null);

        var result = await controller.Put(SettingsCatalog.ExampleLabel, new UpdateSettingRequest(nullEl, null), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Null((await settings.GetViewAsync(SettingsCatalog.ExampleLabel))!.Override);
        Assert.Empty(log.Entries);
    }

    [Fact]
    public async Task Put_rejects_a_malformed_value_for_the_declared_type()
    {
        var (controller, settings, log) = NewSut();

        // A non-numeric string under an Int key fails the type parse (AC-08's type gate).
        var result = await controller.Put(SettingsCatalog.ExampleThreshold, Request("not-a-number"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Null((await settings.GetViewAsync(SettingsCatalog.ExampleThreshold))!.Override);
        Assert.Empty(log.Entries);
    }

    // ---- AC-08: bounds, not just type -------------------------------------------

    [Fact]
    public async Task Put_rejects_a_value_that_type_parses_but_is_out_of_bounds()
    {
        var (controller, settings, log) = NewSut();

        // 500 parses as an Int but exceeds the declared Max of 100 - rejected, no write, no row.
        var result = await controller.Put(SettingsCatalog.ExampleThreshold, Request(500), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Null((await settings.GetViewAsync(SettingsCatalog.ExampleThreshold))!.Override);
        Assert.Empty(log.Entries);
    }

    [Fact]
    public async Task Put_rejects_a_value_below_the_declared_min()
    {
        var (controller, settings, log) = NewSut();

        // 0 is below the Min of 1 (the "can never zero a limiter / mass-expire the store" rail).
        var result = await controller.Put(SettingsCatalog.ExampleThreshold, Request(0), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Null((await settings.GetViewAsync(SettingsCatalog.ExampleThreshold))!.Override);
        Assert.Empty(log.Entries);
    }

    [Fact]
    public async Task Put_accepts_a_value_at_the_bounds_edge()
    {
        var (controller, settings, _) = NewSut();

        // The bounds are inclusive - the Max itself is allowed.
        var result = await controller.Put(SettingsCatalog.ExampleThreshold, Request(100), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(100, await settings.GetIntAsync(SettingsCatalog.ExampleThreshold));
    }

    // ---- AC-10: confirmation gate -----------------------------------------------

    [Fact]
    public async Task Put_to_a_confirmation_gated_key_without_confirm_is_rejected()
    {
        var (controller, settings, log) = NewSut();

        // ExampleEnabled is RequiresConfirmation - a PUT without confirm:true is a 400, no write,
        // no log row (a load-bearing flip can never be an accidental one-field PUT).
        var result = await controller.Put(SettingsCatalog.ExampleEnabled, Request(false), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Null((await settings.GetViewAsync(SettingsCatalog.ExampleEnabled))!.Override);
        Assert.Empty(log.Entries);
    }

    [Fact]
    public async Task Put_to_a_confirmation_gated_key_with_confirm_proceeds()
    {
        var (controller, settings, log) = NewSut();

        var result = await controller.Put(SettingsCatalog.ExampleEnabled, Request(false, confirm: true), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.False(await settings.GetBoolAsync(SettingsCatalog.ExampleEnabled));
        var row = Assert.Single(log.Entries);
        Assert.Equal("settings.put", row.Action);
        Assert.Equal("true -> false", row.Note);
    }

    // ---- AC-04 / AC-09: DELETE ---------------------------------------------------

    [Fact]
    public async Task Delete_clears_an_override_and_logs_one_row()
    {
        var (controller, settings, log) = NewSut();
        await controller.Put(SettingsCatalog.ExampleThreshold, Request(20), CancellationToken.None);

        var result = await controller.Delete(SettingsCatalog.ExampleThreshold, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(3, await settings.GetIntAsync(SettingsCatalog.ExampleThreshold)); // back to default
        // One put row + one delete row = two total; the delete row notes the revert.
        Assert.Equal(2, log.Entries.Count);
        var deleteRow = log.Entries[^1];
        Assert.Equal("settings.delete", deleteRow.Action);
        Assert.Equal("reverted to default", deleteRow.Note);
    }

    [Fact]
    public async Task Delete_of_a_key_with_no_override_writes_no_log_row()
    {
        var (controller, _, log) = NewSut();

        var result = await controller.Delete(SettingsCatalog.ExampleThreshold, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Empty(log.Entries); // a no-op DELETE writes no row (AC-09)
    }

    [Fact]
    public async Task Delete_rejects_an_unknown_key()
    {
        var (controller, _, log) = NewSut();

        var result = await controller.Delete("no.such.key", CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(log.Entries);
    }

    // ---- AC-06: the admin boundary (over the REAL app) --------------------------

    [Fact]
    public async Task Endpoints_are_401_when_unauthenticated()
    {
        using var factory = new AdminApiFactory();
        var client = factory.CreateClient();

        var get = await client.GetAsync("/api/admin/settings");
        var put = await client.PutAsJsonAsync(
            $"/api/admin/settings/{SettingsCatalog.ExampleThreshold}", new { value = 5 });
        var delete = await client.DeleteAsync($"/api/admin/settings/{SettingsCatalog.ExampleThreshold}");

        Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, put.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, delete.StatusCode);
    }

    [Fact]
    public async Task AllowlistedOperator_CanGetPutAndDelete_OverHttp()
    {
        using var factory = new AdminApiFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", factory.OperatorCredential());

        // GET the catalog - 200, and the example key reads its code default.
        var get = await client.GetAsync("/api/admin/settings");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var all = await get.Content.ReadFromJsonAsync<List<RuntimeSettingView>>();
        Assert.NotNull(all);
        Assert.Contains(all!, v => v.Key == SettingsCatalog.ExampleThreshold);

        // PUT an in-bounds override -> 200.
        var put = await client.PutAsJsonAsync(
            $"/api/admin/settings/{SettingsCatalog.ExampleThreshold}", new { value = 42 });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        // GET again -> the override is visible with the operator stamp.
        var afterPut = await client.GetFromJsonAsync<List<RuntimeSettingView>>("/api/admin/settings");
        var overridden = afterPut!.Single(v => v.Key == SettingsCatalog.ExampleThreshold);
        Assert.NotNull(overridden.Override);
        Assert.Equal(42, ((JsonElement)overridden.Override!.Value).GetInt32());

        // DELETE -> 200, and the key reverts to its code default (no override).
        var delete = await client.DeleteAsync($"/api/admin/settings/{SettingsCatalog.ExampleThreshold}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        var afterDelete = await client.GetFromJsonAsync<List<RuntimeSettingView>>("/api/admin/settings");
        Assert.Null(afterDelete!.Single(v => v.Key == SettingsCatalog.ExampleThreshold).Override);
    }

    /// <summary>
    /// Boots the real API in memory with a configured operator allowlist (Development environment,
    /// matching the AdminEntitlementsController boundary tests). No storage connection string, so
    /// the settings service runs on the in-memory store - the SAME store the endpoints write.
    /// </summary>
    private sealed class AdminApiFactory : WebApplicationFactory<Program>
    {
        private const string AllowlistedOperator = "ops@quibblestone.com";

        /// <summary>A genuine operator credential on the running app's own key ring.</summary>
        public string OperatorCredential()
        {
            var provider = Services.GetRequiredService<IDataProtectionProvider>();
            var protector = provider.CreateProtector(OperatorSession.OperatorSessionPurpose).ToTimeLimitedDataProtector();
            var payload = $"{AllowlistedOperator}|{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            return protector.Protect(payload, TimeSpan.FromHours(1));
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Operator:AllowedEmails:0"] = AllowlistedOperator,
                });
            });
        }
    }
}
