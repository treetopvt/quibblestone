// ----------------------------------------------------------------------------
//  RoomPlayedHistoryTests - unit tests for Room's per-room played-template
//  history (story-selection/03: PlayedTemplateIds + MarkTemplatePlayed).
//
//  Exercises the ephemeral, in-memory, room-lifetime history directly through
//  Room's public surface: ordering (oldest-first / most-recently-played
//  last), dedupe-and-move-to-end on a repeat play, and that a fresh room
//  starts with an empty history. The cap is documented behavior (a defensive
//  ceiling far above any realistic catalog size) rather than something worth
//  looping hundreds of times to prove here.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Rooms;

namespace QuibbleStone.Api.Tests;

public class RoomPlayedHistoryTests
{
    [Fact]
    public void A_fresh_room_has_no_played_history()
    {
        var room = Room.CreateHosted("ABCD", "conn-host", "Mossy", "teal");

        Assert.Empty(room.PlayedTemplateIds);
    }

    [Fact]
    public void MarkTemplatePlayed_appends_in_order()
    {
        var room = Room.CreateHosted("ABCD", "conn-host", "Mossy", "teal");

        room.MarkTemplatePlayed("a");
        room.MarkTemplatePlayed("b");
        room.MarkTemplatePlayed("c");

        Assert.Equal(new[] { "a", "b", "c" }, room.PlayedTemplateIds);
    }

    [Fact]
    public void MarkTemplatePlayed_dedupes_and_moves_a_repeat_to_the_end()
    {
        var room = Room.CreateHosted("ABCD", "conn-host", "Mossy", "teal");

        room.MarkTemplatePlayed("a");
        room.MarkTemplatePlayed("b");
        room.MarkTemplatePlayed("c");
        room.MarkTemplatePlayed("a"); // recycled and played again

        Assert.Equal(new[] { "b", "c", "a" }, room.PlayedTemplateIds);
    }

    [Fact]
    public void PlayedTemplateIds_returns_a_detached_snapshot()
    {
        var room = Room.CreateHosted("ABCD", "conn-host", "Mossy", "teal");
        room.MarkTemplatePlayed("a");

        var snapshot = room.PlayedTemplateIds;
        room.MarkTemplatePlayed("b");

        // The earlier snapshot must not observe the later mutation.
        Assert.Equal(new[] { "a" }, snapshot);
        Assert.Equal(new[] { "a", "b" }, room.PlayedTemplateIds);
    }
}
