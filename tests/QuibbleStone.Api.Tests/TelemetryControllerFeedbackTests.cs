// ----------------------------------------------------------------------------
//  TelemetryControllerFeedbackTests - unit tests for the per-tale thumbs
//  up/down feedback endpoint (story-selection/05, AC-01/AC-02/AC-04/AC-05).
//
//  These exercise the REAL TelemetryController against the REAL TemplateCatalog
//  and a hand-rolled fake sink (no mocking framework in the harness), locking in
//  the anonymous feedback contract TaleFeedback.tsx relies on:
//
//    - AC-02: a valid template id + "up"/"down" vote records exactly ONE
//      FeedbackEvent, and a re-vote with the SAME VoteId (a changed thumb) is
//      handed to the sink again so the sink can upsert (last write wins) -
//      this test proves the CONTROLLER always forwards, never de-dupes itself.
//    - AC-02/junk: an UNKNOWN template id, an invalid vote value, or a missing
//      vote id is DROPPED silently - nothing recorded, still 202.
//    - AC-05: a THROWING sink never faults the response (voting fails soft).
//    - AC-04: the recorded event carries only anonymous facts (template id,
//      vote, mode, session id, vote id) - never PII. A shape assertion on
//      FeedbackEvent itself (mirrors GameHubStartRoundTests's ServeEvent PII
//      test) locks this in structurally, not just per-instance.
// ----------------------------------------------------------------------------

using Microsoft.AspNetCore.Mvc;
using QuibbleStone.Api.Content;
using QuibbleStone.Api.Controllers;
using QuibbleStone.Api.Telemetry;

namespace QuibbleStone.Api.Tests;

public class TelemetryControllerFeedbackTests
{
    private static readonly TemplateCatalog Catalog = new();

    [Fact]
    public void Feedback_records_one_event_for_a_valid_up_vote()
    {
        var fake = new FakeTelemetrySink();
        var controller = new TelemetryController(Catalog, fake);

        // "space-llama" is a real catalog id (a full story).
        var result = controller.Feedback(new FeedbackRequest(
            TemplateId: "space-llama",
            Vote: "up",
            Mode: "solo",
            SessionId: "device-session-guid",
            VoteId: "vote-guid-1"));

        Assert.IsType<AcceptedResult>(result);

        var evt = Assert.Single(fake.FeedbackEvents);
        Assert.Equal("space-llama", evt.TemplateId);
        Assert.Equal("up", evt.Vote);
        Assert.Equal("solo", evt.Mode);
        Assert.Equal("device-session-guid", evt.SessionId);
        Assert.Equal("vote-guid-1", evt.VoteId);
    }

    [Fact]
    public void Feedback_records_one_event_for_a_valid_down_vote()
    {
        var fake = new FakeTelemetrySink();
        var controller = new TelemetryController(Catalog, fake);

        var result = controller.Feedback(new FeedbackRequest(
            TemplateId: "space-llama",
            Vote: "down",
            Mode: "classic-blind",
            SessionId: "device-session-guid",
            VoteId: "vote-guid-2"));

        Assert.IsType<AcceptedResult>(result);
        var evt = Assert.Single(fake.FeedbackEvents);
        Assert.Equal("down", evt.Vote);
        Assert.Equal("classic-blind", evt.Mode);
    }

    [Fact]
    public void Feedback_forwards_a_changed_vote_with_the_same_VoteId_so_the_sink_can_upsert()
    {
        // AC-02: the controller does not de-dupe itself - it forwards every valid
        // tap to the sink, which upserts on VoteId (last write wins). Tapping up
        // then down (same VoteId) must reach the sink TWICE so the second write
        // can overwrite the first.
        var fake = new FakeTelemetrySink();
        var controller = new TelemetryController(Catalog, fake);

        controller.Feedback(new FeedbackRequest("space-llama", "up", "solo", "session-1", "same-vote-id"));
        controller.Feedback(new FeedbackRequest("space-llama", "down", "solo", "session-1", "same-vote-id"));

        Assert.Equal(2, fake.FeedbackEvents.Count);
        Assert.All(fake.FeedbackEvents, e => Assert.Equal("same-vote-id", e.VoteId));
        Assert.Equal("down", fake.FeedbackEvents[^1].Vote);
    }

    [Fact]
    public void Feedback_drops_an_unknown_template_id_silently()
    {
        var fake = new FakeTelemetrySink();
        var controller = new TelemetryController(Catalog, fake);

        var result = controller.Feedback(new FeedbackRequest(
            TemplateId: "totally-made-up-template",
            Vote: "up",
            Mode: "solo",
            SessionId: "device-session-guid",
            VoteId: "vote-guid-3"));

        Assert.IsType<AcceptedResult>(result);
        Assert.Empty(fake.FeedbackEvents);
    }

    [Theory]
    [InlineData("meh")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Up")] // case-sensitive on purpose: a crafted client cannot smuggle a variant value in.
    public void Feedback_drops_an_invalid_vote_silently(string? vote)
    {
        var fake = new FakeTelemetrySink();
        var controller = new TelemetryController(Catalog, fake);

        var result = controller.Feedback(new FeedbackRequest(
            TemplateId: "space-llama",
            Vote: vote,
            Mode: "solo",
            SessionId: "device-session-guid",
            VoteId: "vote-guid-4"));

        Assert.IsType<AcceptedResult>(result);
        Assert.Empty(fake.FeedbackEvents);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Feedback_drops_a_missing_vote_id_silently(string? voteId)
    {
        var fake = new FakeTelemetrySink();
        var controller = new TelemetryController(Catalog, fake);

        var result = controller.Feedback(new FeedbackRequest(
            TemplateId: "space-llama",
            Vote: "up",
            Mode: "solo",
            SessionId: "device-session-guid",
            VoteId: voteId));

        Assert.IsType<AcceptedResult>(result);
        Assert.Empty(fake.FeedbackEvents);
    }

    [Fact]
    public void Feedback_returns_success_even_when_the_sink_throws()
    {
        // AC-05: a throwing sink must never fault the response (voting fails soft).
        var controller = new TelemetryController(Catalog, new ThrowingTelemetrySink());

        var result = controller.Feedback(new FeedbackRequest(
            TemplateId: "space-llama",
            Vote: "up",
            Mode: "solo",
            SessionId: "device-session-guid",
            VoteId: "vote-guid-5"));

        Assert.IsType<AcceptedResult>(result);
    }

    [Fact]
    public void FeedbackEvent_carries_no_PII_fields()
    {
        // AC-04: a shape assertion - the feedback event has ONLY anonymous fields
        // and NOTHING that could carry a person (no nickname, code, connectionId,
        // IP, or hub player-session id). If someone adds such a field, this fails.
        var propertyNames = typeof(FeedbackEvent)
            .GetProperties()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        string[] forbidden =
        [
            "Nickname", "Name", "DisplayName", "Code", "JoinCode", "RoomCode",
            "ConnectionId", "Connection", "Ip", "IpAddress", "PlayerSessionId",
            "UserId", "Email",
        ];
        foreach (var banned in forbidden)
        {
            Assert.DoesNotContain(banned, propertyNames);
        }

        // And it carries exactly the six anonymous fields the story specifies.
        Assert.Equal(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "TemplateId", "Vote", "TimestampUtc", "Mode", "SessionId", "VoteId",
            },
            propertyNames);
    }
}
