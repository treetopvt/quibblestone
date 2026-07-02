# Feature: Platform & DevOps

## Summary
The foundation everything else builds on: monorepo structure, CI/CD, deployable
infrastructure, a test harness, and observability. Kept intentionally tiny
(README section 9). The walking-skeleton PR delivered its first cut; the
remaining Slice-1 work is a test harness and a clean deploy to a dev environment.

## README reference
README section 7 (Epic Map - Phase 0, Platform & DevOps) and section 9 (IaC -
"get it up first, keep it tiny").

## Stories
- [ ] 01 - Test harness (Vitest + Playwright)
- [ ] 02 - Deploy to a dev environment
- [ ] 03 - Continuous delivery to UAT on merge to main
- [ ] 04 - Operational observability (Application Insights)
- [ ] 05 - Anonymous product-usage metrics (game types, session length, approximate reach)

## Dependencies
- None for the Slice-1 stories (01-03) - this is the base of the stack.
- child-safety (stories 04-05: telemetry must be no-PII / no-content by
  construction, README section 6 - the audience is kids).
- infra Key Vault (story 04: the App Insights connection string is its first real
  consumer; CLAUDE.md section 10).

## Design notes
- **Already delivered by the walking-skeleton PR:** monorepo (`api/`, `web/`,
  `infra/`), one ASP.NET Core app hosting REST + SignalR, a React + Vite + MUI
  client, Bicep for the five-resource dev footprint, and GitHub Actions that
  build both projects. CI is green.
- Test strategy follows the architecture (README section 4): Vitest for the pure
  engine logic, Playwright for the scary 2-player real-time flow. See
  `.claude/agents/testing-agent.md`.
- Keep the footprint tiny - do not gold-plate. New resources or pipeline steps
  must earn their place.
- **Observability (stories 04-05) is the named-but-unspecified part of this
  feature's remit** (see Summary). It splits deliberately: story 04 is
  OPERATIONAL health (Application Insights - exceptions, failed requests, hub
  failures, latency; the "find and fix bugs" layer, low privacy surface), and
  story 05 is PRODUCT usage (anonymous game-type / session-length / approximate
  reach). The split keeps the child-privacy-sensitive usage tracking in its own
  tightly-scoped story. Both are **no-PII / no-content by construction** (README
  section 6) and reuse one App Insights pipeline plus the anonymous telemetry
  philosophy `story-selection/04` already set - not three parallel systems.
- **README phasing:** operational observability is Platform/DevOps scope (README
  section 7); product analytics is parked as "demand-driven" (README section 12).
  Story 05 is that demand now being pulled forward, deliberately, and kept anonymous.
