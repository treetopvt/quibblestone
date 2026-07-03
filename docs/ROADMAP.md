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
- **Freshness loop** - length classes, quick-story, no-repeats rotation; plus the
  anonymous serve log (`story-selection/04`) and device-local favorite-a-story
  replay (`story-selection/06`) - the whole "Keep It Fresh" arc is closed out.
- **Land the Laugh** - the reveal payoff polish (`reveal-delight/01-04`, PR #112):
  the reaction row, the word-by-word carving animation, the Golden Guardian
  funniest-word vote + next-round crown, and per-word "carved by" attribution.
- **Modes in Group Play** - the host picks the mode for the whole room and every
  player plays it (`group-play/05`, PR #116): Classic Blind, Word Bank, and
  Progressive Reveal, resolved through a shared mode registry (solo + group now
  consume one list). Progressive Story stays deferred (it needs a live cross-player
  "story so far" broadcast - its own story). Group play is no longer Classic-Blind-only.
- **Spread the Word** - a finished tale AND a live room now travel (`session-engine/06`
  + `keepsake-gallery/01-04`, PR #130): a tappable `/join/:code` deep-link share from
  the Lobby, save the reveal as a stone-tablet image, watermarked image share, a
  device-local "Tales we've carved" gallery, and the host-opt-in public tale link
  (server-side re-vetted, unguessable slug, noindex, TTL, per-IP rate limited). The
  public tale page stays disabled until Azure Table Storage is provisioned (the code
  ships behind a connection-string flag - see the keepsake-published-tales runbook).
- Profanity filter + family-safe toggle; MUI theme + shared components; Vitest +
  Playwright harness gating CI.

## Open / near-done

| Item | Story | Note |
|---|---|---|
| Observability (App Insights) | `platform-devops/04` | crashes, errors, latency |
| Anonymous usage metrics | `platform-devops/05` | modes, session length; reuses 04's pipeline |
| Fresh Runes (free half) | `game-modes/07` | deterministic reshuffle, no AI |
| Reconnect / rejoin | `session-engine` (deferred) | survive a dropped phone |

## The paths, by horizon

### 1. Finish the alpha (small wrap-ups)
- **Go Live, the last mile** - observability (`platform-devops/04`) + anonymous
  usage (`platform-devops/05`). Deployed already, but flying blind until telemetry
  lands. *(App Insights + IaC: I prep it; the Azure provisioning is yours to run.)*
- **Keep It Fresh** - done: the serve log (`story-selection/04`) and device-local
  favorite-a-story replay (`story-selection/06`) both shipped.
- **Fresh Runes, free half** - the deterministic word-bank reshuffle (`game-modes/07`,
  non-AI layer).
- **Don't Lose the Room** - reconnect + rejoin (`session-engine`, deferred hardening).

### 2. Biggest bang, now
- **Modes in Group Play** (`group-play/05`) - **DONE** (PR #116): the host picks the
  mode (Classic Blind, Word Bank, Progressive Reveal) so the room plays more than
  Classic Blind, over a shared mode registry. *Progressive Story is deferred - it
  needs a live cross-player "story so far" broadcast (its own story).*
- **Land the Laugh** (`reveal-delight/01-04`) - **DONE** (PR #112): reactions,
  carving animation, word attribution, Golden Guardian. All on the built reveal.
- **Spread the Word** (`session-engine/06` + `keepsake-gallery/01-04`) - **DONE**
  (PR #130): deep-link join, save/share the tale, device-local gallery, public tale
  page. The public tale page ships disabled until Azure Table Storage is provisioned
  (connection-string flag - see the keepsake-published-tales runbook).

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

> **Now planned (2026-07-02).** The gate + the first AI slice are decomposed into
> buildable stories, ready for an `/orchestrate-feature` session: the new
> [`ai-cost-gate`](./features/ai-cost-gate/feature.md) feature (6 stories: proxy,
> entitlement-at-session, quota/meter, spend circuit-breaker + attribution,
> moderate-before-display, IaC seam), the free reshuffle
> [`game-modes/07`](./features/game-modes/07-word-bank-jumble.md) (ships first, no
> AI), and the AI jumble [`ai-on-demand-generation/05`](./features/ai-on-demand-generation/05-ai-word-bank-jumble.md)
> + its moderation [`/02`](./features/ai-on-demand-generation/02-live-moderation-gate.md).
> Provider/model + cost decisions: [ADR 0001](./adr/0001-ai-provider.md) (Azure AI
> Foundry, gpt-4o-mini, in-app proxy, existing filter now + Content Safety later,
> AI jumble free-for-all in alpha behind quota + breaker). The cross-feature build
> order (gate foundation -> free jumble -> AI jumble) is in
> [`ai-cost-gate/implementation.md`](./features/ai-cost-gate/implementation.md).

## Recommended sequence

1. **Done** - deploy, routing, solo modes, freshness loop, Land the Laugh
   (`reveal-delight`, the reveal payoff polish), Modes in Group Play
   (`group-play/05`, the host mode picker - group play now runs all three
   real-time-safe modes), and Spread the Word (`session-engine/06` +
   `keepsake-gallery/01-04`, a shared tale and a live room both travel; the public
   tale page ships behind an Azure-provisioning flag).
2. **Now** - observability + anonymous usage (`platform-devops/04-05`, see how the
   alpha plays).
3. **Early AI** - the Fresh Runes AI jumble behind the cost gate (proves the whole
   pipeline on the cheapest payload).
4. **Later** - voices, on-demand, packs, charging - all reuse the gate; let the alpha
   and the first AI slice tell you which comes first.

## Using this in an implementation session

1. Pick a card / row above and open its story under `docs/features/<feature>/`.
2. Branch per the git workflow in `CLAUDE.md` (a new branch per unit of work).
3. Build to the story's acceptance criteria; keep to the stack conventions.
4. Verify (`npm run build`, `npm run test:unit`, and `npm run test:e2e` where a flow
   is involved) before opening the PR.
