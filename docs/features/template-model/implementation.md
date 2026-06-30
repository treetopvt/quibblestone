<!--
  Implementation plan for the template-model feature. Bridges feature.md + stories to orchestration.
  Use hyphens/colons/parentheses, never em dashes.
-->

# Implementation Plan: Template & Content Model

> The bridge between planning and orchestration. The **Wave Plan** below is DAG-ready: the `orchestrate-feature`
> skill's Phase 1 validates and adjusts it rather than deriving it. See
> [`docs/FEATURE_ORCHESTRATION_PLAYBOOK.md`](../../FEATURE_ORCHESTRATION_PLAYBOOK.md).

This is the **content foundation** for "one engine, many thin modes" (README section 4): a single mode-agnostic
template type plus a deterministic assembler that every mode and the reveal build on. Story 01 (schema + assembler)
is foundation wave; story 02 (authoring format + seed library) imports the schema, so it serializes behind 01.

## Reuse map

| Concern | Reuse | Where |
|---|---|---|
| Pure-TS home for engine logic | the `web/src/engine/` module this feature establishes | `web/src/engine/template.ts` (new) |
| Test harness for the pure assembler | Vitest (**platform-devops/01**) | `web/vitest.config.ts` |
| Tag consumers | the family-safe gate (**child-safety/02**) and the host template list (**group-play/01**) | `api/src/Safety/FamilySafeContentSelector.cs` |
| FillBlank prompt-card field rendering (reference) | the design pack screen 4 spec | `docs/design/README.md` (Screens, FillBlank) |

What this feature **exports** that others import:
- The `Template` / `Blank` types and the `BlankCategory` enum (adjective / noun / verb / name / place / exclamation
  / number / plural-noun) - imported by `game-modes`, `the-reveal`, `single-player`, `group-play`.
- The **deterministic** `assemble(template, orderedWords)` function carrying **per-word attribution**
  (`playerSessionId + word`) - the pure logic the reveal renders and Round Complete counts.
- The **seed library** data (10-15 hand-written, family-safe templates) the app loads.

## Wave Plan (DAG)

Sizing rule: a builder owns files **disjoint** from its concurrent siblings.

| Story | Issue | Files it owns (footprint) | Depends-on | Can-run-with | Wave | Effort |
|---|---|---|---|---|---|---|
| 01 template-schema | #25 | `web/src/engine/template.ts` (types + `BlankCategory`), `web/src/engine/assemble.ts` (pure assembler), unit tests | none (content foundation) | child-safety/01, design-system/01-02, platform-devops/01-02 (all disjoint, other features) | 1 | medium |
| 02 authoring + seed-library | #26 | `web/src/content/seedLibrary.ts` (the 10-15 templates as data), an authoring `README`/doc | template-model/01 | child-safety/02 (disjoint) | 2 | medium |

**Concurrency per wave:** Wave 1 = 1 story in this feature (runs alongside the other foundations - disjoint files).
Wave 2 = 1 story. Story 02 imports the schema type from story 01, so they **serialize**.

## Per-story tech notes

### 01 - Template schema (typed blanks, optional word bank, tags)
- **Approach:** define the type in **shared, pure TS** (`web/src/engine/`), free of UI/real-time concerns (AC
  notes). A template has a title/subject and body with **ordered, typed blanks** (AC-01). Each blank is
  self-contained: `category` label (the purple chip, e.g. "ADJECTIVE"), a `prompt` sentence, a `subHint`, and 3
  `sparkWords` - all **properties of the blank definition**, not derived at runtime (AC-02), so the format stays
  hand-authorable. Optional `wordBank` (AC-03) and `tags` (theme + age, AC-04). The schema is **mode-agnostic**
  (AC-05) - the mode decides see/answer/reveal, never the template. `assemble()` replaces blanks in order
  deterministically and preserves **per-word attribution** (AC-06).
- **Key files it owns:** `web/src/engine/template.ts`, `web/src/engine/assemble.ts`, their Vitest tests.
- **Exports:** `Template`, `Blank`, `BlankCategory`, `assemble()`, the assembled-result type (with attribution).
- **Gotchas:** keep `assemble()` pure (this is the prime unit-test target, README section 4). `BlankCategory` is a
  small **extensible** enum. Out of scope: AI-generated templates, per-host word banks, rich media, and
  AI-personalized spark chips (Slice 1 spark chips are hardcoded in the schema).

### 02 - Authoring format + seed library
- **Approach:** a simple, **documented** authoring format - editing a data file, no special tooling (AC-01) - and
  10-15 hand-written templates that load into the app (AC-02). Every seed is tagged family-safe and vetted to the
  same standard players' words must meet (AC-03). Adding a template is a data change, no code change (AC-04).
- **Key files it owns:** `web/src/content/seedLibrary.ts`, an authoring doc.
- **Exports:** the seed library array the engine loads (later this can move behind the API / Table Storage; a
  bundled data file is fine for Slice 1).
- **Gotchas:** keep the format readable so writing a funny template is quick (it is the difference between a fun
  library and a chore). Out of scope: AI generation, an authoring UI, user-generated templates.

## Cross-cutting concerns

- **One engine, many thin modes:** the schema is the contract that makes every later mode cheap. If a mode story
  ever needs to change this schema to work, that is the abstraction leaking - flag it at integration (playbook
  Principle 2).
- **Inter-feature ordering:** `template-model/01` is foundation wave (with `child-safety/01`, `design-system/01`,
  `platform-devops/01-02`). It must land before `child-safety/02` (tags), `game-modes/01` (the engine plays a
  template), `the-reveal/01` (assembly), `single-player/01`, and `group-play`. `template-model/02` (seed content) is
  needed before any feature that actually plays a round end to end (`single-player/01`, `group-play/01`).
- **Child safety:** seed content is vetted family-safe (story 02 AC-03); the free-text filter (`child-safety/01`)
  still guards player-submitted words at play time - the curated library and the runtime filter are complementary.
- **No i18n** - template text is authored plain. **No em dashes** in templates or the authoring doc.
