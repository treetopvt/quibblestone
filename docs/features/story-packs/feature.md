# Feature: Story Packs (Guardian's Vault)

## Summary
Themed content packs (Holiday, Road-Trip, Spooky, and more later) browsable in
a themed catalog UI, the "Guardian's Vault": free packs always playable, locked
packs behind a friendly, kid-safe paywall. This is the answer to "ran out of
content" for the paying player and the add-on-pack half of monetization
(README section 3), built on the same mode-agnostic template schema every
other feature already speaks.

## README reference
README section 3 (Monetization - "Add-on packs: themed content (holiday,
sci-fi, road-trip edition) as an alternative or supplement to subscription.
Same billing plumbing") and section 7 (Epic Map - Phase 3, "Add-On Pack
Catalog"). Also section 2 (the content-velocity edge) and section 6 (child
safety applies to pack content exactly as it does to base content).

## Stories
<!-- Status: Not Started | In Progress | Complete | Blocked | Dropped -->
| Story | Issue | Title | Status |
|---|---|---|---|
| 01 | #75 | Pack catalog model + Guardian's Vault browser | Not Started |
| 02 | #76 | Free-vs-locked gating | Not Started |
| 03 | #77 | First themed packs (seed content) | Not Started |

## Dependencies
- template-model (a pack is a curated grouping of templates; it reuses the
  schema and tags, it does not invent a parallel content shape).
- design-system (the Vault browser is built from the shared theme, AppBar,
  Button, and Guardian components - it is not a bespoke look).
- child-safety (every pack, and every template inside it, is family-safe-toggle
  aware and vetted to the same standard as base content).
- billing-entitlements (story 02's paywall consumes the entitlement seam - a
  capability key `pack.<id>` checked at session-creation - authored in
  parallel; see Design notes).
- ai-content-factory (story 03's packs beyond the initial hand-curated set
  draw on the published library that feature produces; story 03 itself can
  ship against hand-curated content alone if ai-content-factory has not yet
  shipped - see 03's Out of Scope).

## Design notes
- **A pack is a grouping, not a new content type.** A story pack is a named,
  themed collection of existing-shape templates (`template-model`'s
  `Template`/`Blank`/`BlankCategory`) plus pack-level metadata (name, theme
  tag, age tag, free-or-locked, a hero illustration/icon). The engine never
  knows a template came from a pack versus the base library - "one engine,
  many thin modes" extends to content grouping the same way it extends to
  content origin (hand-written vs. AI-generated, see ai-content-factory).
- **Guardian's Vault** is the themed name for the pack browser, matching the
  Guardian mascot / stone-tablet motif already established (design-system).
  Each pack renders as a glowing stone tablet in a grid; a locked pack carries
  a gold-lock badge (gold `#FFB22E`, the CTA color, doing double duty as the
  "this is the thing to tap" signal even when locked) instead of hiding the
  pack entirely - browsing is not gated, playing is.
- **Free tier stays generous** (README section 3): base content (the Slice-1
  seed library and anything the family-safe toggle already covers) is never
  moved behind a pack. Packs are additive, themed supplements, not a
  repackaging of what used to be free.
- **Entitlement is a thin, session-creation-time check** (README section 3),
  never per-request. A locked pack's gate is checked once, when a host is
  choosing content to start a session (mirrors the family-safe toggle's
  session/host-level decision point, child-safety/02) - not re-checked on
  every blank or every reveal. Story 02 is explicit about what happens for
  free vs. paid players at that single decision point.
- **No ads, no dark patterns.** The locked-pack paywall routes to a purchase
  flow; it does not nag, does not time-pressure, does not partially reveal
  locked content to bait a purchase. This is a kid-facing product (README
  section 6) and README section 3 explicitly calls out avoiding the single
  most resented pattern in the category.
- **Packs persist in Table Storage** (README section 4: "Table Storage for
  templates and entitlements"), alongside the templates they reference and
  the entitlement records story 02 checks against.
- Slice-1's seed library (`template-model/02`) is the pattern story 03
  mirrors for its first hand-curated packs: small, hand-picked, family-safe,
  vetted before anyone plays them.

## Parked - Phase 2+
- On-demand / AI-personalized pack generation ("make me a pack about our
  family dog") - that is On-Demand AI Generation (README Phase 3 XL),
  explicitly separate and parked; packs in this feature are pre-built and
  pre-vetted, never generated live for a player.
- Pack search/discovery, ratings, or a recommendation surface (README section
  7, Phase 4 - "Content discovery/search... demand-driven").
- User-generated or community packs (README section 7, Phase 4, "optional UGC
  template creation").
- Bundling/cross-sell logic (buy 3 packs, get one free) - a pricing-strategy
  decision, not a content-model concern; layers onto the entitlement seam
  later without changing this feature's shape.
- Per-player pack ownership within a shared family/group plan (the
  entitlement model here assumes the purchaser's account gates the pack for
  their sessions, matching README section 3's "family plan" framing) - finer-
  grained sharing rules are a billing-entitlements concern, not this
  feature's.

## Decisions
- 2026-07-01: Authored as a look-ahead feature ahead of Slice 1 shipping, per
  the "keep the backlog ahead of development" mandate. All three stories are
  Status "Not Started", Issue "TBD" - planned, not scheduled. They park behind
  Slice 1 shipping, behind `design-system` (the Vault browser needs the theme
  and shared components), and behind `billing-entitlements` landing far enough
  to expose the `pack.<id>` capability-key seam story 02 checks.
- 2026-07-01: Scoped story 03 to work against hand-curated content
  independent of `ai-content-factory`'s completion, so this feature is not
  blocked end-to-end on that feature's pipeline - the same "tiny hand-written
  library first" discipline README section 8 used for Slice 1's base content.
- 2026-07-07: Correction to the first entry above: the three stories did not
  stay at Issue "TBD" - issues #75/#76/#77 were filed 2026-07-01 (see the
  Stories table).
