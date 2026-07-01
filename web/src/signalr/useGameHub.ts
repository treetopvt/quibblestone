// ----------------------------------------------------------------------------
//  useGameHub - React hook that owns the SignalR connection to the API's
//  GameHub. It is the web client's half of the real-time walking skeleton.
//
//  Responsibilities:
//    - Build and start ONE HubConnection (with automatic reconnect).
//    - Expose the live connection status so the UI can show connected / not.
//    - Own the current room state and the game invokes (createRoom, joinRoom,
//      clearRoom) plus the single "RosterChanged" handler.
//
//  Real game features (rooms, rosters, reveal) add more invokes/handlers on
//  this same connection rather than opening new ones.
//
//  session-engine/01 adds the first game invoke, createRoom, which asks the
//  hub's CreateRoom method to mint a room and returns the created room's state
//  (the join code + the roster with the host). The Player / RoomState types
//  below MIRROR the hub's PlayerDto / RoomStateDto wire contract
//  (api/src/Hubs/GameHub.cs) - keep them in sync.
//
//  session-engine/02 adds joinRoom (join an existing room by code + display
//  name, returning a JoinResult envelope) and, crucially, moves the LIVE room
//  state INTO this hook so roster updates flow through the ONE connection:
//  the hook registers a single "RosterChanged" handler (server broadcasts the
//  updated roster to a room's group when anyone joins) and exposes the current
//  `room`, so both the host and existing players see a newcomer appear in
//  near-real-time. App reads `room` from here instead of holding its own copy.
//
//  group-play/01 adds the round START seam on the SAME connection: startRound
//  (a host-only invoke - the server enforces the host check) and a single
//  "RoundStarted" handler that the server broadcasts to the whole room group so
//  every player transitions into word collection together. The hook exposes the
//  current `round` (the template id + mode + round number the client resolves
//  full content from) so App can route into the round. StartRoundResult /
//  RoundInfo below MIRROR the hub's StartRoundResultDto / RoundStartedDto wire
//  contract (api/src/Hubs/GameHub.cs) - keep them in sync BY HAND (no codegen).
//
//  group-play/02 adds one more handler on the SAME connection: "YourBlanks", a
//  PER-CONNECTION message the hub sends right after RoundStarted telling THIS
//  client only its own blank indices for the round (never another player's, no
//  PII). The hook exposes `assignedBlankIndices` (null until it arrives, cleared
//  on leave / round reset) so GroupRound fills only the blanks this player owes.
//  The YourBlanks type MIRRORS the hub's YourBlanksDto - keep it in sync BY HAND.
//
//  group-play/03 closes the real-time loop on the SAME connection: (a) submitWord
//  (an invoke that submits ONE word for ONE assigned blank; the server runs the
//  safety filter FIRST and records only on pass, so this maps 1:1 to FillBlank's
//  onSubmitWord accepted/message contract), (b) a "CollectProgress" handler ->
//  `collectProgress` (the "[N] of [M] done" counts + per-player done/writing list
//  the Waiting screen renders; NO submitted words, AC-01), and (c) a "RevealReady"
//  handler -> `reveal` (the ordered reveal words that route EVERY player to the
//  shared Reveal in near-real-time, AC-05 - the client resolves the template + does
//  assembly LOCALLY via the web engine; the server never assembles). The three new
//  types MIRROR the hub's SubmitWordResultDto / CollectProgressDto / RevealReadyDto
//  wire contracts - keep them in sync BY HAND (no codegen). All three states clear
//  on leave and reset on a fresh RoundStarted so a prior round never bleeds through.
//
//  group-play/04 adds the replay-loop seam on the SAME connection: (a) backToLobby
//  (a host-only invoke - the server enforces the host check - that ends the round
//  and returns EVERYONE to the lobby, same room + roster) and (b) a bare
//  "BackToLobby" handler that CLEARS round + reveal + collectProgress +
//  assignedBlankIndices so every client falls back to the Lobby it still holds
//  (room + roster stay set). "Play another round" needs no new invoke - it is just
//  startRound again on the same room; the server increments the round number and the
//  existing RoundStarted broadcast resets reveal + routes everyone into the new round.
//  NOTE: `round` is deliberately kept set THROUGH the reveal (RevealReady does NOT
//  clear it) so Round Complete can read round.roundNumber for its "ROUND N CARVED"
//  badge; round is cleared only on leave or on BackToLobby.
// ----------------------------------------------------------------------------

import { useCallback, useEffect, useRef, useState } from 'react';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';

