<!--
  PARTIALLY DECOMPOSED - README Phase 4 delight tier (the XL, ships LAST) OVERALL, but stories 02 + 05 (the
  AI word-bank jumble and its moderation) are PULLED FORWARD as the cheapest/safest AI slice that proves the
  shared AI cost gate (ROADMAP horizon 3; ADR 0001). Those two are now buildable story files + this feature has
  an implementation.md scoped to them. Stories 01, 03, 04 (whole-template generation, verdict, entitlement) stay
  sketch rows until Phase 4 proper. Use hyphens/colons/parentheses, never em dashes.
-->

# Feature: On-Demand AI Generation

## Summary
The magic, and the heaviest child-safety burden: a player asks for a bespoke tale
("a story about our dog Biscuit in space") and the engine generates a fresh
template (typed blanks) live. Because it puts LIVE AI output in front of kids, it
ships **last**, behind the strongest moderation, at the top of the paid tier.
This feature also houses the optional, playful **"AI Guardian's Verdict"** - an AI
second-opinion judge for Versus / solo play. It is **partially decomposed**: the
lightest payload (stories 02 + 05, the AI word-bank jumble and its moderation) is
pulled forward as buildable stories - the first AI slice that proves the shared
`ai-cost-gate` - while the heavier stories (01 whole-template generation, 03
verdict, 04 entitlement) stay vision-level sketch rows until Phase 4 proper.

