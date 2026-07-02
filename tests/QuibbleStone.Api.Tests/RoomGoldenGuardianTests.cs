// ----------------------------------------------------------------------------
//  RoomGoldenGuardianTests - unit tests for the AUTHORITATIVE (server-side)
//  Golden Guardian funniest-word vote on Room (reveal-delight/03, #58).
//
//  The tally + tie-break RULE is the unit-tested reference spec in
//  web/src/engine/vote.ts (vote.test.ts). Room.ResolveGoldenGuardian is its
//  hand-kept C# twin (the authority on the wire) - the same "TS reference /
//  C# twin" convention as distribute.ts <-> Room.ComputeAssignments. What is
//  C#-ONLY, and so covered here, is the Room FLOW around that rule that vote.ts
//  cannot reach: which casts are accepted, auto-resolution when everyone has
//  voted, the winner -> contributor mapping, the host's early close, and the
//  "next round only" crown lifecycle (AC-03/AC-04).
//
//  These exercise the REAL Room through its PUBLIC surface: build a room, start
//  a round, submit every assigned blank (round-robin: blank k -> player k in
//  roster order, host first) to reach the "reveal" phase, then vote. The vote
//  token is a blank's body-order index as a string ("0", "1", ...).
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Rooms;

namespace QuibbleStone.Api.Tests;

public class RoomGoldenGuardianTests
{
    // A 3-player room driven to the reveal phase with three filled blanks
    // (blank0 -> Host/conn-0, blank1 -> P1/conn-1, blank2 -> P2/conn-2). Returns
    // the room ready for Golden Guardian voting.
    private static Room RoomAtReveal()
    {
        var room = Room.CreateHosted("ABCD", "conn-0", "Host", "teal");
        Assert.True(room.TryAddPlayer("P1", "gold", "conn-1"));
        Assert.True(room.TryAddPlayer("P2", "coral", "conn-2"));

        room.StartRound("tmpl-1", "classic-blind", blankCount: 3);
        room.RecordSubmission("conn-0", 0, "alpha");
        room.RecordSubmission("conn-1", 1, "bravo");
        Assert.Equal(Room.SubmitOutcome.RoundComplete, room.RecordSubmission("conn-2", 2, "charlie"));
        return room;
    }

    [Fact]
    public void CastVote_BeforeReveal_IsRejected()
    {
        var room = Room.CreateHosted("ABCD", "conn-0", "Host", "teal");
        Assert.True(room.TryAddPlayer("P1", "gold", "conn-1"));
        room.StartRound("tmpl-1", "classic-blind", blankCount: 2);

        // Still "prompting" - no reveal, so no vote exists yet.
        var result = room.CastGoldenGuardianVote("conn-0", "0");
        Assert.False(result.Accepted);
        Assert.False(result.Resolved);
    }

    [Fact]
    public void CastVote_NonFilledOrCraftedToken_IsIgnored()
    {
        var room = RoomAtReveal();

        // A token outside the filled-blank set (there is no blank 9) is never
        // recorded - a vote is only ever one of the already-shown coral words (AC-07).
        var result = room.CastGoldenGuardianVote("conn-0", "9");
        Assert.False(result.Accepted);
        Assert.Equal(0, result.VotedCount);
    }

    [Fact]
    public void CastVote_NonSeatedConnection_IsIgnored()
    {
        var room = RoomAtReveal();

        var result = room.CastGoldenGuardianVote("stranger-conn", "1");
        Assert.False(result.Accepted);
        Assert.Equal(0, result.VotedCount);
    }

    [Fact]
    public void Vote_AutoResolves_WhenEveryPresentPlayerHasVoted_AndPicksTheMostVotedWord()
    {
        var room = RoomAtReveal();

        // Two of three vote blank "1" (P1's "bravo"), one votes blank "2".
        Assert.False(room.CastGoldenGuardianVote("conn-0", "1").Resolved); // 1 of 3
        Assert.False(room.CastGoldenGuardianVote("conn-1", "2").Resolved); // 2 of 3
        var final = room.CastGoldenGuardianVote("conn-2", "1");            // 3 of 3 -> resolves

        Assert.True(final.Resolved);
        Assert.Equal(3, final.VotedCount);
        Assert.Equal(3, final.TotalVoters);
        Assert.Equal("1", final.WinningBlankId);
        // The winning word (blank 1) was contributed by P1 - the crown's wearer.
        Assert.Equal("P1", final.WinnerNickname);
    }

    [Fact]
    public void Vote_RecastMovesTheVote_NeverStacksASecond()
    {
        var room = RoomAtReveal();

        // conn-0 first votes "0", then MOVES to "1"; conn-1 and conn-2 vote "1".
        room.CastGoldenGuardianVote("conn-0", "0");
        room.CastGoldenGuardianVote("conn-0", "1"); // moves - "0" now has zero votes
        room.CastGoldenGuardianVote("conn-1", "1");
        var final = room.CastGoldenGuardianVote("conn-2", "0"); // 3 of 3 -> resolves

        // "1" has two votes (conn-0 moved here + conn-1), "0" has one (conn-2) -> "1" wins.
        Assert.True(final.Resolved);
        Assert.Equal("1", final.WinningBlankId);
        Assert.Equal("P1", final.WinnerNickname);
    }

    [Fact]
    public void Tie_IsBrokenByFirstWordToReachTheMaxCount_InCastOrder()
    {
        var room = RoomAtReveal();

        // A clean 1-1-1 across three different words: max count is 1, and the
        // FIRST cast (conn-0 -> "2") is the first word to reach it, so it wins.
        room.CastGoldenGuardianVote("conn-0", "2");
        room.CastGoldenGuardianVote("conn-1", "0");
        var final = room.CastGoldenGuardianVote("conn-2", "1");

        Assert.True(final.Resolved);
        Assert.Equal("2", final.WinningBlankId);
        Assert.Equal("P2", final.WinnerNickname); // blank 2 was P2's "charlie"
    }

    [Fact]
    public void HostClose_WithNoVotes_ResolvesWithNoWinnerAndNoCrown()
    {
        var room = RoomAtReveal();

        var result = room.CloseGoldenGuardian();

        Assert.True(result.Resolved);
        Assert.Null(result.WinningBlankId);
        Assert.Null(result.WinnerNickname);

        // No crown was awarded, so the next round wears none.
        var next = room.StartRound("tmpl-2", "classic-blind", blankCount: 2);
        Assert.Null(next.CrownedNickname);
    }

    [Fact]
    public void Crown_IsWornForExactlyTheNextRound_ThenCleared()
    {
        var room = RoomAtReveal();

        // Resolve with P1 (blank "1") as the funniest-word winner.
        room.CastGoldenGuardianVote("conn-0", "1");
        room.CastGoldenGuardianVote("conn-1", "1");
        var resolved = room.CastGoldenGuardianVote("conn-2", "1");
        Assert.Equal("P1", resolved.WinnerNickname);

        // The NEXT round crowns P1 (AC-04)...
        var nextRound = room.StartRound("tmpl-2", "classic-blind", blankCount: 2);
        Assert.Equal("P1", nextRound.CrownedNickname);

        // ...and the round AFTER that clears it - the crown is next-round-only,
        // never carried forward unless a new vote re-awards it.
        var roundAfter = room.StartRound("tmpl-3", "classic-blind", blankCount: 2);
        Assert.Null(roundAfter.CrownedNickname);
    }
}