const HUB_URL = import.meta.env.VITE_SIGNALR_HUB_URL;

export type ConnectionStatus = 'connecting' | 'connected' | 'disconnected';

/**
 * One player in a room, as sent by the hub (PlayerDto). Anonymous by design -
 * an in-session nickname (empty for the host until story 02 adds a name step),
 * a Guardian variant, and whether they are the host. No PII (README section 6).
 */
export interface Player {
  nickname: string;
  variant: string;
  isHost: boolean;
}

/**
 * The state of a room as returned by createRoom (RoomStateDto): the join code
 * plus the current roster (host first). Story 03 broadcasts this same shape on
 * roster changes.
 */
export interface RoomState {
  code: string;
  players: Player[];
}

/**
 * The result of joining a room (JoinResultDto), mirroring the hub's wire
 * contract. On a rejected join (unknown/expired code, an empty/too-long name, a
 * name the safety filter blocks, or a name already taken in the room), ok is
 * false and error carries a friendly, kid-readable message to show inline. On
 * success, ok is true and room is the updated roster.
 */
export interface JoinResult {
  ok: boolean;
  room: RoomState | null;
  error: string | null;
}

/**
 * The result of the host starting a round (group-play/01), mirroring the hub's
 * StartRoundResultDto wire contract. On a rejected start (unknown/expired code,
 * the caller is not the host, too few players, or no template available) ok is
 * false and error carries a friendly, kid-readable message to show inline. On
 * success ok is true and error is null - the actual round detail arrives for
 * EVERY player via the RoundStarted broadcast, not through this envelope.
 */
export interface StartRoundResult {
  ok: boolean;
  error: string | null;
}

/**
 * The current round as broadcast by the hub's RoundStarted event
 * (RoundStartedDto). Carries ONLY the selected template's id (the client
 * resolves the full prose/body from its bundled seedLibrary BY ID - the server
 * never ships template content), the mode ("classic-blind" in Slice 1), and the
 * 1-based round number. group-play/02 adds a separate per-connection message for
 * each player's own blank assignments.
 */
export interface RoundInfo {
  templateId: string;
  mode: string;
  roundNumber: number;
}

/**
 * The blanks THIS client owes for the current round (group-play/02), mirroring
 * the hub's YourBlanksDto wire contract. Sent per-connection as "YourBlanks"
 * right after RoundStarted - it carries ONLY this player's blank INDICES into
 * the round template's ordered blanks (the client resolves each to its prompt
 * via getBlanks(template)[index], Classic blind - prompt only, AC-02). It never
 * carries another player's blanks and no PII. The hook exposes just the indices;
 * they are null until "YourBlanks" arrives (a brief "dealing your blanks" beat
 * after the round starts) and cleared on leave / when a round resets.
 */
interface YourBlanks {
  blankIndices: number[];
}

/**
 * The result of submitting one word (group-play/03), mirroring the hub's
 * SubmitWordResultDto wire contract. On a rejected submission (no round / not in
 * the prompting phase, a word the safety filter blocks, or a blank that is not
 * this connection's) ok is false and error carries a friendly, kid-readable
 * message. On success ok is true and error is null. The submitWord invoke maps
 * this 1:1 to FillBlank's onSubmitWord contract (ok -> accepted, error -> message).
 */
interface SubmitWordResult {
  ok: boolean;
  error: string | null;
}

/**
 * One player's collection progress as sent by the hub's "CollectProgress" event
 * (PlayerProgressDto): an anonymous nickname + Guardian variant + whether they
 * have submitted ALL their assigned blanks. NO submitted words (AC-01) and no PII
 * beyond the already-filtered nickname + variant.
 */
export interface PlayerProgress {
  nickname: string;
  variant: string;
  done: boolean;
}

/**
 * Room-wide collection progress as broadcast by the hub's "CollectProgress" event
 * (CollectProgressDto), mirroring its wire contract: the "[N] of [M] quibblers
 * done" counts plus the per-player done/writing list the Waiting screen renders.
 * It carries NO submitted words (AC-01) - only who is done and who is still
 * writing. Null until the first progress arrives, cleared on leave / round reset.
 */
export interface CollectProgress {
  doneCount: number;
  playerCount: number;
  players: PlayerProgress[];
}

/**
 * One blank position for the reveal as sent by the hub's "RevealReady" event
 * (RevealWordDto): the submitted word plus its owning player (nickname + variant),
 * in blank order. An unfilled blank (a player who left) is an empty word
 * attributed to no one, so the client's assemble() keeps alignment. Every word
 * here already passed the safety filter server-side (AC-06).
 */
