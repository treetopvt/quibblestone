<!--
  Implementation plan for the session-engine feature. Bridges feature.md + stories to orchestration.
  Use hyphens/colons/parentheses, never em dashes.
-->

# Implementation Plan: Session & Room Engine

> The bridge between planning and orchestration. The **Wave Plan** below is DAG-ready: the `orchestrate-feature`
> skill's Phase 1 validates and adjusts it rather than deriving it. See
> [`docs/FEATURE_ORCHESTRATION_PLAYBOOK.md`](../../FEATURE_ORCHESTRATION_PLAYBOOK.md).

This feature grows the real-time backbone: the **one** `GameHub.cs` gains room methods and the **one**
`useGameHub.ts` gains the matching invokes/handlers. Stories 01-05 each edit those two shared files (and 03/04/06
share `Lobby.tsx`), so that core is a **serial chain** - concurrency 1 throughout. Story 06 is a later, web-only
add-on (no hub change) gated on client routing landing, so it sits outside the chain. It reuses three foundations:
`design-system` (theme, AppBar, Button, Guardian), `child-safety/01` (nickname filter), and the deployable backbone
from `platform-devops`.

Stories 07-10 are the deferred reconnect/rejoin hardening pass ("Don't Lose the Room" - see feature.md's Decisions
log for why this lands here rather than a new feature). They grow the SAME `GameHub.cs` + `api/src/Rooms/*` (07, 08)
then the SAME `useGameHub.ts` (+ `App.tsx` / `Lobby.tsx`, 09, 10) - another serial chain, concurrency 1, exactly
mirroring how 01-06 grew this feature's core. The API half (07, 08) can be built and xUnit-verified before the web
half exists; the web half (09, 10) is where a human sees the payoff.

## Reuse map

| Concern | Reuse | Where |
|---|---|---|
| Real-time connection (add invokes/handlers, never a 2nd connection) | the one SignalR hook | `web/src/signalr/useGameHub.ts` |
| API hub (grows room methods + an in-memory room registry) | the in-app SignalR hub | `api/src/Hubs/GameHub.cs` |
| Styling / theme tokens | the MUI theme (**design-system/01**) | `web/src/theme.ts` |
| Shared UI contracts | `AppBar`, gold-CTA + outlined-purple Button, `BottomActionBar` (**design-system/01**) | `web/src/components/` |
| Avatar | `<Guardian variant size />` (**design-system/02**) | `web/src/components/Guardian.tsx` |
| Child safety (nicknames are free text) | the server-side filter `IContentSafetyFilter` (**child-safety/01**) | `api/src/Safety/` |
| Icons | FontAwesome, registered once | `web/src/fontawesome.ts` |
| Config (hub/API URLs) | `import.meta.env` (`VITE_*`) | `web/.env.development` |
| Device-local, versioned, try/catch-everywhere storage pattern (the WEB stories 09-10 reuse this shape for the reconnect handle; 07-08 are server-side) | `identity.ts`'s load/save/clear + shape-validation posture | `web/src/identity.ts` |
| Operational telemetry (optional grace-window events, 07) | the App Insights client + `HubAbnormalDisconnect` posture (platform-devops/04) | `api/src/Hubs/GameHub.cs`'s `_appInsights` field |
| Roster tile visual idioms (10's "reconnecting" tile) | the existing pulsing-ring / dashed-border language | `web/src/pages/Lobby.tsx` (`PlayerTile`, `EmptySlot`) |
| Email delivery (an EXISTING seam owned by a different feature - reuse the transport, never the sign-in-specific token/purpose machinery, 12) | `IEmailSender` + `AcsEmailSender` / `NoOpEmailSender` + `EmailOptions`'s config-presence gate (**accounts-identity/04**) | `api/src/Accounts/` |

What this feature **exports** that others import:
- The **room model + registry** (ephemeral, in-memory) and the hub's room methods (`createRoom`, `joinRoom`, roster
  broadcast) - `group-play` builds its round methods on these and reuses the room group.
- The **player record** (`nickname`, `variant`, `sessionId`, host flag) carried in roster broadcasts - consumed by
  `group-play` distribution/attribution and the avatar rows on Waiting / Round Complete.
- The Home, Join, and Lobby **screens**.

## Wave Plan (DAG)

