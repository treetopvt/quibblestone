<!--
  Implementation plan for the child-safety feature. Bridges feature.md + stories to orchestration.
  Use hyphens/colons/parentheses, never em dashes.
-->

# Implementation Plan: Child Safety & Moderation

> The bridge between planning and orchestration. The **Wave Plan** below is DAG-ready: the `orchestrate-feature`
> skill's Phase 1 validates and adjusts it rather than deriving it. See
> [`docs/FEATURE_ORCHESTRATION_PLAYBOOK.md`](../../FEATURE_ORCHESTRATION_PLAYBOOK.md).

This is a **foundation feature** (README section 6 - non-negotiable, designed in from the start). Story 01 is the
single server-side safety primitive that every other feature's free-text surface calls; it has no dependencies and
should land in the very first foundation wave. Story 02 (family-safe toggle) gates curated content and therefore
waits on `template-model/01` (it reads the tags).

## Reuse map

| Concern | Reuse | Where |
|---|---|---|
| API hub (calls the filter on every hub-borne free-text submission) | the one in-app SignalR hub | `api/src/Hubs/GameHub.cs` |
| DI / composition root (register the filter as a service) | the single app composition root | `api/src/Program.cs` |
| Content tags (family-safe gate reads theme/age tags) | **template-model/01** template schema | `web/src/engine/template.ts` (tag shape) + the seed data from `template-model/02` |
| Styling for the toggle control | the MUI theme + shared Button/Switch tokens (**design-system/01**) | `web/src/theme.ts` |
| Config | `import.meta.env` (`VITE_*`) | `web/src/vite-env.d.ts`, `web/.env.development` |

What this feature **exports** that others import:
- `IContentSafetyFilter` (server-side) - the contract `session-engine` (nicknames), `game-modes`, `group-play`, and
  `single-player` all route free text through. There is exactly one implementation; surfaces never reimplement it.
- The family-safe selection rule - the predicate `group-play/01-start-round` and `single-player/01` apply when
  offering templates / word banks.

## Wave Plan (DAG)

Sizing rule: a builder owns files **disjoint** from its concurrent siblings. Foundation first; the API/hub ->
consuming-web chain is serial (no codegen step - the hub signature is the contract).

| Story | Issue | Files it owns (footprint) | Depends-on | Can-run-with | Wave | Effort |
|---|---|---|---|---|---|---|
| 01 profanity-filter | #35 | `api/src/Safety/IContentSafetyFilter.cs`, `api/src/Safety/ContentSafetyFilter.cs`, `api/src/Safety/blocklist` (bundled data); edits `api/src/Program.cs` (DI registration) | none (foundational) | design-system/01-02, platform-devops/01-02, template-model/01 (all file-disjoint, other features) | 1 | medium |
| 02 family-safe-toggle | #36 | `api/src/Safety/FamilySafeContentSelector.cs`; `web/src/components/FamilySafeToggle.tsx`; edits `api/src/Program.cs` (DI) | child-safety/01, template-model/01 | template-model/02 (disjoint) | 2 | low |

**Concurrency per wave:** Wave 1 = 1 story in this feature (but it runs alongside the other foundation features -
its files are disjoint from theirs). Wave 2 = 1 story. Stories 01 and 02 both edit `api/src/Program.cs` (service
registration) and 02 calls 01, so they **serialize** - never give them to parallel builders.

## Per-story tech notes

### 01 - Profanity / safety filter on free text
- **Approach:** one service behind an interface `IContentSafetyFilter` with a single method that takes candidate
  text and returns a pass/fail result plus a friendly, non-shaming message on failure (AC-02). Registered in the DI
  container (`Program.cs`) so the hub and any future REST controller resolve the same instance (AC-05). The check is
  **server-side / authoritative** (AC-04) - the client may pre-validate for UX, but the server is the gate.
- **Key files it owns:** `api/src/Safety/IContentSafetyFilter.cs` (the exported contract), `ContentSafetyFilter.cs`
  (implementation), a bundled baseline blocklist resource. Verbose header comment on each (CLAUDE.md section 4).
- **Exports:** `IContentSafetyFilter` - imported by every free-text surface. No free-text surface ships its own
  word logic.
- **Gotchas:** keep the matching logic **pure** (string in -> verdict out) so it is unit-testable (Vitest covers
  the web engine; xUnit/`dotnet test` covers this once `platform-devops/01` wires a harness). Slice 1 is a solid
  baseline, **not** locale-complete coverage (Out of Scope). No AI moderation, no human-moderation queue. Async
  signature even if the baseline impl is synchronous, so a future remote/AI check is a drop-in.

### 02 - Family-safe toggle
- **Approach:** "the same filter, plus a content gate." A session-level boolean (default **on** - safe by default,
  AC-02) narrows the **offered/playable content set** to items tagged family-safe. It does **not** relax the story-01
  profanity filter, which always runs on free text (AC-04). Server-side `FamilySafeContentSelector` applies the
  predicate over the template/word-bank tags from `template-model`; the `FamilySafeToggle.tsx` control surfaces the
  setting where a host sets up a round and on the solo setup path.
- **Key files it owns:** `api/src/Safety/FamilySafeContentSelector.cs`, `web/src/components/FamilySafeToggle.tsx`.
- **Exports:** the selection predicate consumed by `group-play/01-start-round` (host template list) and
  `single-player/01` (solo template pick).
- **Gotchas:** it is a **session/host-level** setting decided at setup, not a per-request check (mirrors the
  monetization-seam discipline, README section 3). No granular age ratings, no per-player overrides (Out of Scope).
  Reads tags only - it does not own the tag shape (that is `template-model/01`).

## Cross-cutting concerns

- **This feature IS the cross-cutting safety primitive.** Every other feature's free-text surface (nicknames in
  `session-engine/02` + `/05`, blank answers in `game-modes/02`, `group-play/03`, `single-player/01`, the reveal in
  `the-reveal/01`) routes through story 01's filter before anyone sees the text. That is why story 01 is foundation
  wave and must merge before any consuming feature's free-text wave.
- **Inter-feature ordering:** `child-safety/01` has no prerequisites - schedule it in the first foundation wave with
  `design-system/01`, `platform-devops/01-02`, and `template-model/01` (all disjoint). `child-safety/02` requires
  `template-model/01` (tags), so it lands after that schema exists.
- **No PII:** players are anonymous (nickname + Guardian variant only). Nothing in this feature stores more about a
  player than the in-session nickname it is vetting.
- **No i18n:** the friendly rejection messages are plain strings (the stack has no translation layer); keep them
  brief and kid-readable.
- **No em dashes** in any authored content or messages.
