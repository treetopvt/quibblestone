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

> **Update - fixed on this branch.** `Room.AddPlayer` now caps a room at
> `Room.MaxPlayers` = 6 (host included), atomically under the room lock, and
> `JoinRoom` returns the friendly "room's full" message. This bounds F2 (the join
> race) and F3 (the O(N^2) fan-out) for all real play. Covered by
> `RoomCapacityTests` + a `GameHubJoinTests` case. The low-level `TryAddPlayer`
> stays uncapped for the distribution invariant sweep (N up to 8).

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

## UAT results (as of 2026-07-07)

Ran the same harness against the deployed **UAT** environment
(`quibblestone-uat-api-7achtfuwtltwo`, hub `/hubs/game`) from a workstation with
egress to it - the run the LOCAL section above recommended but could not perform
from the cloud sandbox. This is the real **B1** ceiling, over the real network, with
App Insights and the AI cost gate live. Deployed build was `cba9c69` (current
`main`).

### TL;DR (UAT)

- **The B1 sustained the whole tested range - up to 600 concurrent connections /
  100 rooms / 200 rounds - at 100% completion, 0 errors, 0 failed connects. No hard
  failure knee was reached.** The server was never resource-bound: plan CPU peaked
  at **42%** (during churn), memory at **82%** (working set grew only ~40 MB, 148 ->
  188 MB over the whole campaign), HTTP queue length stayed **0**, and there were
  **zero HTTP 5xx** in every run.
- **The cost of load on B1 is latency-tail growth, not failures.** Friends-and-family
  scale (a handful of 6-player rooms) sits orders of magnitude below anything that
  showed strain - **the alpha is not load-constrained on UAT**, confirming the LOCAL
  conclusion on the actual B1 hardware.
- **F2 did not reproduce and no round ever aborted.** 0 failed joins at every rung;
  0 RoundAborted anywhere (including churn + AI).
- **Reconnect (F4) and the AI gate (F5) held up, and the AI gate was proven with
  real spend.** Real gpt-5-mini generation works on UAT; the path fails safe.
- **Follow-up ceiling probe (sustained, to 3,000 conns) - connection *count* was
  never the wall.** The B1 held **3,000 concurrent connections at 100% completion**
  and <= 49% CPU; the single load-client machine became the bottleneck first
  (~2,400-3,000 conns). The only thing that pegged server CPU was connect /
  disconnect **storms** (~98-99%), and **memory** is the trending sustained limit
  (88% at 3,000 conns). See "Ceiling probe" below.

### Environment + method delta vs LOCAL

- **Target:** single **B1** App Service (1 core, ~1.75 GB), **Always On enabled**
  (confirmed for the test window - addresses F6, no cold-start tax). In-process hub
  **confirmed on UAT**: `POST /hubs/game/negotiate` returns an in-process
  `connectionToken` with `availableTransports` (WebSockets/SSE/LongPolling) and
  **no** Azure SignalR redirect URL. Transport was WebSocket (site request count
  tracked ~negotiate count, not the request storm long-polling would produce).
- **Azure SignalR is provisioned but unwired (confirms F1 on UAT).** The RG holds
  `quibblestone-uat-signalr-...` at **Free_F1**, Default mode, but no connection
  string is wired into the app - so the B1 in-process hub is the capacity unit,
  exactly as F1 (written from local) assumed. Note Free_F1 caps at ~20 concurrent
  connections, so it could not carry this load even if wired; real scale-out needs a
  paid tier plus `.AddAzureSignalR(...)`.
- **AI is live** (`Ai__Endpoint` + `Ai__Deployment` = gpt-5-mini are set), so `--ai`
  spent real (tiny) money - see the AI run.
- **Observability:** App Service **platform metrics** (CPU / memory / HTTP queue /
  requests / 5xx / response time) were reliable and are the basis for the
  server-side numbers here. The **App Insights Logs query API** intermittently
  returned `BadArgumentError` / empty this session (a known query gotcha), so
  server-side confirmation of the `HubGraceStarted` / `HubAbnormalDisconnect` custom
  events and the AI cost-attribution events could **not** be captured; the
  client-side harness fully covers the behavioural claims below, and the
  `exceptions` table returned empty (no hub exceptions) when it did respond.

### Results

Completion was **100%, with zero errors and zero failed connects, in every run.**
Latencies are successful hub invokes only (ms), warm unless noted.

