<!--
  Implementation plan for the ai-content-factory feature. Bridges feature.md + stories to orchestration.
  Use hyphens/colons/parentheses, never em dashes.
-->

# Implementation Plan: AI Content Factory (back office)

> The bridge between planning and orchestration. The **Wave Plan** below is DAG-ready: the `orchestrate-feature`
> skill's Phase 1 validates and adjusts it rather than deriving it. See
> [`docs/FEATURE_ORCHESTRATION_PLAYBOOK.md`](../../FEATURE_ORCHESTRATION_PLAYBOOK.md).

This is a **server-only, back-office pipeline** (README section 2's "cheap moat", section 6's non-negotiable
moderation gate): generate -> vet -> publish, strictly one-directional, entirely inside `api/`. There is no player-
facing web surface in this feature at all - the "UI" is an internal review queue, in scope as a minimal server-
rendered or API-driven tool, not a themed player screen. Story 01 (generation) and story 03 (publish) both write to
the same Table Storage library, so they share a schema but are temporally serial (01 produces candidates, 02 gates
them, 03 writes only approved ones) - the whole feature is naturally a serial chain, not a fan-out.

## Reuse map

| Concern | Reuse | Where |
|---|---|---|
| Template / Blank / BlankCategory schema (candidates and published templates conform to this) | `template-model/01`'s mode-agnostic type | `web/src/engine/template.ts` (the TS shape mirrored server-side) |
| Content safety gate (vetting reuses this, does not reimplement it) | `IContentSafetyFilter`, registered once in DI (**child-safety/01**) | `api/src/Safety/IContentSafetyFilter.cs` |
| DI / composition root (register the generation job + vetting service) | the single app composition root | `api/src/Program.cs` |
| Secrets (AI provider key) | Azure Key Vault, provisioned by IaC (**platform-devops**) | `infra/main.bicep` (Key Vault resource); referenced server-side only, never `VITE_*` |
| Durable storage for candidates + published library | Azure Table Storage (README section 4: "templates and entitlements") | new `api/src/Content/` service layer (this feature establishes it) |
| Async background execution pattern | `IHostedService` / a scheduled job inside the existing single ASP.NET Core app (README section 4: Functions carve-out is a *later* infra change, not a Slice requirement) | `api/src/Content/` (this feature) |
| REST seam pattern (thin controller over an injected service, no logic of its own) | the existing `ModerationController` pattern | `api/src/Controllers/ModerationController.cs` (reference, not reused directly - different domain) |

What this feature **exports** that others import:
- The **content library** in Table Storage (approved, published templates) - the single source `story-packs` (and
  any future game-mode template picker) reads from, alongside the hand-written seed library.
- A **candidate/template schema mapping** server-side that mirrors `web/src/engine/template.ts`, so a published
  template is indistinguishable in shape from a hand-written one to any consumer.
- The **vetting queue** service/contract - not consumed by other Slice-1-adjacent features yet, but the seam
  `story-packs/03`'s "vetting pipeline (ai-content-factory/02 or the manual equivalent)" points at.

## Wave Plan (DAG)

Sizing rule: a builder owns files **disjoint** from its concurrent siblings. This feature is a serial pipeline by
nature (generate -> vet -> publish), so waves are mostly sequential rather than fanned-out; there is no parallelism
to exploit within the feature itself, only alongside other features.

| Story | Issue | Files it owns (footprint) | Depends-on | Can-run-with | Wave | Effort |
|---|---|---|---|---|---|---|
| 01 batch-generation-job | TBD | `api/src/Content/IContentGenerationJob.cs`, `api/src/Content/ContentGenerationJob.cs`, `api/src/Content/AiProviderClient.cs`, `api/src/Content/CandidateTemplate.cs`; edits `api/src/Program.cs` (DI + job registration) | template-model/01 (schema), platform-devops (Key Vault reachable) | story-packs/01 (other feature, disjoint files) | 1 | high |
| 02 vetting-queue | TBD | `api/src/Content/IVettingQueue.cs`, `api/src/Content/VettingQueue.cs`, `api/src/Controllers/ContentReviewController.cs`; edits `api/src/Program.cs` (DI) | ai-content-factory/01, child-safety/01 (`IContentSafetyFilter`) | none (serializes behind 01; shares `Program.cs` edits) | 2 | medium |
| 03 publish-to-library | TBD | `api/src/Content/IContentLibrary.cs`, `api/src/Content/TableStorageContentLibrary.cs`; edits `api/src/Program.cs` (DI) | ai-content-factory/02 | story-packs/01-02 (other feature, disjoint files) | 3 | medium |

**Concurrency per wave:** Wave 1 = 1 (generation job; can run alongside `story-packs/01`'s catalog-model work in the
other feature, since they touch disjoint files). Wave 2 = 1 (vetting; strictly behind 01, and it edits
`Program.cs` again so it cannot run concurrently with anything else touching that file). Wave 3 = 1 (publish;
behind 02). No wave in this feature has internal fan-out - each story is a single builder. All three stories edit
`api/src/Program.cs` (service registration), so they must **serialize relative to each other** even though they
are also logically serial by dependency.

## Per-story tech notes

### 01 - Batch generation job
- **Approach:** an offline job (not reachable by any player-facing route) that calls an AI text-generation provider
  to produce candidate templates shaped to the `template-model` schema (title/subject, ordered typed blanks with
  category/prompt/subHint/sparkWords, optional word bank, theme/age tags). The provider key is resolved from Key
  Vault at startup/runtime, never hardcoded and never a `VITE_*` value. Output is written as **candidates**, not
  published templates - it has zero path to a player-visible surface. Run it as an in-app hosted/background job to
  start (README section 4: Functions is the natural *later* carve-out for this exact workload, not a Slice-1
  requirement) - trigger it manually (an admin-only endpoint or a scheduled task) rather than building a fancy
  scheduler up front.
