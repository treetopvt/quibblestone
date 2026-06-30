# Story: Authoring format + seed library

**Feature:** Template & Content Model  ·  **Status:** Not Started

## Context
Slice 1 ships a tiny hand-written library so there is something genuinely funny
to play before any AI exists. This story defines how templates are authored and
seeds 10-15 of them. See [feature.md](./feature.md).

## Acceptance Criteria
- [ ] AC-01: There is a simple, documented authoring format for writing a template
      by hand (editing a data file, no special tooling required).
- [ ] AC-02: A seed library of 10-15 hand-written templates exists and loads into
      the app.
- [ ] AC-03: Every seed template is tagged family-safe and has been vetted to the
      same standard players' submitted words must meet (README section 6).
- [ ] AC-04: A new template can be added as data (no code change), or with a
      single clearly documented step.

## Out of Scope
- AI generation and the back-office content factory (Phase 2).
- A content-authoring UI.
- User-generated templates (Phase 4).

## Technical Notes
- Store the seed library as data the app loads (later this can move behind the
  API / Table Storage; for Slice 1 a bundled data file is fine).
- Keep the authoring format readable so writing a funny template is quick.

## Dependencies
- template-model/01-template-schema
