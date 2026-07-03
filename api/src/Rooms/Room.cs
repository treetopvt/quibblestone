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
//
//  ==================== IDENTITY CONTRACT (accounts-identity/01) ==============
//  This record is PII-FREE BY DESIGN and stays that way (README section 3:
//  "players are anonymous forever"; section 6: minimal data on minors). A player
//  is, and forever remains, "no account": there is NO email, NO person-tied
//  device identifier, NO account/purchaser reference, and NO sign-in prompt
//  anywhere on Room or Player or in the join/lobby flow. DO NOT add an
//  account/device/email/purchaser field here.
//
//  A purchaser account, IF one ever exists in a session, lives in a SEPARATE
//  record keyed independently (see api/src/Accounts, accounts-identity/02) and
//  is NEVER referenced from this file - adding accounts is additive and must
//  never become a prerequisite for play (feature.md design note).
//
//  The ONE session-level entitlement seam is `Room.Entitlements` (a
//  `SessionEntitlements` capability-key set, captured exactly once via
//  `Room.CaptureEntitlements` at GameHub.CreateRoom - ai-cost-gate/02, #121,
//  PR #132). It carries CAPABILITY KEYS ONLY, NEVER a purchaser identity, which
//  is what upholds ADR 0002's load-bearing invariant ("entitlement travels with
//  the session, not identity"). There is no second placeholder flag to add:
//  billing-entitlements/01 (#70) resolves a real purchaser to capabilities at
//  session-creation and captures ONLY the resolved set here, never the purchaser.
//  ===========================================================================
// ----------------------------------------------------------------------------

using System.Security.Cryptography;
using QuibbleStone.Api.Entitlements;

namespace QuibbleStone.Api.Rooms;

/// <summary>
/// One anonymous player in a room. Minimal by design (no PII, README section 6):
/// just an in-session nickname, a Guardian variant, the owning connection, and
/// whether this player is the host.
///
/// session-engine/07 (hold the seat): a seat also carries a server-minted
/// <see cref="ReconnectToken"/> (the opaque handle story 08's Rejoin spends - see
/// <see cref="Room.NewReconnectToken"/>) and a <see cref="Connected"/> flag. Both
/// are SERVER-SIDE on this record; the token is NEVER projected onto the wire DTO
/// (that would let another player hijack the seat, AC-06), and only the Connected
/// flag reaches the roster shape (consumed by web story 10). "Marking a seat
/// disconnected" rebuilds this immutable record with <c>Connected = false</c> under
/// the room lock (see <see cref="Room.MarkDisconnected"/>) - it never mutates in place.
/// </summary>
/// <param name="Nickname">In-session display name (the host now picks one on HostSetup before the room is minted - see <see cref="Room.CreateHosted"/>).</param>
/// <param name="Variant">Guardian avatar variant (the host picks one on HostSetup; joiners pick on Join).</param>
/// <param name="ConnectionId">The SignalR connection that owns this player - used for leave-detection in story 03.</param>
/// <param name="IsHost">True for the room creator; false for joiners.</param>
/// <param name="ReconnectToken">session-engine/07: the opaque, cryptographically random, per-seat reconnect handle (returned ONLY to the owning caller, never broadcast). Story 08's Rejoin matches it to reclaim this seat.</param>
/// <param name="Connected">session-engine/07: false while the seat is being held through a disconnect grace window; true otherwise. The one grace-state field the roster DTO surfaces (web story 10 renders it); the token never is.</param>
public sealed record Player(
    string Nickname,
    string Variant,
    string ConnectionId,
    bool IsHost,
    string ReconnectToken,
    bool Connected = true);

/// <summary>
/// session-engine/07: the ticket <see cref="Room.MarkDisconnected"/> hands back
/// when it opens a grace hold on a dropped seat. The <see cref="Episode"/> uniquely
/// identifies THIS disconnect (so a stale timer from an earlier drop can never evict
/// a seat that has since reconnected and dropped again), and <see cref="Token"/> is
/// the cancellation token the one-shot grace-expiry <c>Task.Delay</c> waits on -
/// story 08's Rejoin cancels its source (<see cref="Room.CancelGrace"/>) to keep the seat.
/// </summary>
public sealed record GraceHoldTicket(Guid Episode, CancellationToken Token);

/// <summary>
/// session-engine/08: the two outcomes of <see cref="Room.ReclaimSeat"/> - the seat
/// was found by its reconnect token and reclaimed, or there was nothing to reclaim
/// (unknown / already-evicted / wrong-room token). The hub maps this to a friendly
/// result envelope, never a throw (AC-05).
/// </summary>
public enum ReclaimStatus
{
    /// <summary>No seat in this room holds the given token (unknown, already evicted, or a token for a different room); nothing was mutated.</summary>
    NotFound,

    /// <summary>The seat was reclaimed under the new connection; the round-phase rehydration fields are populated.</summary>
    Reclaimed,
}

