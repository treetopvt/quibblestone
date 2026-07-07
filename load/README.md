# QuibbleStone SignalR load harness

A **standalone** developer tool that drives the real QuibbleStone `GameHub` with
many concurrent simulated players across many rooms, to expose real-time /
architecture holes and measure per-hub-method latency under load. It plays the
full round flow (create room, join, start round, collect words round-robin,
reveal) the same way the web client does, over the same `/hubs/game` SignalR
endpoint.

It is deliberately **not** part of the app:

- **Not** in `QuibbleStone.slnx`, **not** referenced by the API, **not** in CI.
  `dotnet build QuibbleStone.slnx`, `dotnet test`, and the CI/Deploy workflows are
  all unaffected. Run it by hand.
- Build/run it by its own path: `load/QuibbleStone.LoadTest.csproj` (net10.0).
- One dependency: `Microsoft.AspNetCore.SignalR.Client` (the standard first-party
  hub client). Everything else is plain `System.*`.

---

## Primary target: UAT

The intended target is the **deployed UAT environment** (local dev is fine for a
rehearsal but temperamental). UAT is where the shipped telemetry (App Insights),
the real Azure SignalR wiring, and - when you use `--ai` - the real AI cost gate
all exist, so it is the environment whose limits you actually want to find.

### Finding the UAT hub URL

The UAT App Service name carries a `uniqueString()` suffix minted by Bicep
(`infra/main.bicep`: `quibblestone-uat-api-<suffix>`), so it is **not** hard-coded
anywhere in the repo - the deploy workflow discovers it at run time. Discover the
hostname yourself with the Azure CLI (same query the deploy workflow uses):

```bash
az webapp list -g quibblestone-uat-rg \
  --query "[?tags.app=='quibblestone'].defaultHostName" -o tsv
# -> quibblestone-uat-api-<suffix>.azurewebsites.net
```

The hub URL is then `https://<that-host>/hubs/game`. If you cannot run `az` (no
Azure access), get the API hostname from the Azure portal (resource group
`quibblestone-uat-rg`, the App Service tagged `app=quibblestone`) and set it as
`--hub-url https://<uat-api-host>/hubs/game`.

> Note: this document does not hard-code the UAT URL because the `uniqueString`
> suffix must be discovered per environment. Substitute `<uat-api-host>` below.

### UAT caution (read before running)

- **UAT is a single small B1 App Service instance** (1 core, ~1.75 GB), and the
  hub runs **in-process** (one node, in-memory room registry). It will saturate
  far sooner than a scaled-out deployment. **Ramp gradually** (start with
  `--ramp 10` and a handful of rooms; increase across runs) and watch CPU/memory
  before pushing harder.
- **Do not run during a live friends-and-family session.** A load run will
  compete for the same single instance and can degrade real players' rooms.
- Prefer running from a single machine so the per-IP AI rate limiter (below)
  behaves predictably.

---

## Prerequisites

### For a local rehearsal (safe default)

Start the API first, on `:5180`, in a separate terminal:

```bash
dotnet run --project api/QuibbleStone.Api.csproj      # http://localhost:5180
```

With no configuration (a fresh clone), the API's storage / AI / Stripe / email
seams all no-op, so `Ai:Endpoint` is unset and **AI is inert locally** (any
`--ai` call returns a fell-back result and costs nothing - see AI notes).

### For UAT

Nothing to start - UAT is already deployed. Just point `--hub-url` at it.

---

## Running it

```bash
# Local rehearsal (API must be running on :5180) - no AI, modest load
dotnet run --project load/QuibbleStone.LoadTest.csproj -- \
  --rooms 10 --players-per-room 6 --rounds 1

# UAT, gentle first pass (no AI)
dotnet run --project load/QuibbleStone.LoadTest.csproj -- \
  --hub-url https://<uat-api-host>/hubs/game \
  --rooms 10 --players-per-room 5 --rounds 2 --ramp 10

# UAT, find the connection knee (ramp up gradually across runs)
dotnet run --project load/QuibbleStone.LoadTest.csproj -- \
  --hub-url https://<uat-api-host>/hubs/game \
  --rooms 50 --players-per-room 6 --rounds 3 --ramp 25

# UAT, with churn (mid-round drop + Rejoin, exercises the grace/reconnect path)
dotnet run --project load/QuibbleStone.LoadTest.csproj -- \
  --hub-url https://<uat-api-host>/hubs/game \
  --rooms 20 --players-per-room 6 --rounds 2 --ramp 20 --churn 0.25

# UAT, with the opt-in AI scenario (small REAL cost - see AI notes)
dotnet run --project load/QuibbleStone.LoadTest.csproj -- \
  --hub-url https://<uat-api-host>/hubs/game \
  --rooms 10 --players-per-room 5 --ramp 15 --ai --ai-calls-per-room 2
```

