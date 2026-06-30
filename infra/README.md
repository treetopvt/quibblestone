# infra - QuibbleStone infrastructure (Bicep)

The tiny footprint from README section 9. The bar is **"deploys cleanly to dev"** -
this is intentionally not gold-plated.

## Resources

| # | Resource | Type | Purpose |
|---|----------|------|---------|
| 1 | Static Web App | `Microsoft.Web/staticSites` | Hosts the React + Vite web client |
| 2 | App Service (+ Plan) | `Microsoft.Web/sites` (+ `serverfarms`) | Hosts the single ASP.NET Core app (API + SignalR hub) |
| 3 | Azure SignalR Service | `Microsoft.SignalRService/signalR` | Real-time backplane for production scale-out |
| 4 | Storage Account | `Microsoft.Storage/storageAccounts` | Table (templates, entitlements) + Blob (AI images, later) |
| 5 | Key Vault | `Microsoft.KeyVault/vaults` | Secrets (Stripe, AI provider keys) |

The App Service Plan is the sixth ARM resource but part of resource #2 (App
Service cannot run without a plan).

## Validate (no Azure needed)

```bash
az bicep build --file infra/main.bicep
```

## Deploy to a dev resource group

```bash
az group create -n quibblestone-dev-rg -l eastus
az deployment group create \
  -g quibblestone-dev-rg \
  -f infra/main.bicep \
  -p infra/main.bicepparam
```

For the full end-to-end procedure (provision, capture outputs, set the Deploy
workflow secrets/vars, the one cross-origin App Service setting, and the
`/health` + "Connected" smoke check), see the runbook:
[`docs/runbooks/deploy-to-dev.md`](../docs/runbooks/deploy-to-dev.md).

## Deliberately deferred (do not gold-plate yet)

- VNet / private endpoints / diagnostic settings.
- Wiring the API to **Azure SignalR Service** (the skeleton uses the in-process
  hub; production chains `.AddAzureSignalR(...)` with the connection string from
  Key Vault - see api/src/Program.cs).
- Granting the App Service managed identity an RBAC role on Key Vault.
- Binding the Static Web App / App Service to the GitHub deploy workflow.
