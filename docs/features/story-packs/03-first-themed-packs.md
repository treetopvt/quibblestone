# Story: First themed packs (seed content)

**Feature:** Story Packs (Guardian's Vault)  ·  **Status:** Not Started  ·  **Issue:** #77

## Context
A pack catalog with nothing in it proves nothing. This story hand-authors
and vets an initial small set of themed packs so the Guardian's Vault has
something genuinely playable, mirroring exactly how Slice 1 shipped with a
tiny hand-written library before any AI existed (`template-model/02`). See
[feature.md](./feature.md) and README section 8's "tiny hand-written library
first" discipline.

## Acceptance Criteria
- [ ] AC-01: Given the initial pack set, then it includes Holiday, Road-Trip,
      and Spooky - each a small, hand-curated collection of templates
      conforming to the `Pack` model from story 01.
- [ ] AC-02: Given every template in every initial pack, then each is tagged
      family-safe and vetted to the same standard players' submitted words
      must meet (README section 6) - "Spooky" is silly-spooky, not
      genuinely scary, consistent with the family-safe, all-ages brand
      (docs/design/DESIGN_RULES.md: "playful storybook-fantasy... all-ages").
- [ ] AC-03: Given the family-safe toggle is on, then every initial pack's
      content is still fully offered (all three packs pass the family-safe
      bar by construction, not as an exception).
- [ ] AC-04: Given the authoring format from `template-model/02`, then each
      pack is added as data (a new `web/src/content/packs/<name>.ts` file),
      no code change beyond registering the pack in the catalog.
- [ ] AC-05: Given a new pack needs to be added later, then the documented
      steps to do so are the same for a hand-curated pack and (once
      `ai-content-factory` ships) a pack drawing on published AI content -
      story 01's model does not distinguish the two at read time.

## Out of Scope
- Free-vs-locked assignment strategy for these specific packs (a pricing/
  business decision made separately, at billing-entitlements integration
  time - this story authors content, story 02 owns the gating mechanism).
- Drawing on `ai-content-factory`'s published library (a soft dependency
  only - this story ships against hand-curated content regardless of that
  feature's status, mirroring the Slice-1 seed library's own independence
  from AI content).
- A content-authoring UI (mirrors `template-model/02`'s own out-of-scope
  stance).
- Exhaustive pack coverage - this is a small initial set (Holiday, Road-Trip,
  Spooky), not an attempt to launch with every conceivable theme.

## Technical Notes
- Project: `web/` (content data), following `template-model/02`'s pattern
  exactly: a simple, documented, hand-authorable format, no special tooling.
- Each pack file (`web/src/content/packs/holiday.ts`, `roadTrip.ts`,
  `spooky.ts`) exports a `Pack` (story 01's type) plus either inline
  `Template`s or references to templates added to the base library - follow
  whichever approach story 01's model settles on, documented in the same
  authoring note.
- Keep each pack small (mirrors the Slice-1 seed library's 10-15-template
  scale per pack, or fewer to start) - funny and finished beats exhaustive.
- Vet every template the same way the Slice-1 seed library was vetted:
  family-safe tag, reviewed against the child-safety standard, no
  edge-of-appropriate content dressed up as "just spooky enough."

## Tests
No test harness is wired up yet for this feature's code; note intended
tests here per the platform-devops harness plan (mirrors
`template-model/02`'s validation-spec approach).

| AC | Test |
|---|---|
| AC-01 | `web/src/content/packs/packs.test.ts` (planned) - asserts Holiday, Road-Trip, and Spooky packs exist, each with a non-empty template list conforming to story 01's `Pack` shape |
| AC-02 | `web/src/content/packs/packs.test.ts` (planned) - every template in every initial pack is `familySafe`; manual editorial review of tone (silly-spooky, not scary) |
| AC-03 | manual: with the family-safe toggle on, confirm all three initial packs remain fully selectable in the Guardian's Vault |
| AC-04 | manual: confirm adding a new template to an existing pack is a data-only change (edit the pack's `.ts` file, no other code touched) |
| AC-05 | manual: review the authoring note for parity between the hand-curated and (future) AI-sourced pack-authoring steps |

## Dependencies
- story-packs/01-pack-catalog-model (the `Pack` shape these packs conform to)
- template-model/02-authoring-and-seed-library (the authoring-format
  pattern this story mirrors)
