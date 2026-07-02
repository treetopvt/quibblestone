# Story: Family-safe toggle

**Feature:** Child Safety & Moderation  ·  **Status:** Complete

## Context
Beyond filtering typed words, the curated content itself should be gateable to a
family-safe set, so a host can guarantee the whole session is kid-appropriate.
See [feature.md](./feature.md).

## Acceptance Criteria
- [x] AC-01: Given a family-safe toggle, when it is on, then only content
      (templates and word banks) tagged family-safe is offered or playable.
- [x] AC-02: Given the kid-facing audience, then family-safe is the default,
      safe-by-default posture.
- [x] AC-03: Given the toggle state, then it is honored everywhere content is
      selected (template pick, any word banks).
- [x] AC-04: Given family-safe is on, then the free-text profanity filter (story
      01) still applies in full - the toggle gates curated content, it does not
      relax the filter.

## Out of Scope
- Granular age ratings beyond family-safe vs not (later).
- Per-player overrides (this is a session/host-level setting for now).

## Technical Notes
- Reads the theme/age tags from template-model; filters the offered content set.
- Default on. Surface it where the host sets up a session (and for solo play).

## Tests
- Unit (Vitest): `web/src/content/familySafe.test.ts` covers `isFamilySafe` and
  `selectTemplates` on/off filtering, immutability (shallow copy, not the same
  reference), and empty-input cases (AC-01/AC-03).
- Manual (verified in the solo playthrough, feat/solo-play): the family-safe
  toggle surfaces on the solo setup, defaults ON (AC-02, via `FAMILY_SAFE_DEFAULT`),
  and gates the solo template pick + replay to the family-safe set (AC-01/AC-03).
  The profanity filter still runs on every free-text answer regardless of the
  toggle (AC-04) - the toggle path (`familySafe.ts`) is fully isolated from the
  filter path (`checkWord` / `IContentSafetyFilter`).
- Scope note: for this wave the family-safe gate is web-side (solo content
  selection over `seedLibrary` tags). The server-side `FamilySafeContentSelector`
  lands with `group-play/01` (its only consumer - the host template list), which
  is out of this umbrella.

## Dependencies
- template-model/01-template-schema (tags)
- child-safety/01-profanity-filter (complementary, always-on)
