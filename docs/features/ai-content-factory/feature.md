# Feature: AI Content Factory (back office)

## Summary
An offline, back-office pipeline that batch-generates candidate story templates
with an AI provider, routes every candidate through human-in-the-loop vetting
(the child-safety filter plus age/theme classification), and publishes only
approved templates into the content library. This is the "cheap moat" (README
section 2): it keeps the library bottomless without hand-writing every story,
and it keeps the solo builder unblocked on content. Nothing generated here ever
reaches a player unvetted, and nothing is generated live in front of a kid.

## README reference
README section 2 (Market Positioning - "a bottomless, fresh, AI-generated
content library... directly attacks the #1 complaint in the category: running
out of stories"), section 3 (add-on packs monetization consumes this library),
section 6 (Child Safety & Moderation - "a strong moderation pipeline before any
*live* AI generation is exposed to kids"), and section 7 (Epic Map - Phase 2,
"AI Content Factory - back office: batch-generate + vet + publish templates
offline. The cheap version of the moat; seed content even in Phase 1"). Also
section 4's "why one ASP.NET Core app instead of Azure Functions" callout,
which names async AI generation jobs as "the natural first candidates" to carve
out later.

## Stories
<!-- Status: Not Started | In Progress | Complete | Blocked | Dropped -->
| Story | Issue | Title | Status |
|---|---|---|---|
| 01 | TBD | Batch generation job | Not Started |
| 02 | TBD | Vetting / moderation queue | Not Started |
| 03 | TBD | Publish to library | Not Started |

## Dependencies
- template-model (the generation job's output must conform to the `Template` /
  `Blank` / `BlankCategory` schema from `template-model/01`; the published
  library speaks that same schema).
- child-safety (story 02 runs every candidate through `IContentSafetyFilter`,
  the one authoritative gate - it does not reimplement moderation logic).
- platform-devops (Key Vault must exist and be reachable for the AI provider
  key before story 01 can call a real provider).

`story-packs` depends on this feature in return: story-packs/03 (first themed
packs) is the first consumer of the published library, and later packs draw
from whatever this factory publishes.

## Design notes
- **This is an offline, back-office pipeline - not a player-facing feature.**
  There is no UI a player ever sees; the "UI" is an internal review queue for
  the builder (and, later, a human moderator) to work through candidates. Live,
  on-demand generation in front of a player is a *separate*, explicitly parked
  Phase-4 XL (README section 7) - see Parked below.
- The pipeline is a strict, one-directional gate: **generate -> vet -> publish**.
  A candidate cannot skip vetting. Nothing AI-generated is ever readable by a
  player before a human has approved it (README section 6). This is the
  headline constraint for the whole feature and shapes every story's ACs.
- The generation job (story 01) is the natural first candidate for an Azure
  Functions carve-out (README section 4: "the natural first candidates are
  async AI generation jobs"), because it is bursty, off the request path, and
  benefits from scale-to-zero. For the solo, nights-and-weekends build, an
  in-app background job (hosted service / scheduled task inside the existing
  ASP.NET Core app) is the pragmatic Slice-appropriate start; a Functions
  extraction is a later infra change, not a reason to block this feature. Say
  this explicitly in each story's Technical Notes so a future builder does not
  assume Functions is required to start.
- Generated candidates and published templates conform to the **same
  mode-agnostic schema** as the hand-written seed library (`template-model/01`
  / `/02`). The factory does not invent a parallel content shape - "one engine,
  many thin modes" only holds if every mode can play any template regardless
  of its origin (hand-written or AI-generated).
- The AI provider key lives in Azure Key Vault (README section 4: "Secrets:
  Azure Key Vault... AI provider keys"). It is never a `VITE_*` variable and
  never reaches the browser (CLAUDE.md section 4) - generation is entirely
  server-side / back-office.
- Vetting (story 02) reuses the **same** `IContentSafetyFilter` every
  free-text surface in the live game calls (child-safety/01) - it is not a
  second moderation implementation. On top of that pass/fail check, this
  story adds the human-in-the-loop review step and age/theme classification
  that the live per-word filter does not attempt (a whole template's tone,
  not just individual words, needs a human judgment call).
- Content is mutable, not a system of record (CLAUDE.md preamble): a published
  template can be edited or unpublished later without ceremony. Table Storage
  holds the library; there is no append-only audit trail requirement here.
- Publish (story 03) is the seam `story-packs` builds on: a themed pack is a
  curated grouping of library templates (some hand-written, some
  factory-published), so this feature's library is a prerequisite input to any
  pack beyond the hand-curated Slice-1-style seed set.

## Parked - Phase 2+
- **On-Demand AI Generation** (README section 7, Phase 3, sized XL - "a story
  about our dog Biscuit in space") is explicitly **not** this feature. It is
  live generation in front of a player at play time, with the heaviest
  moderation burden in the whole backlog (README section 7: "hence last").
  This feature only ever generates and vets *offline*, before anyone plays.
  Do not let this feature's scope creep toward exposing generation to players.
- AI illustration and AI voice narration generation (README section 7, Phase
  3) - a separate delight-tier pipeline, not templates/text.
- Automating the human review step away entirely (full auto-publish on filter
  pass) - the human-in-the-loop gate in story 02 is deliberate and
  non-negotiable per README section 6, not a placeholder to remove later.
- A public-facing "suggest a story idea" intake from players (Phase 4,
  demand-driven; would need its own moderation and rate-limiting design).
- Multi-provider generation / provider failover (start with one provider).

## Decisions
- 2026-07-01: Authored as a look-ahead feature ahead of Slice 1 shipping, per
  the "keep the backlog ahead of development" mandate. All three stories are
  Status "Not Started", Issue "TBD" - planned, not scheduled. They park behind
  Slice 1 shipping and behind `platform-devops`'s Key Vault provisioning
  (story 01 needs a reachable secret store for the provider key).
- 2026-07-01: Explicitly separated this feature from On-Demand AI Generation
  (README Phase 3 XL) so a future builder does not fold live generation into
  this back-office pipeline. The two share an AI provider but nothing else -
  different trust boundary, different moderation burden, different phase.
