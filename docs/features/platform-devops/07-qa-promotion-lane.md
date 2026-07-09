# Story: QA lane + tag-based promotion to beta

**Feature:** Platform & DevOps  ·  **Status:** Complete (shipped 2026-07-08)

## Context
The app is about to open to beta testers on the current cloud environment, and a
large infrastructure overhaul is starting at the same time. Today there is ONE
cloud environment ("uat") that auto-deploys on every merge to `main`
(`.github/workflows/deploy.yml`, `on: push: branches:[main]`) - so the overhaul
would flow straight onto the testers' site with no way to shake it out first.
This story adds a second lane so `main` can land somewhere safe before it reaches
testers:
- **QA** = a NEW resource group `quibblestone-qa-rg` (its own Storage / Key Vault
  / App Insights, fully isolated data), auto-deployed on every merge to `main`.
  Its own isolated AI cost-gate footprint in a NEW AI resource group
  `quibblestone-ai-qa-rg` on the same PAYG subscription, with its own smaller
  monthly budget.
- **BETA** = the EXISTING `quibblestone-uat-rg` site (keeps `quibblestone.com`,
  all its wired secrets), left physically untouched and re-labelled "beta". It is
  deployed ONLY on a deliberate promotion, never automatically.
- Promotion from qa to beta is triggered by pushing a version tag (`v*`) -
  including the tag a GitHub Release creates; a `workflow_dispatch` with a `ref`
  input allows re-running/rolling back to an older tag.

Key decision: the existing site is NOT renamed (renaming would mean
re-provisioning + re-binding the custom domain right before testers arrive). It
stays physically `environmentName=uat` in `quibblestone-uat-rg`; "beta" is a
label/lane, and the beta deploy reuses the existing `uat` GitHub Environment (so
no new OIDC federated-credential subject or protection rules are needed for it).
Only qa needs a new GitHub Environment + a new federated subject. See
[feature.md](./feature.md).

## Acceptance Criteria
- [x] AC-01: Given the CORS/Stripe/email/AI wiring currently lives once inline in
      `deploy.yml`, then a reusable deploy workflow
      (`.github/workflows/deploy-env.yml`, `workflow_call`) parameterizes that
      logic by environment (`githubEnvironment`, `bicepEnvironmentName`,
      `resourceGroup`, `aiResourceGroup`, an optional `ref`) so the wiring lives
      in ONE place, not duplicated per lane.
- [x] AC-02: Given `deploy-qa.yml`, when a PR merges to `main`, then it deploys to
      qa automatically (auto-provisioning the qa footprint on first run, the same
      first-deploy behavior story 03 already gives uat today).
- [x] AC-03: Given `promote-beta.yml`, then it deploys to the beta site (the
      existing uat resources) ONLY on a version tag (`v*`) push (including the tag
      a GitHub Release creates), plus a manual `workflow_dispatch(ref)` for
      rollback - and it checks out the promoted `ref` so beta ships exactly the
      tagged commit.
- [x] AC-04: Given the env-parameterized Bicep (resource names already derive
      from `environmentName`), then qa is data-isolated from beta: its own
      resource group, Storage account, Key Vault, and App Insights.
- [x] AC-05: Given qa needs to load-test without risking beta's budget, then qa
      has its own AI cost-gate provider (its own Azure OpenAI account in
      `quibblestone-ai-qa-rg` and its own Cost Management budget), so qa/load
      testing never spends against or trips the beta $20 budget/breaker. The API
      managed-identity principal for the AI keyless grant is DISCOVERED at deploy
      time (`az webapp identity show`) rather than hardcoded per lane.
- [x] AC-06: Given a promotion runs, then beta behavior is unchanged from today -
      it inherits the existing repo-level GitHub vars, and qa overrides only the
      few that differ (e.g. `STRIPE_ENABLED`/`EMAIL_ENABLED` off for a minimal
      qa).
- [x] AC-07: Given cutover safety, then the qa lane is fully additive - the
      existing auto-deploy to beta continues until qa is proven, then the
      push-to-`main` trigger is removed from the beta path so beta only ever
      moves on a tag.

