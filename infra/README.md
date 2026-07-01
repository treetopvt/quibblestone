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

## Cost lever: `appServicePlanSku`

Everything above is on Free/near-zero SKUs except the App Service Plan, so that
plan is the only real cost. It is a parameter (`appServicePlanSku`) that
**defaults to `F1` (Free, $0)** - standing up UAT costs nothing. Scale up when
you want Always On + reliable WebSockets (a solid real-time demo):

| SKU | Tier | Always On | Cost | Use |
|---|---|---|---|---|
| `F1` (default) | Free | no | $0 | kick the tyres; app sleeps when idle |
| `B1` | Basic | yes | ~$13/mo | realistic floor for live multiplayer |
| `B2` / `S1` | Basic / Standard | yes | more | headroom, slots, autoscale |

`alwaysOn` and the `/health` check turn on automatically on paid tiers (they are
rejected on Free F1). WebSockets are enabled on every tier for the SignalR hub.

## Validate (no Azure needed)

```bash
az bicep build --file infra/main.bicep
```

## Provision UAT

Push-button (no local `az`): GitHub -> Actions -> **Provision UAT** -> Run
workflow (pick the `sku`). Or locally:

```bash
az group create -n quibblestone-uat-rg -l eastus
az deployment group create \
  -g quibblestone-uat-rg \
  -f infra/main.bicep \
  -p infra/main.uat.bicepparam
# scale up later: add -p appServicePlanSku=B1  (or re-run Provision UAT with sku=B1)
```

For the full end-to-end procedure (one-time OIDC bootstrap, provision,
auto-deploy on merge to main, and the `/health` + "Connected" smoke check), see
the runbook:
[`docs/runbooks/deploy-to-uat.md`](../docs/runbooks/deploy-to-uat.md). The
earlier dev-environment procedure is preserved at
[`docs/runbooks/deploy-to-dev.md`](../docs/runbooks/deploy-to-dev.md).

## Deliberately deferred (do not gold-plate yet)

- VNet / private endpoints / diagnostic settings.
- Wiring the API to **Azure SignalR Service** (the skeleton uses the in-process
  hub; production chains `.AddAzureSignalR(...)` with the connection string from
  Key Vault - see api/src/Program.cs).
- Granting the App Service managed identity an RBAC role on Key Vault.
- Environments beyond UAT, and a manual approval gate before UAT deploy (add a
  required reviewer on the `uat` GitHub Environment - no code change).

The Static Web App / App Service are now bound to the GitHub deploy workflow
(`.github/workflows/deploy.yml`, OIDC, auto on merge to main).
