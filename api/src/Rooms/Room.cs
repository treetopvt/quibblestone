// ----------------------------------------------------------------------------
//  Room + Player - the ephemeral in-memory room model (session-engine/01).
//
//  QuibbleStone is a toy, not a system of record (README section 4, CLAUDE.md
//  section 10): a room lives only in the memory of the in-process SignalR hub
//  for the duration of a play session. There is NO database and NO durable
//  persistence - when the process restarts, rooms are gone, and that is fine.
//
//  A Room carries:
//    - Code       : the short human-friendly join code (4 chars, unambiguous
//                   alphabet - generated + kept unique by RoomRegistry).
//    - Players    : the roster. The host is the first player (IsHost = true);
//                   later stories (02 join, 05 avatar) append joiners.
//    - LastActive : a sliding-window timestamp bumped on activity. RoomRegistry
//                   sweeps rooms idle past the inactivity window (AC-05).
//
//  A Player is the anonymous, minimal record the charter allows (README
//  sections 3 and 6 - no PII): a Nickname, a Guardian Variant, the owning
//  SignalR ConnectionId (so later stories can find the player on leave), and
//  the IsHost flag. Story 01 only ever creates the host player; story 02 fills
//  in real joiner nicknames (routed through the safety filter first), and story
//  05 lets joiners pick a Variant. The host defaults to the "teal" variant.
//
//  Concurrency: SignalR invokes can run concurrently across connections, so the
//  Players list is guarded by a per-room lock (see the mutation helpers). The
//  registry owns cross-room concurrency (the room dictionary).
// ----------------------------------------------------------------------------

namespace QuibbleStone.Api.Rooms;

/// <summary>
/// One anonymous player in a room. Minimal by design (no PII, README section 6):
/// just an in-session nickname, a Guardian variant, the owning connection, and
/// whether this player is the host.
/// </summary>
/// <param name="Nickname">In-session display name (the host now picks one on HostSetup before the room is minted - see <see cref="Room.CreateHosted"/>).</param>
/// <param name="Variant">Guardian avatar variant (the host picks one on HostSetup; joiners pick on Join).</param>
/// <param name="ConnectionId">The SignalR connection that owns this player - used for leave-detection in story 03.</param>
/// <param name="IsHost">True for the room creator; false for joiners.</param>
public sealed record Player(
    string Nickname,
    string Variant,
    string ConnectionId,
    bool IsHost);

/// <summary>
/// One player's blank assignment within a round (group-play/02). Records WHICH
/// blank indices this player owes, keyed server-side by the owning connection,
/// alongside just enough anonymous identity (nickname + Guardian variant) for
/// group-play/04's per-player word-count attribution on Round Complete.
///
/// The <see cref="ConnectionId"/> is a SERVER-SIDE handle only - it (like the
/// connectionId on <see cref="Player"/>) is NEVER put on the wire (no PII,
/// README section 6). The hub uses it to send each player ONLY its own blanks
/// (Clients.Client(connectionId)); a client never learns another player's
/// connection or blanks. <see cref="BlankIndices"/> are indices into the
/// template's ordered blanks (getBlanks(template)[i]) - index-based to match the
/// pure TS reference (web/src/engine/distribute.ts), which is why the catalog
/// carries BlankCount.
/// </summary>
/// <param name="ConnectionId">The owning SignalR connection (server-side handle; never on the wire).</param>
/// <param name="Nickname">The player's in-session nickname (for gp/04 attribution; already safety-filtered on join).</param>
/// <param name="Variant">The player's Guardian variant (for gp/04 attribution).</param>
/// <param name="IsHost">True for the host player (dealt first, player index 0).</param>
/// <param name="BlankIndices">The blank indices (into the template's ordered blanks) this player owes; may be empty when there are fewer blanks than players.</param>
public sealed record PlayerAssignment(
    string ConnectionId,
    string Nickname,
    string Variant,
    bool IsHost,
    IReadOnlyList<int> BlankIndices);

/// <summary>
/// One collected submission within a round (group-play/03): a single blank's
/// submitted word, tagged with just enough anonymous identity (nickname +
/// Guardian variant) to attribute the word to its author on the reveal, exactly
/// the way the web engine's SubmittedWord attribution does client-side.
///
/// The <see cref="Word"/> has ALREADY passed the server-side content-safety
/// filter before it was recorded (AC-01, AC-06) - a blocked word never reaches
/// this record. An EMPTY word is a legitimate SKIP placeholder (the player chose
/// to skip the blank), which keeps positional alignment in the reveal, matching
/// the web engine's skipBlank rule. No connectionId is stored here - it stays a
/// server-side handle on <see cref="PlayerAssignment"/> (no PII, README section 6).
/// </summary>
/// <param name="Word">The submitted (already safety-vetted) word; empty for a skip placeholder.</param>
/// <param name="Nickname">The submitting player's in-session nickname (already filtered on join; for reveal attribution).</param>
/// <param name="Variant">The submitting player's Guardian variant (for reveal attribution).</param>
public sealed record Submission(string Word, string Nickname, string Variant);

