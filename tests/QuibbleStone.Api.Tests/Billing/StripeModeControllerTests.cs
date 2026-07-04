// ----------------------------------------------------------------------------
//  StripeModeControllerTests - the operator-gated mode-toggle endpoint
//  (billing-entitlements/06 AC-06): a non-operator caller can neither read nor flip
//  the mode; reading is a separate action from flipping (no GET side effect); an
//  unknown mode value is rejected (never a silent default to Live); the interim gate
//  denies when no operator secret is configured.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using QuibbleStone.Api.Billing;
using QuibbleStone.Api.Controllers;

namespace QuibbleStone.Api.Tests.Billing;

public class StripeModeControllerTests
{
    private const string OperatorSecret = "op-secret-123";

    private static (StripeModeController Controller, IActiveStripeContext Context) NewController(string? configuredSecret = OperatorSecret)
    {
        var options = new StripeOptions
        {
            Live = new StripeModeConfig { SecretKey = "sk_live_1" },
            Test = new StripeModeConfig { SecretKey = "sk_test_1" },
        };
        var context = new ActiveStripeContext(new InMemoryActiveStripeModeStore(), options);
        var gate = new InterimSecretOperatorGate(configuredSecret);
        var controller = new StripeModeController(context, gate, NullLogger<StripeModeController>.Instance);
        return (controller, context);
    }

    private static void SetSecretHeader(StripeModeController controller, string? presented)
    {
        var http = new DefaultHttpContext();
        if (presented is not null)
        {
            http.Request.Headers[StripeModeController.OperatorSecretHeader] = presented;
        }
        controller.ControllerContext = new ControllerContext { HttpContext = http };
    }

    // AC-06: no operator credential -> GET is 401 and reads nothing.
    [Fact]
    public async Task Get_without_operator_secret_is_unauthorized()
    {
        var (controller, _) = NewController();
        SetSecretHeader(controller, presented: null);

        var action = await controller.Get(CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(action);
    }

    // AC-06: a correct operator credential can read the current mode (Test by default).
    [Fact]
    public async Task Get_with_operator_secret_returns_the_active_mode()
    {
        var (controller, _) = NewController();
        SetSecretHeader(controller, OperatorSecret);

        var action = await controller.Get(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action);
        var view = Assert.IsType<StripeModeView>(ok.Value);
        Assert.Equal("test", view.ActiveMode);
        Assert.True(view.Enabled);
    }

    // AC-06: a wrong operator credential cannot flip the mode, and nothing changes.
    [Fact]
    public async Task Post_with_wrong_secret_is_unauthorized_and_does_not_change_the_mode()
    {
        var (controller, context) = NewController();
        SetSecretHeader(controller, "wrong-secret");

        var action = await controller.Set(new StripeModeChangeBody("live"), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(action);
        Assert.Equal(StripeMode.Test, (await context.GetStateAsync()).Mode); // unchanged
    }

    // AC-06: a correct credential flips the mode; the flip is visible on the next read.
    [Fact]
    public async Task Post_with_operator_secret_flips_the_mode()
    {
        var (controller, context) = NewController();
        SetSecretHeader(controller, OperatorSecret);

        var action = await controller.Set(new StripeModeChangeBody("live"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action);
        var view = Assert.IsType<StripeModeView>(ok.Value);
        Assert.Equal("live", view.ActiveMode);
        Assert.NotNull(view.LastChangedUtc);
        Assert.Equal(StripeMode.Live, (await context.GetStateAsync()).Mode);
    }

    // AC-06: an unknown mode value is a 400 and changes nothing (never a silent go-Live).
    [Fact]
    public async Task Post_with_an_unknown_mode_is_rejected_and_changes_nothing()
    {
        var (controller, context) = NewController();
        SetSecretHeader(controller, OperatorSecret);

        var action = await controller.Set(new StripeModeChangeBody("banana"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(action);
        Assert.Equal(StripeMode.Test, (await context.GetStateAsync()).Mode); // unchanged
    }

    // AC-06: the interim gate with NO operator secret configured denies everything (inert,
    // not open) - even a caller that sends some header value.
    [Fact]
    public async Task Unconfigured_gate_denies_all()
    {
        var (controller, _) = NewController(configuredSecret: null);
        SetSecretHeader(controller, "anything");

        var action = await controller.Get(CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(action);
    }
}
