# Story: Anonymous product-usage metrics (game types, session length, approximate reach)

**Feature:** Platform & DevOps  ·  **Status:** In Progress  <!-- Not Started | In Progress | Complete | Blocked | Dropped -->  ·  **Issue:** #107

## Context
Beyond "is it broken" (`platform-devops/04`), the product question is "how is the toy
actually used" - which game modes get played, how long a session lasts, and roughly
how many distinct devices play - so the roadmap is steered by real behavior, not
guesses. README section 12 parks "analytics" as demand-driven; this is that demand,
scoped hard to stay ANONYMOUS BY CONSTRUCTION (README section 6): players have no
accounts and no PII (README section 3), so "who is playing" can only ever be an
approximate, anonymous device/session count, never a real person. It reuses the
operational Application Insights pipeline from `platform-devops/04` (custom events) and
coordinates with `story-selection/04`'s anonymous serve log so one telemetry
philosophy holds, not three. See [feature.md](./feature.md).

## Acceptance Criteria
- [ ] AC-01: Given a round is played, then an anonymous usage EVENT is recorded with the
      MODE (Classic blind, Word Bank, Progressive Story, Progressive Reveal) and the
      context (solo vs group) - emitted as an Application Insights custom event via
      `platform-devops/04`'s pipeline, fire-and-forget, never blocking gameplay. This
      answers "what types of games they play".
- [ ] AC-02 (session length): Given play, then session/round DURATION is captured
      anonymously (e.g. round start-to-finish, and/or app-open to app-close) so "how
      long they play" is answerable - with no per-person identity attached.
- [ ] AC-03 (approximate reach): Given there are NO accounts (README section 3), then
      "how many are playing" is answered ONLY by an anonymous device/session id (a random
      id in `localStorage`, no PII, reset by clearing storage) or App Insights' built-in
      anonymous session/user telemetry - and it is documented as APPROXIMATE (a device
      count, never a verified unique person). True unique-user identity is explicitly
      deferred to the Phase-2 account seam (accounts-identity).
- [ ] AC-04 (child-safety / PII, non-negotiable): Given every usage event, then it is
      anonymous by construction - no nickname, join code, player session id,
      IP-derived identity, submitted word, or story text - only mode, solo/group,
      counts, durations, length class, and the anonymous device/session id (README
      section 6). The family-safe posture is unaffected (this adds no content surface).
- [ ] AC-05 (entitlement): Given these are INTERNAL metrics, then there is no
      entitlement gate and no player-facing UI - free/base and paid sessions are
      measured identically and anonymously. Usage telemetry is not a feature a player
      turns on or pays for; it consumes no billing-entitlements capability key.
- [ ] AC-06 (one telemetry philosophy, no third system): Given the existing telemetry
      surfaces, then this story REUSES `platform-devops/04`'s App Insights pipeline for
      usage custom events and COORDINATES with `story-selection/04`'s anonymous serve
      log - it does not stand up a third parallel telemetry stack. Operational health
      (04), content-curation serve counts (story-selection/04-05), and product usage
      (this) stay coherent, each with a clear purpose and no duplicated plumbing.
- [ ] AC-07: Given the events exist, then the headline questions are answerable with a
      straightforward App Insights query - modes played over time, median session
      length, and an approximate active-device count - with NO dashboard required (a
      dashboard is demand-driven, README section 12).
- [ ] AC-08: Given a telemetry outage, then it fails soft - a failed usage write never
      blocks, delays, or errors a round (the same fire-and-forget posture
      `story-selection/04` sets for the serve log).

## Out of Scope
- Real unique-user identity, cross-device dedupe, or login-based counting - needs
  accounts-identity (Phase 2), parked; today's number is honestly an anonymous device
  count (AC-03).
- Third-party product-analytics SDKs (PostHog, Segment, GA4, Amplitude), session
  replay, heatmaps, or funnel/cohort tooling - App Insights custom events only,
  anonymous. (Some of those also raise child-privacy concerns this app must avoid.)
- Per-player behavioral profiling, targeting, or any personalization off this data
  (rejected - the audience is kids, README section 6).
- Content-curation signals (per-template serve counts, like-rate) - that is
  `story-selection/04-05`, a different purpose and sink; coordinate, do not duplicate.
- A creator/product analytics dashboard or scheduled reports (demand-driven).
- Operational health telemetry (exceptions, latency) - that is `platform-devops/04`.

## Technical Notes
- **Reuse 04's pipeline:** record usage as App Insights custom events -
  `TrackEvent("RoundStarted", { mode, context })` and a completion event with a
  duration - server-side in `GameHub.StartRound`/round-complete for group, and a minimal
  client wrapper for solo (`Solo.tsx`). No new sink, no new resource.
- **Anonymous id (AC-03):** a random GUID in `localStorage` (mirror `identity.ts`'s
  device-local, anonymous posture) OR rely on App Insights' anonymous session/user ids -
  either way no PII; document that the count is approximate. Reset on storage clear.
- **PII scrubbing:** the same initializer/processor from `platform-devops/04` applies
  (single choke point) so no usage call site can accidentally attach identity or content
  (AC-04).
- **Mode label (AC-01):** source the mode from the active `ModeConfig` (game-modes) at
  round start - a stable enum-ish string, not free text.
- **Coordinate with `story-selection/04`:** that story owns the per-template serve log
  (content curation); this story owns game-type/session-length/reach (product usage).
  Keep them complementary and, ideally, queryable together - do not fork a second
  Table Storage schema for what a custom event covers.
- MUI theme / FontAwesome not relevant (no player-facing UI). TS strict; no `any`. No
  em dashes in code/docs.

## Tests
| AC | Test |
|---|---|
| AC-01 | manual: play each mode (solo + group); confirm a `RoundStarted` custom event with the correct mode/context appears in App Insights |
| AC-02 | manual: complete a round/session; confirm a duration is recorded |
| AC-03 | manual + code review: an anonymous device/session id is used (no PII); the reach number is documented as approximate |
| AC-04 | code review + manual: no nickname/join-code/session-id/content on any usage event; sample live events for leaks |
| AC-05 | code review: no entitlement gate, no player-facing toggle; free and paid measured identically |
| AC-06 | code review: usage events ride 04's App Insights pipeline; no third telemetry system; serve-log coordination noted |
| AC-07 | manual: an App Insights query returns modes-over-time, median session length, and approximate active devices - no dashboard needed |
| AC-08 | manual: simulate a telemetry outage; confirm rounds start/finish normally with no error surfaced |

## Dependencies
- platform-devops/04-operational-observability (the App Insights pipeline + PII scrubber this reuses)
- game-modes (the mode label attached to the RoundStarted event)
- single-player + group-play (the round start/complete lifecycle the events hook into)
- story-selection/04-story-delivery-metrics (coordinate the anonymous telemetry philosophy; complementary, not duplicated)
- child-safety/01-profanity-filter (the no-PII / no-content posture)