## README reference
README section 2 ("a bottomless, fresh, AI-generated content library ... the
retention engine" / the moat), section 7 (Epic Map - Phase 4, "On-Demand AI
Generation (XL) - ... Heaviest moderation burden, hence last"), section 6 ("a
strong moderation pipeline before any live AI generation is exposed to kids"),
section 4 (Key Vault provider keys; async generation as a Functions carve-out),
section 3 (top paid tier).

## Stories
<!-- Status: Not Started | In Progress | Complete | Blocked | Dropped -->
| Story | Issue | Title | Status |
|---|---|---|---|
| 01 | TBD | Generate a template live from a player prompt (sketch - Phase 4) | Not Started |
| 02 | #127 | [Live moderation gate](./02-live-moderation-gate.md) (buildable; jumble-scoped now) | Not Started |
| 03 | TBD | AI "Guardian's Verdict" - optional playful funniest-pick judge (sketch - Phase 4) | Not Started |
| 04 | TBD | Gate behind the ai.onDemand entitlement (sketch; see `ai-cost-gate/02`) | Not Started |
| 05 | #126 | [Generate word-bank options on demand](./05-ai-word-bank-jumble.md) (buildable; the jumble backend) | Not Started |

## Dependencies
- template-model (generated output conforms to the existing template schema -
  typed blanks, tags - so the engine plays it unchanged).
- game-modes (a generated template is played by the existing engine and modes;
  story 03's judge relates to the parked game-modes Versus/Duel mode - see
  [`docs/features/game-modes/feature.md`](../game-modes/feature.md) "Parked -
  Phase 2+/3").
- ai-content-factory (reuses the SAME generate -> vet plumbing and moderation
  queue - on-demand is the "live, self-service" sibling of the offline factory,
  not a fork of it).
- billing-entitlements (the `ai.onDemand` capability key at session-creation,
  likely the top subscription tier - the seam already reserves it).
- child-safety (the crux - live moderation of prompt and output).
- the-reveal (a generated tale is revealed like any other).

## Design notes
- **Why it ships LAST (README section 7).** Live AI output to children is the
  highest-risk surface in the whole product. The offline `ai-content-factory`
  proves the generate -> vet -> publish pipeline with a human in the loop;
  on-demand only becomes safe once that moderation is strong enough to run
  WITHOUT a human reviewing each request. Do not pull this forward.
- **Moderation IS the feature (story 02).** The player PROMPT and the GENERATED
  template must both pass automated safety BEFORE the template is playable or
  visible; the family-safe toggle tightens it further. On a failed prompt,
  refuse-and-retry with a friendly message - never explain the rejection in a way
  that teaches evasion. Sample/log for ongoing human audit.
- **Reuse the factory, do not fork it.** On-demand shares its generation and
  moderation code with `ai-content-factory` (offline). The only differences are
  the trigger (a live player request vs. a batch job) and the absence of a
  per-item human gate - which is exactly why the automated gate must be that much
  stronger. Keep the shared pipeline in one place.
- **Abuse and cost pressure appear HERE first.** A live, paid, generative surface
  needs rate limits and quotas. Note the seam: the entitlement gate answers
  "unlocked / not" at session-creation; how-many-generations-are-left is a
  separate metering concern (not per-request entitlement checks - that stays a
  smell per billing-entitlements).
- **AI "Guardian's Verdict" (story 03) - answering "have the AI score it?".** For
  group Versus (the parked game-modes Versus/Duel mode) the ROOM vote is the
  canonical judge - the whole point is people laughing together, not an
  algorithm's verdict. The Guardian's Verdict is an OPTIONAL, non-authoritative
  "second opinion", and - importantly - the only judge available in SOLO play,
  where there is no room to vote: it lets a solo player duel and get a laugh
  plus a one-line reason. It is a lightweight TEXT use of the same AI plumbing,
  and the reason it prints is generated text, so it must itself pass the safety
  filter. Never framed as authoritative, never a score. Cross-reference
  [`docs/features/game-modes/feature.md`](../game-modes/feature.md) "Parked -
  Phase 2+/3" for the Versus/Duel mode itself.
- **Entitlement at session-creation** via the billing-entitlements seam
  (`ai.onDemand`), top tier. Free and base tiers play the curated library +
  packs; on-demand is the premium magic. Kid-safe, no ads.
- **Word-bank options on demand (story 05) - the lightest live-generation use,
  and the backend for `game-modes/07`'s "jumble".** Instead of a whole template,
  this generates a small set of fresh, on-theme/on-brand WORDS for one blank's
  category, so a Word Bank player who dislikes the options can jumble for new
  ones. It is the same "generate then moderate before any child sees it" pipeline
  as story 01/02 (never a fork), just a tiny payload - which makes it a good
  early, lower-risk proving ground for the live pipeline. The consuming UX,
  and a FREE deterministic (non-AI) reshuffle fallback from the curated pool,
  live in `game-modes/07` (see its cross-reference); this feature owns only the
  AI generation + moderation of the words. Cross-reference
  [`docs/features/game-modes/07-word-bank-jumble.md`](../game-modes/07-word-bank-jumble.md).

## Parked - beyond Phase 4
- Fully-custom "make me a whole themed pack" (on-demand text + illustration +
  voice combined into a bespoke pack).
- User-tunable generation parameters (length, spiciness within family-safe,
  reading level).
- Saving/publishing a player-generated tale into the SHARED library - that would
  route back through `ai-content-factory`'s human moderation queue, never
  auto-publish.

## Decisions
- 2026-07-01: Sketched as a vision-level feature.md only (Phase 4, the furthest
  out) during the look-ahead pass; stories + implementation.md deferred to phase
  decomposition per the stub convention. Recorded three load-bearing constraints
  so decomposition starts correctly: (1) it reuses `ai-content-factory`'s
  generate + moderate pipeline rather than forking it; (2) live moderation
  without a per-item human gate is the prerequisite that makes it safe, which is
  why it ships last; (3) the AI "Guardian's Verdict" judge is optional and
  non-authoritative and primarily a SOLO affordance - the in-room human vote
  stays canonical for group Versus (the parked game-modes Versus/Duel mode),
  with async "share it to an outsider to judge" parked (it adds latency, a
  public surface, and moderation load the room vote avoids).
- 2026-07-02: Added sketch story 05 (generate word-bank options on demand) as the
  AI backend for `game-modes/07`'s "jumble", surfaced from play (a Word Bank
  player wanting fresh options). Recorded that it reuses the SAME generate +
  moderate pipeline (never a fork), is the lightest-payload live-generation use
  (a handful of category words, not a whole template) and so a good early proving
  ground, and that the consuming UX plus a FREE deterministic reshuffle fallback
  live in `game-modes/07` - this feature owns only the moderated AI generation.
  Still a sketch (no story file / implementation.md) until Phase 4 decomposition.
- 2026-07-02: **Pulled stories 05 and 02 forward from sketch into buildable stories**
  (with an `implementation.md` scoped to them) as the first AI slice that proves the
  shared `ai-cost-gate` on the cheapest/safest payload (ROADMAP horizon 3). Story 05
  (the AI word-bank jumble) rides the gate's proxy + quota + breaker + moderation and
  falls back to `game-modes/07`'s free reshuffle; story 02 (moderation) is scoped to
  the generated-WORD payload now (composing the gate's moderate-before-display seam),
  with the heavier prompt + whole-template moderation left shaped for story 01 to
  extend later. Provider = Azure AI Foundry / gpt-4o-mini; moderation = existing filter
  now, Content Safety later; runtime = in-app proxy; alpha = AI jumble free for all,
  quota/breaker-gated not entitlement (see [ADR 0001](../../adr/0001-ai-provider.md)).
  Stories 01, 03, 04 stay sketch rows until Phase 4 proper - this feature overall still
  ships last, but its lightest payload leads so the gate is proved before the heavy
  ones arrive.