- **Key files it owns:** `api/src/Content/IContentGenerationJob.cs` (the exported contract), `ContentGenerationJob.cs`
  (implementation), `AiProviderClient.cs` (the provider HTTP call, isolated so the provider is swappable later),
  `CandidateTemplate.cs` (the candidate DTO, a superset of `Template` carrying provenance + review status).
  Verbose header comment on each (CLAUDE.md section 4).
- **Exports:** `IContentGenerationJob`, `CandidateTemplate` - consumed by story 02 (the vetting queue reads
  candidates this job produced).
- **Gotchas:** this job **never** writes to the published library directly - it only ever produces candidates for
  story 02 to gate. Keep provider-specific logic isolated to `AiProviderClient.cs` so a provider swap (README
  section 12: "AI provider(s) for text... open decision") does not ripple through the job logic. No player-facing
  route calls this job, ever - that boundary is the whole point of the feature.

### 02 - Vetting / moderation queue
- **Approach:** a human-in-the-loop review surface (internal/admin, not a themed player screen) listing candidates
  from story 01, each already run through the **existing** `IContentSafetyFilter` (child-safety/01) as a first-pass
  automated gate, plus a lightweight age/theme classification pass and a family-safe tag assignment. A reviewer
  (the solo builder, or a future human moderator) can reject, edit, or approve each candidate. Nothing produced by
  story 01 is visible to `story-packs` or any game mode until it is approved here - this is the non-negotiable gate
  from README section 6.
- **Key files it owns:** `api/src/Content/IVettingQueue.cs` (the exported contract), `VettingQueue.cs`
  (implementation - reads candidates, calls `IContentSafetyFilter`, records reviewer decisions),
  `api/src/Controllers/ContentReviewController.cs` (the thin REST seam for the review UI, following the
  `ModerationController` pattern: no logic of its own, only shapes requests/responses around the injected
  services).
- **Exports:** `IVettingQueue`, the approved-candidate contract story 03 reads from.
- **Gotchas:** **reuse `IContentSafetyFilter`, do not reimplement matching logic** - this story adds
  classification and human review on top of it, it is not a second filter. The automated pass is necessary but not
  sufficient: a candidate that passes the word-level filter can still fail human review for tone, coherence, or
  age-fit (a whole-template judgment call the per-word filter was never designed to make). No candidate reaches
  `story-packs` or any template picker without an explicit human "approve".

### 03 - Publish to library
- **Approach:** approved candidates from story 02 are written into the content library (Table Storage), in the
  same shape as hand-written seed templates, so every downstream consumer (game modes, `story-packs`) treats
  AI-published and hand-written templates identically. Published templates are **versioned but mutable** (CLAUDE.md
  preamble: this is a toy, not a system of record) - editing or unpublishing later is a normal operation, not a
  ceremony.
- **Key files it owns:** `api/src/Content/IContentLibrary.cs` (the exported contract), `TableStorageContentLibrary.cs`
  (implementation).
- **Exports:** `IContentLibrary` - the read seam `story-packs/01` (catalog model) and `story-packs/03` (first
  themed packs) both consume to pull published templates into pack groupings.
- **Gotchas:** publish is **additive and idempotent** - re-running publish for an already-published candidate must
  not duplicate it. The library schema must stay a strict superset-compatible match with `template-model`'s
  `Template` / `Blank` / `BlankCategory` so nothing downstream needs an "is this AI or hand-written" branch (one
  engine, many thin modes extends to content origin too - the engine should never care where a template came from).

## Cross-cutting concerns

- **Generate -> vet -> publish is strictly one-directional and every story upholds it.** No story in this feature
  (or in `story-packs`) may add a path that lets a story-01 candidate become visible to a player without passing
  through story 02's human approval. This is the single most important invariant in the feature.
- **This feature never runs live in front of a player.** All three stories are back-office / admin-only. If a
  future story proposes exposing generation, review, or publish to a player-facing route, that is a scope
  violation of README section 6 and section 7's Phase-3/4 boundary - flag it, do not build it here.
- **Secrets discipline:** the AI provider key lives in Key Vault, resolved server-side only. It must never appear
  in `web/`, in a `VITE_*` variable, in committed config, or in a candidate/template payload sent to the browser.
- **Schema fidelity:** every candidate and every published template conforms to `template-model/01`'s
  `Template`/`Blank`/`BlankCategory` shape. If generation or publish ever needs a schema field the engine doesn't
  have, that is a `template-model` change proposed there, not a parallel shape invented in this feature.
- **Inter-feature ordering:** `template-model/01` (schema) and `child-safety/01` (`IContentSafetyFilter`) must exist
  before this feature starts. `platform-devops`'s Key Vault provisioning must exist before story 01 can resolve a
  real provider key (a local/dev secret can stand in earlier, but the Key Vault seam should be wired from the
  start, not retrofitted - mirrors the "build the account hooks in early" discipline README section 3 applies to
  billing). This feature's story 03 must land before `story-packs/03` (first themed packs) can draw on
  AI-published content, though `story-packs/03` can still ship against hand-curated content alone if this feature
  is not yet done (see `story-packs/03`'s Out of Scope).
- **No i18n** (plain strings in any review-tool UI or messages). **No em dashes.**