/// <summary>
/// session-engine/08: what <see cref="Room.ReclaimSeat"/> hands back after matching a
/// reconnect token to a held seat and swapping in the caller's new connection - just
/// enough round-phase context (computed ATOMICALLY under the room lock at reclaim time)
/// for the hub to build the rehydration envelope story 09 consumes:
///   - <see cref="Status"/>: whether a seat was actually reclaimed (AC-05 guard).
///   - <see cref="Phase"/>: "lobby" (no round) | "prompting" | "reveal" (AC-02).
///   - <see cref="IsHost"/>: whether the reclaimed seat is the room's host (AC-02).
///   - <see cref="RemainingBlankIndices"/>: for a "prompting" round, THIS seat's own
///     not-yet-submitted blank indices - index-only, no words, no PII, mirroring the
///     shape of <see cref="Hubs.YourBlanksDto"/> (AC-03). Empty in any other phase.
/// The room-wide progress ("N of M") and the reveal words are NOT duplicated here - the
/// hub reads them from the EXISTING <see cref="GetProgressCounts"/> / <see cref="GetProgress"/>
/// / <see cref="BuildReveal"/> projections (no second parallel round bookkeeping).
/// </summary>
/// <param name="Status">Whether a seat was reclaimed or nothing matched the token.</param>
/// <param name="Phase">The round's phase at reclaim time: "lobby" | "prompting" | "reveal".</param>
/// <param name="IsHost">True when the reclaimed seat is the room's host.</param>
/// <param name="RemainingBlankIndices">This seat's outstanding blank indices (prompting only; empty otherwise).</param>
public sealed record ReclaimResult(
    ReclaimStatus Status,
    string Phase,
    bool IsHost,
    IReadOnlyList<int> RemainingBlankIndices)
{
    /// <summary>The shared "nothing to reclaim" result (AC-05) - a clean, mutation-free miss.</summary>
    public static readonly ReclaimResult NotFound =
        new(ReclaimStatus.NotFound, "lobby", false, Array.Empty<int>());
}

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

    /// <summary>
    /// When this round OPENED (UTC), stamped once under the room lock by
    /// <see cref="Room.StartRound"/> (platform-devops/05, AC-02). It exists purely so
    /// the anonymous product-usage "RoundCompleted" event can measure round DURATION
    /// (reveal time minus this) - an anonymous session length, never tied to a
    /// person. Immutable for the life of the round; copied verbatim into every
    /// snapshot so a duration read outside the lock is consistent.
    /// </summary>
    public required DateTimeOffset StartedUtc { get; init; }

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

    /// <summary>
    /// reveal-delight/03 (AC-01/AC-02): the Golden Guardian "funniest word" votes
    /// cast during THIS round's reveal, keyed by the voter's connection id -> the
    /// blank TOKEN they chose (the blank's body-order position, as a string - the
    /// same opaque token the web Reveal assigns each coral word). One active vote per
    /// voter (a re-cast overwrites, mirroring web/src/engine/vote.ts). Ephemeral and
    /// per-round: a fresh round starts empty. No PII - a connection id is a
    /// server-side handle, and the token is an already-vetted, already-displayed
    /// word's position (AC-07). Mutated only under the room lock.
    /// </summary>
    public Dictionary<string, string> GoldenGuardianVotes { get; set; } =
        new(StringComparer.Ordinal);

    /// <summary>
    /// reveal-delight/03: the voter connection ids in FIRST-cast order, so the
    /// tie-break ("first blank to reach the max count") is deterministic - the exact
    /// rule web/src/engine/vote.ts documents and pins in its Vitest spec. A voter
    /// that MOVES its vote keeps its original position (still one voter).
    /// </summary>
    public List<string> GoldenGuardianCastOrder { get; set; } = [];

    /// <summary>
    /// reveal-delight/03 (AC-03): true once the funniest-word vote has RESOLVED (every
    /// present player voted, or the host closed voting early). Resolves exactly once
    /// per round; further votes after resolution are ignored.
    /// </summary>
    public bool GoldenGuardianResolved { get; set; }

    /// <summary>
    /// reveal-delight/03 (AC-03): the winning blank token once resolved, or null (not
    /// yet resolved, or resolved with zero votes -> no winner, no crown).
    /// </summary>
    public string? GoldenGuardianWinningBlankId { get; set; }

    /// <summary>
    /// reveal-delight/03 (AC-04): the nickname crowned FOR this round - moved from the
    /// room's PENDING crown when this round opens (<see cref="Room.StartRound"/>) so
    /// the previous round's funniest-word winner wears the crown for exactly this one
    /// round. Null when no crown applies. Server-tracked round state, never a client
    /// timer.
    /// </summary>
    public string? CrownedNickname { get; set; }
}

