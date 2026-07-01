// ----------------------------------------------------------------------------
//  RoomRegistryTests - unit tests for the ephemeral room store (session-engine/01).
//
//  These lock in the room-creation contract behind the CreateRoom hub method,
//  exercising the REAL RoomRegistry (no mocks) so the actual code-generation and
//  roster behavior is covered:
//
//    1. AC-01: a created room seats the caller as the host / first player.
//    2. AC-02: the join code is 4 chars from the unambiguous alphabet - it never
//       contains the look-alike glyphs O, 0, I, 1, or l.
//    3. AC-03: codes are unique among currently active rooms.
//    4. Host identity (build/host-identity): the host is seated with the display
//       name + variant it picked on HostSetup (no longer an empty nickname), IsHost
//       true (still no PII beyond the anonymous in-session record).
//
//  Expiry (AC-05) is time-based (a 30-minute sliding window) and is left to a
//  manual/integration check rather than a clock-dependent unit test - the sweep
//  logic is small and lazy; wiring a fake clock is out of scope for Slice 1.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Rooms;

namespace QuibbleStone.Api.Tests;

public class RoomRegistryTests
{
    // The look-alike glyphs the code alphabet must exclude (AC-02).
    private const string AmbiguousGlyphs = "O0I1l";

    [Fact]
    public void CreateRoom_seats_the_caller_as_the_host_first_player()
    {
        var registry = new RoomRegistry();

        var room = registry.CreateRoom("conn-host", "Mossy", "teal");
        var players = room.SnapshotPlayers();

        Assert.Single(players);
        var host = players[0];
        Assert.True(host.IsHost);
        Assert.Equal("conn-host", host.ConnectionId);
        Assert.Equal("Mossy", host.Nickname); // build/host-identity: host names itself on HostSetup
        Assert.Equal("teal", host.Variant);   // the variant the host picked
    }

    [Fact]
    public void CreateRoom_generates_a_four_char_code_with_no_ambiguous_glyphs()
    {
        var registry = new RoomRegistry();

        // Many rooms: the alphabet exclusion must hold for every generated code,
        // not just by luck on one draw.
        for (var i = 0; i < 500; i++)
        {
            var code = registry.CreateRoom($"conn-{i}", "Mossy", "teal").Code;

            Assert.Equal(4, code.Length);
            foreach (var ch in code)
            {
                Assert.DoesNotContain(ch, AmbiguousGlyphs);
                // Codes are drawn from uppercase A-Z + digits 2-9 only.
                Assert.True(char.IsAsciiLetterUpper(ch) || char.IsAsciiDigit(ch));
            }
        }
    }

    [Fact]
    public void CreateRoom_produces_codes_unique_among_active_rooms()
    {
        var registry = new RoomRegistry();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < 300; i++)
        {
            var code = registry.CreateRoom($"conn-{i}", "Mossy", "teal").Code;
            Assert.True(seen.Add(code), $"Duplicate active room code generated: {code}");
        }

        Assert.Equal(300, registry.ActiveRoomCount);
    }

    [Fact]
    public void TryGet_finds_a_created_room_case_insensitively()
    {
        var registry = new RoomRegistry();
        var code = registry.CreateRoom("conn-host", "Mossy", "teal").Code;

        Assert.NotNull(registry.TryGet(code));
        Assert.NotNull(registry.TryGet(code.ToLowerInvariant()));
        Assert.Null(registry.TryGet("ZZZZ-not-a-real-code"));
    }
}
