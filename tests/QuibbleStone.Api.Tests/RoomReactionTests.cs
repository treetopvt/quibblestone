// ----------------------------------------------------------------------------
//  RoomReactionTests - unit tests for the room-wide reaction tally on Room
//  (reveal-delight/01, AC-04; reactions v2 - the UX de-clutter). The reaction
//  counter is a genuinely new pure-logic seam on Room, so it gets focused
//  coverage here (the ACs themselves are manual - two browser contexts - but the
//  counting + one-per-user select/move/toggle + per-reveal reset is testable in
//  isolation through Room's public surface).
//
//  These exercise the REAL Room type through its PUBLIC surface:
//    1. SetReaction SELECTS a connection's single reaction: it bumps exactly the
//       tapped type by one and leaves the other two untouched, returning the full
//       updated tally over the three types (love/wow/nope).
//    2. One-per-user: the SAME connection tapping the SAME type TOGGLES it off
//       (its count returns to zero), and tapping a DIFFERENT type MOVES the
//       reaction (old decrements, new increments) - it never inflates.
//    3. Two DIFFERENT connections each hold their own reaction independently.
//    4. StartRound RESETS every reaction count back to zero AND forgets every
//       per-connection hold (counts + holds are ephemeral per reveal).
//    5. A leaving connection's held reaction is dropped from the tally.
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
    public void SetReaction_Selects_BumpsOnlyTheTappedType_AndReturnsFullTally()
    {
        var room = BuildRoom();

        var tally = room.SetReaction("conn-0", "love");

        Assert.Equal(1, tally["love"]);
        Assert.Equal(0, tally["wow"]);
        Assert.Equal(0, tally["nope"]);
    }

    [Fact]
    public void SetReaction_SameTypeAgain_TogglesOff()
    {
        var room = BuildRoom();

        room.SetReaction("conn-0", "wow");
        var tally = room.SetReaction("conn-0", "wow");

        // The one-per-user rule: a second tap on the pill this connection already
        // holds removes it (the old count-inflation bug is fixed - it never runs up).
        Assert.Equal(0, tally["wow"]);
        Assert.Equal(0, tally["love"]);
        Assert.Equal(0, tally["nope"]);
    }

    [Fact]
    public void SetReaction_DifferentType_MovesTheReaction()
    {
        var room = BuildRoom();

        room.SetReaction("conn-0", "love");
        var tally = room.SetReaction("conn-0", "nope");

        // Switching pills MOVES the single reaction: the old type decrements and the
        // new one increments - the total held by this connection stays exactly one.
        Assert.Equal(0, tally["love"]);
        Assert.Equal(1, tally["nope"]);
        Assert.Equal(0, tally["wow"]);
    }

    [Fact]
    public void SetReaction_TwoConnections_HoldIndependently()
    {
        var room = BuildRoom();

        room.SetReaction("conn-0", "love");
        var tally = room.SetReaction("conn-1", "wow");

        // Two players each hold their own reaction - one-per-USER, not one-per-room.
        Assert.Equal(1, tally["love"]);
        Assert.Equal(1, tally["wow"]);
        Assert.Equal(0, tally["nope"]);
    }

    [Fact]
    public void StartRound_ResetsReactionCountsAndHoldsToZero()
    {
        var room = BuildRoom();

        room.SetReaction("conn-0", "love");
        room.SetReaction("conn-1", "wow");

        // A fresh round starts every reaction count back at zero AND forgets each
        // connection's hold (ephemeral per reveal). Two blanks over two players so
        // the deal is well-formed.
        room.StartRound("tmpl-1", "classic-blind", blankCount: 2);

        // conn-0 holding nothing again: this is a fresh SELECT, so it is the only count.
        var tally = room.SetReaction("conn-0", "nope");
        Assert.Equal(0, tally["love"]);
        Assert.Equal(0, tally["wow"]);
        Assert.Equal(1, tally["nope"]);
    }

    [Fact]
    public void RemovePlayer_DropsThatConnectionsHeldReaction()
    {
        var room = BuildRoom();

        room.SetReaction("conn-0", "love");
        room.SetReaction("conn-1", "wow");

        // conn-1 leaves - its held Wow must not linger in the tally.
        Assert.True(room.RemovePlayer("conn-1"));

        // A follow-up reaction from the host reports the current tally; Wow is gone.
        var tally = room.SetReaction("conn-0", "love"); // toggles conn-0's Love off
        Assert.Equal(0, tally["love"]);
        Assert.Equal(0, tally["wow"]);
        Assert.Equal(0, tally["nope"]);
    }
}
