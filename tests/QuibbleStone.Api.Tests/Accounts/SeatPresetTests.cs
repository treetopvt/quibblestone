// ----------------------------------------------------------------------------
//  SeatPresetTests - controller + store tests for kid seat presets
//  (accounts-identity/08, issue #228). They drive the REAL AccountsController
//  preset endpoints against the REAL InMemorySeatPresetStore + InMemoryAccountStore
//  + a real (ephemeral) Data Protection provider + the shared content-safety filter
//  (real or a spy), matching the harness style of SignInTests - no mocking framework.
//
//  They pin the load-bearing guarantees of the story:
//    - AC-01: a signed-in family can create / list / edit / delete presets, and a
//      preset carries ONLY { id, nickname, variant } (the SeatPreset record shape).
//    - AC-02 / auth: every endpoint 401s without a valid family credential, so no
//      preset state leaks to an unauthenticated caller.
//    - AC-04 / AC-07: a preset nickname passes the EXACT SAME server-side content-
//      safety filter + display-name length cap as a manual name BEFORE it is stored;
//      a blocked or over-length name is rejected (400) and nothing is stored.
//    - AC-05 (scoping): presets are keyed by AccountId - one family can never see,
//      edit, or delete another family's presets.
//    - Variant normalization mirrors the join path (unknown -> "teal").
//
//  AC-03 (a preset join is indistinguishable from a manual join) is a CODE-LEVEL
//  guarantee, not an assertion here: these endpoints are the account plane only and
//  import nothing from api/src/Rooms; the join path is unchanged. See the story's
//  AC-03 code check + the /verify walkthrough.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using QuibbleStone.Api.Accounts;
using QuibbleStone.Api.Controllers;
using QuibbleStone.Api.Safety;

namespace QuibbleStone.Api.Tests.Accounts;

public class SeatPresetTests
{
    private const string TestSigningKey = "test-signing-key-not-a-real-secret";

    private sealed record Harness(
        AccountsController Controller,
        InMemoryAccountStore Accounts,
        InMemorySeatPresetStore Presets,
        PurchaserCredentialService Credential,
        SpySafetyFilter Safety);

    // Build a controller with an optional pre-signed-in family credential set on the
    // request (Authorization: Bearer), and an optional safety verdict toggle.
    private static Harness NewHarness(string? signedInEmail = null, bool allowSafety = true)
    {
        var accounts = new InMemoryAccountStore();
        var presets = new InMemorySeatPresetStore();
        var tokens = new MagicLinkTokenService(TestSigningKey, new InMemoryConsumedNonceStore());
        IDataProtectionProvider dataProtection = new EphemeralDataProtectionProvider();
        var credential = new PurchaserCredentialService(dataProtection);
        var environment = new FakeWebHostEnvironment("Development");
        IEmailSender email = new NoOpEmailSender(NullLogger<NoOpEmailSender>.Instance);
        var safety = new SpySafetyFilter(allowSafety);

        var httpContext = new DefaultHttpContext();
        if (signedInEmail is not null)
        {
            // The family already has an account, and this device holds the credential
            // accounts-identity/03 mints on sign-in - resolved by the SAME service.
            accounts.CreateOrGetAsync(signedInEmail).GetAwaiter().GetResult();
            var bearer = credential.Protect(signedInEmail);
            httpContext.Request.Headers.Authorization = $"Bearer {bearer}";
        }

        var controller = new AccountsController(
            tokens, accounts, credential, email, new EmailOptions(), environment,
            // accounts-identity/09 device deps: valid instances for the ctor; these
            // preset tests never call a device endpoint, so they are inert here.
            new FamilyDeviceLinkService(new InMemoryFamilyLinkCodeStore(), new InMemoryFamilyDeviceTokenStore()),
            new InMemoryFamilyDeviceTokenStore(),
            new AdultSignalResolutionService(new PurchaserCredentialService(new EphemeralDataProtectionProvider()), new FamilyDeviceLinkService(new InMemoryFamilyLinkCodeStore(), new InMemoryFamilyDeviceTokenStore())),
            new FamilyDeviceRedeemGlobalThrottle(),
            NullLogger<AccountsController>.Instance, presets, safety)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };

