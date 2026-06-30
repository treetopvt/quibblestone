# docs/features - the backlog, as code

Quibbler's backlog lives **in the repo** (docs-as-code, README section 11) so
stories version and travel through pull requests alongside the code that
satisfies them. Each story file is a single PR-sized unit - the natural thing to
hand a coding agent one at a time.

## Layout

```
docs/features/
  <feature-slug>/
    feature.md         what the feature is + why (links back to a README section)
    01-<story>.md      order-prefixed story files, in build sequence
    02-<story>.md
```

- One folder per feature.
- Each feature folder has a `feature.md` plus one markdown file per story.
- **Story files are order-prefixed** (`01-`, `02-`, ...) so a feature reads
  top-to-bottom in build order.

## What is here now

These folders are the **Phase 0-1 / Slice 1** features (README sections 7-8) -
the thin vertical slice that gets the family laughing in a car:

- `platform-devops/` - CI/CD, environments, IaC (this scaffold is its first cut)
- `session-engine/` - SignalR backbone: create room, join code, roster
- `template-model/` - templates, typed blanks, optional word banks
- `game-modes/` - the "one engine, many thin modes" abstraction + Classic blind
- `single-player/`
- `group-play/` - host controls, blank distribution, collection
- `the-reveal/` - animated text reveal + host-read-aloud
- `child-safety/` - profanity filter, family-safe toggle (cross-cutting)

They are **empty on purpose**: story files (and `feature.md`) are written in a
**separate backlog pass**, decomposing one slice at a time. Phase 2-4 feature
folders (accounts, billing, AI content/illustration/voice, add-on packs) get
created when their phase comes up, so the tree stays honest about what is
actually ready to build.

## Templates

See README section 11 for the canonical `feature.md` and story (`NN-<slug>.md`)
templates. The `story-agent` (`.claude/agents/story-agent.md`) authors and
maintains these files.
