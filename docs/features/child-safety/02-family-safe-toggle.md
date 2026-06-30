# Story: Family-safe toggle

**Feature:** Child Safety & Moderation  ·  **Status:** Not Started

## Context
Beyond filtering typed words, the curated content itself should be gateable to a
family-safe set, so a host can guarantee the whole session is kid-appropriate.
See [feature.md](./feature.md).

## Acceptance Criteria
- [ ] AC-01: Given a family-safe toggle, when it is on, then only content
      (templates and word banks) tagged family-safe is offered or playable.
- [ ] AC-02: Given the kid-facing audience, then family-safe is the default,
      safe-by-default posture.
- [ ] AC-03: Given the toggle state, then it is honored everywhere content is
      selected (template pick, any word banks).
- [ ] AC-04: Given family-safe is on, then the free-text profanity filter (story
      01) still applies in full - the toggle gates curated content, it does not
      relax the filter.

## Out of Scope
- Granular age ratings beyond family-safe vs not (later).
- Per-player overrides (this is a session/host-level setting for now).

## Technical Notes
- Reads the theme/age tags from template-model; filters the offered content set.
- Default on. Surface it where the host sets up a session (and for solo play).

## Dependencies
- template-model/01-template-schema (tags)
- child-safety/01-profanity-filter (complementary, always-on)
