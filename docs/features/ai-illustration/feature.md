<!--
  SKETCH (vision-level) - README Phase 3 delight tier. feature.md only: no story files or implementation.md yet.
  This folder is decomposed into stories when its phase comes up (docs/features/README.md stub convention).
  Use hyphens/colons/parentheses, never em dashes.
-->

# Feature: AI Illustration

## Summary
An AI-generated illustration of the finished tale - "the phone shows a picture
of our ridiculous story." It is the keepsake-and-share hook that earns
word-of-mouth: a tale you can look at, save, and send. A premium delight, gated
by a thin entitlement at session-creation; the free tier plays perfectly well
with text-only reveals. This file is a **sketch** (vision-level): feature.md
only - story files and implementation.md are authored when Phase 3 is decomposed.

## README reference
README section 2 ("AI illustrations ... the delight features that earn
word-of-mouth"), section 7 (Epic Map - Phase 3, "AI Illustration (L) - image of
the finished story (share / keepsake hook)"), section 4 (Blob Storage for
AI-generated illustrations; async AI jobs as the natural first Azure Functions
carve-out), section 6 (moderation before any AI output reaches kids), section 3
(premium tier).

## Stories (sketch - to be filed when Phase 3 is decomposed)
<!-- Status: Not Started | In Progress | Complete | Blocked | Dropped -->
| Story | Issue | Title | Status |
|---|---|---|---|
| 01 | TBD | Generate an illustration from the assembled tale | Not Started |
| 02 | TBD | Show the illustration on Reveal + keepsake | Not Started |
| 03 | TBD | Moderate image prompts and outputs before display | Not Started |
| 04 | TBD | Gate behind the ai.illustration entitlement | Not Started |

## Dependencies
- the-reveal (the assembled tale and the screen/tablet the image accompanies).
- keepsake-gallery (the illustration becomes part of the shareable/saved
  keepsake and the public tale page).
- billing-entitlements (the `ai.illustration` capability key, checked once at
  session-creation - the seam already reserves it).
- child-safety (image prompt + output moderation; family-safe toggle).
- ai-content-factory (reuses the offline generate -> vet plumbing for
  library/pack art where a human review path applies).
- infra (Blob Storage - already provisioned and currently unused per CLAUDE.md
  section 10; this feature is its first real consumer; Key Vault for the
  provider key).

## Design notes
- **Async, never inline.** Image generation is slow and costly; it must never
  block the reveal's laugh. Generate after the reveal (or pre-generate for
  library tales) as a background job (README section 4's Functions carve-out
  candidate) and show a tasteful "conjuring the picture..." state on the tablet.
- **Blob Storage, cached per tale.** Store one image per assembled tale under a
  deterministic key so re-viewing or re-sharing never regenerates (cost + speed).
  This finally gives the provisioned-but-unused Blob resource a consumer.
- **Moderation is the hard part and non-negotiable (README section 6).** BOTH
  the prompt derived from the tale AND the returned image must pass a safety gate
  before any child sees it; the family-safe toggle tightens style and content.
  Prefer a provider with strong built-in safety filters, and keep a human-review
  path for library/pack art (shared with ai-content-factory).
- **On brand.** The illustration lives inside the stone-tablet / keepsake frame;
  storybook-fantasy style matching the Guardians and the warm palette, not
  photoreal. Theme tokens frame it (`web/src/theme.ts`); FontAwesome for any
  chrome.
- **Entitlement at session-creation.** Gated via the billing-entitlements seam
  (`ai.illustration`); free/base tiers get text reveals, the image is a premium
  delight and never nags mid-play (kid-safe placement, no ads - README section 3).
- **Cost control.** Cache aggressively; consider generating only on an explicit
  "make a picture" tap rather than on every reveal.

## Parked - Phase 4+
- On-demand custom art direction ("draw it as a comic / in crayon").
- Consistent recurring character/mascot art across a family's tales.
- Animated or parallax illustration.
- A printable keepsake ("yearbook of tales") built from saved illustrations.

## Decisions
- 2026-07-01: Sketched as a vision-level feature.md only (Phase 3 is months out)
  during the look-ahead backlog pass; full stories + implementation.md are
  deferred until the phase is decomposed, per the docs/features stub convention.
  Recorded the load-bearing shape up front so decomposition starts from the
  charter's constraints: async background generation (a Functions candidate),
  Blob Storage with per-tale caching, moderation of both prompt and output
  before any child sees it, and gating via the existing `ai.illustration`
  capability key (never a new per-request check).