| Scenario | Conns | Rooms x players x rounds | Compl. | SubmitWord p50 / p95 / p99 | Thru (submits/s) | Notes |
|---|---:|---|---|---|---:|---|
| Smoke | 12 | 3 x 4 x 1 | 100% | 150 / 1186 / 1186 | 7.8 | cold process (first CreateRoom 382 ms, first StartRound/SubmitWord tail ~1.2 s = JIT warmup) |
| Ramp 1 | 50 | 10 x 5 x 2 | 100% | 51 / 82 / 88 | 80 | warm steady state |
| Ramp 2 | 150 | 25 x 6 x 2 | 100% | 70 / 143 / 368 | 130 | tail starts to grow |
| Ramp 3 | 300 | 50 x 6 x 2 | 100% | 327 / 675 / 934 | 85 | transient dip at ~30% CPU (see below) |
| Ramp 3 re-run | 300 | 50 x 6 x 2 | 100% | 93 / 377 / 508 | 133 | true steady state - dip not reproduced |
| Ramp 4 (ceiling) | 600 | 100 x 6 x 2 | 100% | 135 / 361 / 1114 | 183 | still clean; CPU max 34% |
| Churn 25% | 120 | 20 x 6 x 2 | 100% | 51 / 126 / 154 | 109 | 40/40 Rejoins OK, 0 aborts, Rejoin p50 43 / p99 142 |
| AI (real) | 50 | 10 x 5 x 1 | 100% | 121 / 1600 / 1605 | - | 15/20 gpt-5-mini generated, 4 fell back, 1 canceled, 0x 429; AiJumble p50 10.4 s / max 28.8 s |

Server-side envelope across the whole campaign: plan CPU 13-42%, memory 78-82%
(working set 148-188 MB), HTTP queue length 0, HTTP 5xx 0. Connection establishment
never failed (0 / 600 at the top end).

### The B1 knee

There is **no failure knee in the tested range** - nothing failed up to 600 conns.
What grows is the **latency tail**: warm SubmitWord p50 rose 51 -> 70 -> 93 -> 135 ms
and p99 rose 88 -> 368 -> 508 -> 1114 ms across 50 -> 150 -> 300 -> 600 conns, while
throughput kept climbing (80 -> 183 submits/s). The B1 keeps absorbing more work;
each unit just takes longer. The soft inflection is around **300-600 conns**, where
p99 crosses ~0.5 s then ~1.1 s - far above friends-and-family scale, and it never
broke completion. Because CPU never exceeded 42% and the HTTP queue never built, the
tail is **single-core scheduling + per-connection serialization
(`MaximumParallelInvocationsPerClient = 1`) + real network RTT (~20-40 ms/call)**,
not resource exhaustion.

**Watch for run-to-run variance.** The first 300-conn run showed p50 327 ms /
85 submits/s at only ~30% CPU; an immediate re-run showed p50 93 ms / 133 submits/s.
The dip was thread-pool ramp / GC warmup, not a ceiling (CPU was not saturated). A
single UAT run can mislead - re-run before trusting a number. A follow-up pushed far
past 600 conns - see "Ceiling probe" next.

### Ceiling probe (follow-up): how far does one B1 actually go?

The ramp above stopped at 600 conns. A follow-up drove **sustained** load (5-6
rounds/room, so each run spans a full minute and the 1-min CPU metric reads true
steady state) up to 3,000 connections, escalating until something broke. The short
answer: **connection *count* was never the wall - the single load-client machine
became the bottleneck first, around 2,400-3,000 conns.**

| Run | Conns | Ramp | Connects OK | SubmitWord p50 / p99 | Submits/s | Server CPU | Mem % | Verdict |
|---|---:|---:|---|---|---:|---|---:|---|
| A | 1,200 | 60 | 1200 / 1200 | 499 ms / 1.8 s | 267 | ~34%* | 80 | clean |
| B | 1,800 | 80 | 1800 / 1800 | 649 ms / 1.9 s | 327 | 49% | 85 | clean |
| C | 2,400 | 100 | 2400 / 2400 | 825 ms / 2.2 s | 380 | 40% steady / **98% connect-storm** | 86 | clean |
| D | 3,000 | 400 | **2711 / 3000** (289 failed) | 2.0 s / 14 s | 158 | not pegged, queue 0 | 87 | broke - see below |
| E | 3,000 | 100 | 3000 / 3000 | 773 ms / 4.3 s | 224 | ~15% (server idle-ish) | 88 | clean connects |

<sub>* diluted by short-run averaging; duty-cycle-corrected ~60%.</sub>

- **Connection count is not the wall.** Run E held **3,000 concurrent connections at
  100% completion**, server at 15-49% CPU, HTTP queue 0, 5xx 0.
- **The load client became the limiter around 2,400-3,000 conns.** Server CPU *fell*
  as connections rose (49% at 1,800 -> 40% at 2,400 -> 15% at 3,000): past ~2,400 the
  single client machine could not drive the connections hard enough, so the server
  loafed while client-measured latency inflated. Run D's 289 connect failures were a
  **400-concurrent-handshake storm choking the client** (`Connect /
  OperationCanceledException`), not the server - server CPU was unpegged and the HTTP
  queue was 0. A true server failure ceiling needs a **distributed client** (multiple
  machines / IPs), which the single-machine constraint here rules out.
- **What the server itself is bound by, in order:**
  - **Connect / disconnect storms** are the only thing that pegged the CPU: bringing
    2,400 conns up (ramp 100) hit **98%**; tearing ~2,700 conns down at once
    (end-of-run) spiked to **99%**. Steady play never approached that. This is the
    real operational risk - a restart / deploy drops every in-memory room, and the
    whole crowd reconnecting at once is a thundering herd.
  - **Memory** is the one server resource that climbed monotonically: 78% idle ->
    **88% at 3,000 conns** (working set 148 -> 275 MB, ~42 KB/conn). It is the likely
    first *sustained* wall, extrapolating to ~4,000-5,000 conns.
  - **Steady CPU** is a non-issue in range (<= 50%).