export interface RevealWord {
  word: string;
  nickname: string;
  variant: string;
}

/**
 * The shared reveal payload as broadcast by the hub's "RevealReady" event
 * (RevealReadyDto), mirroring its wire contract: the round's template id plus the
 * ordered reveal words (blank order). This is what routes EVERY player to the
 * shared Reveal in near-real-time (AC-05). The client resolves the template from
 * its bundled seedLibrary by id and assembles the story LOCALLY via the web engine
 * (the server never assembles). Null until it arrives, cleared on leave / round
 * reset so a prior round's reveal never bleeds into the next.
 */
export interface RevealInfo {
  templateId: string;
  words: RevealWord[];
}

export interface UseGameHub {
  status: ConnectionStatus;
  /** The current room (code + live roster), or null when not in one. Owned here so RosterChanged updates flow to every screen. */
  room: RoomState | null;
  /**
   * Whether THIS client is the host of the current room (session-engine/03).
   * True when this client created the room, false when it joined one, and
   * cleared when it leaves. The Lobby gates the host-only "Start game" CTA on
   * this (AC-05). It is tracked from the caller's own action rather than read
   * off the roster, because IsHost on a PlayerDto is not tied to a connection
   * on the wire (no PII), so a client cannot tell which roster row is "me".
   */
  isHost: boolean;
  /**
   * The current round (template id + mode + round number), or null while in the
   * lobby (group-play/01). Set from the hub's "RoundStarted" broadcast so every
   * player - host and joiners alike - transitions into the round together; App
   * routes into word collection when this is non-null. Cleared on leave.
   */
  round: RoundInfo | null;
  /**
   * The blank INDICES this client owes for the current round (group-play/02),
   * or null until the hub's per-connection "YourBlanks" message arrives (a brief
   * beat after `round` is set). GroupRound renders FillBlank over ONLY these
   * blanks, resolved to prompts via getBlanks(template)[index] (Classic blind -
   * prompt only, AC-02). Cleared on leave and whenever `round` resets, so a stale
   * assignment never bleeds into the next round.
   */
  assignedBlankIndices: number[] | null;
  /**
   * Room-wide collection progress (group-play/03), or null until the first
   * "CollectProgress" broadcast arrives. Carries the "[N] of [M] done" counts and
   * the per-player done/writing list (NO submitted words, AC-01) the Waiting
   * screen renders. Cleared on leave and reset on a fresh RoundStarted.
   */
  collectProgress: CollectProgress | null;
  /**
   * The shared reveal payload (group-play/03), or null until the hub's
   * "RevealReady" broadcast arrives (the moment the LAST assigned blank is
   * submitted). Non-null routes EVERY player to the shared Reveal in
   * near-real-time (AC-05); the client resolves the template + assembles LOCALLY
   * via the web engine. Cleared on leave and reset on a fresh RoundStarted.
   */
  reveal: RevealInfo | null;
  /**
   * Submit ONE word for ONE of this client's assigned blanks (group-play/03).
   * Invokes the hub's SubmitWord with the current room code (from roomCodeRef),
   * the blank INDEX, and the word; the SERVER runs the safety filter FIRST and
   * records only on pass (AC-01, AC-06). A SKIP submits an empty word so the blank
   * records an empty placeholder (preserving reveal alignment). Resolves with
   * { accepted, message } - the exact shape FillBlank's onSubmitWord expects
   * (accepted:false shows the message inline and lets the player retry). Does NOT
   * set `reveal` itself: the server's RevealReady broadcast drives that for
   * everyone. Returns a not-connected failure if the hub is not ready.
   */
  submitWord: (blankIndex: number, word: string) => Promise<{ accepted: boolean; message?: string }>;
  /**
   * Start a round as the host (group-play/01). Invokes the hub's host-only
   * StartRound with the current room code (from roomCodeRef) and the host's
   * family-safe toggle value; the SERVER enforces the host check and filters the
   * template catalog by the toggle (authoritative, AC-03/AC-04). Resolves with
   * the StartRoundResult envelope (ok + friendly error). Returns a not-connected
   * error envelope if the hub is not ready. Does NOT set `round` itself - the
   * server's RoundStarted broadcast drives that for everyone, including the host.
   */
  startRound: (familySafe: boolean) => Promise<StartRoundResult>;
  /**
   * Return the whole group to the lobby as the host (group-play/04, AC-05).
   * Invokes the hub's host-only BackToLobby with the current room code (from
   * roomCodeRef); the SERVER enforces the host check. Resolves with { ok, error }
   * (a friendly, kid-readable message on an expected rejection). Does NOT clear
   * round/reveal itself - the server's bare "BackToLobby" broadcast drives that for
   * EVERYONE (host included) so all players land back on the Lobby with the code +
   * roster preserved. Returns a not-connected failure if the hub is not ready.
   */
  backToLobby: () => Promise<{ ok: boolean; error: string | null }>;
  /**
   * Create a room and become its host (session-engine/01). On success updates
   * `room` and resolves with the created room's state, or undefined if the
   * connection is not ready. Uses the ONE shared connection - never a second.
   */
  createRoom: () => Promise<RoomState | undefined>;
  /**
   * Join an existing room by code with a display name and Guardian variant
   * (session-engine/02). Resolves with the JoinResult envelope; on ok it also
   * updates `room`. Returns a not-connected error envelope if the hub is not
   * ready. The display name is safety-checked server-side.
   */
  joinRoom: (code: string, displayName: string, variant: string) => Promise<JoinResult>;
  /**
   * Leave the current room and return Home. Tells the server (LeaveRoom) so this
   * connection is removed from the room group and its player leaves the roster
   * for everyone else, then clears local room state. The shared connection stays
   * open for the next game. Safe to call when not in a room (no-op).
   */
  clearRoom: () => void;
}

