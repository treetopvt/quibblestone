<!--
  Load / stress test findings for the QuibbleStone real-time backbone.
  Companion to the harness under /load (load/README.md is the runbook). Update the
  "as of" date + the results if you re-run. Hyphens/colons/parentheses, no em dashes.
-->

# Load test findings - the SignalR backbone

**As of 2026-07-07.** Ran the standalone SignalR load harness (`/load`, see
[`load/README.md`](../../load/README.md)) against a **local** API instance to
characterize how the real-time architecture behaves under concurrency, ahead of
the friends-and-family alpha.

## TL;DR

- **The architecture comfortably handles far more than the alpha will ever see.**
  A single instance drove **1,200 concurrent connections / 200 rooms / 200 rounds
  at 100% completion with zero errors**, p99 hub latency ~170 ms. The
  friends-and-family scale (a handful of 6-player rooms) is orders of magnitude
  below anything that showed stress - **the alpha is not load-constrained.**
- **The real ceiling is architectural, not code: one process, no scale-out.** The
  hub runs in-process and the room registry is an in-memory `ConcurrentDictionary`
  (CLAUDE.md section 10). Capacity == one node; there is no Azure SignalR backplane
  wired. Fine for a toy alpha; it is the first thing to address before any
  public-scale launch.
- **Two concrete holes surfaced, both tied to large rooms** (which the game does
  not intend, but does not prevent): a **reproducible "1 failed join per room"
  race** under a concurrent join storm, and **O(N^2) `RosterChanged` fan-out**
  during joins. Both are bounded to nothing by enforcing the 6-player cap the UI
  already implies (roadmap W2).
- **Reconnect + the AI gate held up under load.** 30% mid-round churn -> 160/160
  Rejoins succeeded, 100% completion; the AI jumble path degraded cleanly to the
  free fallback (zero cost locally).