Sizing rule: a builder owns files **disjoint** from its concurrent siblings. The API/hub -> consuming-web chain is
serial (the hub signature is the contract; there is no codegen step).

| Story | Issue | Files it owns (footprint) | Depends-on | Can-run-with | Wave | Effort |
|---|---|---|---|---|---|---|
| 01 create-room | #20 | `api/src/Hubs/GameHub.cs` (room methods), `api/src/Rooms/RoomRegistry.cs`, `api/src/Rooms/Room.cs`, `web/src/signalr/useGameHub.ts`, `web/src/pages/Home.tsx` | platform-devops (backbone), design-system/01 | game-modes chain, the-reveal/01 (other features, disjoint) | 1 | high |
| 02 join-with-code | #21 | edits `GameHub.cs` (join method + code validation), `useGameHub.ts` (join invoke + roster handler), `web/src/pages/Join.tsx` (code + name form) | se/01, child-safety/01, design-system/01 | game-modes chain | 2 | high |
| 05 guardian-avatar-selection | #24 | edits `GameHub.cs` (player.`variant`), `useGameHub.ts`, `web/src/pages/Join.tsx` (avatar grid) | se/02, design-system/02, child-safety/01 | game-modes chain | 3 | low |
| 03 player-roster | #22 | edits `GameHub.cs` (roster broadcast + leave detection), `useGameHub.ts`, `web/src/pages/Lobby.tsx` | se/01, se/02, se/05, design-system/02 | game-modes chain | 4 | medium |
| 04 copy-share-room-code | #23 | `web/src/pages/Lobby.tsx` (share widget); edits `web/src/theme.ts` (filled-purple Share variant) | se/01, se/03, design-system/01 | game-modes chain | 5 | low |
| 06 share-room-link | #115 | edits `web/src/pages/Lobby.tsx` (share/copy payload -> deep link), `web/src/pages/Join.tsx` (seed code from `/join/:code` route param) | se/02, se/04, design-system Parked #59 (client routing) | - | post-routing | low |
| 07 disconnect-grace-window | #141 | edits `api/src/Rooms/Room.cs` (seat/token/grace state), `api/src/Rooms/RoomRegistry.cs` (mark-disconnected path), `api/src/Hubs/GameHub.cs` (`OnDisconnectedAsync`, `CreateRoom`/`JoinRoom` result envelopes gain the token, `PlayerDto` gains `Connected`) | se/01, se/02, se/03 | - | 6 | high |
| 08 rejoin-and-resume | #142 | edits `api/src/Hubs/GameHub.cs` (new `Rejoin` method + `RejoinResultDto`), `api/src/Rooms/Room.cs` (`ReclaimSeat` + rehydration snapshot helpers) | se/07 | - | 7 | high |
| 09 web-remember-and-rejoin | #143 | new `web/src/reconnect.ts`, edits `web/src/signalr/useGameHub.ts` (rejoin invoke + wiring to `onreconnected` + mount-time check, save/clear the handle) | se/08 | - | 8 | medium-high |
| 10 web-resume-live-screen | #144 | edits `web/src/App.tsx` (live-route guards wait on an in-flight rejoin), `web/src/pages/Lobby.tsx` (`PlayerTile`/`Player` type gain `connected`, a "reconnecting" tile treatment) | se/09 | - | 9 | medium |
| 11 invite-slot-action | - (no issue filed) | edits `web/src/pages/Lobby.tsx` (`InviteSlot` gains an `onClick`; `ShareWidget`'s Copy/Share closures are lifted into a shared helper/hook both call) | se/04, se/06, design-system/05 | - | 10 | low |
| 12 email-game-invite | #180 | new `api/src/Controllers/EmailInviteController.cs`, new `api/src/Invites/EmailInviteRateLimit.cs` (or `api/src/Rooms/`, builder's call); edits `api/src/Accounts/IEmailSender.cs` / `AcsEmailSender.cs` / `NoOpEmailSender.cs` (new send method + template), `api/src/Program.cs` (new rate-limit policy registration); edits `web/src/pages/useRoomInvite.ts` (new `sendEmail` / `emailAvailable`), new `web/src/pages/emailInvite.ts` (REST client), edits `web/src/pages/Lobby.tsx` (`ShareWidget` gains the email control) | se/04, se/06, se/11, accounts-identity/04 (shipped) | - | 11 | medium |
| 13 lifecycle-guards | #186 | edits `api/src/Hubs/GameHub.cs` (`StartRound` + `JoinRoom` phase guards + a shared `RoomNotFoundMessage` const), `api/src/Rooms/RoomRegistry.cs` (`SweepExpired` connected-seat exemption + a `SeatGraceService`-style test-window constructor), `web/src/signalr/useGameHub.ts` (wire the existing `resetRoomState()` into the in-room call wrappers on a room-not-found) | se/01, se/02, se/07, group-play/01 (all shipped) | - | 12 | medium |

**Concurrency per wave:** 1 at every wave. The chain is `01 -> 02 -> 05 -> 03 -> 04 -> 06` (all shipped), then the
reconnect hardening chain `07 -> 08 -> 09 -> 10` (all shipped), then the standalone follow-on `11` (shipped - web-only,
no hub change, edits the same `Lobby.tsx` as 04/06/10 so it runs after them rather than concurrently). Reason: 01/02/05/03 all edit `GameHub.cs` +
`useGameHub.ts` (the contract grows story by story); 05 and 02 share `Join.tsx`; 03 and 04 share `Lobby.tsx`; 07 and
08 both edit `Room.cs` + `GameHub.cs` (08's `Rejoin` spends the token/grace-state 07 mints); 09 and 10 both edit
`useGameHub.ts`-adjacent web surface (09 the hook, 10 the routing + roster tile that CONSUMES what 09 exposes).
Note the ordering choice **05 before 03**: the roster tiles render each player's chosen Guardian variant, so the
variant must be in the player record (05) before the roster (03) shows it - story 02 defaults the variant to `teal`
until 05 lands, which keeps 02 shippable on its own. The whole feature runs **in parallel with the `game-modes` and
`the-reveal` chains** (those touch the pure engine + FillBlank/Reveal screens, never the hub) - EXCEPT that any
in-flight `group-play`/`reveal-delight` story touching `GameHub.cs`'s disconnect/round-abort path or `Room.cs`'s
`RoundState` should NOT run concurrently with 07/08 (same files); check `git log` for open work there before
fanning out waves 6-7. Story 12 (email invite, Not Started) is a further standalone follow-on after 11 - it touches
neither `GameHub.cs` nor `Room.cs`/`RoomRegistry.cs` at all (a plain REST endpoint, deliberately decoupled from the
hub chain), so its only real serialization concerns are file-level: `api/src/Accounts/*` (shared with
accounts-identity, already shipped - no active conflict today) and `Program.cs`'s rate-limiter block (shared with
every other rate-limited endpoint in the repo) - check for open work on either before building it, same as any
other `Program.cs` edit. Story 13 (lifecycle guards, Not Started) is a standalone hardening follow-on (Wave 12,
concurrency 1): its three ACs (W3 `StartRound` guard, W4 `JoinRoom` guard, W1 idle-sweep exemption + client
fallback) all share `api/src/Hubs/GameHub.cs` (AC-01/AC-02 both edit it; AC-03 adds a shared not-found const there),
so they build as ONE unit, not fanned out - plus `RoomRegistry.cs` and `useGameHub.ts`. Because it edits the hub +
registry + hook, it must NOT run concurrently with any OPEN `group-play`/`reveal-delight` work touching those files
(all such work is shipped today - check `git log` before fanning out, same as waves 6-9).

## Per-story tech notes

### 01 - Create a room and get a join code
- **Approach:** add room creation to `GameHub.cs` plus an ephemeral in-memory `RoomRegistry` (rooms expire when idle
  - AC-05; this is a toy, not a system of record, README section 4). Server-side code generation: 4 characters,
  **exclude ambiguous glyphs** O/0/I/1/l, unique among active rooms (AC-02, AC-03). Build the Home screen (kicker
  chip, stone-tablet hero with the `HeroGuardian` asset, gold "Create a game" CTA + outlined-purple "Join a game"
  + the "No account needed" reassurance line) using the shared Button components and the one connection hook.
- **Owns / exports:** the room registry + model, the host-lands-in-lobby flow, the Home screen. `group-play` reuses
  the registry and room group.
- **Gotchas:** ephemeral state only (no DB - the in-process hub holds it, CLAUDE.md section 10). Out of scope:
  reconnect/rejoin, custom/locked rooms, persisting history, Home animations.

### 02 - Join a room with a code and nickname
- **Approach:** a host-validated `joinRoom(code, displayName, variant)` hub method that checks the code, runs the
  **nickname through `IContentSafetyFilter`** before storing/showing it (AC-03), enforces in-room name uniqueness
  (AC-06), and broadcasts the roster change (AC-05). The Join screen is the two-card form (room-code card + character
  card) with the interpolated "Join [CODE] ->" gold CTA and the "100% anonymous - no email, no account" reassurance
  (AC-02). Ask only for code + name - **no PII** (AC-02).
- **Owns / exports:** the join hub method and the Join form/flow. The variant arg defaults to `teal` until story 05.
- **Gotchas:** never render an unfiltered name to anyone. Friendly, brief validation messages (the audience includes
  kids). Out of scope: rejoin-after-disconnect, persistent identity, the avatar-grid UI (that is story 05).

### 05 - Guardian avatar selection at join
- **Approach:** "the same join call, plus a chosen variant." Add `selectedVariant` state (default `teal`) and a
  3-column grid of `<Guardian variant size={52} />` tiles to the Join screen (single-select, gold ring + check-badge
  pop on selection), and extend the player model with `variant` (`purple | gold | coral | teal | sand | plum`)
  stored in room state and broadcast in roster updates (AC-03, AC-04).
- **Owns / exports:** the variant on the player record - consumed by the roster (03), Waiting (`group-play/03`), and
  Round Complete (`group-play/04`).
- **Gotchas:** entrance ring/badge uses `transform: scale` only (never opacity on a list item). The nickname filter
  (AC-05) is the same `child-safety/01` call story 02 already wires - this story just adds the variant to that call.
  Out of scope: persisting the choice across sessions (Phase 2), custom avatars, >6 variants.

### 03 - See a live player roster
- **Approach:** the hub broadcasts join/leave to the room group; the web client subscribes via the one hook and
  re-renders a 3-column tile grid (74px circle tiles, each with `<Guardian variant size={52} />`, name, role chip;
  host gets crown badge + pulsing gold ring). "Carvers gathered" count chip, scale-pop entrance + transient "[Name]
  pulled up a stone" toast, dashed pulsing empty slots, host-only "Start game" CTA (AC-01 to AC-05). Every displayed
  name has already passed the filter at join (AC-06).
- **Owns / exports:** the Lobby screen and roster rendering. The host-only Start CTA is the seam `group-play/01`
  hangs the round-start on.
- **Gotchas:** leave detection rides the SignalR connection lifecycle for Slice 1. `transform: scale` entrances
  only; the toast is short-lived state, not permanent UI. Out of scope: rich presence, reconnect states, host
  kick/mute, capacity beyond 6.

### 04 - Copy and share the room code from the Lobby
- **Approach:** **web only**, no API change (the code is already in client state from story 01). A stone-tablet share
  widget with `navigator.clipboard.writeText()` "Copy" (teal-check "Copied!" for ~1.8s, then revert) and a Web Share
  button gated on `navigator.share && navigator.canShare()` with graceful fallback when unavailable (AC-02 to AC-04).
- **Owns / exports:** the Lobby share widget. Adds a **filled-purple** Share button variant to `theme.ts` if not
  already present (a third button style per the design spec).
- **Gotchas:** "Copied!" is local component state (`setTimeout` revert), no server round-trip. This is the one
  session-engine story that edits `theme.ts` - it must land after `design-system/01` and not overlap any other
  `theme.ts` editor. Out of scope: link-with-code URLs, QR codes, deep links (those are story 06, gated on routing).

### 06 - Share a join link to the room (deep-link share)
- **Approach:** **web only**, no hub change. Upgrade story 04's Web Share + Copy payload from the bare code to a full
  `/join/:code` deep link built from the app origin (or a `VITE_PUBLIC_BASE_URL`), and seed the Join screen's code
  field from the route param so a recipient lands pre-filled and only picks a nickname + Guardian (AC-01, AC-02, AC-04,
  AC-06). Normalize/validate the route-supplied code exactly as a typed one (AC-03). This is the live-room sibling of
  `keepsake-gallery/04` (which shares a finished tale's read-only page) - deliberately a different surface.
- **Owns / exports:** the deep-link share/copy payload on the Lobby, plus "seed code from route" on Join.
- **Gotchas:** **hard dependency on client routing** (design-system Parked #59 - react-router with `/join/:code`);
  this story is Blocked until that lands and does NOT implement routing itself. Keep `useGameHub` mounted once ABOVE
  the router so the one connection is never remounted. Never hardcode the link host (AC-06). The link carries only the
  room code - no nickname, token, or PII (AC-07). Reuse story 04's `typeof navigator.share === 'function'` detection;
  do not gate a text/URL share on `navigator.canShare()`. Out of scope: QR codes, auto-join/nickname-prefill links.

### 07 - Hold the seat: a disconnect grace window
- **Approach:** today `GameHub.OnDisconnectedAsync` calls `RoomRegistry.RemoveConnection` immediately, and
  `HandlePlayerLeftAsync` aborts a "prompting" round on the spot. This story inserts a grace window between "the
  connection dropped" and "the seat is actually gone": on an abnormal disconnect, mark the seat disconnected (keep it
  in the roster, `PlayerCount` unchanged, the round NOT aborted) and schedule a ONE-SHOT delayed eviction as a
  fire-and-forget `async` task (`await Task.Delay(graceWindow, ct)` under a per-seat `CancellationTokenSource` that
  story 08's `Rejoin` cancels on reconnect; wrapped in try/catch so a fault is logged, never unobserved) that, on
  firing, performs exactly today's eviction + conditional `RoundAborted` + `RosterChanged` if the seat is STILL
  disconnected. Deliberately NOT `Task.Delay(...).ContinueWith(...)` - ContinueWith's default-scheduler +
  unobserved-exception semantics make it a footgun for a fire-and-forget timer. `LeaveRoom` (deliberate) is untouched - it keeps evicting immediately, no grace. Also mints
  a server-side, cryptographically random reconnect token at `CreateRoom`/`JoinRoom`, returned ONLY in that call's own
  result envelope (never on `RoomStateDto`/`PlayerDto`, which every player receives) - story 08 is the only consumer.
- **Owns / exports:** the seat's disconnected/grace state + the reconnect token on `Room`'s player record; `PlayerDto`
  gains `Connected` (consumed by web story 10's roster tile). Story 08 spends the token via `Rejoin`.
  `RoomRegistry`/`Room` gain the mark-disconnected path alongside the existing `RemoveConnection`.
  `CreateRoomResultDto`/`JoinResultDto` gain the token field.
  `HandlePlayerLeftAsync`'s abort-on-prompting logic is now invoked from the grace-expiry path, not directly from
  `OnDisconnectedAsync`.
- **Gotchas:** `Player` is an immutable `record` - "mark disconnected" rebuilds the roster entry under the room's
  `_gate` lock (mirrors how `RecordSubmission` swaps in a fresh dictionary), it does not mutate a field in place. The
  scheduled continuation is the ONE place in this codebase a timer is justified over the lazy-on-access sweep
  `RoomRegistry.SweepExpired` uses elsewhere - the grace window is seconds, not the 30-minute idle window, and the
  OTHER seated players are actively waiting, so eviction must be pushed even if nobody calls the hub in the meantime.
  `GameHub`'s constructor also now takes `IEntitlementService` (ai-cost-gate/02, already merged) - test builders
  supplying a `GameHub` need that extra argument. Out of scope: the `Rejoin` method itself (08), any client UI (09,
  10), host migration (parked - see feature.md), persisting seat state beyond process memory.

### 08 - Rejoin: reclaim your seat and resume the round
- **Approach:** a new `Rejoin(code, token)` hub method that finds the seat 07 is holding, swaps in the caller's NEW
  `Context.ConnectionId`, re-subscribes it to the room's SignalR group (`Groups.AddToGroupAsync`), cancels the
  pending grace-expiry continuation, and returns a rehydration envelope: the roster, the host flag, and the round's
  phase ("lobby" | "prompting" | "reveal"). For "prompting" it ALSO returns this seat's own remaining (not-yet-
  submitted) blank indices - reusing the same index-only, no-PII shape as the existing `YourBlanksDto` - plus the
  room's current `CollectProgress` counts; for "reveal" it returns the shared `RevealReadyDto` payload (the words),
  so the resuming client renders the same story everyone else sees. A successful reclaim re-broadcasts `RosterChanged`
  with that seat's `Connected` flipped true (AC-06) so everyone else's tile updates.
- **Owns / exports:** the `Rejoin` hub method + `RejoinResultDto` (the new wire contract story 09 consumes),
  `Room.ReclaimSeat(token, newConnectionId)`, and small pure snapshot helpers reusing `Room`'s EXISTING
  `GetProgress()`/`GetProgressCounts()`/`BuildReveal()` (no new parallel round bookkeeping) for exactly one seat's
  remaining blanks.
- **Gotchas:** an unknown/expired/wrong-room token is a friendly `Ok:false` envelope, never a throw (mirrors every
  other hub method's envelope style). A race between "grace expires" and "Rejoin lands" must resolve deterministically
  under the same `_gate` lock - whichever wins, the other is a no-op. Deliberately out of scope: reaction-tally /
  Golden Guardian vote-state rehydration on a reveal-phase reconnect (parked in feature.md as a fast-follow - those
  counters are room-wide broadcast state, not per-seat, so they just show at their current value on the NEXT
  broadcast); host migration (still parked - a reconnecting HOST whose grace already expired has no seat left to
  reclaim); multi-device resume (the token is device-local, not a player account).

### 09 - Web: remember this seat and rejoin automatically
- **Approach:** a new `web/src/reconnect.ts` (mirroring `identity.ts`'s versioned-key, try/catch-everywhere,
  validate-the-shape posture) storing exactly one `{code, token}` pair, saved on every successful `createRoom`/
  `joinRoom`, cleared on `clearRoom` (deliberate leave/Home). `useGameHub.ts` gets an internal `rejoin()` helper
  wired to BOTH the EXISTING `connection.onreconnected(...)` handler (extend it, do not add a second) and a one-shot
  mount-time check ("connected AND no room AND a stored handle exists" - covers a full page reload / app relaunch,
  not just a same-tab network blip). On success it feeds the SAME setters the normal join/round flow already
  populates (`setRoom`, `setIsHost`, `setRound`, `setAssignedBlankIndices`, `setCollectProgress`, `setReveal`) - no
  new parallel state tree. On failure it discards the stored handle.
- **Owns / exports:** `web/src/reconnect.ts`'s load/save/clear API; the hook's internal auto-rejoin wiring. Story 10
  consumes whatever state this populates (it does not add its own trigger).
- **Gotchas:** reuse the EXISTING `inRoomRef`/`roomCodeRef` race-guard pattern (the file's own "post-leave re-entry
  bug" comment) rather than inventing a second guard against a rejoin racing a deliberate leave. No new
  `HubConnection` - this rides the one connection. Out of scope: any visible UI (10), retrying a FAILED rejoin
  attempt (one attempt per trigger is enough for Slice 1).

### 10 - Web: resume the live screen, don't bounce Home
- **Approach:** thread a "rejoin in flight" signal (from 09) through `App.tsx`'s live-route guards (`/lobby`,
  `/round`, `/reveal`), which today redirect to `/` the instant their backing state is null - show a brief
  "reconnecting your game..." beat instead while that signal is true, falling through to today's `<Navigate to="/" />`
  once it resolves negatively. Once 09's rejoin resolves, the EXISTING routing effect (recap > reveal > round >
  lobby precedence) already lands the player on the right screen with no extra code - this story just stops it from
  redirecting Home FIRST. Add a `connected` flag to the web `Player` type (mirroring `PlayerDto.Connected` from 07)
  and render a dimmed/pulsing "reconnecting..." tile variant in `Lobby.tsx`'s `PlayerTile`, reusing the file's own
  established pulsing-ring / dashed-border visual language (`EmptySlot`, the host's pulsing gold ring) rather than a
  new visual system.
- **Owns / exports:** the resume-aware live-route guards; the roster's "reconnecting" tile. This is the story that
  delivers the user-visible payoff of 07-09.
- **Gotchas:** keep the "reconnecting..." messaging calm and kid-readable (README section 10) - a blip reads as
  "hang tight," never an alarm. Verify with the two-browser-context walkthrough this repo's orchestration playbook
  already uses for real-time stories (Phase 4): drop one browser's network mid-round, confirm the OTHER browser's
  tile changes and the round waits, restore the network, confirm the dropped browser resumes on the SAME screen with
  only its remaining blanks. Out of scope: any change to the reconnect MECHANICS (07-09 own those), host-migration
  UI, a persistent connection-quality indicator beyond the roster tile.

### 11 - Wire the "+ invite" roster slot to the share action (shipped)
- **Approach:** the "+ invite" slot (`design-system/05`) was a decorative, non-interactive dashed circle.
  `ShareWidget`'s Copy/Share closures (`handleCopy`, `handleShare`, the `joinLink` build, the `canShare`
  feature-detect, the `copied` confirmation state) were lifted into a new shared hook,
  `web/src/pages/useRoomInvite.ts`'s `useRoomInvite(code)`, that both `ShareWidget` and `InviteSlot` now call.
  `InviteSlot` became a real `Box component="button" type="button"` with an `onClick` that invokes it: Share-first
  when `navigator.share` is available, Copy-plus-brief-confirmation fallback otherwise (mirroring the widget's own
  posture - the slot swaps its "+" glyph for a check + "copied!" caption while `copied` is true). No hub/API
  change - this is the same client-side-only surface as stories 04/06.
- **Owns / exports:** `useRoomInvite(code)` (`web/src/pages/useRoomInvite.ts`) - the shared invite-action hook both
  the widget and the slot call; nothing new for other features to consume. Its `resolveOrigin` helper is exported
  and covered by `useRoomInvite.test.ts` (Vitest, node env - the one pure decision in an otherwise-stateful hook).
- **Gotchas:** do not fork the copy/share logic into a second implementation (AC-03) - this is the whole point of
  the story. Keeps story 04's feature-detection posture (`typeof navigator.share === 'function'`; never gated on
  `navigator.canShare()`). Not host-gated (AC-04) - the room code is already visible to everyone on this screen. Out
  of scope: changing the share payload/wording (04/06 own that), a QR code, or any visual redesign of the slot.

### 12 - Email a game invite to a friend
- **Approach:** a third invite channel alongside Copy/Share, added as a NEW, stateless REST endpoint (no hub method,
  no `Room.cs` / `RoomRegistry.cs` touch) that shape-validates a room code (the same alphabet/length
  `RoomRegistry.GenerateCode` mints from) and an email address, builds the `/join/:code` link server-side (reusing
  an existing "public web app origin" config value - `EmailOptions.LinkBaseUrl` recommended - never a
  client-supplied URL), and sends a FIXED-template invite email through a NEW method on the EXISTING `IEmailSender`
  seam (accounts-identity/04) - both `AcsEmailSender` and `NoOpEmailSender` gain that method, but the sign-in-specific
  `MagicLinkPurpose` / `IMagicLinkTokenService` machinery is untouched and unused here. The web side extends
  `useRoomInvite(code)` (session-engine/11) with an `emailAvailable` flag (read before rendering the control,
  mirroring `GET /api/billing/products`'s `enabled` posture) and a `sendEmail(toEmail)` action, surfaced as a new
  input + button in `Lobby.tsx`'s `ShareWidget`.
- **Owns / exports:** the new send endpoint + its rate-limit policy; the `IEmailSender` interface's second method
  (any future feature that wants a plain notification email, not a magic link, can now reuse it too); `useRoomInvite`'s
  `sendEmail` / `emailAvailable`.
- **Gotchas:** never accept a client-supplied link to embed in the email body (open-relay / phishing smell) - the
  server builds it from a shape-validated code. Never call `SendMagicLinkAsync` / pass a `MagicLinkPurpose` for this -
  it is a different method with its own template. Per-IP rate limit only (mirrors every existing limiter in this
  codebase; no per-room counter). Not host-gated (mirrors story 11 AC-04). No free-text field (no profanity-filter
  surface) unless a personal note is added later, which would need its own AC. Out of scope: multi-recipient sends, a
  resend/retry action, delivery-status tracking, validating the room still exists before sending.

### 13 - Round and room lifecycle guards (StartRound, JoinRoom, idle sweep)
- **Approach:** three small, server-authoritative guards closing the roadmap's W3/W4/W1 alpha-gate warnings, all on
  EXISTING live code (no new surface, no new free-text field, no new player data). AC-01 (W3): reject `StartRound`
  (`GameHub.cs`) while `CurrentRound.Phase == "prompting"` - mirror `PassHost`'s exact phase-check, guard
  "prompting" ONLY (a "reveal"-phase restart is the shipped "Play another round" flow and must keep working).
  AC-02 (W4): reject `JoinRoom` (`GameHub.cs`) whenever `CurrentRound is not null` (BOTH "prompting" and "reveal"),
  checked first, before the name-length + async safety-filter work. AC-03 (W1): narrow `RoomRegistry.SweepExpired()`'s
  cull to also require no connected seat (`!SnapshotPlayers().Any(p => p.Connected)`), plus wire the existing
  `resetRoomState()` into `useGameHub.ts`'s in-room call wrappers when the server returns the room-not-found message.
- **Owns / exports:** the two hub-method phase guards; a shared `RoomNotFoundMessage` const on `GameHub` (collapsing
  today's six duplicated literals); a public `RoomRegistry` test-window constructor + `DefaultInactivityWindow`
  (mirroring `SeatGraceService`'s dual-constructor) that finally makes the idle sweep xUnit-testable; a small pure
  `isRoomNotFoundError` classifier on the web side.
- **Gotchas:** AC-01 must NOT gate on "any round exists" (that breaks the reveal-phase replay). AC-02 must cover BOTH
  non-lobby phases (`RoomStateDto` carries no phase, so a joining client cannot self-detect a live round) and must
  NOT touch `Rejoin` (a resume, not a new join). AC-03 picks the exemption over lengthening the window; keep the
  30-minute default for every other `new RoomRegistry()` call site untouched; wire `resetRoomState()` ONLY into the
  AFTER-join wrappers (`startRound`/`backToLobby`/`passHost`/`submitWord`/`remixWord`), never `joinRoom`/`createRoom`
  (pre-join validation) or `rejoin` (already alpha-gate-B4-fixed). All three ACs share `GameHub.cs`, so build as one
  unit. Full detail in the story's Technical Notes.

## Cross-cutting concerns

- **Inter-feature ordering (prerequisites, assumed merged before this feature orchestrates):** `child-safety/01`
  (nickname filter), `design-system/01` (theme + AppBar + Button), `design-system/02` (Guardian), and the
  `platform-devops` backbone. This feature must complete before `group-play` (which grows the **same** `GameHub.cs`
  with round methods - they cannot overlap) and before `single-player/01` (which edits `Home.tsx`, owned by se/01).
- **One SignalR connection:** all real-time work adds invokes/handlers to `useGameHub.ts` - never a second
  `HubConnection`. Hub/API URLs come from `import.meta.env`.
- **Child safety + no PII:** nicknames route through the filter before display; the only data held on a player is the
  in-session nickname + Guardian variant (anonymous, README sections 3 and 6).
- **No i18n** (plain strings), **big tap targets**, **no em dashes**.
- **Reconnect/rejoin (stories 07-10):** a reconnect token is minted server-side and returned ONLY to its owning
  connection - never broadcast on `RoomStateDto`/`PlayerDto` (a token on the wire to other players would let one
  hijack another's seat). It is anonymous, device-local, and scoped to exactly one seat in one ephemeral room -
  never a cross-room or cross-device identity (README section 6 / CLAUDE.md section 5). Reconnect is a
  `session-engine` concern grown on the SAME room/round model - it must never fork by mode ("one engine, many thin
  modes"). Before fanning out waves 6-9, check for any OPEN `group-play`/`reveal-delight` work touching
  `GameHub.cs`'s disconnect/round-abort path or `Room.cs`'s `RoundState` (same files, must not run concurrently).
- **Email invite (story 12):** reuses accounts-identity/04's `IEmailSender` seam but NEVER its sign-in-specific
  `MagicLinkPurpose` / `IMagicLinkTokenService` machinery (a game invite is not a sign-in flow and mints no token),
  and never accepts a client-supplied link to embed in the sent email (builds it server-side from a shape-validated
  room code only, to avoid an open-relay / phishing smell) - see the story's own Technical Notes for the full
  reasoning.