## Out of Scope
- Renaming the existing uat resources/RG to "beta" - kept physically uat to avoid
  re-provisioning + a domain re-bind right before testers arrive; can rename
  later in a quiet window.
- A pretty qa domain (`qa.quibblestone.com`) - qa uses the raw Static Web App
  hostname to start; a subdomain can be bound later with no code change.
- Stripe live mode and full billing wiring in qa - qa ships Stripe/email off by
  default; turn on per-need with qa-scoped vars + qa Key Vault secrets.
- The one-time Azure/GitHub bootstrap (create the two qa resource groups, grant
  the CI service principal Contributor + User Access Administrator on them, add
  the `environment:qa` federated credential subject, set qa environment vars) -
  owner-run, documented in the runbook
  `docs/runbooks/deploy-qa-and-promote-beta.md`.
- Production hardening already deferred elsewhere (VNet, Azure SignalR wiring,
  etc.).

## Technical Notes
- **Reusable core + two thin callers.** `deploy-env.yml` (`workflow_call`) holds
  the build/provision/discover/CORS/AI/Stripe/email/deploy steps parameterized by
  environment; `deploy-qa.yml` (`push: main`) and `promote-beta.yml` (tag `v*` +
  `workflow_dispatch(ref)`) call it with `secrets: inherit`. Per-lane
  concurrency: `deploy-qa` uses group `deploy-qa`; `promote-beta` uses group
  `deploy-uat` (matching the legacy workflow so they serialize against the same
  resources during the interim before cutover).
- **Env-scoped config.** The job sets `environment: <githubEnvironment>`, so
  `vars.*` (`STRIPE_*`, `EMAIL_*`, `APP_SERVICE_PLAN_SKU`,
  `AI_MONTHLY_BUDGET_USD`, etc.) resolve per-lane. Beta reuses the existing uat
  Environment and inherits today's repo-level vars unchanged; qa is a new
  Environment that overrides the minimal set.
- **Bicep needs no rewrite.** `infra/main.bicep` already composes names from
  `namePrefix` + `environmentName` + `uniqueString(rg)`, so a new
  `environmentName` (qa) is enough for isolation (AC-04). New param files
  `infra/main.qa.bicepparam` and `infra/ai.qa.bicepparam` mirror the uat ones for
  local/manual use (CI passes params inline). The AI provider file
  `infra/ai.bicep` is reused as-is; the core deploys it with inline params (model
  config from `ai.bicep` defaults) and a discovered `apiPrincipalId`, so no
  committed per-lane GUID.
- Runbook: `docs/runbooks/deploy-qa-and-promote-beta.md` (one-time bootstrap +
  the tag-to-promote operating procedure + the final cutover step).

## Dependencies
Story 03 (Continuous delivery to UAT on merge to main) - reuses its OIDC login,
tag-based resource discovery, and auto-provision approach. Relates to the AI cost
gate (`ai-cost-gate/06`) for the per-lane AI footprint.

## Delivered (as-built, 2026-07-08)
Shipped in PRs #192 (lanes) + #193 (qa-on-Playground fix) + #200 (cutover). Deltas from
the plan above, all forced by subscription limits found at deploy time:
- **qa's whole footprint runs on the Playground PAYG sub, not the student sub.** The
  student sub caps App Service at ONE plan (B1 quota 1, held by beta; F1 quota 0), so qa
  could not get an API host there. `deploy-env.yml` gained an `appSubscriptionId` input;
  `deploy-qa.yml` points it at Playground.
- **qa app resources are in `westus2`** (App Service quota on Playground is regional;
  `eastus2` = 0); qa AI stays in `eastus2`; qa runs **F1 ($0)**.
- **`Microsoft.SignalRService` had to be registered** on the Playground sub.
- **`qa.quibblestone.com`** was bound (Cloudflare CNAME via API + SWA custom domain +
  CORS `__1`) - the "pretty qa domain" listed Out of Scope above has since shipped.
- **Cutover done:** legacy `deploy.yml` removed (#200); first beta baseline tag `v0.1.0`.

Full operating + bootstrap detail: `docs/runbooks/deploy-qa-and-promote-beta.md`.
