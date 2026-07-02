<!--
  QuibbleStone roadmap - the living view of where the build is and what comes next.
  Companion to docs/features/ (the backlog as code): every item here traces to a
  story there. Update the "as of" date and the Shipped / Open sections as work lands.
  Use hyphens/colons/parentheses, never em dashes.
-->

# QuibbleStone Roadmap

**As of 2026-07-02.** The thin vertical slice is live and playable end to end -
so this is no longer "get to playable," it is "what makes an alpha land, and how
do we bring AI in without a stranger running up the bill." Every path below traces
to a written story in [`docs/features/`](./features/); this file is the map over
that backlog, not new scope.

An interactive version of this map (same content, visual):
https://claude.ai/code/artifact/2e5c39ac-98e9-4afc-b7d4-1c06fbf677bd

## Guiding compass

- **Do not lose momentum before something is fun** (README section 8). The slice
  is fun now, so the bar shifts to "more laughs per round" and "does it travel."
- **Keep it a toy** - ephemeral, anonymous, family-safe by construction (README
  sections 4, 6).
- **New rule the moment AI enters:** meter every expensive call from day one
  (see "The AI cost gate" below).

## Where we are (shipped)

- Rooms, join codes, roster, Guardian avatars; solo + group play; the coral text
  reveal + round-complete recap.
- **Deployed** - a live dev environment + auto-deploy to UAT on merge.
- **Client routing** (react-router) + the `/join/:code` deep-link seam (PR #102).
- **Solo mode picker** - all four modes playable in SOLO (PR #97, `single-player/02`).
- **Freshness loop** - length classes, quick-story, no-repeats rotation.
- Profanity filter + family-safe toggle; MUI theme + shared components; Vitest +
  Playwright harness gating CI.

> Known gap: **group play is Classic-Blind-only.** The other three built modes are
> not yet reachable in a group - that is `group-play/05` (below), and it is mostly
> wiring (the engine, mode surfaces, solo registry, and a `Mode` field on the wire
> already exist).

## Open / near-done

| Item | Story | Note |
|---|---|---|
| Observability (App Insights) | `platform-devops/04` | crashes, errors, latency |
| Anonymous usage metrics | `platform-devops/05` | modes, session length; reuses 04's pipeline |
| Favorite a story | `story-selection/06` | device-local replay |
| Serve log | `story-selection/04` | what got played |
| Fresh Runes (free half) | `game-modes/07` | deterministic reshuffle, no AI |
| Group mode selection | `group-play/05` | host picks the mode for the room |
| Reconnect / rejoin | `session-engine` (deferred) | survive a dropped phone |

## The paths, by horizon

### 1. Finish the alpha (small wrap-ups)
- **Go Live, the last mile** - observability (`platform-devops/04`) + anonymous
  usage (`platform-devops/05`). Deployed already, but flying blind until telemetry
  lands. *(App Insights + IaC: I prep it; the Azure provisioning is yours to run.)*
- **Keep It Fresh, the last two** - favorite a story (`story-selection/06`) + serve
  log (`story-selection/04`).
- **Fresh Runes, free half** - the deterministic word-bank reshuffle (`game-modes/07`,
  non-AI layer).
- **Don't Lose the Room** - reconnect + rejoin (`session-engine`, deferred hardening).

### 2. Biggest bang, now
- **Modes in Group Play** (`group-play/05`) - host picks the mode; Classic Blind,
  Word Bank, Progressive Reveal. High value (the group is the whole point), mostly
  wiring. *Progressive Story is deferred - it needs a live cross-player "story so
  far" broadcast (its own story).*
- **Land the Laugh** (`reveal-delight/01-04`) - reactions, carving animation, word
  attribution, Golden Guardian. Best bang-for-buck, all on the built reveal.
- **Spread the Word** (`session-engine/06` + `keepsake-gallery/01-04`) - deep-link
  join, save/share the tale, public tale page. Routing already made the deep link
  cheap.

### 3. The AI question (explore sooner, gate the cost)
Prove the AI plumbing ONCE, on the cheapest/safest payload, behind a gate built
once and reused by every later AI feature.
- **Thin slice:** the **Fresh Runes AI jumble** (`game-modes/07` AI layer, backed by
  `ai-on-demand-generation/05`) - a tiny text payload, easy to moderate, with a
  non-AI fallback. What it teaches transfers straight to voices and on-demand tales.
- **The cost gate** (see the detailed section below) - built here, reused everywhere.

### 4. Beyond alpha (once the gate exists)
- **Full AI delight** - character voices (changeable), AI illustration, on-demand
  "a story about our dog in space" (`ai-voice-narration`, `ai-illustration`,
  `ai-on-demand-generation`). Same gate, bigger payloads, strongest moderation.
- **Charge for it** - purchaser account + tip jar -> Stripe -> gated purchase
  (`accounts-identity`, `billing-entitlements`). The cost gate is already half of it.
- **Bottomless library** - pack catalog + the offline generate -> vet -> publish
  content factory (`story-packs`, `ai-content-factory`).

## The AI cost gate

The hard part of AI is not any one feature - it is the shared plumbing (a provider,
moderation, cost control). Build this gate once; every AI feature inherits it.

1. **Server-side only** - the provider key lives in Key Vault; the browser never
   calls AI directly, so every call is yours to see and throttle.
2. **Entitlement at session start** - one check when the room is created decides if
   this session gets AI (the `billing-entitlements` seam). Free tier gets a capped
   taste or the non-AI fallback.
3. **Rate limit + quota** - per-session and per-IP limits, plus an "N calls left"
   meter, so even an allowed session cannot spam.
4. **Spend circuit-breaker** - a daily budget ceiling; cross it and AI degrades
   gracefully to the deterministic fallback. Covers bugs and abuse alike.
5. **Moderate before display** - AI output is unvetted text; it passes the safety
   filter + family-safe before any child sees it. Non-negotiable (README section 6).

**The reframe:** AI does not wait for monetization, but it pulls the *gating seam*
forward. You build the cost-control seam now (for safety and cost) and attach real
charging to it later. Players stay anonymous - the gate meters **compute per
session, not identity**.

## Recommended sequence

1. **Done** - deploy, routing, solo modes, freshness loop.
2. **Now** - Land the Laugh + Group modes + the Keep-It-Fresh leftovers (favorite +
   serve log). All ride things already built.
3. **Then** - observability + anonymous usage (see how the alpha plays), then Spread
   the Word (let a shared tale travel).
4. **Early AI** - the Fresh Runes AI jumble behind the cost gate (proves the whole
   pipeline on the cheapest payload).
5. **Later** - voices, on-demand, packs, charging - all reuse the gate; let the alpha
   and the first AI slice tell you which comes first.

## Using this in an implementation session

1. Pick a card / row above and open its story under `docs/features/<feature>/`.
2. Branch per the git workflow in `CLAUDE.md` (a new branch per unit of work).
3. Build to the story's acceptance criteria; keep to the stack conventions.
4. Verify (`npm run build`, `npm run test:unit`, and `npm run test:e2e` where a flow
   is involved) before opening the PR.