/// <summary>
/// The reveal payload for ONE blank position (group-play/03), in blank ORDER: the
/// submitted word plus its owning player (nickname + Guardian variant). This is
/// the server's ordered projection of the round's submissions, positionally
/// aligned to the template's ordered blanks (blank index 0..M-1) so the web
/// engine's assemble() can pair each word to its blank purely by position - the
/// SAME positional contract solo relies on.
///
/// A blank with no submission (edge: a player left before submitting) renders as
/// an EMPTY word attributed to no one, so alignment holds and the story simply
/// reads blank there. No connectionId, no PII beyond the already-filtered
/// nickname + variant (README section 6).
/// </summary>
/// <param name="Word">The submitted word for this blank position; empty when no submission exists (a left player).</param>
/// <param name="Nickname">The owning player's nickname, or empty when no submission exists.</param>
/// <param name="Variant">The owning player's Guardian variant, or empty when no submission exists.</param>
public sealed record RevealWord(string Word, string Nickname, string Variant);

/// <summary>
/// One player's word-collection progress within a round (group-play/03): who they
/// are (anonymous nickname + Guardian variant) and whether they have submitted
/// ALL of their assigned blanks yet. This is the shape the hub's "CollectProgress"
/// broadcast carries so every client can render the Waiting screen's progress row
/// (done at full opacity + teal check, still-writing dimmed + pulsing badge).
///
/// It deliberately carries NO submitted words (AC-01: words are never shown to
/// other players before the reveal) and no connectionId (server-side handle only,
/// no PII, README section 6) - just the done/writing status per player.
/// </summary>
/// <param name="Nickname">The player's in-session nickname (already filtered on join).</param>
/// <param name="Variant">The player's Guardian variant.</param>
/// <param name="Done">True once this player has submitted every blank it was assigned.</param>
public sealed record PlayerProgress(string Nickname, string Variant, bool Done);

/// <summary>
/// The mutable state of the room's CURRENT round (group-play). Null while the
/// room sits in the lobby; set by <see cref="Room.StartRound"/> when the host
/// starts a round (group-play/01) and mutated in place under the room lock as the
/// round progresses.
///
/// Deliberately EXTENSIBLE - later group-play stories grow this same record
/// rather than adding parallel round bookkeeping:
///   - group-play/02 adds per-player blank assignments (index-based, which is why
///     the catalog already carries BlankCount) - see <see cref="Assignments"/>.
///   - group-play/03 adds collected submissions (<see cref="Submissions"/>) + moves
///     <see cref="Phase"/> from "prompting" to "reveal".
///   - group-play/04 increments <see cref="RoundNumber"/> and resets the phase for
///     the replay loop (a play-again in the SAME room -> round 2, 3, ...), and
///     reads <see cref="Assignments"/> for word-count attribution. A back-to-lobby
///     that clears the round (<see cref="Room.BackToLobby"/>) resets the next
///     start back to round 1.
/// group-play/01 only sets the opening shape below.
/// </summary>
public sealed class RoundState
{
    /// <summary>1-based round number; group-play/04 increments it on replay (round 2, 3, ...).</summary>
    public required int RoundNumber { get; set; }

    /// <summary>The selected template's id - the key the client resolves full content from (seedLibrary).</summary>
    public required string TemplateId { get; set; }

    /// <summary>The play mode. Classic blind only for Slice 1 ("classic-blind").</summary>
    public required string Mode { get; set; }

    /// <summary>
    /// The round's lifecycle phase: "prompting" once a round starts (players are
    /// collecting words); "reveal" once every assigned blank is submitted
    /// (group-play/03 advances it in <see cref="Room.RecordSubmission"/>). (The
    /// lobby itself is represented by <see cref="Room.CurrentRound"/> being null,
    /// not a phase value.)
    /// </summary>
    public required string Phase { get; set; }

    /// <summary>
    /// The per-player round-robin blank assignment (group-play/02): who owes which
    /// blank indices. Computed and set by <see cref="Room.StartRound"/> when the
    /// round opens, ordered host-first (the roster order the deal used). Empty only
    /// in the degenerate case of a contentless template (blankCount 0). The hub
    /// reads this to send each player its own blanks (blind); group-play/04 reads it
    /// for word-count attribution. Snapshots return a detached copy so reads outside
    /// the lock never observe a later mutation.
    /// </summary>
    public IReadOnlyList<PlayerAssignment> Assignments { get; set; } = [];

    /// <summary>
    /// The collected submissions so far (group-play/03), keyed by blank INDEX into
    /// the template's ordered blanks. A blank index appears here once its owning
    /// player has submitted it (a real word, or an empty skip placeholder). Mutated
    /// ONLY under the room lock (see <see cref="Room.RecordSubmission"/>);
    /// <see cref="Room.CurrentRound"/> hands back a detached copy so reads outside
    /// the lock never observe a mid-mutation dictionary. Never populated with a word
    /// that failed the safety filter - the hub vets before recording (AC-01, AC-06).
    /// </summary>
    public IReadOnlyDictionary<int, Submission> Submissions { get; set; } =
        new Dictionary<int, Submission>();
}

