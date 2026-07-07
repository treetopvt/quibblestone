# docs/features - the backlog, as code

QuibbleStone's backlog lives **in the repo** (docs-as-code, README section 11) so
stories version and travel through pull requests alongside the code that
satisfies them. Each story file is a single PR-sized unit - the natural thing to
hand a coding agent one at a time.

## Layout

```
docs/features/
  _template/           copy-from templates (feature.md, NN-story.md, implementation.md)
  <feature-slug>/
    feature.md         what the feature is + why (links back to a README section)
    implementation.md  the bridge to orchestration: reuse map + DAG-ready Wave Plan
    01-<story>.md      order-prefixed story files, in build sequence
    02-<story>.md
```

- One folder per feature.
- Each feature folder has a `feature.md`, an `implementation.md`, plus one markdown
  file per story.
- **Story files are order-prefixed** (`01-`, `02-`, ...) so a feature reads
  top-to-bottom in build order.
- **`implementation.md`** is the planning -> orchestration bridge (per-story tech
  notes + reuse map + a file-footprint Wave Plan). It is what the
  `orchestrate-feature` skill reads to fan out parallel builders; see
  `docs/FEATURE_ORCHESTRATION_PLAYBOOK.md`. Required for every fully-specified
  feature.

## What is here now

These folders are the **Phase 0-1 / Slice 1** features (README sections 7-8) -
the thin vertical slice that gets the family laughing in a car:

- `platform-devops/` - CI/CD, environments, IaC (this scaffold is its first cut)
- `design-system/` - MUI theme from brand tokens, AppBar + Button contracts,
  Guardian avatar component; UI prerequisite for all screen stories
- `session-engine/` - SignalR backbone: create room, join code, roster, copy/share
  code, Guardian avatar selection at join; later grew a deep-link join share (06),
  the "Don't Lose the Room" reconnect/rejoin hardening pass (07-10), and the
  roster "+ invite" slot wiring (11)
- `template-model/` - templates, typed blanks, category/prompt/spark model,
  optional word banks
- `game-modes/` - the "one engine, many thin modes" abstraction + Classic blind
- `single-player/`
- `group-play/` - host controls, blank distribution, collection, waiting
  interstitial, round-complete replay loop
- `the-reveal/` - coral-highlighted text reveal
- `child-safety/` - profanity filter, family-safe toggle (cross-cutting)

Each is **now specified** with a `feature.md` and order-prefixed story files -
this was the separate backlog pass that decomposes one slice at a time. The
**Next round** section below then runs a deliberate look-ahead pass over the
Phase 2-4 features so the backlog stays ahead of development; Status fields keep
the tree honest. (2026-07-07: when this page was written that meant "Slice 1 is
ready to build now, look-ahead stories are all Not Started" - since then Slice 1
has shipped and many of the look-ahead features have been decomposed, issued, and
built; each story file's own Status field is the source of truth, not this
snapshot.)

## Next round - post-V1 look-ahead (Phase 2-4)

Slice 1 is about to reach V1, so this round of the backlog runs *ahead* of
development to carry the product vision (README sections 2-3, 5, 7, 10). These
features are fully specified (`feature.md` + `implementation.md` + stories); at
authoring time every story was **Status: Not Started, Issue: TBD**, the map for
what comes after V1. (Corrected 2026-07-07: that snapshot is stale - for example
`story-selection` has shipped, `story-packs` carries filed issues #75-#77 and
`ai-content-factory` #78-#80, and several other groups below are partly or fully
built. Check each story file's Status.) Grouped by intent:

**Playability - a funnier payoff (README sections 5, 10)**
- `game-modes/` (extended) - the remaining modes on the one engine: Progressive
  reveal ("Whisper"), Blind + word bank, Owner-curated bank, and Versus/Duel
  (the one honest engine stretch: many answers per blank + a room vote).
- `reveal-delight/` - the reaction row, the word-by-word "carving" animation, and
  the light "Golden Guardian" funniest-word award (no leaderboard - it stays a
  toy, not a competition).

**Retention & growth (README sections 1-2, 7)**
- `replay-remix/` - "Carve it again", one-blank remix, and rotating host ("Pass
  the chisel") - cheap replay on top of the existing round-complete loop.
- `keepsake-gallery/` - save a reveal as a shareable stone-tablet image
  (watermarked - the non-ad growth loop) plus a device-local "Tales we've carved"
  history.
- `story-selection/` - length-aware story picks (quick tale vs full tale),
  freshness rotation (no repeats until the pool runs dry, explicit replay
  excepted), an anonymous serve log, and thumbs up/down tale feedback - the
  loop that keeps the library feeling fresh and tells content creators which
  tales land.

**Monetization - donate-first, gate-ready (README section 3, CLAUDE.md section 6)**
- `accounts-identity/` - anonymous players forever; a lightweight purchaser
  account only at purchase (the auth hooks land early, before they are needed).
- `billing-entitlements/` - the entitlement seam (one check at session-creation,
  everything default-unlocked), the tip jar, Stripe, the first gated purchase,
  and restore/manage.
- `story-packs/` - the "Guardian's Vault" pack catalog, free-vs-locked gating on
  the seam, and the first hand-curated themed packs.
- `sysadmin-console/` - the separate, auth-gated operator back office (added
  2026-07-07 to this listing; see ADR 0002): magic-link operator login against a
  Key Vault allowlist in its own bundle (never the kid PWA), grant/revoke an
  entitlement by purchaser email, and report -> auto-hide -> operator review of
  public tales.

**Content moat (README sections 2, 7)**
- `ai-content-factory/` - the offline generate -> vet -> publish pipeline (never
  live to kids); the "cheap moat" that keeps the library bottomless.

**Delight tier - sketched (README Phase 3-4)**

The word-of-mouth / premium-AI epics now have vision-level `feature.md` sketches
(no story files or `implementation.md` yet - they are decomposed when their phase
comes up). `billing-entitlements` already reserves their capability keys so gating
them later is a config flip.
- `ai-illustration/` - an AI image of the finished tale (keepsake + share hook).
- `ai-voice-narration/` - TTS character voices; wires the reveal's
  already-reserved narration bar (the car-ride killer feature).
- `ai-on-demand-generation/` - live "a story about our dog in space" generation
  (ships last, heaviest moderation) plus the optional AI "Guardian's Verdict"
  funniest-pick judge for solo/Versus.

Keepsake Gallery also gained story 04 (a shareable tale back-link with a "Play
QuibbleStone" CTA) so a shared tale converts a viewer into a new player, not just
a brand impression.

## Templates

See README section 11 for the canonical `feature.md` and story (`NN-<slug>.md`)
templates; `docs/features/_template/` holds copy-from versions of those plus the
`implementation.md` template. The `story-agent` (`.claude/agents/story-agent.md`)
authors and maintains these files.
