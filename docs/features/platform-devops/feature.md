# Feature: Platform & DevOps

## Summary
The foundation everything else builds on: monorepo structure, CI/CD, deployable
infrastructure, a test harness, and observability. Kept intentionally tiny
(README section 9). The walking-skeleton PR delivered its first cut; since then
the test harness (01), continuous delivery to UAT (03), and both observability
stories (04-05, PR #110) have shipped, and the separate dev-environment deploy
(02) was superseded by 03's single auto-provisioned UAT environment
(status refreshed 2026-07-07).

## README reference
README section 7 (Epic Map - Phase 0, Platform & DevOps) and section 9 (IaC -
"get it up first, keep it tiny").

## Stories
| Story | Issue | Title | Status |
|---|---|---|---|
| 01 | #18 | Test harness (Vitest + Playwright) | Complete |
| 02 | #19 | Deploy to a dev environment | Dropped (superseded by 03) |
| 03 | - | Continuous delivery to UAT on merge to main | Complete |
| 04 | #106 | Operational observability (App Insights) | Complete |
| 05 | #107 | Anonymous product-usage metrics | Complete |
| 06 | #189 | Repair the drifted e2e suite and gate it in CI | Complete |

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
