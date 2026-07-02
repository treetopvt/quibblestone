// ----------------------------------------------------------------------------
//  RoomReactionTests - unit tests for the room-wide reaction tally on Room
//  (reveal-delight/01, AC-04). The reaction counter is the one genuinely new
//  pure-logic seam this story adds to Room, so it gets focused coverage here
//  (the ACs themselves are manual - two browser contexts - but the counting +
//  per-reveal reset is testable in isolation through Room's public surface).
//
//  These exercise the REAL Room type through its PUBLIC surface:
//    1. IncrementReaction bumps exactly the tapped type by one and leaves the
//       other three untouched, returning the full updated tally.
//    2. Repeated taps accumulate (no per-player de-dupe - Out of Scope).
//    3. StartRound RESETS every reaction count back to zero (counts are
//       ephemeral per reveal, not persisted across a replay).
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Rooms;

namespace QuibbleStone.Api.Tests;

public class RoomReactionTests
{
    private static Room BuildRoom()
    {
        var room = Room.CreateHosted("ABCD", "conn-0", "Host", "teal");
        Assert.True(room.TryAddPlayer("P1", "gold", "conn-1"));
        return room;
    }

    [Fact]
    public void IncrementReaction_BumpsOnlyTheTappedType_AndReturnsFullTally()
    {
        var room = BuildRoom();

        var tally = room.IncrementReaction("laugh");

        Assert.Equal(1, tally["laugh"]);
        Assert.Equal(0, tally["heart"]);
        Assert.Equal(0, tally["wow"]);
        Assert.Equal(0, tally["star"]);
    }

    [Fact]
    public void IncrementReaction_Accumulates_NoPerPlayerDedupe()
    {
        var room = BuildRoom();

        room.IncrementReaction("heart");
        room.IncrementReaction("heart");
        var tally = room.IncrementReaction("star");

        // Repeated taps of the same type accumulate (a player may tap as often as
        // they like - the per-player de-dupe guard is explicitly Out of Scope).
        Assert.Equal(2, tally["heart"]);
        Assert.Equal(1, tally["star"]);
        Assert.Equal(0, tally["laugh"]);
        Assert.Equal(0, tally["wow"]);
    }

    [Fact]
    public void StartRound_ResetsReactionCountsToZero()
    {
        var room = BuildRoom();

        room.IncrementReaction("laugh");
        room.IncrementReaction("wow");

        // A fresh round starts every reaction count back at zero (ephemeral per
        // reveal). Two blanks over two players so the deal is well-formed.
        room.StartRound("tmpl-1", "classic-blind", blankCount: 2);

        var tally = room.IncrementReaction("star");
        Assert.Equal(0, tally["laugh"]);
        Assert.Equal(0, tally["wow"]);
        Assert.Equal(0, tally["heart"]);
        Assert.Equal(1, tally["star"]); // the post-reset tap is the only count
    }
}