/// <summary>
/// reveal-delight/03: the outcome of a Golden Guardian vote action (a cast or a
/// host close), for the hub to broadcast. Carries the live "N of M voted" figures
/// plus, once the vote resolves, the winning blank token and the winning
/// contributor's nickname (the crown's future wearer). Not a result envelope for the
/// caller (a stray vote needs no retry) - it drives the room-wide broadcasts.
/// </summary>
/// <param name="Accepted">True if this cast was recorded (a valid, still-open vote); false if ignored (no reveal, already resolved, or an unknown/empty blank).</param>
/// <param name="VotedCount">How many CURRENTLY-present players have voted (the "N").</param>
/// <param name="TotalVoters">How many players are present and can vote (the "M").</param>
/// <param name="Resolved">True if this action resolved the vote (transitioned to a final winner).</param>
/// <param name="WinningBlankId">The winning blank token when resolved, or null (not resolved, or zero votes).</param>
/// <param name="WinnerNickname">The winning contributor's nickname when resolved with a winner, else null (no crown).</param>
public sealed record GoldenGuardianVoteResult(
    bool Accepted,
    int VotedCount,
    int TotalVoters,
    bool Resolved,
    string? WinningBlankId,
    string? WinnerNickname);

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

    // session-engine/07 (hold the seat): the in-flight disconnect grace holds,
    // keyed by the DROPPED connection id. A hold opens when a seat is marked
    // disconnected (MarkDisconnected) and closes when the seat is reclaimed
    // (CancelGrace - story 08's Rejoin) or evicted on grace expiry
    // (TryReleaseSeat). Each hold carries a unique EPISODE (so a stale timer for an
    // earlier drop cannot evict a seat that reconnected then dropped again) and a
    // CancellationTokenSource the one-shot grace-expiry Task.Delay waits on. A
    // pending hold NEVER keeps an abandoned room alive (AC-05): the seat still
    // counts in _players until eviction, and eviction (TryReleaseSeat) frees it so
    // the registry drops the room the instant it empties. Guarded by _gate like
    // every other piece of room state; the CTS is disposed when its hold closes.
    private readonly Dictionary<string, GraceHold> _graceHolds = new(StringComparer.Ordinal);

    // One in-flight grace hold: the disconnect episode + the timer's cancellation
    // source. A plain mutable holder (never handed out); the room owns its lifetime.
    private sealed class GraceHold
    {
        public required Guid Episode { get; init; }
        public required CancellationTokenSource Cts { get; init; }
    }

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

    // reveal-delight/03 (AC-04): the PENDING Golden Guardian crown - the nickname
    // that won the most recent funniest-word vote and should wear the crown for the
    // NEXT round. Set when a vote resolves (CastGoldenGuardianVote / Close
    // GoldenGuardianVoting) and CONSUMED at the next StartRound (moved onto that
    // round's CrownedNickname, then cleared here). So the crown lasts exactly one
    // round: round N's reveal awards it, round N+1 wears it, round N+2 clears it
    // unless round N+1's reveal awarded a fresh one. Deliberately NOT cleared by
    // BackToLobby, so the crown carries whether the host plays again or returns to
    // the lobby first. Guarded by _gate. Never PII beyond an in-session nickname.
    private string? _pendingCrownNickname;

    // ai-cost-gate/02 (AC-01): the AI entitlements evaluated EXACTLY ONCE for this
    // session, captured here at room-creation and read for the room's lifetime -
    // NEVER re-evaluated per tap/round/AI call (that is the smell the cost gate
    // forbids). Set once via CaptureEntitlements immediately after the room is
    // minted (GameHub.CreateRoom); a second capture is a programming error and
    // throws. Null only in the brief window before capture (and in older callers /
    // tests that never capture - e.g. RoomRegistry.CreateRoom used directly).
    // Guarded by _gate. Carries no PII: it is a set of capability keys, keyed off
    // the anonymous session, not a player (README section 6, AC-05).
    private SessionEntitlements? _entitlements;

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
    /// ai-cost-gate/02 (AC-01): the AI entitlements captured ONCE for this session
    /// at room-creation (see <see cref="CaptureEntitlements"/>), or null before
    /// capture. Later AI code READS this captured result - it never re-evaluates
    /// entitlement per tap/round/AI call. In alpha these are default-unlocked, so
    /// the AI jumble is reachable by every session (AC-03); the real runtime gate
    /// is quota + the spend breaker, not this value (AC-04). Anonymous (a set of
    /// capability keys keyed off the session, never a player - README section 6).
    /// </summary>
    public SessionEntitlements? Entitlements
    {
        get
        {
            lock (_gate)
            {
                return _entitlements;
            }
        }
    }

    /// <summary>
    /// ai-cost-gate/02 (AC-01): capture the session's AI entitlements EXACTLY ONCE,
    /// at room-creation. GameHub.CreateRoom calls this immediately after the room is
    /// minted, passing the result of a SINGLE
    /// <see cref="Entitlements.IEntitlementService.EvaluateForSession"/> call. The
    /// value is then read for the room's lifetime and never re-evaluated. Capturing
    /// twice is a programming error (a second evaluation would defeat the whole
    /// "entitlement once" contract) and throws. Guarded by <see cref="_gate"/>.
    /// </summary>
    /// <param name="entitlements">The session-creation entitlement evaluation to stash.</param>
    /// <exception cref="InvalidOperationException">If entitlements were already captured for this room.</exception>
    public void CaptureEntitlements(SessionEntitlements entitlements)
    {
        ArgumentNullException.ThrowIfNull(entitlements);
        lock (_gate)
        {
            if (_entitlements is not null)
            {
                throw new InvalidOperationException(
                    "Session entitlements were already captured for this room - they are evaluated exactly once at creation (ai-cost-gate/02, AC-01).");
            }
            _entitlements = entitlements;
        }
    }

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
            IsHost: true,
            ReconnectToken: NewReconnectToken()));
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
                IsHost: false,
                ReconnectToken: NewReconnectToken()));

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

            // session-engine/08 (Gate-1 nit carried from 07): the seat is gone, so any
            // pending grace hold keyed by this same connection id must go with it - drop
            // and dispose it so a CancellationTokenSource never outlives the seat it
            // guards (a deliberate LeaveRoom during a held-then-abandoned window, or any
            // odd sequencing, could otherwise leak a live CTS). No-op when none pending.
            DiscardGraceHoldLocked(connectionId);

            LastActiveUtc = DateTimeOffset.UtcNow;
            return true;
        }
    }

    /// <summary>
    /// session-engine/07 (AC-06/AC-07): the owning connection's opaque reconnect
    /// token, or null if that connection is not seated here. Returned ONLY to the
    /// caller of CreateRoom/JoinRoom in that call's own result envelope - it is
    /// NEVER placed on the roster DTO the whole room receives, so no other player
    /// can learn it and hijack the seat. Read under the lock.
    /// </summary>
    /// <param name="connectionId">The connection whose own token to read.</param>
    /// <returns>The seat's reconnect token, or null when the connection is not seated here.</returns>
    public string? GetReconnectToken(string connectionId)
    {
        if (string.IsNullOrEmpty(connectionId))
        {
            return null;
        }

        lock (_gate)
        {
            return _players.FirstOrDefault(p => p.ConnectionId == connectionId)?.ReconnectToken;
        }
    }

    /// <summary>
    /// session-engine/07 (AC-01/AC-02): mark a dropped connection's seat DISCONNECTED
    /// instead of removing it, and open a one-shot grace hold for it. The seat stays
    /// in <see cref="_players"/> (so <see cref="PlayerCount"/>, <see cref="IsHost"/>,
    /// and the round's assignments/submissions all still see it) - only its
    /// <see cref="Player.Connected"/> flag flips false, by rebuilding the immutable
    /// record under the lock (the same swap-a-fresh-value pattern
    /// <see cref="RecordSubmission"/> uses), never a field mutation.
    ///
    /// Returns a <see cref="GraceHoldTicket"/> (episode + cancellation token) the
    /// caller hands to the grace scheduler to run the deferred eviction, or null when
    /// the connection is not seated here OR its seat is already being held (a second
    /// disconnect for the same still-disconnected seat is a no-op, so grace is never
    /// double-scheduled). Deliberately does NOT bump <see cref="LastActiveUtc"/> - a
    /// drop is not activity, and the seat's presence alone keeps the room out of the
    /// idle sweep for the seconds-long grace window.
    /// </summary>
    /// <param name="connectionId">The dropped SignalR connection.</param>
    /// <returns>The grace ticket to schedule the deferred eviction, or null if there is no live seat to hold.</returns>
    public GraceHoldTicket? MarkDisconnected(string connectionId)
    {
        if (string.IsNullOrEmpty(connectionId))
        {
            return null;
        }

        lock (_gate)
        {
            var index = _players.FindIndex(p => p.ConnectionId == connectionId);
            if (index < 0 || !_players[index].Connected)
            {
                // Not seated here, or already being held - nothing new to hold.
                return null;
            }

            // Rebuild the immutable record with Connected = false (remove + re-add via
            // an in-place swap under the lock), preserving the seat's reconnect token.
            _players[index] = _players[index] with { Connected = false };

            // Defensive: retire any stale hold for this connection before opening a
            // fresh episode (should not happen given the Connected guard above).
            DiscardGraceHoldLocked(connectionId);

            var cts = new CancellationTokenSource();
            var episode = Guid.NewGuid();
            _graceHolds[connectionId] = new GraceHold { Episode = episode, Cts = cts };
            return new GraceHoldTicket(episode, cts.Token);
        }
    }

    /// <summary>
    /// session-engine/07: cancel a seat's pending grace hold (the cancellation seam
    /// story 08's Rejoin consumes). Cancelling the source wakes the grace-expiry
    /// <c>Task.Delay</c> so it exits WITHOUT evicting, and drops the hold. This only
    /// stops the timer - it deliberately does NOT flip the seat back to connected or
    /// swap in the new connection id; that is story 08's ReclaimSeat, which calls
    /// this as one step of a full reclaim under the same lock. A no-op (returns
    /// false) when there is no hold for the connection.
    /// </summary>
    /// <param name="connectionId">The held (dropped) connection whose grace to cancel.</param>
    /// <returns>True if a pending hold was cancelled; false if none was pending.</returns>
    public bool CancelGrace(string connectionId)
    {
        if (string.IsNullOrEmpty(connectionId))
        {
            return false;
        }

        lock (_gate)
        {
            return DiscardGraceHoldLocked(connectionId);
        }
    }

    /// <summary>
    /// session-engine/07 + /08: drop and dispose the pending grace hold (if any) keyed by
    /// <paramref name="connectionId"/> - cancel its timer's <see cref="CancellationTokenSource"/>
    /// (so a waiting <c>Task.Delay</c> exits without evicting) and dispose it, so a CTS's
    /// lifetime is never longer than the seat it guards. Returns true if a hold was
    /// dropped, false if none was pending. MUST be called under <see cref="_gate"/> - it
    /// is the single shared "retire this connection's grace hold" primitive that
    /// <see cref="CancelGrace"/>, <see cref="MarkDisconnected"/> (stale-hold cleanup),
    /// <see cref="RemovePlayer"/> (Gate-1), and <see cref="ReclaimSeat"/> all compose.
    /// </summary>
    /// <param name="connectionId">The (dropped) connection whose grace hold to retire.</param>
    /// <returns>True if a pending hold was dropped and disposed; false if none was pending.</returns>
    private bool DiscardGraceHoldLocked(string connectionId)
    {
        if (!_graceHolds.Remove(connectionId, out var hold))
        {
            return false;
        }

        hold.Cts.Cancel();
        hold.Cts.Dispose();
        return true;
    }

    /// <summary>
    /// session-engine/07 (AC-03): the DEFERRED eviction - release a held seat once its
    /// grace window has elapsed with no reconnect. Guarded by <see cref="_gate"/> and
    /// idempotent-safe: it evicts ONLY if the hold still exists AND carries the SAME
    /// <paramref name="episode"/> as when the timer started. If the seat reconnected
    /// (its hold was cancelled) or a newer disconnect superseded this one (a different
    /// episode), this is a no-op returning false, so a stale timer can never evict a
    /// live seat. On a real release it removes the seat and retires the hold, exactly
    /// like the immediate <see cref="RemovePlayer"/> path - just deferred.
    /// </summary>
    /// <param name="connectionId">The held (dropped) connection to evict.</param>
    /// <param name="episode">The disconnect episode the expiring timer was started for.</param>
    /// <returns>True if the seat was evicted; false if the hold was cancelled or superseded.</returns>
    public bool TryReleaseSeat(string connectionId, Guid episode)
    {
        if (string.IsNullOrEmpty(connectionId))
        {
            return false;
        }

        lock (_gate)
        {
            if (!_graceHolds.TryGetValue(connectionId, out var hold) || hold.Episode != episode)
            {
                // Reconnected within grace, or superseded by a newer drop - do NOT evict.
                return false;
            }

            _players.RemoveAll(p => p.ConnectionId == connectionId);
            _graceHolds.Remove(connectionId);
            hold.Cts.Dispose();
            LastActiveUtc = DateTimeOffset.UtcNow;
            return true;
        }
    }

    /// <summary>
    /// session-engine/08 (AC-01/AC-02/AC-03): reclaim a held seat by its reconnect token
    /// under a brand-new SignalR connection, and hand back just enough round-phase context
    /// for the hub to rehydrate the resuming client. SignalR's own
    /// <c>withAutomaticReconnect</c> always gets a FRESH connection id after a drop, so a
    /// device spends the token 07 minted (see <see cref="NewReconnectToken"/>) to prove it
    /// owns the seat and move it to the new connection - the whole reclaim happens
    /// ATOMICALLY under <see cref="_gate"/>:
    ///   1. Find the seat whose <see cref="Player.ReconnectToken"/> matches. No match ->
    ///      <see cref="ReclaimResult.NotFound"/> (unknown, already-evicted-by-grace, or a
    ///      token for a DIFFERENT room since the caller looked this room up by code): a
    ///      clean AC-05 miss that mutates NOTHING and never throws.
    ///   2. Cancel the seat's pending grace-expiry hold via <see cref="DiscardGraceHoldLocked"/>
    ///      (story 07's cancellation seam). This is the DETERMINISTIC race resolver: this
    ///      method and the grace-expiry eviction (<see cref="TryReleaseSeat"/>) both take
    ///      THIS lock, so whichever wins the lock first wins outright - if grace-expiry ran
    ///      first it already removed the seat (step 1 returns NotFound); if we run first we
    ///      drop the hold here (the later TryReleaseSeat finds no hold and no-ops).
    ///   3. Rebuild the immutable <see cref="Player"/> record with the new connection id and
    ///      <see cref="Player.Connected"/> flipped true (the swap-a-fresh-value pattern, never
    ///      a field mutation), preserving the token, nickname, and variant.
    ///   4. If a round is live, rekey THIS seat's blank assignment from the old connection id
    ///      to the new one (<see cref="RekeyAssignmentLocked"/>) so the resumed player can
    ///      submit its still-outstanding blanks (<see cref="RecordSubmission"/> matches by
    ///      connection id). The blank indices and every recorded submission are preserved
    ///      untouched - only the routing key moves.
    /// The returned <see cref="ReclaimResult"/> carries the phase, the host flag, and (for a
    /// "prompting" round) THIS seat's remaining blank indices - never another seat's,
    /// never any word or PII (AC-03/AC-07). Room-wide progress + the reveal words are read
    /// by the hub from the EXISTING <see cref="GetProgress"/> / <see cref="GetProgressCounts"/>
    /// / <see cref="BuildReveal"/> projections, so there is no second parallel bookkeeping.
    /// </summary>
    /// <param name="token">The seat's reconnect token, from the caller's own create/join envelope.</param>
    /// <param name="newConnectionId">The caller's NEW SignalR connection to seat the reclaimed player under.</param>
    /// <returns>A reclaimed result with phase-data, or <see cref="ReclaimResult.NotFound"/> when nothing matched.</returns>
    public ReclaimResult ReclaimSeat(string token, string newConnectionId)
    {
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(newConnectionId))
        {
            return ReclaimResult.NotFound;
        }

        lock (_gate)
        {
            var index = _players.FindIndex(p =>
                string.Equals(p.ReconnectToken, token, StringComparison.Ordinal));
            if (index < 0)
            {
                // Unknown token, a seat already evicted when its grace window expired, or a
                // token minted for a different room - nothing here to reclaim (AC-05). No
                // mutation of any kind.
                return ReclaimResult.NotFound;
            }

            var seat = _players[index];

            // Seat-hijack hardening (child safety, extends story 07's AC-06/AC-07): Rejoin
            // reclaims a HELD (dropped) seat only. A seat that is still connected is not in a
            // grace window, so reclaiming it would let a LEAKED token boot the live occupant
            // and take over the seat - and the booted connection would linger in the room's
            // SignalR group, still receiving broadcasts. Every legitimate reconnect marks the
            // seat disconnected (OnDisconnectedAsync fires and MarkDisconnected flips Connected
            // to false) BEFORE the returning client reaches Rejoin, so this rejects only a
            // takeover attempt, never a real resume. Friendly no-op failure mapped to the same
            // envelope as an unknown/expired token (AC-05) - nothing here is mutated.
            if (seat.Connected)
            {
                return ReclaimResult.NotFound;
            }

            var oldConnectionId = seat.ConnectionId;

            // Cancel the pending grace-expiry hold for the OLD connection (the deterministic
            // race resolver - see the method summary). No-op when no hold is pending.
            DiscardGraceHoldLocked(oldConnectionId);

            // Swap in the caller's new connection and mark the seat connected again by
            // rebuilding the immutable record - token / nickname / variant preserved.
            _players[index] = seat with { ConnectionId = newConnectionId, Connected = true };

            // Rekey this seat's live-round assignment (if any) so the resumed player owns its
            // outstanding blanks under the new connection; compute the remaining indices from
            // the (rekeyed) assignment for the rehydration payload.
            var assignment = _round is null
                ? null
                : RekeyAssignmentLocked(oldConnectionId, newConnectionId);

            LastActiveUtc = DateTimeOffset.UtcNow;

            var phase = _round?.Phase ?? "lobby";
            var remaining = assignment is null
                ? Array.Empty<int>()
                : RemainingBlankIndices(assignment, _round!.Submissions);

            return new ReclaimResult(ReclaimStatus.Reclaimed, phase, seat.IsHost, remaining);
        }
    }

    /// <summary>
    /// session-engine/08: move a live round's blank assignment from <paramref name="oldConnectionId"/>
    /// to <paramref name="newConnectionId"/> when a seat is reclaimed under a new connection,
    /// rebuilding the assignment list with just that one entry's immutable record swapped (the
    /// blank indices, nickname, variant, and host flag all preserved - only the connection key
    /// moves). Returns the updated assignment, or null when this connection owned no assignment
    /// (a lobby reclaim, or a seat dealt zero blanks that still has an entry - which is also
    /// returned). MUST be called under <see cref="_gate"/> with a non-null round.
    /// </summary>
    private PlayerAssignment? RekeyAssignmentLocked(string oldConnectionId, string newConnectionId)
    {
        if (_round is null)
        {
            return null;
        }

        PlayerAssignment? updated = null;
        var next = new List<PlayerAssignment>(_round.Assignments.Count);
        foreach (var assignment in _round.Assignments)
        {
            if (assignment.ConnectionId == oldConnectionId)
            {
                updated = assignment with { ConnectionId = newConnectionId };
                next.Add(updated);
            }
            else
            {
                next.Add(assignment);
            }
        }

        if (updated is not null)
        {
            _round.Assignments = next;
        }

        return updated;
    }

    /// <summary>
    /// session-engine/08 (AC-03): the small PURE helper behind a "prompting" rejoin - given
    /// ONE seat's own <paramref name="assignment"/> and the round's <paramref name="submissions"/>,
    /// return the blank indices it still owes (assigned but not yet in the submission store),
    /// in ascending blank order. Index-only, no words, no connection id, no PII - it mirrors
    /// the shape of <see cref="Hubs.YourBlanksDto"/> exactly (the resumed word-collection
    /// screen shows only what is left, nothing already-answered re-asked). Pure and static:
    /// it reads only its two arguments, so it is trivially unit-testable and never another
    /// seat's blanks.
    /// </summary>
    /// <param name="assignment">The seat's own blank assignment.</param>
    /// <param name="submissions">The round's collected submissions, keyed by blank index.</param>
    /// <returns>The still-outstanding blank indices for this seat (empty when all are submitted).</returns>
    private static IReadOnlyList<int> RemainingBlankIndices(
        PlayerAssignment assignment,
        IReadOnlyDictionary<int, Submission> submissions)
    {
        var remaining = new List<int>();
        foreach (var index in assignment.BlankIndices)
        {
            if (!submissions.ContainsKey(index))
            {
                remaining.Add(index);
            }
        }

        return remaining;
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

            // reveal-delight/03 (AC-04): CONSUME the pending crown (if any) onto this
            // round, then clear it - so the previous round's funniest-word winner
            // wears the crown for exactly THIS one round. A round with no pending
            // crown carries null (the crown clears if it is not re-awarded).
            var crownedNickname = _pendingCrownNickname;
            _pendingCrownNickname = null;

            _round = new RoundState
            {
                // group-play/04: increment off the previous round (or 0 when the room
                // has never started one), so a play-again in the same room -> round 2,
                // 3, ... and a BackToLobby (which nulls _round) resets the next to 1.
                RoundNumber = (_round?.RoundNumber ?? 0) + 1,
                TemplateId = templateId,
                Mode = mode,
                Phase = "prompting",
                // platform-devops/05 (AC-02): stamp the open time under the lock so
                // the anonymous RoundCompleted event can measure round duration.
                StartedUtc = DateTimeOffset.UtcNow,
                Assignments = assignments,
                // group-play/03: a fresh, empty submission store for the new round.
                Submissions = new Dictionary<int, Submission>(),
                // reveal-delight/03: the crown this round wears (from the pending
                // crown above); the vote fields start empty for the new round.
                CrownedNickname = crownedNickname,
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
                StartedUtc = _round.StartedUtc,
                Assignments = _round.Assignments,
                // A round just started, so there are no submissions yet; hand back
                // a fresh empty copy so this snapshot never aliases the live store.
                Submissions = new Dictionary<int, Submission>(),
                // reveal-delight/03: expose the crown on the snapshot so the hub can
                // broadcast it on RoundStarted (who wears the crown this round).
                CrownedNickname = _round.CrownedNickname,
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
    /// reveal-delight/03 (AC-01/AC-02): record ONE player's Golden Guardian vote for
    /// the funniest coral word, then report the live progress and (if this cast
    /// completes the vote) the resolution.
    ///
    /// Only valid during the reveal phase (the vote lives on the reveal). One active
    /// vote per voter - a re-cast MOVES the vote (overwrites the connection's entry;
    /// the voter keeps its first-cast position for the tie-break), mirroring
    /// web/src/engine/vote.ts. The <paramref name="blankToken"/> must name a real
    /// FILLED blank (a non-empty submitted word) - a token outside that set is
    /// ignored (AC-07: a vote is only ever one of the already-vetted, already-shown
    /// coral words). The vote AUTO-RESOLVES once every CURRENTLY-present player has
    /// voted; the host may also close it early via <see cref="CloseGoldenGuardian"/>.
    /// Resolving sets the pending crown to the winning word's contributor for the
    /// next round (AC-04). Mutated only under the room lock.
    /// </summary>
    /// <param name="connectionId">The voting connection (server-side handle; no PII on the wire).</param>
    /// <param name="blankToken">The chosen blank's body-order position token (must be a filled blank).</param>
    /// <returns>The cast outcome + live "N of M" figures + any resolution, for the hub to broadcast.</returns>
    public GoldenGuardianVoteResult CastGoldenGuardianVote(string connectionId, string blankToken)
    {
        lock (_gate)
        {
            // A vote only exists on a reveal that has not already resolved.
            if (_round is null ||
                !string.Equals(_round.Phase, "reveal", StringComparison.Ordinal) ||
                _round.GoldenGuardianResolved)
            {
                return BuildVoteResult(accepted: false, resolved: false);
            }

            // Only a SEATED player may vote (a stray/left connection cannot skew the
            // tally). The vote is keyed by connection id, a server-side handle.
            if (!_players.Any(p => p.ConnectionId == connectionId))
            {
                return BuildVoteResult(accepted: false, resolved: false);
            }

            // The token must be one of the offered options: a FILLED (non-empty)
            // blank. Anything else (an empty/skipped blank or a crafted token) is
            // ignored - never recorded (AC-07).
            if (!IsFilledBlankToken(blankToken))
            {
                return BuildVoteResult(accepted: false, resolved: false);
            }

            // One active vote per voter: a first-time voter joins the cast order; a
            // returning voter keeps its position and just moves its choice.
            if (!_round.GoldenGuardianVotes.ContainsKey(connectionId))
            {
                _round.GoldenGuardianCastOrder.Add(connectionId);
            }
            _round.GoldenGuardianVotes[connectionId] = blankToken;
            LastActiveUtc = DateTimeOffset.UtcNow;

            // Auto-resolve once every present player has voted (AC-03).
            var everyPresentVoted =
                _players.Count > 0 &&
                _players.All(p => _round.GoldenGuardianVotes.ContainsKey(p.ConnectionId));
            if (everyPresentVoted)
            {
                ResolveGoldenGuardian();
            }

            return BuildVoteResult(accepted: true, resolved: _round.GoldenGuardianResolved);
        }
    }

    /// <summary>
    /// reveal-delight/03 (AC-03): the HOST closes Golden Guardian voting early via the
    /// low-pressure "Reveal the winner" affordance, resolving it with whatever votes
    /// are in (mirroring the host's "move things along" posture elsewhere). Host-only
    /// is ENFORCED by the caller (the hub checks <see cref="IsHost"/>); this method
    /// only resolves. Idempotent: closing an already-resolved (or non-reveal) vote is
    /// a no-op that reports the current state. A close with zero votes resolves with
    /// no winner (no crown). Mutated only under the room lock.
    /// </summary>
    /// <returns>The resolution + live "N of M" figures for the hub to broadcast.</returns>
    public GoldenGuardianVoteResult CloseGoldenGuardian()
    {
        lock (_gate)
        {
            if (_round is null ||
                !string.Equals(_round.Phase, "reveal", StringComparison.Ordinal))
            {
                return BuildVoteResult(accepted: false, resolved: false);
            }
            if (!_round.GoldenGuardianResolved)
            {
                ResolveGoldenGuardian();
                LastActiveUtc = DateTimeOffset.UtcNow;
            }
            return BuildVoteResult(accepted: true, resolved: _round.GoldenGuardianResolved);
        }
    }

    /// <summary>
    /// True when <paramref name="blankToken"/> names a FILLED blank in the current
    /// round (a submission with a non-empty word) - the offered vote option set.
    /// Must be called under <see cref="_gate"/>.
    /// </summary>
    private bool IsFilledBlankToken(string? blankToken)
    {
        if (_round is null ||
            string.IsNullOrEmpty(blankToken) ||
            !int.TryParse(blankToken, out var index))
        {
            return false;
        }
        return _round.Submissions.TryGetValue(index, out var submission) &&
               !string.IsNullOrEmpty(submission.Word);
    }

    /// <summary>
    /// reveal-delight/03: resolve the funniest-word vote - pick the winning blank
    /// token and set the pending crown to its contributor. The winner is the blank
    /// with the most votes; ties are broken by "first blank to REACH that max count",
    /// walking the cast order (the exact deterministic rule web/src/engine/vote.ts
    /// pins). Zero votes -> no winner, no crown (a friendly non-event). Must be called
    /// under <see cref="_gate"/> with a non-null round in the reveal phase.
    /// </summary>
    private void ResolveGoldenGuardian()
    {
        if (_round is null)
        {
            return;
        }

        _round.GoldenGuardianResolved = true;

        // Only votes from players STILL PRESENT count toward the winner. A player who
        // voted then left the room during the reveal (HandlePlayerLeftAsync does not
        // abort a reveal-phase round) must not skew the winner or the crown, and must
        // not sit in the tie-break replay's cast order either - this keeps the resolved
        // winner consistent with the "N of M present voted" tally (Copilot review on #112).
        var presentConnectionIds = _players
            .Select(p => p.ConnectionId)
            .ToHashSet(StringComparer.Ordinal);

        // Tally per token, present voters only.
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (connectionId, token) in _round.GoldenGuardianVotes)
        {
            if (!presentConnectionIds.Contains(connectionId))
            {
                continue;
            }
            counts[token] = counts.TryGetValue(token, out var c) ? c + 1 : 1;
        }
        if (counts.Count == 0)
        {
            // No votes from present players - resolved with no winner, so no crown.
            _round.GoldenGuardianWinningBlankId = null;
            return;
        }

        var maxCount = counts.Values.Max();

        // Tie-break: replay the cast order (present voters only) and take the first
        // token to reach maxCount.
        var running = new Dictionary<string, int>(StringComparer.Ordinal);
        string? winningToken = null;
        foreach (var connectionId in _round.GoldenGuardianCastOrder)
        {
            if (!presentConnectionIds.Contains(connectionId) ||
                !_round.GoldenGuardianVotes.TryGetValue(connectionId, out var token))
            {
                continue;
            }
            running[token] = running.TryGetValue(token, out var c) ? c + 1 : 1;
            if (running[token] == maxCount)
            {
                winningToken = token;
                break;
            }
        }

        _round.GoldenGuardianWinningBlankId = winningToken;

        // Award the pending crown to the winning word's contributor for the NEXT
        // round (AC-04). The token is the blank's body-order index into the ordered
        // submissions, so map it straight to that submission's nickname.
        if (winningToken is not null &&
            int.TryParse(winningToken, out var winnerIndex) &&
            _round.Submissions.TryGetValue(winnerIndex, out var winnerSubmission) &&
            !string.IsNullOrEmpty(winnerSubmission.Nickname))
        {
            _pendingCrownNickname = winnerSubmission.Nickname;
        }
    }

    /// <summary>
    /// Build a <see cref="GoldenGuardianVoteResult"/> from the live round: the
    /// "N of M" figures (N = present players who have voted, M = present players) and
    /// the current resolution. Must be called under <see cref="_gate"/>.
    /// </summary>
    private GoldenGuardianVoteResult BuildVoteResult(bool accepted, bool resolved)
    {
        if (_round is null)
        {
            return new GoldenGuardianVoteResult(accepted, 0, 0, resolved, null, null);
        }

        var totalVoters = _players.Count;
        var votedCount = _players.Count(p => _round.GoldenGuardianVotes.ContainsKey(p.ConnectionId));
        string? winnerNickname = null;
        if (_round.GoldenGuardianResolved &&
            _round.GoldenGuardianWinningBlankId is not null &&
            int.TryParse(_round.GoldenGuardianWinningBlankId, out var winnerIndex) &&
            _round.Submissions.TryGetValue(winnerIndex, out var winnerSubmission))
        {
            winnerNickname = winnerSubmission.Nickname;
        }

        return new GoldenGuardianVoteResult(
            accepted,
            votedCount,
            totalVoters,
            _round.GoldenGuardianResolved,
            _round.GoldenGuardianResolved ? _round.GoldenGuardianWinningBlankId : null,
            winnerNickname);
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
    /// session-engine/07 (AC-06/AC-07): mint a per-seat, server-side,
    /// cryptographically random reconnect token. It is an OPAQUE 256-bit value (hex
    /// so it is a safe string on the wire, no reserved characters) with NO nickname,
    /// device fingerprint, or cross-room identity encoded - just enough entropy that
    /// it cannot be guessed or replayed. Scoped to exactly one seat in one ephemeral
    /// room and discarded with the seat (evicted or the room expires); it is never
    /// used to correlate a player across rooms or devices (no PII, README section 6 /
    /// CLAUDE.md section 5).
    /// </summary>
    private static string NewReconnectToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

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
                    StartedUtc = _round.StartedUtc,
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
