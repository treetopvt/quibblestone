// ----------------------------------------------------------------------------
//  RoomCapacityTests - the server-authoritative per-room player cap (W2/F2).
//
//  Before this, Room enforced nickname uniqueness but NO capacity, so the lobby's
//  "n of 6" was cosmetic and a join-storm could pack a room arbitrarily (with
//  O(N^2) roster fan-out - see docs/load-testing/findings.md, F2/F3). Room.AddPlayer
//  now caps a room at Room.MaxPlayers seats (host included), atomically under the
//  room lock. These tests pin that contract at the Room level; GameHubJoinTests
//  covers the friendly "room's full" message the hub returns from it.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Rooms;

namespace QuibbleStone.Api.Tests;

public class RoomCapacityTests
{
    // A room with the host seated (player 1) plus joiners added via the capped
    // AddPlayer, named P1..P(totalPlayers-1). Asserts every within-cap add is Added.
    private static Room FillRoom(int totalPlayers)
    {
        var room = Room.CreateHosted("ABCD", "conn-0", "Host", "teal");
        for (var i = 1; i < totalPlayers; i += 1)
        {
            Assert.Equal(AddPlayerResult.Added, room.AddPlayer($"P{i}", "gold", $"conn-{i}"));
        }
        return room;
    }

    [Fact]
    public void AddPlayer_seats_up_to_the_cap_including_the_host()
    {
        var room = FillRoom(Room.MaxPlayers); // host + (MaxPlayers - 1) joiners
        Assert.Equal(Room.MaxPlayers, room.PlayerCount);
    }

    [Fact]
    public void AddPlayer_refuses_a_seat_past_the_cap_as_RoomFull_with_no_partial_add()
    {
        var room = FillRoom(Room.MaxPlayers);

        var overflow = room.AddPlayer("OneTooMany", "coral", "conn-overflow");

        Assert.Equal(AddPlayerResult.RoomFull, overflow);
        Assert.Equal(Room.MaxPlayers, room.PlayerCount);
        Assert.DoesNotContain(room.SnapshotPlayers(), p => p.Nickname == "OneTooMany");
    }

    [Fact]
    public void AddPlayer_reports_NameTaken_below_the_cap_case_insensitively()
    {
        var room = Room.CreateHosted("ABCD", "conn-0", "Host", "teal");
        Assert.Equal(AddPlayerResult.Added, room.AddPlayer("Maple", "gold", "conn-1"));

        // Same name, different case, different connection: refused distinctly from full.
        var dup = room.AddPlayer("maple", "teal", "conn-2");

        Assert.Equal(AddPlayerResult.NameTaken, dup);
        Assert.Equal(2, room.PlayerCount);
    }

    [Fact]
    public void A_held_disconnected_seat_still_counts_toward_the_cap()
    {
        // Fill to the cap, then one seat drops abnormally: session-engine/07 HOLDS
        // the seat (it stays on the roster for the grace window) so the player can
        // Rejoin. The cap must keep counting it - the seat is RESERVED, not freed -
        // so a new joiner cannot steal it out from under the reconnecting player.
        var room = FillRoom(Room.MaxPlayers);
        room.MarkDisconnected("conn-1");

        Assert.Equal(Room.MaxPlayers, room.PlayerCount);
        Assert.Equal(AddPlayerResult.RoomFull, room.AddPlayer("LatePlayer", "plum", "conn-late"));
    }

    [Fact]
    public void TryAddPlayer_is_the_uncapped_primitive_for_fixtures()
    {
        // TryAddPlayer deliberately skips the capacity cap (it is the low-level
        // roster append the distribution invariant sweep uses to build N > MaxPlayers
        // rosters). It still enforces uniqueness. Production joins use AddPlayer.
        var room = FillRoom(Room.MaxPlayers);

        Assert.True(room.TryAddPlayer("BeyondCap", "gold", "conn-beyond"));
        Assert.Equal(Room.MaxPlayers + 1, room.PlayerCount);
    }
}
