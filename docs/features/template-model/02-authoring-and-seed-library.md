# Story: Authoring format + seed library

**Feature:** Template & Content Model  ·  **Status:** Complete

## Context
Slice 1 ships a tiny hand-written library so there is something genuinely funny
to play before any AI exists. This story defines how templates are authored and
seeds 10-15 of them. See [feature.md](./feature.md).

## Acceptance Criteria
- [x] AC-01: There is a simple, documented authoring format for writing a template
      by hand (editing a data file, no special tooling required).
- [x] AC-02: A seed library of 10-15 hand-written templates exists and loads into
      the app.
- [x] AC-03: Every seed template is tagged family-safe and has been vetted to the
      same standard players' submitted words must meet (README section 6).
- [x] AC-04: A new template can be added as data (no code change), or with a
      single clearly documented step.

## Out of Scope
- AI generation and the back-office content factory (Phase 2).
- A content-authoring UI.
- User-generated templates (Phase 4).

## Technical Notes
- Store the seed library as data the app loads (later this can move behind the
  API / Table Storage; for Slice 1 a bundled data file is fine).
- Keep the authoring format readable so writing a funny template is quick.

## Tests
Validation specs run under the same minimal Vitest seed as story 01
(`npm run test:unit` in `web/`):
- `web/src/content/seedLibrary.test.ts` - asserts the library holds 10-15
  templates (AC-02), every id is unique, every template is `familySafe` /
  `all-ages` (AC-03), every blank carries exactly 3 non-empty spark words plus a
  prompt / sub-hint / category label, and every template `assemble()`s without
  throwing via the real engine assembler. Confirms both word-bank and free-text
  templates exist.

## Dependencies
- template-model/01-template-schema
