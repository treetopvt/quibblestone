# Story: Freshness rotation: no repeats until the pool runs dry

**Feature:** Story Selection & Freshness  ·  **Status:** Complete  ·  **Issue:** #93

## Context
With ~15 templates and a uniform random pick, a family playing five rounds has
good odds of hearing the same story twice in one sitting - the fastest way for
the game to stop feeling fresh. This story adds the freshness stage to story
01's pipeline: the random pick avoids recently-played templates until the
eligible pool is exhausted, then recycles oldest-first. Explicit replay (solo
"Play again", replay-remix's "Carve it again") deliberately bypasses it - the
player deciding to repeat is a feature, the dice repeating is a bug. Scoped to
what an account-less game can know: this device (solo), this room (group). See
[feature.md](./feature.md).

## Acceptance Criteria
- [x] AC-01: Given solo play on one device, when I play consecutive rounds via
      the random pick, then no template repeats until every template in my
      eligible pool (after safety + length filters) has been played once; the
      history survives a page refresh (device-local persistence).
- [x] AC-02: Given a group room, when the host starts consecutive rounds, then
      no template repeats within that room's lifetime until the eligible pool
      is exhausted. The room's played history lives on the server Room record
      and dies with the room (ephemeral, like the rest of room state).
- [x] AC-03: Given the eligible pool is exhausted, then selection recycles
      (least-recently-played first preferred; at minimum the full pool
      reopens) and play continues without error or visible hiccup.
- [x] AC-04: Given an explicit replay (solo "Play again" with the same
      template, "Carve it again" per replay-remix/01, or replaying a favorite
      per story-selection/06), then the
      freshness filter is bypassed AND the replay does not re-stamp the
      template's freshness history - replaying a favorite must not make the
      random pick "forget" other unplayed stories.
- [x] AC-05: Given freshness filtering, then it composes as the LAST filter
      before the random pick (safety -> length -> freshness -> random) and
      never weakens the earlier stages; with an empty freshness result the
      fallback of AC-03 applies within the already-safe, already-length-
      filtered pool.
- [x] AC-06: Given the solo device history, then it stores template ids and
      nothing else - no words, no timestamps traceable to a person, no PII
      (README section 6); clearing browser storage simply resets freshness.

## Out of Scope
- Cross-device or cross-session-per-person freshness (needs accounts-identity;
  parked in feature.md).
- Any UI for viewing or editing play history ("Tales we've carved" is
  keepsake-gallery/03; this history is invisible plumbing).
- Weighted randomness / recency decay curves - a plain exclusion set with an
  oldest-first reset is the whole story.
- Persisting group room history beyond the room's life.

## Technical Notes
- Web: a pure `selectFresh(templates, playedIds)` stage in
  `web/src/content/` (reference spec, unit-tested like `familySafe.ts` /
  `distribute.ts`), plus a tiny device-history module (localStorage,
  `quibblestone.playedTemplates` or similar - an ordered array of ids, capped;
  same device-local posture keepsake-gallery/03 documents). Solo pick composes
  it after the safety and length stages.
- Server: mirror the pure exclusion rule beside the other selectors; Room
  gains a `PlayedTemplateIds` ordered list, appended in StartRound on random
  picks only (see AC-04). In-memory only - this is a toy; a server restart
  resetting freshness is acceptable behavior, not a bug.
- Coordination seam with replay-remix/01: its pinned-template path must skip
  BOTH the filter and the history append. If that story lands first, add the
  skip there; if this lands first, leave the seam documented in StartRound.
- Recycle rule: when the exclusion empties the pool, drop the oldest played id
  and retry (or equivalently pick least-recently-played) - deterministic and
  unit-testable on both sides.

## Tests
| AC | Test |
|---|---|
| AC-01 | Vitest: selectFresh excludes played ids; manual: solo, small pool via quick pref, confirm no repeat until exhaustion and survival across refresh |
| AC-02 | API unit test: repeated StartRound on one room never repeats until pool exhausted |
| AC-03 | Vitest + API unit test: exhausted pool recycles oldest-first, never throws |
| AC-04 | API unit test: pinned replay ignores and does not append history; Vitest for the solo equivalent |
| AC-05 | Vitest: pipeline composition order test (safety filter output is the freshness input) |
| AC-06 | code review + Vitest on the history module's stored shape (ids only) |

## Dependencies
- story-selection/01 (the pipeline this stage slots into).
- session-engine / group-play (the Room record and StartRound) - Complete.
- replay-remix/01 (seam coordination only - neither blocks the other; see
  Technical Notes).

## Orchestration notes (Gate 1 - build/ss-03)
- Tests: `web/src/content/fresh.test.ts` + `playedHistory.test.ts` (AC-01/03/05/06),
  `tests/QuibbleStone.Api.Tests/FreshnessContentSelectorTests.cs` +
  `RoomPlayedHistoryTests.cs` + new `GameHubStartRoundTests.cs` cases
  (AC-02/03/04). All green.
- Gate-1 review: clean, no blockers. Pipeline order (safety -> length ->
  freshness -> random) and the web/C# mirror verified behavior-identical;
  ids-only history both sides; AC-04 bypass seam documented at every call site;
  wire contract + engine untouched.
- W-001 (recycle boundary) - RESOLVED (user sign-off 2026-07-02): tightened the
  recycle to EXCLUDE the single most-recently-played story when the pool holds
  >=2, so the wrap can never immediately repeat the tale just served (a 1-story
  pool still returns it - a repeat is then unavoidable). Applied identically on
  both sides (fresh.ts `recycleExcludingMostRecent` / FreshnessContentSelector
  `RecycleExcludingMostRecent`) with new "never immediately repeats across a
  wrap" + size-1 tests both in web and C#. This delivers AC-03's
  "least-recently-played first" intent functionally rather than as an inert sort.
