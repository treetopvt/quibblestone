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
  code, Guardian avatar selection at join
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
the tree honest (Slice 1 is what is ready to build now, look-ahead stories are
all Not Started).

## Next round - post-V1 look-ahead (Phase 2-4)

Slice 1 is about to reach V1, so this round of the backlog runs *ahead* of
development to carry the product vision (README sections 2-3, 5, 7, 10). These
features are fully specified (`feature.md` + `implementation.md` + stories) but
every story is **Status: Not Started, Issue: TBD** - they are the map for what
comes after V1, not work in flight. Grouped by intent:

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

**Monetization - donate-first, gate-ready (README section 3, CLAUDE.md section 6)**
- `accounts-identity/` - anonymous players forever; a lightweight purchaser
  account only at purchase (the auth hooks land early, before they are needed).
- `billing-entitlements/` - the entitlement seam (one check at session-creation,
  everything default-unlocked), the tip jar, Stripe, the first gated purchase,
  and restore/manage.
- `story-packs/` - the "Guardian's Vault" pack catalog, free-vs-locked gating on
  the seam, and the first hand-curated themed packs.

**Content moat (README sections 2, 7)**
- `ai-content-factory/` - the offline generate -> vet -> publish pipeline (never
  live to kids); the "cheap moat" that keeps the library bottomless.

The delight-tier epics that are still README backlog only (AI illustration, voice
narration, live on-demand generation) get their own folders when their phase comes
up - `billing-entitlements` already reserves their capability keys so gating them
later is a config flip.

## Templates

See README section 11 for the canonical `feature.md` and story (`NN-<slug>.md`)
templates; `docs/features/_template/` holds copy-from versions of those plus the
`implementation.md` template. The `story-agent` (`.claude/agents/story-agent.md`)
authors and maintains these files.
