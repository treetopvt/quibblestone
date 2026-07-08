<!--
  Story: control-plane/03. Depends on control-plane/01. See feature.md and
  docs/adr/0003-admin-platform-and-family-accounts.md, Layer 1.
  Use hyphens/colons/parentheses, never em dashes.
-->

# Story: Knob migration

**Feature:** Control Plane (`docs/features/control-plane/feature.md`)  ·  **Status:** Not Started  ·  **Issue:** #TBD

## Context
ADR 0003's audit named seven hardcoded operational constants, each already documented in its own file
with the rationale behind its current value, as exactly the piecemeal mechanism this feature replaces.
This story migrates each onto a settings key (`control-plane/01`), keeping its current value as the
code default so behavior is bit-for-bit identical until an operator overrides it. This is the widest
file-footprint story in the feature - see `feature.md`'s Design notes and the cross-feature hazard
below for why it must run alone in its wave slot.

The seven knobs and their current homes:

| Knob | Current constant | File | New settings key | Type | Code default |
|---|---|---|---|---|---|
| Report auto-hide threshold | `AutoHideThreshold` | `api/src/PublishedTales/PublishedTalesController.cs` (~line 100) | `moderation.tale.autoHideThreshold` | Int | 3 |
| AI per-IP rate limit | `aiPerIpPermitPerWindow` | `api/src/Program.cs` (~line 301) | `ai.rateLimit.perIpPermitPerMinute` | Int | 30 |
| AI per-session quota | `AiOptions.QuotaPerSession` | `api/src/Ai/AiOptions.cs` | `ai.quota.perSession` | Int | 20 |
| AI monthly spend ceiling | `AiOptions.MonthlyCeilingUsd` | `api/src/Ai/AiOptions.cs` | `ai.spend.monthlyCeilingUsd` | Decimal | 20 |
| Seat grace window | `SeatGraceService.DefaultGraceWindow` | `api/src/Rooms/SeatGraceService.cs` | `session.seatGraceWindowSeconds` | Int | 180 |
| Public tale TTL | `PublishedTalesController.TaleTtl` (~line 92) | `api/src/PublishedTales/PublishedTalesController.cs` | `tales.ttlDays` | Int | 30 |
| Operator login rate limit | `OperatorLoginRateLimit.PermitLimit` | `api/src/Admin/OperatorLoginRateLimit.cs` | `admin.operatorLogin.rateLimitPermitPerMinute` | Int | 5 |

An eighth candidate - the keepsake vault's TTL and claim-restore window - is CONDITIONAL: as of this
writing `docs/features/keepsake-vault/` does not exist (ADR 0003 Layer 2 has not been decomposed yet).
This story does NOT invent placeholder settings keys for a store that does not exist; when
`keepsake-vault/01` lands its vault TTL (and, if built by then, a restore window), that feature migrates
them onto this same pattern in its own story, noted in its `feature.md`.

## Acceptance Criteria
- [ ] AC-01: Given each of the seven knobs above is migrated to a settings key carrying its current
      hardcoded value as the code default, when no operator override exists for any of them, then
      observed behavior (auto-hide decisions, the AI per-IP limit, the AI per-session quota, the AI
      monthly ceiling, the seat grace window, published-tale TTL, the operator-login rate limit) is
      IDENTICAL to before this story, verified per knob - zero regression.
- [ ] AC-02: Given an operator overrides the report auto-hide threshold, when a published tale
      accumulates reports after the cache window (story 01) elapses, then the NEW threshold governs the
      auto-hide decision from that point forward - no redeploy.
- [ ] AC-03: Given an operator overrides the seat grace window, when a NEW disconnect is handled after
      the change, then `SeatGraceService` schedules the NEW window for it; a grace timer already
      scheduled for a disconnect that started BEFORE the change keeps running with its ORIGINAL window
      (no retroactive change to an in-flight timer).
