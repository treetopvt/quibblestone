# Story: Like / dislike a tale (content feedback)

**Feature:** Story Selection & Freshness  ·  **Status:** In Progress  ·  **Issue:** #95

## Context
Content creators (today: whoever hand-writes seedLibrary; tomorrow: the AI
content factory's vet loop) have zero signal about which templates land and
which fall flat. This story adds the quietest possible feedback control at the
end of a tale: thumbs up / thumbs down on the STORY TEMPLATE, one vote per
player per round, recorded anonymously into story 04's sink. This is
deliberately not the Reaction row (reveal-delight/01 - party-style celebration
of this telling); it is a curation signal about the template itself. See
[feature.md](./feature.md) Design notes for that boundary.

## Acceptance Criteria
- [x] AC-01: Given the end of a tale (solo Reveal and group Round Complete),
      then I see an unobtrusive "Did you like this story?" thumbs up / thumbs
      down control - big tap targets, theme-styled, FontAwesome icons -
      placed so it never competes with the primary replay CTAs.
- [x] AC-02: Given I tap a thumb, then my vote is recorded for that template
      and round, I see a small acknowledgement (the control reflects my
      choice), and I can change my vote until I leave the screen; only my
      final state counts.
- [x] AC-03: Given a group round, then every player (host or not) can vote
      independently, and one player's vote never blocks, reveals, or alters
      another's - votes are per-player-per-round, tallied per template.
- [x] AC-04: Given a recorded vote, then it carries template id, up/down, UTC
      timestamp, mode, and the same opaque instance ids story 04 uses - no
      nickname, no join code, no PII (README section 6). No free text
      anywhere on this surface (nothing for the safety filter to filter; the
      family-safe posture is unaffected).
- [x] AC-05: Given the sink is unavailable, then voting fails soft: the UI
      still acknowledges locally and the game flow is never blocked (same
      posture as story 04's AC-03).
- [x] AC-06: Given stored votes, then per-template totals (ups, downs, by
      time range) are answerable with a straightforward table query, joinable
      against serve counts (story 04) to get a like-rate per serve.
- [x] AC-07: Given I skip voting entirely, then nothing nags me - no modal,
      no reminder, no badge. Silence is a valid answer and is simply absent
      from the data.

## Out of Scope
- Free-text feedback, star ratings, or per-blank/per-word feedback (the
  Golden Guardian award in reveal-delight/03 covers "funniest word" as play,
  not telemetry).
- Showing aggregate like counts to players (this is creator-facing curation
  data; players see only their own vote).
- Using votes to alter selection weighting (parked in feature.md - selection
  stays uniform random for now).
- A creator dashboard (parked; dev-only queries read this for now).
- De-duplicating repeat votes across replays of the same template on the same
  device (per-round votes are the honest grain; toy-grade).

## Technical Notes
- Reuses story 04's `ITelemetrySink` and storage wiring - one new table (e.g.
  `StoryFeedback`, PartitionKey = template id) and one small anonymous POST
  endpoint shared by solo and group (a plain REST call is fine for both; this
  need not ride the SignalR hub since it is not room-coordinated state -
  contrast with reveal-delight/01, whose live counts ARE room state).
- Web: one small `TaleFeedback` component used by both `Reveal.tsx` (solo) and
  `RoundComplete.tsx` (group). Local component state for the current vote;
  send-on-change with the last write winning for the round (client mints one
  vote id per round so changed votes overwrite, not double-count - or the
  endpoint upserts on (roundInstanceId, deviceSessionId)).
- Placement per the design pack's button hierarchy: the thumbs sit below or
  beside the keepsake panel, visually quieter than "Play another round" -
  they must not read as the next-step CTA.
- Coordination with reveal-delight/01: if the Reaction row lands on the same
  screen later, the thumbs stay a separate, smaller affordance ("rate the
  story", not "react to the moment"). Flag in that story's build if the two
  visually collide.

## Tests
| AC | Test |
|---|---|
| AC-01 | manual: solo Reveal and group Round Complete both show the control, correctly subordinated to the replay CTAs |
| AC-02 | Vitest on the component's vote state; manual: change vote before leaving, confirm single final row |
| AC-03 | manual: two browser contexts vote oppositely, both recorded |
| AC-04 | API unit test on the DTO/table shape (no name/code fields); code review |
| AC-05 | API unit test: throwing sink returns success-shaped response; manual: UI acknowledges with API stopped |
| AC-06 | manual: table query totals for one template id; join against serves |
| AC-07 | manual: complete a round without voting - no prompt, no data row |

## Dependencies
- story-selection/04 (the sink and storage plumbing this reuses).
- the-reveal / group-play/04 (the screens this control lands on) - Complete.

## Orchestration notes (Gate 1 - build/ss-05)
- Tests: `web/src/components/TaleFeedback.test.ts` (vote-state transition, AC-02),
  `tests/QuibbleStone.Api.Tests/TelemetryControllerFeedbackTests.cs` (PII shape
  AC-04, junk dropped, throwing sink still 202 AC-05, upsert forwarding). Green.
- Gate-1 review: clean, no blockers, no warnings. All fences verified: no PII in
  the event/DTO/table (a shape test fails CI if a field is added), fail-soft 202
  + no nag (nothing writes on mount/unmount, only a tap), NOT the reveal-delight
  Reaction row (plain per-device REST, no SignalR/room state/aggregate UI),
  upsert last-write-wins (RowKey=VoteId), subordinate placement, and the group
  TRANSIENT reveal deliberately omits the control so a round is asked exactly
  once (on Round Complete).
- Accepted non-blocking minors (toy-grade, left as-is): S-1 the AC-07 test case
  asserts a local literal (AC-07's "nothing writes on skip" is structural, held
  by code review); S-2 re-tapping the SAME thumb re-POSTs an idempotent upsert
  (same VoteId -> same row, harmless).
- `infra/main.bicep` StoryFeedback table unvalidated locally (`az`/`bicep` CLI
  absent) - mirrors story 04's StoryServes exactly; run `az bicep build` before
  any deploy (same carry-forward as story 04).
