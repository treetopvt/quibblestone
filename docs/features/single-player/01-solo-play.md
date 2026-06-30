# Story: Solo play (Classic blind end to end)

**Feature:** Single-Player Experience  ·  **Status:** Not Started

## Context
The fastest path to a laugh, for one person, with no setup. It is also the funnel
that sells someone on starting a group. See [feature.md](./feature.md).

## Acceptance Criteria
- [ ] AC-01: Given I am on the home screen, when I choose solo play, then I start a
      game by myself with no room and no join code.
- [ ] AC-02: Given solo play, then I play Classic blind: I pick or am given a
      template and am prompted for each blank by its type, answering with free text.
- [ ] AC-03: Given I fill every blank, then I see the text reveal of my completed
      story.
- [ ] AC-04: Given solo play, then my free-text answers pass the safety filter and
      only family-safe content is used, so it is safe to hand to a kid.
- [ ] AC-05: Given solo play, then I am never asked for an account or any personal
      information.
- [ ] AC-06: Given I finish a story, then I can start another in one tap (the
      "bored in line" replay loop) via the Reveal screen's "Play another round".
- [ ] AC-07: Given I finish a solo story, then I land on the Reveal screen with a
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

## Dependencies
- template-model/01-template-schema
- game-modes/02-classic-blind
- the-reveal/01-text-reveal
- child-safety/01-profanity-filter
- child-safety/02-family-safe-toggle
- design-system/01-mui-theme-and-app-shell
