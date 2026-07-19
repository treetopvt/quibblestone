<!--
  TEMPLATE: operate-stub.md - a PROPORTIONAL Operate stage. No project in the set has reached production,
  so this ships the minimum real artifact and NOTHING prod-shaped-as-if-proven. Scale up only when the
  project actually has prod + real users.
  Prose style: hyphens, colons, parentheses - never em dashes.
-->

# Operate (proportional)

The pipeline is not done at "merged" - it is done at "running, and reversible." But do not ship a
prod-shaped runbook you have not earned. This stub is the **minimum** and is all most pre-production
projects need.

## The minimum (ship this now)

1. **Pre-merge gates** - the machine gate exists and gates every stack on the PR (see `ci-minimal.yml`).
2. **Assume-green deploy** - a deploy workflow that runs off already-gated, already-green trunk. It does
   not re-run quality gates as a substitute for gating the PR.
3. **One-command rollback** - written down next to the promote step:
   ```
   # Roll back to the last-good release:
   <deploy-command> --ref <last-good-tag>        # e.g. redeploy the previous v* tag / artifact
   ```
   If you cannot state the rollback command, you do not have a rollback plan.
4. **A plaintext incident log** - one file per incident, append-only:
   ```
   docs/incidents/YYYY-MM-DD-<slug>.md

   - What happened:
   - When (detected / resolved):
   - Impact:
   - Fix:
   - Rollback used? (yes/no, which ref)
   - Follow-up (story/ADR to file after the fire is out):
   ```

## The rules

- **A production incident does NOT re-enter at Stage 1.** Branch from the current production tag, make
  the minimal fix, run the one gate that matters (fast checks + a targeted review), tag, promote.
  Backfill the story / ADR **after** the fire is out, never before.
- **The feature branch is not a home.** It rebases onto trunk at every wave boundary and lives no
  longer than the feature; anything expected to outlive a few waves integrates to trunk **behind a
  flag** instead.

## Scale up ONLY when you actually have production

Add these when there are real users, not before (each is unproven by any project in the set so far):

- feature flags to decouple deploy from release
- health checks + automated rollback triggers
- staged / canary rollout
- an on-call rotation and alerting

Until then, the four-item minimum above is the honest and sufficient Operate stage.
