# Story: Pack catalog model + Guardian's Vault browser

**Feature:** Story Packs (Guardian's Vault)  ·  **Status:** Not Started  ·  **Issue:** #75

## Context
Before anything can be free-or-locked or purchased, there needs to be a
concept of a "pack" - a named, themed grouping of templates - and a place to
browse them. This story defines that model (reusing the existing template
schema rather than inventing a new one) and builds the browse screen, the
Guardian's Vault. See [feature.md](./feature.md) and README section 3.

## Acceptance Criteria
- [ ] AC-01: Given the pack model, then a pack has an id, a display name, a
      theme tag, an age tag, a free-or-locked flag, and an ordered list of
      the templates it contains - each template conforming to the existing
      `template-model` `Template`/`Blank`/`BlankCategory` schema with no
      pack-specific forking of that shape.
- [ ] AC-02: Given the Guardian's Vault browse screen, when a player opens it,
      then they see a grid of packs rendered as glowing stone-tablet cards
      (theme tokens, not hardcoded values), each showing the pack name, a
      theme icon, and whether it is free or locked.
- [ ] AC-03: Given a pack is free, then its card renders with no lock
      indicator and is fully browsable; given a pack is locked, then its
      card renders with a gold-lock badge but is still visible and tappable
      (browsing is never gated, only playing).
- [ ] AC-04: Given pack data, then it persists in Azure Table Storage
      (matching README section 4's storage model for templates and
      entitlements) - not only as a bundled front-end data file, once the
      catalog is server-backed.
- [ ] AC-05: Given a player views a pack's detail (tapping a card), then they
      see the templates it contains (title/subject visible, blank content
      not spoiled), consistent with how the family-safe toggle already
      presents curated content.
- [ ] AC-06: Given pack names, theme labels, and any other free-text-adjacent
      display strings shipped with a pack, then they pass the same content
      safety standard as any other authored content (README section 6) - no
      PII is collected or displayed anywhere in the catalog (packs are static
      authored content, not player-submitted).

## Out of Scope
- Free-vs-locked enforcement / paywall interaction (story 02) - this story
  only renders the lock badge, it does not gate anything.
- The actual pack content itself beyond whatever is needed to prove the
  model works end to end (story 03 authors the first real packs).
- Pack search, filtering, or recommendation (README Phase 4).
- Per-player pack ownership or multi-tenant catalog concerns
  (billing-entitlements territory).

## Technical Notes
- Projects: `web/` and `api/`. Define `Pack` in `web/src/engine/pack.ts` as a
  new, small type alongside (not merged into) `Template` - a pack
  *references* templates by id, it does not duplicate their shape.
- Server-side: `IPackCatalog` (mirrors `IContentLibrary`'s pattern from
  ai-content-factory) backed by Table Storage; a thin `PackCatalogController`
  (REST) exposes list/get, following the existing no-logic-in-the-controller
  pattern from `ModerationController.cs`.
- Build the Vault browse screen from the shared design-system contracts only:
  `AppBar`, the theme's `tablet` gradient tokens for the card shape, `Button`
  for any CTA, FontAwesome for the lock icon and theme icons (register new
  icons in `web/src/fontawesome.ts` if needed - do not import ad hoc). No
  hardcoded colors or pixel spacing (CLAUDE.md section 4).
- Entry point: a clearly-secondary link from `Home.tsx`, matching the
  existing Solo-entry pattern (a text link, not competing with the primary
  "Create a game" / "Join a game" CTAs).
- This story's catalog can and should work with zero or hand-curated-only
  packs present - it does not hard-depend on `ai-content-factory` having
  shipped.

## Tests
No test harness is wired up yet for this feature's code; the canonical
harness is Vitest (web, pure logic) + Playwright (browser smoke) per
`platform-devops/01`, plus xUnit/`dotnet test` for `api/` once wired.

| AC | Test |
|---|---|
| AC-01 | `web/src/engine/pack.test.ts` (planned) - asserts the `Pack` type shape and that referenced template ids resolve to valid `Template`s |
| AC-02 | manual: open the Guardian's Vault and confirm the grid renders stone-tablet cards using theme tokens (no hardcoded hex/px in the component) |
| AC-03 | manual: confirm a free pack's card has no lock badge and a locked pack's card shows the gold-lock badge, both tappable |
| AC-04 | manual: inspect Table Storage (or local storage emulator) for a persisted pack record after catalog seed/load |
| AC-05 | manual: tap a pack card and confirm the detail view lists template titles/subjects without revealing blank content |
| AC-06 | manual: confirm no player-identifying field appears anywhere in a pack or catalog record; pack copy reviewed against the same standard as seed content |

## Dependencies
- template-model/01-template-schema (the schema packs group)
- design-system/01-mui-theme-and-app-shell
- design-system/02-guardian-component (theme icon treatment reference)
