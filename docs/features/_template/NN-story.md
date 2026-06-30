<!--
  Template: docs/features/{slug}/NN-<slug>.md  (order-prefixed: 01-, 02-, ...)
  Canonical template lives in README section 11. This mirrors it so a feature reads top-to-bottom in build order.
  One builder owns this story: it must be buildable to its ACs from this file + the implementation.md per-story
  note + the reuse map, with no other context. Use hyphens/colons/parentheses, never em dashes.
-->

# Story: <title>

**Feature:** <parent feature>  ·  **Status:** Not Started  <!-- Not Started | In Progress | Complete | Blocked | Dropped -->  ·  **Issue:** #<n>

## Context
<Why this story exists; what the user or system needs. Link to feature.md.>

## Acceptance Criteria
- [ ] AC-01: <observable, testable outcome (Given / When / Then preferred)>
- [ ] AC-02: <...>
<!-- Child-safety AC where the story submits or shows free text: content passes the safety filter / honors the
     family-safe toggle, and no PII is collected (README section 6). -->
<!-- Entitlement AC where the story creates a paid-tier capability: state free-vs-paid at session-creation time
     (README section 3) - do not sprinkle per-request checks. -->

## Out of Scope
<What this story deliberately does NOT do - guards against scope creep.>

## Technical Notes
<Stack-specific hints: relevant projects (api/, web/), patterns, libraries, gotchas. Which reuse-map rows apply.>

## Tests
<Link each AC to the test that proves it. (No test harness is wired up yet - story platform-devops/01 sets up
 Vitest + Playwright. Until then, note the intended test and the manual check.)>

| AC | Test |
|---|---|
| AC-01 | `<path to unit/e2e test, or "manual: ...">` |

## Dependencies
<Stories that must land first, or "none".>