export function useGameHub(): UseGameHub {
  const connectionRef = useRef<HubConnection | null>(null);
  const [status, setStatus] = useState<ConnectionStatus>('connecting');
  const [room, setRoom] = useState<RoomState | null>(null);
  // Whether this client hosts the current room (set by createRoom / joinRoom,
  // cleared by clearRoom) - the Lobby's host-only Start CTA reads this (AC-05).
  const [isHost, setIsHost] = useState(false);
  // The current round, set from the "RoundStarted" broadcast (group-play/01) and
  // cleared on leave. Non-null flips App into word collection for every player.
  const [round, setRound] = useState<RoundInfo | null>(null);
  // This client's own blank indices for the round (group-play/02), set from the
  // per-connection "YourBlanks" message and cleared on leave / round reset. Null
  // until it arrives (a brief "dealing your blanks" beat after `round` is set).
  const [assignedBlankIndices, setAssignedBlankIndices] = useState<number[] | null>(null);
  // Room-wide collection progress (group-play/03), set from "CollectProgress" and
  // cleared on leave / round reset. Drives the Waiting screen's progress row.
  const [collectProgress, setCollectProgress] = useState<CollectProgress | null>(null);
  // The shared reveal payload (group-play/03), set from "RevealReady" and cleared
  // on leave / round reset. Non-null routes every player into the shared Reveal.
  const [reveal, setReveal] = useState<RevealInfo | null>(null);
  // Whether this client is currently seated in a room, plus that room's code.
  // Kept as refs (not state) so the stable RosterChanged handler and clearRoom
  // read the latest value without re-binding. The handler guards on inRoomRef so
  // a roster broadcast that RACES a leave cannot resurrect room state after the
  // player has already gone Home (the post-leave re-entry bug).
  const inRoomRef = useRef(false);
  const roomCodeRef = useRef<string | null>(null);

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl(HUB_URL)
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Information)
      .build();

    connectionRef.current = connection;

    // `cancelled` guards EVERY state update below, so a handler that fires during
    // teardown (StrictMode's dev remount, an HMR swap, or a real unmount racing
    // an in-flight reconnect/close) can never setState on a torn-down effect.
    let cancelled = false;

    // Live roster updates (session-engine/02): the hub broadcasts the updated
    // roster to a room's group whenever someone joins. Registered ONCE here,
    // before start(), so it is never missed and there is only one handler on
    // the one connection. Whoever is in the room (host + players) gets the new
    // roster and the UI updates in near-real-time (AC-05). Detached on cleanup.
    const handleRosterChanged = (state: RoomState) => {
      // Ignore a broadcast that arrives after we have left (raced our LeaveRoom /
      // group removal) or during teardown - otherwise it would resurrect room
      // state and bounce a player who just went Home back into the lobby.
      if (cancelled || !inRoomRef.current) return;
      setRoom(state);
    };
    connection.on('RosterChanged', handleRosterChanged);

    // Round start (group-play/01): the hub broadcasts "RoundStarted" to the whole
    // room group when the host starts a round, so every player - host and joiners
    // alike - moves into word collection together in near-real-time (AC-01,
    // AC-02). Registered ONCE here, before start(), so it is never missed, and
    // guarded by inRoomRef so a broadcast that races a leave cannot pull a player
    // who has gone Home back into a round.
    const handleRoundStarted = (info: RoundInfo) => {
      if (cancelled || !inRoomRef.current) return;
      // Reset any prior round's assignment so a stale one never shows for the new
      // round; "YourBlanks" (below) fills it in a beat later (group-play/02). Also
      // drop the prior round's progress + reveal (group-play/03) so a replay starts
      // clean and a stale reveal never re-routes a fresh round to the payoff screen.
      setAssignedBlankIndices(null);
      setCollectProgress(null);
      setReveal(null);
      setRound(info);
    };
    connection.on('RoundStarted', handleRoundStarted);

    // Per-player blank assignment (group-play/02): right after RoundStarted the
    // hub sends THIS connection its own blanks as "YourBlanks" (only its indices,
    // no other player, no PII). Registered ONCE here, before start(), so it is
    // never missed, and guarded by inRoomRef so a message racing a leave cannot
    // set an assignment for a player who has gone Home.
    const handleYourBlanks = (payload: YourBlanks) => {
      if (cancelled || !inRoomRef.current) return;
      setAssignedBlankIndices(payload.blankIndices);
    };
    connection.on('YourBlanks', handleYourBlanks);

    // Collection progress (group-play/03): the hub broadcasts "CollectProgress" to
    // the whole room group after each submission so every client can render the
    // Waiting screen's progress row (done/writing) and "[N] of [M] done" counts. It
    // carries NO submitted words (AC-01). Registered ONCE, guarded by inRoomRef so a
    // broadcast racing a leave cannot revive state for a player who has gone Home.
    const handleCollectProgress = (payload: CollectProgress) => {
      if (cancelled || !inRoomRef.current) return;
      setCollectProgress(payload);
    };
    connection.on('CollectProgress', handleCollectProgress);

    // Reveal transition (group-play/03, AC-05): the hub broadcasts "RevealReady" to
    // the whole room group the moment the LAST assigned blank is submitted, so every
    // player - done or still writing - moves to the shared Reveal in near-real-time
    // without refreshing. It ships the template id + ordered words; the client
    // resolves the template and assembles LOCALLY (the server never assembles).
    // Registered ONCE, guarded by inRoomRef.
    const handleRevealReady = (payload: RevealInfo) => {
      if (cancelled || !inRoomRef.current) return;
      setReveal(payload);
    };
    connection.on('RevealReady', handleRevealReady);

    // Back to lobby (group-play/04, AC-05): the host ended the round, so the hub
    // broadcasts a BARE "BackToLobby" to the whole room group. Every player drops
    // the round/reveal/progress/assignment locally and falls back to the Lobby it
    // STILL holds (room + roster stay set - the round ending does not change the
    // roster). Registered ONCE, guarded by inRoomRef so a broadcast racing a leave
    // cannot touch state for a player who has already gone Home.
    const handleBackToLobby = () => {
      if (cancelled || !inRoomRef.current) return;
      setRound(null);
      setReveal(null);
      setCollectProgress(null);
      setAssignedBlankIndices(null);
    };
    connection.on('BackToLobby', handleBackToLobby);

    connection.onreconnecting(() => {
      if (!cancelled) setStatus('connecting');
    });
    connection.onreconnected(() => {
      if (!cancelled) setStatus('connected');
    });
    connection.onclose(() => {
      if (!cancelled) setStatus('disconnected');
    });

    connection
      .start()
      .then(() => {
        if (!cancelled) setStatus('connected');
      })
      .catch(() => {
        if (!cancelled) setStatus('disconnected');
      });

    // Tear the connection down on unmount (and on StrictMode's dev remount).
    return () => {
      cancelled = true;
      connection.off('RosterChanged', handleRosterChanged);
      connection.off('RoundStarted', handleRoundStarted);
      connection.off('YourBlanks', handleYourBlanks);
      connection.off('CollectProgress', handleCollectProgress);
      connection.off('RevealReady', handleRevealReady);
      connection.off('BackToLobby', handleBackToLobby);
      void connection.stop();
    };
  }, []);

  const createRoom = useCallback(
    async (): Promise<RoomState | undefined> => {
      const connection = connectionRef.current;
      if (!connection || connection.state !== HubConnectionState.Connected) {
        return undefined;
      }
      const created = await connection.invoke<RoomState>('CreateRoom');
      inRoomRef.current = true;
      roomCodeRef.current = created.code;
      setRoom(created);
      setIsHost(true); // the creator is the host (AC-05)
      return created;
    },
    [],
  );

  const joinRoom = useCallback(
    async (code: string, displayName: string, variant: string): Promise<JoinResult> => {
      const connection = connectionRef.current;
      if (!connection || connection.state !== HubConnectionState.Connected) {
        return {
          ok: false,
          room: null,
          error: "We're not connected yet - give it a moment and try again.",
        };
      }
      const result = await connection.invoke<JoinResult>('JoinRoom', code, displayName, variant);
      if (result.ok && result.room) {
        inRoomRef.current = true;
        roomCodeRef.current = result.room.code;
        setRoom(result.room);
        setIsHost(false); // a joiner is never the host (AC-05)
      }
      return result;
    },
    [],
  );

  const startRound = useCallback(
    async (familySafe: boolean): Promise<StartRoundResult> => {
      const connection = connectionRef.current;
      const code = roomCodeRef.current;
      if (
        !connection ||
        connection.state !== HubConnectionState.Connected ||
        !code
      ) {
        return {
          ok: false,
          error: "We're not connected yet - give it a moment and try again.",
        };
      }
      // The server enforces the host check and filters the catalog by familySafe
      // (AC-03/AC-04). We do NOT set `round` here on success: the hub's
      // RoundStarted broadcast drives the transition for EVERYONE (host included)
      // so all players move together (AC-01, AC-02).
      return connection.invoke<StartRoundResult>('StartRound', code, familySafe);
    },
    [],
  );

  const backToLobby = useCallback(
    async (): Promise<{ ok: boolean; error: string | null }> => {
      const connection = connectionRef.current;
      const code = roomCodeRef.current;
      if (
        !connection ||
        connection.state !== HubConnectionState.Connected ||
        !code
      ) {
        return {
          ok: false,
          error: "We're not connected yet - give it a moment and try again.",
        };
      }
      // The server enforces the host check (group-play/04, AC-05). We do NOT clear
      // round/reveal here on success: the hub's bare "BackToLobby" broadcast drives
      // that for EVERYONE (host included) so all players fall back to the Lobby
      // together, with the code + roster preserved.
      return connection.invoke<{ ok: boolean; error: string | null }>('BackToLobby', code);
    },
    [],
  );

  const submitWord = useCallback(
    async (blankIndex: number, word: string): Promise<{ accepted: boolean; message?: string }> => {
      const connection = connectionRef.current;
      const code = roomCodeRef.current;
      if (
        !connection ||
        connection.state !== HubConnectionState.Connected ||
        !code
      ) {
        return {
          accepted: false,
          message: "We're not connected yet - give it a moment and try again.",
        };
      }
      // The SERVER runs the safety filter FIRST and records only on pass (AC-01,
      // AC-06); we never set `reveal` here - the RevealReady broadcast drives that
      // for everyone. Map the SubmitWordResult envelope to FillBlank's contract.
      const result = await connection.invoke<SubmitWordResult>('SubmitWord', code, blankIndex, word);
      return result.ok
        ? { accepted: true }
        : { accepted: false, message: result.error ?? 'That word is not allowed here. Try another!' };
    },
    [],
  );

  const clearRoom = useCallback(() => {
    const connection = connectionRef.current;
    const code = roomCodeRef.current;
    // Mark "left" BEFORE anything else so an in-flight RosterChanged is ignored.
    inRoomRef.current = false;
    roomCodeRef.current = null;
    setRoom(null);
    setIsHost(false);
    setRound(null); // group-play/01: drop any in-progress round on leave.
    setAssignedBlankIndices(null); // group-play/02: drop this client's blanks on leave.
    setCollectProgress(null); // group-play/03: drop collection progress on leave.
    setReveal(null); // group-play/03: drop any shared reveal on leave.
    // Tell the server so this connection leaves the room group and drops off the
    // roster for everyone else (AC-04). Fire-and-forget: returning Home must not
    // block on the network, and a failure (e.g. already disconnected) is harmless.
    if (connection && connection.state === HubConnectionState.Connected && code) {
      void connection.invoke('LeaveRoom', code).catch(() => {});
    }
  }, []);

  return {
    status,
    room,
    isHost,
    round,
    assignedBlankIndices,
    collectProgress,
    reveal,
    submitWord,
    createRoom,
    joinRoom,
    startRound,
    backToLobby,
    clearRoom,
  };
}
