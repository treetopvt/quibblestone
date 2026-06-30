# Story: Deploy to a dev environment

**Feature:** Platform & DevOps  ·  **Status:** Not Started

## Context
"Deploys cleanly to dev" is the IaC bar (README section 9). The skeleton has the
Bicep and a deploy workflow; this story actually stands up the dev environment
and makes the app reachable in the cloud. See [feature.md](./feature.md).

## Acceptance Criteria
- [ ] AC-01: Given the Bicep, when it is deployed to a dev resource group, then
      the five resources are created without errors.
- [ ] AC-02: Given the deploy workflow's required secrets/vars are set, when the
      Deploy workflow runs, then the API is published to App Service and the web
      client to the Static Web App.
- [ ] AC-03: Given the deployed app, then the web client reaches the API's
      `/health` and opens the SignalR hub connection (shows "Connected").
- [ ] AC-04: Given the deployed web build, then it points at the deployed API URL
      (not localhost), sourced from build-time configuration.

## Out of Scope
- Production hardening (VNet, private endpoints, diagnostics) - deferred.
- Wiring the app to Azure SignalR Service (the in-process hub is fine for now).
- Custom domains, multiple environments beyond dev.

## Technical Notes
- `az deployment group create -f infra/main.bicep -p infra/main.bicepparam`.
- Set the secrets/vars listed at the top of `.github/workflows/deploy.yml`
  (publish profile, app name, Static Web Apps token, the deployed URLs).
- The web build reads `VITE_API_BASE_URL` / `VITE_SIGNALR_HUB_URL` at build time.

## Dependencies
None (builds on the skeleton).