### Options

| Flag | Env var | Default | Meaning |
|---|---|---|---|
| `--rooms N` | `QS_LOAD_ROOMS` | 10 | Rooms to simulate concurrently. |
| `--players-per-room N` | `QS_LOAD_PLAYERS_PER_ROOM` | 6 | Players incl. host (**min 2** - `StartRound` needs 2+). |
| `--rounds N` | `QS_LOAD_ROUNDS` | 1 | Full rounds per room (replayed via `StartRound`). |
| `--hub-url URL` | `QS_LOAD_HUB_URL` | `http://localhost:5180/hubs/game` | The hub endpoint. Set to the UAT hub for a UAT run. |
| `--ramp N` | `QS_LOAD_RAMP` | 50 | Max clients **connecting** at once (find the knee). |
| `--churn F` | `QS_LOAD_CHURN` | 0 | Fraction `0..1` of joiners to drop + Rejoin mid-round. |
| `--ai` | `QS_LOAD_AI=true` | off | Opt-in: fire real `POST /api/ai/jumble` calls per room. |
| `--ai-calls-per-room N` | `QS_LOAD_AI_CALLS_PER_ROOM` | 2 | AI calls per room when `--ai` is on. |
| `--help`, `-h` | | | Print usage. |

CLI wins over env var wins over default. Defaults are modest so a first run is
safe. The round is always `classic-blind`, family-safe, story-length `full` (so
every player gets at least one blank - a heavier real-time exercise); change those
constants in `Config.cs` to probe other shapes.

---

## Reading the summary

- **connections** - attempted / succeeded / failed opening a hub connection. A
  rising `failed` count as you raise `--ramp` / `--rooms` is the connection knee.
- **rooms + rounds**
  - `rooms created` / `create-failed` - `CreateRoom` envelope outcomes.
  - `rooms understaffed` - fewer than 2 players seated, so the round never started
    (usually a symptom of join failures under load).
  - `joins ok / failed` - `JoinRoom` envelope outcomes.
  - `rounds started` vs `rounds completed` (**reached RevealReady**) - the round
    **completion rate**. Incomplete rounds (timed out before reveal, or aborted by
    a leave) are the headline real-time health signal.
  - `submits ok / rej / att` - `SubmitWord` outcomes (rejected = a friendly Ok=false,
    e.g. a blocked word; with benign words this should be 0).
- **broadcasts observed** - server -> client fan-out actually arriving at clients
  (`RoundStarted`, `CollectProgress`, `RevealReady`, `RoundAborted`, `RosterChanged`).
  Confirms the hub is pushing, and how much - `CollectProgress` grows with every
  submission times the room size (the fan-out cost).
- **disconnects / reconnects** - `auto reconnecting`/`reconnected` (SignalR's
  automatic reconnect firing on **unexpected** drops - a red flag if non-zero
  without churn) vs `churn drops`/`rejoins` (the deliberate `--churn` drop + Rejoin).
- **throughput** - completed rounds/sec and successful submits/sec over wall clock.
- **per-method latency (ms)** - count, p50, p95, p99, max, mean for each hub method
  (`CreateRoom`, `JoinRoom`, `StartRound`, `SubmitWord`, plus `Rejoin`/`LeaveRoom`),
  measured with a stopwatch around each `InvokeAsync`. **Successful invokes only**,
  so the percentiles describe healthy latency; failures are in the error buckets.
- **AI jumble** (only when `--ai`) - see below.
- **errors** - bucketed by `method / ExceptionType` (e.g. transport faults, timeouts).