/// <summary>
/// An ephemeral, in-memory game room. Not persisted anywhere (CLAUDE.md
/// section 10): it lives in the <see cref="RoomRegistry"/> only while active.
/// </summary>
public sealed class Room
{
    // Guards the mutable roster AND the current round - SignalR invokes on
    // different connections can touch the same room concurrently.
    private readonly object _gate = new();
    private readonly List<Player> _players = [];

    // The current round, or null while the room is in the lobby. Guarded by
    // _gate: mutated only under the lock (see StartRound), snapshotted for reads.
    private RoundState? _round;

    // story-selection/03 (freshness rotation, AC-02): the ordered, in-memory,
    // room-lifetime history of template ids this room has already played,
    // oldest-first / most-recently-played last - the shape
    // FreshnessContentSelector.SelectFreshOrRecycle's recycle step wants. Lives
    // ONLY on this Room (ephemeral, dies with the room, README section 4/10 -
    // this is a toy, not a system of record) and is NEVER persisted, mirroring
    // the solo device's localStorage-backed history
    // (web/src/content/playedHistory.ts) one level down: same "ids only, no
    // words, no PII" contract (AC-06), just server-side and per-room instead of
    // device-local. Guarded by _gate; mutated only via MarkTemplatePlayed.
    private readonly List<string> _playedTemplateIds = [];

    // A generous ceiling on how many template ids the per-room history can
    // hold - well above any realistic catalog size (today's catalog is under
    // 20 entries, and MarkTemplatePlayed dedupes so this list can never exceed
    // the number of DISTINCT templates ever played). A defensive backstop
    // against unbounded growth, not a limit a normal session should ever reach
    // (mirrors the web module's MAX_HISTORY_SIZE rationale).
    private const int MaxPlayedTemplateHistory = 200;

    // reveal-delight/01 (AC-04): the room-wide reaction tally for the CURRENT
    // reveal - one counter per allowed reaction type (laugh/heart/wow/star). This
    // is ephemeral, per-reveal state (Out of Scope: no persistence): it is RESET
    // to all-zero every time a new round starts (see StartRound). A reaction is a
    // TYPE ENUM only - no text, no player identity is ever stored here (AC-06, no
    // PII). Guarded by _gate; mutated only via IncrementReaction / the StartRound
    // reset. The hub validates the type against the allowed set BEFORE calling
    // IncrementReaction, so every key here is always one of the four.
    private readonly Dictionary<string, int> _reactionCounts = NewReactionTally();

    // A fresh all-zero tally over exactly the four allowed reaction types. The
    // ordinal comparer keeps the (already-lowercased-by-the-hub) keys exact.
    private static Dictionary<string, int> NewReactionTally() =>
        new(StringComparer.Ordinal)
        {
            ["laugh"] = 0,
            ["heart"] = 0,
            ["wow"] = 0,
            ["star"] = 0,
        };

