// ----------------------------------------------------------------------------
//  RoomRoundLogicTests - unit tests for the group-play round engine on Room
//  (group-play/02, group-play/03), the highest-value gap flagged in review on
//  PR #48 (Room.cs + GameHub.SubmitWord had NO coverage before this file).
//
//  These exercise the REAL Room type end-to-end through its PUBLIC surface
//  (no mocking framework in the harness, and ComputeAssignments is PRIVATE, so
//  it is exercised only through StartRound's returned RoundState.Assignments -
//  the same authoritative path the hub relies on):
//
//    1. Round-robin assignment (via StartRound.Assignments):
//       - blank k -> player (k % N), host first, each bucket ascending.
//       - full coverage: every blank 0..M-1 assigned exactly once, no gaps/dupes.
//       - per-player counts differ by at most one.
//       - the AC-01 worked example (8 blanks / 5 players -> 2/2/2/1/1).
//       - PARITY with the pure TS reference web/src/engine/distribute.ts (see
//         web/src/engine/distribute.test.ts) - the C# dealing rule is hand-kept
//         in lockstep with that unit-tested spec; this file mirrors its exact
//         vectors and its N x M invariant sweep as the oracle for this mirror.
//       - fewer blanks than players -> extra players still get a (empty) bucket.
//    2. RecordSubmission:
//       - only the OWNING connection may submit its own blank; a non-owner is
//         Rejected and nothing is recorded.
//       - completion flips to "reveal" EXACTLY when the last assigned blank
//         lands (every earlier submit stays Recorded / "prompting").
//       - concurrency-safe: firing every owned submission concurrently yields
//         EXACTLY ONE RoundComplete and a final "reveal" phase (no double
//         completion); a submission after completion is Rejected.
//       - a duplicate re-submit of an already-recorded blank overwrites rather
//         than double-counting toward completion.
//    3. BuildReveal:
//       - words come back in BLANK ORDER, each filled position carrying the
//         submitting player's word + nickname + variant.
//       - an unfilled blank (round not completed) renders as an EMPTY
//         RevealWord, preserving positional alignment.
// ----------------------------------------------------------------------------

using QuibbleStone.Api.Rooms;

namespace QuibbleStone.Api.Tests;

public class RoomRoundLogicTests
{
    // --- Helpers ---------------------------------------------------------------

    // Builds a room with a host plus (playerCount - 1) joiners, named P1..P(n-1)
    // (P0 is the host "Host"). Returns the room and the players in SEATING order
    // (host first), which is also the order StartRound deals against.
    private static Room BuildRoomWithPlayers(int playerCount)
    {
        if (playerCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(playerCount));
        }

        var room = Room.CreateHosted("ABCD", "conn-0", "Host", "teal");
        for (var i = 1; i < playerCount; i += 1)
        {
            Assert.True(room.TryAddPlayer($"P{i}", "gold", $"conn-{i}"));
        }

