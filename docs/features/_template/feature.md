<!--
  Template: docs/features/{slug}/feature.md
  Canonical template lives in README section 11. This copy mirrors it for convenience and adds the
  orchestration-facing bits (Decisions log; the Stories table carries the GitHub issue number).
  One folder per feature; a feature.md plus one NN-<slug>.md per story plus an implementation.md.
  Use hyphens/colons/parentheses, never em dashes.
-->

# Feature: <name>

## Summary
<1-3 sentences: what this feature is and the value it delivers.>

## README reference
<Link to the relevant README section, e.g. section 4 architecture / section 7 epic.>

## Stories
<!-- Status: Not Started | In Progress | Complete | Blocked | Dropped -->
| Story | Issue | Title | Status |
|---|---|---|---|
| 01 | #<n> | <story title> | Not Started |
| 02 | #<n> | <story title> | Not Started |

## Dependencies
<Other features that must exist first, or "none".>

## Design notes
<Architecture/design considerations specific to this feature.>

## Parked - Phase 2+
<Great ideas that belong to a later phase. Record them here; do not pull into the active slice (CLAUDE.md section 7).>

## Decisions
<Running log of cross-cutting decisions made during planning or at a verification checkpoint. Each entry: date,
decision, why. Behavior-changing checkpoint feedback lands here first, then in the affected stories' ACs.>