        return new Harness(controller, accounts, presets, credential, safety);
    }

    private static T Body<T>(IActionResult result) where T : class
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return Assert.IsType<T>(ok.Value);
    }

    // ---- Auth: no credential -> 401 on every endpoint (AC-02) --------------------

    [Fact]
    public async Task List_WithoutCredential_Returns401()
    {
        var harness = NewHarness();
        var result = await harness.Controller.ListPresets(default);
        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task Create_WithoutCredential_Returns401_AndStoresNothing()
    {
        var harness = NewHarness();
        var result = await harness.Controller.CreatePreset(new SeatPresetBody("Emma", "gold"), default);
        Assert.IsType<UnauthorizedResult>(result);
        Assert.False(harness.Safety.WasChecked); // never even reaches the filter
    }

    [Fact]
    public async Task Update_And_Delete_WithoutCredential_Return401()
    {
        var harness = NewHarness();
        var update = await harness.Controller.UpdatePreset(Guid.NewGuid().ToString(), new SeatPresetBody("Emma", "gold"), default);
        Assert.IsType<UnauthorizedResult>(update);
        var delete = await harness.Controller.DeletePreset(Guid.NewGuid().ToString(), default);
        Assert.IsType<UnauthorizedResult>(delete);
    }

    // ---- AC-01: create / list / edit / delete under a family account -------------

    [Fact]
    public async Task Create_Then_List_ReturnsThePreset_WithOnlyNicknameAndVariant()
    {
        var harness = NewHarness("parent@example.com");

        var created = Body<SeatPresetView>(await harness.Controller.CreatePreset(new SeatPresetBody("Emma", "gold"), default));
        Assert.Equal("Emma", created.Nickname);
        Assert.Equal("gold", created.Variant);
        Assert.True(Guid.TryParse(created.Id, out _));

        var list = Body<SeatPresetsResult>(await harness.Controller.ListPresets(default));
        var only = Assert.Single(list.Presets);
        Assert.Equal(created.Id, only.Id);
        Assert.Equal("Emma", only.Nickname);
        Assert.Equal("gold", only.Variant);
    }

    [Fact]
    public async Task Update_ChangesNicknameAndVariant_KeepingTheSameId()
    {
        var harness = NewHarness("parent@example.com");
        var created = Body<SeatPresetView>(await harness.Controller.CreatePreset(new SeatPresetBody("Emma", "gold"), default));

        var updated = Body<SeatPresetView>(
            await harness.Controller.UpdatePreset(created.Id, new SeatPresetBody("Emmie", "teal"), default));
        Assert.Equal(created.Id, updated.Id);
        Assert.Equal("Emmie", updated.Nickname);
        Assert.Equal("teal", updated.Variant);

        var list = Body<SeatPresetsResult>(await harness.Controller.ListPresets(default));
        var only = Assert.Single(list.Presets);
        Assert.Equal("Emmie", only.Nickname);
    }

    [Fact]
    public async Task Delete_RemovesThePreset()
    {
        var harness = NewHarness("parent@example.com");
        var created = Body<SeatPresetView>(await harness.Controller.CreatePreset(new SeatPresetBody("Emma", "gold"), default));

        var delete = await harness.Controller.DeletePreset(created.Id, default);
        Assert.IsType<NoContentResult>(delete);

        var list = Body<SeatPresetsResult>(await harness.Controller.ListPresets(default));
        Assert.Empty(list.Presets);
    }

    [Fact]
    public async Task List_WithNoPresets_ReturnsEmpty_NotAnError()
    {
        var harness = NewHarness("parent@example.com");
        var list = Body<SeatPresetsResult>(await harness.Controller.ListPresets(default));
        Assert.Empty(list.Presets);
    }

    // ---- AC-04 / AC-07: same safety filter + length cap as any display name ------

    [Fact]
    public async Task Create_RunsTheNicknameThroughTheSameSafetyFilter()
    {
        var harness = NewHarness("parent@example.com");
        await harness.Controller.CreatePreset(new SeatPresetBody("Emma", "gold"), default);
        // The SAME IContentSafetyFilter every free-text surface uses was invoked with
        // the preset nickname before it was stored (AC-04) - never trusted client-side.
        Assert.True(harness.Safety.WasChecked);
        Assert.Contains("Emma", harness.Safety.Checked);
    }

    [Fact]
    public async Task Create_WithFilteredNickname_IsRejected_AndStoresNothing()
    {
        // The filter blocks -> the SAME rejection a manual join would get (AC-04).
        var harness = NewHarness("parent@example.com", allowSafety: false);
        var result = await harness.Controller.CreatePreset(new SeatPresetBody("badword", "gold"), default);
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.IsType<SeatPresetError>(bad.Value);

        var list = Body<SeatPresetsResult>(await harness.Controller.ListPresets(default));
        Assert.Empty(list.Presets);
    }

    [Fact]
    public async Task Create_WithRealFilter_BlocksAKnownBlocklistedNickname()
    {
        // Wire the REAL ContentSafetyFilter to prove the preset path is gated by the
        // exact same blocklist a manual name is ("arse" is in blocklist.txt).
        var harness = NewHarness("parent@example.com");
        var accounts = harness.Accounts;
        var presets = harness.Presets;
        var credential = harness.Credential;
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = $"Bearer {credential.Protect("parent@example.com")}";
        var controller = new AccountsController(
            new MagicLinkTokenService(TestSigningKey, new InMemoryConsumedNonceStore()),
            accounts, credential, new NoOpEmailSender(NullLogger<NoOpEmailSender>.Instance),
            new EmailOptions(), new FakeWebHostEnvironment("Development"),
            new FamilyDeviceLinkService(new InMemoryFamilyLinkCodeStore(), new InMemoryFamilyDeviceTokenStore()),
            new InMemoryFamilyDeviceTokenStore(),
            new AdultSignalResolutionService(new PurchaserCredentialService(new EphemeralDataProtectionProvider()), new FamilyDeviceLinkService(new InMemoryFamilyLinkCodeStore(), new InMemoryFamilyDeviceTokenStore())),
            new FamilyDeviceRedeemGlobalThrottle(),
            NullLogger<AccountsController>.Instance, presets, new ContentSafetyFilter())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };

        var result = await controller.CreatePreset(new SeatPresetBody("arse", "gold"), default);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Create_WithEmptyNickname_IsRejected()
    {
        var harness = NewHarness("parent@example.com");
        var result = await harness.Controller.CreatePreset(new SeatPresetBody("   ", "gold"), default);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Create_WithOverLengthNickname_IsRejected()
    {
        var harness = NewHarness("parent@example.com");
        // One over the shared display-name cap (SeatPresetRules.MaxNicknameLength = 14).
        var tooLong = new string('a', SeatPresetRules.MaxNicknameLength + 1);
        var result = await harness.Controller.CreatePreset(new SeatPresetBody(tooLong, "gold"), default);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    // ---- Variant normalization mirrors the join path -----------------------------

    [Fact]
    public async Task Create_NormalizesAnUnknownVariantToTeal()
    {
        var harness = NewHarness("parent@example.com");
        var created = Body<SeatPresetView>(await harness.Controller.CreatePreset(new SeatPresetBody("Emma", "rainbow"), default));
        Assert.Equal("teal", created.Variant);
    }

    [Fact]
    public async Task Create_LowercasesAKnownVariant()
    {
        var harness = NewHarness("parent@example.com");
        var created = Body<SeatPresetView>(await harness.Controller.CreatePreset(new SeatPresetBody("Emma", "GOLD"), default));
        Assert.Equal("gold", created.Variant);
    }

    // ---- AC-05: presets are scoped to the owning family AccountId ----------------

    [Fact]
    public async Task Update_CannotReachAnotherFamilysPreset()
    {
        // Family A creates a preset.
        var familyA = NewHarness("a@example.com");
        var createdA = Body<SeatPresetView>(await familyA.Controller.CreatePreset(new SeatPresetBody("Emma", "gold"), default));

        // Family B (a DIFFERENT account) shares A's stores but a different credential -
        // it must not be able to update A's preset (a 404, never a cross-account edit).
        var accountB = await familyA.Accounts.CreateOrGetAsync("b@example.com");
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = $"Bearer {familyA.Credential.Protect("b@example.com")}";
        var controllerB = new AccountsController(
            new MagicLinkTokenService(TestSigningKey, new InMemoryConsumedNonceStore()),
            familyA.Accounts, familyA.Credential, new NoOpEmailSender(NullLogger<NoOpEmailSender>.Instance),
            new EmailOptions(), new FakeWebHostEnvironment("Development"),
            new FamilyDeviceLinkService(new InMemoryFamilyLinkCodeStore(), new InMemoryFamilyDeviceTokenStore()),
            new InMemoryFamilyDeviceTokenStore(),
            new AdultSignalResolutionService(new PurchaserCredentialService(new EphemeralDataProtectionProvider()), new FamilyDeviceLinkService(new InMemoryFamilyLinkCodeStore(), new InMemoryFamilyDeviceTokenStore())),
            new FamilyDeviceRedeemGlobalThrottle(),
            NullLogger<AccountsController>.Instance, familyA.Presets, new SpySafetyFilter(true))
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };
        _ = accountB;

        var update = await controllerB.UpdatePreset(createdA.Id, new SeatPresetBody("Hacked", "coral"), default);
        Assert.IsType<NotFoundResult>(update);

        var delete = await controllerB.DeletePreset(createdA.Id, default);
        Assert.IsType<NotFoundResult>(delete);

        // Family B's own list is empty; A's preset is untouched.
        var listB = Body<SeatPresetsResult>(await controllerB.ListPresets(default));
        Assert.Empty(listB.Presets);
        var listA = Body<SeatPresetsResult>(await familyA.Controller.ListPresets(default));
        Assert.Equal("Emma", Assert.Single(listA.Presets).Nickname);
    }

    // ---- Update / delete of a missing preset -> 404 ------------------------------

    [Fact]
    public async Task Update_MissingPreset_Returns404_NeverCreates()
    {
        var harness = NewHarness("parent@example.com");
        var result = await harness.Controller.UpdatePreset(Guid.NewGuid().ToString(), new SeatPresetBody("Emma", "gold"), default);
        Assert.IsType<NotFoundResult>(result);
        var list = Body<SeatPresetsResult>(await harness.Controller.ListPresets(default));
        Assert.Empty(list.Presets);
    }

    [Fact]
    public async Task Delete_MissingPreset_Returns404()
    {
        var harness = NewHarness("parent@example.com");
        var result = await harness.Controller.DeletePreset(Guid.NewGuid().ToString(), default);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Update_WithMalformedId_Returns404()
    {
        var harness = NewHarness("parent@example.com");
        var result = await harness.Controller.UpdatePreset("not-a-guid", new SeatPresetBody("Emma", "gold"), default);
        Assert.IsType<NotFoundResult>(result);
    }

    // ---- Store-level: partition isolation + stable list order --------------------

    [Fact]
    public async Task Store_PartitionsByAccount_AndListsInCreationOrder()
    {
        var store = new InMemorySeatPresetStore();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await store.CreateAsync(a, "First", "gold");
        await store.CreateAsync(a, "Second", "teal");
        await store.CreateAsync(b, "OtherFamily", "coral");

        var listA = await store.ListAsync(a);
        Assert.Equal(new[] { "First", "Second" }, listA.Select(p => p.Nickname).ToArray());

        var listB = await store.ListAsync(b);
        Assert.Equal("OtherFamily", Assert.Single(listB).Nickname);

        // An account with no presets lists empty.
        Assert.Empty(await store.ListAsync(Guid.NewGuid()));
    }

    // A tiny spy over the safety contract: records the candidates it was asked to vet
    // and returns a fixed allow/deny verdict (mirrors the SpySafetyFilter pattern in
    // GameHubSubmitWordTests) so a test can assert the SAME filter was invoked (AC-04).
    private sealed class SpySafetyFilter(bool allow) : IContentSafetyFilter
    {
        public List<string?> Checked { get; } = new();
        public bool WasChecked => Checked.Count > 0;

        public ValueTask<ContentSafetyResult> CheckAsync(string? candidate, CancellationToken cancellationToken = default)
        {
            Checked.Add(candidate);
            var verdict = allow ? ContentSafetyResult.Allowed : ContentSafetyResult.Blocked("Let's try a different name.");
            return ValueTask.FromResult(verdict);
        }
    }

    // A minimal IWebHostEnvironment stand-in (mirrors the private one in SignInTests);
    // the preset endpoints do not branch on the environment, but the controller ctor
    // requires one.
    private sealed class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public FakeWebHostEnvironment(string environmentName) => EnvironmentName = environmentName;

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "QuibbleStone.Api.Tests";
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
