# Story: Publish to library

**Feature:** AI Content Factory (back office)  ·  **Status:** Not Started  ·  **Issue:** #80

## Context
The last step of the pipeline: an approved candidate becomes a real,
playable template. This story writes approved content into the same content
library game modes already draw from, so the library grows without the
downstream engine ever needing to know or care whether a template was
hand-written or AI-generated. It is also the feed `story-packs` draws on for
anything beyond the initial hand-curated pack set. See
[feature.md](./feature.md) and README section 2.

## Acceptance Criteria
- [ ] AC-01: Given a candidate has been explicitly approved (story 02, AC-03),
      when publish runs, then it is written into the content library (Table
      Storage) in the same shape as a hand-written seed template - no
      "AI-origin" special case for any downstream consumer.
- [ ] AC-02: Given a candidate has NOT been approved, then publish never
      writes it to the library, even if it passed the automated filter (the
      human approval gate from story 02 is the only path to publish).
- [ ] AC-03: Given a template is published, then it is immediately readable
      by `IContentLibrary` consumers (game-mode template pickers,
      `story-packs`) using the exact same read path as hand-written seed
      content - no separate "AI library" query surface.
- [ ] AC-04: Given publish is re-run for an already-published candidate, then
      it does not create a duplicate entry (idempotent).
- [ ] AC-05: Given content is mutable (CLAUDE.md preamble: this is a toy, not
      a system of record), then a published template can be edited or
      unpublished afterward without special ceremony - publish does not lock
      the record.
- [ ] AC-06: Given a published template, then it retains its family-safe flag
      and theme/age tags from vetting (story 02), so the family-safe toggle
      and content selectors work identically for AI-published and
      hand-written content.

## Out of Scope
- Generation or vetting logic (stories 01 and 02 own those).
- A content-authoring UI for editing published templates (a data-level edit
  is enough for this story; an authoring UI is a later, separate concern -
  mirrors `template-model/02`'s own out-of-scope stance).
- Versioned rollback / history of edits (mutable, not audited - CLAUDE.md
  preamble).
- Any pack-grouping or catalog logic (that is `story-packs/01`, which reads
  from this library but owns its own grouping model).

## Technical Notes
- Project: `api/`. Storage: Azure Table Storage (README section 4:
  "templates and entitlements"), same store the hand-written seed library
  will eventually migrate behind if/when it moves off the bundled data file
  (`template-model/02`'s Technical Notes already flag this as a later move).
- The published-template shape must be schema-compatible with
  `web/src/engine/template.ts`'s `Template` / `Blank` / `BlankCategory` - a
  strict match, not a superset with extra required fields, so the engine
  never needs an "is this AI or hand-written" branch (one engine, many thin
  modes extends to content origin).
- Idempotency (AC-04): key the write on the candidate's stable id (assigned
  in story 01) so re-running publish on the same approved candidate is a
  no-op or an overwrite of the same record, never a duplicate row.
- `IContentLibrary` (this story's export) is the read seam - keep the write
  path (publish) and the read path (list/get for consumers) on the same
  interface so `story-packs` has exactly one thing to depend on.

## Tests
No test harness is wired up yet for this pipeline's back-office code; note
the intended tests here so they exist once xUnit/`dotnet test` is available.

| AC | Test |
|---|---|
| AC-01 | `api/tests/Content/TableStorageContentLibraryTests.cs` (planned) - publishing an approved candidate produces a library entry matching the `Template`/`Blank` shape |
| AC-02 | manual: attempt to publish an unapproved/rejected candidate id and confirm no library write occurs |
| AC-03 | manual: after publish, read the template back via `IContentLibrary` using the same call a game-mode template picker or `story-packs` would use |
| AC-04 | `api/tests/Content/TableStorageContentLibraryTests.cs` (planned) - publishing the same candidate id twice yields one library entry |
| AC-05 | manual: edit a published template's data record directly and confirm no locking/versioning error blocks it |
| AC-06 | manual: confirm a published template's family-safe flag and tags match what story 02 assigned, and that the family-safe toggle (child-safety/02) correctly includes/excludes it |

## Dependencies
- ai-content-factory/02-vetting-queue (approved candidates to publish)
- template-model/01-template-schema (the schema the library must match)
