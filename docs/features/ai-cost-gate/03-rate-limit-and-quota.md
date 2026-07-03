<!--
  Story 03 of the AI cost gate - per-session/per-IP rate-limit + quota, with an "N calls left" meter. No em dashes.
-->

# Story: Rate-limit + quota metering (per-session / per-IP + "N calls left")

**Feature:** AI Cost Gate  ·  **Status:** Complete  ·  **Issue:** #122

## Context
The gate's third piece (feature.md; ROADMAP "The AI cost gate" piece 3): even an
allowed session must not be able to spam AI. This story adds per-session and per-IP
rate-limits plus a per-session quota with an "N calls left" meter the client can
show. It is DISTINCT from the entitlement gate (story 02): entitlement answers
"unlocked / not" once at session-creation; metering answers "how many are left"
during play (the split `ai-on-demand-generation`'s feature.md and `game-modes/07`
AC-08 both call out). In alpha, where the jumble is free for everyone (ADR 0001 C),
this metering plus the circuit-breaker (story 04) is the ACTUAL gate on the jumble.
See [feature.md](./feature.md).

## Acceptance Criteria
- [x] AC-01: Given a session making AI calls, then a per-session quota limits how
      many AI calls it may make (a sensible alpha default, e.g. N per session and/or
      a refill window), enforced server-side before the proxy (story 01) is called -
      a client cannot exceed it by replaying requests.
- [x] AC-02 (meter): Given the quota, then the remaining count ("N Fresh Runes
      left") is returned to the client so it can show the meter and soft-disable the
      action at zero - the number is server-authoritative (the client displays it,
      never decides it).
- [x] AC-03 (per-IP abuse guard): Given many sessions from one origin, then a per-IP
      rate-limit caps the aggregate AI call rate (a coarse abuse guard on top of the
      per-session quota), so spinning up sessions cannot multiply spend without
      bound. The IP is used transiently for rate-limiting only and is never stored as
      identity or attached to telemetry (README section 6; the PII scrubber already
      zeroes client IP).
- [x] AC-04 (anonymous keys): Given metering, then it keys off the anonymous session
      (the room/solo `InstanceId`) and a transient IP bucket ONLY - never a nickname,
      join code, account, or any PII. Clearing the quota state loses nothing about a
      person (there is no person recorded).
- [x] AC-05 (degrade, do not error): Given a session that hits its quota or the
      per-IP limit, then the AI call is refused gracefully and the caller falls back
      to the deterministic path (the free reshuffle for the jumble) - the player sees
      "no fresh AI runes right now", never an error or a broken round.
- [x] AC-06 (distinct from entitlement, distinct from the breaker): Given the three
      controls, then metering (this story) is separate code from the entitlement
      check (story 02) and from the monthly spend circuit-breaker (story 04): quota
      is per-session call-count; the breaker is global monthly dollars. Both must
      pass for an AI call to proceed; neither is a substitute for the other.
- [x] AC-07 (fail-safe default): Given the metering store is unavailable, then the
      gate defaults to the SAFE side - it does not fail open into unbounded calls;
      if it cannot confirm remaining quota, it degrades to the deterministic fallback
      rather than calling AI freely.

## Out of Scope
- The entitlement check (story 02) and the monthly spend breaker + attribution
  (story 04) - separate pieces this coordinates with.
- The jumble UX for the meter (the "N Fresh Runes left" chip rendering) -
  `game-modes/07` owns the surface; this story provides the server-authoritative
  number and the API/hub field that carries it.
- A durable cross-restart quota that survives process recycles with perfect
  accuracy - alpha metering can live in memory or a light Table Storage counter;
  exactness across a redeploy is not required (it is a toy, CLAUDE.md section 10).
  The monthly SPEND total (story 04) is the one that must persist.
- Per-feature quotas (different limits for jumble vs future voice/on-demand) - one
  shared per-session quota now; the attribution data (story 04) informs
  per-feature limits later (feature.md Parked).

## Technical Notes
- **Where:** a new metering component in `api/src/Ai/` (e.g. `IAiQuota` /
  `AiQuota`), checked in the proxy call path BEFORE `IAiCompletionClient.CompleteAsync`.
  Keep it a small, testable service registered as a singleton (mirrors
  `RoomRegistry`'s process-wide in-memory posture).
- **Session key:** reuse the room/solo `InstanceId` (already the anonymous grouping
  key for the serve log) so quota, attribution (story 04), and the serve log all
  speak the same anonymous id.
- **Per-IP:** ASP.NET Core has built-in rate limiting (`AddRateLimiter` / partitioned
  limiters) - use it for the coarse per-IP guard rather than hand-rolling; partition
  by the remote IP for the AI endpoint/hub method only. Do not persist the IP.
- **Meter to client:** thread the remaining count back on the AI call's result
  envelope (the same DTO the jumble returns), and/or expose it on the room state so
  `game-modes/07` can render it. Follow the hand-mirrored DTO convention (no codegen;
  the hub signature is the contract).
- Fail-safe (AC-07): treat "cannot read quota" as "no quota available" -> fallback,
  never -> unlimited. Async; nullable respected; no PII to telemetry.

## Tests
| AC | Test |
|---|---|
| AC-01 | `tests/QuibbleStone.Api.Tests/Ai/AiQuotaTests.cs`: a session exceeding N calls is refused before the proxy is invoked |
| AC-02 | `api/tests` + manual: the remaining count decrements and reaches zero server-side; the client shows it |
| AC-03 | `api/tests`/manual: many sessions from one IP hit the per-IP cap; single-session play is unaffected |
| AC-04 | code review: quota/rate keys are the anonymous InstanceId + transient IP only; no PII stored or logged |
| AC-05 | manual: at quota, the jumble falls back to the deterministic reshuffle with a friendly message, no error |
| AC-06 | code review: quota, entitlement (02), and breaker (04) are distinct; an AI call requires quota AND breaker to pass |
| AC-07 | `api/tests`: with the quota store unavailable, the gate degrades to fallback, not unlimited calls |

## Dependencies
- cost-gate/01 (the proxy this guards - metering runs before it).
- cost-gate/04 (the spend breaker it coordinates with; both must pass).
- `game-modes/07` (the consumer that renders the "N Fresh Runes left" meter).
