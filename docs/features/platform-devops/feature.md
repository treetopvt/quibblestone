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

## Dependencies
None. This is the base of the stack.

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
