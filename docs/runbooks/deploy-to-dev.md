<!--
  Runbook: deploy QuibbleStone to the dev environment (platform-devops story 02,
  issue #19). This is the owner-run procedure that turns the committed Bicep +
  Deploy workflow into a reachable cloud dev environment.

  It maps each step to an acceptance criterion (AC-01..AC-04). The live deploy
  needs an Azure subscription + GitHub repo secrets that only the project owner
  controls, so this file is the authoritative recipe rather than something CI runs
  on its own. Prose uses hyphens, colons, parentheses - never em dashes.
-->

# Runbook: Deploy to a dev environment

Stands up the tiny dev footprint (README section 9) and makes the app reachable in
the cloud. Two halves, run in order:

1. **Provision** the Azure resources from `infra/main.bicep` (AC-01).
2. **Deploy** the API and web client via the `Deploy` GitHub workflow (AC-02),
   built to point at the deployed API (AC-04), then smoke-checked (AC-03).

This is a one-time setup per resource group, then re-runnable on each release.

## Prerequisites

- An Azure subscription you can create resources in, and the Azure CLI
  (`az`, with the Bicep extension - `az bicep version` to confirm).
- `az login` completed (and `az account set --subscription <id>` if you have more
  than one).
- Repo admin rights on GitHub (to set Actions secrets and variables).

## Part 1 - Provision the resources (AC-01)

The Bicep stands up the five charter resources (Static Web App, App Service + Plan,
Azure SignalR Service, Storage, Key Vault). Names are derived from
`namePrefix` + `environmentName` + a deterministic `uniqueString(resourceGroup().id)`
suffix (see `infra/main.bicep`), so they are stable per resource group.

```bash
# Validate the template first (no Azure login needed):
az bicep build --file infra/main.bicep

# Create the dev resource group and deploy:
az group create -n quibblestone-dev-rg -l eastus2  # a Static-Web-Apps region (eastus does not host them)
az deployment group create \
  -g quibblestone-dev-rg \
  -f infra/main.bicep \
  -p infra/main.bicepparam
```

When it completes, capture the outputs - you need three of them for Part 2:

```bash
az deployment group show \
  -g quibblestone-dev-rg \
  -n main \
  --query properties.outputs
```

| Output | Used as | Why |
|---|---|---|
| `apiAppName` | `secrets.AZURE_API_APP_NAME` | which App Service to publish the API to |
| `apiDefaultHostName` | the base for `vars.API_BASE_URL` | the deployed API origin the web build targets |
| `webAppName` | (reference) | the deployed Static Web App |

`AC-01 met` when the deployment reports succeeded and the five resources exist in
`quibblestone-dev-rg`.

## Part 2 - Configure the Deploy workflow

`.github/workflows/deploy.yml` is manual (`workflow_dispatch`) and reads everything
sensitive from GitHub repo configuration - **nothing secret is committed, and no
secret goes into a `VITE_*` source file** (Vite bakes `VITE_*` vars into the public
bundle).

### Secrets (Settings -> Secrets and variables -> Actions -> Secrets)

| Secret | How to get it |
|---|---|
| `AZURE_API_APP_NAME` | the `apiAppName` output from Part 1. |
| `AZURE_API_PUBLISH_PROFILE` | the App Service publish profile XML: `az webapp deployment list-publishing-profiles -g quibblestone-dev-rg -n <apiAppName> --xml` - paste the whole XML document. |
| `AZURE_STATIC_WEB_APPS_API_TOKEN` | the Static Web App deployment token: `az staticwebapp secrets list -n <webAppName> --query properties.apiKey -o tsv` (or Portal -> Static Web App -> Manage deployment token). |

### Variables (Settings -> Secrets and variables -> Actions -> Variables)

| Variable | Value | Notes |
|---|---|---|
| `API_BASE_URL` | `https://<apiDefaultHostName>` | the deployed API origin, e.g. `https://quibblestone-dev-api-ab12cd.azurewebsites.net`. **Required** - the web job fails fast if it is missing. Include the scheme; a trailing slash is trimmed automatically. |
| `SIGNALR_HUB_URL` | (optional) | the full hub URL. Leave unset and the build derives `${API_BASE_URL}/hubs/game` (the hub route from `api/src/Program.cs`). Only set it if the hub ever moves off the API origin. |

These are **variables, not secrets**: they are public URLs that ship in the browser
bundle by design (that is what AC-04 requires).

## Part 3 - One required App Service setting for cross-origin (path to AC-03)

The deployed web client lives on the Static Web App origin and calls the API on a
different origin, so the API must allow that origin for both REST (`/health`) and
the SignalR hub. The API reads its allow-list from configuration
(`Cors:AllowedOrigins`, see `api/src/Program.cs`) and `AllowCredentials` is on for
the hub transport, so a wildcard will not work - it must be the exact web origin.

Set it once on the App Service (App Service config uses `__` for nested keys):

```bash
# webDefaultHostName is the webAppName resource's hostname; get it from:
#   az staticwebapp show -n <webAppName> --query defaultHostname -o tsv
az webapp config appsettings set \
  -g quibblestone-dev-rg \
  -n <apiAppName> \
  --settings Cors__AllowedOrigins__0="https://<webDefaultHostName>"
```

Without this, `/health` and the hub handshake are blocked by CORS and AC-03 fails
even though both services are up.

## Part 4 - Run the deploy (AC-02, AC-04)

Trigger the workflow: GitHub -> Actions -> **Deploy** -> Run workflow (or
`gh workflow run Deploy`).

What happens:

- **api job**: `dotnet publish` the single ASP.NET Core app, then
  `azure/webapps-deploy` pushes it to the App Service named by
  `AZURE_API_APP_NAME` using the publish profile (AC-02).
- **web job**: resolves the build URLs (fails fast if `API_BASE_URL` is unset),
  then `npm ci && npm run build` with `VITE_API_BASE_URL` / `VITE_SIGNALR_HUB_URL`
  set from those variables, so the bundle is baked to call the **deployed** API,
  not localhost (AC-04). `Azure/static-web-apps-deploy` then uploads `web/dist`
  with `skip_app_build: true` (we already built).

`AC-02 met` when both jobs are green. `AC-04 met` because the bundle was built with
the deployed URLs present (a build with them unset is rejected by the web job).

## Part 5 - Smoke check (AC-03)

1. **API health** - hit the deployed API directly:

   ```bash
   curl -s https://<apiDefaultHostName>/health
   # expect HTTP 200 and a small JSON body: { status, service, version, utc }
   ```

2. **Web reaches the hub** - open the Static Web App URL
   (`https://<webDefaultHostName>`) in a browser. The landing page connects the one
   SignalR connection (`web/src/signalr/useGameHub.ts`) and fires a Ping; the
   `ConnectionStatus` chip should read **"Connected"** (it shows "Disconnected" if
   the hub handshake fails - usually the CORS origin in Part 3, or a wrong
   `SIGNALR_HUB_URL`).

`AC-03 met` when `/health` returns 200 and the web client shows "Connected".

## Re-deploying

On later changes, just re-run the **Deploy** workflow - Parts 1-3 are one-time per
resource group. Re-run Part 1 only when the infrastructure itself changes (the
deployment is idempotent: unchanged resources are left as-is).

## Teardown

```bash
az group delete -n quibblestone-dev-rg --yes --no-wait
```

Deleting the group removes all five resources (Key Vault has soft-delete on, so the
vault name is reserved until it is purged or the retention window lapses).

## Deliberately deferred (do not gold-plate)

Production hardening (VNet, private endpoints, diagnostics), wiring the API to the
provisioned Azure SignalR Service (a later `.AddAzureSignalR(...)` one-liner - the
in-process hub is fine for dev), custom domains, and environments beyond dev. See
`infra/README.md`.
