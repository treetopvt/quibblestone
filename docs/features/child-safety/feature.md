# Feature: Child Safety & Moderation

## Summary
Cross-cutting, non-negotiable safety designed in from the start: a profanity/
safety filter on all submitted free text, a family-safe toggle, vetted library
content, and minimal data collection on minors. Other features depend on this.

## README reference
README section 6 (Child Safety & Moderation - "non-negotiable, designed in from
the start") and section 3 (identity model: anonymous players, minimal data on
minors / COPPA / GDPR-K).

## Stories
<!-- Status: Not Started | In Progress | Complete | Blocked | Dropped -->
| Story | Issue | Title | Status |
|---|---|---|---|
| 01 | #35 | Profanity / safety filter on free text | Complete |
| 02 | #36 | Family-safe toggle | Complete |

## Dependencies
None for 01 (it is foundational and other features call it). 02 uses
template-model tags.

## Design notes
- The filter is **one reusable, server-side check** that every free-text entry
  point calls (nicknames in session-engine, blank answers in game-modes /
  group-play / single-player). It must be authoritative (server-side) so a client
  cannot bypass it.
- Slice 1 needs a solid baseline filter, not perfect/locale-complete coverage.
  Live AI-generated content moderation is the heaviest burden and is explicitly
  last (README section 7, Phase 3) - not in scope here.
- Minimal data on minors: players are anonymous (code + nickname, no PII). No
  feature should capture more about a player than the in-session nickname.