- **These are LOCAL numbers - an optimistic upper bound.** UAT is a single **B1**
  App Service (1 core, ~1.75 GB), smaller than the test box, so its knee arrives
  sooner. **Run the harness against UAT** (from a machine with egress to it - this
  cloud sandbox's egress policy blocks the UAT host) to get the real B1 ceiling.

## Method + environment

- **Harness:** `/load` - a .NET SignalR client that plays the real full-round flow
  (CreateRoom -> JoinRoom -> StartRound -> per-player SubmitWord -> RevealReady)
  across many concurrent rooms, timing each hub invoke. See `load/README.md`.
- **Target:** local API (`dotnet run`, Release, logging at Warning so console I/O
  did not skew latency), in-process hub, in-memory registry - the SAME code UAT
  runs, on a different (larger) machine.
- **Why local, not UAT:** UAT was the intended target, but this session's egress
  proxy denies the UAT host by org policy (`connect_rejected` 403), so the run
  could not originate here. Local still exercises the identical hub/registry
  concurrency - it just does not show UAT's B1 resource ceiling or network RTT.
- **AI:** local `Ai:Endpoint` is unset, so `--ai` calls degrade to the free
  fallback (zero cost). Real gpt-5-mini cost only occurs against UAT.

## Results

Completion was **100% with zero errors in every run.** Latencies are successful
hub invokes only (ms).

| Scenario | Conns | Rooms x rounds | SubmitWord p50 / p95 / p99 | Notes |
|---|---:|---|---|---|
| Baseline | 60 | 10 x 1 | 20 / 21 / 26 | CreateRoom p50 ~89 ms = JIT warmup |
| Medium | 300 | 50 x 2 | 13 / 22 / 33 | 1,232 submits/s |
| Heavy | 600 | 100 x 2 | 21 / 80 / 101 | knee begins (p95 ~80-100 ms) |
| Very heavy | 1,200 | 200 x 1 | 27 / 97 / 167 | 1,200 conns, 0 errors, 0 failed connects |
| Big room | 250 | 10 x 1 (25 players) | 10 / 26 / 28 | **10 failed joins (1/room)**; RosterChanged 2,304 |
| Churn 30% | 240 | 40 x 2 | 9 / 25 / 28 | **160/160 Rejoins OK**, 0 aborts |
| AI (local) | 50 | 10 x 1 | - | **20/20 jumble fell back** (inert, free) |
| Large-room probe | - | 5x40, 20x20, 3x60 | - | **exactly 1 failed join per room**; RosterChanged O(N^2) |

Peak observed throughput: ~1,750 submits/s (100 rooms x 2 rounds), ~180 rounds/s.
Connection establishment never failed (0/1,200 at the top end).

## Findings

### F1 - Single in-process instance, no scale-out (architectural ceiling) - Medium

The hub is in-process and the room registry is a singleton in-memory
`ConcurrentDictionary` (`api/src/Rooms/RoomRegistry.cs`); no Azure SignalR backplane
is wired (`.AddAzureSignalR(...)` is a documented one-liner, not active -
`api/src/Program.cs`). So total capacity is one node's, all room state is
process-local, and a restart drops every live room. This is a deliberate alpha
choice (CLAUDE.md section 10) and was not a problem at any tested load, but it is
the hard ceiling: you cannot add a second instance for capacity or availability
without wiring the backplane AND moving room state off-process (or using sticky
sessions, which does not help a single room span nodes).
**Recommendation:** fine to leave for the alpha. Before public-scale launch, wire
Azure SignalR (backplane) and decide room-state ownership; until then, treat "one
App Service instance" as the capacity unit and scale UP (bigger SKU), not out.

### F2 - No per-room player cap + a reproducible large-room join race - Medium

`Room.TryAddPlayer` enforces nickname uniqueness but **no capacity limit**
(`api/src/Rooms/Room.cs`), and `StartRound` only requires >= 2. The lobby says "n
of 6" but nothing enforces 6 (roadmap W2). Under a concurrent join storm to one
room, the harness reproducibly recorded **exactly one failed join per room** (Ok=
false, not a transport error) - 1/room at 25, 40, and 60 players/room, independent
of room size, scaling with room *count*. The round still completed with the seated
players, so it is not a crash, but it is a real concurrency edge in the
JoinRoom/TryAddPlayer path that only appears when many clients join one room at
once - which only happens in (unintended) large rooms.
**Recommendation:** enforce the 6-player cap in `JoinRoom` with a friendly "room's
full" (roadmap W2) - that bounds this to nothing for real play. If large rooms are
ever wanted, investigate the 1-per-room JoinRoom race directly (the per-room
`_gate` + the async safety-filter await straddling the roster mutation is the place
to look).

### F3 - O(N^2) RosterChanged fan-out during a join storm - Low (Medium if caps lift)

Each successful join broadcasts `RosterChanged` to the whole room, so N joiners
arriving at once produce ~N^2 messages: measured ~1,520 for a 40-player room and
~3,490 for a 60-player room (vs a trivial ~30 for a 6-player room). Per-submission
`CollectProgress` fan-out is the gentler O(room size). For the intended 6-player
rooms this is a non-issue; it only bites if the cap is absent AND a big room forms.
**Recommendation:** the F2 cap also caps this. If large rooms are ever supported,
consider coalescing/debouncing roster broadcasts during a join burst.

### F4 - Reconnect + grace path is robust under load - Positive

30% mid-round churn (clean disconnect -> grace hold -> token Rejoin) over 40 rooms
gave **160/160 successful Rejoins, 0 RoundAborted, 100% completion**, Rejoin p99
~25 ms. The reconnect hardening (session-engine/07-11) holds up under concurrency -
no action needed.

### F5 - AI cost gate degrades cleanly - Positive (verify on UAT)

Local `--ai` runs put 20/20 jumble calls through `POST /api/ai/jumble`; all fell
back to the free reshuffle (Ai:Endpoint unset) with zero cost and zero errors,
confirming the gated path and its fail-safe. The spend-bounded behavior (per-session
quota 20, per-IP 30/min, $20/month breaker) can only be exercised against UAT -
do a small `--ai` run there and confirm 429s/fallbacks are recorded as healthy gate
signals, not errors (they are).

### F6 - Cold-start + warmup latency - Low (operational)

The first CreateRoom in a fresh process was ~90 ms (JIT); steady state was lower.
On App Service without **Always On**, an idle instance also cold-starts (seconds)
on the first request - a real first-player-of-the-day delay. Roadmap B2 already
bumped UAT to B1; confirm **Always On** is set for the test window.

## Run it against UAT (recommended next step)

This sandbox cannot reach UAT, so run from a machine that can. Discover the host and
point the harness at it (full runbook: `load/README.md`):

```bash
# UAT App Service name carries a uniqueString suffix; discover it:
az webapp list -g quibblestone-uat-rg \
  --query "[?tags.app=='quibblestone'].defaultHostName" -o tsv
# (the repo records the current one at infra/ai.uat.bicepparam:23:
#  quibblestone-uat-api-7achtfuwtltwo -> .azurewebsites.net)

# Gentle first pass, then ramp up across runs; watch App Insights + App Service CPU:
dotnet run -c Release --project load/QuibbleStone.LoadTest.csproj -- \
  --hub-url https://<uat-api-host>/hubs/game --rooms 10 --players-per-room 5 --rounds 2 --ramp 10
```

Ramp gradually (B1 is one small core), do **not** run during a live tester session,
and watch App Insights (hub exceptions, `HubAbnormalDisconnect`/`HubGraceStarted`)
plus App Service CPU / memory / HTTP queue length - the harness output is only half
the picture. A small `--ai` run there exercises the real gate at fraction-of-a-cent
cost.

## Caveats

- **Local numbers are an optimistic upper bound** vs UAT B1 (fewer cores, network
  RTT, cold starts). Read the *shape* (100% completion, gradual latency knee, the
  large-room edges), not the absolute ms, as the UAT prediction.
- Transport was WebSocket over loopback; through a corporate proxy or Cloudflare the
  transport may fall back (SSE/long-polling) and behave differently.
- The harness is a single-process client, so all traffic shared one source IP -
  representative of the server, but per-IP limits (AI 30/min) would bind a real
  multi-client crowd differently.