        return room;
    }

    // Flattens every player's BlankIndices into one ascending list.
    private static List<int> AllAssigned(IReadOnlyList<PlayerAssignment> assignments)
    {
        return assignments.SelectMany(a => a.BlankIndices).OrderBy(i => i).ToList();
    }

    // --- Round-robin assignment (via StartRound.Assignments) -------------------

    [Theory]
    [InlineData(1, 6)]
    [InlineData(2, 5)]
    [InlineData(3, 10)]
    [InlineData(5, 8)]
    [InlineData(6, 4)]
    public void StartRound_deals_blank_k_to_player_k_mod_N_ascending(int playerCount, int blankCount)
    {
        var room = BuildRoomWithPlayers(playerCount);

        var round = room.StartRound("wobbly-wizard", "classic-blind", blankCount);

        Assert.Equal(playerCount, round.Assignments.Count);
        for (var playerIndex = 0; playerIndex < playerCount; playerIndex += 1)
        {
            var expected = Enumerable.Range(0, blankCount)
                .Where(k => k % playerCount == playerIndex)
                .ToList();

            Assert.Equal(expected, round.Assignments[playerIndex].BlankIndices);
        }
    }

    [Theory]
    [InlineData(1, 6)]
    [InlineData(2, 5)]
    [InlineData(3, 10)]
    [InlineData(5, 8)]
    [InlineData(5, 3)]
    [InlineData(7, 0)]
    public void StartRound_assigns_every_blank_exactly_once_no_gaps_no_duplicates(int playerCount, int blankCount)
    {
        var room = BuildRoomWithPlayers(playerCount);

        var round = room.StartRound("wobbly-wizard", "classic-blind", blankCount);

        var expected = Enumerable.Range(0, blankCount).ToList();
        Assert.Equal(expected, AllAssigned(round.Assignments));
    }

    [Theory]
    [InlineData(1, 6)]
    [InlineData(2, 5)]
    [InlineData(3, 10)]
    [InlineData(5, 8)]
    [InlineData(6, 4)]
    public void StartRound_keeps_per_player_counts_within_one_of_each_other(int playerCount, int blankCount)
    {
        var room = BuildRoomWithPlayers(playerCount);

        var round = room.StartRound("wobbly-wizard", "classic-blind", blankCount);

        var counts = round.Assignments.Select(a => a.BlankIndices.Count).ToList();
        Assert.True(counts.Max() - counts.Min() <= 1);
    }

    [Fact]
    public void StartRound_AC01_worked_example_8_blanks_5_players_is_2_2_2_1_1_host_first()
    {
        var room = BuildRoomWithPlayers(5);

        var round = room.StartRound("wobbly-wizard", "classic-blind", 8);

        // p0: 0,5   p1: 1,6   p2: 2,7   p3: 3   p4: 4
        var buckets = round.Assignments.Select(a => a.BlankIndices).ToList();
        Assert.Equal(new List<int> { 0, 5 }, buckets[0]);
        Assert.Equal(new List<int> { 1, 6 }, buckets[1]);
        Assert.Equal(new List<int> { 2, 7 }, buckets[2]);
        Assert.Equal(new List<int> { 3 }, buckets[3]);
        Assert.Equal(new List<int> { 4 }, buckets[4]);

        Assert.Equal(new[] { 2, 2, 2, 1, 1 }, round.Assignments.Select(a => a.BlankIndices.Count));

        // Host first (player index 0) is the host.
        Assert.True(round.Assignments[0].IsHost);
        Assert.False(round.Assignments[1].IsHost);
    }

    // ============================================================================
    // PARITY ORACLE: web/src/engine/distribute.ts / web/src/engine/distribute.test.ts
    // is the pure, unit-tested TS reference for the round-robin dealing rule; this
    // C# Room.StartRound distribution is HAND-KEPT identical to it (no codegen -
    // see the "MIRRORS distribute.ts" note on Room.ComputeAssignments). The cases
    // below reproduce the exact vectors pinned in distribute.test.ts so a drift
    // between the TS spec and this C# mirror shows up here.
    // ============================================================================

    [Fact]
    public void StartRound_matches_distribute_ts_vector_2_players_5_blanks()
    {
        // distributeBlanks(2, 5) -> [[0,2,4],[1,3]]
        var room = BuildRoomWithPlayers(2);
        var round = room.StartRound("wobbly-wizard", "classic-blind", 5);

        Assert.Equal(new List<int> { 0, 2, 4 }, round.Assignments[0].BlankIndices);
        Assert.Equal(new List<int> { 1, 3 }, round.Assignments[1].BlankIndices);
    }

    [Fact]
    public void StartRound_matches_distribute_ts_vector_5_players_3_blanks()
    {
        // distributeBlanks(5, 3) -> [[0],[1],[2],[],[]]
        var room = BuildRoomWithPlayers(5);
        var round = room.StartRound("wobbly-wizard", "classic-blind", 3);

        var buckets = round.Assignments.Select(a => a.BlankIndices).ToList();
        Assert.Equal(new List<int> { 0 }, buckets[0]);
        Assert.Equal(new List<int> { 1 }, buckets[1]);
        Assert.Equal(new List<int> { 2 }, buckets[2]);
        Assert.Empty(buckets[3]);
        Assert.Empty(buckets[4]);
    }

    [Fact]
    public void StartRound_matches_distribute_ts_vector_2_players_6_blanks_round_robin_not_chunked()
    {
        // distributeBlanks(2, 6) -> [[0,2,4],[1,3,5]] (round-robin, not chunked
        // as [[0,1,2],[3,4,5]] would be).
        var room = BuildRoomWithPlayers(2);
        var round = room.StartRound("wobbly-wizard", "classic-blind", 6);

        Assert.Equal(new List<int> { 0, 2, 4 }, round.Assignments[0].BlankIndices);
        Assert.Equal(new List<int> { 1, 3, 5 }, round.Assignments[1].BlankIndices);
    }

    [Fact]
    public void StartRound_invariant_sweep_mirrors_distribute_ts_for_N_1_to_8_M_0_to_20()
    {
        // Mirrors the TS invariant sweep in distribute.test.ts: for every N in
        // 1..8 and M in 0..20, coverage is exactly 0..M-1 (no gaps/dupes) and the
        // per-player count spread is at most one.
        for (var playerCount = 1; playerCount <= 8; playerCount += 1)
        {
            for (var blankCount = 0; blankCount <= 20; blankCount += 1)
            {
                var room = BuildRoomWithPlayers(playerCount);
                var round = room.StartRound("wobbly-wizard", "classic-blind", blankCount);

                var expectedCoverage = Enumerable.Range(0, blankCount).ToList();
                Assert.Equal(expectedCoverage, AllAssigned(round.Assignments));

                var counts = round.Assignments.Select(a => a.BlankIndices.Count).ToList();
                Assert.True(
                    counts.Max() - counts.Min() <= 1,
                    $"N={playerCount}, M={blankCount}: counts {string.Join(",", counts)} spread more than 1");
            }
        }
    }

    [Fact]
    public void StartRound_with_fewer_blanks_than_players_still_gives_every_player_a_bucket()
    {
        var room = BuildRoomWithPlayers(5);

        var round = room.StartRound("wobbly-wizard", "classic-blind", 3);

        Assert.Equal(5, round.Assignments.Count);
        // The two extra players (indices 3, 4) get empty - but present - buckets.
        Assert.Empty(round.Assignments[3].BlankIndices);
        Assert.Empty(round.Assignments[4].BlankIndices);
        // Everyone still has an assignment record (nickname/variant/connection intact).
        Assert.All(round.Assignments, a => Assert.False(string.IsNullOrEmpty(a.ConnectionId)));
    }

    // --- RecordSubmission --------------------------------------------------------

    [Fact]
    public void RecordSubmission_rejects_a_blank_not_owned_by_the_calling_connection()
    {
        var room = BuildRoomWithPlayers(2);
        var round = room.StartRound("wobbly-wizard", "classic-blind", 5);
        // p0 owns [0,2,4]; p1 owns [1,3]. conn-0 tries to submit blank 1 (p1's).

        var outcome = room.RecordSubmission("conn-0", 1, "banana");

        Assert.Equal(Room.SubmitOutcome.Rejected, outcome);
        Assert.Empty(room.CurrentRound!.Submissions);
    }

    [Fact]
    public void RecordSubmission_accepts_the_true_owner_submitting_its_own_blank()
    {
        var room = BuildRoomWithPlayers(2);
        room.StartRound("wobbly-wizard", "classic-blind", 5);

        // conn-1 owns blank 1.
        var outcome = room.RecordSubmission("conn-1", 1, "banana");

        Assert.Equal(Room.SubmitOutcome.Recorded, outcome);
        Assert.True(room.CurrentRound!.Submissions.ContainsKey(1));
        Assert.Equal("banana", room.CurrentRound!.Submissions[1].Word);
    }

    [Fact]
    public void RecordSubmission_completes_the_round_exactly_when_the_last_blank_lands()
    {
        var room = BuildRoomWithPlayers(2);
        var round = room.StartRound("wobbly-wizard", "classic-blind", 5);
        // p0 (conn-0) owns [0,2,4]; p1 (conn-1) owns [1,3].

        var ownerByBlank = round.Assignments
            .SelectMany(a => a.BlankIndices.Select(idx => (idx, a.ConnectionId)))
            .OrderBy(pair => pair.idx)
            .ToList();

        Assert.Equal(5, ownerByBlank.Count);

        for (var i = 0; i < ownerByBlank.Count; i += 1)
        {
            var (blankIndex, connectionId) = ownerByBlank[i];
            var outcome = room.RecordSubmission(connectionId, blankIndex, $"word-{blankIndex}");

            var isLast = i == ownerByBlank.Count - 1;
            if (isLast)
            {
                Assert.Equal(Room.SubmitOutcome.RoundComplete, outcome);
                Assert.Equal("reveal", room.CurrentRound!.Phase);
            }
            else
            {
                Assert.Equal(Room.SubmitOutcome.Recorded, outcome);
                Assert.Equal("prompting", room.CurrentRound!.Phase);
            }
        }
    }

    [Fact]
    public void RecordSubmission_is_rejected_once_the_round_has_moved_to_reveal()
    {
        var room = BuildRoomWithPlayers(1);
        room.StartRound("wobbly-wizard", "classic-blind", 1);

        // The single player (host) owns blank 0 - submit it to complete the round.
        var complete = room.RecordSubmission("conn-0", 0, "banana");
        Assert.Equal(Room.SubmitOutcome.RoundComplete, complete);
        Assert.Equal("reveal", room.CurrentRound!.Phase);

        // A late/duplicate submission after completion is rejected.
        var late = room.RecordSubmission("conn-0", 0, "late-word");
        Assert.Equal(Room.SubmitOutcome.Rejected, late);
        // The reveal-phase word is untouched by the rejected late attempt.
        Assert.Equal("banana", room.CurrentRound!.Submissions[0].Word);
    }

    [Fact]
    public async Task RecordSubmission_is_concurrency_safe_exactly_one_RoundComplete_and_final_reveal_phase()
    {
        // A meatier shape (5 players, 8 blanks -> 2/2/2/1/1) so several
        // submissions race to be "the last one" from different threads.
        var room = BuildRoomWithPlayers(5);
        var round = room.StartRound("wobbly-wizard", "classic-blind", 8);

        var ownedPairs = round.Assignments
            .SelectMany(a => a.BlankIndices.Select(idx => (a.ConnectionId, idx)))
            .Distinct()
            .ToList();

        Assert.Equal(8, ownedPairs.Count);

        var tasks = ownedPairs
            .Select(pair => Task.Run(() => room.RecordSubmission(pair.ConnectionId, pair.idx, $"word-{pair.idx}")))
            .ToArray();

        var outcomes = await Task.WhenAll(tasks);

        Assert.Equal(1, outcomes.Count(o => o == Room.SubmitOutcome.RoundComplete));
        Assert.Equal(7, outcomes.Count(o => o == Room.SubmitOutcome.Recorded));
        Assert.DoesNotContain(Room.SubmitOutcome.Rejected, outcomes);
        Assert.Equal("reveal", room.CurrentRound!.Phase);
        Assert.Equal(8, room.CurrentRound!.Submissions.Count);
    }

    [Fact]
    public void RecordSubmission_duplicate_resubmit_overwrites_without_double_counting_toward_completion()
    {
        var room = BuildRoomWithPlayers(2);
        var round = room.StartRound("wobbly-wizard", "classic-blind", 2);
        // p0 (conn-0) owns [0]; p1 (conn-1) owns [1].

        var first = room.RecordSubmission("conn-0", 0, "apple");
        Assert.Equal(Room.SubmitOutcome.Recorded, first);

        // Re-submit the SAME blank before the round completes - it must overwrite,
        // not somehow register as a second distinct submission.
        var overwrite = room.RecordSubmission("conn-0", 0, "avocado");
        Assert.Equal(Room.SubmitOutcome.Recorded, overwrite);
        Assert.Equal("avocado", room.CurrentRound!.Submissions[0].Word);
        Assert.Single(room.CurrentRound!.Submissions);

        // Completing the round still requires exactly the OTHER outstanding blank -
        // the duplicate did not silently complete it early.
        var complete = room.RecordSubmission("conn-1", 1, "banana");
        Assert.Equal(Room.SubmitOutcome.RoundComplete, complete);
        Assert.Equal(2, room.CurrentRound!.Submissions.Count);
    }

    // --- BuildReveal --------------------------------------------------------------

    [Fact]
    public void BuildReveal_returns_words_in_blank_order_with_submitter_attribution()
    {
        var room = BuildRoomWithPlayers(2);
        var round = room.StartRound("wobbly-wizard", "classic-blind", 4);
        // p0 (conn-0, Host/teal) owns [0,2]; p1 (conn-1, P1/gold) owns [1,3].

        room.RecordSubmission("conn-0", 0, "wobbly");
        room.RecordSubmission("conn-1", 1, "sparkly");
        room.RecordSubmission("conn-0", 2, "wizard");
        var final = room.RecordSubmission("conn-1", 3, "hat");
        Assert.Equal(Room.SubmitOutcome.RoundComplete, final);

        var reveal = room.BuildReveal();

        Assert.Equal(4, reveal.Count);
        Assert.Equal(new RevealWord("wobbly", "Host", "teal"), reveal[0]);
        Assert.Equal(new RevealWord("sparkly", "P1", "gold"), reveal[1]);
        Assert.Equal(new RevealWord("wizard", "Host", "teal"), reveal[2]);
        Assert.Equal(new RevealWord("hat", "P1", "gold"), reveal[3]);
    }

    [Fact]
    public void BuildReveal_renders_unfilled_blanks_as_empty_preserving_positional_alignment()
    {
        var room = BuildRoomWithPlayers(2);
        room.StartRound("wobbly-wizard", "classic-blind", 4);
        // p0 owns [0,2]; p1 owns [1,3]. Only submit SOME blanks - the round never
        // completes, so BuildReveal must still be callable and show gaps as empty.

        room.RecordSubmission("conn-0", 0, "wobbly");
        room.RecordSubmission("conn-1", 3, "hat");
        // Blanks 1 and 2 deliberately left unfilled.

        var reveal = room.BuildReveal();

        Assert.Equal(4, reveal.Count);
        Assert.Equal(new RevealWord("wobbly", "Host", "teal"), reveal[0]);
        Assert.Equal(new RevealWord(string.Empty, string.Empty, string.Empty), reveal[1]);
        Assert.Equal(new RevealWord(string.Empty, string.Empty, string.Empty), reveal[2]);
        Assert.Equal(new RevealWord("hat", "P1", "gold"), reveal[3]);
    }
}