- [ ] AC-04: Given an operator overrides the public tale TTL, when a NEW tale is published after the
      change, then it is stamped with the NEW TTL; a tale published BEFORE the change keeps the TTL it
      was originally stamped with at publish time (no retroactive extension or shortening of an
      already-published tale's expiry).
- [ ] AC-05: Given an operator overrides the AI per-session quota or the AI monthly spend ceiling, when
      a new quota check or spend evaluation happens after the cache window elapses, then the NEW value
      governs - without an app redeploy, and without a behavior change to any AI call already in flight.
- [ ] AC-06: Given the rate-limiter-backed knobs (AI per-IP, operator-login), when a NEW rate-limit
      partition is created after the cache window elapses, then it is built with the current settings
      value; an already-live partition (an IP mid-window) may finish its current window under the OLD
      value - a documented, acceptable lag, not a bug, since ASP.NET Core's rate limiter bakes a
      partition's options in at the point the partition is created.
- [ ] AC-07: Given no storage is configured (the in-memory settings fallback, story 01 AC-05), when any
      of these seven knobs is read, then it behaves identically to the Table-Storage-backed path (code
      default, or an in-process override) - local dev / CI need zero setup.

## Out of Scope
- The keepsake vault's TTL / restore window (conditional, deferred - see Context).
- Any NEW moderation, AI-gating, or session behavior beyond what already exists today. This is a
  value-SOURCE migration only - no knob's semantics, only where its current value lives, changes.
- A console UI for editing any of these seven values (`sysadmin-console`'s Operations tab).
- Changing `AiQuota`, `AiSpendBreaker`, or `SeatGraceService`'s PUBLIC constructor shape in a way that
  breaks their existing test-only overloads (`SeatGraceService`'s explicit-`TimeSpan` test constructor,
  for instance) - the DI-facing constructor may gain an `IRuntimeSettingsService` dependency, but the
  test-facing override constructor stays available for a spec that wants a deterministic tiny window.
- RBAC / scoped operator authorization (parked in `feature.md`).

## Technical Notes
Each of the seven files changes from "read a `const`/static-readonly value" to "read the current
effective value from `IRuntimeSettingsService` (story 01) at the point of use," with the SAME literal
kept as the settings key's code default (so a fresh clone with no override behaves identically).

**The one real gotcha, twice over: values captured once at construction vs. read live.** Several of
these constants are currently captured into a field ONCE, at DI-container construction time (a
process-wide singleton), not re-read per check:
- `AiQuota` (`api/src/Ai/AiQuota.cs`) captures `_perSessionLimit` once from `AiOptions.QuotaPerSession`
  in its constructor.
- `AiSpendBreaker` (`api/src/Ai/AiSpendBreaker.cs`) holds `AiOptions` and reads
  `_options.MonthlyCeilingUsd` on every check, but `AiOptions` itself is bound once at startup - the
  same "baked in" effect.
- `SeatGraceService` (`api/src/Rooms/SeatGraceService.cs`) is a process-wide singleton constructed with
  `DefaultGraceWindow` (180s) as a static readonly value.

For each of these, the migration is: keep injecting `AiOptions`/the static default as the CODE DEFAULT
source for the settings catalog entry, but change the READ site (the actual quota check, the actual
spend comparison, the actual `ScheduleEviction` call) to ask `IRuntimeSettingsService` for the CURRENT
effective value at the moment it is needed, rather than trusting a field captured at construction. The
settings service's short cache (story 01) keeps this cheap - it is not a storage round-trip per check.

- `ASP.NET Core`'s `RateLimiter` policies (`Program.cs`, `AiPerIpRateLimitPolicy` and
  `OperatorLoginRateLimit.PolicyName`) build a `FixedWindowRateLimiterOptions` via a factory lambda that
  runs when a NEW partition (a new IP bucket) is created - not on every request against an existing
  partition. Read the current settings value INSIDE that factory lambda (resolving
  `IRuntimeSettingsService` from the request's `HttpContext.RequestServices`, not a value closed over at
  `AddRateLimiter` registration time) so a newly-created partition picks up the latest override; an
  already-open partition keeps its own options until its window resets (AC-06's documented lag).
- `PublishedTalesController.AutoHideThreshold` and `TaleTtl`: read via `IRuntimeSettingsService` at the
  point each is used (the report-count comparison; the `ExpiresUtc = now + ttl` stamp on publish) rather
  than as `public const`/`static readonly` fields. `ReportedTalesController.cs` (which likely also
  references `AutoHideThreshold` for its own auto-hide check) needs the same read-site change if it
  currently reads the constant directly - confirm at build time.
- `OperatorLoginRateLimit.PermitLimit`: becomes the settings key's code default; the live value is read
  the same way as the AI per-IP policy above.

**Cross-feature hazard (ADR 0003), the load-bearing one for this story:** this is explicitly called out
in ADR 0003's cross-feature build order as the story that "touches many files across the API" and "must
be scheduled ALONE in its wave slot" - not just within `control-plane`, but across every feature in
flight under ADR 0003 (and, practically, any other feature touching `Program.cs`,
`api/src/Rooms/SeatGraceService.cs`, `api/src/PublishedTales/`, `api/src/Ai/`, or `api/src/Admin/` at
the same time). Schedule it when the tree is otherwise quiet in those areas.

## Tests
| AC | Test |
|---|---|
| AC-01 | `tests/QuibbleStone.Api.Tests/Settings/KnobMigrationRegressionTests.cs` - one assertion per knob at its code default matches today's shipped constant |
| AC-02 | `tests/QuibbleStone.Api.Tests/PublishedTales/PublishedTalesControllerTests.cs` (or `ReportedTalesControllerTests.cs`) - overridden threshold changes the auto-hide decision |
| AC-03 | `tests/QuibbleStone.Api.Tests/Rooms/SeatGraceServiceTests.cs` - in-flight timer unaffected by a mid-flight override; a new disconnect after the override uses the new window |
| AC-04 | `PublishedTalesControllerTests.cs` - a tale published before a TTL override keeps its original expiry; one published after gets the new TTL |
| AC-05 | `tests/QuibbleStone.Api.Tests/Ai/AiQuotaTests.cs`, `AiSpendBreakerTests.cs` - overridden quota/ceiling governs a check made after the override |
| AC-06 | manual: exercise the AI per-IP and operator-login rate limits before/after an override, noting the documented partition-creation-time lag |
| AC-07 | same test files, constructed over `InMemoryRuntimeSettingsStore` - identical assertions with no storage configured |

## Dependencies
`control-plane/01` (the runtime settings service every read site in this story calls through). Does
not depend on `control-plane/02`.