---

## What to watch server-side

Run a load pass while watching the target's telemetry - the harness output is only
half the picture.

- **App Insights** (UAT has it wired; local no-ops it):
  - **Failures / exceptions** - hub method exceptions surface via
    `HubTelemetryFilter` (anonymous method name only). A spike here is the thing to
    correlate with the harness's error buckets and incomplete rounds.
  - **Custom events** - `HubAbnormalDisconnect` / `HubGraceStarted` (abnormal drops
    and grace-window opens) climb under connection pressure and during `--churn`.
  - **Performance** - server request duration + rate, and outbound dependency calls.
- **App Service** (Azure portal -> the API App Service -> Metrics):
  - **CPU %** and **Memory working set** - on **B1** these are the first to peg.
  - **Response time**, **Requests**, and **HTTP queue length** - a growing queue
    length is the in-process hub back-pressuring.
- **AI cost gate** (only relevant with `--ai`, and only on UAT where AI is
  configured): App Insights AI attribution events + the `$20`/month budget alert.

---

## AI cost notes (the `--ai` scenario)

**AI is OFF by default. With `--ai` off, the harness makes zero AI calls and
incurs zero AI cost** - it never touches any AI endpoint.

- **Local is inert**: the API only makes a real AI call when `Ai:Endpoint` is
  configured. It is unset locally (the default), so a local `--ai` run returns
  `fellBack=true` for every call and costs nothing - useful to rehearse the path.
- **UAT costs real (tiny) money**: on UAT `Ai:Endpoint` **is** set, so `--ai`
  calls hit the real `gpt-5-mini`. Each jumble is a few hundred tokens
  (`$0.25`/1M input, `$2.00`/1M output) - a **fraction of a cent** per call. A
  default `--ai` run (`--ai-calls-per-room 2` over 10 rooms = ~20 calls) is
  negligible. Keep the volume modest.
- **Cost is bounded server-side by the gate** (the charter's load-bearing rule):
  - **per-session quota**: 20 AI calls per anonymous room session (`Ai:QuotaPerSession`).
  - **per-IP rate limit**: 30 requests/minute (`AiPerIpRateLimitPolicy`).
  - **spend circuit-breaker**: opens at 100% of the `$20`/month ceiling
    (`Ai:MonthlyCeilingUsd`).
- **A `429` or a fell-back (`200`, `fellBack=true`) response under load is an
  EXPECTED, HEALTHY result** - it means the gate (per-IP limiter / quota / breaker)
  is doing its job. The harness records these as outcomes, **not** as errors. Only
  a transport exception is bucketed as an error (under `AiJumble`).

The AI summary block reports: `attempted`, `ai-generated` (a real paid call),
`fell back` (gate degraded -> free reshuffle), `rate limited (429)`, and
`other http errors`.

---

## Known limits this probes

The alpha runs a **single in-process hub instance** with an **in-memory room
registry** (`CLAUDE.md` section 10 - a toy, not a system of record). This harness
is aimed squarely at the holes that implies:

- **Single in-process instance** - no scale-out; all rooms + fan-out live on one
  node. On UAT that node is a small **B1**. CPU/memory/queue-length are the ceiling.
- **In-memory room registry** - room state is process-local and ephemeral; a
  restart drops every room. Rooms accumulate in memory during a run (the harness
  leaves each room via `LeaveRoom` at the end to release them; they also idle-sweep).
- **No per-room player cap** - you can pack rooms arbitrarily with
  `--players-per-room`; watch how per-submission fan-out (`CollectProgress` to the
  whole room) scales with room size.
- **Serialized per-client invokes** - the hub uses the default
  `MaximumParallelInvocationsPerClient = 1`, so a single connection's calls
  serialize server-side. Cross-connection work is parallel; per-connection is not.
- **Grace / reconnect under load** - `--churn` exercises the disconnect grace
  window + token `Rejoin` path (session-engine/07-08); watch `HubGraceStarted` and
  the `Rejoin` latency / failure counts.

---

## Build

```bash
dotnet build load/QuibbleStone.LoadTest.csproj
```

Standalone - it does not touch the solution build or CI.