**The ceiling, stated honestly:**

- **Real (mostly-idle) players:** comfortably thousands - the server held 3,000
  aggressive bot connections at <= 49% CPU, and humans generate a fraction of bot load.
- **Sustained hard ceiling:** **>= 3,000 conns** (unbroken on count from one machine),
  memory-bound, est. ~4,000-5,000.
- **Connection-burst ceiling:** ~2,400 simultaneous (re)connects push CPU to ~98%;
  sharper herds jam beyond that. **This is the number that matters operationally**
  (the post-restart reconnect herd).
- **Usable / snappy ceiling:** far lower - p50 crosses ~0.5 s around 1,200 aggressive
  conns and is ~0.8 s at 3,000. For idle real players, snappy well into the thousands.

### vs the LOCAL baseline

Same **shape** (100% completion, 0 errors, a gradual latency knee, no failures),
higher **absolute latency**, exactly as the LOCAL section predicted. At matched
concurrency UAT p99 is roughly 5-10x LOCAL: at 300 conns 508 ms (UAT, warm) vs 33 ms
(local); at 600 conns 1114 ms vs 101 ms. The gap is the B1's single small core plus
~20-40 ms network RTT per call, vs loopback WebSocket on a larger box. LOCAL was the
optimistic upper bound it claimed to be; the UAT numbers are the ones to quote for
real play - and they are still comfortable for the alpha.

### F2 "1 failed join per room" and RoundAborted on UAT

- **F2 did not appear: 0 failed joins at every rung** (up to 100 rooms x 6). As LOCAL
  found, the 1-per-room JoinRoom race needs a concurrent join *storm into a single
  room* (25/40/60 players); at the intended 6 players/room it never triggers, and the
  harness only packs 6/room here. Large-room behaviour was **not** re-probed on UAT -
  the 6-player cap (roadmap W2) bounds it to nothing for real play, so it was not
  worth spending B1 time on the pathological shape.
- **RoundAborted was 0 in every run,** including 25% churn and the AI run. No round
  was aborted by a mid-round leave or by a slow / failed AI call.

### Reconnect + AI gate on UAT

- **F4 (reconnect / grace) confirmed on UAT.** 25% mid-round churn over 20 rooms gave
  40/40 successful token Rejoins, 0 RoundAborted, 100% completion, and 0 unexpected
  auto-reconnects (the deliberate drops were caught by the grace + Rejoin path, not
  SignalR's fallback auto-reconnect). Rejoin p50 43 ms / p99 142 ms.
- **F5 (AI cost gate) confirmed with REAL cost, and upgraded.** Of 20 real
  `POST /api/ai/jumble` calls: **15 were genuine gpt-5-mini generations** (so real AI
  generation works on UAT - a prior `max_completion_tokens` fallback bug is
  resolved), 4 degraded cleanly to the free reshuffle, 1 hit a client-side
  `TaskCanceledException` (timeout), and 0 were rate-limited (429). All 10 rounds
  still completed - **the AI path is isolated from game flow and fails safe.** The
  driver is AI latency: gpt-5-mini jumble generation is **slow (p50 10.4 s, max
  28.8 s)**, which is what produced the fallbacks / cancellation, and which inflated
  co-located hub-method tails (SubmitWord p95 1.6 s during the AI run vs 82 ms at the
  same 50-conn level without AI - the AI proxy shares the one B1 core and process
  with the hub). Cost was ~15 paid calls = a fraction of a cent, far under the
  $20/month breaker. **Operational flag:** a 10-29 s AI wait is a poor live UX; the
  gate's timeout -> fallback tuning is what protects players, and it worked here.

### Bottom line for the alpha

UAT B1 is comfortably sufficient for the friends-and-family test and well beyond.
What this run surfaces, in priority order: (1) nothing blocks the F2F test on
capacity grounds - proceed; (2) if the AI delight tier moves front and centre, the
gpt-5-mini latency (10-29 s) and its contention with the hub on one core matter far
more than raw connection load - consider a snappier model / deployment, response
streaming, or moving AI work off the hub's core; (3) before any public-scale launch,
wire a paid Azure SignalR tier plus `.AddAzureSignalR(...)` and move room state
off-process (F1) - the Free_F1 resource present today is a placeholder, not a
backplane.

The single near-term risk worth a cheap fix now: a restart / deploy drops every
in-memory room and the whole crowd reconnects at once (the storm that pegs CPU to
~98-99%). The web client reconnects on fixed delays (`useGameHub.ts`:
`withAutomaticReconnect` ~0/2/10/30 s + a manual 2/5/10/30 s loop) with **no
jitter**, so every client retries in lockstep. Adding randomized jitter to those
delays spreads the herd over time and is the highest-value, lowest-cost mitigation
on the current single B1 - see the recommendations discussion.
