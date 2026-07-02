# Story: Solo play (Classic blind end to end)

**Feature:** Single-Player Experience  ·  **Status:** Complete

## Context
The fastest path to a laugh, for one person, with no setup. It is also the funnel
that sells someone on starting a group. See [feature.md](./feature.md).

## Acceptance Criteria
- [x] AC-01: Given I am on the home screen, when I choose solo play, then I start a
      game by myself with no room and no join code.
- [x] AC-02: Given solo play, then I play Classic blind: I pick or am given a
      template and am prompted for each blank by its type, answering with free text.
- [x] AC-03: Given I fill every blank, then I see the text reveal of my completed
      story.
- [x] AC-04: Given solo play, then my free-text answers pass the safety filter and
      only family-safe content is used, so it is safe to hand to a kid.
- [x] AC-05: Given solo play, then I am never asked for an account or any personal
      information.
- [x] AC-06: Given I finish a story, then I can start another in one tap (the
      "bored in line" replay loop) via the Reveal screen's "Play another round".
- [x] AC-07: Given I finish a solo story, then I land on the Reveal screen with a
      personal summary (story title and my word count) and the "Share the tale"
      (Web Share) and "Play another round" actions; I do NOT see the group Round
      Complete crew recap (group-play/04) - there is no crew to recap and no
      per-player attribution to show.

## Out of Scope
- Accounts; saving the tale as an image / keepsake export (Phase 3). Sharing the
  story text via the Web Share API is in scope (reuses the Reveal screen's
  "Share the tale" - the-reveal/01).
- The group Round Complete crew recap screen (group-play/04) - solo skips it.
- AI content; modes other than Classic blind.
- Any multiplayer / room behavior (that is group-play).

## Technical Notes
- Reuse the engine (game-modes) with a single filler; render via the-reveal.
- No SignalR room is required for solo (it is local to the one client), though it
  still uses the same engine logic.

## Tests
- Unit (Vitest): `web/src/pages/Solo.test.ts` covers the pure `pickRandomTemplate`
  helper (empty list, single item, always-in-range, Math.random boundary).
- Manual (verified in the solo playthrough, feat/solo-play, real browser on the
  Vite build with a mock of the .NET moderation endpoint): Home "Or play solo
  right now" -> setup (family-safe toggle, default on) -> Classic-blind FillBlank
  per blank (AC-01/AC-02) -> a blocked word is rejected inline and retried, a
  clean word is accepted, all via the engine-boundary safety check (AC-04) ->
  Skip leaves a blank empty without shifting later words -> text Reveal (AC-03)
  with a personal summary "You filled N words" and NO crew recap (AC-07) ->
  one-tap "Play another round" returns straight to FillBlank (AC-06). No room,
  join code, account, or PII anywhere in the flow (AC-01/AC-05). The real .NET
  IContentSafetyFilter is compiled + tested on CI (Gate 3); local live-run of the
  API was not possible (the .NET SDK host is egress-blocked in the web session).

## Dependencies
- template-model/01-template-schema
- game-modes/02-classic-blind
- the-reveal/01-text-reveal
- child-safety/01-profanity-filter
- child-safety/02-family-safe-toggle
- design-system/01-mui-theme-and-app-shell
