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
| 06 | - | Repair the drifted e2e suite and gate it in CI | Not Started |
| 07 | #TBD | Durable Data Protection key ring + token signing key posture | Not Started |
| 08 | #TBD | The second environment (beta rebadge + platform instance) | Not Started |

## Dependencies
- None for the Slice-1 stories (01-03) - this is the base of the stack.
- child-safety (stories 04-05: telemetry must be no-PII / no-content by
  construction, README section 6 - the audience is kids).
- infra Key Vault (story 04: the App Insights connection string is its first real
  consumer; CLAUDE.md section 10; story 07 adds a Key Vault KEY alongside the
  existing secrets, on the same already-provisioned vault).

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
- **Stories 07-08 (ADR 0003, 2026-07-08) are foundation-tier and independent of
  everything else in this feature.** Story 07 (durable Data Protection key ring +
  token signing key) closes a durability gap named at build time in
  `accounts-identity/03`/`/04` but deliberately deferred as a "billing-entitlements
  deployment follow-up" - it is reclassified here as platform-devops scope because
  it is infra + `Program.cs` wiring, not a billing concern. Story 08 (the second
  environment) is the mechanical half of ADR 0003 Decision 4's "UAT is rebadged
  beta; this work gets a second environment" - a parameter set against the
  existing `main.bicep`, never a template fork.

## Decisions
- 2026-07-08: [ADR 0003](../../adr/0003-admin-platform-and-family-accounts.md)
  (the admin platform - family accounts, the keepsake vault, the control plane,
  and the operator console) assigns this feature two Layer-0/Decision-4 items:
  story 07, the durable Data Protection key ring (Key Vault / Blob backed) so
  purchaser and operator credentials survive restarts and scale-out, plus
  automatic (not hand-set) provisioning of a durable `Accounts:TokenSigningKey`;
  and story 08, Decision 4's second environment - the existing UAT instance is
  rebadged "beta" (naming/docs only, the friends-and-family test runs there
  undisturbed), and a new instance provisions from the SAME `infra/main.bicep`
  via a new bicepparam + a parameterized deploy target, hosting the platform
  layers (identity spine, control plane) until they are ready to promote toward
  beta/production. Both stories are Wave 1 (independent of every other ADR 0003
  feature) per the ADR's cross-feature table; story 07's `Program.cs` edit
  shares that file's serial-merge hazard with several other Wave-1 stories in
  OTHER features (`accounts-identity/05`, `keepsake-vault/01`,
  `control-plane/01`, `sysadmin-console/04`) - land it as its own small,
  promptly-rebased PR.
