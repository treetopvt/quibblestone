// ----------------------------------------------------------------------------
//  TelemetryControllerTests - unit tests for the SOLO serve-log endpoint
//  (story-selection/04, AC-02/AC-03).
//
//  These exercise the REAL TelemetryController against the REAL TemplateCatalog
//  and a hand-rolled fake sink (no mocking framework in the harness), locking in
//  the anonymous serve-log contract solo relies on:
//
//    - AC-02: a valid template id records exactly ONE serve event ("solo" mode);
//      an UNKNOWN / invented id is DROPPED silently - nothing recorded, still 202.
//    - AC-03: a THROWING sink never faults the response - the endpoint still
//      returns success (telemetry never gates the solo flow).
//    - AC-04: the recorded event carries only anonymous facts (player count 1,
//      the opaque session id) - never PII.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.Mvc;
using QuibbleStone.Api.Content;
using QuibbleStone.Api.Controllers;

namespace QuibbleStone.Api.Tests;

public class TelemetryControllerTests
{
    private static readonly TemplateCatalog Catalog = new();

    [Fact]
    public void Serve_records_one_solo_event_for_a_valid_template_id()
    {
        var fake = new FakeTelemetrySink();
        var controller = new TelemetryController(Catalog, fake);

        // "space-llama" is a real catalog id (a full story - 10 blanks).
        var result = controller.Serve(new ServeLogRequest(
            TemplateId: "space-llama",
            Mode: "solo",
            FamilySafe: true,
            SessionId: "device-session-guid"));

        Assert.IsType<AcceptedResult>(result);

        var evt = Assert.Single(fake.Events);
        Assert.Equal("space-llama", evt.TemplateId);
        Assert.Equal("solo", evt.Mode);
        // The length class is DERIVED server-side from the catalog blank count
        // (space-llama has 10 -> "full"), never trusted from the client.
        Assert.Equal("full", evt.LengthClass);
        // AC-04: a solo round is one player (a COUNT), and the opaque session id is
        // carried through the instance-id slot - never a nickname or PII.
        Assert.Equal(1, evt.PlayerCount);
        Assert.True(evt.FamilySafe);
        Assert.Equal("device-session-guid", evt.InstanceId);
    }

    [Fact]
    public void Serve_drops_an_unknown_template_id_silently()
    {
        // AC-02: an invented / unknown id must record NOTHING but still return 202
        // (the endpoint never leaks which ids are real).
        var fake = new FakeTelemetrySink();
        var controller = new TelemetryController(Catalog, fake);

        var result = controller.Serve(new ServeLogRequest(
            TemplateId: "totally-made-up-template",
            Mode: "solo",
            FamilySafe: false,
            SessionId: "device-session-guid"));

        Assert.IsType<AcceptedResult>(result);
        Assert.Empty(fake.Events);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Serve_drops_a_missing_template_id_silently(string? templateId)
    {
        var fake = new FakeTelemetrySink();
        var controller = new TelemetryController(Catalog, fake);

        var result = controller.Serve(new ServeLogRequest(
            TemplateId: templateId,
            Mode: "solo",
            FamilySafe: false,
            SessionId: "device-session-guid"));

        Assert.IsType<AcceptedResult>(result);
        Assert.Empty(fake.Events);
    }

    [Fact]
    public void Serve_returns_success_even_when_the_sink_throws()
    {
        // AC-03: a throwing sink must never fault the response.
        var controller = new TelemetryController(Catalog, new ThrowingTelemetrySink());

        var result = controller.Serve(new ServeLogRequest(
            TemplateId: "space-llama",
            Mode: "solo",
            FamilySafe: true,
            SessionId: "device-session-guid"));

        Assert.IsType<AcceptedResult>(result);
    }

    [Fact]
    public void Serve_derives_the_length_class_from_the_catalog_not_the_client()
    {
        // The client no longer sends a length class - the server DERIVES it from
        // the template's authoritative blank count. "sneezy-dinosaur" is a quick
        // seed template (5 blanks), so the recorded class is "quick" with no client
        // input at all: a crafted client cannot poison length metrics.
        var fake = new FakeTelemetrySink();
        var controller = new TelemetryController(Catalog, fake);

        var result = controller.Serve(new ServeLogRequest(
            TemplateId: "sneezy-dinosaur",
            Mode: "solo",
            FamilySafe: true,
            SessionId: "device-session-guid"));

        Assert.IsType<AcceptedResult>(result);
        var evt = Assert.Single(fake.Events);
        Assert.Equal("quick", evt.LengthClass);
    }
}
