# Story: Length classes + the one selection pipeline

**Feature:** Story Selection & Freshness  ·  **Status:** Complete  ·  **Issue:** #91

## Context
Today both template picks (solo in `Solo.tsx`, group in `GameHub.StartRound`)
are a uniform random draw over the family-safe-filtered library, blind to
length. The seed library now runs 9-10 blanks per story - great for a full
group, heavy for a solo player or a quick round in a school pickup line. This
story is the foundation: classify every template into a length class derived
from its blank count, and refactor both selection sites into one explicit,
pure pipeline (family-safe gate -> length filter -> random pick) that later
stories (quick-story option, freshness) extend with more stages. It also
authors the quick-length seed content, because a length filter over a library
with no quick stories selects nothing. See [feature.md](./feature.md).

## Acceptance Criteria
- [x] AC-01: Given any template, then its length class (`quick` | `full`)
      derives from its blank count against a single exported threshold
      constant - web derives from `getBlanks(template).length`, server from
      the catalog's existing `BlankCount`. No new authored tag, no change to
      the Template schema or the engine.
- [x] AC-02: Given the web, then a pure `selectByLength(templates, lengthPref)`
      stage exists alongside `selectTemplates` (the family-safe gate) and is
      unit-tested: `quick` returns only quick templates, `full` only full,
      `any` returns the input unfiltered and never mutates it.
- [x] AC-03: Given the server, then a mirrored length-filter stage exists next
      to `FamilySafeContentSelector` with identical behavior over
      `TemplateCatalogEntry.BlankCount`, and `GameHub.StartRound`'s pick reads
      as the explicit pipeline: family-safe gate -> length filter -> random.
      The family-safe gate ALWAYS runs first; no path around it.
- [x] AC-04: Given the seed library, then it contains at least 4 quick
      templates (4-6 blanks) alongside the existing full ones, each meeting
      every existing seed bar (family-safe, all-ages, 3 spark words per blank,
      assembles clean) and mirrored into `TemplateCatalog.cs`. The
      seedLibrary size test's upper bound is raised to fit.
- [x] AC-05: Given no caller asks for a length (this story adds no UI), then
      both picks behave exactly as before: `any` is the default everywhere and
      the observable behavior of solo and group play is unchanged.
- [x] AC-06: Given a length filter would produce an empty pool (e.g. a future
      pack with no quick stories), then selection falls back to the family-safe
      pool rather than failing the round - degrade to a longer story, never to
      an error. Fallback behavior is identical on both sides and unit-tested.

## Out of Scope
- Any player-facing UI (the quick-story toggle is story 02).
- Freshness / repeat avoidance (story 03).
- A `medium` class or per-mode tuning tables - two classes until real play
  says otherwise.
- Changing how blanks are distributed or assembled (engine untouched).

## Technical Notes
- Web: new pure module `web/src/content/length.ts` (threshold constant +
  classify + filter), same file-shape and header-comment discipline as
  `familySafe.ts`. Threshold suggestion: quick <= 6 blanks, full >= 7 - the
  current library (9-10) is all full, which is why AC-04 authors quick content.
- Server: extend the selection in `GameHub.StartRound` via a small pure class
  next to `FamilySafeContentSelector` (e.g. `LengthContentSelector`),
  singleton-registered like its sibling. Keep the two sides in behavioral
  lockstep BY HAND and say so in both headers (established convention).
- Quick seed templates: follow `web/src/content/README.md` (this story should
  also add a "quick vs full" note to that guide). Good quick premises read as
  one beat: a single joke setup + punchline, 4-6 blanks, 2-3 sentences.
- `TemplateCatalog.cs` mirror update is part of this story's definition of
  done - the wire contract note in that file applies.

## Tests
| AC | Test |
|---|---|
| AC-01 | `web/src/content/length.test.ts` (classification at, below, above the threshold) |
| AC-02 | `web/src/content/length.test.ts` (filter per pref; input never mutated) |
| AC-03 | `tests/QuibbleStone.Api.Tests/LengthContentSelectorTests.cs`: family-safe-off + quick pref picks only quick ids (selector unit; StartRound gains no length param until story 02, so the pipeline ordering is enforced by structure until then) |
| AC-04 | `web/src/content/seedLibrary.test.ts` (extend: at least 4 quick templates; existing shape/safety specs cover the rest) |
| AC-05 | existing solo + group specs stay green with no changes to their assertions |
| AC-06 | unit test both sides: empty length pool falls back to the family-safe pool |

## Dependencies
- template-model/02 (the seed library + catalog this classifies) - Complete.
- none new; this is the feature's foundation story.