    private Room(string code)
    {
        Code = code;
        // story-selection/04 (anonymous serve log, AC-01): mint an OPAQUE instance
        // id ONCE per room. This is deliberately NOT the join code - the serve log
        // must be able to say "these serves came from the same room instance"
        // WITHOUT ever storing anything a person could be traced by (the code is
        // shown to players and typed to join; this GUID is server-only and
        // meaningless outside the log). It never leaves the server except as an
        // anonymous field on a ServeEvent (AC-04).
        InstanceId = Guid.NewGuid().ToString();
        LastActiveUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>The short, human-friendly join code (4 chars, unambiguous alphabet).</summary>
    public string Code { get; }

    /// <summary>
    /// An OPAQUE, per-room instance id (story-selection/04, AC-01): a GUID minted
    /// once when the room is created, used ONLY as an anonymous grouping key in the
    /// serve log. It is NOT the join code and carries no PII - it just lets an
    /// engineer see that several serves belong to the same room instance without
    /// ever learning who was in it (AC-04).
    /// </summary>
    public string InstanceId { get; }

    /// <summary>
    /// Last time this room saw activity. Bumped by <see cref="Touch"/>; the
    /// registry sweeps rooms idle past the inactivity window (AC-05).
    /// </summary>
    public DateTimeOffset LastActiveUtc { get; private set; }

    /// <summary>
    /// Creates a room with the given code and its host as the first player.
    ///
    /// build/host-identity: the host now carries a REAL display name + Guardian
    /// variant (the host picks both on the HostSetup screen before the room is
    /// minted, mirroring the joiner name step). The name has ALREADY been trimmed,
    /// length-checked, and vetted by the content-safety filter server-side by the
    /// caller (the hub's CreateRoom) BEFORE it reaches here - this method takes both
    /// values as-given and never vets them, exactly like <see cref="TryAddPlayer"/>
    /// takes a pre-vetted joiner name. The variant is likewise expected to be
    /// already normalized to one of the six known values. This closes the earlier
    /// gap where the host was seated with an empty nickname + the default "teal"
    /// variant, so the host showed blank in the lobby, reveal, and recap.
    /// </summary>
    /// <param name="code">The room's minted join code.</param>
    /// <param name="hostConnectionId">The host's SignalR connection.</param>
    /// <param name="nickname">The vetted, trimmed host display name.</param>
    /// <param name="variant">The host's already-normalized Guardian variant.</param>
    public static Room CreateHosted(string code, string hostConnectionId, string nickname, string variant)
    {
        var room = new Room(code);
        room._players.Add(new Player(
            Nickname: nickname,
            Variant: variant,
            ConnectionId: hostConnectionId,
            IsHost: true));
        return room;
    }

    /// <summary>Bumps the sliding-window activity timestamp (AC-05).</summary>
    public void Touch()
    {
        lock (_gate)
        {
            LastActiveUtc = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Attempts to add a joiner to the roster (session-engine/02). The nickname
    /// must be unique within the room, compared case-insensitively (AC-06), so
    /// "Sam" and "sam" cannot both be in the same room. The uniqueness check and
    /// the append happen together under the room lock, so two joiners racing for
    /// the same name cannot both slip in.
    ///
    /// The caller (the hub) is responsible for having already routed the nickname
    /// through the content-safety filter (README section 6) BEFORE calling this -
    /// this method takes the name as-given and never vets it. It only enforces the
    /// in-room uniqueness invariant and appends the player.
    /// </summary>
    /// <param name="nickname">The vetted, trimmed in-session display name.</param>
    /// <param name="variant">The chosen Guardian variant.</param>
    /// <param name="connectionId">The joiner's SignalR connection.</param>
    /// <returns>True if the player was added; false if the name is already taken.</returns>
    public bool TryAddPlayer(string nickname, string variant, string connectionId)
    {
        lock (_gate)
        {
            var taken = _players.Any(p =>
                string.Equals(p.Nickname, nickname, StringComparison.OrdinalIgnoreCase));
            if (taken)
            {
                return false;
            }

            _players.Add(new Player(
                Nickname: nickname,
                Variant: variant,
                ConnectionId: connectionId,
                IsHost: false));

            LastActiveUtc = DateTimeOffset.UtcNow;
            return true;
        }
    }

    /// <summary>
    /// Removes the player owning the given connection from the roster
    /// (session-engine/03 leave-detection). Called when a connection drops
    /// (the app is closed, the tab navigates away, or the network dies) so the
    /// departed player's tile reverts to an empty slot on every remaining
    /// client (AC-04). The removal and the emptiness check the caller makes
    /// afterwards both run under the room lock via this method + <see
    /// cref="IsEmpty"/>, so a concurrent leave cannot corrupt the roster.
    ///
    /// A connection is only ever seated once in a room (Slice 1), so at most one
    /// player is removed. Removing a connection that is not in this room is a
    /// no-op that returns false.
    /// </summary>
    /// <param name="connectionId">The SignalR connection that dropped.</param>
    /// <returns>True if a player was removed; false if the connection was not seated here.</returns>
    public bool RemovePlayer(string connectionId)
    {
        lock (_gate)
        {
            var removed = _players.RemoveAll(p => p.ConnectionId == connectionId);
            if (removed == 0)
            {
                return false;
            }

            LastActiveUtc = DateTimeOffset.UtcNow;
            return true;
        }
    }

    /// <summary>
    /// True when the roster is empty (the last player has left). The registry
    /// uses this after a <see cref="RemovePlayer"/> to drop an abandoned room
    /// immediately rather than waiting for the idle sweep (session-engine/03).
    /// </summary>
    public bool IsEmpty
    {
        get
        {
            lock (_gate)
            {
                return _players.Count == 0;
            }
        }
    }

    /// <summary>A point-in-time snapshot of the roster (safe to hand out; a copy).</summary>
    public IReadOnlyList<Player> SnapshotPlayers()
    {
        lock (_gate)
        {
            return _players.ToArray();
        }
    }

    /// <summary>
    /// The number of players currently seated in the room. Read under the lock so
    /// it never observes a torn roster mid-mutation. Used by group-play/01 to
    /// enforce the "at least one other player" rule before a round starts (AC-01).
    /// </summary>
    public int PlayerCount
    {
        get
        {
            lock (_gate)
            {
                return _players.Count;
            }
        }
    }

    /// <summary>
    /// True when the given connection owns the room's HOST player (group-play/01,
    /// AC-03). The server-authoritative host check: the host is the single roster
    /// entry with IsHost == true, and only the connection that created the room
    /// owns it. Guarded by the room lock so it never races a roster mutation.
    ///
    /// This is why the host check cannot be trusted to the client - IsHost is on
    /// the wire as an anonymous flag with no connection identity, so a non-host
    /// client could claim to be the host; only the SERVER can tie a connection to
    /// the host player, which is exactly what this method does.
    /// </summary>
    /// <param name="connectionId">The calling connection to test.</param>
    /// <returns>True if that connection owns the host player; false otherwise.</returns>
    public bool IsHost(string connectionId)
    {
        if (string.IsNullOrEmpty(connectionId))
        {
            return false;
        }

        lock (_gate)
        {
            return _players.Any(p => p.IsHost && p.ConnectionId == connectionId);
        }
    }

    /// <summary>
    /// Opens a new round on this room (group-play/01 + /02). Sets the round state
    /// (round number, selected template id, mode, the "prompting" phase, and the
    /// per-player round-robin blank assignment) under the room lock so a concurrent
    /// roster change cannot interleave, and returns a snapshot of the new round for
    /// the hub to broadcast + to deal each player its own blanks.
    ///
    /// group-play/02: the blank distribution is computed HERE, under the lock, from
    /// the roster snapshot at round-start time - so the deal is atomic with the
    /// roster it dealt to (a join/leave racing StartRound either lands fully before
    /// the snapshot or fully after, never mid-deal). See <see cref="ComputeAssignments"/>
    /// for the round-robin rule (blank index k -> player index k % N, host first),
    /// which MIRRORS the pure TS reference web/src/engine/distribute.ts.
    ///
    /// group-play/04: the round number now INCREMENTS off the previous round rather
    /// than being hardcoded to 1 - RoundNumber = (_round?.RoundNumber ?? 0) + 1. So a
    /// fresh room (no prior round) opens round 1, a play-again in the SAME room opens
    /// round 2, 3, ..., and a <see cref="BackToLobby"/> that clears _round back to null
    /// resets the NEXT start to round 1. The phase always resets to "prompting" and a
    /// fresh empty submission store is dealt, so the replay loop reuses this one method
    /// (no parallel "restart" path). group-play/03 layers submissions onto the same
    /// <see cref="RoundState"/>.
    /// </summary>
    /// <param name="templateId">The auto-selected template's id (resolved to content client-side).</param>
    /// <param name="mode">The play mode ("classic-blind" for Slice 1).</param>
    /// <param name="blankCount">The template's blank count (from the catalog); dealt round-robin across the roster (group-play/02).</param>
    /// <returns>A snapshot of the round just started (including the assignment), safe to hand to the broadcast and per-player deal.</returns>
    public RoundState StartRound(string templateId, string mode, int blankCount)
    {
        lock (_gate)
        {
            var assignments = ComputeAssignments(_players, blankCount);

            _round = new RoundState
            {
                // group-play/04: increment off the previous round (or 0 when the room
                // has never started one), so a play-again in the same room -> round 2,
                // 3, ... and a BackToLobby (which nulls _round) resets the next to 1.
                RoundNumber = (_round?.RoundNumber ?? 0) + 1,
                TemplateId = templateId,
                Mode = mode,
                Phase = "prompting",
                Assignments = assignments,
                // group-play/03: a fresh, empty submission store for the new round.
                Submissions = new Dictionary<int, Submission>(),
            };

            // reveal-delight/01 (AC-04): reactions are ephemeral per reveal, so a
            // fresh round starts every reaction count back at zero (the web hook
            // resets its mirror on the matching RoundStarted broadcast).
            foreach (var type in _reactionCounts.Keys.ToArray())
            {
                _reactionCounts[type] = 0;
            }

            LastActiveUtc = DateTimeOffset.UtcNow;

            // Hand back a detached copy so callers reading it outside the lock
            // never observe a later in-place mutation of the live round. The
            // assignment list is freshly built (immutable records over an int[]),
            // so it is safe to share directly.
            return new RoundState
            {
                RoundNumber = _round.RoundNumber,
                TemplateId = _round.TemplateId,
                Mode = _round.Mode,
                Phase = _round.Phase,
                Assignments = _round.Assignments,
                // A round just started, so there are no submissions yet; hand back
                // a fresh empty copy so this snapshot never aliases the live store.
                Submissions = new Dictionary<int, Submission>(),
            };
        }
    }

    /// <summary>
    /// Ends the current round and returns the room to the LOBBY (group-play/04,
    /// AC-05). Clears <see cref="_round"/> back to null under the room lock so the
    /// room is once again "in the lobby" - but WITHOUT touching the roster or the
    /// code: the room stays live and every player is preserved, so the host can
    /// start a fresh round without anyone re-joining or re-entering a code. Because
    /// the round number lives on <see cref="RoundState"/> (now null), the NEXT
    /// <see cref="StartRound"/> increments off 0 and opens round 1 again.
    ///
    /// Idempotent: calling this when the room is already in the lobby (_round is
    /// null) is a harmless no-op. Guarded by the same <see cref="_gate"/> lock as
    /// every other round mutation, so it never races a submission or a start.
    /// </summary>
    public void BackToLobby()
    {
        lock (_gate)
        {
            _round = null;
            LastActiveUtc = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// A point-in-time snapshot of this room's played-template history
    /// (story-selection/03, AC-02): the ids already played in this room,
    /// oldest-first / most-recently-played last - the input
    /// <see cref="Safety.FreshnessContentSelector.SelectFreshOrRecycle"/> reads
    /// to filter/recycle the next round's pool. Ephemeral (in-memory, room-
    /// lifetime only) and ID-ONLY (AC-06 - no words, no PII). Returns a
    /// detached copy so a caller reading it outside the lock never observes a
    /// later <see cref="MarkTemplatePlayed"/> mutation.
    /// </summary>
    public IReadOnlyList<string> PlayedTemplateIds
    {
        get
        {
            lock (_gate)
            {
                return _playedTemplateIds.ToArray();
            }
        }
    }

    /// <summary>
    /// Records <paramref name="templateId"/> as just-played in this room
    /// (story-selection/03, AC-02), for the NEXT StartRound's freshness filter
    /// to exclude it. Dedupes: if the id is already present it is moved to the
    /// end (it is the most recently played again, e.g. after a recycle),
    /// otherwise it is appended. Trims from the FRONT (oldest dropped first) if
    /// the history would exceed <see cref="MaxPlayedTemplateHistory"/> - a
    /// defensive backstop, not a limit normal play should reach (see the field
    /// comment). Mutated only under the room lock.
    ///
    /// AC-04 bypass seam: this is called from GameHub.StartRound's RANDOM-pick
    /// path only. A FUTURE pinned-template replay (replay-remix/01, "carve it
    /// again") must SKIP this call entirely for that round - replaying a
    /// favorite must not make the random pick "forget" the other templates this
    /// room has not seen yet. No such path exists today; see the comment at the
    /// GameHub.StartRound call site marking exactly where that branch would go.
    /// </summary>
    /// <param name="templateId">The id of the template a random pick just served.</param>
    public void MarkTemplatePlayed(string templateId)
    {
        lock (_gate)
        {
            _playedTemplateIds.Remove(templateId);
            _playedTemplateIds.Add(templateId);
            if (_playedTemplateIds.Count > MaxPlayedTemplateHistory)
            {
                _playedTemplateIds.RemoveAt(0);
            }
        }
    }

    /// <summary>
    /// Increments the room-wide count for one reaction type (reveal-delight/01,
    /// AC-04) and returns a detached snapshot of the whole tally for the hub to
    /// broadcast. The caller (the hub's React) MUST have already validated
    /// <paramref name="reactionType"/> against the four allowed types (and
    /// lowercased it), so the key is always present - this method never widens the
    /// tally with an arbitrary client-supplied string. A reaction is a TYPE ENUM
    /// only: no text and no player identity are recorded (AC-06, no PII). The
    /// counts are ephemeral per reveal and are reset in <see cref="StartRound"/>.
    /// Mutated only under the room lock so a concurrent reaction cannot corrupt
    /// the tally, and the returned copy never aliases the live dictionary.
    /// </summary>
    /// <param name="reactionType">The already-validated, lowercased reaction type (one of laugh/heart/wow/star).</param>
    /// <returns>A detached copy of the updated per-type tally.</returns>
    public IReadOnlyDictionary<string, int> IncrementReaction(string reactionType)
    {
        lock (_gate)
        {
            _reactionCounts[reactionType] += 1;
            LastActiveUtc = DateTimeOffset.UtcNow;
            return new Dictionary<string, int>(_reactionCounts, StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// The pure ROUND-ROBIN blank distribution (group-play/02, AC-01/AC-04),
    /// AUTHORITATIVE server-side so a client can never assign itself an easier
    /// share. Deals each blank index k to player index (k % N) in ROSTER ORDER
    /// (host first, since the host is <see cref="Player.IsHost"/> and the first
    /// entry in <see cref="_players"/>), wrapping - so every blank is assigned
    /// exactly once, per-player counts differ by at most one, and (when
    /// blankCount &gt;= N) everyone contributes.
    ///
    /// ============================ MIRRORS distribute.ts =====================
    /// This is the C# twin of web/src/engine/distribute.ts. The algorithm is
    /// INTENTIONALLY DUPLICATED (no codegen, no shared source): the TS version is
    /// the unit-tested reference/spec, this C# version is the authority on the
    /// wire. Keep them identical BY HAND - if the dealing rule changes here,
    /// change it there (and its Vitest spec) too.
    /// ========================================================================
    ///
    /// Called only under <see cref="_gate"/> (from <see cref="StartRound"/>); takes
    /// the live roster reference but never mutates it. Guards a contentless template
    /// (blankCount &lt;= 0) by giving every player an empty blank list.
    /// </summary>
    private static IReadOnlyList<PlayerAssignment> ComputeAssignments(
        IReadOnlyList<Player> players,
        int blankCount)
    {
        var playerCount = players.Count;
        if (playerCount == 0)
        {
            return [];
        }

        // One (initially empty) bucket per player, preserved even when there is
        // nothing to deal - callers always get exactly `playerCount` entries.
        var buckets = new List<int>[playerCount];
        for (var i = 0; i < playerCount; i += 1)
        {
            buckets[i] = [];
        }

        // Deal in ascending blank-index order so each bucket comes out already
        // sorted (blank k -> player k % playerCount). Negative/zero blankCount
        // simply skips the loop, leaving empty buckets.
        for (var blankIndex = 0; blankIndex < blankCount; blankIndex += 1)
        {
            var playerIndex = blankIndex % playerCount;
            buckets[playerIndex].Add(blankIndex);
        }

        var assignments = new PlayerAssignment[playerCount];
        for (var i = 0; i < playerCount; i += 1)
        {
            var player = players[i];
            assignments[i] = new PlayerAssignment(
                ConnectionId: player.ConnectionId,
                Nickname: player.Nickname,
                Variant: player.Variant,
                IsHost: player.IsHost,
                BlankIndices: buckets[i]);
        }

        return assignments;
    }

    /// <summary>
    /// A point-in-time snapshot of the current round, or null while the room is in
    /// the lobby. Returns a copy so callers cannot mutate the live round outside
    /// the lock (later group-play stories read this to resume / re-broadcast).
    /// </summary>
    public RoundState? CurrentRound
    {
        get
        {
            lock (_gate)
            {
                if (_round is null)
                {
                    return null;
                }

                return new RoundState
                {
                    RoundNumber = _round.RoundNumber,
                    TemplateId = _round.TemplateId,
                    Mode = _round.Mode,
                    Phase = _round.Phase,
                    Assignments = _round.Assignments,
                    // Detached copy of the submission store so a read outside the
                    // lock never observes a later RecordSubmission mutation. The
                    // Submission records are immutable, so a shallow copy is safe.
                    Submissions = new Dictionary<int, Submission>(_round.Submissions),
                };
            }
        }
    }

    /// <summary>
    /// The outcome of <see cref="RecordSubmission"/> (group-play/03). Distinguishes
    /// the three cases the hub needs to act on differently, without throwing on the
    /// expected "not your blank" rejection:
    ///   - <see cref="Rejected"/>: the blank index is NOT assigned to this
    ///     connection (or there is no round / the round is not prompting). The hub
    ///     turns this into a friendly failure; nothing is recorded.
    ///   - <see cref="Recorded"/>: the word was recorded and the round still has
    ///     outstanding blanks.
    ///   - <see cref="RoundComplete"/>: the word was recorded AND that was the last
    ///     outstanding blank, so the round is now complete (the hub advances the
    ///     phase to reveal and broadcasts the reveal payload).
    /// </summary>
    public enum SubmitOutcome
    {
        /// <summary>The blank is not this connection's (or no prompting round exists); nothing recorded.</summary>
        Rejected,

        /// <summary>Recorded; the round still has outstanding blanks.</summary>
        Recorded,

        /// <summary>Recorded, and that was the final outstanding blank - the round is complete.</summary>
        RoundComplete,
    }

    /// <summary>
    /// Records ONE player's submission for ONE assigned blank (group-play/03),
    /// AUTHORITATIVE under the room lock. A player may only submit its OWN blanks:
    /// the blank index MUST appear in the assignment keyed by <paramref name="connectionId"/>,
    /// otherwise nothing is recorded and <see cref="SubmitOutcome.Rejected"/> is
    /// returned (a crafted client cannot fill another player's blank, AC-01). The
    /// word is taken AS-GIVEN - the caller (the hub) is responsible for having
    /// already routed it through the content-safety filter (AC-01, AC-06); an empty
    /// word is a legitimate SKIP placeholder that still records, preserving reveal
    /// alignment (matching the web engine's skipBlank rule).
    ///
    /// Recording is idempotent-ish: re-submitting an already-recorded blank simply
    /// overwrites it (editing is out of scope for gp/03, but a duplicate delivery
    /// must not double-count toward completion). Completion is defined over the
    /// ASSIGNED blanks across ALL players: the round is complete once every blank
    /// index that appears in any player's assignment has a submission. A round with
    /// zero assigned blanks (a contentless template) is trivially complete on the
    /// first (no-op) call path - but the hub never submits in that case since no
    /// client is dealt a blank.
    /// </summary>
    /// <param name="connectionId">The submitting connection (server-side handle; must own the blank).</param>
    /// <param name="blankIndex">The blank index into the template's ordered blanks.</param>
    /// <param name="word">The already-safety-vetted word (empty for a skip placeholder).</param>
    /// <returns>Whether the submission was rejected, recorded, or completed the round.</returns>
    public SubmitOutcome RecordSubmission(string connectionId, int blankIndex, string word)
    {
        lock (_gate)
        {
            if (_round is null || !string.Equals(_round.Phase, "prompting", StringComparison.Ordinal))
            {
                // No round to collect into, or the round has already moved past
                // collection (e.g. a late submission racing the reveal) - reject.
                return SubmitOutcome.Rejected;
            }

            // Verify the blank is actually assigned to THIS connection (AC-01). We
            // find the caller's own assignment and check the index is in its list.
            var ownsBlank = _round.Assignments.Any(a =>
                a.ConnectionId == connectionId && a.BlankIndices.Contains(blankIndex));
            if (!ownsBlank)
            {
                return SubmitOutcome.Rejected;
            }

            // Record (or overwrite a duplicate delivery) under the lock. Mutate a
            // fresh dictionary and swap it in so any snapshot handed out earlier
            // stays immutable (defensive - CurrentRound already deep-copies).
            var owner = _round.Assignments.First(a => a.ConnectionId == connectionId);
            var next = new Dictionary<int, Submission>(_round.Submissions)
            {
                [blankIndex] = new Submission(word, owner.Nickname, owner.Variant),
            };
            _round.Submissions = next;
            LastActiveUtc = DateTimeOffset.UtcNow;

            // The round is complete once every ASSIGNED blank index (across all
            // players) has a submission. Advance the phase to "reveal" ATOMICALLY
            // here (under the same lock) so a submission racing this one cannot slip
            // in after completion - the phase guard at the top rejects it, and only
            // the ONE call that fills the last blank sees RoundComplete.
            var complete = _round.Assignments
                .SelectMany(a => a.BlankIndices)
                .All(next.ContainsKey);
            if (complete)
            {
                _round.Phase = "reveal";
            }

            return complete ? SubmitOutcome.RoundComplete : SubmitOutcome.Recorded;
        }
    }

    /// <summary>
    /// A per-player DONE view for the progress broadcast (group-play/03): for each
    /// player (roster/assignment order, host first), whether they have submitted
    /// ALL of their assigned blanks yet. A player assigned zero blanks (fewer
    /// blanks than players) counts as done immediately - they owe nothing. Carries
    /// no words and no connectionId (AC-01, no PII). Read under the lock over a
    /// detached snapshot so it never observes a mid-mutation store.
    /// </summary>
    /// <returns>Per-player progress (nickname + variant + done), or empty if there is no round.</returns>
    public IReadOnlyList<PlayerProgress> GetProgress()
    {
        lock (_gate)
        {
            if (_round is null)
            {
                return [];
            }

            var submissions = _round.Submissions;
            return _round.Assignments
                .Select(a => new PlayerProgress(
                    a.Nickname,
                    a.Variant,
                    a.BlankIndices.All(submissions.ContainsKey)))
                .ToArray();
        }
    }

    /// <summary>
    /// The number of players who have submitted ALL their assigned blanks, and the
    /// total player count (group-play/03) - the "[N] of [M] quibblers done" figures
    /// the Waiting card shows. Computed from the same DONE rule as
    /// <see cref="GetProgress"/>, under the lock.
    /// </summary>
    /// <returns>A tuple of (done count, total player count); (0, 0) when there is no round.</returns>
    public (int DoneCount, int PlayerCount) GetProgressCounts()
    {
        lock (_gate)
        {
            if (_round is null)
            {
                return (0, 0);
            }

            var submissions = _round.Submissions;
            var doneCount = _round.Assignments
                .Count(a => a.BlankIndices.All(submissions.ContainsKey));
            return (doneCount, _round.Assignments.Count);
        }
    }

    /// <summary>
    /// Builds the ORDERED reveal payload (group-play/03, AC-05): for blank index
    /// 0..M-1, the submitted word plus its owning player (nickname + variant), in
    /// blank order - the positional contract the web engine's assemble() pairs
    /// against. A blank with no submission (edge: a player left before submitting)
    /// renders as an EMPTY word attributed to no one, so alignment holds. Read under
    /// the lock over the live round. The server does NOT assemble the story - it
    /// only projects the words in order; clients assemble locally (AC-05).
    ///
    /// M (the total blank count) is derived from the round's assignments: the deal
    /// hands out every blank index 0..M-1 exactly once, so the count of all assigned
    /// indices IS M - no catalog lookup needed here (and it stays correct even if a
    /// player left, since the assignment record is the round-start snapshot).
    /// </summary>
    /// <returns>The ordered reveal words, one per blank position (empty entries for unfilled blanks).</returns>
    public IReadOnlyList<RevealWord> BuildReveal()
    {
        lock (_gate)
        {
            if (_round is null)
            {
                return [];
            }

            var submissions = _round.Submissions;
            var blankCount = _round.Assignments.Sum(a => a.BlankIndices.Count);
            var words = new RevealWord[blankCount];
            for (var index = 0; index < blankCount; index += 1)
            {
                words[index] = submissions.TryGetValue(index, out var submission)
                    ? new RevealWord(submission.Word, submission.Nickname, submission.Variant)
                    : new RevealWord(string.Empty, string.Empty, string.Empty);
            }

            return words;
        }
    }
}
