# Story: Batch generation job

**Feature:** AI Content Factory (back office)  ·  **Status:** Not Started  ·  **Issue:** #78

## Context
Hand-writing every template gets old fast, and the #1 complaint about the
incumbent (README section 2) is running out of content. This story is the
first step of the "cheap moat": an offline job that calls an AI provider to
produce candidate templates in bulk, shaped to the existing content schema.
It produces **candidates only** - nothing here is visible to a player. See
[feature.md](./feature.md) and README section 2.

## Acceptance Criteria
- [ ] AC-01: Given the job is run, when it calls the configured AI provider,
      then it produces one or more candidate templates, each shaped to the
      `template-model` schema: a title/subject, ordered typed blanks (category,
      prompt, subHint, spark words), an optional word bank, and theme/age tags.
- [ ] AC-02: Given a candidate is produced, then it is stored with a distinct
      "candidate" / unreviewed status, separate from the published content
      library - it is not readable by any game mode, `story-packs`, or any
      player-facing route.
- [ ] AC-03: Given the AI provider key, then it is resolved from Azure Key
      Vault at runtime and is never present in `web/`, in a `VITE_*` variable,
      or in any payload sent to the browser.
- [ ] AC-04: Given the job is triggered, then it runs as an in-app background
      job (no player-facing HTTP route triggers generation) - an admin-only
      trigger (a manual invoke or a scheduled task) is acceptable for this
      story; there is no requirement to build a full scheduler.
- [ ] AC-05: Given the AI provider call fails or returns malformed output,
      then the job fails safely (logs the failure, produces no partial or
      malformed candidate) rather than writing a broken candidate into the
      queue.
- [ ] AC-06: Given a batch run, then every candidate carries provenance
      (generated-by-job, timestamp, provider) so the vetting queue (story 02)
      can display it and a reviewer knows what they are looking at.

## Out of Scope
- Vetting, moderation, age/theme classification, or human review (story 02).
- Publishing to the live content library (story 03).
- Any player-facing or live/on-demand generation (README Phase 3 XL,
  explicitly parked - see feature.md "Parked").
- A polished scheduling UI or multi-provider failover (start with one
  provider, triggered manually or on a simple schedule).
- Illustration or voice generation (a separate delight-tier pipeline).

## Technical Notes
- Project: `api/`. This is the natural first Azure Functions carve-out
  candidate per README section 4 ("the natural first candidates are async AI
  generation jobs... which genuinely benefit from event triggers and
  scale-to-zero") - but for this Slice, an in-app hosted/background job
  (`IHostedService` or a simple triggered job inside the existing single
  ASP.NET Core app) is the pragmatic starting point. Do not block this story
  on standing up Functions; that extraction is a later infra change.
- Candidate output must conform to the same shape as `web/src/engine/
  template.ts`'s `Template` / `Blank` / `BlankCategory` (mirrored
  server-side) - the factory does not invent a parallel schema. If the AI
  provider's natural output doesn't map cleanly, add a mapping/normalization
  step in this job rather than changing the schema.
- Isolate the provider-specific HTTP call behind a small client
  (`AiProviderClient.cs`-shaped) so a provider swap later (README section 12:
  "AI provider(s) for text... open decision") stays contained.
- Async all the way; no secrets in committed config (CLAUDE.md section 4).
  Key Vault reference resolved through the existing secrets configuration
  pattern established by `platform-devops`.

## Tests
No test harness is wired up yet for this pipeline's back-office code; note
the intended tests here so they exist once xUnit/`dotnet test` is available
(platform-devops/01 sets up the web-side Vitest/Playwright harness first).

| AC | Test |
|---|---|
| AC-01 | `api/tests/Content/ContentGenerationJobTests.cs` (planned) - asserts a mocked provider response maps to a valid candidate matching the schema shape; manual: run the job locally against a dev provider key and inspect stored candidates |
| AC-02 | manual: confirm no existing controller/hub route can read a candidate; a candidate is absent from any response the web client can reach |
| AC-03 | manual: grep the built `web/dist` bundle for the provider key value (must not appear); confirm the key is read via Key Vault configuration, not `appsettings.json` or `VITE_*` |
| AC-04 | manual: trigger the job via the admin-only path and confirm no player-facing route exists that starts it |
| AC-05 | `api/tests/Content/ContentGenerationJobTests.cs` (planned) - a mocked provider failure/malformed-response case produces zero candidates and a logged failure |
| AC-06 | manual: inspect a stored candidate record for provenance fields (job id or timestamp, provider name) |

## Dependencies
- template-model/01-template-schema (the schema candidates must conform to)
- platform-devops (Key Vault provisioned and reachable)
