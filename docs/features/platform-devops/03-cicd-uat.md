# Story: Continuous delivery to UAT on merge to main

**Feature:** Platform & DevOps  ·  **Status:** Complete

## Context
Story 02 made the app deployable by hand. This story makes it deploy itself:
when an approved PR merges to `main`, the app ships to a **UAT** environment with
no manual steps. UAT is the only cloud environment for now (README section 9 -
keep the footprint tiny); more environments earn their place later. Cost is kept
at $0 to stand up by defaulting the one paid resource (the App Service Plan) to
its Free SKU, with a one-input path to scale up. See [feature.md](./feature.md).

## Acceptance Criteria
- [ ] AC-01: Given the OIDC federated credential + three repo secrets are set,
      when a PR is approved and merged to `main` for the first time (or the
      Provision UAT workflow is run), then the resource group and the
      five-resource footprint are created without stored credentials - no
      separate manual provisioning step.
- [ ] AC-02: Given UAT exists (auto-provisioned on the first run), when a PR is
      approved and merged to `main`, then the Deploy workflow runs automatically
      and publishes the API to App Service and the web client to the Static Web
      App.
- [ ] AC-03: Given the deploy, then the pipeline discovers the resource names and
      URLs from the resource group at run time (no hand-copied publish profiles,
      tokens, or URL variables) and sets the API CORS origin to the web origin.
- [ ] AC-04: Given the deployed web build, then it points at the deployed API URL
      (not localhost), sourced from build-time config discovered in AC-03.
- [ ] AC-05: Given cost minimization, then the App Service Plan defaults to F1
      (Free, $0) and can be scaled to B1+ by re-running Provision UAT with a
      different `sku` input (no code change).

## Out of Scope
- Environments beyond UAT (prod, staging) - added when there is something to
  promote.
- A manual approval gate before UAT deploy - chosen as auto-on-merge; a required
  reviewer can be added on the `uat` GitHub Environment later without code
  changes.
- Production hardening (VNet, private endpoints, diagnostics) and wiring the app
  to Azure SignalR Service - still deferred (the in-process hub is fine for UAT).
- The one-time Azure credential bootstrap itself (owner-run, needs Azure Owner);
  documented in the runbook.

## Technical Notes
- **Auth: OIDC federated credentials**, not stored secrets. `azure/login@v2`
  exchanges a GitHub-issued token; the only repo config is `AZURE_CLIENT_ID`,
  `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` (identifiers, not passwords).
- **Provision** (`.github/workflows/provision.yml`, `workflow_dispatch`): runs
  `infra/main.bicep` with an inline `appServicePlanSku` so the SKU input scales
  the plan. Idempotent - re-run to change SKU or region.
- **Deploy** (`.github/workflows/deploy.yml`, `on: push: main` +
  `workflow_dispatch`): build+test gate, then discover resources (tagged
  `app=quibblestone`) in the group, set CORS, deploy API via
  `azure/webapps-deploy` (OIDC session, no publish profile), build web with the
  discovered `VITE_API_BASE_URL`, deploy web via the SWA action with a token
  fetched at run time.
- **Cost lever:** `appServicePlanSku` in `infra/main.bicep` (default `F1`).
  `alwaysOn` + `/health` check switch on automatically on paid tiers.
- Runbook: [docs/runbooks/deploy-to-uat.md](../../runbooks/deploy-to-uat.md).

## Dependencies
Story 02 (Deploy to a dev environment) - reuses its Bicep and smoke-check
approach.
